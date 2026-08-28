"""Data coordinator for GWM."""

from __future__ import annotations

import asyncio
import logging
import time
from contextlib import suppress
from datetime import timedelta
from typing import Any

from homeassistant.config_entries import ConfigEntry
from homeassistant.core import HomeAssistant
from homeassistant.exceptions import ConfigEntryAuthFailed
from homeassistant.helpers.update_coordinator import DataUpdateCoordinator, UpdateFailed

from gwm_ora_client import GwmAuthenticationError, GwmClientError

from .api import GwmOraApiAuthError, GwmOraApiClient, GwmOraApiError, GwmOraApiUnavailable
from .cloud_commands import DirectClimateCommandApi
from .cloud_runtime import DirectCloudReadClient, DirectReadOnlyCommandApi
from .const import DOMAIN

_LOGGER = logging.getLogger(__name__)


class GwmOraDataUpdateCoordinator(DataUpdateCoordinator[dict[str, Any]]):
    """Coordinator that polls the add-on's cached vehicle data."""

    _TERMINAL_COMMAND_STATES = {"completed", "failed", "timeout", "canceled"}

    def __init__(
        self,
        hass: HomeAssistant,
        api: GwmOraApiClient | DirectReadOnlyCommandApi | DirectClimateCommandApi,
        *,
        config_entry: ConfigEntry | None = None,
        direct_client: DirectCloudReadClient | None = None,
        update_interval_seconds: int = 30,
    ) -> None:
        super().__init__(
            hass,
            _LOGGER,
            config_entry=config_entry,
            name=DOMAIN,
            update_interval=timedelta(seconds=update_interval_seconds),
        )
        self.api = api
        self.direct_client = direct_client
        self._command_tasks: dict[str, asyncio.Task[None]] = {}
        self._charging_plan_active: dict[str, bool] = {}

    async def _async_update_data(self) -> dict[str, Any]:
        if self.direct_client is not None:
            try:
                return await self.direct_client.async_get_vehicle_data()
            except GwmAuthenticationError as err:
                with suppress(Exception):
                    await self.direct_client.async_authentication_rejected()
                raise ConfigEntryAuthFailed(
                    "Direct GWM cloud authentication was rejected"
                ) from err
            except GwmClientError as err:
                raise UpdateFailed(
                    f"Direct GWM cloud {err.category} during {err.operation}"
                ) from err
        try:
            return await self.api.async_get_vehicles()
        except GwmOraApiAuthError as err:
            raise ConfigEntryAuthFailed("Add-on API token rejected") from err
        except (GwmOraApiUnavailable, GwmOraApiError) as err:
            raise UpdateFailed(str(err)) from err

    @property
    def vehicles(self) -> list[dict[str, Any]]:
        """Return vehicle snapshots."""
        data = self.data or {}
        return list(data.get("vehicles", []))

    @property
    def region(self) -> str:
        """Return the add-on region."""
        return str((self.data or {}).get("region") or "").lower()

    def vehicle(self, vin: str) -> dict[str, Any] | None:
        """Return one vehicle snapshot by VIN."""
        return next((vehicle for vehicle in self.vehicles if vehicle.get("vin") == vin), None)

    def resolve_vehicle(self, identifier: str) -> dict[str, Any] | None:
        """Return one vehicle snapshot by internal VIN or display serial number.

        Users supply the human-readable VIN (the device serial number, GWM's
        ``showedVin``), while the add-on keys vehicles by the encoded ``vin``.
        Accept either so service calls can use the VIN shown on the device.
        """
        display_identifier = identifier.casefold()
        return next(
            (
                vehicle
                for vehicle in self.vehicles
                if identifier == vehicle.get("vin")
                or display_identifier == str(vehicle.get("serial_number") or "").casefold()
            ),
            None,
        )

    def charging_plan_active(self, vin: str) -> bool | None:
        """Return the last known charging-plan state for a vehicle."""
        return self._charging_plan_active.get(vin)

    def set_charging_plan_active(self, vin: str, active: bool) -> None:
        """Update a vehicle's locally known charging-plan state."""
        self._charging_plan_active[vin] = active
        self.async_update_listeners()

    def async_track_command(self, command: dict[str, Any]) -> None:
        """Track a queued remote command and push status updates into HA."""
        self._apply_command_status(command)
        command_id = command.get("id")
        if not command_id or command.get("state") in self._TERMINAL_COMMAND_STATES:
            return

        if command_id in self._command_tasks:
            return

        task = self.hass.async_create_task(self._async_follow_command(command_id))
        self._command_tasks[command_id] = task
        task.add_done_callback(lambda _: self._command_tasks.pop(command_id, None))

    def async_cancel_command_tasks(self) -> None:
        """Cancel in-flight command status polling tasks."""
        for task in self._command_tasks.values():
            task.cancel()
        self._command_tasks.clear()

    async def _async_follow_command(self, command_id: str) -> None:
        """Poll one command until the add-on reports a terminal state."""
        direct_command = self.direct_client is not None
        deadline_seconds = 310 if direct_command and self.region == "rus" else 130
        poll_interval = 5 if direct_command else 2
        deadline = time.monotonic() + deadline_seconds
        while time.monotonic() < deadline:
            await asyncio.sleep(poll_interval)
            try:
                command = await self.api.async_get_command(command_id)
            except (GwmOraApiUnavailable, GwmOraApiError, GwmClientError) as err:
                _LOGGER.debug("Could not refresh GWM command %s status: %s", command_id, err)
                continue

            self._apply_command_status(command)
            if command.get("state") not in self._TERMINAL_COMMAND_STATES:
                continue

            if command.get("state") == "completed":
                await self._async_refresh_after_completed_command()
            return

    async def _async_refresh_after_completed_command(self) -> None:
        """Refresh cached vehicle data immediately after a successful command."""
        with suppress(
            GwmOraApiUnavailable,
            GwmOraApiError,
            GwmOraApiAuthError,
            GwmClientError,
        ):
            self.async_set_updated_data(await self.api.async_refresh())

    def _apply_command_status(self, command: dict[str, Any]) -> None:
        """Overlay a command status onto cached coordinator vehicle data."""
        vin = command.get("vin")
        status = command.get("status")
        if not vin or not status or not self.data:
            return

        vehicles = []
        changed = False
        for vehicle in self.vehicles:
            if vehicle.get("vin") != vin:
                vehicles.append(vehicle)
                continue

            updated = dict(vehicle)
            updated["command_status"] = status
            vehicles.append(updated)
            changed = True

        if not changed:
            return

        data = dict(self.data)
        data["vehicles"] = vehicles
        self.async_set_updated_data(data)
