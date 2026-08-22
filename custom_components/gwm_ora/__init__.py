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

from .api import GwmOraApiAuthError, GwmOraApiClient, GwmOraApiError, GwmOraApiUnavailable
from .const import (
    ATTR_END_TIME,
    ATTR_START_TIME,
    ATTR_VIN,
    CONF_TOKEN,
    DEFAULT_NAME,
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

    api: GwmOraApiClient
    coordinator: GwmOraDataUpdateCoordinator


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
    if entry.title == LEGACY_DEFAULT_NAME:
        hass.config_entries.async_update_entry(entry, title=DEFAULT_NAME)

    session = async_get_clientsession(hass)
    api = GwmOraApiClient(
        session,
        entry.data[CONF_HOST],
        entry.data[CONF_PORT],
        entry.data[CONF_TOKEN],
    )
    coordinator = GwmOraDataUpdateCoordinator(hass, api)

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
    entry.runtime_data.coordinator.async_cancel_command_tasks()
    return await hass.config_entries.async_unload_platforms(entry, PLATFORMS)
