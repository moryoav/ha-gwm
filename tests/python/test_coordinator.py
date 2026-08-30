"""Coordinator VIN-resolution tests."""

from __future__ import annotations

import pytest

pytest.importorskip("homeassistant")

from homeassistant.core import HomeAssistant
from homeassistant.exceptions import ConfigEntryAuthFailed
from homeassistant.helpers.update_coordinator import UpdateFailed

from custom_components.gwm_ora.cloud_commands import DirectClimateCommandApi
from custom_components.gwm_ora.cloud_runtime import DirectReadOnlyCommandApi
from custom_components.gwm_ora.coordinator import GwmOraDataUpdateCoordinator
from gwm_ora_client import GwmAuthenticationError, GwmNetworkError


def _coordinator_with(vehicles: list[dict]) -> GwmOraDataUpdateCoordinator:
    # Bypass __init__ (needs a real hass/api); resolve_vehicle only reads .data.
    coordinator = GwmOraDataUpdateCoordinator.__new__(GwmOraDataUpdateCoordinator)
    coordinator.data = {"vehicles": vehicles}
    coordinator._charging_plan_active = {}
    return coordinator


def test_resolve_vehicle_matches_encoded_vin_or_display_serial() -> None:
    coordinator = _coordinator_with(
        [{"vin": "ENCODED123", "serial_number": "LGWTEST00XX000001"}]
    )

    # The encoded VIN the add-on keys on.
    assert coordinator.resolve_vehicle("ENCODED123")["serial_number"] == "LGWTEST00XX000001"
    # The display VIN / device serial the user sees and services.yaml documents.
    assert coordinator.resolve_vehicle("LGWTEST00XX000001")["vin"] == "ENCODED123"
    # Display VIN entry is case-insensitive.
    assert coordinator.resolve_vehicle("lgwtest00xx000001")["vin"] == "ENCODED123"
    # Unknown identifier.
    assert coordinator.resolve_vehicle("NOPE") is None


def test_vehicle_lookup_stays_strict_on_encoded_vin() -> None:
    coordinator = _coordinator_with(
        [{"vin": "ENCODED123", "serial_number": "LGWTEST00XX000001"}]
    )

    assert coordinator.vehicle("ENCODED123") is not None
    assert coordinator.vehicle("LGWTEST00XX000001") is None


def test_charging_plan_state_is_kept_per_vehicle() -> None:
    coordinator = _coordinator_with([])
    coordinator.async_update_listeners = lambda: None

    coordinator.set_charging_plan_active("VIN-A", True)
    coordinator.set_charging_plan_active("VIN-B", False)

    assert coordinator.charging_plan_active("VIN-A") is True
    assert coordinator.charging_plan_active("VIN-B") is False
    assert coordinator.charging_plan_active("VIN-C") is None


@pytest.mark.asyncio
async def test_direct_coordinator_uses_configured_account_interval() -> None:
    class DirectClient:
        async def async_get_vehicle_data(self) -> dict:
            return {"region": "eu", "vehicles": []}

    coordinator = GwmOraDataUpdateCoordinator(
        HomeAssistant("synthetic-config"),
        DirectReadOnlyCommandApi(),
        direct_client=DirectClient(),  # type: ignore[arg-type]
        update_interval_seconds=120,
    )

    assert await coordinator._async_update_data() == {"region": "eu", "vehicles": []}
    assert coordinator.update_interval.total_seconds() == 120


@pytest.mark.asyncio
async def test_direct_coordinator_runs_owned_charging_cleanup_after_each_refresh() -> None:
    calls: list[dict[str, object]] = []

    class DirectClient:
        async def async_get_vehicle_data(self) -> dict[str, object]:
            return {"region": "eu", "vehicles": []}

    api = object.__new__(DirectClimateCommandApi)

    async def cleanup(entry_data: dict[str, object]) -> None:
        calls.append(entry_data)

    api.async_cleanup_owned_charging_plans = cleanup  # type: ignore[method-assign]
    coordinator = GwmOraDataUpdateCoordinator(
        HomeAssistant("synthetic-config"),
        api,
        direct_client=DirectClient(),  # type: ignore[arg-type]
    )
    coordinator.config_entry = type("Entry", (), {"data": {"region": "eu"}})()

    assert await coordinator._async_update_data() == {"region": "eu", "vehicles": []}
    assert calls == [{"region": "eu"}]

    async def failed_cleanup(entry_data: dict[str, object]) -> None:
        del entry_data
        raise ValueError("synthetic storage failure")

    api.async_cleanup_owned_charging_plans = failed_cleanup  # type: ignore[method-assign]
    assert await coordinator._async_update_data() == {"region": "eu", "vehicles": []}


@pytest.mark.asyncio
@pytest.mark.parametrize(
    ("error", "expected"),
    [
        (GwmAuthenticationError(operation="acquire_vehicles"), ConfigEntryAuthFailed),
        (GwmNetworkError(operation="acquire_vehicles"), UpdateFailed),
    ],
)
async def test_direct_coordinator_classifies_refresh_failures(
    error: Exception,
    expected: type[Exception],
) -> None:
    class DirectClient:
        retired = False

        async def async_get_vehicle_data(self) -> dict:
            raise error

        async def async_authentication_rejected(self) -> None:
            self.retired = True

    direct_client = DirectClient()

    coordinator = GwmOraDataUpdateCoordinator(
        HomeAssistant("synthetic-config"),
        DirectReadOnlyCommandApi(),
        direct_client=direct_client,  # type: ignore[arg-type]
    )

    with pytest.raises(expected):
        await coordinator._async_update_data()
    assert direct_client.retired is isinstance(error, GwmAuthenticationError)
