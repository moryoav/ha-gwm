"""Read-only direct-cloud runtime and bounded config-flow handoff.

A successful config, reauth, or reconfigure flow stages one validated overseas
read session for immediate entry setup. The bounded handoff is only an
in-process optimization; private account-bound storage owns restart recovery.
"""

from __future__ import annotations

import asyncio
import re
from collections.abc import Callable
from dataclasses import dataclass, field
from datetime import UTC, datetime
from typing import Never, Protocol

from homeassistant.core import HomeAssistant

from gwm_ora_client import (
    AnzAuthenticated,
    AnzAuthState,
    CloudVehicle,
    CloudVehicleBasics,
    CloudVehicleStatus,
    EuAuthenticated,
    EuAuthState,
    GwmClient,
    GwmClientConfig,
    GwmConfigurationError,
    GwmOptionalEndpointError,
    GwmProtocolError,
    GwmSession,
    Region,
    RussiaAuthenticated,
    RussiaAuthState,
    VehicleIdentifier,
    map_vehicle_snapshot,
)

from .api import GwmOraApiForbidden
from .cloud_auth import (
    CloudAuthenticationResult,
    DirectCloudCredentials,
    direct_unique_id,
)
from .const import (
    CONF_ACCOUNT,
    CONF_COUNTRY,
    CONF_PASSWORD,
    CONF_REGION,
    DOMAIN,
    REGION_ANZ,
    REGION_EU,
    REGION_RUSSIA,
)

_HANDOFF_TTL_SECONDS = 5 * 60
_HANDOFF_DATA_KEY = f"{DOMAIN}_direct_cloud_handoffs"
_ACCOUNT_BINDING = re.compile(r"[0-9a-f]{64}")


class _OverseasReadClient(Protocol):
    @property
    def authenticated(self) -> bool: ...

    async def acquire_vehicles(self) -> tuple[CloudVehicle, ...]: ...

    async def get_last_status(self, identifier: VehicleIdentifier) -> CloudVehicleStatus: ...

    async def get_vehicle_basics(
        self,
        identifier: VehicleIdentifier,
    ) -> CloudVehicleBasics: ...

    async def aclose(self) -> None: ...


class _AuthStateStore(Protocol):
    async def async_clear_auth_state(
        self,
        entry_data: dict[str, object],
    ) -> None: ...


@dataclass(frozen=True, slots=True, repr=False)
class DirectCloudBootstrap:
    """One validated, memory-only overseas session handoff."""

    region: str
    account_binding: str = field(repr=False)
    state: EuAuthState | AnzAuthState | RussiaAuthState = field(repr=False)
    session: GwmSession = field(repr=False)

    def __post_init__(self) -> None:
        if (
            self.region not in {REGION_EU, REGION_ANZ, REGION_RUSSIA}
            or not isinstance(self.account_binding, str)
            or _ACCOUNT_BINDING.fullmatch(self.account_binding) is None
            or type(self.session) is not GwmSession
            or not _bootstrap_state_matches(
                self.region,
                self.account_binding,
                self.state,
                self.session,
            )
        ):
            raise GwmConfigurationError(operation="login")

    @classmethod
    def from_authentication(
        cls,
        credentials: DirectCloudCredentials,
        result: CloudAuthenticationResult,
    ) -> DirectCloudBootstrap:
        """Create a handoff only from a complete overseas authentication."""

        valid_result = (
            credentials.region == REGION_EU
            and type(result) is EuAuthenticated
            or credentials.region == REGION_ANZ
            and type(result) is AnzAuthenticated
            or credentials.region == REGION_RUSSIA
            and type(result) is RussiaAuthenticated
        )
        if not valid_result or not isinstance(
            result,
            (EuAuthenticated, AnzAuthenticated, RussiaAuthenticated),
        ):
            raise GwmConfigurationError(operation="login")
        if (
            result.session.device_id != credentials.device_id
            or result.session.country != credentials.country
        ):
            raise GwmConfigurationError(operation="login")
        return cls(
            region=credentials.region,
            account_binding=credentials.account_binding,
            state=result.state,
            session=result.session,
        )


@dataclass(slots=True)
class _PendingBootstrap:
    bootstrap: DirectCloudBootstrap
    expiry: asyncio.TimerHandle


def stage_direct_cloud_bootstrap(
    hass: HomeAssistant,
    unique_id: str,
    bootstrap: DirectCloudBootstrap,
) -> None:
    """Stage one bounded, replaceable handoff for config-entry setup."""

    if not isinstance(unique_id, str) or not unique_id.startswith("cloud:"):
        raise GwmConfigurationError(operation="login")
    store = hass.data.setdefault(_HANDOFF_DATA_KEY, {})
    if not isinstance(store, dict):
        raise GwmConfigurationError(operation="login")
    previous = store.pop(unique_id, None)
    if isinstance(previous, _PendingBootstrap):
        previous.expiry.cancel()

    pending: _PendingBootstrap

    def expire() -> None:
        if store.get(unique_id) is pending:
            store.pop(unique_id, None)

    pending = _PendingBootstrap(
        bootstrap=bootstrap,
        expiry=asyncio.get_running_loop().call_later(_HANDOFF_TTL_SECONDS, expire),
    )
    store[unique_id] = pending


def consume_direct_cloud_bootstrap(
    hass: HomeAssistant,
    unique_id: str | None,
) -> DirectCloudBootstrap | None:
    """Consume exactly one staged handoff."""

    if not isinstance(unique_id, str):
        return None
    store = hass.data.get(_HANDOFF_DATA_KEY)
    if not isinstance(store, dict):
        return None
    pending = store.pop(unique_id, None)
    if not isinstance(pending, _PendingBootstrap):
        return None
    pending.expiry.cancel()
    return pending.bootstrap


class DirectCloudReadClient:
    """Own one authenticated overseas client and normalize account reads."""

    def __init__(
        self,
        region: str,
        client: _OverseasReadClient,
        *,
        clock: Callable[[], datetime] | None = None,
        bootstrap: DirectCloudBootstrap | None = None,
        state_store: _AuthStateStore | None = None,
        entry_data: dict[str, object] | None = None,
    ) -> None:
        if region not in {REGION_EU, REGION_ANZ, REGION_RUSSIA}:
            raise GwmConfigurationError(operation="request")
        self.region = region
        self._client = client
        self._clock = clock or (lambda: datetime.now(UTC))
        self._bootstrap = bootstrap
        self._state_store = state_store
        self._entry_data = dict(entry_data or {})

    @classmethod
    def from_entry_data(
        cls,
        data: dict[str, object],
        unique_id: str | None,
        bootstrap: DirectCloudBootstrap,
        *,
        state_store: _AuthStateStore | None = None,
    ) -> DirectCloudReadClient:
        """Validate a staged handoff against the current config entry."""

        if not isinstance(unique_id, str):
            raise GwmConfigurationError(operation="login")
        credentials = DirectCloudCredentials(
            region=str(data.get(CONF_REGION, "")),
            country=str(data.get(CONF_COUNTRY, "")),
            account=str(data.get(CONF_ACCOUNT, "")),
            password=(
                str(data[CONF_PASSWORD])
                if isinstance(data.get(CONF_PASSWORD), str)
                else None
            ),
            device_id=bootstrap.session.device_id,
        )
        if (
            bootstrap.region != credentials.region
            or bootstrap.account_binding != credentials.account_binding
            or bootstrap.session.country != credentials.country
            or unique_id != direct_unique_id(credentials)
        ):
            raise GwmConfigurationError(operation="login")

        try:
            region = Region(credentials.region)
        except ValueError:
            raise GwmConfigurationError(operation="login") from None
        client = GwmClient(GwmClientConfig(region), session=bootstrap.session)
        return cls(
            credentials.region,
            client,
            bootstrap=bootstrap,
            state_store=state_store,
            entry_data=data,
        )

    @property
    def reusable_bootstrap(self) -> DirectCloudBootstrap | None:
        """Return the current in-process handoff if authentication still holds."""

        return self._bootstrap if self._client.authenticated else None

    async def async_get_vehicle_data(self) -> dict[str, object]:
        """Perform one atomic account discovery/status/basics refresh."""

        vehicles = await self._client.acquire_vehicles()
        records: list[tuple[CloudVehicle, CloudVehicleStatus, CloudVehicleBasics]] = []
        for vehicle in vehicles:
            status = await self._client.get_last_status(vehicle.identifier)
            try:
                basics = await self._client.get_vehicle_basics(vehicle.identifier)
            except GwmOptionalEndpointError:
                if self.region != REGION_ANZ:
                    raise
                basics = CloudVehicleBasics()
            records.append((vehicle, status, basics))

        refreshed_at = self._clock()
        try:
            snapshots = [
                map_vehicle_snapshot(
                    vehicle,
                    status,
                    basics,
                    refreshed_at=refreshed_at,
                    # Write support is deliberately unavailable until Tasks
                    # 17-20, even when the user has opted in already.
                    remote_commands_available=False,
                ).as_dict()
                for vehicle, status, basics in records
            ]
        except (TypeError, ValueError):
            raise GwmProtocolError(operation="request") from None

        return {
            "region": self.region,
            "remote_commands_enabled": False,
            "charging_control_enabled": False,
            "vehicles": snapshots,
        }

    async def aclose(self) -> None:
        """Close the owned regional transport."""

        await self._client.aclose()

    async def async_authentication_rejected(self) -> None:
        """Retire a rejected durable revision without logging its contents."""

        self._bootstrap = None
        if self._state_store is not None:
            await self._state_store.async_clear_auth_state(self._entry_data)


def _bootstrap_state_matches(
    region: str,
    account_binding: str,
    state: object,
    session: GwmSession,
) -> bool:
    expected = {
        REGION_EU: EuAuthState,
        REGION_ANZ: AnzAuthState,
        REGION_RUSSIA: RussiaAuthState,
    }.get(region)
    if expected is None or type(state) is not expected:
        return False
    return (
        state.account_binding == account_binding
        and state.country == session.country
        and state.device_id == session.device_id
        and state.access_token is not None
        and state.access_token == session.access_token
    )


class DirectReadOnlyCommandApi:
    """Fail closed if a deferred write surface is invoked directly."""

    @staticmethod
    def _unavailable() -> Never:
        raise GwmOraApiForbidden("Direct-cloud writes are not available yet")

    async def async_refresh(self) -> dict[str, object]:
        self._unavailable()

    async def async_get_vehicles(self) -> dict[str, object]:
        self._unavailable()

    async def async_get_command(self, command_id: str) -> dict[str, object]:
        self._unavailable()

    async def async_set_climate(self, *args: object, **kwargs: object) -> dict[str, object]:
        self._unavailable()

    async def async_lock(self, *args: object, **kwargs: object) -> dict[str, object]:
        self._unavailable()

    async def async_close_windows(self, *args: object, **kwargs: object) -> dict[str, object]:
        self._unavailable()

    async def async_vehicle_control(self, *args: object, **kwargs: object) -> dict[str, object]:
        self._unavailable()

    async def async_get_charging_plan(self, *args: object, **kwargs: object) -> dict[str, object]:
        self._unavailable()

    async def async_set_charging_plan(self, *args: object, **kwargs: object) -> dict[str, object]:
        self._unavailable()


__all__ = [
    "DirectCloudBootstrap",
    "DirectCloudReadClient",
    "DirectReadOnlyCommandApi",
    "consume_direct_cloud_bootstrap",
    "stage_direct_cloud_bootstrap",
]
