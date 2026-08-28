"""Direct climate orchestration, journal recovery, timeout, and isolation tests."""

from __future__ import annotations

import base64
from dataclasses import replace
from datetime import UTC, datetime, timedelta
from pathlib import Path
from typing import Any

import pytest

pytest.importorskip("homeassistant")

from homeassistant.core import HomeAssistant

from custom_components.gwm_ora.api import GwmOraApiForbidden
from custom_components.gwm_ora.cloud_auth import (
    DirectCloudCredentials,
    direct_entry_data,
    direct_unique_id,
)
from custom_components.gwm_ora.cloud_commands import DirectClimateCommandApi
from custom_components.gwm_ora.cloud_runtime import DirectClimateContext
from custom_components.gwm_ora.cloud_storage import direct_cloud_state_store
from gwm_ora_client import (
    CloudClimateConfiguration,
    CloudStatusItem,
    CloudVehicle,
    CloudVehicleBasics,
    CloudVehicleStatus,
    EuAuthState,
    EuIssuedIdentity,
    GwmApiError,
    RemoteCommandAcceptance,
    RemoteCommandResultItem,
    VehicleIdentifier,
)

_NOW = datetime(2026, 8, 28, 18, 0, tzinfo=UTC)
_VIN = "LGWEEUA50PK000001"
_DEVICE_ID = "0123456789abcdef0123456789abcdef"


class _Clock:
    def __init__(self) -> None:
        self.value = _NOW

    def __call__(self) -> datetime:
        return self.value


class _Cloud:
    region = "eu"

    def __init__(self, *, currently_on: bool = True) -> None:
        self.currently_on = currently_on
        self.updated: list[tuple[int, int]] = []
        self.sent = []
        self.poll_results: list[tuple[RemoteCommandResultItem, ...]] = []
        self.send_error: BaseException | None = None

    async def async_get_climate_context(
        self,
        identifier: VehicleIdentifier,
        *,
        include_status: bool,
    ) -> DirectClimateContext:
        status = (
            CloudVehicleStatus(
                items=(CloudStatusItem("2202001", "1" if self.currently_on else "0"),)
            )
            if include_status
            else None
        )
        return DirectClimateContext(
            vehicle=CloudVehicle(identifier),
            basics=CloudVehicleBasics(CloudClimateConfiguration("22", "900")),
            status=status,
        )

    async def async_update_climate_defaults(
        self,
        identifier: VehicleIdentifier,
        *,
        temperature: int,
        operation_time_minutes: int,
    ) -> None:
        assert identifier.value == _VIN
        self.updated.append((temperature, operation_time_minutes))

    async def async_send_climate_command(
        self,
        command: object,
        *,
        security_password_hash: str,
    ) -> RemoteCommandAcceptance:
        if self.send_error is not None:
            raise self.send_error
        assert len(security_password_hash) == 32
        self.sent.append(command)
        return RemoteCommandAcceptance("provider-command-1")

    async def async_get_remote_command_results(
        self,
        identifier: VehicleIdentifier,
        command_id: str,
    ) -> tuple[RemoteCommandResultItem, ...]:
        assert identifier.value == _VIN
        assert command_id == "provider-command-1"
        return self.poll_results.pop(0) if self.poll_results else ()

    async def async_get_vehicle_data(self) -> dict[str, object]:
        return {"region": self.region, "vehicles": []}


def _credentials() -> DirectCloudCredentials:
    return DirectCloudCredentials(
        "eu",
        "DE",
        "private-account",
        "private-password",
        _DEVICE_ID,
    )


def _state(credentials: DirectCloudCredentials) -> EuAuthState:
    return replace(
        EuAuthState.for_credentials(credentials.client_credentials()),
        access_token="private-access",
        refresh_token="private-refresh",
        gw_id="private-gw",
        bean_id="private-bean",
        issued_identity=EuIssuedIdentity(
            certificate=base64.b64encode(b"synthetic-certificate").decode(),
            private_key=base64.b64encode(b"synthetic-private-key").decode(),
        ),
    )


async def _api(
    tmp_path: Path,
    cloud: _Cloud,
    clock: _Clock,
    *,
    enabled: bool = True,
) -> tuple[DirectClimateCommandApi, Any, DirectCloudCredentials]:
    credentials = _credentials()
    hass = HomeAssistant(str(tmp_path))
    store = direct_cloud_state_store(hass, direct_unique_id(credentials))
    await store.async_save_auth_state(credentials, _state(credentials))
    api = DirectClimateCommandApi(
        cloud,  # type: ignore[arg-type]
        store,
        credentials,
        enabled=enabled,
        security_pin="1234" if enabled else None,
        clock=clock,
    )
    return api, store, credentials


@pytest.mark.asyncio
async def test_acceptance_is_journaled_before_polling_and_reaches_terminal_result(
    tmp_path: Path,
) -> None:
    cloud = _Cloud()
    cloud.poll_results = [
        (RemoteCommandResultItem("provider-command-1", "0x04", "2000", "Waiting"),),
        (RemoteCommandResultItem("provider-command-1", "0x04", "0", "Success"),),
    ]
    clock = _Clock()
    api, store, credentials = await _api(tmp_path, cloud, clock)

    accepted = await api.async_set_climate(_VIN, mode="cool", temperature=21)
    journal = await store.async_get_command_journal(direct_entry_data(credentials))
    assert accepted["state"] == "in_progress"
    assert len(journal) == 1
    assert journal[0].cloud_command_id == "provider-command-1"
    assert journal[0].state == "accepted"
    assert cloud.updated == [(21, 15)]
    assert len(cloud.sent) == 1

    pending = await api.async_get_command(str(accepted["id"]))
    assert pending["state"] == "in_progress"
    assert (await store.async_get_command_journal(direct_entry_data(credentials)))[0].state == "polling"
    completed = await api.async_get_command(str(accepted["id"]))
    assert completed["state"] == "completed"
    assert "Success [0]" in str(completed["status"])
    assert (await store.async_get_command_journal(direct_entry_data(credentials)))[0].state == "completed"


@pytest.mark.asyncio
async def test_restart_restores_polling_without_resending_vehicle_operation(tmp_path: Path) -> None:
    clock = _Clock()
    first_cloud = _Cloud()
    first, store, credentials = await _api(tmp_path, first_cloud, clock)
    accepted = await first.async_set_climate(_VIN, mode="cool")
    assert len(first_cloud.sent) == 1

    second_cloud = _Cloud()
    second_cloud.poll_results = [
        (RemoteCommandResultItem("provider-command-1", "0x04", "6", "Success"),)
    ]
    second = DirectClimateCommandApi(
        second_cloud,  # type: ignore[arg-type]
        store,
        credentials,
        enabled=True,
        security_pin="1234",
        clock=clock,
    )
    restored = await second.async_restore(direct_entry_data(credentials))
    assert restored[0]["id"] == accepted["id"]
    assert second_cloud.sent == []
    completed = await second.async_get_command(str(accepted["id"]))
    assert completed["state"] == "completed"
    assert second_cloud.sent == []


@pytest.mark.asyncio
async def test_timeout_is_persisted_without_an_extra_poll(tmp_path: Path) -> None:
    cloud = _Cloud()
    clock = _Clock()
    api, store, credentials = await _api(tmp_path, cloud, clock)
    accepted = await api.async_set_climate(_VIN, mode="cool")
    clock.value += timedelta(seconds=91)

    timed_out = await api.async_get_command(str(accepted["id"]))
    assert timed_out["state"] == "timeout"
    assert cloud.poll_results == []
    assert (await store.async_get_command_journal(direct_entry_data(credentials)))[0].state == "failed"


@pytest.mark.asyncio
async def test_rejection_and_disabled_mode_never_create_a_journal_entry(tmp_path: Path) -> None:
    cloud = _Cloud()
    cloud.send_error = GwmApiError(operation="send_climate_command", api_code="607777")
    clock = _Clock()
    api, store, credentials = await _api(tmp_path, cloud, clock)
    with pytest.raises(GwmApiError):
        await api.async_set_climate(_VIN, mode="cool")
    assert await store.async_get_command_journal(direct_entry_data(credentials)) == ()

    disabled, _store, _credentials_value = await _api(tmp_path / "disabled", _Cloud(), clock, enabled=False)
    with pytest.raises(GwmOraApiForbidden):
        await disabled.async_set_climate(_VIN, mode="cool")


@pytest.mark.asyncio
async def test_runtime_only_and_temperature_while_off_save_without_command(tmp_path: Path) -> None:
    clock = _Clock()
    cloud = _Cloud(currently_on=False)
    api, _store, _credentials_value = await _api(tmp_path, cloud, clock)

    runtime = await api.async_set_climate(_VIN, operation_time_minutes=20)
    temperature = await api.async_set_climate(_VIN, temperature=24)
    assert runtime["state"] == "completed"
    assert temperature["state"] == "completed"
    assert cloud.updated == [(22, 20), (24, 15)]
    assert cloud.sent == []
