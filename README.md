# GWM for Home Assistant
[![HACS][hacs-badge]][hacs-url] [![release][release-badge]][release-url] [![license][license-badge]][license-url]

---

## Support me on Ko-fi

If this project is useful to you, you can support its continued development:

[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/Y5B124NZ2L)

---

<img alt="GWM" src="https://www.gwm.com.my/content/dam/gwm/pages/my/en/logo/gwm-black-pc.svg">

Control and monitor your GWM vehicle from Home Assistant. The add-on connects to your GWM account, and the Home Assistant integration creates sensors and controls for your vehicle.

## Tested Vehicles

This integration has been tested with:

- ORA 03
- ORA 05
- HAVAL H3

Other GWM models may also work. If you try the integration with another model, please [open a GitHub issue](https://github.com/moryoav/ha-gwm/issues/new/choose) and report the model, region, and which features you tested so this list can be expanded.

## What You Get

- Battery SOC, range, odometer, charging, plug, cabin temperature, tire, lock, window, door, trunk, A/C, and location entities, plus model-dependent fuel and comfort data.
- Native Home Assistant controls for A/C mode, temperature, run time, door lock/unlock, and closing windows. Experimental China support also exposes remote start/stop, vehicle search, tailgate, and sunroof controls.
- A remote command status sensor that shows progress while commands are being sent to the car.
- Automatic discovery of the add-on by the integration.
- A small add-on Web UI showing add-on health and the latest cached vehicle summary.

Remote commands can take time. The car may report several pending attempts before a command succeeds, especially for A/C and locking. Watch the **Remote command status** sensor after pressing a command.

## Installation

### 1. Add the Add-on Repository

[![Add the GWM add-on repository to Home Assistant](https://my.home-assistant.io/badges/supervisor_add_addon_repository.svg)](https://my.home-assistant.io/redirect/supervisor_add_addon_repository/?repository_url=https%3A%2F%2Fgithub.com%2Fmoryoav%2Fha-gwm)

Manual path:

1. Go to **Settings** -> **Apps**.
2. Select **Install app**.
3. Open **Repositories** from the menu.
4. Add:

```text
https://github.com/moryoav/ha-gwm
```

### 2. Install and Configure the Add-on

Install **GWM**, then fill in the add-on options. Select the cloud region that serves the account.

```yaml
region: eu
country: your-gwm-country-code
username: owner@example.com
password: your-gwm-password
enable_remote_commands: true
enable_charging_control: false
security_pin: "your-official-app-pin"
poll_interval_seconds: 60
log_level: info
```

- `region`: Cloud gateway for the account. Use `eu` for Europe/Israel, `aus` for Australia/New Zealand, `rus` for Russia, or experimental `cn` for mainland China.
- `country`: Two-letter country where the GWM account was registered, such as `DE`, `GB`, `AU`, `NZ`, `RU`, or `CN`. It must match the account registration country.
- `username`: E-mail address for the account, or its registered phone number when using `cn`.
- `password`: Password for `eu`, `aus`, and `rus`. China uses SMS login and ignores this field.
- `enable_remote_commands`: Enables A/C, lock, unlock, and close-window controls. In China it also enables the experimental remote start/stop, vehicle-search, tailgate, and sunroof buttons. Use `false` for read-only entities.
- `enable_charging_control`: Enables the **Scheduled charging** switch and the `gwm_ora.set_charging_plan` / `gwm_ora.clear_charging_plan` actions. Default `false`, independent of `enable_remote_commands`, and needs no security PIN. Validated on an ANZ vehicle; the experimental China implementation uses the corresponding China-app command and is untested on a live vehicle.
- `security_pin`: The remote-control PIN configured in the official GWM app. It is a prerequisite for remote commands outside China. The China app protocol does not send it.
- `poll_interval_seconds`: How often the add-on refreshes vehicle data from GWM.
- `log_level`: Add-on logging verbosity.

#### First-login GWM verification

When the add-on logs in for the first time, GWM may send a one-time verification code by SMS or e-mail. China always uses SMS login. Check the phone messages and e-mail inbox for your GWM account, including spam or junk folders. For European accounts, the e-mail will most likely come from `noreply@gwm-eu.com` with the subject `GWM Verification Code`.

<img src="https://raw.githubusercontent.com/moryoav/ha-gwm/main/docs/images/gwm-verification-code-email.jpeg" alt="Example GWM Verification Code e-mail" width="320">

After you receive the code:

1. Go back to the **GWM** add-on **Configuration** page.
2. Click **Show unused optional configuration options**.
3. Fill in **Verification code** (`verification_code`) with the one-time code.
4. Save the configuration.
5. Restart the add-on.

After successful authentication, the add-on Web UI should show **Authenticated** as **Yes** and **Verification** as **Not required**.

![GWM add-on authenticated status](https://raw.githubusercontent.com/moryoav/ha-gwm/main/docs/images/gwm-addon-authenticated.jpg)

### 3. Install the Integration

#### HACS

[![Open the GWM HACS repository](https://my.home-assistant.io/badges/hacs_repository.svg)](https://my.home-assistant.io/redirect/hacs_repository/?owner=moryoav&repository=ha-gwm&category=integration)

GWM is available in the default HACS catalog, so no custom repository setup is required.

1. Select the button above, or open HACS and search for **GWM** under **Integrations**.
2. Select **GWM** and choose **Download**.
3. Restart Home Assistant.

### 4. Add the Integration

[![Add the GWM integration](https://my.home-assistant.io/badges/config_flow_start.svg)](https://my.home-assistant.io/redirect/config_flow_start/?domain=gwm_ora)

After Home Assistant restarts, open **Settings** -> **Devices & services**. The integration should discover the running add-on as **GWM**; select it and confirm setup. If it is not discovered, select **Add integration** and search for **GWM**.

You do not enter your GWM username or password in the integration.

## Entities

- Sensors: SOC, range, odometer, charging status, remaining charging time, SOCE, tire pressures, tire temperatures, interior temperature, optional fuel level/range and seat heating/ventilation levels, acquisition/update timestamps, and remote command status.
- Binary sensors: charging active, charge plug, A/C active, lock open, driver/passenger windows and doors, trunk, air circulation, defrosters, and optional steering-wheel and windscreen heater states.
- Disabled diagnostic sensors: raw tire state, window-learning state, engine state code, sunroof position code, and GPS authorization data when supplied by the vehicle.
- Device tracker: vehicle GPS location when available.
- Climate: A/C mode `off`/`cool`, target temperature, current cabin temperature. Experimental China support also offers `heat`.
- Number: climate run time from 5 to 30 minutes in one-minute steps.
- Lock: lock and unlock vehicle doors.
- Button: close all windows. China-only experimental buttons include remote start/stop, horn, lights, combined vehicle search, tailgate open/close, and four sunroof positions.
- Switch: enable an eight-hour charging window now or clear the vehicle's charging schedule.

Remote command entities are unavailable until remote commands are enabled and, outside China, a security PIN is configured in the add-on.
The **Scheduled charging** switch is unavailable until charging control is enabled separately.

GWM models and regions do not all return the same status signals. Model-specific fuel, comfort, and diagnostic entities are disabled by default where appropriate and can be enabled from the Home Assistant entity list. If a car does not return a value, that entity remains unknown without affecting polling or the other entities. Front doors, windows, seat heating, and seat ventilation use driver/passenger naming so their labels stay correct on both left-hand-drive and right-hand-drive cars.

### Charging schedule control

Charging schedule writes are disabled by default. Set `enable_charging_control: true` in the add-on, restart it, and reload the integration to make the controls available. This opt-in is separate from normal remote commands and does not use the vehicle security PIN.

The **Scheduled charging** switch is an assumed-state convenience control:

- Turning it on replaces the vehicle's current schedule with a one-off window that starts now and ends eight hours later.
- Turning it off clears the schedule and restores normal charge-when-plugged-in behavior. It is not a hard stop command, so a plugged-in vehicle may begin charging after the schedule is cleared.

For an exact window, call `gwm_ora.set_charging_plan` from **Developer tools** > **Actions** or an automation. Both times are required and the window must be at least five minutes:

```yaml
action: gwm_ora.set_charging_plan
data:
  vin: "LGWTEST00XX000001"
  start_time: "2026-08-22 23:00:00+03:00"
  end_time: "2026-08-23 06:00:00+03:00"
```

A future start time leaves the car waiting until the window begins. GWM keeps one charging-plan slot per vehicle, so setting a new window replaces the old one rather than adding another. To remove the window:

```yaml
action: gwm_ora.clear_charging_plan
data:
  vin: "LGWTEST00XX000001"
```

The add-on records the exact plan it writes. If charging control is later disabled, it retries cleanup of that plan, but leaves a schedule alone when the official GWM app has replaced or changed it. The feature was live-tested on an ANZ ORA 5. The separate China charging implementation remains experimental and untested.

### Use the charging status with evcc

The **Charging status** sensor reports `disconnected`, `connected`, `charging`, `awaiting_charging`, `waiting_for_power`, or `error`. When you use this sensor as the vehicle status in evcc, add the two GWM waiting states to evcc's status B mapping:

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

Replace the example entity IDs with the IDs from your Home Assistant installation. In the evcc user interface, the same setting is available as the advanced **States for status B** field.

## Remote Commands

Remote commands are slower than normal Home Assistant switches because they go through the GWM cloud and then to the car. After you send a command, the **Remote command status** sensor should show messages such as:

```text
A/C: sending command to GWM
A/C: accepted by GWM, waiting for vehicle result
A/C: waiting for vehicle result (3/18)
A/C: completed - Success [0]
```

The integration follows the command while it is running and refreshes vehicle data after a successful command.

Set **Climate run time** before turning on the A/C to choose how long the remote climate command runs. The value can be 5 to 30 minutes in one-minute steps and applies to the next A/C command. Changing the run time alone saves the setting; it does not start or stop the A/C.

## Supported Vehicles

This project is designed for vehicles that can be managed through the official GWM mobile app and use a supported GWM cloud region.

Regional GWM services and vehicle firmware can differ, so some entities may be unavailable on some cars.

This integration supports accounts on the European GWM cloud (`region: eu`), including EU countries and Israel; the Australia/New Zealand cloud (`region: aus`); and the live-tested Russia cloud (`region: rus`). Experimental, partially live-validated mainland-China support (`region: cn`) is also available for NavInfo/AutoAI vehicles.

### Mainland China experimental testing

China support was derived from the mainland-China GWM Android app. Vehicle discovery, sensors, cooling, lock/unlock, and closing windows have received initial live validation on a WEY VV6. Heating, remote start/stop, horn/lights, tailgate, sunroof, and charging schedules remain experimental until each command is confirmed on a live vehicle.

For a China account, set `region: cn`, `country: CN`, and put the registered phone number in `username`. Leave `password`, `verification_code`, and `security_pin` empty on the first start. The add-on requests an SMS code and reports `verification_required`; enter that code in `verification_code`, save, and restart. Keep both command opt-ins off until vehicle discovery and sensor values have been compared with the official app.

The first implementation only supports vehicles whose account data reports `belongPlatform: navinfo`. After read-only validation, enable and test one control at a time with the vehicle visible and in a safe state. See the [detailed China test procedure](addons/gwm_ora/DOCS.md#mainland-china-cn-region-experimental). Never post phone numbers, VINs, tokens, SMS codes, or complete debug logs publicly.

### Australia/New Zealand account sessions

The ANZ backend permits one active session per account. Using the same account in the add-on and official phone app can cause them to repeatedly log each other out. A dedicated account shared to the vehicle is recommended. Grant control permission if you intend to use remote commands.

## Troubleshooting

### Add-on Is Not Discovered

- Confirm the add-on is installed and started.
- Check the add-on log for login or configuration errors.
- Restart the add-on.
- Restart Home Assistant if the integration was installed after the add-on had already started.

### Integration Cannot Connect

- Confirm the add-on is running.
- Remove and re-add the integration if discovery data changed.
- For manual development installs, reconfigure the integration with the current host, port, and token.

### GWM Login Fails

- Verify the same account works in the official GWM app.
- Confirm `region`, `country`, `username`, and, outside China, `password`. For `aus`, `country` must match the country where the account was registered. For `cn`, use `country: CN` and the registered phone number as `username`.
- When the add-on reports `verification_required`, follow the [first-login verification steps](#first-login-gwm-verification), enter the received one-time code, and restart the add-on.

### Remote Commands Are Unavailable

Remote commands require:

- `enable_remote_commands: true`
- `security_pin` configured in the add-on, except for `region: cn`

After changing either option, restart the add-on and reload the integration.

### Remote Command Status Does Not Change

- Make sure the **Remote command status** sensor is enabled.
- Update to the latest release.
- Restart the add-on and reload the integration.
- Check the add-on log for GWM command errors.

### Entities Are Missing or Unavailable

- Some entities depend on data returned by your vehicle and region.
- Newly discovered vehicles are added automatically after Home Assistant sees them.
- If a value is not returned by GWM, the related entity may be unavailable.

## Example Automations

Notify when the charge plug is connected but charging is not active:

```yaml
alias: GWM vehicle plugged in but not charging
triggers:
  - trigger: state
    entity_id: binary_sensor.gwm_vehicle_charge_plug
    to: "on"
conditions:
  - condition: state
    entity_id: binary_sensor.gwm_vehicle_charging_active
    state: "off"
actions:
  - action: notify.mobile_app_phone
    data:
      message: "The GWM vehicle is plugged in but not charging."
```

Pre-cool the cabin before a commute:

```yaml
alias: GWM vehicle pre-cool before commute
triggers:
  - trigger: time
    at: "07:20:00"
conditions:
  - condition: numeric_state
    entity_id: sensor.gwm_vehicle_soc
    above: 30
actions:
  - action: climate.set_temperature
    target:
      entity_id: climate.gwm_vehicle_a_c_climate
    data:
      temperature: 22
      hvac_mode: cool
```

Test remote command automations manually first and use them only when the vehicle is parked somewhere safe.

## Removal

1. Delete the `GWM` integration entry from Home Assistant.
2. Stop and uninstall the `GWM` add-on.
3. Remove this repository from the add-on store if you no longer need it.
4. Remove the custom integration from HACS or delete `/config/custom_components/gwm_ora`.
5. Restart Home Assistant.

## Privacy And Safety

Your GWM account details and vehicle PIN are configured in the add-on, not the integration. The add-on stores generated tokens in its own add-on data folder.

Remote commands can affect the real vehicle. Use them carefully.

## Disclaimer

This project is unofficial and is not affiliated with or endorsed by Great Wall Motor, GWM, or Home Assistant. Vehicle cloud APIs and remote command behavior may change without notice.

Use at your own risk. You are responsible for validating behavior, protecting credentials, keeping backups, and deciding whether remote commands are appropriate for your vehicle and environment.

## Special Thanks

Special thanks to [zivillian](https://github.com/zivillian) and the [zivillian/ora2mqtt](https://github.com/zivillian/ora2mqtt) project for blazing the trail. Their work uncovered many of the details behind GWM connectivity and helped inspire the current development of this integration.

Thanks to [AlexandrErohin](https://github.com/AlexandrErohin) for contributing the initial model-specific vehicle sensor set and Russia cloud support.

Deep thanks to [wilberforce](https://github.com/wilberforce) for reverse-engineering the ANZ authentication and signing flow, implementing AU/NZ support, decoding vehicle status mappings, and validating authentication and comfort signals against a live vehicle.

[hacs-badge]: https://img.shields.io/badge/HACS-Default-41BDF5.svg?style=flat-square
[hacs-url]: https://github.com/hacs/integration
[release-badge]: https://img.shields.io/github/v/release/moryoav/ha-gwm?style=flat-square
[release-url]: https://github.com/moryoav/ha-gwm/releases
[license-badge]: https://img.shields.io/github/license/moryoav/ha-gwm?style=flat-square
[license-url]: https://github.com/moryoav/ha-gwm/blob/main/LICENSE
