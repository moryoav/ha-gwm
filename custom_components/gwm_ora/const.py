"""Constants for the GWM ORA integration."""

from __future__ import annotations

from homeassistant.const import Platform

DOMAIN = "gwm_ora"
DEFAULT_NAME = "GWM ORA"
DEFAULT_PORT = 8099
CONF_TOKEN = "token"
CONF_API_VERSION = "api_version"
CONF_SLUG = "slug"

PLATFORMS: list[Platform] = [
    Platform.SENSOR,
    Platform.BINARY_SENSOR,
    Platform.DEVICE_TRACKER,
    Platform.CLIMATE,
    Platform.LOCK,
    Platform.BUTTON,
    Platform.NUMBER,
    Platform.SWITCH,
]

# AU/NZ charging schedule control (behind the add-on's enable_charging_control opt-in)
SERVICE_SET_CHARGING_PLAN = "set_charging_plan"
SERVICE_CLEAR_CHARGING_PLAN = "clear_charging_plan"
ATTR_VIN = "vin"
ATTR_ENABLE = "enable"
ATTR_START_TIME = "start_time"
ATTR_END_TIME = "end_time"
# Duration of the window the manual switch sets when turned on.
DEFAULT_CHARGE_WINDOW_HOURS = 8
