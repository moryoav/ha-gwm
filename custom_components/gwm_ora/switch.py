"""Switch platform for GWM charging control."""

from __future__ import annotations

import logging
import time
from typing import Any

from homeassistant.components.switch import SwitchEntity
from homeassistant.core import HomeAssistant
from homeassistant.helpers.entity_platform import AddEntitiesCallback

from . import GwmOraConfigEntry
from .api import GwmOraApiError
from .const import DEFAULT_CHARGE_WINDOW_HOURS
from .entity import GwmOraEntity, async_call_addon_api, setup_vehicle_entities, vehicle_value

PARALLEL_UPDATES = 0

# How long a switch keeps showing the requested state before falling back to
# the value reported by the car, and how many polls it may span at most.
OPTIMISTIC_STATE_TIMEOUT = 120.0
OPTIMISTIC_STATE_MAX_UPDATES = 2

_LOGGER = logging.getLogger(__name__)


def _charging_plan_is_active(response: dict[str, Any]) -> bool:
    """Return whether a getChargingInfos response contains an active plan."""
    return any(
        plan.get("plan_type") is not None and str(plan["plan_type"]) != "-1"
        for plan in response.get("charge_plan_list") or []
    )


def _is_china_beantech_vehicle(coordinator, vehicle: dict[str, Any]) -> bool:
    """Return whether the vehicle is a BeanTech vehicle on the China gateway."""
    return (
        coordinator.region == "cn"
        and str(vehicle.get("platform") or "").lower() == "beantech"
    )


def _beantech_switches(api, coordinator, vin: str) -> tuple[SwitchEntity, ...]:
    """Switch entities that only the BeanTech platform supports."""
    return (
        GwmOraRemoteStartSwitch(api, coordinator, vin),
        GwmOraRemoteControlSwitch(
            api, coordinator, vin,
            turn_on_action="seat_heating_driver", turn_off_action="seat_heating_stop",
            state_key="front_driver_seat_heater_level", translation_key="seat_heating_driver",
        ),
        GwmOraRemoteControlSwitch(
            api, coordinator, vin,
            turn_on_action="seat_heating_passenger", turn_off_action="seat_heating_stop",
            state_key="front_passenger_seat_heater_level", translation_key="seat_heating_passenger",
        ),
        GwmOraRemoteControlSwitch(
            api, coordinator, vin,
            turn_on_action="seat_ventilation_driver", turn_off_action="seat_ventilation_stop",
            state_key="front_driver_seat_vent_level", translation_key="seat_ventilation_driver",
        ),
        GwmOraRemoteControlSwitch(
            api, coordinator, vin,
            turn_on_action="seat_ventilation_passenger", turn_off_action="seat_ventilation_stop",
            state_key="front_passenger_seat_vent_level", translation_key="seat_ventilation_passenger",
        ),
        GwmOraRemoteControlSwitch(
            api, coordinator, vin,
            turn_on_action="steering_wheel_heating", turn_off_action="steering_wheel_heating_stop",
            state_key="steering_wheel_heater_active", translation_key="steering_wheel_heating",
        ),
        GwmOraRemoteControlSwitch(
            api, coordinator, vin,
            turn_on_action="defrost_front", turn_off_action="defrost_front_stop",
            state_key="front_defroster", translation_key="defrost_front",
        ),
        GwmOraRemoteControlSwitch(
            api, coordinator, vin,
            turn_on_action="defrost_back", turn_off_action="defrost_back_stop",
            state_key="rear_defroster", translation_key="defrost_back",
        ),
        GwmOraClimatePresetSwitch(
            api, coordinator, vin,
            temperature=17, translation_key="fast_cool",
        ),
        GwmOraClimatePresetSwitch(
            api, coordinator, vin,
            temperature=31, translation_key="fast_heat",
        ),
        GwmOraBatteryHeatSwitch(
            api, coordinator, vin,
            turn_on_action="battery_initiative_heat",
            turn_off_action="battery_initiative_heat_stop",
            translation_key="battery_initiative_heat",
        ),
        GwmOraBatteryHeatSwitch(
            api, coordinator, vin,
            turn_on_action="battery_gun_heat",
            turn_off_action="battery_gun_heat_stop",
            translation_key="battery_gun_heat",
        ),
        GwmOraSmartChargeSwitch(api, coordinator, vin),
    )


def _vehicle_switches(api, coordinator, vehicle: dict[str, Any]) -> tuple[SwitchEntity, ...]:
    """Return the switches for a vehicle, filtered by backend platform.

    Entities are created per platform rather than gated only by ``available``,
    because an unavailable entity still runs ``async_added_to_hass``: a BeanTech
    vehicle must not try to read the NavInfo charging plan, and a NavInfo
    vehicle must not try to read the BeanTech charging endpoint.
    """
    vin = vehicle["vin"]
    if _is_china_beantech_vehicle(coordinator, vehicle):
        return _beantech_switches(api, coordinator, vin)
    return (GwmOraChargingScheduleSwitch(api, coordinator, vin),)


async def async_setup_entry(
    hass: HomeAssistant,
    entry: GwmOraConfigEntry,
    async_add_entities: AddEntitiesCallback,
) -> None:
    """Set up GWM switches."""
    setup_vehicle_entities(
        entry,
        async_add_entities,
        lambda vehicle: _vehicle_switches(
            entry.runtime_data.api, entry.runtime_data.coordinator, vehicle
        ),
    )


class GwmOraChargingScheduleSwitch(GwmOraEntity, SwitchEntity):
    """Manual on/off for scheduled charging.

    On sets a charging window from now for DEFAULT_CHARGE_WINDOW_HOURS (the car
    charges only within it); off clears the plan (the car charges whenever it is
    plugged in). For precise windows, use the ``gwm_ora.set_charging_plan``
    service. The plan is read once when the entity is added and tracked locally
    afterwards, since the vehicle does not report it in the polled status
    snapshot -- so a plan changed from the app shows up only after a restart.
    """

    _attr_translation_key = "charging_schedule"

    def __init__(self, api, coordinator, vin: str) -> None:
        super().__init__(coordinator, vin)
        self._api = api
        self._attr_unique_id = f"{vin}_charging_schedule"

    async def async_added_to_hass(self) -> None:
        """Load the current charging-plan state when the entity is added."""
        await super().async_added_to_hass()
        if not self.charging_control_available:
            return

        try:
            response = await self._api.async_get_charging_plan(self.vin)
        except GwmOraApiError as err:
            _LOGGER.debug("Could not read the current GWM charging plan: %s", err)
            return

        self.coordinator.set_charging_plan_active(
            self.vin, _charging_plan_is_active(response)
        )

    @property
    def is_on(self) -> bool | None:
        """Return the last known charging-plan state."""
        return self.coordinator.charging_plan_active(self.vin)

    @property
    def available(self) -> bool:
        """Return whether charging control is enabled in the add-on.

        BeanTech vehicles use the smart-scheduled-charging switch instead: they
        have a single chargingMode toggle rather than the plan window this
        switch writes, so exposing both would give one switch that always fails.
        """
        return (
            super().available
            and self.charging_control_available
            and not self.is_china_beantech
        )

    async def async_turn_on(self, **kwargs: Any) -> None:
        """Set a charging window from now for the default duration."""
        now_ms = int(time.time() * 1000)
        end_ms = now_ms + DEFAULT_CHARGE_WINDOW_HOURS * 3600 * 1000
        await async_call_addon_api(
            self._api.async_set_charging_plan(
                self.vin, enable=True, start_time=now_ms, end_time=end_ms, plan_type=0
            ),
            forbidden_translation_key="charging_control_unavailable",
        )
        self.coordinator.set_charging_plan_active(self.vin, True)

    async def async_turn_off(self, **kwargs: Any) -> None:
        """Clear the charging plan so the car charges whenever it is plugged in."""
        await async_call_addon_api(
            self._api.async_set_charging_plan(self.vin, enable=False),
            forbidden_translation_key="charging_control_unavailable",
        )
        self.coordinator.set_charging_plan_active(self.vin, False)


class _OptimisticRemoteSwitch(GwmOraEntity, SwitchEntity):
    """Switch that shows the requested state until the car reports back.

    Remote commands take a while to land in the polled status snapshot, so a
    plain switch snaps back to the old state right after being toggled. Setting
    ``assumed_state`` would fix that, but it also makes Home Assistant render
    the entity as a pair of on/off buttons instead of a single toggle, so the
    requested state is tracked here with a timeout instead.
    """

    _optimistic_state: bool | None = None
    _optimistic_until: float = 0.0
    _optimistic_updates_left: int = 0

    def _actual_is_on(self) -> bool | None:
        """Return the state reported by the car."""
        raise NotImplementedError

    @property
    def is_on(self) -> bool | None:
        if (
            self._optimistic_state is not None
            and time.monotonic() < self._optimistic_until
        ):
            return self._optimistic_state
        return self._actual_is_on()

    def _set_optimistic(self, value: bool) -> None:
        """Show ``value`` until the car confirms it or the timeout expires."""
        self._optimistic_state = value
        self._optimistic_until = time.monotonic() + OPTIMISTIC_STATE_TIMEOUT
        self._optimistic_updates_left = OPTIMISTIC_STATE_MAX_UPDATES
        self.async_write_ha_state()

    def _handle_coordinator_update(self) -> None:
        if self._optimistic_state is not None:
            self._optimistic_updates_left -= 1
            # Stop overriding once the car agrees, once it has had a couple of
            # polls to report the change, or once the timeout expires. The poll
            # budget matters for the mutually exclusive seat heating/ventilation
            # switches: turning one on makes the car switch the other off, and
            # that must not stay hidden behind a stale requested state.
            if (
                self._actual_is_on() == self._optimistic_state
                or self._optimistic_updates_left <= 0
                or time.monotonic() >= self._optimistic_until
            ):
                self._optimistic_state = None
        super()._handle_coordinator_update()


class GwmOraRemoteStartSwitch(_OptimisticRemoteSwitch):
    """Remote engine start/stop for BeanTech vehicles."""

    _attr_translation_key = "remote_start"

    def __init__(self, api, coordinator, vin: str) -> None:
        super().__init__(coordinator, vin)
        self._api = api
        self._attr_unique_id = f"{vin}_remote_start"

    def _actual_is_on(self) -> bool | None:
        """Return whether the engine is running."""
        return vehicle_value(self.vehicle, "engine_state_code") == 1

    @property
    def available(self) -> bool:
        """Return whether remote start is available."""
        return (
            super().available
            and self.remote_commands_available
            and self.is_china_beantech
            and self.security_pin_configured
        )

    async def async_turn_on(self, **kwargs: Any) -> None:
        """Start the engine for the configured run time."""
        command = await async_call_addon_api(
            self._api.async_vehicle_control(
                self.vin,
                "remote_start",
                run_time_minutes=self.coordinator.remote_start_run_time(self.vin),
            )
        )
        self.coordinator.async_track_command(command)
        self._set_optimistic(True)

    async def async_turn_off(self, **kwargs: Any) -> None:
        """Stop the engine."""
        command = await async_call_addon_api(
            self._api.async_vehicle_control(self.vin, "remote_stop")
        )
        self.coordinator.async_track_command(command)
        self._set_optimistic(False)


class GwmOraRemoteControlSwitch(_OptimisticRemoteSwitch):
    """Generic BeanTech remote-control on/off switch."""

    def __init__(
        self,
        api,
        coordinator,
        vin: str,
        *,
        turn_on_action: str,
        turn_off_action: str,
        state_key: str,
        translation_key: str,
    ) -> None:
        super().__init__(coordinator, vin)
        self._api = api
        self._turn_on_action = turn_on_action
        self._turn_off_action = turn_off_action
        self._state_key = state_key
        self._attr_translation_key = translation_key
        self._attr_unique_id = f"{vin}_{turn_on_action}"

    def _actual_is_on(self) -> bool | None:
        value = vehicle_value(self.vehicle, self._state_key)
        if value is None:
            return None
        return bool(value)

    @property
    def available(self) -> bool:
        return (
            super().available
            and self.remote_commands_available
            and self.is_china_beantech
        )

    async def async_turn_on(self, **kwargs: Any) -> None:
        command = await async_call_addon_api(
            self._api.async_vehicle_control(self.vin, self._turn_on_action)
        )
        self.coordinator.async_track_command(command)
        self._set_optimistic(True)

    async def async_turn_off(self, **kwargs: Any) -> None:
        command = await async_call_addon_api(
            self._api.async_vehicle_control(self.vin, self._turn_off_action)
        )
        self.coordinator.async_track_command(command)
        self._set_optimistic(False)


class GwmOraSmartChargeSwitch(GwmOraEntity, SwitchEntity):
    """Smart scheduled charging for BeanTech vehicles.

    The car exposes a single ``chargingMode`` toggle: on charges only inside the
    window configured in the app (``customTime``), off charges as soon as it is
    plugged in. The window itself is not editable here -- it is reported as
    attributes so it is visible where the switch is.

    The state is read from the car when the entity is added and re-read after
    each toggle. It is not part of the polled status snapshot, so a change made
    in the app shows up on the next Home Assistant restart.
    """

    _attr_translation_key = "smart_charge"

    def __init__(self, api, coordinator, vin: str) -> None:
        super().__init__(coordinator, vin)
        self._api = api
        self._attr_unique_id = f"{vin}_smart_charge"
        self._start_time: str | None = None
        self._end_time: str | None = None

    async def _async_read_state(self) -> None:
        """Read the charging mode and window from the car."""
        try:
            response = await self._api.async_get_charging_mode(self.vin)
        except GwmOraApiError as err:
            _LOGGER.debug("Could not read the GWM smart charging mode: %s", err)
            return

        self._start_time = response.get("start_time")
        self._end_time = response.get("end_time")
        self.coordinator.set_local_flag(
            self.vin, "smart_charge", bool(response.get("enabled"))
        )

    async def async_added_to_hass(self) -> None:
        """Read the current charging mode when the entity is added."""
        await super().async_added_to_hass()
        if not self.charging_control_available:
            return
        await self._async_read_state()

    @property
    def is_on(self) -> bool | None:
        """Return whether scheduled charging is active."""
        return self.coordinator.local_flag(self.vin, "smart_charge")

    @property
    def extra_state_attributes(self) -> dict[str, Any] | None:
        """Expose the charging window configured in the app."""
        if self._start_time is None and self._end_time is None:
            return None
        return {"start_time": self._start_time, "end_time": self._end_time}

    @property
    def available(self) -> bool:
        return (
            super().available
            and self.charging_control_available
            and self.is_china_beantech
        )

    async def async_turn_on(self, **kwargs: Any) -> None:
        """Charge only inside the window configured in the app."""
        await self._async_set(True)

    async def async_turn_off(self, **kwargs: Any) -> None:
        """Charge as soon as the car is plugged in."""
        await self._async_set(False)

    async def _async_set(self, enable: bool) -> None:
        command = await async_call_addon_api(
            self._api.async_set_charging_mode(self.vin, enable=enable),
            forbidden_translation_key="charging_control_unavailable",
        )
        self.coordinator.set_local_flag(self.vin, "smart_charge", enable)
        # Tracked like any other remote command so its result shows up in the
        # command-status sensor ("... completed - 充电设置成功 [0]"). The switch
        # stays optimistic until the command reaches a terminal state, then reads
        # the value back so a failure reverts it and a success reflects the car.
        self.coordinator.async_track_command(command, on_terminal=self._async_read_state)


class GwmOraBatteryHeatSwitch(_OptimisticRemoteSwitch):
    """Battery pack heating (active, or while plugged in).

    The vehicle accepts these commands but does not report the resulting state
    in its status snapshot -- ``battery_pack_state`` stayed at 0 across verified
    on/off commands -- so the switch reflects the last command sent from Home
    Assistant rather than a value read back from the car.
    """

    def __init__(
        self,
        api,
        coordinator,
        vin: str,
        *,
        turn_on_action: str,
        turn_off_action: str,
        translation_key: str,
    ) -> None:
        super().__init__(coordinator, vin)
        self._api = api
        self._turn_on_action = turn_on_action
        self._turn_off_action = turn_off_action
        self._attr_translation_key = translation_key
        self._attr_unique_id = f"{vin}_{translation_key}"

    def _actual_is_on(self) -> bool | None:
        return self.coordinator.local_flag(self.vin, self._attr_translation_key)

    @property
    def available(self) -> bool:
        return (
            super().available
            and self.remote_commands_available
            and self.is_china_beantech
            and self.security_pin_configured
        )

    async def async_turn_on(self, **kwargs: Any) -> None:
        command = await async_call_addon_api(
            self._api.async_vehicle_control(self.vin, self._turn_on_action)
        )
        self.coordinator.async_track_command(command)
        self.coordinator.set_local_flag(self.vin, self._attr_translation_key, True)
        self._set_optimistic(True)

    async def async_turn_off(self, **kwargs: Any) -> None:
        command = await async_call_addon_api(
            self._api.async_vehicle_control(self.vin, self._turn_off_action)
        )
        self.coordinator.async_track_command(command)
        self.coordinator.set_local_flag(self.vin, self._attr_translation_key, False)
        self._set_optimistic(False)


class GwmOraClimatePresetSwitch(_OptimisticRemoteSwitch):
    """Fast cool / fast heat, driven by the A/C command at a fixed temperature.

    The car has no dedicated fast cool/heat command: both are the normal A/C
    start with the temperature pinned to one end of its range.
    """

    def __init__(
        self,
        api,
        coordinator,
        vin: str,
        *,
        temperature: int,
        translation_key: str,
    ) -> None:
        super().__init__(coordinator, vin)
        self._api = api
        self._temperature = temperature
        self._attr_translation_key = translation_key
        self._attr_unique_id = f"{vin}_{translation_key}"

    @property
    def climate(self) -> dict[str, Any]:
        """Return the vehicle's climate block."""
        vehicle = self.vehicle or {}
        return vehicle.get("climate") or {}

    def _actual_is_on(self) -> bool | None:
        """Return whether the A/C is running at this preset's temperature."""
        if self.climate.get("mode") == "off":
            return False
        return self.climate.get("target_temperature_c") == self._temperature

    @property
    def available(self) -> bool:
        return (
            super().available
            and self.remote_commands_available
            and self.is_china_beantech
        )

    async def async_turn_on(self, **kwargs: Any) -> None:
        """Start the A/C pinned to this preset's temperature."""
        command = await async_call_addon_api(
            self._api.async_set_climate(
                self.vin, mode="auto", temperature=self._temperature
            )
        )
        self.coordinator.async_track_command(command)
        self._set_optimistic(True)

    async def async_turn_off(self, **kwargs: Any) -> None:
        """Stop the A/C."""
        command = await async_call_addon_api(
            self._api.async_set_climate(self.vin, mode="off")
        )
        self.coordinator.async_track_command(command)
        self._set_optimistic(False)
