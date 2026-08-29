"""Restart-safe direct-cloud command orchestration."""

from __future__ import annotations

import hashlib
import secrets
from collections.abc import Callable
from datetime import UTC, datetime, timedelta
from typing import Never, cast

from gwm_ora_client import (
    DEFAULT_OPERATION_TIME_MINUTES,
    DEFAULT_TEMPERATURE_C,
    ClimateCommand,
    ClimateMode,
    CloseWindowsCommand,
    DoorLockCommand,
    GwmClientError,
    Region,
    VehicleIdentifier,
    is_valid_operation_time,
    normalize_operation_time,
    normalize_temperature,
    select_remote_command_result,
    valid_temperature,
)

from .api import GwmOraApiError, GwmOraApiForbidden
from .cloud_auth import DirectCloudCredentials
from .cloud_runtime import DirectCloudReadClient
from .cloud_storage import DirectCloudStateStore, DirectCommandJournalEntry

_DEFAULT_RESULT_TIMEOUT = timedelta(seconds=90)
_RUSSIA_RESULT_TIMEOUT = timedelta(seconds=300)


class DirectClimateCommandApi:
    """Expose approved direct writes over the durable Task 14 journal."""

    def __init__(
        self,
        cloud: DirectCloudReadClient,
        state_store: DirectCloudStateStore,
        credentials: DirectCloudCredentials,
        *,
        enabled: bool,
        security_pin: str | None,
        clock: Callable[[], datetime] | None = None,
    ) -> None:
        if (
            type(state_store) is not DirectCloudStateStore
            or type(credentials) is not DirectCloudCredentials
            or type(enabled) is not bool
            or (security_pin is not None and not isinstance(security_pin, str))
            or (clock is not None and not callable(clock))
        ):
            raise ValueError("direct_command_api_invalid")
        self._cloud = cloud
        self._state_store = state_store
        self._credentials = credentials
        self._enabled = enabled
        self._security_pin = security_pin.strip() if security_pin else ""
        self._clock = clock or (lambda: datetime.now(UTC))
        self._commands: dict[str, DirectCommandJournalEntry] = {}
        self._timeout_ids: set[str] = set()

    async def async_restore(
        self,
        entry_data: dict[str, object],
    ) -> tuple[dict[str, object], ...]:
        """Load accepted commands without ever resending their vehicle operation."""

        journal = await self._state_store.async_get_command_journal(entry_data)
        self._commands = {entry.journal_id: entry for entry in journal}
        return tuple(
            self._command_view(entry)
            for entry in journal
            if entry.state in {"accepted", "polling"}
        )

    async def async_refresh(self) -> dict[str, object]:
        return await self._cloud.async_get_vehicle_data()

    async def async_get_vehicles(self) -> dict[str, object]:
        return await self._cloud.async_get_vehicle_data()

    async def async_set_climate(
        self,
        vin: str,
        *,
        mode: str | None = None,
        temperature: int | None = None,
        operation_time_minutes: int | None = None,
    ) -> dict[str, object]:
        """Validate, resolve, send, and journal one climate request."""

        self._ensure_available()
        if not isinstance(vin, str):
            raise GwmOraApiError("A/C command requires a valid vehicle")
        try:
            identifier = VehicleIdentifier(vin)
        except (TypeError, ValueError):
            raise GwmOraApiError("A/C command requires a valid vehicle") from None
        normalized_mode = mode.strip().lower() if isinstance(mode, str) else None
        if normalized_mode not in {None, "cool", "off"}:
            raise GwmOraApiError("A/C mode must be 'cool' or 'off' in this region")
        if temperature is not None and (
            isinstance(temperature, bool)
            or not isinstance(temperature, int)
            or not 16 <= temperature <= 32
        ):
            raise GwmOraApiError("A/C temperature must be a whole number from 16 to 32")
        if operation_time_minutes is not None and (
            isinstance(operation_time_minutes, bool)
            or not isinstance(operation_time_minutes, int)
            or not is_valid_operation_time(operation_time_minutes)
        ):
            raise GwmOraApiError(
                "A/C run time must be a whole number from 5 to 30 minutes"
            )
        if (
            normalized_mode is None
            and temperature is None
            and operation_time_minutes is None
        ):
            raise GwmOraApiError(
                "A/C command requires a mode, temperature, or run time"
            )

        run_time_only = normalized_mode is None and temperature is None
        context = await self._cloud.async_get_climate_context(
            identifier,
            include_status=temperature is not None and normalized_mode is None,
        )
        climate = context.basics.climate
        stored_temperature = None if climate is None else climate.temperature
        stored_operation_time = None if climate is None else climate.operation_time
        if run_time_only:
            effective_temperature = valid_temperature(stored_temperature)
            if effective_temperature is None:
                raise GwmOraApiError(
                    "Current A/C temperature is unavailable; no settings were changed"
                )
        else:
            effective_temperature = normalize_temperature(
                str(temperature) if temperature is not None else stored_temperature,
                DEFAULT_TEMPERATURE_C,
            )
        effective_operation_time = (
            operation_time_minutes
            if operation_time_minutes is not None
            else normalize_operation_time(
                stored_operation_time,
                DEFAULT_OPERATION_TIME_MINUTES,
            )
        )
        currently_on = _climate_is_on(context.status)

        if (
            normalized_mode in {"cool"}
            or temperature is not None
            or operation_time_minutes is not None
        ):
            await self._cloud.async_update_climate_defaults(
                identifier,
                temperature=effective_temperature,
                operation_time_minutes=effective_operation_time,
            )

        should_send = (
            normalized_mode is not None or temperature is not None and currently_on
        )
        command_name = "A/C run time" if run_time_only else "A/C"
        if not should_send:
            message = (
                f"{command_name}: saved; applies to the next A/C command"
                if run_time_only
                else f"{command_name}: saved; A/C is off so no remote command was sent"
            )
            return _local_completed_command(identifier.value, message)

        command = ClimateCommand(
            identifier=identifier,
            mode=cast(ClimateMode, normalized_mode or "cool"),
            temperature=effective_temperature,
            operation_time_minutes=effective_operation_time,
            currently_on=currently_on,
        )
        acceptance = await self._cloud.async_send_climate_command(
            command,
            security_password_hash=_security_password_hash(self._security_pin),
        )
        return await self._record_acceptance(
            identifier, command_name, acceptance.command_id
        )

    async def async_lock(self, vin: str, action: str) -> dict[str, object]:
        """Validate, send, and journal one lock or unlock request."""

        self._ensure_available()
        identifier = _vehicle_identifier(vin, command_name="Door lock")
        normalized_action = action.strip().lower() if isinstance(action, str) else ""
        if normalized_action not in {"lock", "unlock"}:
            raise GwmOraApiError("Door lock action must be 'lock' or 'unlock'")
        command_name = "Door lock" if normalized_action == "lock" else "Door unlock"
        acceptance = await self._cloud.async_send_lock_command(
            DoorLockCommand(identifier, normalized_action == "lock"),
            security_password_hash=_security_password_hash(self._security_pin),
        )
        return await self._record_acceptance(
            identifier, command_name, acceptance.command_id
        )

    async def async_close_windows(self, vin: str) -> dict[str, object]:
        """Validate, send, and journal one close-all-windows request."""

        self._ensure_available()
        identifier = _vehicle_identifier(vin, command_name="Window close")
        acceptance = await self._cloud.async_send_close_windows_command(
            CloseWindowsCommand(identifier),
            security_password_hash=_security_password_hash(self._security_pin),
        )
        return await self._record_acceptance(
            identifier, "Window close", acceptance.command_id
        )

    async def async_get_command(self, command_id: str) -> dict[str, object]:
        """Poll one accepted provider ID and persist every terminal transition."""

        entry = self._commands.get(command_id)
        if entry is None:
            raise GwmOraApiError("Remote command was not found")
        if entry.state in {"completed", "failed"}:
            return self._command_view(entry)
        now = self._now()
        if now - entry.created_at >= self._result_timeout:
            return await self._mark_timeout(entry, now)
        if entry.state == "accepted":
            entry = await self._state_store.async_update_command(
                self._credentials,
                entry.journal_id,
                state="polling",
                updated_at=now,
            )
            self._commands[entry.journal_id] = entry
        try:
            results = await self._cloud.async_get_remote_command_results(
                VehicleIdentifier(entry.vehicle_id),
                entry.cloud_command_id,
            )
        except GwmClientError:
            raise
        result = select_remote_command_result(
            results,
            command_id=entry.cloud_command_id,
            region=Region(self._cloud.region),
            expected_remote_type=_expected_remote_type(entry.command_name),
        )
        now = self._now()
        if result is None or result.state == "pending":
            if now - entry.created_at >= self._result_timeout:
                return await self._mark_timeout(entry, now)
            return self._command_view(
                entry,
                status=f"{entry.command_name}: accepted by GWM, waiting for vehicle result",
            )
        state = "completed" if result.state == "completed" else "failed"
        entry = await self._state_store.async_update_command(
            self._credentials,
            entry.journal_id,
            state=state,
            updated_at=now,
        )
        self._commands[entry.journal_id] = entry
        status_word = "completed" if state == "completed" else "failed"
        details = result.result_message or "no message"
        code = result.result_code or "unknown"
        return self._command_view(
            entry,
            status=f"{entry.command_name}: {status_word} - {details} [{code}]",
        )

    async def _record_acceptance(
        self,
        identifier: VehicleIdentifier,
        command_name: str,
        cloud_command_id: str,
    ) -> dict[str, object]:
        try:
            entry = await self._state_store.async_record_accepted_command(
                self._credentials,
                vehicle_id=identifier.value,
                command_name=command_name,
                cloud_command_id=cloud_command_id,
                accepted_at=self._now(),
            )
        except Exception as err:
            raise GwmOraApiError(
                "GWM accepted the command but its recovery journal could not be saved; do not retry"
            ) from err
        self._commands[entry.journal_id] = entry
        return self._command_view(entry)

    async def _mark_timeout(
        self,
        entry: DirectCommandJournalEntry,
        now: datetime,
    ) -> dict[str, object]:
        entry = await self._state_store.async_update_command(
            self._credentials,
            entry.journal_id,
            state="failed",
            updated_at=now,
        )
        self._commands[entry.journal_id] = entry
        self._timeout_ids.add(entry.journal_id)
        return self._command_view(
            entry,
            state="timeout",
            status=f"{entry.command_name}: timed out waiting for vehicle result",
        )

    def _command_view(
        self,
        entry: DirectCommandJournalEntry,
        *,
        state: str | None = None,
        status: str | None = None,
    ) -> dict[str, object]:
        view_state = state or (
            "timeout"
            if entry.journal_id in self._timeout_ids
            else "in_progress"
            if entry.state in {"accepted", "polling"}
            else entry.state
        )
        if status is None:
            status = {
                "accepted": f"{entry.command_name}: accepted by GWM, waiting for vehicle result",
                "polling": f"{entry.command_name}: accepted by GWM, waiting for vehicle result",
                "completed": f"{entry.command_name}: completed",
                "failed": f"{entry.command_name}: failed",
            }[entry.state]
        return {
            "id": entry.journal_id,
            "vin": entry.vehicle_id,
            "name": entry.command_name,
            "state": view_state,
            "status": status,
        }

    @property
    def _result_timeout(self) -> timedelta:
        return (
            _RUSSIA_RESULT_TIMEOUT
            if self._cloud.region == Region.RUSSIA.value
            else _DEFAULT_RESULT_TIMEOUT
        )

    def _ensure_available(self) -> None:
        if not self._enabled:
            raise GwmOraApiForbidden("Direct-cloud remote commands are disabled")
        if not self._security_pin:
            raise GwmOraApiForbidden(
                "Direct-cloud remote commands require a security PIN"
            )

    def _now(self) -> datetime:
        value = self._clock()
        if (
            not isinstance(value, datetime)
            or value.tzinfo is None
            or value.utcoffset() is None
        ):
            raise GwmOraApiError("Direct command clock is invalid")
        return value.astimezone(UTC)

    @staticmethod
    def _unavailable() -> Never:
        raise GwmOraApiForbidden("This direct-cloud write is not available yet")

    async def async_vehicle_control(
        self, *args: object, **kwargs: object
    ) -> dict[str, object]:
        self._unavailable()

    async def async_get_charging_plan(
        self, *args: object, **kwargs: object
    ) -> dict[str, object]:
        self._unavailable()

    async def async_set_charging_plan(
        self, *args: object, **kwargs: object
    ) -> dict[str, object]:
        self._unavailable()


def _climate_is_on(status: object) -> bool:
    items = getattr(status, "items", ()) if status is not None else ()
    return any(item.code == "2202001" and str(item.value) == "1" for item in items)


def _security_password_hash(pin: str) -> str:
    return hashlib.md5(
        pin.encode("ascii", errors="replace"), usedforsecurity=False
    ).hexdigest()


def _vehicle_identifier(vin: object, *, command_name: str) -> VehicleIdentifier:
    if not isinstance(vin, str):
        raise GwmOraApiError(f"{command_name} command requires a valid vehicle")
    try:
        return VehicleIdentifier(vin)
    except (TypeError, ValueError):
        raise GwmOraApiError(
            f"{command_name} command requires a valid vehicle"
        ) from None


def _expected_remote_type(command_name: str) -> str:
    if command_name in {"A/C", "A/C run time"}:
        return "0x04"
    if command_name in {"Door lock", "Door unlock"}:
        return "0x05"
    if command_name == "Window close":
        return "0x08"
    raise GwmOraApiError(
        "Remote command journal contains an unsupported command family"
    )


def _local_completed_command(vin: str, status: str) -> dict[str, object]:
    return {
        "id": secrets.token_hex(16),
        "vin": vin,
        "name": "A/C",
        "state": "completed",
        "status": status,
    }


__all__ = ["DirectClimateCommandApi"]
