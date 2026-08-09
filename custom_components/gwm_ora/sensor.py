"""Sensor platform for GWM ORA."""

from __future__ import annotations

from collections.abc import Callable
from dataclasses import dataclass
from datetime import datetime
from typing import Any

from homeassistant.components.sensor import SensorDeviceClass, SensorEntity, SensorEntityDescription, SensorStateClass
from homeassistant.const import PERCENTAGE, UnitOfLength, UnitOfPressure, UnitOfTemperature, UnitOfTime
from homeassistant.core import HomeAssistant
from homeassistant.helpers.entity import EntityCategory
from homeassistant.helpers.entity_platform import AddEntitiesCallback
from homeassistant.util import dt as dt_util

from . import GwmOraConfigEntry
from .entity import GwmOraEntity, setup_vehicle_entities, vehicle_value

PARALLEL_UPDATES = 0

CHARGING_STATUS_OPTIONS = [
    "disconnected",
    "connected",
    "charging",
    "awaiting_charging",
    "waiting_for_power",
    "error",
]


@dataclass(frozen=True, kw_only=True)
class GwmOraSensorEntityDescription(SensorEntityDescription):
    """Describes a GWM ORA sensor."""

    value_fn: Callable[[dict[str, Any] | None], Any]


def _value(key: str) -> Callable[[dict[str, Any] | None], Any]:
    return lambda vehicle: vehicle_value(vehicle, key)


def _timestamp(key: str) -> Callable[[dict[str, Any] | None], datetime | None]:
    def read(vehicle: dict[str, Any] | None) -> datetime | None:
        if vehicle is None:
            return None
        value = (vehicle.get("timestamps") or {}).get(key)
        return dt_util.parse_datetime(value) if value else None

    return read


SENSORS: tuple[GwmOraSensorEntityDescription, ...] = (
    GwmOraSensorEntityDescription(
        key="soc",
        translation_key="soc",
        device_class=SensorDeviceClass.BATTERY,
        native_unit_of_measurement=PERCENTAGE,
        state_class=SensorStateClass.MEASUREMENT,
        value_fn=_value("soc"),
    ),
    GwmOraSensorEntityDescription(
        key="range_km",
        translation_key="range",
        device_class=SensorDeviceClass.DISTANCE,
        native_unit_of_measurement=UnitOfLength.KILOMETERS,
        state_class=SensorStateClass.MEASUREMENT,
        value_fn=_value("range_km"),
    ),
    GwmOraSensorEntityDescription(
        key="odometer_km",
        translation_key="odometer",
        device_class=SensorDeviceClass.DISTANCE,
        native_unit_of_measurement=UnitOfLength.KILOMETERS,
        state_class=SensorStateClass.TOTAL_INCREASING,
        value_fn=_value("odometer_km"),
    ),
    GwmOraSensorEntityDescription(
        key="remaining_charging_time_min",
        translation_key="remaining_charging_time",
        device_class=SensorDeviceClass.DURATION,
        native_unit_of_measurement=UnitOfTime.MINUTES,
        state_class=SensorStateClass.MEASUREMENT,
        value_fn=_value("remaining_charging_time_min"),
    ),
    GwmOraSensorEntityDescription(
        key="charging_status",
        translation_key="charging_status",
        device_class=SensorDeviceClass.ENUM,
        options=CHARGING_STATUS_OPTIONS,
        value_fn=_value("charging_status"),
    ),
    GwmOraSensorEntityDescription(
        key="soce",
        translation_key="soce",
        native_unit_of_measurement=PERCENTAGE,
        state_class=SensorStateClass.MEASUREMENT,
        value_fn=_value("soce"),
    ),
    GwmOraSensorEntityDescription(
        key="interior_temperature_c",
        translation_key="interior_temperature",
        device_class=SensorDeviceClass.TEMPERATURE,
        native_unit_of_measurement=UnitOfTemperature.CELSIUS,
        state_class=SensorStateClass.MEASUREMENT,
        value_fn=_value("interior_temperature_c"),
    ),
    GwmOraSensorEntityDescription(
        key="tire_pressure_front_left_kpa",
        translation_key="tire_pressure_front_left",
        device_class=SensorDeviceClass.PRESSURE,
        native_unit_of_measurement=UnitOfPressure.KPA,
        state_class=SensorStateClass.MEASUREMENT,
        value_fn=_value("tire_pressure_front_left_kpa"),
    ),
    GwmOraSensorEntityDescription(
        key="tire_pressure_front_right_kpa",
        translation_key="tire_pressure_front_right",
        device_class=SensorDeviceClass.PRESSURE,
        native_unit_of_measurement=UnitOfPressure.KPA,
        state_class=SensorStateClass.MEASUREMENT,
        value_fn=_value("tire_pressure_front_right_kpa"),
    ),
    GwmOraSensorEntityDescription(
        key="tire_pressure_rear_left_kpa",
        translation_key="tire_pressure_rear_left",
        device_class=SensorDeviceClass.PRESSURE,
        native_unit_of_measurement=UnitOfPressure.KPA,
        state_class=SensorStateClass.MEASUREMENT,
        value_fn=_value("tire_pressure_rear_left_kpa"),
    ),
    GwmOraSensorEntityDescription(
        key="tire_pressure_rear_right_kpa",
        translation_key="tire_pressure_rear_right",
        device_class=SensorDeviceClass.PRESSURE,
        native_unit_of_measurement=UnitOfPressure.KPA,
        state_class=SensorStateClass.MEASUREMENT,
        value_fn=_value("tire_pressure_rear_right_kpa"),
    ),
    GwmOraSensorEntityDescription(
        key="tire_temperature_front_left_c",
        translation_key="tire_temperature_front_left",
        device_class=SensorDeviceClass.TEMPERATURE,
        native_unit_of_measurement=UnitOfTemperature.CELSIUS,
        state_class=SensorStateClass.MEASUREMENT,
        value_fn=_value("tire_temperature_front_left_c"),
    ),
    GwmOraSensorEntityDescription(
        key="tire_temperature_front_right_c",
        translation_key="tire_temperature_front_right",
        device_class=SensorDeviceClass.TEMPERATURE,
        native_unit_of_measurement=UnitOfTemperature.CELSIUS,
        state_class=SensorStateClass.MEASUREMENT,
        value_fn=_value("tire_temperature_front_right_c"),
    ),
    GwmOraSensorEntityDescription(
        key="tire_temperature_rear_left_c",
        translation_key="tire_temperature_rear_left",
        device_class=SensorDeviceClass.TEMPERATURE,
        native_unit_of_measurement=UnitOfTemperature.CELSIUS,
        state_class=SensorStateClass.MEASUREMENT,
        value_fn=_value("tire_temperature_rear_left_c"),
    ),
    GwmOraSensorEntityDescription(
        key="tire_temperature_rear_right_c",
        translation_key="tire_temperature_rear_right",
        device_class=SensorDeviceClass.TEMPERATURE,
        native_unit_of_measurement=UnitOfTemperature.CELSIUS,
        state_class=SensorStateClass.MEASUREMENT,
        value_fn=_value("tire_temperature_rear_right_c"),
    ),
    GwmOraSensorEntityDescription(
        key="acquisition_time",
        translation_key="acquisition_time",
        device_class=SensorDeviceClass.TIMESTAMP,
        entity_category=EntityCategory.DIAGNOSTIC,
        entity_registry_enabled_default=False,
        value_fn=_timestamp("acquisition_time"),
    ),
    GwmOraSensorEntityDescription(
        key="update_time",
        translation_key="update_time",
        device_class=SensorDeviceClass.TIMESTAMP,
        entity_category=EntityCategory.DIAGNOSTIC,
        entity_registry_enabled_default=False,
        value_fn=_timestamp("update_time"),
    ),
    GwmOraSensorEntityDescription(
        key="command_status",
        translation_key="command_status",
        entity_category=EntityCategory.DIAGNOSTIC,
        value_fn=lambda vehicle: None if vehicle is None else vehicle.get("command_status"),
    ),
)


async def async_setup_entry(
    hass: HomeAssistant,
    entry: GwmOraConfigEntry,
    async_add_entities: AddEntitiesCallback,
) -> None:
    """Set up GWM ORA sensors."""
    setup_vehicle_entities(
        entry,
        async_add_entities,
        lambda vehicle: (
            GwmOraSensor(entry.runtime_data.coordinator, vehicle["vin"], description)
            for description in SENSORS
        ),
    )


class GwmOraSensor(GwmOraEntity, SensorEntity):
    """A GWM ORA sensor."""

    entity_description: GwmOraSensorEntityDescription

    def __init__(
        self,
        coordinator,
        vin: str,
        description: GwmOraSensorEntityDescription,
    ) -> None:
        super().__init__(coordinator, vin)
        self.entity_description = description
        self._attr_unique_id = f"{vin}_{description.key}"

    @property
    def native_value(self) -> Any:
        """Return the sensor value."""
        return self.entity_description.value_fn(self.vehicle)
