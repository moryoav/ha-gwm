"""Home Assistant-facing errors for the GWM integration."""

from __future__ import annotations


class GwmCommandError(Exception):
    """Raised when a command cannot be validated or completed."""


class GwmCommandForbidden(GwmCommandError):
    """Raised when an explicitly disabled command surface is used."""
