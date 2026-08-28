"""Isolated bounded transport for the mainland-China feasibility POC.

The overseas client intentionally rejects compressed responses and has a
different route/header contract.  This module keeps China's gzip and fixed
port requirements behind a separate, two-operation wire boundary.
"""

from __future__ import annotations

import asyncio
import json
import math
import re
import ssl
import zlib
from collections.abc import Mapping
from dataclasses import dataclass, field
from types import MappingProxyType
from typing import Any, Literal, Protocol, Self
from urllib.parse import quote, unquote_to_bytes, urlsplit

import aiohttp
from yarl import URL

from ._dotnet_json import encode_dotnet_json
from ._protocol import _Deadline
from .errors import (
    GwmClientError,
    GwmClosedError,
    GwmConfigurationError,
    GwmDeadlineExceededError,
    GwmNetworkError,
    GwmProtocolError,
    GwmRedirectError,
    GwmResponseTooLargeError,
    GwmRoutePolicyError,
    GwmTlsError,
)

type _ChinaService = Literal["g_app", "bean_tech", "auto_ai"]

_DISCOVERY_URL = "https://gapp-api.gwmapp-h.com/gcar/v1/app/android/vehicle/query-vehicle-list"
_AUTO_AI_ORIGIN = "https://ti.gwm.com.cn:8443"
_AUTO_AI_PATH = "/tsp/ead"
_DISCOVERY_BODY = b'{"vehicleVersion":13}'
_OFFICIAL_USER_AGENT = "okhttp/4.2.2"
_READ_CHUNK_BYTES = 64 * 1024
_MAX_DECIMAL_HEADER_LENGTH = 20
_MAX_ALLOWED_RESPONSE_BYTES = 16 * 1024 * 1024
_MAX_STATUS_URL_LENGTH = 256 * 1024
_MAX_STATUS_PAYLOAD_LENGTH = 64 * 1024
_MAX_WIRE_JSON_DEPTH = 16
_SAFE_RESPONSE_HEADERS = frozenset({"content-type", "retry-after"})
_SKIP_AUTO_HEADERS = frozenset({"Accept", "Accept-Encoding", "User-Agent"})
_HEADER_NAME = re.compile(r"[-!#$%&'*+.^_`|~0-9A-Za-z]+")
_DEVICE_ID = re.compile(r"[0-9A-Fa-f]{32}")
_LOWER_HEX_64 = re.compile(r"[0-9a-f]{64}")
_BASE64_SHA1 = re.compile(r"[A-Za-z0-9+/]{27}=")
_DISCOVERY_HEADERS = frozenset(
    {
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
)
_STATUS_HEADERS = frozenset(
    {
        "v",
        "cid",
        "client",
        "sign",
        "time",
        "ckey",
        "protocolVer",
        "token",
        "brandType",
        "Accept-Encoding",
        "User-Agent",
    }
)


@dataclass(frozen=True, slots=True)
class ChinaTransportCapabilities:
    """Non-secret evidence about the deliberately selected POC adapter."""

    protocol_service_aliases: tuple[str, ...] = ("g_app", "bean_tech", "auto_ai")
    enabled_read_service_aliases: tuple[str, ...] = ("g_app", "auto_ai")
    bean_tech_http_deferred: bool = True
    bounded_gzip: bool = True
    http2_preferred_by_app: bool = True
    http2_available_in_adapter: bool = False
    live_http_version_validation_required: bool = True


@dataclass(frozen=True, slots=True)
class _ChinaTransportRequest:
    operation: Literal["acquire_vehicles", "get_last_status"]
    service: _ChinaService
    method: Literal["GET", "POST"]
    url: str = field(repr=False)
    headers: Mapping[str, str] = field(repr=False)
    body: bytes | None = field(default=None, repr=False)

    def __post_init__(self) -> None:
        copied = _validated_headers(self.headers)
        if self.operation == "acquire_vehicles":
            _validate_discovery_request(self, copied)
        elif self.operation == "get_last_status":
            _validate_status_request(self, copied)
        else:  # pragma: no cover - the Literal is still a runtime boundary
            raise ValueError("operation_invalid")
        object.__setattr__(self, "headers", MappingProxyType(copied))


@dataclass(frozen=True, slots=True)
class _ChinaTransportResponse:
    status: int
    headers: Mapping[str, str] = field(repr=False)
    body: bytes = field(repr=False)

    def __post_init__(self) -> None:
        if isinstance(self.status, bool) or not isinstance(self.status, int) or not 100 <= self.status <= 599:
            raise ValueError("http_status_invalid")
        if not isinstance(self.body, bytes):
            raise ValueError("response_body_invalid")
        object.__setattr__(
            self,
            "headers",
            MappingProxyType(
                {
                    str(name).lower(): str(value)
                    for name, value in self.headers.items()
                    if str(name).lower() in _SAFE_RESPONSE_HEADERS
                }
            ),
        )


class _ChinaAsyncTransport(Protocol):
    async def execute(
        self,
        request: _ChinaTransportRequest,
        *,
        deadline: _Deadline,
        connect_timeout: float,
        read_timeout: float,
    ) -> _ChinaTransportResponse: ...

    async def aclose(self) -> None: ...


class ChinaAiohttpTransport:
    """Execute the two allowed China reads without ambient HTTP state."""

    capabilities = ChinaTransportCapabilities()

    def __init__(
        self,
        session: aiohttp.ClientSession,
        *,
        owns_session: bool = False,
        max_compressed_bytes: int = 4 * 1024 * 1024,
        max_response_bytes: int = 4 * 1024 * 1024,
    ) -> None:
        _validate_response_limit(max_compressed_bytes)
        _validate_response_limit(max_response_bytes)
        if type(owns_session) is not bool:
            raise ValueError("session_ownership_invalid")
        self._session = session
        self._owns_session = owns_session
        self._max_compressed_bytes = max_compressed_bytes
        self._max_response_bytes = max_response_bytes
        self._ssl_context = _create_ssl_context()
        self._closed = False
        self._closing = False
        self._close_lock = asyncio.Lock()

    @classmethod
    def create_owned(
        cls,
        *,
        max_compressed_bytes: int = 4 * 1024 * 1024,
        max_response_bytes: int = 4 * 1024 * 1024,
    ) -> Self:
        """Create a dedicated HTTP/1.1 adapter inside the active event loop."""

        _validate_response_limit(max_compressed_bytes)
        _validate_response_limit(max_response_bytes)
        if cls is not ChinaAiohttpTransport:
            raise TypeError("transport_subclass_not_supported")
        session = aiohttp.ClientSession(
            auto_decompress=False,
            cookie_jar=aiohttp.DummyCookieJar(),
            middlewares=(),
            raise_for_status=False,
            skip_auto_headers=_SKIP_AUTO_HEADERS,
            trace_configs=[],
            trust_env=False,
        )
        session._retry_connection = False
        return cls(
            session,
            owns_session=True,
            max_compressed_bytes=max_compressed_bytes,
            max_response_bytes=max_response_bytes,
        )

    @property
    def closed(self) -> bool:
        return self._closed

    async def __aenter__(self) -> Self:
        if self._closed or self._closing:
            raise GwmClosedError()
        return self

    async def __aexit__(self, *_exc_info: object) -> None:
        await self.aclose()

    async def aclose(self) -> None:
        async with self._close_lock:
            if self._closed:
                return
            self._closing = True
            try:
                if self._owns_session and not self._session.closed:
                    await self._session.close()
            except BaseException:
                self._closing = False
                raise
            self._closed = True
            self._closing = False

    async def execute(
        self,
        request: _ChinaTransportRequest,
        *,
        deadline: _Deadline,
        connect_timeout: float,
        read_timeout: float,
    ) -> _ChinaTransportResponse:
        """Send one validated China POC request and decode at most one gzip stream."""

        if type(request) is not _ChinaTransportRequest:
            raise GwmRoutePolicyError()
        operation = request.operation
        if type(deadline) is not _Deadline or not all(
            _valid_phase_timeout(value) for value in (connect_timeout, read_timeout)
        ):
            raise GwmConfigurationError(operation=operation)
        if self._closed or self._closing or self._session.closed:
            raise GwmClosedError(operation=operation)
        self._validate_session_policy(operation=operation)
        _validate_tls_context(self._ssl_context)

        loop = asyncio.get_running_loop()
        remaining = deadline.remaining(loop.time())
        if remaining <= 0:
            raise GwmDeadlineExceededError(operation=operation)
        timeout = aiohttp.ClientTimeout(
            total=remaining,
            connect=min(connect_timeout, remaining),
            sock_read=min(read_timeout, remaining),
        )

        failure: GwmClientError | None = None
        try:
            async with self._session.request(
                request.method,
                URL(request.url, encoded=True),
                allow_redirects=False,
                auto_decompress=False,
                auth=None,
                cookies={},
                data=request.body,
                headers=request.headers,
                middlewares=(),
                params=None,
                proxy=None,
                proxy_auth=None,
                raise_for_status=False,
                skip_auto_headers=_SKIP_AUTO_HEADERS,
                ssl=self._ssl_context,
                timeout=timeout,
            ) as response:
                return await self._read_response(response, operation=operation)
        except asyncio.CancelledError:
            raise
        except GwmClientError:
            raise
        except TimeoutError:
            failure = GwmDeadlineExceededError(operation=operation)
        except (
            aiohttp.ClientConnectorCertificateError,
            aiohttp.ClientConnectorSSLError,
            aiohttp.ClientSSLError,
            aiohttp.ServerFingerprintMismatch,
            ssl.CertificateError,
            ssl.SSLError,
        ):
            failure = GwmTlsError(operation=operation)
        except (aiohttp.ClientConnectionError, aiohttp.ClientPayloadError, OSError):
            failure = GwmNetworkError(operation=operation)
        except aiohttp.ClientError:
            failure = GwmNetworkError(operation=operation)
        if failure is not None:
            raise failure
        raise GwmNetworkError(operation=operation)

    async def _read_response(
        self,
        response: aiohttp.ClientResponse,
        *,
        operation: str,
    ) -> _ChinaTransportResponse:
        if 300 <= response.status <= 399:
            raise GwmRedirectError(operation=operation)

        encoding_value = _single_response_header(
            response.headers,
            "Content-Encoding",
            operation=operation,
        )
        encoding = (encoding_value or "").strip().lower()
        if encoding not in {"", "identity", "gzip"}:
            raise GwmProtocolError(operation=operation)
        wire_limit = self._max_compressed_bytes if encoding == "gzip" else self._max_response_bytes
        content_length = _validated_content_length(
            _single_response_header(
                response.headers,
                "Content-Length",
                operation=operation,
            ),
            operation=operation,
        )
        if content_length is not None and content_length > wire_limit:
            raise GwmResponseTooLargeError(operation=operation)

        if encoding == "gzip":
            body, wire_count = await self._read_gzip(response, operation=operation)
        else:
            body, wire_count = await self._read_identity(response, operation=operation)
        if content_length is not None and content_length != wire_count:
            raise GwmProtocolError(operation=operation)
        return _ChinaTransportResponse(
            status=response.status,
            headers=_selected_headers(response.headers),
            body=body,
        )

    async def _read_identity(
        self,
        response: aiohttp.ClientResponse,
        *,
        operation: str,
    ) -> tuple[bytes, int]:
        body = bytearray()
        async for chunk in response.content.iter_chunked(_READ_CHUNK_BYTES):
            data = _validated_chunk(chunk, operation=operation)
            if len(body) + len(data) > self._max_response_bytes:
                raise GwmResponseTooLargeError(operation=operation)
            body.extend(data)
        return bytes(body), len(body)

    async def _read_gzip(
        self,
        response: aiohttp.ClientResponse,
        *,
        operation: str,
    ) -> tuple[bytes, int]:
        inflater = zlib.decompressobj(16 + zlib.MAX_WBITS)
        compressed_count = 0
        body = bytearray()
        try:
            async for chunk in response.content.iter_chunked(_READ_CHUNK_BYTES):
                data = _validated_chunk(chunk, operation=operation)
                compressed_count += len(data)
                if compressed_count > self._max_compressed_bytes:
                    raise GwmResponseTooLargeError(operation=operation)
                if inflater.eof and data:
                    raise GwmProtocolError(operation=operation)
                remaining = self._max_response_bytes - len(body)
                inflated = inflater.decompress(data, remaining + 1)
                if len(inflated) > remaining or inflater.unconsumed_tail:
                    raise GwmResponseTooLargeError(operation=operation)
                body.extend(inflated)
                if inflater.unused_data:
                    raise GwmProtocolError(operation=operation)
            remaining = self._max_response_bytes - len(body)
            tail = inflater.flush(remaining + 1)
        except zlib.error:
            raise GwmProtocolError(operation=operation) from None
        if len(tail) > remaining:
            raise GwmResponseTooLargeError(operation=operation)
        body.extend(tail)
        if not inflater.eof or inflater.unused_data or inflater.unconsumed_tail:
            raise GwmProtocolError(operation=operation)
        return bytes(body), compressed_count

    def _validate_session_policy(self, *, operation: str) -> None:
        if isinstance(self._session, aiohttp.ClientSession) and (
            type(self._session) is not aiohttp.ClientSession
            or type(self._session.connector) is not aiohttp.TCPConnector
        ):
            raise GwmConfigurationError(operation=operation)
        if self._session.trust_env:
            raise GwmConfigurationError(operation=operation)
        if getattr(self._session, "auto_decompress", None) is not False:
            raise GwmConfigurationError(operation=operation)
        if getattr(self._session, "_default_auth", None) is not None:
            raise GwmConfigurationError(operation=operation)
        if getattr(self._session, "_default_proxy", None) is not None:
            raise GwmConfigurationError(operation=operation)
        if getattr(self._session, "_default_proxy_auth", None) is not None:
            raise GwmConfigurationError(operation=operation)
        if self._session.headers:
            raise GwmConfigurationError(operation=operation)
        if not isinstance(self._session.cookie_jar, aiohttp.DummyCookieJar):
            raise GwmConfigurationError(operation=operation)
        if getattr(self._session, "_raise_for_status", None) is not False:
            raise GwmConfigurationError(operation=operation)
        if getattr(self._session, "_retry_connection", None) is not False:
            raise GwmConfigurationError(operation=operation)
        if getattr(self._session, "_middlewares", None) != ():
            raise GwmConfigurationError(operation=operation)
        if getattr(self._session, "_trace_configs", None) not in ([], ()):
            raise GwmConfigurationError(operation=operation)
        if getattr(self._session, "_request_class", None) is not aiohttp.ClientRequest:
            raise GwmConfigurationError(operation=operation)
        if getattr(self._session, "_response_class", None) is not aiohttp.ClientResponse:
            raise GwmConfigurationError(operation=operation)
        skip_auto_headers = getattr(self._session, "skip_auto_headers", None)
        if skip_auto_headers is None or {
            str(name).lower() for name in skip_auto_headers
        } != {name.lower() for name in _SKIP_AUTO_HEADERS}:
            raise GwmConfigurationError(operation=operation)


def _validated_headers(headers: Mapping[str, str]) -> dict[str, str]:
    if not isinstance(headers, Mapping):
        raise ValueError("header_invalid")
    copied: dict[str, str] = {}
    normalized: set[str] = set()
    for name, value in headers.items():
        lower = name.lower() if isinstance(name, str) else ""
        if (
            not isinstance(name, str)
            or _HEADER_NAME.fullmatch(name) is None
            or lower in normalized
            or not isinstance(value, str)
            or not value
            or len(value) > 16 * 1024
            or any(ord(character) < 0x20 or ord(character) > 0x7E for character in value)
        ):
            raise ValueError("header_invalid")
        normalized.add(lower)
        copied[name] = value
    return copied


def _validate_discovery_request(request: _ChinaTransportRequest, headers: Mapping[str, str]) -> None:
    if (
        request.service != "g_app"
        or request.method != "POST"
        or request.url != _DISCOVERY_URL
        or request.body != _DISCOVERY_BODY
        or set(headers) != _DISCOVERY_HEADERS
        or headers.get("SourceApp") != "GWM"
        or headers.get("SourceType") != "ANDROID"
        or headers.get("SourceAppVer") != "2.1.5"
        or headers.get("SourceAppCode") != "2150"
        or headers.get("AppId") != "GWM-APP-ANDROID-1100018"
        or headers.get("NoteId") != "145765423214576567716671"
        or headers.get("Accept-Encoding") != "gzip"
        or headers.get("User-Agent") != _OFFICIAL_USER_AGENT
        or headers.get("Content-Type") != "application/json; charset=UTF-8"
        or _DEVICE_ID.fullmatch(headers.get("DeviceId", "")) is None
        or _LOWER_HEX_64.fullmatch(headers.get("Sign", "")) is None
        or not _second_aligned_epoch(headers.get("Timestamp", ""))
    ):
        raise ValueError("route_invalid")


def _validate_status_request(request: _ChinaTransportRequest, headers: Mapping[str, str]) -> None:
    if (
        request.service != "auto_ai"
        or request.method != "GET"
        or request.body is not None
        or set(headers) != _STATUS_HEADERS
        or headers.get("v") != "1.0"
        or headers.get("client") != "phone"
        or headers.get("ckey") != "ea49a50f914b8d38af1c84809d302683"
        or headers.get("protocolVer") != "2.1.2"
        or headers.get("brandType") != "GWM"
        or headers.get("Accept-Encoding") != "gzip"
        or headers.get("User-Agent") != _OFFICIAL_USER_AGENT
        or _DEVICE_ID.fullmatch(headers.get("cid", "")) is None
        or _BASE64_SHA1.fullmatch(headers.get("sign", "")) is None
        or not _epoch_milliseconds(headers.get("time", ""))
        or not _valid_auto_ai_url(request.url, headers)
    ):
        raise ValueError("route_invalid")


def _valid_auto_ai_url(url: str, headers: Mapping[str, str]) -> bool:
    try:
        url.encode("ascii")
        if len(url) > _MAX_STATUS_URL_LENGTH:
            return False
        parsed = urlsplit(url)
        if (
            parsed.scheme != "https"
            or parsed.hostname != "ti.gwm.com.cn"
            or parsed.port != 8443
            or parsed.path != _AUTO_AI_PATH
            or not parsed.query.startswith("p=")
            or "&" in parsed.query
            or parsed.username is not None
            or parsed.password is not None
            or parsed.fragment
            or "\\" in url
            or any(character.isspace() for character in url)
        ):
            return False
        encoded = parsed.query[2:]
        payload = unquote_to_bytes(encoded).decode("utf-8", errors="strict")
        if len(payload) > _MAX_STATUS_PAYLOAD_LENGTH:
            return False
        if quote(payload, safe="", encoding="utf-8", errors="strict") != encoded:
            return False
        wrapper = json.loads(
            payload,
            object_pairs_hook=_unique_wire_object,
            parse_constant=_reject_wire_constant,
        )
        _validate_wire_json_depth(wrapper)
        if encode_dotnet_json(wrapper) != payload:
            return False
    except (
        UnicodeDecodeError,
        UnicodeEncodeError,
        TypeError,
        ValueError,
        json.JSONDecodeError,
        RecursionError,
    ):
        return False
    if not isinstance(wrapper, dict) or list(wrapper) != ["body", "header"]:
        return False
    body = wrapper.get("body")
    header = wrapper.get("header")
    if not isinstance(body, dict) or list(body) != ["vin"] or not _safe_wire_text(body.get("vin"), maximum=512):
        return False
    if not isinstance(header, dict) or list(header) != [
        "brandType",
        "cVer",
        "fn",
        "fv",
        "mobileId",
        "osType",
        "osVer",
        "rs",
        "ts",
        "tk",
        "v",
    ]:
        return False
    return (
        header.get("brandType") == "gwm"
        and header.get("cVer") == "2.1.5"
        and header.get("fn") == "GW.M.GET_VEHICLE_STATE"
        and header.get("fv") == "0202"
        and header.get("mobileId") == headers.get("cid")
        and header.get("osType") == "Android"
        and header.get("osVer") == ""
        and header.get("rs") == "2"
        and isinstance(header.get("ts"), str)
        and len(header["ts"]) == 17
        and header["ts"].isdecimal()
        and header.get("tk") == headers.get("token")
        and header.get("v") == "1.0"
    )


def _unique_wire_object(pairs: list[tuple[str, object]]) -> dict[str, object]:
    result: dict[str, object] = {}
    normalized: set[str] = set()
    for key, value in pairs:
        folded = key.casefold()
        if folded in normalized:
            raise ValueError("duplicate_json_key")
        normalized.add(folded)
        result[key] = value
    return result


def _reject_wire_constant(_value: str) -> object:
    raise ValueError("invalid_json_number")


def _validate_wire_json_depth(value: object, *, depth: int = 0) -> None:
    if depth > _MAX_WIRE_JSON_DEPTH:
        raise ValueError("json_too_deep")
    if isinstance(value, Mapping):
        for child in value.values():
            _validate_wire_json_depth(child, depth=depth + 1)
    elif isinstance(value, list):
        for child in value:
            _validate_wire_json_depth(child, depth=depth + 1)


def _safe_wire_text(value: object, *, maximum: int) -> bool:
    return (
        isinstance(value, str)
        and 0 < len(value) <= maximum
        and all(0x21 <= ord(character) <= 0x7E for character in value)
    )


def _epoch_milliseconds(value: str) -> bool:
    return 10 <= len(value) <= 17 and value.isdecimal()


def _second_aligned_epoch(value: str) -> bool:
    return _epoch_milliseconds(value) and value.endswith("000")


def _validated_chunk(chunk: object, *, operation: str) -> bytes:
    if not isinstance(chunk, bytes | bytearray):
        raise GwmProtocolError(operation=operation)
    return bytes(chunk)


def _validated_content_length(value: object, *, operation: str) -> int | None:
    if value is None:
        return None
    if (
        not isinstance(value, str)
        or not value.isdecimal()
        or len(value) > _MAX_DECIMAL_HEADER_LENGTH
    ):
        raise GwmProtocolError(operation=operation)
    return int(value)


def _single_response_header(
    headers: Mapping[str, Any],
    name: str,
    *,
    operation: str,
) -> str | None:
    getall = getattr(headers, "getall", None)
    if callable(getall):
        values = getall(name, [])
        if len(values) > 1:
            raise GwmProtocolError(operation=operation)
        if values:
            return str(values[0])
        return None
    value = headers.get(name)
    return None if value is None else str(value)


def _selected_headers(headers: Mapping[str, Any]) -> Mapping[str, str]:
    return {
        str(name).lower(): str(value)
        for name, value in headers.items()
        if str(name).lower() in _SAFE_RESPONSE_HEADERS
    }


def _create_ssl_context() -> ssl.SSLContext:
    context = ssl.create_default_context(ssl.Purpose.SERVER_AUTH)
    context.minimum_version = ssl.TLSVersion.TLSv1_2
    _validate_tls_context(context)
    return context


def _validate_tls_context(context: object) -> None:
    if (
        not isinstance(context, ssl.SSLContext)
        or not context.check_hostname
        or context.verify_mode != ssl.CERT_REQUIRED
        or context.minimum_version < ssl.TLSVersion.TLSv1_2
        or context.security_level <= 0
    ):
        raise ValueError("tls_context_invalid")


def _validate_response_limit(value: object) -> None:
    if (
        isinstance(value, bool)
        or not isinstance(value, int)
        or not 0 < value <= _MAX_ALLOWED_RESPONSE_BYTES
    ):
        raise ValueError("response_limit_invalid")


def _valid_phase_timeout(value: object) -> bool:
    return (
        not isinstance(value, bool)
        and isinstance(value, int | float)
        and math.isfinite(value)
        and value > 0
    )


__all__ = ["ChinaAiohttpTransport", "ChinaTransportCapabilities"]
