"""Direct-entry lifecycle and diagnostics tests for the staged HA path."""

from __future__ import annotations

import ssl
from types import MappingProxyType, SimpleNamespace
from typing import Any

import pytest

pytest.importorskip("homeassistant")

from homeassistant.components.diagnostics import REDACTED
from homeassistant.config_entries import ConfigEntry
from homeassistant.core import HomeAssistant
from homeassistant.exceptions import ConfigEntryAuthFailed, ConfigEntryNotReady

from custom_components import gwm_ora
from custom_components.gwm_ora import async_setup_entry, async_unload_entry
from custom_components.gwm_ora.cloud_runtime import (
    DirectCloudBootstrap,
    consume_direct_cloud_bootstrap,
    stage_direct_cloud_bootstrap,
)
from custom_components.gwm_ora.const import (
    CONF_ACCOUNT,
    CONF_CONNECTION_TYPE,
    CONF_PASSWORD,
    CONF_POLL_INTERVAL_SECONDS,
    CONF_REGION,
    CONF_SECURITY_PIN,
    CONNECTION_TYPE_CLOUD,
    DOMAIN,
)
from custom_components.gwm_ora.diagnostics import async_get_config_entry_diagnostics
from gwm_ora_client import GwmSession


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
async def test_direct_entry_without_memory_handoff_requests_reauthentication() -> None:
    entry = _direct_entry()
    hass = HomeAssistant("synthetic-config")

    with pytest.raises(ConfigEntryAuthFailed):
        await async_setup_entry(hass, entry)


@pytest.mark.asyncio
async def test_direct_entry_setup_and_unload_own_runtime_lifecycle(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    entry = _direct_entry(options={CONF_POLL_INTERVAL_SECONDS: 180})
    hass = HomeAssistant("synthetic-config")
    bootstrap = DirectCloudBootstrap(
        region="eu",
        account_binding="a" * 64,
        session=GwmSession(
            "DE",
            "0123456789abcdef0123456789abcdef",
            "synthetic-access-token",
            ssl.create_default_context(),
        ),
    )
    stage_direct_cloud_bootstrap(hass, entry.unique_id or "", bootstrap)
    forwarded: list[tuple[str, ...]] = []
    unloaded: list[tuple[str, ...]] = []

    class Cloud:
        reusable_bootstrap = bootstrap

        def __init__(self) -> None:
            self.closed = False

        async def aclose(self) -> None:
            self.closed = True

    cloud = Cloud()

    class Coordinator:
        def __init__(self, *args: Any, **kwargs: Any) -> None:
            assert kwargs["update_interval_seconds"] == 180
            assert kwargs["config_entry"] is entry
            assert kwargs["direct_client"] is cloud
            self.data = {"region": "eu", "vehicles": []}
            self.cancelled = False

        async def async_config_entry_first_refresh(self) -> None:
            return None

        def async_cancel_command_tasks(self) -> None:
            self.cancelled = True

    monkeypatch.setattr(
        gwm_ora.DirectCloudReadClient,
        "from_entry_data",
        classmethod(lambda cls, *args: cloud),
    )
    monkeypatch.setattr(gwm_ora, "GwmOraDataUpdateCoordinator", Coordinator)
    monkeypatch.setattr(gwm_ora, "_async_register_services", lambda hass: None)

    class ConfigEntries:
        async def async_forward_entry_setups(
            self,
            target: ConfigEntry,
            platforms: list[Any],
        ) -> None:
            assert target is entry
            forwarded.append(tuple(str(platform) for platform in platforms))

        async def async_unload_platforms(
            self,
            target: ConfigEntry,
            platforms: list[Any],
        ) -> bool:
            assert target is entry
            unloaded.append(tuple(str(platform) for platform in platforms))
            return True

    hass.config_entries = ConfigEntries()  # type: ignore[assignment]

    assert await async_setup_entry(hass, entry) is True
    assert entry.runtime_data.cloud is cloud
    assert forwarded
    assert await async_unload_entry(hass, entry) is True
    assert unloaded
    assert cloud.closed
    assert entry.runtime_data.coordinator.cancelled
    assert consume_direct_cloud_bootstrap(hass, entry.unique_id) is bootstrap


@pytest.mark.asyncio
async def test_transient_first_refresh_failure_restages_handoff_for_retry(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    entry = _direct_entry()
    hass = HomeAssistant("synthetic-config")
    bootstrap = DirectCloudBootstrap(
        region="eu",
        account_binding="b" * 64,
        session=GwmSession(
            "DE",
            "0123456789abcdef0123456789abcdef",
            "synthetic-access-token",
            ssl.create_default_context(),
        ),
    )
    stage_direct_cloud_bootstrap(hass, entry.unique_id or "", bootstrap)

    class Cloud:
        reusable_bootstrap = bootstrap

        def __init__(self) -> None:
            self.closed = False

        async def aclose(self) -> None:
            self.closed = True

    cloud = Cloud()

    class Coordinator:
        def __init__(self, *args: Any, **kwargs: Any) -> None:
            return None

        async def async_config_entry_first_refresh(self) -> None:
            raise ConfigEntryNotReady("synthetic transient failure")

    monkeypatch.setattr(
        gwm_ora.DirectCloudReadClient,
        "from_entry_data",
        classmethod(lambda cls, *args: cloud),
    )
    monkeypatch.setattr(gwm_ora, "GwmOraDataUpdateCoordinator", Coordinator)

    with pytest.raises(ConfigEntryNotReady):
        await async_setup_entry(hass, entry)

    assert cloud.closed
    assert consume_direct_cloud_bootstrap(hass, entry.unique_id) is bootstrap


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
    entry.runtime_data = SimpleNamespace(
        coordinator=SimpleNamespace(data={"vehicles": [secrets]})
    )

    result = await async_get_config_entry_diagnostics(object(), entry)  # type: ignore[arg-type]

    assert result["vehicles"]["vehicles"][0]["vin"] == REDACTED
    assert result["entry"]["data"][CONF_ACCOUNT] == REDACTED
    assert result["entry"]["data"][CONF_PASSWORD] == REDACTED
    assert result["entry"]["options"][CONF_SECURITY_PIN] == REDACTED
    assert result["entry"]["unique_id"] == REDACTED
    rendered = repr(result)
    assert "private-account" not in rendered
    assert "private-password" not in rendered
    assert all(value not in rendered for value in secrets.values())
