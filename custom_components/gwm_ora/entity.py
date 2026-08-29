"""Base entities for GWM."""

from __future__ import annotations

from collections.abc import Callable, Iterable
from typing import TYPE_CHECKING, Any

from homeassistant.exceptions import HomeAssistantError
from homeassistant.helpers.device_registry import DeviceInfo
from homeassistant.helpers.entity_platform import AddEntitiesCallback
from homeassistant.helpers.update_coordinator import CoordinatorEntity

from .api import GwmOraApiAuthError, GwmOraApiError, GwmOraApiForbidden, GwmOraApiUnavailable
from .const import DOMAIN
from .coordinator import GwmOraDataUpdateCoordinator

if TYPE_CHECKING:
    from . import GwmOraConfigEntry


class GwmOraEntity(CoordinatorEntity[GwmOraDataUpdateCoordinator]):
    """Base entity bound to one GWM vehicle."""

    _attr_has_entity_name = True

    def __init__(self, coordinator: GwmOraDataUpdateCoordinator, vin: str) -> None:
        super().__init__(coordinator)
        self.vin = vin

    @property
    def vehicle(self) -> dict[str, Any] | None:
        """Return the current vehicle snapshot."""
        return self.coordinator.vehicle(self.vin)

    @property
    def available(self) -> bool:
        """Return whether the entity is available."""
        return super().available and self.vehicle is not None

    @property
    def device_info(self) -> DeviceInfo:
        """Return device registry info."""
        vehicle = self.vehicle or {}
        return DeviceInfo(
            identifiers={(DOMAIN, self.vin)},
            name=vehicle.get("name") or "GWM vehicle",
            manufacturer=vehicle.get("manufacturer") or "GWM",
            model=vehicle.get("model"),
            serial_number=vehicle.get("serial_number"),
        )

    @property
    def remote_commands_available(self) -> bool:
        """Return whether remote commands are available for this vehicle."""
        vehicle = self.vehicle or {}
        capabilities = vehicle.get("capabilities") or {}
        return bool(capabilities.get("remote_commands"))

    @property
    def climate_commands_available(self) -> bool:
        """Return the climate-specific capability with add-on compatibility."""

        vehicle = self.vehicle or {}
        capabilities = vehicle.get("capabilities") or {}
        return bool(capabilities.get("climate_commands", capabilities.get("remote_commands")))

    @property
    def lock_window_commands_available(self) -> bool:
        """Return the lock/window capability with add-on compatibility."""

        vehicle = self.vehicle or {}
        capabilities = vehicle.get("capabilities") or {}
        return bool(capabilities.get("lock_window_commands", capabilities.get("remote_commands")))

    @property
    def china_vehicle_commands_available(self) -> bool:
        """Return the extended-China capability with add-on compatibility."""

        vehicle = self.vehicle or {}
        capabilities = vehicle.get("capabilities") or {}
        return bool(
            capabilities.get("china_vehicle_commands", capabilities.get("remote_commands"))
        )

    @property
    def vehicle_platform(self) -> str:
        """Return the normalized vehicle backend platform."""
        return str((self.vehicle or {}).get("platform") or "").lower()

    @property
    def is_china_beantech(self) -> bool:
        """Return whether this is a BeanTech vehicle on the China gateway."""
        return self.coordinator.region == "cn" and self.vehicle_platform == "beantech"

    @property
    def charging_control_available(self) -> bool:
        """Return whether charging control is available for this vehicle."""
        return _vehicle_charging_control_available(
            self.vehicle,
            self.coordinator.data,
        )


def _vehicle_charging_control_available(
    vehicle: dict[str, Any] | None,
    coordinator_data: dict[str, Any] | None,
) -> bool:
    """Return the per-vehicle capability with old add-on fallback."""
    capabilities = (vehicle or {}).get("capabilities") or {}
    if "charging_control" in capabilities:
        return bool(capabilities["charging_control"])
    return bool((coordinator_data or {}).get("charging_control_enabled"))


def vehicle_value(vehicle: dict[str, Any] | None, key: str) -> Any:
    """Return a value from a vehicle snapshot."""
    if vehicle is None:
        return None
    return (vehicle.get("values") or {}).get(key)


def setup_vehicle_entities(
    entry: GwmOraConfigEntry,
    async_add_entities: AddEntitiesCallback,
    factory: Callable[[dict[str, Any]], Iterable[GwmOraEntity]],
) -> None:
    """Add entities for all current and newly discovered vehicles."""
    coordinator = entry.runtime_data.coordinator
    known_vins: set[str] = set()

    def add_new_vehicle_entities() -> None:
        entities: list[GwmOraEntity] = []
        for vehicle in coordinator.vehicles:
            vin = vehicle.get("vin")
            if not vin or vin in known_vins:
                continue
            known_vins.add(vin)
            entities.extend(factory(vehicle))
        if entities:
            async_add_entities(entities)

    add_new_vehicle_entities()
    entry.async_on_unload(coordinator.async_add_listener(add_new_vehicle_entities))


async def async_call_addon_api(
    call,
    *,
    forbidden_translation_key: str = "remote_command_unavailable",
):
    """Call the add-on API and raise translated Home Assistant errors."""
    try:
        return await call
    except GwmOraApiAuthError as err:
        raise HomeAssistantError(
            translation_domain=DOMAIN,
            translation_key="addon_auth_failed",
        ) from err
    except GwmOraApiForbidden as err:
        raise HomeAssistantError(
            translation_domain=DOMAIN,
            translation_key=forbidden_translation_key,
        ) from err
    except GwmOraApiUnavailable as err:
        raise HomeAssistantError(
            translation_domain=DOMAIN,
            translation_key="addon_unavailable",
        ) from err
    except GwmOraApiError as err:
        raise HomeAssistantError(
            translation_domain=DOMAIN,
            translation_key="addon_request_failed",
        ) from err
