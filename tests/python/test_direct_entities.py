"""Existing entity-platform behavior on direct normalized snapshots."""

from __future__ import annotations

from types import SimpleNamespace
from typing import Any

import pytest

pytest.importorskip("homeassistant")

from homeassistant.core import HomeAssistant

from custom_components.gwm_ora.button import (
    GwmOraChinaRemoteButton,
    GwmOraCloseWindowsButton,
)
from custom_components.gwm_ora.climate import GwmOraClimate
from custom_components.gwm_ora.cloud_runtime import DirectReadOnlyCommandApi
from custom_components.gwm_ora.coordinator import GwmOraDataUpdateCoordinator
from custom_components.gwm_ora.entity import setup_vehicle_entities
from custom_components.gwm_ora.lock import GwmOraDoorLock
from custom_components.gwm_ora.number import GwmOraClimateRunTimeNumber
from custom_components.gwm_ora.sensor import (
    SENSORS,
    GwmOraSensor,
    _sensor_descriptions_for_vehicle,
)
from custom_components.gwm_ora.switch import GwmOraChargingScheduleSwitch


def _vehicle(
    vin: str,
    soc: float,
    *,
    platform: str | None = None,
    charge_soc: float | None = None,
    climate_commands: bool = False,
    lock_window_commands: bool = False,
    china_vehicle_commands: bool = False,
    charging_control: bool = False,
) -> dict[str, Any]:
    return {
        "vin": vin,
        "platform": platform,
        "name": f"Vehicle {vin[-1]}",
        "manufacturer": "GWM",
        "model": "Synthetic",
        "serial_number": f"SERIAL-{vin[-1]}",
        "capabilities": {
            "remote_commands": False,
            "climate_commands": climate_commands,
            "lock_window_commands": lock_window_commands,
            "china_vehicle_commands": china_vehicle_commands,
            "charging_control": charging_control,
        },
        "values": {"soc": soc, "charge_soc": charge_soc},
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


@pytest.mark.asyncio
async def test_direct_coordinator_keeps_mixed_china_platform_entities_isolated() -> None:
    coordinator = GwmOraDataUpdateCoordinator(
        HomeAssistant("synthetic-config"),
        DirectReadOnlyCommandApi(),
        direct_client=SimpleNamespace(),  # type: ignore[arg-type]
    )
    coordinator.async_set_updated_data(
        {
            "region": "cn",
            "remote_commands_enabled": False,
            "charging_control_enabled": False,
            "vehicles": [
                _vehicle("SYNTHETIC-NAVINFO", 78, platform="navinfo"),
                _vehicle(
                    "SYNTHETIC-BEANTECH",
                    71,
                    platform="beantech",
                    charge_soc=82.5,
                ),
            ],
        }
    )
    added: list[GwmOraSensor] = []
    entry = SimpleNamespace(
        runtime_data=SimpleNamespace(coordinator=coordinator),
        async_on_unload=lambda callback: None,
    )

    setup_vehicle_entities(
        entry,  # type: ignore[arg-type]
        lambda entities: added.extend(entities),  # type: ignore[arg-type]
        lambda vehicle: (
            GwmOraSensor(coordinator, vehicle["vin"], description)
            for description in _sensor_descriptions_for_vehicle(
                vehicle,
                coordinator.region,
            )
        ),
    )

    by_vehicle = {
        vin: {entity.entity_description.key: entity for entity in added if entity.vin == vin}
        for vin in ("SYNTHETIC-NAVINFO", "SYNTHETIC-BEANTECH")
    }
    assert by_vehicle["SYNTHETIC-NAVINFO"]["soc"].native_value == 78
    assert "charge_soc" not in by_vehicle["SYNTHETIC-NAVINFO"]
    assert by_vehicle["SYNTHETIC-BEANTECH"]["soc"].native_value == 71
    assert by_vehicle["SYNTHETIC-BEANTECH"]["charge_soc"].native_value == 82.5
    assert all(not entity.remote_commands_available for entity in added)
    assert all(not entity.charging_control_available for entity in added)


@pytest.mark.asyncio
async def test_task17_capability_exposes_only_climate_and_keeps_beantech_hidden() -> None:
    coordinator = GwmOraDataUpdateCoordinator(
        HomeAssistant("synthetic-config"),
        DirectReadOnlyCommandApi(),
        direct_client=SimpleNamespace(),  # type: ignore[arg-type]
    )
    coordinator.async_set_updated_data(
        {
            "region": "eu",
            "vehicles": [_vehicle("SYNTHETIC-A", 80, climate_commands=True)],
        }
    )
    assert GwmOraClimate(DirectReadOnlyCommandApi(), coordinator, "SYNTHETIC-A").available
    assert GwmOraClimateRunTimeNumber(
        DirectReadOnlyCommandApi(), coordinator, "SYNTHETIC-A"
    ).available
    assert not GwmOraDoorLock(DirectReadOnlyCommandApi(), coordinator, "SYNTHETIC-A").available

    coordinator.async_set_updated_data(
        {
            "region": "cn",
            "vehicles": [
                _vehicle(
                    "SYNTHETIC-BEANTECH",
                    70,
                    platform="beantech",
                    climate_commands=True,
                )
            ],
        }
    )
    assert not GwmOraClimate(
        DirectReadOnlyCommandApi(), coordinator, "SYNTHETIC-BEANTECH"
    ).available
    assert not GwmOraClimateRunTimeNumber(
        DirectReadOnlyCommandApi(), coordinator, "SYNTHETIC-BEANTECH"
    ).available


@pytest.mark.asyncio
async def test_task18_capability_exposes_lock_window_without_task19_buttons() -> None:
    api = DirectReadOnlyCommandApi()
    coordinator = GwmOraDataUpdateCoordinator(
        HomeAssistant("synthetic-config"),
        api,
        direct_client=SimpleNamespace(),  # type: ignore[arg-type]
    )
    coordinator.async_set_updated_data(
        {
            "region": "eu",
            "vehicles": [
                _vehicle(
                    "SYNTHETIC-A",
                    80,
                    climate_commands=True,
                    lock_window_commands=True,
                )
            ],
        }
    )

    assert GwmOraDoorLock(api, coordinator, "SYNTHETIC-A").available
    assert GwmOraCloseWindowsButton(api, coordinator, "SYNTHETIC-A").available
    assert not GwmOraChinaRemoteButton(
        api,
        coordinator,
        "SYNTHETIC-A",
        "remote_start",
        "remote_start",
    ).available
    assert not GwmOraDoorLock(api, coordinator, "MISSING").available


@pytest.mark.asyncio
async def test_task19_china_buttons_are_capability_and_platform_filtered() -> None:
    api = DirectReadOnlyCommandApi()
    coordinator = GwmOraDataUpdateCoordinator(
        HomeAssistant("synthetic-config"),
        api,
        direct_client=SimpleNamespace(),  # type: ignore[arg-type]
    )
    coordinator.async_set_updated_data(
        {
            "region": "cn",
            "vehicles": [
                _vehicle(
                    "SYNTHETIC-NAVINFO",
                    80,
                    platform="navinfo",
                    china_vehicle_commands=True,
                ),
                _vehicle(
                    "SYNTHETIC-BEANTECH",
                    70,
                    platform="beantech",
                    china_vehicle_commands=True,
                ),
                _vehicle(
                    "SYNTHETIC-UNKNOWN",
                    60,
                    platform="future-platform",
                    china_vehicle_commands=True,
                ),
            ],
        }
    )

    assert GwmOraChinaRemoteButton(
        api,
        coordinator,
        "SYNTHETIC-NAVINFO",
        "tailgate_open",
        "open_tailgate",
    ).available
    assert GwmOraChinaRemoteButton(
        api,
        coordinator,
        "SYNTHETIC-BEANTECH",
        "remote_start",
        "remote_start",
    ).available
    assert not GwmOraChinaRemoteButton(
        api,
        coordinator,
        "SYNTHETIC-BEANTECH",
        "tailgate_open",
        "open_tailgate",
    ).available
    assert not GwmOraChinaRemoteButton(
        api,
        coordinator,
        "SYNTHETIC-UNKNOWN",
        "horn",
        "sound_horn",
    ).available


@pytest.mark.asyncio
async def test_task20_capability_exposes_existing_charging_switch_only_when_enabled() -> None:
    api = DirectReadOnlyCommandApi()
    coordinator = GwmOraDataUpdateCoordinator(
        HomeAssistant("synthetic-config"),
        api,
        direct_client=SimpleNamespace(),  # type: ignore[arg-type]
    )
    coordinator.async_set_updated_data(
        {
            "region": "eu",
            "charging_control_enabled": True,
            "vehicles": [
                _vehicle("SYNTHETIC-A", 80, charging_control=True),
                _vehicle("SYNTHETIC-B", 70),
            ],
        }
    )

    assert GwmOraChargingScheduleSwitch(api, coordinator, "SYNTHETIC-A").available
    assert not GwmOraChargingScheduleSwitch(api, coordinator, "SYNTHETIC-B").available
