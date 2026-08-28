"""Existing entity-platform behavior on direct normalized snapshots."""

from __future__ import annotations

from types import SimpleNamespace
from typing import Any

import pytest

pytest.importorskip("homeassistant")

from homeassistant.core import HomeAssistant

from custom_components.gwm_ora.climate import GwmOraClimate
from custom_components.gwm_ora.cloud_runtime import DirectReadOnlyCommandApi
from custom_components.gwm_ora.coordinator import GwmOraDataUpdateCoordinator
from custom_components.gwm_ora.entity import setup_vehicle_entities
from custom_components.gwm_ora.sensor import SENSORS, GwmOraSensor


def _vehicle(vin: str, soc: float) -> dict[str, Any]:
    return {
        "vin": vin,
        "name": f"Vehicle {vin[-1]}",
        "manufacturer": "GWM",
        "model": "Synthetic",
        "serial_number": f"SERIAL-{vin[-1]}",
        "capabilities": {"remote_commands": False},
        "values": {"soc": soc},
        "timestamps": {},
        "climate": {},
        "raw_items": {},
    }


@pytest.mark.asyncio
async def test_existing_platform_adds_new_direct_vehicles_without_removing_old_entities() -> None:
    coordinator = GwmOraDataUpdateCoordinator(
        HomeAssistant("synthetic-config"),
        DirectReadOnlyCommandApi(),
        direct_client=SimpleNamespace(),  # type: ignore[arg-type]
    )
    coordinator.async_set_updated_data(
        {"region": "eu", "vehicles": [_vehicle("SYNTHETIC-A", 80)]}
    )
    added: list[GwmOraSensor] = []
    soc_description = next(description for description in SENSORS if description.key == "soc")
    entry = SimpleNamespace(
        runtime_data=SimpleNamespace(coordinator=coordinator),
        async_on_unload=lambda callback: None,
    )

    setup_vehicle_entities(
        entry,  # type: ignore[arg-type]
        lambda entities: added.extend(entities),  # type: ignore[arg-type]
        lambda vehicle: (
            GwmOraSensor(coordinator, vehicle["vin"], soc_description),
        ),
    )

    assert len(added) == 1
    first = added[0]
    assert first.native_value == 80
    assert first.available
    assert not GwmOraClimate(
        DirectReadOnlyCommandApi(),
        coordinator,
        "SYNTHETIC-A",
    ).available

    coordinator.async_set_updated_data(
        {
            "region": "eu",
            "vehicles": [
                _vehicle("SYNTHETIC-A", 79),
                _vehicle("SYNTHETIC-B", 55),
            ],
        }
    )
    assert len(added) == 2
    assert first.native_value == 79
    assert added[1].native_value == 55

    coordinator.async_set_updated_data(
        {"region": "eu", "vehicles": [_vehicle("SYNTHETIC-B", 54)]}
    )
    assert len(added) == 2
    assert not first.available
    assert added[1].available

    coordinator.last_update_success = False
    assert not added[1].available
