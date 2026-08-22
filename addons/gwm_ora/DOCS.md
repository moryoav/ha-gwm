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
| `region` | yes | GWM cloud gateway to use: `eu` for Europe/Israel accounts, `aus` for Australia/New Zealand accounts, or `rus` for Russia accounts. Defaults to `eu`. |
| `country` | yes | Two-letter GWM account country. It must match the country the account was **registered** in — e.g. `DE` or `GB` for `eu`, `NZ` or `AU` for `aus`, and `RU` for `rus`. A mismatch (for example `AU` for an account registered in New Zealand) fails login with *"Incorrect email or password"*. |
| `username` | yes | GWM account e-mail address. |
| `password` | yes | GWM account password. |
| `verification_code` | no | One-time SMS/e-mail verification code sent by GWM during first login or when this add-on device must be trusted. Fill it only after GWM sends a code. |
| `security_pin` | no | Vehicle remote control PIN from the official app. |
| `enable_remote_commands` | yes | Enables A/C, lock, unlock, and close-window commands. |
| `enable_charging_control` | yes | Enables the **Scheduled charging** switch and the `gwm_ora.set_charging_plan` / `gwm_ora.clear_charging_plan` actions. Default `false`. Independent of `enable_remote_commands` and needs no security PIN. Validated on an ANZ vehicle; the same H5 gateway API is used for all regions. |
| `poll_interval_seconds` | yes | GWM cloud polling interval from 30 to 3600 seconds. |
| `log_level` | yes | One of `trace`, `debug`, `info`, `warning`, or `error`. |

## First-login verification

When the add-on logs in for the first time, GWM sends a one-time verification code by SMS or e-mail. The add-on log and Web UI will report `verification_required` while it is waiting for that code.

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
