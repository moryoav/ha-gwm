"""Offline tests for the GWM cloud read runtime and bounded handoff."""

from __future__ import annotations

import ssl
from dataclasses import replace
from datetime import UTC, datetime

import pytest

pytest.importorskip("homeassistant")

from homeassistant.core import HomeAssistant

from custom_components.gwm_ora.cloud_auth import (
    GwmCloudCredentials,
    cloud_entry_data,
    cloud_unique_id,
)
from custom_components.gwm_ora.cloud_runtime import (
    GwmCloudBootstrap,
    GwmCloudClient,
    consume_cloud_bootstrap,
    stage_cloud_bootstrap,
)
from gwm_client import (
    AnzAuthenticated,
    AnzAuthState,
    ChargingPlanCommand,
    ChargingPlanInfo,
    CloudClimateConfiguration,
    CloudStatusItem,
    CloudVehicle,
    CloudVehicleBasics,
    CloudVehicleStatus,
    GwmConfigurationError,
    GwmNetworkError,
    GwmOptionalEndpointError,
    GwmSession,
    VehicleIdentifier,
)

_DEVICE_ID = "0123456789abcdef0123456789abcdef"
_REFRESHED_AT = datetime(2026, 8, 28, 12, 0, tzinfo=UTC)


def _bootstrap() -> tuple[GwmCloudCredentials, GwmCloudBootstrap]:
    credentials = GwmCloudCredentials(
        "aus",
        "AU",
        "account@example.invalid",
        "password",
        _DEVICE_ID,
    )
    regional = credentials.client_credentials()
    state = replace(
        AnzAuthState.for_credentials(regional),
        access_token="synthetic-access-token",
    )
    session = GwmSession(
        "AU",
        _DEVICE_ID,
        "synthetic-access-token",
        ssl.create_default_context(),
    )
    return credentials, GwmCloudBootstrap.from_authentication(
        credentials,
        AnzAuthenticated(state, session),
    )


class _ReadClient:
    def __init__(self) -> None:
        self.authenticated = True
        self.closed = False
        self.vehicles = (
            CloudVehicle(
                identifier=VehicleIdentifier("SYNTHETIC-VEHICLE-A"),
                app_show_series_name="Synthetic One",
                brand_name="GWM",
                vehicle_type="ORA",
            ),
            CloudVehicle(
                identifier=VehicleIdentifier("SYNTHETIC-VEHICLE-B"),
                vehicle_nickname="Synthetic Two",
                brand_name="GWM",
                vehicle_type="HAVAL",
            ),
        )
        self.statuses = {
            "SYNTHETIC-VEHICLE-A": CloudVehicleStatus(
                device_id="SYNTHETIC-SERIAL-A",
                items=(CloudStatusItem("2013021", 80, "%"),),
            ),
            "SYNTHETIC-VEHICLE-B": CloudVehicleStatus(
                device_id="SYNTHETIC-SERIAL-B",
                items=(CloudStatusItem("2013021", 55, "%"),),
            ),
        }
        self.basics: dict[str, CloudVehicleBasics | Exception] = {
            "SYNTHETIC-VEHICLE-A": CloudVehicleBasics(
                CloudClimateConfiguration(temperature="23", operation_time="15")
            ),
            "SYNTHETIC-VEHICLE-B": GwmOptionalEndpointError(
                operation="vehicle_basics",
                api_code="607099",
            ),
        }
        self.calls: list[tuple[str, str | None]] = []
        self.charging_commands: list[ChargingPlanCommand] = []

    async def acquire_vehicles(self) -> tuple[CloudVehicle, ...]:
        self.calls.append(("vehicles", None))
        return self.vehicles

    async def get_last_status(self, identifier: VehicleIdentifier) -> CloudVehicleStatus:
        self.calls.append(("status", identifier.value))
        return self.statuses[identifier.value]

    async def get_vehicle_basics(self, identifier: VehicleIdentifier) -> CloudVehicleBasics:
        self.calls.append(("basics", identifier.value))
        value = self.basics[identifier.value]
        if isinstance(value, Exception):
            raise value
        return value

    async def get_charging_plan(
        self,
        identifier: VehicleIdentifier,
    ) -> ChargingPlanInfo:
        self.calls.append(("charging", identifier.value))
        return ChargingPlanInfo()

    async def set_charging_plan(self, command: ChargingPlanCommand) -> None:
        self.charging_commands.append(command)

    async def aclose(self) -> None:
        self.closed = True
        self.authenticated = False


@pytest.mark.asyncio
async def test_handoff_is_one_shot_and_validates_entry_identity() -> None:
    credentials, bootstrap = _bootstrap()
    hass = HomeAssistant("synthetic-config")
    unique_id = cloud_unique_id(credentials)

    stage_cloud_bootstrap(hass, unique_id, bootstrap)
    consumed = consume_cloud_bootstrap(hass, unique_id)

    assert consumed is bootstrap
    assert consume_cloud_bootstrap(hass, unique_id) is None
    runtime = GwmCloudClient.from_entry_data(
        cloud_entry_data(credentials),
        unique_id,
        bootstrap,
    )
    assert runtime.region == "aus"
    assert runtime.reusable_bootstrap is bootstrap
    await runtime.aclose()


def test_handoff_rejects_a_different_entry_unique_id() -> None:
    credentials, bootstrap = _bootstrap()

    with pytest.raises(GwmConfigurationError):
        GwmCloudClient.from_entry_data(
            cloud_entry_data(credentials),
            "cloud:aus:different-account",
            bootstrap,
        )


@pytest.mark.asyncio
async def test_multi_vehicle_refresh_maps_snapshots_and_anz_optional_basics() -> None:
    client = _ReadClient()
    runtime = GwmCloudClient("aus", client, clock=lambda: _REFRESHED_AT)

    result = await runtime.async_get_vehicle_data()

    assert result["region"] == "aus"
    assert result["remote_commands_enabled"] is False
    assert result["charging_control_enabled"] is False
    vehicles = result["vehicles"]
    assert isinstance(vehicles, list)
    assert [vehicle["vin"] for vehicle in vehicles] == [
        "SYNTHETIC-VEHICLE-A",
        "SYNTHETIC-VEHICLE-B",
    ]
    assert vehicles[0]["values"]["soc"] == 80.0
    assert vehicles[0]["climate"]["target_temperature_c"] == 23
    assert vehicles[1]["values"]["soc"] == 55.0
    assert vehicles[1]["climate"]["target_temperature_c"] == 22
    assert all(
        vehicle["timestamps"]["last_refresh"] == "2026-08-28T12:00:00+00:00"
        for vehicle in vehicles
    )


@pytest.mark.asyncio
async def test_optional_basics_is_not_hidden_outside_anz() -> None:
    client = _ReadClient()
    runtime = GwmCloudClient("eu", client, clock=lambda: _REFRESHED_AT)

    with pytest.raises(GwmOptionalEndpointError):
        await runtime.async_get_vehicle_data()


@pytest.mark.asyncio
async def test_charging_capability_and_typed_delegation_follow_independent_opt_in() -> None:
    client = _ReadClient()
    runtime = GwmCloudClient(
        "aus",
        client,
        clock=lambda: _REFRESHED_AT,
        charging_control_enabled=True,
    )

    result = await runtime.async_get_vehicle_data()
    assert result["charging_control_enabled"] is True
    assert all(
        vehicle["capabilities"]["charging_control"] is True
        for vehicle in result["vehicles"]
    )
    identifier = VehicleIdentifier("SYNTHETIC-VEHICLE-A")
    assert await runtime.async_get_charging_plan(identifier) == ChargingPlanInfo()
    command = ChargingPlanCommand(identifier, False)
    await runtime.async_set_charging_plan(command)
    assert client.charging_commands == [command]


@pytest.mark.asyncio
async def test_refresh_is_atomic_when_any_vehicle_read_fails() -> None:
    client = _ReadClient()

    async def failed_status(identifier: VehicleIdentifier) -> CloudVehicleStatus:
        if identifier.value == "SYNTHETIC-VEHICLE-B":
            raise GwmNetworkError(operation="get_last_status")
        return client.statuses[identifier.value]

    client.get_last_status = failed_status  # type: ignore[method-assign]
    runtime = GwmCloudClient("aus", client, clock=lambda: _REFRESHED_AT)

    with pytest.raises(GwmNetworkError):
        await runtime.async_get_vehicle_data()


@pytest.mark.asyncio
async def test_rejected_runtime_revision_cannot_be_restaged() -> None:
    credentials, bootstrap = _bootstrap()

    class StateStore:
        cleared: dict[str, object] | None = None

        async def async_clear_auth_state(self, data: dict[str, object]) -> None:
            self.cleared = data

    state_store = StateStore()
    runtime = GwmCloudClient(
        "aus",
        _ReadClient(),
        bootstrap=bootstrap,
        state_store=state_store,
        entry_data=cloud_entry_data(credentials),
    )

    await runtime.async_authentication_rejected()

    assert runtime.reusable_bootstrap is None
    assert state_store.cleared == cloud_entry_data(credentials)
