"""Diagnostics support for GWM."""

from __future__ import annotations

from typing import Any

from homeassistant.components.diagnostics import async_redact_data
from homeassistant.core import HomeAssistant

from . import GwmOraConfigEntry
from .const import (
    CONF_ACCOUNT,
    CONF_CONNECTION_TYPE,
    CONF_PASSWORD,
    CONF_SECURITY_PIN,
    CONF_TOKEN,
    CONNECTION_TYPE_CLOUD,
)

TO_REDACT = {
    CONF_ACCOUNT,
    CONF_PASSWORD,
    CONF_SECURITY_PIN,
    CONF_TOKEN,
    "access_token",
    "account_binding",
    "auto_ai_gw_id",
    "auto_ai_token_id",
    "auto_ai_user_id",
    "bean_id",
    "bean_tech_access_token",
    "bean_tech_bean_id",
    "bean_tech_refresh_token",
    "bean_tech_sso_token",
    "ca_bundle",
    "certificate",
    "certificate_data",
    "device_id",
    "email",
    "g_refresh_token",
    "g_token",
    "gw_id",
    "issued_identity",
    "latitude",
    "location",
    "longitude",
    "phone",
    "private_key",
    "pt_token",
    "refresh_token",
    "sso_token",
    "serial_number",
    "token",
    "unique_id",
    "user_id",
    "username",
    "vehicle_id",
    "verification_code",
    "vin",
    "transformed_private_key_data",
}


async def async_get_config_entry_diagnostics(
    hass: HomeAssistant,
    entry: GwmOraConfigEntry,
) -> dict[str, Any]:
    """Return diagnostics for a config entry."""
    vehicles = (
        None
        if entry.data.get(CONF_CONNECTION_TYPE) == CONNECTION_TYPE_CLOUD
        else entry.runtime_data.coordinator.data
    )
    data = {
        "entry": {
            "data": dict(entry.data),
            "options": dict(entry.options),
            "title": entry.title,
            "unique_id": entry.unique_id,
        },
        "vehicles": vehicles,
    }
    return async_redact_data(data, TO_REDACT)
