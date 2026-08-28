"""Config flow tests for the GWM integration."""

from __future__ import annotations

import ssl
from dataclasses import replace
from types import MappingProxyType
from typing import Any

import pytest

pytest.importorskip("pytest_asyncio")
pytest.importorskip("homeassistant")

from homeassistant.config_entries import ConfigEntry, ConfigFlowResult
from homeassistant.const import CONF_HOST, CONF_PORT
from homeassistant.data_entry_flow import FlowResultType

from custom_components.gwm_ora import config_flow
from custom_components.gwm_ora.cloud_auth import direct_unique_id
from custom_components.gwm_ora.cloud_runtime import consume_direct_cloud_bootstrap
from custom_components.gwm_ora.const import (
    CONF_ACCOUNT,
    CONF_ALLOW_SESSION_RECLAIM,
    CONF_CONNECTION_TYPE,
    CONF_COUNTRY,
    CONF_ENABLE_CHARGING_CONTROL,
    CONF_ENABLE_REMOTE_COMMANDS,
    CONF_LOG_LEVEL,
    CONF_PASSWORD,
    CONF_POLL_INTERVAL_SECONDS,
    CONF_REGION,
    CONF_SECURITY_PIN,
    CONF_TOKEN,
    CONF_VERIFICATION_CODE,
    CONNECTION_TYPE_ADDON,
    CONNECTION_TYPE_CLOUD,
    DOMAIN,
)
from gwm_ora_client import (
    AnzAuthenticated,
    AnzAuthState,
    AnzCredentials,
    AnzSessionReclaimRequired,
    ChinaAuthState,
    ChinaRiskControlRequired,
    EuAuthenticated,
    EuAuthState,
    EuCredentials,
    EuVerificationRequired,
    GwmApiError,
    GwmAuthenticationError,
    GwmConfigurationError,
    GwmNetworkError,
    GwmRateLimitError,
    GwmSession,
    RussiaAuthenticated,
    RussiaAuthState,
    RussiaCredentials,
)

_DEVICE_ID = "0123456789abcdef0123456789abcdef"


@pytest.fixture(autouse=True)
def _memory_direct_state_store(monkeypatch: pytest.MonkeyPatch) -> None:
    """Keep config-flow unit tests independent of Home Assistant disk I/O."""

    stores: dict[str, object] = {}

    class StateStore:
        def __init__(self) -> None:
            self.saved: list[tuple[object, object]] = []
            self.loaded: object | None = None

        async def async_load_auth_state(self, data: object) -> object | None:
            assert data is not None
            return self.loaded

        async def async_save_auth_state(
            self,
            credentials: object,
            state: object,
        ) -> None:
            self.saved.append((credentials, state))

        async def async_clear_auth_state(self, data: object) -> None:
            assert data is not None

    def get_store(hass: object, unique_id: str) -> StateStore:
        assert hass is not None
        if hasattr(hass, "data"):
            hass.data["_test_direct_state_stores"] = stores  # type: ignore[attr-defined]
        store = stores.setdefault(unique_id, StateStore())
        assert isinstance(store, StateStore)
        return store

    async def remove_store(hass: object, unique_id: str | None) -> None:
        assert hass is not None
        if hasattr(hass, "data"):
            hass.data.setdefault("_test_removed_direct_states", []).append(  # type: ignore[attr-defined]
                unique_id
            )
        if unique_id is not None:
            stores.pop(unique_id, None)

    monkeypatch.setattr(config_flow, "direct_cloud_state_store", get_store)
    monkeypatch.setattr(config_flow, "async_remove_direct_cloud_state", remove_store)


def _set_flow_hass(flow: Any) -> None:
    try:
        flow.hass = object()
    except AttributeError:
        flow._hass = object()


def _authenticated(credentials: config_flow.DirectCloudCredentials) -> object:
    """Build a valid regional authenticated result without network access."""

    regional = credentials.client_credentials()
    context = ssl.create_default_context()
    if isinstance(regional, EuCredentials):
        state = replace(
            EuAuthState.for_credentials(regional),
            access_token="synthetic-access-token",
        )
        return EuAuthenticated(
            state,
            GwmSession(
                regional.country,
                regional.device_id,
                "synthetic-access-token",
                context,
            ),
        )
    if isinstance(regional, AnzCredentials):
        state = replace(
            AnzAuthState.for_credentials(regional),
            access_token="synthetic-access-token",
        )
        return AnzAuthenticated(
            state,
            GwmSession(
                regional.country,
                regional.device_id,
                "synthetic-access-token",
                context,
            ),
        )
    if isinstance(regional, RussiaCredentials):
        state = replace(
            RussiaAuthState.for_credentials(regional),
            access_token="synthetic-access-token",
        )
        return RussiaAuthenticated(
            state,
            GwmSession(
                regional.country,
                regional.device_id,
                "synthetic-access-token",
                context,
            ),
        )
    raise AssertionError("unexpected credentials")


class _ConfigEntries:
    def __init__(self, entry: ConfigEntry | None = None) -> None:
        self.entry = entry

    def async_get_known_entry(self, entry_id: str) -> ConfigEntry:
        assert self.entry is not None and self.entry.entry_id == entry_id
        return self.entry

    def async_entry_for_domain_unique_id(
        self,
        domain: str,
        unique_id: str,
    ) -> ConfigEntry | None:
        assert domain == DOMAIN
        return None


class _Hass:
    def __init__(self, entry: ConfigEntry | None = None) -> None:
        self.config_entries = _ConfigEntries(entry)
        self.data: dict[str, Any] = {}


def _entry(
    *,
    data: dict[str, Any],
    options: dict[str, Any] | None = None,
    entry_id: str = "synthetic-entry",
) -> ConfigEntry:
    unique_id = "synthetic-unique-id"
    if all(key in data for key in (CONF_REGION, CONF_COUNTRY, CONF_ACCOUNT, CONF_PASSWORD)):
        credentials = config_flow.DirectCloudCredentials(
            str(data[CONF_REGION]),
            str(data[CONF_COUNTRY]),
            str(data[CONF_ACCOUNT]),
            str(data[CONF_PASSWORD]),
            _DEVICE_ID,
        )
        unique_id = direct_unique_id(credentials)
    return ConfigEntry(
        data=data,
        discovery_keys=MappingProxyType({}),
        domain=DOMAIN,
        entry_id=entry_id,
        minor_version=1,
        options=options or {},
        source="user",
        subentries_data=None,
        title="GWM test",
        unique_id=unique_id,
        version=1,
    )


def _prepare_user_flow(
    monkeypatch: pytest.MonkeyPatch,
    authenticator: object,
) -> config_flow.GwmOraConfigFlow:
    flow = config_flow.GwmOraConfigFlow()
    flow.hass = _Hass()  # type: ignore[assignment]
    flow.context = {}
    flow._cloud_authenticator = authenticator  # type: ignore[assignment]

    async def set_unique_id(unique_id: str) -> None:
        flow.context["unique_id"] = unique_id

    monkeypatch.setattr(flow, "async_set_unique_id", set_unique_id)
    monkeypatch.setattr(flow, "_abort_if_unique_id_configured", lambda **kwargs: None)
    return flow


@pytest.mark.asyncio
async def test_validate_success(monkeypatch: pytest.MonkeyPatch) -> None:
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
    error_cls = getattr(config_flow, error_name)

    class Client:
        async def async_health(self) -> dict[str, Any]:
            raise error_cls("boom")

    monkeypatch.setattr(config_flow, "async_get_clientsession", lambda hass: object())
    monkeypatch.setattr(config_flow, "GwmOraApiClient", lambda *args: Client())

    flow = config_flow.GwmOraConfigFlow()
    _set_flow_hass(flow)

    assert await flow._async_validate({CONF_HOST: "addon", CONF_PORT: 8099, CONF_TOKEN: "token"}) == expected


@pytest.mark.asyncio
async def test_user_eu_verification_creates_config_only_entry(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    class Authenticator:
        def __init__(self) -> None:
            self.calls: list[dict[str, Any]] = []

        async def async_authenticate(
            self,
            credentials: config_flow.DirectCloudCredentials,
            **kwargs: Any,
        ) -> object:
            self.calls.append(kwargs)
            regional = credentials.client_credentials()
            assert isinstance(regional, EuCredentials)
            if kwargs["verification_code"] is None:
                return EuVerificationRequired(
                    EuAuthState.for_credentials(regional),
                    code_requested=True,
                )
            return _authenticated(credentials)

    authenticator = Authenticator()
    flow = _prepare_user_flow(monkeypatch, authenticator)

    menu = await flow.async_step_user()
    assert menu["type"] is FlowResultType.MENU
    account = await flow.async_step_cloud({CONF_REGION: "eu"})
    assert account["step_id"] == "account"
    verification = await flow.async_step_account(
        {
            CONF_COUNTRY: "DE",
            CONF_ACCOUNT: "account@example.invalid",
            CONF_PASSWORD: "password",
        }
    )
    assert verification["step_id"] == "verification"

    result = await flow.async_step_verification({CONF_VERIFICATION_CODE: "123456"})

    assert result["type"] is FlowResultType.CREATE_ENTRY
    assert result["title"] == "GWM Europe"
    assert result["data"] == {
        CONF_CONNECTION_TYPE: CONNECTION_TYPE_CLOUD,
        CONF_REGION: "eu",
        CONF_COUNTRY: "DE",
        CONF_ACCOUNT: "account@example.invalid",
        CONF_PASSWORD: "password",
    }
    assert flow.context["unique_id"].startswith("cloud:eu:")
    assert consume_direct_cloud_bootstrap(
        flow.hass,
        flow.context["unique_id"],
    ) is not None
    stores = flow.hass.data["_test_direct_state_stores"]
    assert len(stores[flow.context["unique_id"]].saved) == 2
    assert authenticator.calls[1]["verification_code"] == "123456"
    assert set(result["data"]).isdisjoint(
        {"access_token", "device_id", "verification_code", "certificate", "private_key"}
    )


@pytest.mark.asyncio
async def test_anz_reclaim_requires_explicit_unchecked_consent(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    class Authenticator:
        def __init__(self) -> None:
            self.calls: list[bool] = []

        async def async_authenticate(
            self,
            credentials: config_flow.DirectCloudCredentials,
            **kwargs: Any,
        ) -> object:
            allow = kwargs["allow_session_reclaim"]
            self.calls.append(allow)
            regional = credentials.client_credentials()
            assert isinstance(regional, AnzCredentials)
            if not allow:
                state = replace(
                    AnzAuthState.for_credentials(regional),
                    session_reclaim_required=True,
                )
                return AnzSessionReclaimRequired(state)
            return _authenticated(credentials)

    authenticator = Authenticator()
    flow = _prepare_user_flow(monkeypatch, authenticator)
    await flow.async_step_cloud({CONF_REGION: "aus"})
    reclaim = await flow.async_step_account(
        {
            CONF_COUNTRY: "NZ",
            CONF_ACCOUNT: "account@example.invalid",
            CONF_PASSWORD: "password",
        }
    )
    assert reclaim["step_id"] == "session_reclaim"
    assert reclaim["data_schema"]({})[CONF_ALLOW_SESSION_RECLAIM] is False

    rejected = await flow.async_step_session_reclaim({CONF_ALLOW_SESSION_RECLAIM: False})
    assert rejected["errors"] == {
        CONF_ALLOW_SESSION_RECLAIM: "session_reclaim_not_confirmed"
    }
    assert authenticator.calls == [False]

    result = await flow.async_step_session_reclaim({CONF_ALLOW_SESSION_RECLAIM: True})
    assert result["type"] is FlowResultType.CREATE_ENTRY
    assert authenticator.calls == [False, True]
    assert consume_direct_cloud_bootstrap(flow.hass, flow.context["unique_id"]) is not None


@pytest.mark.asyncio
async def test_russia_authentication_can_complete_without_verification(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    class Authenticator:
        async def async_authenticate(
            self,
            credentials: config_flow.DirectCloudCredentials,
            **kwargs: Any,
        ) -> object:
            return _authenticated(credentials)

    flow = _prepare_user_flow(monkeypatch, Authenticator())
    await flow.async_step_cloud({CONF_REGION: "rus"})
    result = await flow.async_step_account(
        {CONF_ACCOUNT: "synthetic-account", CONF_PASSWORD: "password"}
    )

    assert result["type"] is FlowResultType.CREATE_ENTRY
    assert result["data"][CONF_COUNTRY] == "RU"
    assert result["title"] == "GWM Russia"
    assert consume_direct_cloud_bootstrap(flow.hass, flow.context["unique_id"]) is not None


@pytest.mark.asyncio
async def test_restarted_flow_resumes_persisted_device_bound_continuation(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    persisted_device = "f" * 32
    persisted_credentials = config_flow.DirectCloudCredentials(
        "aus",
        "AU",
        "account@example.invalid",
        "password",
        persisted_device,
    )
    regional = persisted_credentials.client_credentials()
    assert isinstance(regional, AnzCredentials)
    persisted_state = replace(
        AnzAuthState.for_credentials(regional),
        access_token="persisted-access-token",
    )

    class Authenticator:
        async def async_authenticate(
            self,
            credentials: config_flow.DirectCloudCredentials,
            **kwargs: Any,
        ) -> object:
            assert credentials.device_id == persisted_device
            assert kwargs["state"] == persisted_state
            return _authenticated(credentials)

    flow = _prepare_user_flow(monkeypatch, Authenticator())
    unique_id = direct_unique_id(persisted_credentials)
    store = config_flow.direct_cloud_state_store(flow.hass, unique_id)
    store.loaded = persisted_state

    await flow.async_step_cloud({CONF_REGION: "aus"})
    result = await flow.async_step_account(
        {
            CONF_COUNTRY: "AU",
            CONF_ACCOUNT: "account@example.invalid",
            CONF_PASSWORD: "password",
        }
    )

    assert result["type"] is FlowResultType.CREATE_ENTRY
    assert flow.context["unique_id"] == unique_id


@pytest.mark.asyncio
async def test_china_is_gated_but_risk_result_has_finite_internal_route() -> None:
    flow = config_flow.GwmOraConfigFlow()
    flow.hass = _Hass()  # type: ignore[assignment]
    gated = await flow.async_step_cloud({CONF_REGION: "cn"})
    assert gated["type"] is FlowResultType.ABORT
    assert gated["reason"] == "china_live_validation_required"

    credentials = config_flow.DirectCloudCredentials(
        "cn",
        "CN",
        "synthetic-cn-account",
        None,
        _DEVICE_ID,
    )
    regional = credentials.client_credentials()
    state = ChinaAuthState.for_credentials(regional)
    flow._direct_credentials = credentials
    result = await flow._async_route_authentication(ChinaRiskControlRequired(state))
    assert result["type"] is FlowResultType.ABORT
    assert result["reason"] == "risk_control_required"
    assert flow._auth_state is None


@pytest.mark.asyncio
@pytest.mark.parametrize(
    ("error", "verification_code", "expected"),
    [
        (GwmRateLimitError(operation="login"), None, "rate_limited"),
        (GwmAuthenticationError(operation="login"), None, "invalid_auth"),
        (
            GwmAuthenticationError(operation="verify_code"),
            "123456",
            "invalid_verification_code",
        ),
        (GwmNetworkError(operation="login"), None, "cannot_connect"),
        (GwmConfigurationError(operation="login"), None, "local_configuration_error"),
        (GwmApiError(operation="login", api_code="999999"), None, "service_error"),
    ],
)
async def test_direct_error_taxonomy(
    error: Exception,
    verification_code: str | None,
    expected: str,
) -> None:
    class Authenticator:
        async def async_authenticate(self, *args: Any, **kwargs: Any) -> object:
            raise error

    flow = config_flow.GwmOraConfigFlow()
    flow._cloud_authenticator = Authenticator()  # type: ignore[assignment]
    flow._direct_credentials = config_flow.DirectCloudCredentials(
        "eu",
        "DE",
        "account@example.invalid",
        "password",
        _DEVICE_ID,
    )

    result, mapped = await flow._async_authenticate(verification_code=verification_code)

    assert result is None
    assert mapped == expected


@pytest.mark.asyncio
async def test_direct_reauth_updates_password_only_after_authentication(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    entry = _entry(
        data={
            CONF_CONNECTION_TYPE: CONNECTION_TYPE_CLOUD,
            CONF_REGION: "eu",
            CONF_COUNTRY: "DE",
            CONF_ACCOUNT: "account@example.invalid",
            CONF_PASSWORD: "old-password",
        }
    )
    updates: list[dict[str, Any]] = []

    class Authenticator:
        async def async_authenticate(
            self,
            credentials: config_flow.DirectCloudCredentials,
            **kwargs: Any,
        ) -> object:
            assert credentials.password == "new-password"
            return _authenticated(credentials)

    flow = config_flow.GwmOraConfigFlow()
    flow.hass = _Hass(entry)  # type: ignore[assignment]
    flow.context = {"source": "reauth", "entry_id": entry.entry_id}
    flow._cloud_authenticator = Authenticator()  # type: ignore[assignment]

    def update_and_abort(
        updated_entry: ConfigEntry,
        **kwargs: Any,
    ) -> ConfigFlowResult:
        assert updated_entry is entry
        updates.append(kwargs)
        return flow.async_abort(reason="reauth_successful")

    monkeypatch.setattr(flow, "async_update_reload_and_abort", update_and_abort)
    await flow.async_step_reauth(dict(entry.data))
    result = await flow.async_step_reauth_confirm({CONF_PASSWORD: "new-password"})

    assert result["reason"] == "reauth_successful"
    assert updates[0]["data"][CONF_PASSWORD] == "new-password"
    assert updates[0]["data"][CONF_ACCOUNT] == entry.data[CONF_ACCOUNT]
    assert consume_direct_cloud_bootstrap(flow.hass, entry.unique_id) is not None
    stores = flow.hass.data["_test_direct_state_stores"]
    assert len(stores[entry.unique_id].saved) == 1


@pytest.mark.asyncio
async def test_direct_reconfigure_authenticates_before_replacing_entry(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    entry = _entry(
        data={
            CONF_CONNECTION_TYPE: CONNECTION_TYPE_CLOUD,
            CONF_REGION: "eu",
            CONF_COUNTRY: "DE",
            CONF_ACCOUNT: "old@example.invalid",
            CONF_PASSWORD: "old-password",
        }
    )
    updates: list[dict[str, Any]] = []

    class Authenticator:
        async def async_authenticate(
            self,
            credentials: config_flow.DirectCloudCredentials,
            **kwargs: Any,
        ) -> object:
            assert credentials.region == "rus"
            return _authenticated(credentials)

    flow = config_flow.GwmOraConfigFlow()
    flow.hass = _Hass(entry)  # type: ignore[assignment]
    flow.context = {"source": "reconfigure", "entry_id": entry.entry_id}
    flow._cloud_authenticator = Authenticator()  # type: ignore[assignment]

    def update_and_abort(
        updated_entry: ConfigEntry,
        **kwargs: Any,
    ) -> ConfigFlowResult:
        assert updated_entry is entry
        updates.append(kwargs)
        return flow.async_abort(reason="reconfigure_successful")

    monkeypatch.setattr(flow, "async_update_reload_and_abort", update_and_abort)
    await flow.async_step_reconfigure({CONF_REGION: "rus"})
    result = await flow.async_step_reconfigure_account(
        {CONF_ACCOUNT: "replacement", CONF_PASSWORD: "new-password"}
    )

    assert result["reason"] == "reconfigure_successful"
    assert updates[0]["data"][CONF_REGION] == "rus"
    assert updates[0]["data"][CONF_ACCOUNT] == "replacement"
    assert updates[0]["unique_id"].startswith("cloud:rus:")
    assert consume_direct_cloud_bootstrap(flow.hass, updates[0]["unique_id"]) is not None
    assert flow.hass.data["_test_removed_direct_states"] == [entry.unique_id]


@pytest.mark.asyncio
async def test_direct_options_make_pin_write_only_and_enforce_opt_in() -> None:
    entry = _entry(
        data={CONF_CONNECTION_TYPE: CONNECTION_TYPE_CLOUD, CONF_REGION: "eu"},
        options={CONF_SECURITY_PIN: "existing-pin"},
    )
    flow = config_flow.GwmOraOptionsFlow()
    flow.hass = _Hass(entry)  # type: ignore[assignment]
    flow.handler = entry.entry_id

    form = await flow.async_step_init()
    assert form["data_schema"]({
        CONF_POLL_INTERVAL_SECONDS: 60,
        CONF_ENABLE_REMOTE_COMMANDS: False,
        CONF_ENABLE_CHARGING_CONTROL: False,
        CONF_LOG_LEVEL: "info",
    })[CONF_SECURITY_PIN] == ""

    preserved = await flow.async_step_init(
        {
            CONF_POLL_INTERVAL_SECONDS: 120,
            CONF_ENABLE_REMOTE_COMMANDS: True,
            CONF_ENABLE_CHARGING_CONTROL: False,
            CONF_LOG_LEVEL: "debug",
            CONF_SECURITY_PIN: "",
        }
    )
    assert preserved["type"] is FlowResultType.CREATE_ENTRY
    assert preserved["data"][CONF_SECURITY_PIN] == "existing-pin"

    disabled = await flow.async_step_init(
        {
            CONF_POLL_INTERVAL_SECONDS: 120,
            CONF_ENABLE_REMOTE_COMMANDS: False,
            CONF_ENABLE_CHARGING_CONTROL: False,
            CONF_LOG_LEVEL: "debug",
            CONF_SECURITY_PIN: "unused-pin",
        }
    )
    assert disabled["type"] is FlowResultType.CREATE_ENTRY
    assert CONF_SECURITY_PIN not in disabled["data"]

    new_entry = _entry(
        data={CONF_CONNECTION_TYPE: CONNECTION_TYPE_CLOUD, CONF_REGION: "eu"}
    )
    required_flow = config_flow.GwmOraOptionsFlow()
    required_flow.hass = _Hass(new_entry)  # type: ignore[assignment]
    required_flow.handler = new_entry.entry_id
    required = await required_flow.async_step_init(
        {
            CONF_POLL_INTERVAL_SECONDS: 60,
            CONF_ENABLE_REMOTE_COMMANDS: True,
            CONF_ENABLE_CHARGING_CONTROL: False,
            CONF_LOG_LEVEL: "info",
            CONF_SECURITY_PIN: "",
        }
    )
    assert required["errors"] == {CONF_SECURITY_PIN: "security_pin_required"}


@pytest.mark.asyncio
async def test_addon_options_remain_managed_by_addon() -> None:
    entry = _entry(
        data={CONF_CONNECTION_TYPE: CONNECTION_TYPE_ADDON, CONF_HOST: "addon"}
    )
    flow = config_flow.GwmOraOptionsFlow()
    flow.hass = _Hass(entry)  # type: ignore[assignment]
    flow.handler = entry.entry_id

    result = await flow.async_step_init()

    assert result["type"] is FlowResultType.ABORT
    assert result["reason"] == "addon_options_managed"
