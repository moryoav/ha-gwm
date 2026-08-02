# GWM ORA

This custom integration connects Home Assistant to the local **GWM ORA** add-on.

For installation buttons, add-on setup, security notes, examples, and troubleshooting, see the repository root `README.md`.

The integration stores only the add-on host, port, generated API token, and discovery metadata. GWM credentials and the vehicle security PIN remain in add-on configuration/storage.

## Platforms

- Sensor
- Binary sensor
- Device tracker
- Climate
- Number
- Lock
- Button

The **Climate run time** number entity saves a duration from 5 to 30 minutes, in one-minute steps, for the next A/C command. Changing it does not start or stop the A/C.

## Setup

1. Install and start the `GWM ORA` add-on.
2. Open HACS, search for **GWM ORA** under **Integrations**, select it, and choose **Download**.
3. Restart Home Assistant.
4. Open **Settings** > **Devices & services** and confirm the discovered **GWM ORA** integration.

If discovery does not appear, restart the add-on and then restart Home Assistant.

## Reconfigure and Reauth

Normal add-on installs should update automatically when Supervisor rediscovery publishes new host or token information.

Manual/development installs can use the integration reconfigure flow to update host, port, and token. If the add-on rejects the stored API token, Home Assistant will start reauthentication and raise a repair issue.

## Diagnostics

Diagnostics redact the generated add-on API token. Review diagnostics before sharing because vehicle snapshots can still contain VINs, timestamps, raw item codes, and location data.

## Quality Scale

Progress toward Home Assistant Integration Quality Scale rules is tracked in `quality_scale.yaml`.
