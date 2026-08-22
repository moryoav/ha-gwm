"""Diagnostics support for GWM."""

from __future__ import annotations

from typing import Any

from homeassistant.components.diagnostics import async_redact_data
from homeassistant.core import HomeAssistant

from . import GwmOraConfigEntry
from .const import CONF_TOKEN

TO_REDACT = {CONF_TOKEN, "token"}


async def async_get_config_entry_diagnostics(
    hass: HomeAssistant,
    entry: GwmOraConfigEntry,
) -> dict[str, Any]:
    """Return diagnostics for a config entry."""
    data = {
        "entry": {
            "data": dict(entry.data),
            "title": entry.title,
            "unique_id": entry.unique_id,
        },
        "vehicles": entry.runtime_data.coordinator.data,
    }
    return async_redact_data(data, TO_REDACT)
