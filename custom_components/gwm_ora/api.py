"""Client for the local GWM add-on API."""

from __future__ import annotations

from typing import Any

import aiohttp


class GwmOraApiError(Exception):
    """Base error for GWM add-on API failures."""


class GwmOraApiAuthError(GwmOraApiError):
    """Raised when the add-on rejects the stored API token."""


class GwmOraApiForbidden(GwmOraApiError):
    """Raised when the add-on rejects an otherwise authenticated request."""


class GwmOraApiUnavailable(GwmOraApiError):
    """Raised when the add-on cannot be reached."""


class GwmOraApiClient:
    """Small async client for the add-on's internal HTTP API."""

    def __init__(
        self,
        session: aiohttp.ClientSession,
        host: str,
        port: int,
        token: str,
    ) -> None:
        self._session = session
        self._base_url = f"http://{host}:{port}/api/v1"
        self._token = token

    async def async_health(self) -> dict[str, Any]:
        """Return add-on health."""
        return await self._request("GET", "/health")

    async def async_get_vehicles(self) -> dict[str, Any]:
        """Return cached vehicle snapshots."""
        return await self._request("GET", "/vehicles")

    async def async_refresh(self) -> dict[str, Any]:
        """Ask the add-on to refresh immediately."""
        return await self._request("POST", "/refresh")

    async def async_get_command(self, command_id: str) -> dict[str, Any]:
        """Return a remote command status."""
        return await self._request("GET", f"/commands/{command_id}")

    async def async_set_climate(
        self,
        vin: str,
        *,
        mode: str | None = None,
        temperature: int | None = None,
        operation_time_minutes: int | None = None,
    ) -> dict[str, Any]:
        """Queue a climate command."""
        payload: dict[str, Any] = {}
        if mode is not None:
            payload["mode"] = mode
        if temperature is not None:
            payload["temperature"] = temperature
        if operation_time_minutes is not None:
            payload["operation_time_minutes"] = operation_time_minutes
        return await self._request(
            "POST",
            f"/vehicles/{vin}/commands/climate",
            json=payload,
        )

    async def async_lock(self, vin: str, action: str) -> dict[str, Any]:
        """Queue a door lock command."""
        return await self._request(
            "POST",
            f"/vehicles/{vin}/commands/lock",
            json={"action": action},
        )

    async def async_close_windows(self, vin: str) -> dict[str, Any]:
        """Queue a close-windows command."""
        return await self._request(
            "POST",
            f"/vehicles/{vin}/commands/windows/close",
            json={},
        )

    async def async_vehicle_control(
        self,
        vin: str,
        action: str,
        *,
        run_time_minutes: int | None = None,
    ) -> dict[str, Any]:
        """Queue an experimental China vehicle-control command."""
        payload: dict[str, Any] = {"action": action}
        if run_time_minutes is not None:
            payload["run_time_minutes"] = run_time_minutes
        return await self._request(
            "POST",
            f"/vehicles/{vin}/commands/control",
            json=payload,
        )

    async def async_get_charging_plan(self, vin: str) -> dict[str, Any]:
        """Return the vehicle's charging schedule."""
        return await self._request("GET", f"/vehicles/{vin}/charging/plan")

    async def async_get_charging_mode(self, vin: str) -> dict[str, Any]:
        """Return BeanTech smart scheduled charging state and its time window."""
        return await self._request("GET", f"/vehicles/{vin}/charging/mode")

    async def async_set_charging_mode(self, vin: str, *, enable: bool) -> dict[str, Any]:
        """Turn BeanTech smart scheduled charging on or off."""
        return await self._request(
            "POST",
            f"/vehicles/{vin}/charging/mode",
            json={"enable": enable},
        )

    async def async_get_remote_records(
        self, vin: str, *, page: int = 1, size: int = 20
    ) -> dict[str, Any]:
        """Return a page of BeanTech remote control records."""
        return await self._request(
            "GET",
            f"/vehicles/{vin}/remote-records",
            params={"page": page, "size": size},
        )

    async def async_set_charging_plan(
        self,
        vin: str,
        *,
        enable: bool,
        start_time: int | None = None,
        end_time: int | None = None,
        plan_type: int | None = None,
        weeks: str | None = None,
    ) -> dict[str, Any]:
        """Set or clear the charging schedule. Times are Unix milliseconds."""
        payload: dict[str, Any] = {"enable": enable}
        if start_time is not None:
            payload["start_time"] = start_time
        if end_time is not None:
            payload["end_time"] = end_time
        if plan_type is not None:
            payload["plan_type"] = plan_type
        if weeks is not None:
            payload["weeks"] = weeks
        return await self._request(
            "POST",
            f"/vehicles/{vin}/charging/plan",
            json=payload,
        )

    async def _request(
        self,
        method: str,
        path: str,
        **kwargs: Any,
    ) -> dict[str, Any]:
        headers = kwargs.pop("headers", {})
        headers["Authorization"] = f"Bearer {self._token}"

        try:
            async with self._session.request(
                method,
                f"{self._base_url}{path}",
                headers=headers,
                **kwargs,
            ) as response:
                if response.status == 401:
                    raise GwmOraApiAuthError("Add-on API token was rejected")
                if response.status == 403:
                    raise GwmOraApiForbidden(await response.text())
                if response.status >= 400:
                    raise GwmOraApiError(await response.text())
                return await response.json()
        except aiohttp.ClientError as err:
            raise GwmOraApiUnavailable(str(err)) from err
