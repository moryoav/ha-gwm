"""Sensor platform for GWM."""

from __future__ import annotations

from collections.abc import Callable
from dataclasses import dataclass
from datetime import datetime
from typing import Any

from homeassistant.components.sensor import SensorDeviceClass, SensorEntity, SensorEntityDescription, SensorStateClass
from homeassistant.const import PERCENTAGE, UnitOfLength, UnitOfPressure, UnitOfTemperature, UnitOfTime, UnitOfVolume
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
    "charging_complete",
    "awaiting_charging",
    "waiting_for_power",
    "error",
]

BEANTECH_SENSOR_KEYS = {
    "charge_soc",
    "charging_gun_model",
    "hcu_powertrain_state",
    "power",
    "battery_pack_state",
    "acc_clean_off",
    "tbox_state",
    "wireless_level",
    "oil_segments",
    "aux_battery_level",
    "remaining_usable_charge_percent",
}


@dataclass(frozen=True, kw_only=True)
class GwmOraSensorEntityDescription(SensorEntityDescription):
    """Describes a GWM sensor."""

    value_fn: Callable[[dict[str, Any] | None], Any]


def _value(key: str) -> Callable[[dict[str, Any] | None], Any]:
    return lambda vehicle: vehicle_value(vehicle, key)


def _remaining_charging_time_value(vehicle: dict[str, Any] | None) -> Any:
    """Return remaining charging minutes, or "未充电" when the car is not charging."""
    value = vehicle_value(vehicle, "remaining_charging_time_min")
    return "未充电" if value is None else value


def _enum_value(
    key: str, options: set[str]
) -> Callable[[dict[str, Any] | None], str | None]:
    def read(vehicle: dict[str, Any] | None) -> str | None:
        value = vehicle_value(vehicle, key)
        normalized = str(value) if value is not None else None
        return normalized if normalized in options else None

    return read


def _charging_gun_model_value(vehicle: dict[str, Any] | None) -> str | None:
    """Return the charging gun model, or a sentinel when no gun is plugged in."""
    if vehicle is None:
        return None
    values = vehicle.get("values") or {}
    plugged = values.get("charge_plug_connected")
    if plugged is False:
        return "not_plugged"
    if plugged is not True:
        return None
    model = values.get("charging_gun_model")
    normalized = str(model) if model is not None else None
    return normalized if normalized in {"0", "1"} else None


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
        key="fuel_level_l",
        translation_key="fuel_level",
        device_class=SensorDeviceClass.VOLUME_STORAGE,
        native_unit_of_measurement=UnitOfVolume.LITERS,
        state_class=SensorStateClass.MEASUREMENT,
        entity_registry_enabled_default=False,
        value_fn=_value("fuel_level_l"),
    ),
    GwmOraSensorEntityDescription(
        key="fuel_range_km",
        translation_key="fuel_range",
        device_class=SensorDeviceClass.DISTANCE,
        native_unit_of_measurement=UnitOfLength.KILOMETERS,
        state_class=SensorStateClass.MEASUREMENT,
        entity_registry_enabled_default=False,
        value_fn=_value("fuel_range_km"),
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
        value_fn=_remaining_charging_time_value,
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
        key="tire_pressure_state_front_left",
        translation_key="tire_pressure_state_front_left",
        entity_category=EntityCategory.DIAGNOSTIC,
        entity_registry_enabled_default=False,
        value_fn=_value("tire_pressure_state_front_left"),
    ),
    GwmOraSensorEntityDescription(
        key="tire_pressure_state_front_right",
        translation_key="tire_pressure_state_front_right",
        entity_category=EntityCategory.DIAGNOSTIC,
        entity_registry_enabled_default=False,
        value_fn=_value("tire_pressure_state_front_right"),
    ),
    GwmOraSensorEntityDescription(
        key="tire_pressure_state_rear_left",
        translation_key="tire_pressure_state_rear_left",
        entity_category=EntityCategory.DIAGNOSTIC,
        entity_registry_enabled_default=False,
        value_fn=_value("tire_pressure_state_rear_left"),
    ),
    GwmOraSensorEntityDescription(
        key="tire_pressure_state_rear_right",
        translation_key="tire_pressure_state_rear_right",
        entity_category=EntityCategory.DIAGNOSTIC,
        entity_registry_enabled_default=False,
        value_fn=_value("tire_pressure_state_rear_right"),
    ),
    GwmOraSensorEntityDescription(
        key="tire_temperature_state_front_left",
        translation_key="tire_temperature_state_front_left",
        entity_category=EntityCategory.DIAGNOSTIC,
        entity_registry_enabled_default=False,
        value_fn=_value("tire_temperature_state_front_left"),
    ),
    GwmOraSensorEntityDescription(
        key="tire_temperature_state_front_right",
        translation_key="tire_temperature_state_front_right",
        entity_category=EntityCategory.DIAGNOSTIC,
        entity_registry_enabled_default=False,
        value_fn=_value("tire_temperature_state_front_right"),
    ),
    GwmOraSensorEntityDescription(
        key="tire_temperature_state_rear_left",
        translation_key="tire_temperature_state_rear_left",
        entity_category=EntityCategory.DIAGNOSTIC,
        entity_registry_enabled_default=False,
        value_fn=_value("tire_temperature_state_rear_left"),
    ),
    GwmOraSensorEntityDescription(
        key="tire_temperature_state_rear_right",
        translation_key="tire_temperature_state_rear_right",
        entity_category=EntityCategory.DIAGNOSTIC,
        entity_registry_enabled_default=False,
        value_fn=_value("tire_temperature_state_rear_right"),
    ),
    GwmOraSensorEntityDescription(
        key="window_learn_front_left",
        translation_key="window_learn_front_left",
        entity_category=EntityCategory.DIAGNOSTIC,
        entity_registry_enabled_default=False,
        value_fn=_value("window_learn_front_left"),
    ),
    GwmOraSensorEntityDescription(
        key="window_learn_front_right",
        translation_key="window_learn_front_right",
        entity_category=EntityCategory.DIAGNOSTIC,
        entity_registry_enabled_default=False,
        value_fn=_value("window_learn_front_right"),
    ),
    GwmOraSensorEntityDescription(
        key="window_learn_rear_left",
        translation_key="window_learn_rear_left",
        entity_category=EntityCategory.DIAGNOSTIC,
        entity_registry_enabled_default=False,
        value_fn=_value("window_learn_rear_left"),
    ),
    GwmOraSensorEntityDescription(
        key="window_learn_rear_right",
        translation_key="window_learn_rear_right",
        entity_category=EntityCategory.DIAGNOSTIC,
        entity_registry_enabled_default=False,
        value_fn=_value("window_learn_rear_right"),
    ),
    GwmOraSensorEntityDescription(
        key="rear_left_seat_heater_level",
        translation_key="rear_left_seat_heater_level",
        entity_registry_enabled_default=False,
        value_fn=_value("rear_left_seat_heater_level"),
    ),
    GwmOraSensorEntityDescription(
        key="rear_right_seat_heater_level",
        translation_key="rear_right_seat_heater_level",
        entity_registry_enabled_default=False,
        value_fn=_value("rear_right_seat_heater_level"),
    ),
    GwmOraSensorEntityDescription(
        key="engine_state_code",
        translation_key="engine_state_code",
        entity_category=EntityCategory.DIAGNOSTIC,
        entity_registry_enabled_default=False,
        value_fn=_value("engine_state_code"),
    ),
    GwmOraSensorEntityDescription(
        key="front_driver_seat_heater_level",
        translation_key="front_driver_seat_heater_level",
        entity_registry_enabled_default=False,
        value_fn=_value("front_driver_seat_heater_level"),
    ),
    GwmOraSensorEntityDescription(
        key="front_passenger_seat_heater_level",
        translation_key="front_passenger_seat_heater_level",
        entity_registry_enabled_default=False,
        value_fn=_value("front_passenger_seat_heater_level"),
    ),
    GwmOraSensorEntityDescription(
        key="front_driver_seat_vent_level",
        translation_key="front_driver_seat_vent_level",
        entity_registry_enabled_default=False,
        value_fn=_value("front_driver_seat_vent_level"),
    ),
    GwmOraSensorEntityDescription(
        key="front_passenger_seat_vent_level",
        translation_key="front_passenger_seat_vent_level",
        entity_registry_enabled_default=False,
        value_fn=_value("front_passenger_seat_vent_level"),
    ),
    GwmOraSensorEntityDescription(
        key="sunroof_position_code",
        translation_key="sunroof_position_code",
        entity_category=EntityCategory.DIAGNOSTIC,
        entity_registry_enabled_default=False,
        value_fn=_value("sunroof_position_code"),
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
    GwmOraSensorEntityDescription(
        key="charge_soc",
        translation_key="charge_soc",
        native_unit_of_measurement=PERCENTAGE,
        state_class=SensorStateClass.MEASUREMENT,
        entity_category=EntityCategory.DIAGNOSTIC,
        entity_registry_enabled_default=False,
        value_fn=_value("charge_soc"),
    ),
    GwmOraSensorEntityDescription(
        key="charging_gun_model",
        translation_key="charging_gun_model",
        device_class=SensorDeviceClass.ENUM,
        options=["not_plugged", "0", "1"],
        entity_category=EntityCategory.DIAGNOSTIC,
        entity_registry_enabled_default=False,
        value_fn=_charging_gun_model_value,
    ),
    GwmOraSensorEntityDescription(
        key="hcu_powertrain_state",
        translation_key="hcu_powertrain_state",
        device_class=SensorDeviceClass.ENUM,
        options=["1", "3", "6"],
        entity_category=EntityCategory.DIAGNOSTIC,
        entity_registry_enabled_default=False,
        value_fn=_enum_value("hcu_powertrain_state", {"1", "3", "6"}),
    ),
    GwmOraSensorEntityDescription(
        key="power",
        translation_key="power",
        entity_category=EntityCategory.DIAGNOSTIC,
        entity_registry_enabled_default=False,
        value_fn=_value("power"),
    ),
    GwmOraSensorEntityDescription(
        key="battery_pack_state",
        translation_key="battery_pack_state",
        entity_category=EntityCategory.DIAGNOSTIC,
        entity_registry_enabled_default=False,
        value_fn=_value("battery_pack_state"),
    ),
    GwmOraSensorEntityDescription(
        key="acc_clean_off",
        translation_key="acc_clean_off",
        entity_category=EntityCategory.DIAGNOSTIC,
        entity_registry_enabled_default=False,
        value_fn=_value("acc_clean_off"),
    ),
    GwmOraSensorEntityDescription(
        key="tbox_state",
        translation_key="tbox_state",
        device_class=SensorDeviceClass.ENUM,
        options=["0", "1"],
        entity_category=EntityCategory.DIAGNOSTIC,
        entity_registry_enabled_default=False,
        value_fn=_enum_value("tbox_state", {"0", "1"}),
    ),
    GwmOraSensorEntityDescription(
        key="wireless_level",
        translation_key="wireless_level",
        entity_category=EntityCategory.DIAGNOSTIC,
        entity_registry_enabled_default=False,
        value_fn=_value("wireless_level"),
    ),
    GwmOraSensorEntityDescription(
        key="oil_segments",
        translation_key="oil_segments",
        entity_category=EntityCategory.DIAGNOSTIC,
        entity_registry_enabled_default=False,
        value_fn=_value("oil_segments"),
    ),
    GwmOraSensorEntityDescription(
        key="aux_battery_level",
        translation_key="aux_battery",
        device_class=SensorDeviceClass.BATTERY,
        native_unit_of_measurement=PERCENTAGE,
        state_class=SensorStateClass.MEASUREMENT,
        entity_registry_enabled_default=False,
        value_fn=_value("aux_battery_level"),
    ),
    GwmOraSensorEntityDescription(
        key="remaining_usable_charge_percent",
        translation_key="remaining_usable_charge",
        device_class=SensorDeviceClass.BATTERY,
        native_unit_of_measurement=PERCENTAGE,
        state_class=SensorStateClass.MEASUREMENT,
        entity_registry_enabled_default=True,
        value_fn=_value("remaining_usable_charge_percent"),
    ),
)


def _sensor_descriptions_for_vehicle(
    vehicle: dict[str, Any],
    region: str,
) -> tuple[GwmOraSensorEntityDescription, ...]:
    """Return descriptions supported by the vehicle backend."""
    if str(region or "").lower() == "cn" and str(vehicle.get("platform") or "").lower() == "beantech":
        return SENSORS
    return tuple(description for description in SENSORS if description.key not in BEANTECH_SENSOR_KEYS)


async def async_setup_entry(
    hass: HomeAssistant,
    entry: GwmOraConfigEntry,
    async_add_entities: AddEntitiesCallback,
) -> None:
    """Set up GWM sensors."""
    setup_vehicle_entities(
        entry,
        async_add_entities,
        lambda vehicle: (
            GwmOraSensor(entry.runtime_data.coordinator, vehicle["vin"], description)
            for description in _sensor_descriptions_for_vehicle(
                vehicle, entry.runtime_data.coordinator.region
            )
        ),
    )


class GwmOraSensor(GwmOraEntity, SensorEntity):
    """A GWM sensor."""

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
