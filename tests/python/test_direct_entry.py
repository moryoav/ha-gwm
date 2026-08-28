"""Direct-entry lifecycle and diagnostics tests for the staged HA path."""

from __future__ import annotations

from types import MappingProxyType
from typing import Any

import pytest

pytest.importorskip("homeassistant")

from homeassistant.components.diagnostics import REDACTED
from homeassistant.config_entries import ConfigEntry

from custom_components.gwm_ora import async_setup_entry, async_unload_entry
from custom_components.gwm_ora.const import (
    CONF_ACCOUNT,
    CONF_CONNECTION_TYPE,
    CONF_PASSWORD,
    CONF_REGION,
    CONF_SECURITY_PIN,
    CONNECTION_TYPE_CLOUD,
    DOMAIN,
)
from custom_components.gwm_ora.diagnostics import async_get_config_entry_diagnostics


def _direct_entry(
    *,
    data_updates: dict[str, Any] | None = None,
    options: dict[str, Any] | None = None,
) -> ConfigEntry:
    data = {
        CONF_CONNECTION_TYPE: CONNECTION_TYPE_CLOUD,
        CONF_REGION: "eu",
        CONF_ACCOUNT: "private-account",
        CONF_PASSWORD: "private-password",
        **(data_updates or {}),
    }
    return ConfigEntry(
        data=data,
        discovery_keys=MappingProxyType({}),
        domain=DOMAIN,
        entry_id="synthetic-direct-entry",
        minor_version=1,
        options=options or {},
        source="user",
        subentries_data=None,
        title="GWM Europe",
        unique_id="cloud:eu:private-binding",
        version=1,
    )


@pytest.mark.asyncio
async def test_direct_entry_is_intentionally_inert_until_coordinator_task() -> None:
    entry = _direct_entry()

    assert await async_setup_entry(object(), entry) is True  # type: ignore[arg-type]
    assert await async_unload_entry(object(), entry) is True  # type: ignore[arg-type]


@pytest.mark.asyncio
async def test_direct_diagnostics_redact_current_and_future_account_state() -> None:
    secrets = {
        CONF_SECURITY_PIN: "private-pin",
        "access_token": "private-access-token",
        "g_refresh_token": "private-refresh-token",
        "auto_ai_user_id": "private-user-id",
        "device_id": "private-device-id",
        "certificate": "private-certificate",
        "private_key": "private-key",
        "serial_number": "private-serial",
        "vehicle_id": "private-vehicle-id",
        "vin": "private-vin",
        "location": "private-location",
    }
    entry = _direct_entry(data_updates=secrets, options={CONF_SECURITY_PIN: "private-pin"})

    result = await async_get_config_entry_diagnostics(object(), entry)  # type: ignore[arg-type]

    assert result["vehicles"] is None
    assert result["entry"]["data"][CONF_ACCOUNT] == REDACTED
    assert result["entry"]["data"][CONF_PASSWORD] == REDACTED
    assert result["entry"]["options"][CONF_SECURITY_PIN] == REDACTED
    assert result["entry"]["unique_id"] == REDACTED
    rendered = repr(result)
    assert "private-account" not in rendered
    assert "private-password" not in rendered
    assert all(value not in rendered for value in secrets.values())
