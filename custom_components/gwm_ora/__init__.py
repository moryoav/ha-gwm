"""GWM native integration."""

from __future__ import annotations

from dataclasses import dataclass
from datetime import timedelta

import voluptuous as vol
from homeassistant.config_entries import ConfigEntry
from homeassistant.const import CONF_HOST, CONF_PORT
from homeassistant.core import HomeAssistant, ServiceCall
from homeassistant.exceptions import (
    ConfigEntryAuthFailed,
    ConfigEntryNotReady,
    ServiceValidationError,
)
from homeassistant.helpers import config_validation as cv
from homeassistant.helpers import issue_registry as ir
from homeassistant.helpers.aiohttp_client import async_get_clientsession
from homeassistant.util import dt as dt_util

from gwm_ora_client import (
    AnzAuthenticated,
    EuAuthenticated,
    GwmAuthenticationError,
    GwmClientError,
    GwmConfigurationError,
    RussiaAuthenticated,
)

from .api import GwmOraApiAuthError, GwmOraApiClient, GwmOraApiError, GwmOraApiUnavailable
from .cloud_auth import DirectCloudAuthenticator
from .cloud_commands import DirectClimateCommandApi
from .cloud_runtime import (
    DirectCloudBootstrap,
    DirectCloudReadClient,
    DirectReadOnlyCommandApi,
    consume_direct_cloud_bootstrap,
    stage_direct_cloud_bootstrap,
)
from .cloud_storage import (
    DirectCloudStateStore,
    async_remove_direct_cloud_state,
    credentials_for_auth_state,
    direct_cloud_state_store,
)
from .const import (
    ATTR_END_TIME,
    ATTR_START_TIME,
    ATTR_VIN,
    CONF_CONNECTION_TYPE,
    CONF_ENABLE_REMOTE_COMMANDS,
    CONF_POLL_INTERVAL_SECONDS,
    CONF_SECURITY_PIN,
    CONF_TOKEN,
    CONNECTION_TYPE_CLOUD,
    DEFAULT_NAME,
    DEFAULT_POLL_INTERVAL_SECONDS,
    DOMAIN,
    LEGACY_DEFAULT_NAME,
    MIN_CHARGE_WINDOW_MINUTES,
    PLATFORMS,
    SERVICE_CLEAR_CHARGING_PLAN,
    SERVICE_SET_CHARGING_PLAN,
)
from .coordinator import GwmOraDataUpdateCoordinator
from .entity import async_call_addon_api


@dataclass(slots=True)
class GwmOraRuntimeData:
    """Runtime data for a GWM config entry."""

    api: GwmOraApiClient | DirectReadOnlyCommandApi | DirectClimateCommandApi
    coordinator: GwmOraDataUpdateCoordinator
    cloud: DirectCloudReadClient | None = None
    state_store: DirectCloudStateStore | None = None


GwmOraConfigEntry = ConfigEntry[GwmOraRuntimeData]


_SET_CHARGING_PLAN_SCHEMA = vol.Schema(
    {
        vol.Required(ATTR_VIN): cv.string,
        vol.Required(ATTR_START_TIME): cv.datetime,
        vol.Required(ATTR_END_TIME): cv.datetime,
    }
)
_CLEAR_CHARGING_PLAN_SCHEMA = vol.Schema({vol.Required(ATTR_VIN): cv.string})


def _charging_window_epoch_ms(start, end) -> tuple[int, int]:
    """Validate a charging window and return UTC Unix milliseconds."""
    start_utc = dt_util.as_utc(start)
    end_utc = dt_util.as_utc(end)
    if end_utc - start_utc < timedelta(minutes=MIN_CHARGE_WINDOW_MINUTES):
        raise ServiceValidationError(
            f"Charging plan window must be at least {MIN_CHARGE_WINDOW_MINUTES} minutes"
        )

    return (
        int(dt_util.as_timestamp(start_utc) * 1000),
        int(dt_util.as_timestamp(end_utc) * 1000),
    )


def _async_register_services(hass: HomeAssistant) -> None:
    """Register the charging-plan services (once)."""
    if hass.services.has_service(DOMAIN, SERVICE_SET_CHARGING_PLAN):
        return

    def _resolve_for_vin(
        vin: str,
    ) -> tuple[GwmOraApiClient, GwmOraDataUpdateCoordinator, str]:
        """Resolve a user-supplied VIN to its (api, internal VIN).

        Accepts either the display VIN (device serial) or the encoded VIN and
        returns the encoded ``vin`` the add-on API expects.
        """
        identifier = vin.strip()
        for entry in hass.config_entries.async_loaded_entries(DOMAIN):
            if entry.data.get(CONF_CONNECTION_TYPE) == CONNECTION_TYPE_CLOUD:
                continue
            vehicle = entry.runtime_data.coordinator.resolve_vehicle(identifier)
            if vehicle is not None:
                return (
                    entry.runtime_data.api,
                    entry.runtime_data.coordinator,
                    vehicle["vin"],
                )
        raise ServiceValidationError(f"No GWM vehicle found with VIN {identifier}")

    async def _set_charging_plan(call: ServiceCall) -> None:
        api, coordinator, resolved_vin = _resolve_for_vin(call.data[ATTR_VIN])
        start_ms, end_ms = _charging_window_epoch_ms(
            call.data[ATTR_START_TIME], call.data[ATTR_END_TIME]
        )
        await async_call_addon_api(
            api.async_set_charging_plan(
                resolved_vin,
                enable=True,
                start_time=start_ms,
                end_time=end_ms,
                plan_type=0,
            ),
            forbidden_translation_key="charging_control_unavailable",
        )
        coordinator.set_charging_plan_active(resolved_vin, True)

    async def _clear_charging_plan(call: ServiceCall) -> None:
        api, coordinator, resolved_vin = _resolve_for_vin(call.data[ATTR_VIN])
        await async_call_addon_api(
            api.async_set_charging_plan(resolved_vin, enable=False),
            forbidden_translation_key="charging_control_unavailable",
        )
        coordinator.set_charging_plan_active(resolved_vin, False)

    hass.services.async_register(
        DOMAIN, SERVICE_SET_CHARGING_PLAN, _set_charging_plan, schema=_SET_CHARGING_PLAN_SCHEMA
    )
    hass.services.async_register(
        DOMAIN, SERVICE_CLEAR_CHARGING_PLAN, _clear_charging_plan, schema=_CLEAR_CHARGING_PLAN_SCHEMA
    )


async def async_setup_entry(hass: HomeAssistant, entry: GwmOraConfigEntry) -> bool:
    """Set up GWM from a config entry."""
    if entry.data.get(CONF_CONNECTION_TYPE) == CONNECTION_TYPE_CLOUD:
        return await _async_setup_direct_entry(hass, entry)

    if entry.title == LEGACY_DEFAULT_NAME:
        hass.config_entries.async_update_entry(entry, title=DEFAULT_NAME)

    session = async_get_clientsession(hass)
    api = GwmOraApiClient(
        session,
        entry.data[CONF_HOST],
        entry.data[CONF_PORT],
        entry.data[CONF_TOKEN],
    )
    coordinator = GwmOraDataUpdateCoordinator(hass, api, config_entry=entry)

    try:
        await coordinator.async_config_entry_first_refresh()
    except (ConfigEntryAuthFailed, GwmOraApiAuthError):
        ir.async_create_issue(
            hass,
            DOMAIN,
            "addon_auth_failed",
            is_fixable=False,
            severity=ir.IssueSeverity.ERROR,
            translation_key="addon_auth_failed",
        )
        raise
    except (GwmOraApiUnavailable, GwmOraApiError) as err:
        raise ConfigEntryNotReady(str(err)) from err

    ir.async_delete_issue(hass, DOMAIN, "addon_auth_failed")
    entry.runtime_data = GwmOraRuntimeData(api=api, coordinator=coordinator)
    _async_register_services(hass)
    await hass.config_entries.async_forward_entry_setups(entry, PLATFORMS)
    return True


async def async_unload_entry(hass: HomeAssistant, entry: GwmOraConfigEntry) -> bool:
    """Unload a config entry."""
    if entry.data.get(CONF_CONNECTION_TYPE) == CONNECTION_TYPE_CLOUD:
        unloaded = await hass.config_entries.async_unload_platforms(entry, PLATFORMS)
        if not unloaded:
            return False
        entry.runtime_data.coordinator.async_cancel_command_tasks()
        cloud = entry.runtime_data.cloud
        if cloud is not None:
            if (
                (bootstrap := cloud.reusable_bootstrap) is not None
                and entry.unique_id is not None
            ):
                stage_direct_cloud_bootstrap(hass, entry.unique_id, bootstrap)
            await cloud.aclose()
        return True
    entry.runtime_data.coordinator.async_cancel_command_tasks()
    return await hass.config_entries.async_unload_platforms(entry, PLATFORMS)


async def _async_setup_direct_entry(
    hass: HomeAssistant,
    entry: GwmOraConfigEntry,
) -> bool:
    """Set up one restart-safe direct read runtime."""

    if entry.unique_id is None:
        raise ConfigEntryAuthFailed("Direct GWM cloud account identity is missing")
    try:
        state_store = direct_cloud_state_store(hass, entry.unique_id)
    except (TypeError, ValueError) as err:
        raise ConfigEntryAuthFailed(
            "Direct GWM cloud account identity is invalid"
        ) from err
    bootstrap = await _async_load_direct_bootstrap(hass, entry, state_store)

    try:
        command_enabled = entry.options.get(CONF_ENABLE_REMOTE_COMMANDS) is True
        security_pin = entry.options.get(CONF_SECURITY_PIN)
        climate_enabled = command_enabled and isinstance(security_pin, str) and bool(security_pin.strip())
        cloud = DirectCloudReadClient.from_entry_data(
            dict(entry.data),
            entry.unique_id,
            bootstrap,
            state_store=state_store,
            climate_commands_enabled=climate_enabled,
        )
    except (GwmConfigurationError, TypeError, ValueError) as err:
        raise ConfigEntryAuthFailed(
            "Direct GWM cloud authentication handoff is invalid"
        ) from err

    try:
        credentials = credentials_for_auth_state(dict(entry.data), bootstrap.state)
        api = DirectClimateCommandApi(
            cloud,
            state_store,
            credentials,
            enabled=command_enabled,
            security_pin=security_pin if isinstance(security_pin, str) else None,
        )
    except (TypeError, ValueError) as err:
        await cloud.aclose()
        raise ConfigEntryAuthFailed("Direct GWM command context is invalid") from err
    coordinator = GwmOraDataUpdateCoordinator(
        hass,
        api,
        config_entry=entry,
        direct_client=cloud,
        update_interval_seconds=int(
            entry.options.get(
                CONF_POLL_INTERVAL_SECONDS,
                DEFAULT_POLL_INTERVAL_SECONDS,
            )
        ),
    )
    try:
        await coordinator.async_config_entry_first_refresh()
        for command in await api.async_restore(dict(entry.data)):
            coordinator.async_track_command(command)
        entry.runtime_data = GwmOraRuntimeData(
            api=api,
            coordinator=coordinator,
            cloud=cloud,
            state_store=state_store,
        )
        _async_register_services(hass)
        await hass.config_entries.async_forward_entry_setups(entry, PLATFORMS)
    except BaseException:
        if (
            (reusable := cloud.reusable_bootstrap) is not None
            and entry.unique_id is not None
        ):
            stage_direct_cloud_bootstrap(hass, entry.unique_id, reusable)
        await cloud.aclose()
        raise
    return True


async def _async_load_direct_bootstrap(
    hass: HomeAssistant,
    entry: GwmOraConfigEntry,
    state_store: DirectCloudStateStore,
) -> DirectCloudBootstrap:
    """Load or resume one account-bound session without a fresh login."""

    bootstrap = consume_direct_cloud_bootstrap(hass, entry.unique_id)
    if bootstrap is not None:
        try:
            credentials = credentials_for_auth_state(dict(entry.data), bootstrap.state)
            await state_store.async_save_auth_state(credentials, bootstrap.state)
        except (TypeError, ValueError) as err:
            raise ConfigEntryAuthFailed(
                "Direct GWM cloud authentication handoff is invalid"
            ) from err
        return bootstrap

    try:
        auth_state = await state_store.async_load_auth_state(dict(entry.data))
        if auth_state is None:
            raise ConfigEntryAuthFailed(
                "Direct GWM cloud authentication must be renewed"
            )
        credentials = credentials_for_auth_state(dict(entry.data), auth_state)
    except ConfigEntryAuthFailed:
        raise
    except (TypeError, ValueError) as err:
        raise ConfigEntryAuthFailed(
            "Direct GWM cloud authentication state is invalid"
        ) from err
    except Exception as err:
        raise ConfigEntryNotReady(
            "Direct GWM cloud private state is temporarily unavailable"
        ) from err

    try:
        result = await DirectCloudAuthenticator().async_authenticate(
            credentials,
            state=auth_state,
            allow_session_reclaim=False,
            allow_password_login=False,
        )
        if not isinstance(
            result,
            (EuAuthenticated, AnzAuthenticated, RussiaAuthenticated),
        ):
            await state_store.async_clear_auth_state(dict(entry.data))
            raise ConfigEntryAuthFailed(
                "Direct GWM cloud authentication requires user confirmation"
            )
        bootstrap = DirectCloudBootstrap.from_authentication(credentials, result)
        await state_store.async_save_auth_state(credentials, result.state)
        return bootstrap
    except ConfigEntryAuthFailed:
        raise
    except GwmAuthenticationError as err:
        await state_store.async_clear_auth_state(dict(entry.data))
        raise ConfigEntryAuthFailed(
            "Direct GWM cloud authentication was rejected"
        ) from err
    except GwmClientError as err:
        raise ConfigEntryNotReady(
            f"Direct GWM cloud {err.category} during {err.operation}"
        ) from err
    except (TypeError, ValueError) as err:
        await state_store.async_clear_auth_state(dict(entry.data))
        raise ConfigEntryAuthFailed(
            "Direct GWM cloud authentication state is invalid"
        ) from err


async def async_remove_entry(
    hass: HomeAssistant,
    entry: GwmOraConfigEntry,
) -> None:
    """Remove private durable state with a deleted direct config entry."""

    if entry.data.get(CONF_CONNECTION_TYPE) == CONNECTION_TYPE_CLOUD:
        await async_remove_direct_cloud_state(hass, entry.unique_id)
