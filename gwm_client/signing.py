"""Deterministic request signing for the regional GWM gateways."""

from __future__ import annotations

import hashlib
import re
import time
import unicodedata
import uuid
from dataclasses import dataclass, field
from typing import Literal
from urllib.parse import SplitResult, quote, unquote, urlsplit, urlunsplit

QueryPolicy = Literal["drop-empty-encoded", "keep-empty-decoded"]
_INVALID_PERCENT_ESCAPE = re.compile(r"%(?![0-9A-Fa-f]{2})")
_HTTP_METHOD = re.compile(r"[-!#$%&'*+.^_`|~0-9A-Za-z]+")


@dataclass(frozen=True, slots=True)
class SigningProfile:
    """Constants and canonicalization rules used by one GWM app family."""

    name: str
    prefix: str
    app_key: str
    app_secret: str = field(repr=False)
    query_policy: QueryPolicy
    uppercase_nonce: bool = False

    def __post_init__(self) -> None:
        if self.query_policy not in {"drop-empty-encoded", "keep-empty-decoded"}:
            raise ValueError(f"Unsupported query policy: {self.query_policy}")


EU_GWM_AUTH = SigningProfile(
    name="eu-gwm-auth",
    prefix="gwm",
    app_key="1874226830",
    app_secret="1eb6caa16ff203c96daf7f06309b8998",
    query_policy="drop-empty-encoded",
)

EU_BT_AUTH = SigningProfile(
    name="eu-bt-auth",
    prefix="bt",
    app_key="1874226830",
    app_secret="1eb6caa16ff203c96daf7f06309b8998",
    query_policy="keep-empty-decoded",
    uppercase_nonce=True,
)

ANZ_BT_AUTH = SigningProfile(
    name="anz-bt-auth",
    prefix="bt",
    app_key="2186661209",
    app_secret="a9664fd3f97665e202e73880de03a0d8",
    query_policy="drop-empty-encoded",
)

RUSSIA_GWM_AUTH = SigningProfile(
    name="russia-gwm-auth",
    prefix="gwm",
    app_key="4694605273",
    app_secret="e4e478c00f570e76a8993653a7b81d57",
    query_policy="drop-empty-encoded",
)


@dataclass(frozen=True, slots=True)
class SignedRequest:
    """The request URL and generated auth headers from a signing operation.

    The headers are additions for an HTTP adapter to merge into its request;
    they are not a replacement for content or application headers.
    """

    method: str
    url: str = field(repr=False)
    headers: dict[str, str] = field(repr=False)
    body: str | None = field(repr=False)


def sign_request(
    profile: SigningProfile,
    method: str,
    url: str,
    body: str | None = None,
    *,
    timestamp: str | None = None,
    nonce: str | None = None,
) -> SignedRequest:
    """Sign one request without performing any I/O.

    ``body`` must be the exact serialized request text.  The official clients
    capture that representation rather than parsing and re-serializing JSON,
    then remove all .NET whitespace globally—even inside JSON string values.
    Explicit timestamps and nonces make captured vectors reproducible.  URLs
    must already be absolute, ASCII, percent-encoded HTTP request URLs.
    """

    request_method = method
    if _HTTP_METHOD.fullmatch(request_method) is None:
        raise ValueError("The request method must be a valid HTTP token")
    parsed = urlsplit(url)
    _validate_url(url, parsed)
    path = parsed.path or "/"

    if request_method == "POST":
        parameters = "" if not body else "json=" + body
        outgoing_url = url
    elif profile.query_policy == "keep-empty-decoded":
        parameters = _canonicalize_decoded_query(parsed.query)
        outgoing_url = url
    else:
        parameters, outgoing_query = _canonicalize_encoded_query(parsed.query)
        outgoing_url = urlunsplit((parsed.scheme, parsed.netloc, path, outgoing_query, ""))

    actual_timestamp = timestamp if timestamp is not None else str(time.time_ns() // 1_000_000)
    actual_nonce = nonce if nonce is not None else _new_nonce(profile.uppercase_nonce)
    prefix = profile.prefix
    auth = (
        f"{prefix}-auth-appkey:{profile.app_key}"
        f"{prefix}-auth-nonce:{actual_nonce}"
        f"{prefix}-auth-timestamp:{actual_timestamp}"
    )
    raw = _strip_dotnet_whitespace(request_method + path + auth + parameters + profile.app_secret)
    escaped = quote(raw, safe="-._~", encoding="utf-8", errors="strict")
    signature = hashlib.sha256(escaped.encode()).hexdigest()

    headers = {
        f"{prefix}-auth-appkey": profile.app_key,
        f"{prefix}-auth-nonce": actual_nonce,
        f"{prefix}-auth-timestamp": actual_timestamp,
        f"{prefix}-auth-sign": signature,
    }
    return SignedRequest(request_method, outgoing_url, headers, body)


def _validate_url(url: str, parsed: SplitResult) -> None:
    if not parsed.scheme or not parsed.netloc:
        raise ValueError("The request URL must be absolute")
    if parsed.scheme.lower() not in {"http", "https"}:
        raise ValueError("The request URL must use HTTP or HTTPS")
    if parsed.fragment:
        raise ValueError("The request URL must not contain a fragment")
    try:
        url.encode("ascii")
    except UnicodeEncodeError as error:
        raise ValueError("The request URL must be ASCII and percent-encoded") from error
    if any(character.isspace() for character in url) or _INVALID_PERCENT_ESCAPE.search(url):
        raise ValueError("The request URL must be percent-encoded")


def _canonicalize_encoded_query(query: str) -> tuple[str, str]:
    kept: list[tuple[str, str, str]] = []
    for token in query.split("&"):
        if not token:
            continue
        key, separator, value = token.partition("=")
        if separator and value:
            kept.append((key, value, f"{key}={value}"))

    kept.sort(key=lambda item: item[2])
    parameters = "".join(f"{key.lower()}={value}" for key, value, _token in kept)
    outgoing_query = "&".join(token for _key, _value, token in kept)
    return parameters, outgoing_query


def _canonicalize_decoded_query(query: str) -> str:
    tokens = sorted(token for token in query.split("&") if token)
    parameters: list[str] = []
    for token in tokens:
        encoded_key, separator, encoded_value = token.partition("=")
        value = encoded_value if separator else ""
        parameters.append(f"{unquote(encoded_key).lower()}={unquote(value)}")
    return "".join(parameters)


def _new_nonce(uppercase: bool) -> str:
    nonce = uuid.uuid4().hex[:16]
    return nonce.upper() if uppercase else nonce


def _strip_dotnet_whitespace(value: str) -> str:
    # Match Char.IsWhiteSpace rather than Python's slightly broader isspace().
    controls = "\t\n\v\f\r\x85"
    return "".join(
        character
        for character in value
        if character not in controls and unicodedata.category(character) not in {"Zs", "Zl", "Zp"}
    )
