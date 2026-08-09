"""Static coverage checks for the GWM ORA integration entity descriptions."""

import pytest


def test_sensor_description_keys_cover_v1_contract() -> None:
    pytest.importorskip("homeassistant")
    from homeassistant.components.sensor import SensorDeviceClass
    from homeassistant.helpers.entity import EntityCategory

    from custom_components.gwm_ora.sensor import SENSORS

    keys = {description.key for description in SENSORS}

    assert {
        "soc",
        "range_km",
        "odometer_km",
        "remaining_charging_time_min",
        "charging_status",
        "soce",
        "interior_temperature_c",
        "command_status",
    } <= keys

    descriptions = {description.key: description for description in SENSORS}
    assert descriptions["acquisition_time"].entity_category is EntityCategory.DIAGNOSTIC
    assert descriptions["update_time"].entity_category is EntityCategory.DIAGNOSTIC
    assert descriptions["command_status"].entity_category is EntityCategory.DIAGNOSTIC
    assert descriptions["acquisition_time"].entity_registry_enabled_default is False
    assert descriptions["update_time"].entity_registry_enabled_default is False
    assert descriptions["command_status"].entity_registry_enabled_default is not False
    assert descriptions["charging_status"].device_class is SensorDeviceClass.ENUM
    assert descriptions["charging_status"].options == [
        "disconnected",
        "connected",
        "charging",
        "awaiting_charging",
        "waiting_for_power",
        "error",
    ]


def test_binary_sensor_description_keys_cover_v1_contract() -> None:
    pytest.importorskip("homeassistant")
    from custom_components.gwm_ora.binary_sensor import BINARY_SENSORS

    keys = {description.key for description in BINARY_SENSORS}

    assert {
        "charging_active",
        "charge_plug_connected",
        "ac_active",
        "lock_open",
        "window_front_left_open",
        "window_front_right_open",
        "window_rear_left_open",
        "window_rear_right_open",
    } <= keys


def test_platforms_declare_parallel_updates() -> None:
    pytest.importorskip("homeassistant")
    from custom_components.gwm_ora import binary_sensor, button, climate, device_tracker, lock, number, sensor

    assert sensor.PARALLEL_UPDATES == 0
    assert binary_sensor.PARALLEL_UPDATES == 0
    assert climate.PARALLEL_UPDATES == 0
    assert lock.PARALLEL_UPDATES == 0
    assert button.PARALLEL_UPDATES == 0
    assert number.PARALLEL_UPDATES == 0
    assert device_tracker.PARALLEL_UPDATES == 0


def test_climate_run_time_number_metadata() -> None:
    pytest.importorskip("homeassistant")
    from homeassistant.components.number import NumberDeviceClass, NumberMode
    from homeassistant.const import Platform, UnitOfTime
    from homeassistant.helpers.entity import EntityCategory

    from custom_components.gwm_ora.const import PLATFORMS
    from custom_components.gwm_ora.number import GwmOraClimateRunTimeNumber

    entity = object.__new__(GwmOraClimateRunTimeNumber)

    assert Platform.NUMBER in PLATFORMS
    assert entity.translation_key == "climate_run_time"
    assert entity.device_class is NumberDeviceClass.DURATION
    assert entity.entity_category is EntityCategory.CONFIG
    assert entity.mode is NumberMode.SLIDER
    assert entity.native_min_value == 5
    assert entity.native_max_value == 30
    assert entity.native_step == 1
    assert entity.native_unit_of_measurement == UnitOfTime.MINUTES


@pytest.mark.asyncio
async def test_climate_run_time_rejects_fractional_values() -> None:
    pytest.importorskip("homeassistant")
    from homeassistant.exceptions import HomeAssistantError

    from custom_components.gwm_ora.number import GwmOraClimateRunTimeNumber

    entity = object.__new__(GwmOraClimateRunTimeNumber)

    with pytest.raises(HomeAssistantError) as error:
        await entity.async_set_native_value(5.9)

    assert error.value.translation_key == "invalid_climate_run_time"
