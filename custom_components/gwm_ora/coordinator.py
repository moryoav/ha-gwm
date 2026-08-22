"""Data coordinator for GWM."""

from __future__ import annotations

import asyncio
import logging
import time
from contextlib import suppress
from datetime import timedelta
from typing import Any

from homeassistant.core import HomeAssistant
from homeassistant.exceptions import ConfigEntryAuthFailed
from homeassistant.helpers.update_coordinator import DataUpdateCoordinator, UpdateFailed

from .api import GwmOraApiAuthError, GwmOraApiClient, GwmOraApiError, GwmOraApiUnavailable
from .const import DOMAIN

_LOGGER = logging.getLogger(__name__)


class GwmOraDataUpdateCoordinator(DataUpdateCoordinator[dict[str, Any]]):
    """Coordinator that polls the add-on's cached vehicle data."""

    _TERMINAL_COMMAND_STATES = {"completed", "failed", "timeout", "canceled"}

    def __init__(self, hass: HomeAssistant, api: GwmOraApiClient) -> None:
        super().__init__(
            hass,
            _LOGGER,
            name=DOMAIN,
            update_interval=timedelta(seconds=30),
        )
        self.api = api
        self._command_tasks: dict[str, asyncio.Task[None]] = {}

    async def _async_update_data(self) -> dict[str, Any]:
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

    def vehicle(self, vin: str) -> dict[str, Any] | None:
        """Return one vehicle snapshot by VIN."""
        return next((vehicle for vehicle in self.vehicles if vehicle.get("vin") == vin), None)

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
        deadline = time.monotonic() + 130
        while time.monotonic() < deadline:
            await asyncio.sleep(2)
            try:
                command = await self.api.async_get_command(command_id)
            except (GwmOraApiUnavailable, GwmOraApiError) as err:
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
        with suppress(GwmOraApiUnavailable, GwmOraApiError, GwmOraApiAuthError):
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
