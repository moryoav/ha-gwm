"""Config flow tests for the GWM integration."""

from __future__ import annotations

from typing import Any

import pytest

pytest.importorskip("pytest_asyncio")


def _set_flow_hass(flow: Any) -> None:
    try:
        flow.hass = object()
    except AttributeError:
        flow._hass = object()


@pytest.mark.asyncio
async def test_validate_success(monkeypatch: pytest.MonkeyPatch) -> None:
    pytest.importorskip("homeassistant")
    from homeassistant.const import CONF_HOST, CONF_PORT

    from custom_components.gwm_ora import config_flow
    from custom_components.gwm_ora.const import CONF_TOKEN

    class Client:
        async def async_health(self) -> dict[str, Any]:
            return {"status": "ok"}

    monkeypatch.setattr(config_flow, "async_get_clientsession", lambda hass: object())
    monkeypatch.setattr(config_flow, "GwmOraApiClient", lambda *args: Client())

    flow = config_flow.GwmOraConfigFlow()
    _set_flow_hass(flow)

    assert await flow._async_validate({CONF_HOST: "addon", CONF_PORT: 8099, CONF_TOKEN: "token"}) == ""


@pytest.mark.asyncio
@pytest.mark.parametrize(
    ("error_name", "expected"),
    [
        ("GwmOraApiAuthError", "invalid_auth"),
        ("GwmOraApiUnavailable", "cannot_connect"),
        ("GwmOraApiError", "cannot_connect"),
    ],
)
async def test_validate_errors(
    monkeypatch: pytest.MonkeyPatch,
    error_name: str,
    expected: str,
) -> None:
    pytest.importorskip("homeassistant")
    from homeassistant.const import CONF_HOST, CONF_PORT

    from custom_components.gwm_ora import config_flow
    from custom_components.gwm_ora.const import CONF_TOKEN

    error_cls = getattr(config_flow, error_name)

    class Client:
        async def async_health(self) -> dict[str, Any]:
            raise error_cls("boom")

    monkeypatch.setattr(config_flow, "async_get_clientsession", lambda hass: object())
    monkeypatch.setattr(config_flow, "GwmOraApiClient", lambda *args: Client())

    flow = config_flow.GwmOraConfigFlow()
    _set_flow_hass(flow)

    assert await flow._async_validate({CONF_HOST: "addon", CONF_PORT: 8099, CONF_TOKEN: "token"}) == expected
