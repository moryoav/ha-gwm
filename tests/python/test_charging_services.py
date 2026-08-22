"""Charging-service validation tests."""

from __future__ import annotations

from datetime import datetime, timedelta
from zoneinfo import ZoneInfo

import pytest

pytest.importorskip("homeassistant")

from homeassistant.exceptions import ServiceValidationError

from custom_components.gwm_ora import _charging_window_epoch_ms
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
