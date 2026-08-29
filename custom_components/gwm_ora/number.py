"""Number platform for GWM."""

from __future__ import annotations

from typing import Any

from homeassistant.components.number import (
    NumberDeviceClass,
    NumberEntity,
    NumberMode,
    RestoreNumber,
)
from homeassistant.const import UnitOfTime
from homeassistant.core import HomeAssistant
from homeassistant.exceptions import HomeAssistantError
from homeassistant.helpers.entity import EntityCategory
from homeassistant.helpers.entity_platform import AddEntitiesCallback

from . import GwmOraConfigEntry
from .const import DOMAIN
from .entity import GwmOraEntity, async_call_addon_api, setup_vehicle_entities

PARALLEL_UPDATES = 0

# Used until the slider has been touched once. Matches the addon's fallback.
DEFAULT_REMOTE_START_RUN_TIME = 15


async def async_setup_entry(
    hass: HomeAssistant,
    entry: GwmOraConfigEntry,
    async_add_entities: AddEntitiesCallback,
) -> None:
    """Set up GWM number entities."""
    setup_vehicle_entities(
        entry,
        async_add_entities,
        lambda vehicle: (
            GwmOraClimateRunTimeNumber(
                entry.runtime_data.api,
                entry.runtime_data.coordinator,
                vehicle["vin"],
            ),
        )
        + (
            (
                GwmOraRemoteStartRunTimeNumber(
                    entry.runtime_data.api,
                    entry.runtime_data.coordinator,
                    vehicle["vin"],
                ),
            )
            if entry.runtime_data.coordinator.region == "cn"
            and str(vehicle.get("platform") or "").lower() == "beantech"
            else ()
        ),
    )


class GwmOraClimateRunTimeNumber(GwmOraEntity, NumberEntity):
    """GWM climate run-time setting."""

    _attr_translation_key = "climate_run_time"
    _attr_device_class = NumberDeviceClass.DURATION
    _attr_entity_category = EntityCategory.CONFIG
    _attr_mode = NumberMode.SLIDER
    _attr_native_min_value = 5
    _attr_native_max_value = 30
    _attr_native_step = 1
    _attr_native_unit_of_measurement = UnitOfTime.MINUTES

    def __init__(self, api, coordinator, vin: str) -> None:
        super().__init__(coordinator, vin)
        self._api = api
        self._attr_unique_id = f"{vin}_climate_run_time"

    @property
    def available(self) -> bool:
        """Return whether the climate run-time setting is available."""
        return (
            super().available
            and self.remote_commands_available
        )

    @property
    def native_value(self) -> float | None:
        """Return the saved climate run time in minutes."""
        vehicle = self.vehicle or {}
        value: Any = (vehicle.get("climate") or {}).get("operation_time_minutes")
        return float(value) if value is not None else None

    async def async_set_native_value(self, value: float) -> None:
        """Save the climate run time used by the next A/C command."""
        if not float(value).is_integer() or value < 5 or value > 30:
            raise HomeAssistantError(
                translation_domain=DOMAIN,
                translation_key="invalid_climate_run_time",
            )

        command = await async_call_addon_api(
            self._api.async_set_climate(self.vin, operation_time_minutes=int(value))
        )
        self.coordinator.async_track_command(command)


class GwmOraRemoteStartRunTimeNumber(GwmOraEntity, RestoreNumber):
    """GWM remote-start run-time setting.

    Kept in Home Assistant instead of on the car: the car stores a single run
    time that it also uses for the A/C, so writing this one through to the
    vehicle would drag the A/C run time along with it. It is sent as
    ``run_time_minutes`` with each remote-start command instead.
    """

    _attr_translation_key = "remote_start_run_time"
    _attr_device_class = NumberDeviceClass.DURATION
    _attr_entity_category = EntityCategory.CONFIG
    _attr_mode = NumberMode.SLIDER
    _attr_native_min_value = 5
    _attr_native_max_value = 30
    _attr_native_step = 1
    _attr_native_unit_of_measurement = UnitOfTime.MINUTES

    def __init__(self, api, coordinator, vin: str) -> None:
        super().__init__(coordinator, vin)
        self._api = api
        self._attr_unique_id = f"{vin}_remote_start_run_time"

    async def async_added_to_hass(self) -> None:
        """Restore the run time that was set before the last restart."""
        await super().async_added_to_hass()
        if self.coordinator.remote_start_run_time(self.vin) is not None:
            return
        last_data = await self.async_get_last_number_data()
        restored = last_data.native_value if last_data else None
        self.coordinator.set_remote_start_run_time(
            self.vin,
            int(restored) if restored is not None else DEFAULT_REMOTE_START_RUN_TIME,
        )

    @property
    def available(self) -> bool:
        """Return whether the remote-start run-time setting is available."""
        return super().available and self.remote_commands_available

    @property
    def native_value(self) -> float | None:
        """Return the run time the next remote start will use."""
        value = self.coordinator.remote_start_run_time(self.vin)
        return float(value) if value is not None else None

    async def async_set_native_value(self, value: float) -> None:
        """Save the run time for the next remote-start command."""
        if not float(value).is_integer() or value < 5 or value > 30:
            raise HomeAssistantError(
                translation_domain=DOMAIN,
                translation_key="invalid_climate_run_time",
            )

        self.coordinator.set_remote_start_run_time(self.vin, int(value))
