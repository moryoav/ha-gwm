# GWM

This custom integration connects Home Assistant to the local **GWM** add-on and exposes vehicles available through the connected GWM account.

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
- Switch

Vehicle models and regions expose different status signals. Optional fuel, comfort, and diagnostic entities are disabled by default where appropriate, and missing values remain unknown without affecting other entities.

The **Climate run time** number entity saves a duration from 5 to 30 minutes, in one-minute steps, for the next A/C command. Changing it does not start or stop the A/C.

For experimental mainland-China accounts, status reading supports NavInfo and BeanTech vehicles. BeanTech status was live-tested on a Tank 300 Hi4-T. NavInfo climate also offers heating and the button platform exposes remote start/stop, horn/lights, tailgate, and sunroof controls when remote commands are enabled. BeanTech currently exposes unverified mappings for lock/unlock, close windows, remote start/stop, horn, flashing lights, and closing the sunroof. Test one command at a time with the vehicle parked and visible.

The optional **Scheduled charging** switch and `gwm_ora.set_charging_plan` / `gwm_ora.clear_charging_plan` actions require charging control to be enabled separately. Use `enable_charging_control: true` for an add-on entry, or enable it in the direct cloud entry options on the integration-only branch. Direct China remains behind its validation gate. The NavInfo weekly schedule contract is offline-tested, while BeanTech charging remains unavailable. See the root README for behavior, safety notes, and examples.

## Setup

1. Install and start the `GWM` add-on.
2. Open HACS, search for **GWM** under **Integrations**, select it, and choose **Download**.
3. Restart Home Assistant.
4. Open **Settings** > **Devices & services** and confirm the discovered **GWM** integration.

If discovery does not appear, restart the add-on and then restart Home Assistant.

## Reconfigure and Reauth

Normal add-on installs should update automatically when Supervisor rediscovery publishes new host or token information.

Manual/development installs can use the integration reconfigure flow to update host, port, and token. If the add-on rejects the stored API token, Home Assistant will start reauthentication and raise a repair issue.

## Diagnostics

Diagnostics redact the generated add-on API token. Review diagnostics before sharing because vehicle snapshots can still contain VINs, timestamps, raw item codes, and location data.

## Quality Scale

Progress toward Home Assistant Integration Quality Scale rules is tracked in `quality_scale.yaml`.
