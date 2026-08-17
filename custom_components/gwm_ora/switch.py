"""Switch platform for GWM ORA (AU/NZ charging control)."""

from __future__ import annotations

import time
from typing import Any

from homeassistant.components.switch import SwitchEntity
from homeassistant.core import HomeAssistant
from homeassistant.helpers.entity_platform import AddEntitiesCallback

from . import GwmOraConfigEntry
from .const import DEFAULT_CHARGE_WINDOW_HOURS
from .entity import GwmOraEntity, async_call_addon_api, setup_vehicle_entities

PARALLEL_UPDATES = 0


async def async_setup_entry(
    hass: HomeAssistant,
    entry: GwmOraConfigEntry,
    async_add_entities: AddEntitiesCallback,
) -> None:
    """Set up GWM ORA switches."""
    setup_vehicle_entities(
        entry,
        async_add_entities,
        lambda vehicle: (
            GwmOraChargingScheduleSwitch(
                entry.runtime_data.api, entry.runtime_data.coordinator, vehicle["vin"]
            ),
        ),
    )


class GwmOraChargingScheduleSwitch(GwmOraEntity, SwitchEntity):
    """Manual on/off for AU/NZ scheduled charging.

    On sets a charging window from now for DEFAULT_CHARGE_WINDOW_HOURS (the car
    charges only within it); off clears the plan (the car charges whenever it is
    plugged in). For precise windows, use the ``gwm_ora.set_charging_plan``
    service. Optimistic, because the vehicle does not report its charging plan
    in the polled status snapshot.
    """

    _attr_translation_key = "charging_schedule"
    _attr_assumed_state = True

    def __init__(self, api, coordinator, vin: str) -> None:
        super().__init__(coordinator, vin)
        self._api = api
        self._attr_unique_id = f"{vin}_charging_schedule"
        self._attr_is_on = False

    @property
    def available(self) -> bool:
        """Return whether charging control is enabled in the add-on."""
        return super().available and self.charging_control_available

    async def async_turn_on(self, **kwargs: Any) -> None:
        """Set a charging window from now for the default duration."""
        now_ms = int(time.time() * 1000)
        end_ms = now_ms + DEFAULT_CHARGE_WINDOW_HOURS * 3600 * 1000
        await async_call_addon_api(
            self._api.async_set_charging_plan(
                self.vin, enable=True, start_time=now_ms, end_time=end_ms, plan_type=0
            )
        )
        self._attr_is_on = True
        self.async_write_ha_state()

    async def async_turn_off(self, **kwargs: Any) -> None:
        """Clear the charging plan so the car charges whenever it is plugged in."""
        await async_call_addon_api(
            self._api.async_set_charging_plan(self.vin, enable=False)
        )
        self._attr_is_on = False
        self.async_write_ha_state()
