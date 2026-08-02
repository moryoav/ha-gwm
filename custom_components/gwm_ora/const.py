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
]
