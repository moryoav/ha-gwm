"""Tests for the local add-on API client."""

from __future__ import annotations

from unittest.mock import AsyncMock

import pytest

pytest.importorskip("homeassistant")

from custom_components.gwm_ora.api import GwmOraApiClient


@pytest.mark.asyncio
async def test_set_climate_includes_operation_time() -> None:
    client = GwmOraApiClient(AsyncMock(), "addon", 8099, "token")
    client._request = AsyncMock(return_value={"id": "command"})

    result = await client.async_set_climate("VIN123", operation_time_minutes=15)

    assert result == {"id": "command"}
    client._request.assert_awaited_once_with(
        "POST",
        "/vehicles/VIN123/commands/climate",
        json={"operation_time_minutes": 15},
    )


@pytest.mark.asyncio
async def test_set_climate_omits_unspecified_values() -> None:
    client = GwmOraApiClient(AsyncMock(), "addon", 8099, "token")
    client._request = AsyncMock(return_value={"id": "command"})

    await client.async_set_climate("VIN123", mode="cool")

    client._request.assert_awaited_once_with(
        "POST",
        "/vehicles/VIN123/commands/climate",
        json={"mode": "cool"},
    )
