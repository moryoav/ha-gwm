"""Config, verification, reauthentication, reconfigure, and options flows."""

from __future__ import annotations

from typing import Any

import voluptuous as vol
from homeassistant import config_entries
from homeassistant.config_entries import ConfigEntry, ConfigFlowResult
from homeassistant.const import CONF_HOST, CONF_PORT
from homeassistant.helpers import selector
from homeassistant.helpers.aiohttp_client import async_get_clientsession
from homeassistant.helpers.service_info.hassio import HassioServiceInfo

from gwm_ora_client import (
    AnzAuthenticated,
    AnzSessionReclaimRequired,
    AnzVerificationRequired,
    ChinaAuthenticated,
    ChinaInitializationRequired,
    ChinaRiskControlRequired,
    ChinaVerificationRequired,
    EuAuthenticated,
    EuIdentityError,
    EuVerificationRequired,
    GwmApiError,
    GwmAuthenticationError,
    GwmClientError,
    GwmConfigurationError,
    GwmRateLimitError,
    GwmTransportError,
    RussiaAuthenticated,
    RussiaIdentityError,
    RussiaVerificationRequired,
)

from .api import GwmOraApiAuthError, GwmOraApiClient, GwmOraApiError, GwmOraApiUnavailable
from .cloud_auth import (
    CloudAuthenticationResult,
    CloudAuthState,
    DirectCloudAuthenticator,
    DirectCloudCredentials,
    direct_entry_data,
    direct_entry_title,
    direct_unique_id,
    generate_device_id,
)
from .cloud_runtime import (
    DirectCloudBootstrap,
    stage_direct_cloud_bootstrap,
)
from .const import (
    CONF_ACCOUNT,
    CONF_ALLOW_SESSION_RECLAIM,
    CONF_API_VERSION,
    CONF_CONNECTION_TYPE,
    CONF_COUNTRY,
    CONF_ENABLE_CHARGING_CONTROL,
    CONF_ENABLE_REMOTE_COMMANDS,
    CONF_LOG_LEVEL,
    CONF_PASSWORD,
    CONF_POLL_INTERVAL_SECONDS,
    CONF_REGION,
    CONF_SECURITY_PIN,
    CONF_SLUG,
    CONF_TOKEN,
    CONF_VERIFICATION_CODE,
    CONFIGURABLE_CLOUD_REGIONS,
    CONNECTION_TYPE_ADDON,
    CONNECTION_TYPE_CLOUD,
    DEFAULT_LOG_LEVEL,
    DEFAULT_NAME,
    DEFAULT_POLL_INTERVAL_SECONDS,
    DEFAULT_PORT,
    DOMAIN,
    LOG_LEVELS,
    MAX_POLL_INTERVAL_SECONDS,
    MIN_POLL_INTERVAL_SECONDS,
    REGION_ANZ,
    REGION_CHINA,
    REGION_EU,
    REGION_RUSSIA,
    SUPPORTED_CLOUD_REGIONS,
)

_REGION_OPTIONS = [
    {"value": REGION_EU, "label": "Europe"},
    {"value": REGION_ANZ, "label": "Australia / New Zealand"},
    {"value": REGION_RUSSIA, "label": "Russia"},
]
_DEFAULT_COUNTRIES = {
    REGION_EU: "DE",
    REGION_ANZ: "AU",
    REGION_RUSSIA: "RU",
    REGION_CHINA: "CN",
}
_AUTHENTICATED_TYPES = (
    EuAuthenticated,
    AnzAuthenticated,
    RussiaAuthenticated,
    ChinaAuthenticated,
)
_VERIFICATION_TYPES = (
    EuVerificationRequired,
    AnzVerificationRequired,
    RussiaVerificationRequired,
    ChinaVerificationRequired,
)


def _password_selector(*, autocomplete: str = "current-password") -> selector.TextSelector:
    return selector.TextSelector(
        selector.TextSelectorConfig(
            type=selector.TextSelectorType.PASSWORD,
            autocomplete=autocomplete,
        )
    )


def _manual_schema(defaults: dict[str, Any] | None = None) -> vol.Schema:
    """Return the manual add-on API schema."""

    defaults = defaults or {}
    return vol.Schema(
        {
            vol.Required(CONF_HOST, default=defaults.get(CONF_HOST, "")): str,
            vol.Required(CONF_PORT, default=defaults.get(CONF_PORT, DEFAULT_PORT)): int,
            vol.Required(CONF_TOKEN, default=defaults.get(CONF_TOKEN, "")): str,
        }
    )


def _manual_data(user_input: dict[str, Any], *, slug: str = "manual") -> dict[str, Any]:
    """Return normalized config entry data for a manually supplied add-on API."""

    return {
        CONF_CONNECTION_TYPE: CONNECTION_TYPE_ADDON,
        CONF_HOST: user_input[CONF_HOST],
        CONF_PORT: user_input[CONF_PORT],
        CONF_TOKEN: user_input[CONF_TOKEN],
        CONF_API_VERSION: 1,
        CONF_SLUG: slug,
    }


def _region_schema(default: str = REGION_EU) -> vol.Schema:
    return vol.Schema(
        {
            vol.Required(CONF_REGION, default=default): selector.SelectSelector(
                selector.SelectSelectorConfig(
                    options=_REGION_OPTIONS,
                    mode=selector.SelectSelectorMode.DROPDOWN,
                )
            )
        }
    )


def _account_schema(
    region: str,
    defaults: dict[str, Any] | None = None,
) -> vol.Schema:
    defaults = defaults or {}
    fields: dict[vol.Marker, object] = {}
    if region == REGION_EU:
        fields[vol.Required(CONF_COUNTRY, default=defaults.get(CONF_COUNTRY, "DE"))] = (
            selector.CountrySelector()
        )
    elif region == REGION_ANZ:
        fields[vol.Required(CONF_COUNTRY, default=defaults.get(CONF_COUNTRY, "AU"))] = (
            selector.CountrySelector(selector.CountrySelectorConfig(countries=["AU", "NZ"]))
        )

    fields[vol.Required(CONF_ACCOUNT, default=defaults.get(CONF_ACCOUNT, ""))] = str
    if region != REGION_CHINA:
        fields[vol.Required(CONF_PASSWORD)] = _password_selector()
    return vol.Schema(fields)


def _verification_schema() -> vol.Schema:
    return vol.Schema(
        {
            vol.Required(CONF_VERIFICATION_CODE): _password_selector(
                autocomplete="one-time-code"
            )
        }
    )


def _direct_options_schema(entry: ConfigEntry, defaults: dict[str, Any] | None = None) -> vol.Schema:
    current = {**entry.options, **(defaults or {})}
    region = str(entry.data.get(CONF_REGION, ""))
    fields: dict[vol.Marker, object] = {
        vol.Required(
            CONF_POLL_INTERVAL_SECONDS,
            default=current.get(CONF_POLL_INTERVAL_SECONDS, DEFAULT_POLL_INTERVAL_SECONDS),
        ): vol.All(
            vol.Coerce(int),
            vol.Range(min=MIN_POLL_INTERVAL_SECONDS, max=MAX_POLL_INTERVAL_SECONDS),
        ),
        vol.Required(
            CONF_ENABLE_REMOTE_COMMANDS,
            default=current.get(CONF_ENABLE_REMOTE_COMMANDS, False),
        ): bool,
        vol.Required(
            CONF_ENABLE_CHARGING_CONTROL,
            default=current.get(CONF_ENABLE_CHARGING_CONTROL, False),
        ): bool,
        vol.Required(
            CONF_LOG_LEVEL,
            default=current.get(CONF_LOG_LEVEL, DEFAULT_LOG_LEVEL),
        ): vol.In(LOG_LEVELS),
    }
    if region != REGION_CHINA:
        fields[
            vol.Optional(
                CONF_SECURITY_PIN,
                # Secrets are write-only in the options UI. A blank value can
                # preserve the existing PIN only while remote controls remain
                # enabled; see the submit handling below.
                default="",
            )
        ] = _password_selector(autocomplete="off")
    return vol.Schema(fields)


def _is_direct(data: dict[str, Any] | ConfigEntry | Any) -> bool:
    entry_data = data.data if isinstance(data, ConfigEntry) else data
    return entry_data.get(CONF_CONNECTION_TYPE) == CONNECTION_TYPE_CLOUD


class GwmOraConfigFlow(config_entries.ConfigFlow, domain=DOMAIN):
    """Handle add-on compatibility and native direct-cloud account flows."""

    VERSION = 1

    def __init__(self) -> None:
        self._discovered_data: dict[str, Any] | None = None
        self._cloud_authenticator: DirectCloudAuthenticator | None = None
        self._direct_mode = "user"
        self._direct_region: str | None = None
        self._direct_credentials: DirectCloudCredentials | None = None
        self._auth_state: CloudAuthState | None = None
        self._initialization_failures: tuple[str, ...] = ()

    @staticmethod
    def async_get_options_flow(config_entry: ConfigEntry) -> GwmOraOptionsFlow:
        """Return the options flow for either entry type."""

        return GwmOraOptionsFlow()

    async def async_step_hassio(
        self,
        discovery_info: HassioServiceInfo,
    ) -> ConfigFlowResult:
        """Handle Supervisor service discovery from the add-on."""

        config = dict(discovery_info.config)
        slug = config.get(CONF_SLUG) or discovery_info.slug or "gwm_ora"
        data = {
            CONF_CONNECTION_TYPE: CONNECTION_TYPE_ADDON,
            CONF_HOST: config[CONF_HOST],
            CONF_PORT: int(config.get(CONF_PORT, DEFAULT_PORT)),
            CONF_TOKEN: config[CONF_TOKEN],
            CONF_API_VERSION: int(config.get(CONF_API_VERSION, 1)),
            CONF_SLUG: slug,
        }

        await self.async_set_unique_id(slug)
        self._abort_if_unique_id_configured(updates=data)
        self._discovered_data = data
        return await self.async_step_hassio_confirm()

    async def async_step_hassio_confirm(
        self,
        user_input: dict[str, Any] | None = None,
    ) -> ConfigFlowResult:
        """Confirm add-on discovery."""

        assert self._discovered_data is not None
        errors: dict[str, str] = {}

        if user_input is not None:
            errors["base"] = await self._async_validate_addon(self._discovered_data)
            if not errors["base"]:
                return self.async_create_entry(title=DEFAULT_NAME, data=self._discovered_data)

        return self.async_show_form(
            step_id="hassio_confirm",
            errors=errors,
            description_placeholders={"host": self._discovered_data[CONF_HOST]},
        )

    async def async_step_user(
        self,
        user_input: dict[str, Any] | None = None,
    ) -> ConfigFlowResult:
        """Choose native direct-cloud setup or the compatibility add-on path."""

        if user_input is not None:
            return await self.async_step_addon(user_input)
        return self.async_show_menu(
            step_id="user",
            menu_options=["cloud", "addon"],
        )

    async def async_step_addon(
        self,
        user_input: dict[str, Any] | None = None,
    ) -> ConfigFlowResult:
        """Handle manual add-on setup for development and non-Supervisor installs."""

        errors: dict[str, str] = {}
        if user_input is not None:
            data = _manual_data(user_input)
            errors["base"] = await self._async_validate_addon(data)
            if not errors["base"]:
                await self.async_set_unique_id(f"{data[CONF_HOST]}:{data[CONF_PORT]}")
                self._abort_if_unique_id_configured(updates=data)
                return self.async_create_entry(title=DEFAULT_NAME, data=data)

        return self.async_show_form(
            step_id="addon",
            data_schema=_manual_schema(),
            errors=errors,
        )

    async def async_step_cloud(
        self,
        user_input: dict[str, Any] | None = None,
    ) -> ConfigFlowResult:
        """Select a direct cloud region whose activation gate has passed."""

        if user_input is None:
            self._direct_mode = "user"
            return self.async_show_form(step_id="cloud", data_schema=_region_schema())

        region = str(user_input.get(CONF_REGION, "")).strip().lower()
        if region == REGION_CHINA:
            return self.async_abort(reason="china_live_validation_required")
        if region not in CONFIGURABLE_CLOUD_REGIONS:
            return self.async_show_form(
                step_id="cloud",
                data_schema=_region_schema(),
                errors={CONF_REGION: "unsupported_region"},
            )
        self._direct_region = region
        self._auth_state = None
        return await self.async_step_account()

    async def async_step_account(
        self,
        user_input: dict[str, Any] | None = None,
    ) -> ConfigFlowResult:
        """Collect and validate the selected regional account."""

        if self._direct_region not in SUPPORTED_CLOUD_REGIONS:
            return self.async_abort(reason="invalid_flow_state")
        errors: dict[str, str] = {}
        if user_input is not None:
            try:
                self._direct_credentials = self._credentials_from_input(
                    self._direct_region,
                    user_input,
                )
                self._auth_state = None
            except (TypeError, ValueError):
                errors["base"] = "invalid_account"
            else:
                result, error = await self._async_authenticate()
                if error:
                    errors["base"] = error
                elif result is not None:
                    return await self._async_route_authentication(result)

        return self.async_show_form(
            step_id="account",
            data_schema=_account_schema(self._direct_region, user_input),
            errors=errors,
        )

    async def async_step_verification(
        self,
        user_input: dict[str, Any] | None = None,
    ) -> ConfigFlowResult:
        """Submit one non-persistent verification code continuation."""

        if self._direct_credentials is None or self._auth_state is None:
            return self.async_abort(reason="invalid_flow_state")
        errors: dict[str, str] = {}
        if user_input is not None:
            code = user_input.get(CONF_VERIFICATION_CODE)
            result, error = await self._async_authenticate(verification_code=code)
            if error:
                errors[CONF_VERIFICATION_CODE if error == "invalid_verification_code" else "base"] = error
            elif result is not None:
                return await self._async_route_authentication(result)
        return self._show_verification(errors)

    async def async_step_session_reclaim(
        self,
        user_input: dict[str, Any] | None = None,
    ) -> ConfigFlowResult:
        """Require explicit consent before an ANZ password login claims the session."""

        if self._direct_credentials is None or self._auth_state is None:
            return self.async_abort(reason="invalid_flow_state")
        errors: dict[str, str] = {}
        if user_input is not None:
            if user_input.get(CONF_ALLOW_SESSION_RECLAIM) is not True:
                errors[CONF_ALLOW_SESSION_RECLAIM] = "session_reclaim_not_confirmed"
            else:
                result, error = await self._async_authenticate(allow_session_reclaim=True)
                if error:
                    errors["base"] = error
                elif result is not None:
                    return await self._async_route_authentication(result)
        return self.async_show_form(
            step_id="session_reclaim",
            data_schema=vol.Schema({vol.Optional(CONF_ALLOW_SESSION_RECLAIM, default=False): bool}),
            errors=errors,
        )

    async def async_step_initialization(
        self,
        user_input: dict[str, Any] | None = None,
    ) -> ConfigFlowResult:
        """Retry a recoverable China downstream-service initialization."""

        if self._direct_credentials is None or self._auth_state is None:
            return self.async_abort(reason="invalid_flow_state")
        errors: dict[str, str] = {}
        if user_input is not None:
            result, error = await self._async_authenticate()
            if error:
                errors["base"] = error
            elif result is not None:
                if isinstance(result, ChinaInitializationRequired):
                    errors["base"] = "initialization_failed"
                return await self._async_route_authentication(result, errors=errors)
        return self._show_initialization(errors)

    async def async_step_reauth(self, entry_data: dict[str, Any]) -> ConfigFlowResult:
        """Handle either direct-cloud or add-on authentication failure."""

        if not _is_direct(entry_data):
            return await self.async_step_reauth_confirm()
        self._direct_mode = "reauth"
        self._direct_region = str(entry_data.get(CONF_REGION, ""))
        self._auth_state = None
        return await self.async_step_reauth_confirm()

    async def async_step_reauth_confirm(
        self,
        user_input: dict[str, Any] | None = None,
    ) -> ConfigFlowResult:
        """Reconnect the current direct account or add-on API."""

        entry = self._get_reauth_entry()
        if not _is_direct(entry):
            return await self._async_step_addon_reauth(entry, user_input)

        region = str(entry.data.get(CONF_REGION, ""))
        if region not in SUPPORTED_CLOUD_REGIONS:
            return self.async_abort(reason="invalid_flow_state")
        errors: dict[str, str] = {}
        if user_input is not None:
            try:
                password = None if region == REGION_CHINA else user_input.get(CONF_PASSWORD)
                self._direct_credentials = DirectCloudCredentials(
                    region=region,
                    country=str(entry.data.get(CONF_COUNTRY, _DEFAULT_COUNTRIES[region])),
                    account=str(entry.data.get(CONF_ACCOUNT, "")),
                    password=password,
                    device_id=generate_device_id(),
                )
                self._auth_state = None
            except (TypeError, ValueError):
                errors["base"] = "invalid_account"
            else:
                result, error = await self._async_authenticate()
                if error:
                    errors["base"] = error
                elif result is not None:
                    return await self._async_route_authentication(result)

        schema = (
            vol.Schema({})
            if region == REGION_CHINA
            else vol.Schema({vol.Required(CONF_PASSWORD): _password_selector()})
        )
        return self.async_show_form(
            step_id="reauth_confirm",
            data_schema=schema,
            errors=errors,
        )

    async def async_step_reconfigure(
        self,
        user_input: dict[str, Any] | None = None,
    ) -> ConfigFlowResult:
        """Reconfigure either the direct account or compatibility add-on path."""

        entry = self._get_reconfigure_entry()
        if not _is_direct(entry):
            return await self._async_step_addon_reconfigure(entry, user_input)

        self._direct_mode = "reconfigure"
        if user_input is None:
            return self.async_show_form(
                step_id="reconfigure",
                data_schema=_region_schema(str(entry.data.get(CONF_REGION, REGION_EU))),
            )
        region = str(user_input.get(CONF_REGION, "")).strip().lower()
        if region == REGION_CHINA:
            return self.async_abort(reason="china_live_validation_required")
        if region not in CONFIGURABLE_CLOUD_REGIONS:
            return self.async_show_form(
                step_id="reconfigure",
                data_schema=_region_schema(),
                errors={CONF_REGION: "unsupported_region"},
            )
        self._direct_region = region
        self._auth_state = None
        return await self.async_step_reconfigure_account()

    async def async_step_reconfigure_account(
        self,
        user_input: dict[str, Any] | None = None,
    ) -> ConfigFlowResult:
        """Authenticate replacement account settings before applying them."""

        entry = self._get_reconfigure_entry()
        if self._direct_region not in SUPPORTED_CLOUD_REGIONS:
            return self.async_abort(reason="invalid_flow_state")
        defaults = (
            dict(entry.data)
            if self._direct_region == entry.data.get(CONF_REGION)
            else {CONF_COUNTRY: _DEFAULT_COUNTRIES[self._direct_region]}
        )
        errors: dict[str, str] = {}
        if user_input is not None:
            try:
                self._direct_credentials = self._credentials_from_input(
                    self._direct_region,
                    user_input,
                )
                self._auth_state = None
            except (TypeError, ValueError):
                errors["base"] = "invalid_account"
            else:
                result, error = await self._async_authenticate()
                if error:
                    errors["base"] = error
                elif result is not None:
                    return await self._async_route_authentication(result)
        return self.async_show_form(
            step_id="reconfigure_account",
            data_schema=_account_schema(self._direct_region, {**defaults, **(user_input or {})}),
            errors=errors,
        )

    def _credentials_from_input(
        self,
        region: str,
        user_input: dict[str, Any],
    ) -> DirectCloudCredentials:
        return DirectCloudCredentials(
            region=region,
            country=str(user_input.get(CONF_COUNTRY, _DEFAULT_COUNTRIES[region])),
            account=user_input[CONF_ACCOUNT],
            password=None if region == REGION_CHINA else user_input.get(CONF_PASSWORD),
            device_id=generate_device_id(),
        )

    async def _async_authenticate(
        self,
        *,
        verification_code: object = None,
        allow_session_reclaim: bool = False,
    ) -> tuple[CloudAuthenticationResult | None, str]:
        credentials = self._direct_credentials
        if credentials is None:
            return None, "invalid_account"
        if self._cloud_authenticator is None:
            self._cloud_authenticator = DirectCloudAuthenticator()
        try:
            result = await self._cloud_authenticator.async_authenticate(
                credentials,
                state=self._auth_state,
                verification_code=(
                    verification_code if isinstance(verification_code, str) else None
                ),
                allow_session_reclaim=allow_session_reclaim,
            )
        except GwmRateLimitError:
            return None, "rate_limited"
        except GwmAuthenticationError:
            return None, (
                "invalid_verification_code" if verification_code is not None else "invalid_auth"
            )
        except GwmTransportError:
            return None, "cannot_connect"
        except (GwmConfigurationError, EuIdentityError, RussiaIdentityError):
            return None, "local_configuration_error"
        except GwmApiError:
            return None, "service_error"
        except GwmClientError:
            return None, "service_error"
        except (TypeError, ValueError):
            return None, "invalid_account"
        return result, ""

    async def _async_route_authentication(
        self,
        result: CloudAuthenticationResult,
        *,
        errors: dict[str, str] | None = None,
    ) -> ConfigFlowResult:
        if isinstance(result, _AUTHENTICATED_TYPES):
            return await self._async_finish_direct(result)
        if isinstance(result, _VERIFICATION_TYPES):
            self._auth_state = result.state
            verification_errors = dict(errors or {})
            if result.code_rejected:
                verification_errors[CONF_VERIFICATION_CODE] = "invalid_verification_code"
            return self._show_verification(verification_errors)
        if isinstance(result, AnzSessionReclaimRequired):
            self._auth_state = result.state
            return self.async_show_form(
                step_id="session_reclaim",
                data_schema=vol.Schema(
                    {vol.Optional(CONF_ALLOW_SESSION_RECLAIM, default=False): bool}
                ),
                errors=errors,
            )
        if isinstance(result, ChinaInitializationRequired):
            self._auth_state = result.state
            self._initialization_failures = result.failures
            return self._show_initialization(errors)
        if isinstance(result, ChinaRiskControlRequired):
            self._auth_state = None
            return self.async_abort(reason="risk_control_required")
        return self.async_abort(reason="invalid_flow_state")

    async def _async_finish_direct(
        self,
        result: CloudAuthenticationResult,
    ) -> ConfigFlowResult:
        credentials = self._direct_credentials
        if credentials is None:
            return self.async_abort(reason="invalid_flow_state")
        data = direct_entry_data(credentials)
        unique_id = direct_unique_id(credentials)
        title = direct_entry_title(credentials.region)
        try:
            bootstrap = DirectCloudBootstrap.from_authentication(credentials, result)
        except GwmConfigurationError:
            return self.async_abort(reason="china_live_validation_required")
        self._auth_state = None

        if self._direct_mode == "user":
            await self.async_set_unique_id(unique_id)
            self._abort_if_unique_id_configured()
            stage_direct_cloud_bootstrap(self.hass, unique_id, bootstrap)
            self._direct_credentials = None
            return self.async_create_entry(title=title, data=data)

        if self._direct_mode == "reauth":
            entry = self._get_reauth_entry()
            if entry.unique_id != unique_id:
                return self.async_abort(reason="invalid_flow_state")
            stage_direct_cloud_bootstrap(self.hass, unique_id, bootstrap)
            self._direct_credentials = None
            return self.async_update_reload_and_abort(
                entry,
                data={**entry.data, **data},
                title=title,
            )

        if self._direct_mode == "reconfigure":
            entry = self._get_reconfigure_entry()
            duplicate = self.hass.config_entries.async_entry_for_domain_unique_id(
                DOMAIN,
                unique_id,
            )
            if duplicate is not None and duplicate.entry_id != entry.entry_id:
                return self.async_abort(reason="already_configured")
            stage_direct_cloud_bootstrap(self.hass, unique_id, bootstrap)
            self._direct_credentials = None
            return self.async_update_reload_and_abort(
                entry,
                unique_id=unique_id,
                title=title,
                data=data,
            )
        return self.async_abort(reason="invalid_flow_state")

    def _show_verification(self, errors: dict[str, str] | None = None) -> ConfigFlowResult:
        return self.async_show_form(
            step_id="verification",
            data_schema=_verification_schema(),
            errors=errors,
        )

    def _show_initialization(self, errors: dict[str, str] | None = None) -> ConfigFlowResult:
        return self.async_show_form(
            step_id="initialization",
            data_schema=vol.Schema({}),
            errors=errors,
            description_placeholders={
                "failure_count": str(len(self._initialization_failures))
            },
        )

    async def _async_step_addon_reconfigure(
        self,
        entry: ConfigEntry,
        user_input: dict[str, Any] | None,
    ) -> ConfigFlowResult:
        errors: dict[str, str] = {}
        if user_input is not None:
            data = _manual_data(user_input, slug=entry.data.get(CONF_SLUG, "manual"))
            errors["base"] = await self._async_validate_addon(data)
            if not errors["base"]:
                return self.async_update_reload_and_abort(entry, data_updates=data)
        return self.async_show_form(
            step_id="reconfigure",
            data_schema=_manual_schema(dict(entry.data)),
            errors=errors,
        )

    async def _async_step_addon_reauth(
        self,
        entry: ConfigEntry,
        user_input: dict[str, Any] | None,
    ) -> ConfigFlowResult:
        errors: dict[str, str] = {}
        if user_input is not None:
            data = _manual_data(user_input, slug=entry.data.get(CONF_SLUG, "manual"))
            errors["base"] = await self._async_validate_addon(data)
            if not errors["base"]:
                return self.async_update_reload_and_abort(entry, data_updates=data)
        return self.async_show_form(
            step_id="reauth_confirm",
            data_schema=_manual_schema(dict(entry.data)),
            errors=errors,
        )

    async def _async_validate_addon(self, data: dict[str, Any]) -> str:
        """Validate add-on API access."""

        session = async_get_clientsession(self.hass)
        api = GwmOraApiClient(session, data[CONF_HOST], data[CONF_PORT], data[CONF_TOKEN])
        try:
            await api.async_health()
        except GwmOraApiAuthError:
            return "invalid_auth"
        except (GwmOraApiUnavailable, GwmOraApiError):
            return "cannot_connect"
        return ""

    # Retain the private helper name used by the existing compatibility tests.
    _async_validate = _async_validate_addon


class GwmOraOptionsFlow(config_entries.OptionsFlowWithReload):
    """Configure direct-cloud polling and future opt-in controls."""

    async def async_step_init(
        self,
        user_input: dict[str, Any] | None = None,
    ) -> ConfigFlowResult:
        entry = self.config_entry
        if not _is_direct(entry):
            return self.async_abort(reason="addon_options_managed")

        errors: dict[str, str] = {}
        if user_input is not None:
            normalized = dict(user_input)
            pin = normalized.get(CONF_SECURITY_PIN)
            normalized_pin = pin.strip() if isinstance(pin, str) else ""
            existing_pin = entry.options.get(CONF_SECURITY_PIN)
            preserved_pin = existing_pin.strip() if isinstance(existing_pin, str) else ""
            if (
                entry.data.get(CONF_REGION) != REGION_CHINA
                and normalized.get(CONF_ENABLE_REMOTE_COMMANDS) is True
                and not (normalized_pin or preserved_pin)
            ):
                errors[CONF_SECURITY_PIN] = "security_pin_required"
            else:
                if (
                    entry.data.get(CONF_REGION) == REGION_CHINA
                    or normalized.get(CONF_ENABLE_REMOTE_COMMANDS) is not True
                ):
                    normalized.pop(CONF_SECURITY_PIN, None)
                elif normalized_pin:
                    normalized[CONF_SECURITY_PIN] = normalized_pin
                else:
                    normalized[CONF_SECURITY_PIN] = preserved_pin
                return self.async_create_entry(data=normalized)

        return self.async_show_form(
            step_id="init",
            data_schema=_direct_options_schema(entry, user_input),
            errors=errors,
        )
