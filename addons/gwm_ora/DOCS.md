# GWM Add-on Documentation

## Installation

1. Add this repository to the Home Assistant add-on store.
2. Install the **GWM** add-on.
3. Fill in the add-on configuration.
4. Start the add-on.
5. Install the `custom_components/gwm_ora` integration from this repository.
6. Confirm the discovered **GWM** integration in Home Assistant.

## Configuration

| Option | Required | Description |
| --- | --- | --- |
| `region` | yes | GWM cloud gateway to use: `eu` for Europe/Israel, `aus` for Australia/New Zealand, `rus` for Russia, or experimental `cn` for mainland China. Defaults to `eu`. |
| `country` | yes | Two-letter GWM account country. It must match the country the account was **registered** in, for example `DE` or `GB` for `eu`, `NZ` or `AU` for `aus`, `RU` for `rus`, and `CN` for `cn`. A mismatch (for example `AU` for an account registered in New Zealand) fails login with *"Incorrect email or password"*. |
| `username` | yes | GWM account e-mail address, or the registered phone number for `cn`. |
| `password` | except `cn` | GWM account password. Ignored for `cn`, which uses SMS login. |
| `verification_code` | no | One-time SMS/e-mail verification code sent by GWM. China login always uses an SMS code; leave this empty on the first China start so the add-on can request one. |
| `security_pin` | no | Vehicle remote control PIN from the official app. Required outside China. NavInfo commands do not send it. The BeanTech encrypted PIN format is not implemented yet. |
| `enable_remote_commands` | yes | Enables A/C, lock, unlock, and close-window commands. In China it also exposes experimental model-dependent controls. BeanTech commands are unverified. |
| `enable_charging_control` | yes | Enables the **Scheduled charging** switch and the `gwm_ora.set_charging_plan` / `gwm_ora.clear_charging_plan` actions. Default `false`. Independent of `enable_remote_commands` and needs no security PIN. Validated on an ANZ vehicle. China charging control is currently limited to NavInfo vehicles. |
| `poll_interval_seconds` | yes | GWM cloud polling interval from 30 to 3600 seconds. |
| `log_level` | yes | One of `trace`, `debug`, `info`, `warning`, or `error`. |

## First-login verification

When the add-on logs in for the first time, GWM may send a one-time verification code by SMS or e-mail. China always uses SMS login. The add-on log and Web UI will report `verification_required` while it is waiting for that code.

Check the phone messages and e-mail inbox for your GWM account, including spam or junk folders. For European accounts, the e-mail will most likely come from `noreply@gwm-eu.com` with the subject `GWM Verification Code`.

<img src="https://raw.githubusercontent.com/moryoav/ha-gwm/main/docs/images/gwm-verification-code-email.jpeg" alt="Example GWM Verification Code e-mail" width="320">

After you receive the code:

1. Open the **GWM** add-on page in Home Assistant.
2. Go to the **Configuration** tab.
3. Click **Show unused optional configuration options**.
4. Fill in **Verification code** (`verification_code`) with the one-time code.
5. Save the configuration.
6. Restart the add-on.

After a successful login, the add-on stores GWM tokens under `/data` and tries to clear `verification_code` from the add-on options. The add-on Web UI should then show **Authenticated** as **Yes** and **Verification** as **Not required**.

![GWM add-on authenticated status](https://raw.githubusercontent.com/moryoav/ha-gwm/main/docs/images/gwm-addon-authenticated.jpg)

If GWM rejects the code, clear `verification_code`, restart the add-on so it requests a fresh code, then enter the new code and restart again. Verification codes are short-lived, so use the newest code you received.

## Australia / New Zealand (`aus` region)

For GWM ANZ accounts (the *GWM* app on the Australian/New Zealand store), set:

- `region`: `aus`
- `country`: the country the account was registered in — usually `NZ` or `AU`. This must match, or login fails with *"Incorrect email or password"* even when the password is correct.

The authentication flow is similar to the EU setup: the first login on a new device triggers a one-time e-mail verification code, which you enter in `verification_code` (see **First-login verification** above), after which the add-on keeps itself signed in by refreshing its token.

**One account, one session.** The ANZ backend allows only a single active session per account: whenever the account signs in on a new device, the previous session is logged out. If the add-on and the official phone app use the **same** account they will repeatedly evict each other. To avoid this, give the add-on its **own** account:

1. Keep using your primary account in the phone app.
2. Create a second GWM account and **share the car** with it, granting **control** permission (not view-only) if you want to use remote commands.
3. Configure the add-on with that second account.

## Russia (`rus` region)

For GWM Russia accounts (the Russian *GWM* Android app), set:

- `region`: `rus`
- `country`: `RU`

Russia uses its own GWM request signing, gateways, and bundled client certificate from the official Russian app. First-login SMS/e-mail verification follows the same add-on setup as the other regions.

Russia support has been live-tested, confirmed working, and merged as a supported region. It is not part of the experimental China work.

## Mainland China (`cn` region, experimental)

China support is based on reverse engineering of the mainland-China GWM Android app and offline protocol fixtures. NavInfo vehicle discovery, sensors, cooling, lock/unlock, and closing windows have received initial live validation on a contributed WEY VV6. BeanTech status reading has been live-tested on a 2024 Tank 300 Hi4-T. NavInfo and BeanTech use separate status and command routes. Any other reported platform stops with an explicit unsupported-platform error.

Start with read-only testing:

1. Set `region` to `cn` and `country` to `CN`.
2. Put the phone number registered to the China GWM account in `username`.
3. Leave `password`, `verification_code`, and `security_pin` empty.
4. Keep `enable_remote_commands` and `enable_charging_control` set to `false`.
5. Save and start the add-on. It should request an SMS code and stop with `verification_required`.
6. Enter the newest SMS code in `verification_code`, save, and restart the add-on.
7. Confirm that authentication and vehicle discovery succeed, then compare every available sensor with the official app.

The add-on stores the three China service sessions under `/data` and tries to clear the one-time code after a successful login. If GWM returns risk-control code `1013`, complete the requested challenge in the official app, clear `verification_code`, and restart the add-on to request a new code.

Only after vehicle discovery and the read-only entities look correct should a tester enable controls. Set `enable_remote_commands: true` to expose the controls supported by that platform. NavInfo exposes A/C, lock, unlock, close windows, remote start/stop, horn, lights, combined vehicle search, tailgate open/close, and sunroof close/tilt/half/full controls. China climate also offers experimental heating.

BeanTech currently exposes initial mappings for lock/unlock, close windows, remote start/stop, horn, flashing lights, and closing the sunroof. These mappings are not live-verified. The exact command fields and the official app's encrypted PIN format still need more research, so a command may fail without reaching the vehicle. BeanTech climate, combined horn and lights, tailgate, other sunroof positions, and charging schedules stay unavailable until their request formats are known.

Test one command at a time while the vehicle is visible, parked, clear of people and obstacles, and in a safe state. For sunroof testing, start closed. Compare the physical result and the **Remote command status** sensor with the official app after every command. If a command fails, share only sanitized request shape, response code, response message, and result-polling details.

Set `enable_charging_control: true` separately to test charging schedules on a compatible vehicle. The China charging implementation currently supports NavInfo only. The scheduled-charging switch stays unavailable for BeanTech vehicles.

Please report the add-on version, vehicle model, `belongPlatform`, which sensors matched or differed, and the result of each command. Do not post phone numbers, VINs, tokens, SMS codes, or complete debug logs publicly.

## Model-specific vehicle data

Different models and regions return different GWM status codes. Version 0.7.0 adds optional fuel, door, trunk, heater, seat heating and ventilation, tire-state, window-learning, engine-state, and sunroof-position data. Car-specific comfort and diagnostic entities are disabled by default where appropriate and can be enabled from the Home Assistant entity list.

Missing or malformed optional values are returned as unknown and do not stop the vehicle refresh or affect supported entities. Front doors, windows, seat heating, and seat ventilation use driver/passenger naming so the labels remain correct for both left-hand-drive and right-hand-drive cars. Engine and sunroof values are intentionally exposed as disabled raw state codes until their meaning is confirmed on more vehicles.

## Charging schedule control

Set `enable_charging_control: true`, restart the add-on, and reload the integration to enable charging writes. This is a separate opt-in from remote commands and does not require `security_pin`.

The **Scheduled charging** switch sets a one-off window from now until eight hours later. Turning it off clears the schedule and restores charge-when-plugged-in behavior. Use `gwm_ora.set_charging_plan` for an exact start and end time or `gwm_ora.clear_charging_plan` to remove the schedule. A window must be at least five minutes. A future start time makes the vehicle wait until the window begins.

GWM stores one charging-plan slot per vehicle, so a new plan replaces the previous one. The add-on tracks the exact plan it writes. If charging control is later disabled, it retries cleanup of that plan while preserving a schedule that was replaced or changed in the official GWM app.

## Web UI

The **Open Web UI** button uses Home Assistant Ingress and shows add-on health plus the latest cached vehicle summary. Remote controls are exposed by the native Home Assistant integration rather than the add-on web page.

The integration includes a **Climate run time** number entity. Set it from 5 to 30 minutes in one-minute steps before starting the A/C. Changing the number saves the duration for the next A/C command and does not start or stop the A/C by itself.

## Security

The add-on does not publish a LAN port, does not use host networking, does not request Docker API access, and does not use `full_access`. The internal API requires a generated bearer token, Ingress pages are restricted to Home Assistant's ingress proxy, and the container ships with a custom AppArmor profile.

This repository publishes stable releases only for now; no canary branch is offered.
