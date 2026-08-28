"""Offline China-only route, gzip, and ambient-state transport tests."""

from __future__ import annotations

import asyncio
import gzip
import json
import ssl
from collections.abc import AsyncIterator, Mapping
from typing import Any, cast
from urllib.parse import quote, unquote, urlsplit

import aiohttp
import pytest
from multidict import CIMultiDict
from yarl import URL

from gwm_ora_client._protocol import _Deadline
from gwm_ora_client.china_crypto import AUTO_AI_CKEY, auto_ai_sign
from gwm_ora_client.china_transport import (
    ChinaAiohttpTransport,
    ChinaTransportCapabilities,
    _ChinaTransportRequest,
)
from gwm_ora_client.errors import (
    GwmClosedError,
    GwmConfigurationError,
    GwmNetworkError,
    GwmProtocolError,
    GwmRedirectError,
    GwmResponseTooLargeError,
    GwmTlsError,
)

SENSITIVE = "SENSITIVE-china-transport-material-019fea1b"
DEVICE_ID = "0123456789abcdef0123456789abcdef"
VIN = "LGWTEST0000000001"


class _FakeContent:
    def __init__(self, chunks: list[object]) -> None:
        self.chunks = chunks

    async def iter_chunked(self, size: int) -> AsyncIterator[object]:
        assert size == 64 * 1024
        for chunk in self.chunks:
            yield chunk


class _FakeResponse:
    def __init__(
        self,
        *,
        status: int = 200,
        headers: Mapping[str, str] | None = None,
        chunks: list[object] | None = None,
    ) -> None:
        self.status = status
        self.headers = headers or {}
        self.content = _FakeContent(chunks or [b"{}"])
        self.exited = False


class _FakeRequestContext:
    def __init__(
        self,
        response: _FakeResponse | None,
        *,
        error: BaseException | None = None,
        wait: bool = False,
    ) -> None:
        self.response = response
        self.error = error
        self.wait = wait

    async def __aenter__(self) -> Any:
        if self.wait:
            await asyncio.Event().wait()
        if self.error is not None:
            raise self.error
        assert self.response is not None
        return self.response

    async def __aexit__(self, *_exc_info: object) -> None:
        if self.response is not None:
            self.response.exited = True


class _FakeSession:
    def __init__(
        self,
        response: _FakeResponse | None = None,
        *,
        error: BaseException | None = None,
        wait: bool = False,
    ) -> None:
        self.response = response or _FakeResponse()
        self.error = error
        self.wait = wait
        self.closed = False
        self.close_calls = 0
        self.trust_env = False
        self.auto_decompress = False
        self.skip_auto_headers = frozenset({"Accept", "Accept-Encoding", "User-Agent"})
        self.headers: dict[str, str] = {}
        self.cookie_jar = aiohttp.DummyCookieJar()
        self._default_auth: object | None = None
        self._default_proxy: object | None = None
        self._default_proxy_auth: object | None = None
        self._raise_for_status = False
        self._retry_connection = False
        self._middlewares: tuple[object, ...] = ()
        self._trace_configs: list[object] = []
        self._request_class = aiohttp.ClientRequest
        self._response_class = aiohttp.ClientResponse
        self.calls: list[tuple[tuple[object, ...], dict[str, object]]] = []

    def request(self, *args: object, **kwargs: object) -> _FakeRequestContext:
        self.calls.append((args, kwargs))
        return _FakeRequestContext(self.response, error=self.error, wait=self.wait)

    async def close(self) -> None:
        self.close_calls += 1
        self.closed = True


def _deadline() -> _Deadline:
    return _Deadline(asyncio.get_running_loop().time() + 10)


def _discovery_request(**changes: object) -> _ChinaTransportRequest:
    values: dict[str, object] = {
        "operation": "acquire_vehicles",
        "service": "g_app",
        "method": "POST",
        "url": "https://gapp-api.gwmapp-h.com/gcar/v1/app/android/vehicle/query-vehicle-list",
        "body": b'{"vehicleVersion":13}',
        "headers": {
            "G-TOKEN": "synthetic-g-token",
            "Authorization": "synthetic-bean-access",
            "ssoId": "synthetic-user",
            "SourceApp": "GWM",
            "SourceType": "ANDROID",
            "SourceAppVer": "2.1.5",
            "SourceAppCode": "2150",
            "Timestamp": "1723456789000",
            "DeviceId": DEVICE_ID,
            "AppId": "GWM-APP-ANDROID-1100018",
            "beanId": "synthetic-bean",
            "NoteId": "145765423214576567716671",
            "Sign": "a" * 64,
            "Accept-Encoding": "gzip",
            "User-Agent": "okhttp/4.2.2",
            "Content-Type": "application/json; charset=UTF-8",
        },
    }
    values.update(changes)
    return _ChinaTransportRequest(**values)  # type: ignore[arg-type]


def _status_request(**changes: object) -> _ChinaTransportRequest:
    timestamp = "1723456789123"
    wrapper = {
        "body": {"vin": VIN},
        "header": {
            "brandType": "gwm",
            "cVer": "2.1.5",
            "fn": "GW.M.GET_VEHICLE_STATE",
            "fv": "0202",
            "mobileId": DEVICE_ID,
            "osType": "Android",
            "osVer": "",
            "rs": "2",
            "ts": "20240812205949123",
            "tk": "synthetic-auto-token",
            "v": "1.0",
        },
    }
    payload = json.dumps(wrapper, separators=(",", ":"))
    values: dict[str, object] = {
        "operation": "get_last_status",
        "service": "auto_ai",
        "method": "GET",
        "url": "https://ti.gwm.com.cn:8443/tsp/ead?p=" + quote(payload, safe=""),
        "body": None,
        "headers": {
            "v": "1.0",
            "cid": DEVICE_ID,
            "client": "phone",
            "sign": auto_ai_sign(timestamp),
            "time": timestamp,
            "ckey": AUTO_AI_CKEY,
            "protocolVer": "2.1.2",
            "token": "synthetic-auto-token",
            "brandType": "GWM",
            "Accept-Encoding": "gzip",
            "User-Agent": "okhttp/4.2.2",
        },
    }
    values.update(changes)
    return _ChinaTransportRequest(**values)  # type: ignore[arg-type]


async def _execute(
    response: _FakeResponse,
    *,
    request: _ChinaTransportRequest | None = None,
    max_compressed_bytes: int = 1024,
    max_response_bytes: int = 1024,
) -> tuple[bytes, _FakeSession]:
    session = _FakeSession(response)
    transport = ChinaAiohttpTransport(
        cast(aiohttp.ClientSession, session),
        max_compressed_bytes=max_compressed_bytes,
        max_response_bytes=max_response_bytes,
    )
    result = await transport.execute(
        request or _discovery_request(),
        deadline=_deadline(),
        connect_timeout=2,
        read_timeout=3,
    )
    return result.body, session


def test_capabilities_name_all_three_services_without_claiming_http2() -> None:
    assert ChinaAiohttpTransport.capabilities == ChinaTransportCapabilities()
    assert ChinaAiohttpTransport.capabilities.protocol_service_aliases == (
        "g_app",
        "bean_tech",
        "auto_ai",
    )
    assert ChinaAiohttpTransport.capabilities.enabled_read_service_aliases == (
        "g_app",
        "auto_ai",
    )
    assert ChinaAiohttpTransport.capabilities.bean_tech_http_deferred
    assert ChinaAiohttpTransport.capabilities.bounded_gzip
    assert ChinaAiohttpTransport.capabilities.http2_preferred_by_app
    assert not ChinaAiohttpTransport.capabilities.http2_available_in_adapter
    assert ChinaAiohttpTransport.capabilities.live_http_version_validation_required


@pytest.mark.parametrize(
    ("factory", "changes"),
    [
        (_discovery_request, {"service": "bean_tech"}),
        (_discovery_request, {"method": "GET"}),
        (_discovery_request, {"url": "https://gapp-api.gwmapp-h.com/other"}),
        (_discovery_request, {"body": b'{"vehicleVersion":14}'}),
        (_status_request, {"service": "g_app"}),
        (_status_request, {"method": "POST"}),
        (_status_request, {"url": "https://ti.gwm.com.cn/tsp/ead?p=x"}),
        (_status_request, {"url": "https://ti.gwm.com.cn:8443/tsp/ead?p=x&other=y"}),
        (_status_request, {"body": b"{}"}),
    ],
)
def test_route_boundary_rejects_cross_service_method_path_query_and_body(
    factory: Any,
    changes: Mapping[str, object],
) -> None:
    with pytest.raises(ValueError, match="^route_invalid$"):
        factory(**changes)


def test_status_route_rejects_duplicate_deep_oversized_and_userinfo_queries() -> None:
    valid_payload = unquote(urlsplit(_status_request().url).query[2:])
    duplicate_payload = valid_payload.replace(
        '{"body":',
        f'{{"body":{{"vin":"{VIN}"}},"body":',
        1,
    )
    deep_payload = "[" * 20 + "0" + "]" * 20
    base = "https://ti.gwm.com.cn:8443/tsp/ead?p="
    urls = [
        base + quote(duplicate_payload, safe=""),
        base + quote(deep_payload, safe=""),
        base + ("x" * (256 * 1024)),
        "https://user@ti.gwm.com.cn:8443/tsp/ead?p=" + quote(valid_payload, safe=""),
    ]
    for url in urls:
        with pytest.raises(ValueError, match="^route_invalid$"):
            _status_request(url=url)

    special_headers = dict(_status_request().headers)
    special_headers["token"] = "SYNTHETIC+TOKEN"
    raw_special = json.loads(valid_payload)
    raw_special["header"]["tk"] = "SYNTHETIC+TOKEN"
    non_dotnet_payload = json.dumps(raw_special, separators=(",", ":"))
    with pytest.raises(ValueError, match="^route_invalid$"):
        _status_request(
            headers=special_headers,
            url=base + quote(non_dotnet_payload, safe=""),
        )


def test_request_headers_are_exact_immutable_and_repr_safe() -> None:
    request = _discovery_request()
    assert set(request.headers) == {
        "G-TOKEN",
        "Authorization",
        "ssoId",
        "SourceApp",
        "SourceType",
        "SourceAppVer",
        "SourceAppCode",
        "Timestamp",
        "DeviceId",
        "AppId",
        "beanId",
        "NoteId",
        "Sign",
        "Accept-Encoding",
        "User-Agent",
        "Content-Type",
    }
    with pytest.raises(TypeError):
        request.headers["G-TOKEN"] = SENSITIVE  # type: ignore[index]
    rendered = repr(request)
    assert "synthetic-g-token" not in rendered
    assert DEVICE_ID not in rendered
    assert request.url not in rendered


@pytest.mark.parametrize("bad_value", ["gzip, br", "br", "", "bad\r\nInjected: yes"])
def test_exact_header_profile_rejects_changed_or_unsafe_values(bad_value: str) -> None:
    headers = dict(_discovery_request().headers)
    headers["Accept-Encoding"] = bad_value
    with pytest.raises(ValueError, match="^(header|route)_invalid$"):
        _discovery_request(headers=headers)


@pytest.mark.asyncio
async def test_transport_sends_exact_post_bytes_with_no_ambient_features() -> None:
    body, session = await _execute(_FakeResponse(chunks=[b'{"ok":true}']))
    assert body == b'{"ok":true}'
    args, options = session.calls[0]
    assert args == (
        "POST",
        URL(
            "https://gapp-api.gwmapp-h.com/gcar/v1/app/android/vehicle/query-vehicle-list",
            encoded=True,
        ),
    )
    assert options["data"] == b'{"vehicleVersion":13}'
    assert len(cast(bytes, options["data"])) == 21
    assert cast(Mapping[str, str], options["headers"])["Content-Type"] == (
        "application/json; charset=UTF-8"
    )
    assert options["allow_redirects"] is False
    assert options["auto_decompress"] is False
    assert options["cookies"] == {}
    assert options["proxy"] is None
    assert options["auth"] is None
    context = cast(ssl.SSLContext, options["ssl"])
    assert context.check_hostname
    assert context.verify_mode == ssl.CERT_REQUIRED
    assert context.minimum_version >= ssl.TLSVersion.TLSv1_2


@pytest.mark.asyncio
async def test_transport_sends_exact_fixed_port_status_get_without_body() -> None:
    request = _status_request()
    body, session = await _execute(_FakeResponse(chunks=[b"{}"]), request=request)
    assert body == b"{}"
    args, options = session.calls[0]
    assert args[0] == "GET"
    assert cast(URL, args[1]).host == "ti.gwm.com.cn"
    assert cast(URL, args[1]).port == 8443
    assert options["data"] is None
    assert cast(Mapping[str, str], options["headers"])["client"] == "phone"


@pytest.mark.asyncio
async def test_gzip_is_streamed_and_decoded_across_chunk_boundaries() -> None:
    expected = b'{"status":"synthetic"}'
    compressed = gzip.compress(expected, mtime=0)
    response = _FakeResponse(
        headers={"Content-Encoding": "GZip", "Content-Length": str(len(compressed))},
        chunks=[compressed[:7], compressed[7:19], compressed[19:]],
    )
    body, _session = await _execute(response)
    assert body == expected


@pytest.mark.asyncio
@pytest.mark.parametrize("encoding", ["br", "gzip, br", "x-gzip"])
async def test_unknown_or_multiple_content_encoding_is_rejected(encoding: str) -> None:
    with pytest.raises(GwmProtocolError, match="^GWM protocol response is invalid$"):
        await _execute(_FakeResponse(headers={"Content-Encoding": encoding}))


@pytest.mark.asyncio
@pytest.mark.parametrize("name", ["Content-Encoding", "Content-Length"])
async def test_duplicate_security_relevant_response_headers_are_rejected(name: str) -> None:
    values = ("gzip", "identity") if name == "Content-Encoding" else ("2", "2")
    headers: CIMultiDict[str] = CIMultiDict([(name, values[0]), (name, values[1])])
    with pytest.raises(GwmProtocolError):
        await _execute(_FakeResponse(headers=headers, chunks=[b"{}"]))


@pytest.mark.asyncio
@pytest.mark.parametrize(
    "payload",
    [
        gzip.compress(b"synthetic", mtime=0)[:-3],
        gzip.compress(b"synthetic", mtime=0)[:-1] + b"x",
        gzip.compress(b"synthetic", mtime=0) + b"trailing",
        gzip.compress(b"one", mtime=0) + gzip.compress(b"two", mtime=0),
    ],
)
async def test_truncated_corrupt_trailing_and_concatenated_gzip_are_rejected(payload: bytes) -> None:
    with pytest.raises(GwmProtocolError, match="^GWM protocol response is invalid$"):
        await _execute(_FakeResponse(headers={"Content-Encoding": "gzip"}, chunks=[payload]))


@pytest.mark.asyncio
async def test_compressed_and_inflated_limits_are_independent() -> None:
    compressed = gzip.compress(b"A" * 500, mtime=0)
    with pytest.raises(GwmResponseTooLargeError):
        await _execute(
            _FakeResponse(headers={"Content-Encoding": "gzip"}, chunks=[compressed]),
            max_compressed_bytes=len(compressed) - 1,
            max_response_bytes=1000,
        )
    with pytest.raises(GwmResponseTooLargeError):
        await _execute(
            _FakeResponse(headers={"Content-Encoding": "gzip"}, chunks=[compressed]),
            max_compressed_bytes=1000,
            max_response_bytes=499,
        )


@pytest.mark.asyncio
@pytest.mark.parametrize("length", ["-1", "+1", "1.0", "x", "9" * 21])
async def test_malformed_content_length_is_rejected(length: str) -> None:
    with pytest.raises(GwmProtocolError):
        await _execute(_FakeResponse(headers={"Content-Length": length}, chunks=[b"{}"]))


@pytest.mark.asyncio
async def test_lying_or_oversized_content_length_is_rejected() -> None:
    with pytest.raises(GwmProtocolError):
        await _execute(_FakeResponse(headers={"Content-Length": "1"}, chunks=[b"{}"]))
    with pytest.raises(GwmResponseTooLargeError):
        await _execute(
            _FakeResponse(headers={"Content-Length": "3"}, chunks=[b"{}"]),
            max_response_bytes=2,
        )


@pytest.mark.asyncio
async def test_redirect_and_non_byte_chunk_fail_before_retention() -> None:
    with pytest.raises(GwmRedirectError):
        await _execute(_FakeResponse(status=302, chunks=[SENSITIVE.encode()]))
    with pytest.raises(GwmProtocolError):
        await _execute(_FakeResponse(chunks=[SENSITIVE]))


@pytest.mark.asyncio
@pytest.mark.parametrize(
    ("error", "expected"),
    [
        (aiohttp.ClientConnectionError(SENSITIVE), GwmNetworkError),
        (ssl.SSLError(SENSITIVE), GwmTlsError),
    ],
)
async def test_adapter_errors_are_mapped_without_source_context(
    error: BaseException,
    expected: type[Exception],
) -> None:
    session = _FakeSession(error=error)
    transport = ChinaAiohttpTransport(cast(aiohttp.ClientSession, session))
    with pytest.raises(expected) as raised:
        await transport.execute(
            _discovery_request(),
            deadline=_deadline(),
            connect_timeout=2,
            read_timeout=3,
        )
    assert SENSITIVE not in str(raised.value)
    assert SENSITIVE not in repr(raised.value)


@pytest.mark.asyncio
async def test_cancellation_propagates_and_session_policy_fails_closed() -> None:
    waiting = _FakeSession(wait=True)
    transport = ChinaAiohttpTransport(cast(aiohttp.ClientSession, waiting))
    task = asyncio.create_task(
        transport.execute(
            _discovery_request(),
            deadline=_deadline(),
            connect_timeout=2,
            read_timeout=3,
        )
    )
    await asyncio.sleep(0)
    task.cancel()
    with pytest.raises(asyncio.CancelledError):
        await task

    unsafe = _FakeSession()
    unsafe.trust_env = True
    unsafe_transport = ChinaAiohttpTransport(cast(aiohttp.ClientSession, unsafe))
    with pytest.raises(GwmConfigurationError):
        await unsafe_transport.execute(
            _discovery_request(),
            deadline=_deadline(),
            connect_timeout=2,
            read_timeout=3,
        )


@pytest.mark.asyncio
@pytest.mark.parametrize(
    ("attribute", "bad_value"),
    [
        ("auto_decompress", True),
        ("_default_auth", object()),
        ("_default_proxy", "https://proxy.invalid"),
        ("_default_proxy_auth", object()),
        ("headers", {"X-Ambient": "unsafe"}),
        ("cookie_jar", object()),
        ("_raise_for_status", True),
        ("_retry_connection", True),
        ("_middlewares", (object(),)),
        ("_trace_configs", [object()]),
        ("_request_class", object()),
        ("_response_class", object()),
        ("skip_auto_headers", frozenset({"Accept", "Content-Length"})),
    ],
)
async def test_every_external_session_ambient_state_boundary_fails_closed(
    attribute: str,
    bad_value: object,
) -> None:
    session = _FakeSession()
    setattr(session, attribute, bad_value)
    transport = ChinaAiohttpTransport(cast(aiohttp.ClientSession, session))
    with pytest.raises(GwmConfigurationError):
        await transport.execute(
            _discovery_request(),
            deadline=_deadline(),
            connect_timeout=2,
            read_timeout=3,
        )


@pytest.mark.asyncio
async def test_transport_lifecycle_closes_only_owned_session_once() -> None:
    external = _FakeSession()
    external_transport = ChinaAiohttpTransport(cast(aiohttp.ClientSession, external))
    await external_transport.aclose()
    await external_transport.aclose()
    assert external.close_calls == 0

    owned = _FakeSession()
    owned_transport = ChinaAiohttpTransport(cast(aiohttp.ClientSession, owned), owns_session=True)
    await owned_transport.aclose()
    await owned_transport.aclose()
    assert owned.close_calls == 1


@pytest.mark.asyncio
async def test_constructor_limits_and_owned_session_policy() -> None:
    session = cast(aiohttp.ClientSession, _FakeSession())
    for value in (True, 0, 16 * 1024 * 1024 + 1):
        with pytest.raises(ValueError, match="^response_limit_invalid$"):
            ChinaAiohttpTransport(session, max_response_bytes=value)  # type: ignore[arg-type]
    with pytest.raises(ValueError, match="^session_ownership_invalid$"):
        ChinaAiohttpTransport(session, owns_session=1)  # type: ignore[arg-type]


@pytest.mark.asyncio
async def test_invalid_deadline_phase_timeout_and_closed_execution_fail_before_http() -> None:
    session = _FakeSession()
    transport = ChinaAiohttpTransport(cast(aiohttp.ClientSession, session))
    with pytest.raises(GwmConfigurationError):
        await transport.execute(  # type: ignore[arg-type]
            _discovery_request(),
            deadline=object(),
            connect_timeout=2,
            read_timeout=3,
        )
    with pytest.raises(GwmConfigurationError):
        await transport.execute(
            _discovery_request(),
            deadline=_deadline(),
            connect_timeout=0,
            read_timeout=3,
        )
    await transport.aclose()
    with pytest.raises(GwmClosedError, match="^GWM client is closed$"):
        await transport.execute(
            _discovery_request(),
            deadline=_deadline(),
            connect_timeout=2,
            read_timeout=3,
        )
    assert session.calls == []


@pytest.mark.asyncio
async def test_owned_transport_disables_ambient_state_and_retry() -> None:
    transport = ChinaAiohttpTransport.create_owned(max_compressed_bytes=32, max_response_bytes=64)
    try:
        session = transport._session
        assert not session.auto_decompress
        assert not session.trust_env
        assert isinstance(session.cookie_jar, aiohttp.DummyCookieJar)
        assert session._retry_connection is False
        assert not session.headers
    finally:
        await transport.aclose()
    assert transport.closed
