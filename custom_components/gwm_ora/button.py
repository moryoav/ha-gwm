"""Button platform for GWM."""

from __future__ import annotations

from homeassistant.components.button import ButtonEntity
from homeassistant.core import HomeAssistant
from homeassistant.helpers.entity_platform import AddEntitiesCallback

from . import GwmOraConfigEntry
from .entity import GwmOraEntity, async_call_addon_api, setup_vehicle_entities

PARALLEL_UPDATES = 0


async def async_setup_entry(
    hass: HomeAssistant,
    entry: GwmOraConfigEntry,
    async_add_entities: AddEntitiesCallback,
) -> None:
    """Set up GWM buttons."""
    setup_vehicle_entities(
        entry,
        async_add_entities,
        lambda vehicle: (
            GwmOraCloseWindowsButton(entry.runtime_data.api, entry.runtime_data.coordinator, vehicle["vin"]),
        ),
    )


class GwmOraCloseWindowsButton(GwmOraEntity, ButtonEntity):
    """Button that closes all windows."""

    _attr_translation_key = "close_windows"

    def __init__(self, api, coordinator, vin: str) -> None:
        super().__init__(coordinator, vin)
        self._api = api
        self._attr_unique_id = f"{vin}_close_windows"

    @property
    def available(self) -> bool:
        """Return whether close-window commands are available."""
        return super().available and self.remote_commands_available

    async def async_press(self) -> None:
        """Close windows."""
        command = await async_call_addon_api(self._api.async_close_windows(self.vin))
        self.coordinator.async_track_command(command)
