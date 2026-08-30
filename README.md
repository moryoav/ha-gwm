# GWM for Home Assistant

[![GitHub Release][release-badge]][release-url]
[![HACS][hacs-badge]][hacs-url]
[![License][license-badge]][license-url]
[![Ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/moryoav)

This custom integration connects Home Assistant directly to supported regional GWM cloud services. It discovers vehicles on the account, creates native Home Assistant entities, polls vehicle status, and provides explicitly enabled remote controls.

The `feature/integration-only` branch is a live-testing checkpoint. The previous Docker add-on is no longer part of this branch. Europe, Australia and New Zealand, and Russia are available in the setup flow. Mainland China remains disabled until its separate direct-client live-read gate passes.

## Important Upgrade Note

This branch does not import credentials, tokens, or state from the retired add-on.

If you are updating an existing installation, you must:

1. Remove the existing **GWM** integration entry from **Settings** > **Devices & services**.
2. Stop the old GWM add-on.
3. Install this branch of the integration.
4. Restart Home Assistant.
5. Add **GWM** again and complete a fresh sign-in.
6. Confirm that polling and entities work before enabling remote commands.
7. Uninstall the old add-on after you no longer need it as a rollback reference.

I chose a fresh sign-in because it is simpler, easier to audit, and avoids transferring passwords, tokens, certificates, device identities, and command state between two different storage designs.

## Supported Accounts

The setup flow currently offers:

- Europe, including EU countries, the United Kingdom, and Israel.
- Australia and New Zealand.
- Russia.

The account region must match the region used by the official GWM app. It is not based on the vehicle's current location.

The project has been tested with these vehicles:

- GWM ORA 03, model year 2023.
- GWM ORA 05, model year 2023.
- GWM ORA 5, Australia and New Zealand model.
- GWM ORA 1, Russia model.
- WEY VV6, mainland-China NavInfo platform.
- Tank 300 Hi4-T, mainland-China BeanTech platform.

Other compatible GWM vehicles may also work. If you test another model, please [open an issue](https://github.com/moryoav/ha-gwm/issues/new/choose) with the model, account region, and features you verified. Never include credentials, tokens, verification codes, VINs, or exact locations.

## Test Installation

This branch is not the current production release in HACS. Back up Home Assistant before testing it.

1. Download the [`feature/integration-only` branch archive](https://github.com/moryoav/ha-gwm/archive/refs/heads/feature/integration-only.zip).
2. Extract `custom_components/gwm_ora` from the archive.
3. Replace `/config/custom_components/gwm_ora` with that folder.
4. Restart Home Assistant.
5. Open **Settings** > **Devices & services** > **Add integration**.
6. Search for **GWM**.
7. Select the account region and complete authentication.

Home Assistant installs the test-only `gwm-client` dependency from an exact GitHub commit recorded in the integration manifest. I use an immutable source archive so the test cannot silently change when the branch advances. Before a production release, I will publish the client through the approved package workflow and replace this test dependency with a pinned PyPI version.

The integration requires Home Assistant 2026.1.0 or newer.

## Authentication

Enter the account details used by the official GWM app. The integration authenticates directly with the selected GWM cloud.

GWM may send a one-time verification code during first setup or reauthentication. Enter the code in the Home Assistant flow. Verification codes are not stored.

For European accounts, the message may come from `noreply@gwm-eu.com` with the subject `GWM Verification Code`.

<img src="https://raw.githubusercontent.com/moryoav/ha-gwm/main/docs/images/gwm-verification-code-email.jpeg" alt="Example GWM verification code email" width="320">

Australia and New Zealand accounts normally permit one active session. The setup flow requires explicit confirmation before it can replace the official app session. I recommend a dedicated account that has been shared access to the vehicle.

The integration privately stores the generated device identity and account-bound authentication state so it can resume after a Home Assistant restart. Passwords and the optional vehicle security PIN are redacted from diagnostics.

## Options

Open the GWM integration entry and select **Configure** to set:

- **Polling interval**: 30 to 3600 seconds. The default is 60 seconds.
- **Enable remote commands**: Off by default.
- **Vehicle security PIN**: Required for remote commands in the currently enabled regions.
- **Enable charging control**: Off by default and independent of remote commands.
- **Log level**: Integration-specific diagnostic verbosity.

Start with both control options disabled. Confirm read-only vehicle data first. Enable one control family at a time only while the vehicle is parked somewhere safe and visible.

## Entities

Depending on the vehicle and region, the integration can create:

- Sensors for battery state of charge, range, odometer, charging state, remaining charging time, tire values, timestamps, fuel values, comfort values, and diagnostic status codes.
- Binary sensors for charging, charge plug, climate, locks, windows, doors, trunk, air circulation, and defrosters.
- A device tracker when the cloud supplies a valid location.
- A climate entity for remote A/C.
- A number entity for the next climate run time.
- A lock entity for door lock and unlock.
- A button for closing all windows.
- A scheduled charging switch.

Missing vehicle signals remain unavailable without interrupting the other entities. Model-specific diagnostic entities are disabled by default where appropriate.

## Remote Commands

Remote commands are slower than ordinary Home Assistant operations because the request travels through the GWM cloud and then waits for the vehicle result. The **Remote command status** sensor shows the current progress.

Set **Climate run time** before starting A/C if you want a duration other than the default. Changing the number only saves the setting. It does not start the climate system.

Remote operations can affect a real vehicle. Test them manually before using them in automations.

## Charging Schedule Control

Charging control has its own opt-in. It does not use the vehicle security PIN.

The **Scheduled charging** switch is a convenience control:

- Turning it on creates an eight-hour charging window starting now.
- Turning it off clears the schedule. This is not a hard stop command, so a connected vehicle may begin charging after the schedule is cleared.

For an exact window, use `gwm_ora.set_charging_plan`:

```yaml
action: gwm_ora.set_charging_plan
data:
  vin: "LGWTEST00XX000001"
  start_time: "2026-08-30 23:00:00+03:00"
  end_time: "2026-08-31 06:00:00+03:00"
```

To clear it:

```yaml
action: gwm_ora.clear_charging_plan
data:
  vin: "LGWTEST00XX000001"
```

The integration records the exact plan it writes. If charging control is later disabled, it retries cleanup only while that exact plan is still present. It leaves schedules changed by the official app untouched.

Charging control was live-tested on an Australia and New Zealand ORA 5 through the previous implementation. The Python integration path is fixture-tested but still needs direct live confirmation.

## evcc

The **Charging status** sensor reports `disconnected`, `connected`, `charging`, `charging_complete`, `awaiting_charging`, `waiting_for_power`, or `error`.

For evcc, include the two GWM waiting states in status B:

```yaml
vehicles:
  - name: gwm_vehicle
    type: template
    template: homeassistant
    uri: http://homeassistant.local:8123
    soc: sensor.gwm_vehicle_soc
    status: sensor.gwm_vehicle_charging_status
    statusB: awaiting_charging, waiting_for_power
```

Replace the example entity IDs with the IDs from your Home Assistant installation.

## Troubleshooting

### The integration does not appear

- Confirm that `/config/custom_components/gwm_ora/manifest.json` exists.
- Restart Home Assistant after replacing the integration folder.
- Clear the browser cache if the integration list is stale.
- Check the Home Assistant log for dependency installation or import errors.

### An old entry requests reauthentication

The previous add-on entry cannot be converted. Remove that entry and add GWM again. The new flow will ask for the GWM account directly.

### Sign-in fails

- Confirm the same account works in the official GWM app.
- Confirm that the selected cloud region and registration country are correct.
- Enter any requested one-time code before it expires.
- For Australia and New Zealand, confirm the single-session warning if you want the integration to take the active session.

### Entities are unavailable

- Wait for the first complete account poll.
- Confirm that the official app currently shows the vehicle.
- Check whether the vehicle supplies the related signal.
- Review Home Assistant logs after removing personal or secret values.

### Remote controls are unavailable

- Enable remote commands in the integration options.
- Enter the correct vehicle security PIN.
- Reload the integration after changing options.
- Confirm read-only polling works before testing a command.

## Mainland China

The Python client contains isolated NavInfo and BeanTech protocol implementations with extensive offline tests. I have not enabled China in the Home Assistant setup flow because each claimed platform still needs a separately approved, sanitized live read through the Python path.

The previous add-on's China results remain useful evidence, but they do not prove that the new client transport is ready for production. China testing will be a separate checkpoint.

## Removal

1. Remove the **GWM** integration entry from Home Assistant.
2. Remove the integration from HACS, or delete `/config/custom_components/gwm_ora`.
3. Restart Home Assistant.

Removing the entry also removes its private integration-owned authentication and command state.

## Privacy and Safety

The integration handles GWM account credentials, authentication tokens, a generated device identity, an optional vehicle security PIN, vehicle identifiers, and potentially precise location data.

Diagnostics redact known credentials, tokens, identifiers, and locations. Review every diagnostic file before sharing it.

Never publish raw cloud responses, packet captures, account data, verification codes, private keys, VINs, or exact vehicle locations.

## Naming and Compatibility

I use **GWM** for the project and new code because support is not limited to ORA vehicles. The Python distribution is `gwm-client`, and the import package is `gwm_client`.

I retain `gwm_ora` as the Home Assistant domain and action namespace. Changing the domain would break entity and device registry links, automations, dashboards, and existing Home Assistant references. The compatibility identifier does not limit supported vehicle brands or models.

Historical ORA test results and attribution to `ora2mqtt` remain named where they are factually relevant.

## Protocol Materials

Some protocol values and bootstrap materials were obtained through interoperability research on official GWM applications. I record their sources, hashes, certificate renewal deadlines, and unresolved redistribution conditions in [Third-Party and Protocol Material Notice](THIRD_PARTY_NOTICES.md).

The live-testing branch is not a production release. Package publication and a production release remain blocked until the recorded permission or authorized-replacement conditions are resolved.

## Disclaimer

This project is unofficial and is not affiliated with or endorsed by Great Wall Motor, GWM, or Home Assistant. Vehicle cloud APIs and remote command behavior may change without notice.

Use it at your own risk. You are responsible for protecting credentials, keeping backups, validating behavior, and deciding whether remote commands are appropriate for your vehicle and environment.

## Special Thanks

Special thanks to [zivillian](https://github.com/zivillian) and [zivillian/ora2mqtt](https://github.com/zivillian/ora2mqtt) for the original interoperability work that helped make this project possible.

Thanks to [AlexandrErohin](https://github.com/AlexandrErohin) for the initial model-specific sensors and Russia support.

Deep thanks to [wilberforce](https://github.com/wilberforce) for the Australia and New Zealand authentication and signing work, vehicle status mappings, and live validation.

[hacs-badge]: https://img.shields.io/badge/HACS-Default-41BDF5.svg?style=flat-square
[hacs-url]: https://github.com/hacs/integration
[release-badge]: https://img.shields.io/github/v/release/moryoav/ha-gwm?style=flat-square
[release-url]: https://github.com/moryoav/ha-gwm/releases
[license-badge]: https://img.shields.io/github/license/moryoav/ha-gwm?style=flat-square
[license-url]: https://github.com/moryoav/ha-gwm/blob/main/LICENSE
