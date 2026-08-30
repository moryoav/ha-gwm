"""Charging-service validation tests."""

from __future__ import annotations

from datetime import datetime, timedelta
from types import SimpleNamespace
from zoneinfo import ZoneInfo

import pytest

pytest.importorskip("homeassistant")

from homeassistant.core import HomeAssistant
from homeassistant.exceptions import ServiceValidationError

from custom_components.gwm_ora import _async_register_services, _charging_window_epoch_ms
from custom_components.gwm_ora.const import (
    ATTR_VIN,
    CONF_CONNECTION_TYPE,
    CONNECTION_TYPE_CLOUD,
    DOMAIN,
    SERVICE_CLEAR_CHARGING_PLAN,
)
from custom_components.gwm_ora.switch import _charging_plan_is_active

_UTC = ZoneInfo("UTC")


def test_charging_window_converts_aware_datetimes_to_epoch_ms() -> None:
    start = datetime(2026, 8, 22, 10, 0, tzinfo=_UTC)
    end = start + timedelta(minutes=5)

    start_ms, end_ms = _charging_window_epoch_ms(start, end)

    assert end_ms - start_ms == 300_000


def test_charging_window_rejects_less_than_five_minutes() -> None:
    start = datetime(2026, 8, 22, 10, 0, tzinfo=_UTC)

    with pytest.raises(ServiceValidationError, match="at least 5 minutes"):
        _charging_window_epoch_ms(start, start + timedelta(minutes=4, seconds=59))


@pytest.mark.parametrize(
    ("response", "expected"),
    [
        ({"charge_plan_list": []}, False),
        ({"charge_plan_list": [{"plan_type": "-1"}]}, False),
        ({"charge_plan_list": [{"plan_type": "0"}]}, True),
        ({"charge_plan_list": [{"plan_type": 0}]}, True),
    ],
)
def test_charging_plan_active_state(response: dict, expected: bool) -> None:
    assert _charging_plan_is_active(response) is expected


@pytest.mark.asyncio
async def test_charging_service_resolves_a_direct_cloud_vehicle(tmp_path) -> None:
    calls: list[tuple[str, bool]] = []

    class Api:
        async def async_set_charging_plan(
            self,
            vin: str,
            *,
            enable: bool,
        ) -> dict[str, object]:
            calls.append((vin, enable))
            return {}

    class Coordinator:
        def resolve_vehicle(self, identifier: str) -> dict[str, object] | None:
            if identifier == "DISPLAY-VIN":
                return {
                    "vin": "LGWTEST0000000001",
                    "capabilities": {"charging_control": True},
                }
            return None

        def set_charging_plan_active(self, vin: str, active: bool) -> None:
            calls.append((vin, active))

    entry = SimpleNamespace(
        data={CONF_CONNECTION_TYPE: CONNECTION_TYPE_CLOUD},
        runtime_data=SimpleNamespace(api=Api(), coordinator=Coordinator()),
    )
    hass = HomeAssistant(str(tmp_path))
    hass.config_entries = SimpleNamespace(  # type: ignore[assignment]
        async_loaded_entries=lambda domain: [entry] if domain == DOMAIN else []
    )
    _async_register_services(hass)

    await hass.services.async_call(
        DOMAIN,
        SERVICE_CLEAR_CHARGING_PLAN,
        {ATTR_VIN: "DISPLAY-VIN"},
        blocking=True,
    )

    assert calls == [
        ("LGWTEST0000000001", False),
        ("LGWTEST0000000001", False),
    ]
