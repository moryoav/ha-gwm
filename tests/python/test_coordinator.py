"""Coordinator VIN-resolution tests."""

from __future__ import annotations

import pytest

pytest.importorskip("homeassistant")

from custom_components.gwm_ora.coordinator import GwmOraDataUpdateCoordinator


def _coordinator_with(vehicles: list[dict]) -> GwmOraDataUpdateCoordinator:
    # Bypass __init__ (needs a real hass/api); resolve_vehicle only reads .data.
    coordinator = GwmOraDataUpdateCoordinator.__new__(GwmOraDataUpdateCoordinator)
    coordinator.data = {"vehicles": vehicles}
    coordinator._charging_plan_active = {}
    return coordinator


def test_resolve_vehicle_matches_encoded_vin_or_display_serial() -> None:
    coordinator = _coordinator_with(
        [{"vin": "ENCODED123", "serial_number": "LGWEEUA57TR603334"}]
    )

    # The encoded VIN the add-on keys on.
    assert coordinator.resolve_vehicle("ENCODED123")["serial_number"] == "LGWEEUA57TR603334"
    # The display VIN / device serial the user sees and services.yaml documents.
    assert coordinator.resolve_vehicle("LGWEEUA57TR603334")["vin"] == "ENCODED123"
    # Display VIN entry is case-insensitive.
    assert coordinator.resolve_vehicle("lgweeua57tr603334")["vin"] == "ENCODED123"
    # Unknown identifier.
    assert coordinator.resolve_vehicle("NOPE") is None


def test_vehicle_lookup_stays_strict_on_encoded_vin() -> None:
    coordinator = _coordinator_with(
        [{"vin": "ENCODED123", "serial_number": "LGWEEUA57TR603334"}]
    )

    assert coordinator.vehicle("ENCODED123") is not None
    assert coordinator.vehicle("LGWEEUA57TR603334") is None


def test_charging_plan_state_is_kept_per_vehicle() -> None:
    coordinator = _coordinator_with([])
    coordinator.async_update_listeners = lambda: None

    coordinator.set_charging_plan_active("VIN-A", True)
    coordinator.set_charging_plan_active("VIN-B", False)

    assert coordinator.charging_plan_active("VIN-A") is True
    assert coordinator.charging_plan_active("VIN-B") is False
    assert coordinator.charging_plan_active("VIN-C") is None
