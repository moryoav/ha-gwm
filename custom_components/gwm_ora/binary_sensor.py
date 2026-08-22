"""Binary sensor platform for GWM."""

from __future__ import annotations

from collections.abc import Callable
from dataclasses import dataclass
from typing import Any

from homeassistant.components.binary_sensor import (
    BinarySensorDeviceClass,
    BinarySensorEntity,
    BinarySensorEntityDescription,
)
from homeassistant.core import HomeAssistant
from homeassistant.helpers.entity import EntityCategory
from homeassistant.helpers.entity_platform import AddEntitiesCallback

from . import GwmOraConfigEntry
from .entity import GwmOraEntity, setup_vehicle_entities, vehicle_value

PARALLEL_UPDATES = 0


@dataclass(frozen=True, kw_only=True)
class GwmOraBinarySensorEntityDescription(BinarySensorEntityDescription):
    """Describes a GWM binary sensor."""

    value_fn: Callable[[dict[str, Any] | None], bool | None]


def _bool_value(*keys: str) -> Callable[[dict[str, Any] | None], bool | None]:
    def value_fn(vehicle: dict[str, Any] | None) -> bool | None:
        for key in keys:
            value = vehicle_value(vehicle, key)
            if value is not None:
                return value
        return None

    return value_fn


BINARY_SENSORS: tuple[GwmOraBinarySensorEntityDescription, ...] = (
    GwmOraBinarySensorEntityDescription(
        key="charging_active",
        translation_key="charging_active",
        device_class=BinarySensorDeviceClass.BATTERY_CHARGING,
        value_fn=_bool_value("charging_active"),
    ),
    GwmOraBinarySensorEntityDescription(
        key="charge_plug_connected",
        translation_key="charge_plug",
        device_class=BinarySensorDeviceClass.PLUG,
        value_fn=_bool_value("charge_plug_connected"),
    ),
    GwmOraBinarySensorEntityDescription(
        key="ac_active",
        translation_key="ac_active",
        value_fn=_bool_value("ac_active"),
    ),
    GwmOraBinarySensorEntityDescription(
        key="lock_open",
        translation_key="lock_open",
        device_class=BinarySensorDeviceClass.LOCK,
        value_fn=lambda vehicle: (
            None if vehicle_value(vehicle, "locked") is None else not vehicle_value(vehicle, "locked")
        ),
    ),
    GwmOraBinarySensorEntityDescription(
        key="window_front_left_open",
        translation_key="window_front_driver",
        device_class=BinarySensorDeviceClass.WINDOW,
        value_fn=_bool_value("window_front_driver_open", "window_front_left_open"),
    ),
    GwmOraBinarySensorEntityDescription(
        key="window_front_right_open",
        translation_key="window_front_passenger",
        device_class=BinarySensorDeviceClass.WINDOW,
        value_fn=_bool_value("window_front_passenger_open", "window_front_right_open"),
    ),
    GwmOraBinarySensorEntityDescription(
        key="window_rear_left_open",
        translation_key="window_rear_passenger_side",
        device_class=BinarySensorDeviceClass.WINDOW,
        value_fn=_bool_value("window_rear_passenger_side_open", "window_rear_left_open"),
    ),
    GwmOraBinarySensorEntityDescription(
        key="window_rear_right_open",
        translation_key="window_rear_driver_side",
        device_class=BinarySensorDeviceClass.WINDOW,
        value_fn=_bool_value("window_rear_driver_side_open", "window_rear_right_open"),
    ),
    GwmOraBinarySensorEntityDescription(
        key="door_front_driver_open",
        translation_key="door_front_driver",
        device_class=BinarySensorDeviceClass.DOOR,
        value_fn=_bool_value("door_front_driver_open"),
    ),
    GwmOraBinarySensorEntityDescription(
        key="door_front_passenger_open",
        translation_key="door_front_passenger",
        device_class=BinarySensorDeviceClass.DOOR,
        value_fn=_bool_value("door_front_passenger_open"),
    ),
    GwmOraBinarySensorEntityDescription(
        key="door_rear_driver_side_open",
        translation_key="door_rear_driver_side",
        device_class=BinarySensorDeviceClass.DOOR,
        value_fn=_bool_value("door_rear_driver_side_open"),
    ),
    GwmOraBinarySensorEntityDescription(
        key="door_rear_passenger_side_open",
        translation_key="door_rear_passenger_side",
        device_class=BinarySensorDeviceClass.DOOR,
        value_fn=_bool_value("door_rear_passenger_side_open"),
    ),
    GwmOraBinarySensorEntityDescription(
        key="trunk_open",
        translation_key="trunk",
        device_class=BinarySensorDeviceClass.DOOR,
        value_fn=_bool_value("trunk_open"),
    ),
    GwmOraBinarySensorEntityDescription(
        key="air_circulation",
        translation_key="air_circulation",
        value_fn=_bool_value("air_circulation"),
    ),
    GwmOraBinarySensorEntityDescription(
        key="front_defroster",
        translation_key="front_defroster",
        value_fn=_bool_value("front_defroster"),
    ),
    GwmOraBinarySensorEntityDescription(
        key="rear_defroster",
        translation_key="rear_defroster",
        value_fn=_bool_value("rear_defroster"),
    ),
    GwmOraBinarySensorEntityDescription(
        key="steering_wheel_heater_active",
        translation_key="steering_wheel_heater",
        entity_registry_enabled_default=False,
        value_fn=_bool_value("steering_wheel_heater_active"),
    ),
    GwmOraBinarySensorEntityDescription(
        key="front_windscreen_heater_active",
        translation_key="front_windscreen_heater",
        entity_registry_enabled_default=False,
        value_fn=_bool_value("front_windscreen_heater_active"),
    ),
    GwmOraBinarySensorEntityDescription(
        key="gps_authorized",
        translation_key="gps_authorized",
        entity_category=EntityCategory.DIAGNOSTIC,
        entity_registry_enabled_default=False,
        value_fn=_bool_value("gps_authorized"),
    ),
)


async def async_setup_entry(
    hass: HomeAssistant,
    entry: GwmOraConfigEntry,
    async_add_entities: AddEntitiesCallback,
) -> None:
    """Set up GWM binary sensors."""
    setup_vehicle_entities(
        entry,
        async_add_entities,
        lambda vehicle: (
            GwmOraBinarySensor(entry.runtime_data.coordinator, vehicle["vin"], description)
            for description in BINARY_SENSORS
        ),
    )


class GwmOraBinarySensor(GwmOraEntity, BinarySensorEntity):
    """A GWM binary sensor."""

    entity_description: GwmOraBinarySensorEntityDescription

    def __init__(
        self,
        coordinator,
        vin: str,
        description: GwmOraBinarySensorEntityDescription,
    ) -> None:
        super().__init__(coordinator, vin)
        self.entity_description = description
        self._attr_unique_id = f"{vin}_{description.key}"

    @property
    def is_on(self) -> bool | None:
        """Return true if the binary sensor is on."""
        return self.entity_description.value_fn(self.vehicle)
