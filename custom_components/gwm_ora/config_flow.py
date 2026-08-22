"""Config flow for GWM."""

from __future__ import annotations

from typing import Any

import voluptuous as vol
from homeassistant import config_entries
from homeassistant.config_entries import ConfigFlowResult
from homeassistant.const import CONF_HOST, CONF_PORT
from homeassistant.helpers.aiohttp_client import async_get_clientsession
from homeassistant.helpers.service_info.hassio import HassioServiceInfo

from .api import GwmOraApiAuthError, GwmOraApiClient, GwmOraApiError, GwmOraApiUnavailable
from .const import CONF_API_VERSION, CONF_SLUG, CONF_TOKEN, DEFAULT_NAME, DEFAULT_PORT, DOMAIN


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
        CONF_HOST: user_input[CONF_HOST],
        CONF_PORT: user_input[CONF_PORT],
        CONF_TOKEN: user_input[CONF_TOKEN],
        CONF_API_VERSION: 1,
        CONF_SLUG: slug,
    }


class GwmOraConfigFlow(config_entries.ConfigFlow, domain=DOMAIN):
    """Handle a GWM config flow."""

    VERSION = 1

    def __init__(self) -> None:
        self._discovered_data: dict[str, Any] | None = None

    async def async_step_hassio(
        self,
        discovery_info: HassioServiceInfo,
    ) -> ConfigFlowResult:
        """Handle Supervisor service discovery from the add-on."""
        config = dict(discovery_info.config)
        slug = config.get(CONF_SLUG) or discovery_info.slug or "gwm_ora"
        data = {
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
            errors["base"] = await self._async_validate(self._discovered_data)
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
        """Handle manual setup for development or non-Supervisor installs."""
        errors: dict[str, str] = {}

        if user_input is not None:
            data = _manual_data(user_input)
            errors["base"] = await self._async_validate(data)
            if not errors["base"]:
                await self.async_set_unique_id(f"{data[CONF_HOST]}:{data[CONF_PORT]}")
                self._abort_if_unique_id_configured(updates=data)
                return self.async_create_entry(title=DEFAULT_NAME, data=data)

        return self.async_show_form(
            step_id="user",
            data_schema=_manual_schema(),
            errors=errors,
        )

    async def async_step_reconfigure(
        self,
        user_input: dict[str, Any] | None = None,
    ) -> ConfigFlowResult:
        """Handle user-triggered reconfiguration."""
        entry = self._get_reconfigure_entry()
        errors: dict[str, str] = {}

        if user_input is not None:
            data = _manual_data(user_input, slug=entry.data.get(CONF_SLUG, "manual"))
            errors["base"] = await self._async_validate(data)
            if not errors["base"]:
                return self.async_update_reload_and_abort(entry, data_updates=data)

        return self.async_show_form(
            step_id="reconfigure",
            data_schema=_manual_schema(dict(entry.data)),
            errors=errors,
        )

    async def async_step_reauth(
        self,
        entry_data: dict[str, Any],
    ) -> ConfigFlowResult:
        """Handle an add-on API authentication failure."""
        return await self.async_step_reauth_confirm()

    async def async_step_reauth_confirm(
        self,
        user_input: dict[str, Any] | None = None,
    ) -> ConfigFlowResult:
        """Confirm updated add-on API connection details."""
        entry = self._get_reauth_entry()
        errors: dict[str, str] = {}

        if user_input is not None:
            data = _manual_data(user_input, slug=entry.data.get(CONF_SLUG, "manual"))
            errors["base"] = await self._async_validate(data)
            if not errors["base"]:
                return self.async_update_reload_and_abort(entry, data_updates=data)

        return self.async_show_form(
            step_id="reauth_confirm",
            data_schema=_manual_schema(dict(entry.data)),
            errors=errors,
        )

    async def _async_validate(self, data: dict[str, Any]) -> str:
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
