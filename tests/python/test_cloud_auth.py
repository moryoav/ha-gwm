"""Offline tests for the Home Assistant direct-cloud authentication adapter."""

from __future__ import annotations

from typing import Any

import pytest

pytest.importorskip("homeassistant")

from custom_components.gwm_ora.cloud_auth import (
    DirectCloudAuthenticator,
    DirectCloudCredentials,
    _load_bootstrap_material,
    direct_entry_data,
    direct_entry_title,
    direct_unique_id,
)
from custom_components.gwm_ora.const import (
    CONF_ACCOUNT,
    CONF_CONNECTION_TYPE,
    CONF_COUNTRY,
    CONF_PASSWORD,
    CONF_REGION,
    CONNECTION_TYPE_CLOUD,
)
from gwm_ora_client import (
    ChinaClientConfig,
    EuBootstrapMaterial,
    GwmClientConfig,
    GwmNetworkError,
    RussiaBootstrapMaterial,
)

_DEVICE_ID = "0123456789abcdef0123456789abcdef"


@pytest.mark.parametrize(
    ("region", "country", "account", "password", "expected_country"),
    [
        ("eu", " de ", " account@example.invalid ", "password", "DE"),
        ("aus", "nz", " account@example.invalid ", "password", "NZ"),
        ("rus", "ru", " synthetic-account ", "password", "RU"),
        ("cn", "ignored", "synthetic-cn-account", None, "CN"),
    ],
)
def test_direct_credentials_normalize_without_secret_repr(
    region: str,
    country: str,
    account: str,
    password: str | None,
    expected_country: str,
) -> None:
    credentials = DirectCloudCredentials(region, country, account, password, _DEVICE_ID)

    assert credentials.country == expected_country
    assert credentials.account == account.strip()
    assert repr(credentials).startswith("<custom_components.gwm_ora.cloud_auth.DirectCloudCredentials")
    assert account.strip() not in repr(credentials)
    assert password is None or password not in repr(credentials)
    assert len(credentials.account_binding) == 64


def test_entry_contract_has_pseudonymous_unique_id_and_no_transient_state() -> None:
    credentials = DirectCloudCredentials(
        "eu",
        "DE",
        "account@example.invalid",
        "password",
        _DEVICE_ID,
    )

    data = direct_entry_data(credentials)
    unique_id = direct_unique_id(credentials)

    assert data == {
        CONF_CONNECTION_TYPE: CONNECTION_TYPE_CLOUD,
        CONF_REGION: "eu",
        CONF_COUNTRY: "DE",
        CONF_ACCOUNT: "account@example.invalid",
        CONF_PASSWORD: "password",
    }
    assert "account@example.invalid" not in unique_id
    assert "password" not in unique_id
    assert _DEVICE_ID not in unique_id
    assert set(data).isdisjoint(
        {
            "access_token",
            "refresh_token",
            "verification_code",
            "certificate",
            "private_key",
            "device_id",
        }
    )
    assert direct_entry_title("eu") == "GWM Europe"


def test_bundled_bootstrap_loader_is_region_scoped_and_offline() -> None:
    assert isinstance(_load_bootstrap_material("eu"), EuBootstrapMaterial)
    assert _load_bootstrap_material("aus") is None
    assert isinstance(_load_bootstrap_material("rus"), RussiaBootstrapMaterial)


class _OverseasClient:
    def __init__(self, config: GwmClientConfig, result: object) -> None:
        self.config = config
        self.result = result
        self.calls: list[tuple[str, dict[str, Any]]] = []
        self.closed = False

    async def authenticate_eu(self, credentials: object, **kwargs: Any) -> object:
        self.calls.append(("eu", kwargs))
        return self.result

    async def authenticate_anz(self, credentials: object, **kwargs: Any) -> object:
        self.calls.append(("aus", kwargs))
        return self.result

    async def authenticate_russia(self, credentials: object, **kwargs: Any) -> object:
        self.calls.append(("rus", kwargs))
        return self.result

    async def aclose(self) -> None:
        self.closed = True


class _ChinaClient:
    def __init__(self, config: ChinaClientConfig, result: object) -> None:
        self.config = config
        self.result = result
        self.calls: list[dict[str, Any]] = []
        self.closed = False

    async def authenticate(self, credentials: object, **kwargs: Any) -> object:
        self.calls.append(kwargs)
        return self.result

    async def aclose(self) -> None:
        self.closed = True


@pytest.mark.asyncio
@pytest.mark.parametrize(
    ("region", "country", "account", "password"),
    [
        ("eu", "DE", "account@example.invalid", "password"),
        ("aus", "AU", "account@example.invalid", "password"),
        ("rus", "RU", "synthetic-account", "password"),
    ],
)
async def test_overseas_attempt_dispatches_and_always_closes(
    region: str,
    country: str,
    account: str,
    password: str,
) -> None:
    marker = object()
    clients: list[_OverseasClient] = []

    def factory(config: GwmClientConfig) -> Any:
        client = _OverseasClient(config, marker)
        clients.append(client)
        return client

    authenticator = DirectCloudAuthenticator(overseas_client_factory=factory)
    credentials = DirectCloudCredentials(region, country, account, password, _DEVICE_ID)

    result = await authenticator.async_authenticate(
        credentials,
        verification_code="123456",
        allow_session_reclaim=region == "aus",
    )

    assert result is marker
    assert clients[0].config.region.value == region
    assert clients[0].calls[0][0] == region
    assert clients[0].calls[0][1]["verification_code"] == "123456"
    assert clients[0].closed


@pytest.mark.asyncio
async def test_china_attempt_dispatches_without_loading_overseas_material() -> None:
    marker = object()
    clients: list[_ChinaClient] = []

    def factory(config: ChinaClientConfig) -> Any:
        client = _ChinaClient(config, marker)
        clients.append(client)
        return client

    def forbidden_loader(region: str) -> None:
        pytest.fail(f"overseas resources loaded for {region}")

    authenticator = DirectCloudAuthenticator(
        china_client_factory=factory,
        resource_loader=forbidden_loader,
    )
    credentials = DirectCloudCredentials(
        "cn",
        "CN",
        "synthetic-cn-account",
        None,
        _DEVICE_ID,
    )

    result = await authenticator.async_authenticate(credentials, verification_code="123456")

    assert result is marker
    assert clients[0].calls[0]["verification_code"] == "123456"
    assert clients[0].closed


@pytest.mark.asyncio
async def test_client_is_closed_when_authentication_fails() -> None:
    clients: list[_OverseasClient] = []

    class FailingClient(_OverseasClient):
        async def authenticate_anz(self, credentials: object, **kwargs: Any) -> object:
            raise GwmNetworkError(operation="login")

    def factory(config: GwmClientConfig) -> Any:
        client = FailingClient(config, object())
        clients.append(client)
        return client

    authenticator = DirectCloudAuthenticator(overseas_client_factory=factory)
    credentials = DirectCloudCredentials(
        "aus",
        "AU",
        "account@example.invalid",
        "password",
        _DEVICE_ID,
    )

    with pytest.raises(GwmNetworkError):
        await authenticator.async_authenticate(credentials)
    assert clients[0].closed
