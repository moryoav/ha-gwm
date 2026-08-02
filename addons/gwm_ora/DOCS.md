# GWM ORA Add-on Documentation

## Installation

1. Add this repository to the Home Assistant add-on store.
2. Install the **GWM ORA** add-on.
3. Fill in the add-on configuration.
4. Start the add-on.
5. Install the `custom_components/gwm_ora` integration from this repository.
6. Confirm the discovered **GWM ORA** integration in Home Assistant.

## Configuration

| Option | Required | Description |
| --- | --- | --- |
| `region` | yes | GWM cloud gateway to use: `eu` for Europe/Israel accounts, `aus` for Australia/New Zealand accounts. Defaults to `eu`. |
| `country` | yes | Two-letter GWM account country. It must match the country the account was **registered** in — e.g. `DE` or `GB` for `eu`, and `NZ` or `AU` for `aus`. A mismatch (for example `AU` for an account registered in New Zealand) fails login with *"Incorrect email or password"*. |
| `username` | yes | GWM account e-mail address. |
| `password` | yes | GWM account password. |
| `verification_code` | no | One-time SMS/e-mail verification code sent by GWM during first login or when this add-on device must be trusted. Fill it only after GWM sends a code. |
| `security_pin` | no | Vehicle remote control PIN from the official app. |
| `enable_remote_commands` | yes | Enables A/C, lock, unlock, and close-window commands. AU/NZ command support is currently experimental and unconfirmed. |
| `poll_interval_seconds` | yes | GWM cloud polling interval from 30 to 3600 seconds. |
| `log_level` | yes | One of `trace`, `debug`, `info`, `warning`, or `error`. |

## First-login verification

When the add-on logs in for the first time, GWM sends a one-time verification code by SMS or e-mail. The add-on log and Web UI will report `verification_required` while it is waiting for that code.

Check the phone messages and e-mail inbox for your GWM account, including spam or junk folders. For European accounts, the e-mail will most likely come from `noreply@gwm-eu.com` with the subject `GWM Verification Code`.

<img src="https://raw.githubusercontent.com/moryoav/ha-gwm_ora/main/docs/images/gwm-verification-code-email.jpeg" alt="Example GWM Verification Code e-mail" width="320">

After you receive the code:

1. Open the **GWM ORA** add-on page in Home Assistant.
2. Go to the **Configuration** tab.
3. Click **Show unused optional configuration options**.
4. Fill in **Verification code** (`verification_code`) with the one-time code.
5. Save the configuration.
6. Restart the add-on.

After a successful login, the add-on stores GWM tokens under `/data` and tries to clear `verification_code` from the add-on options. The add-on Web UI should then show **Authenticated** as **Yes** and **Verification** as **Not required**.

![GWM ORA add-on authenticated status](https://raw.githubusercontent.com/moryoav/ha-gwm_ora/main/docs/images/gwm-addon-authenticated.jpg)

If GWM rejects the code, clear `verification_code`, restart the add-on so it requests a fresh code, then enter the new code and restart again. Verification codes are short-lived, so use the newest code you received.

## Australia / New Zealand (`aus` region)

For GWM ANZ accounts (the *GWM* app on the Australian/New Zealand store), set:

- `region`: `aus`
- `country`: the country the account was registered in — usually `NZ` or `AU`. This must match, or login fails with *"Incorrect email or password"* even when the password is correct.

The authentication flow is similar to the EU setup: the first login on a new device triggers a one-time e-mail verification code, which you enter in `verification_code` (see **First-login verification** above), after which the add-on keeps itself signed in by refreshing its token.

**One account, one session.** The ANZ backend allows only a single active session per account: whenever the account signs in on a new device, the previous session is logged out. If the add-on and the official phone app use the **same** account they will repeatedly evict each other. To avoid this, give the add-on its **own** account:

1. Keep using your primary account in the phone app.
2. Create a second GWM account and **share the car** with it, granting **control** permission (not view-only) if you want to test remote commands.
3. Configure the add-on with that second account.

Login, verification, vehicle discovery, and status polling have been validated against a live ANZ vehicle. Lock, climate, and close-window commands remain available when explicitly enabled with a security PIN, but they are currently **experimental and unconfirmed** on the ANZ backend. Test one command at a time only while the vehicle is parked, safe, and in view. Do not rely on AU/NZ remote-command automations until your vehicle has been tested successfully, and report the result in [issue #1](https://github.com/moryoav/ha-gwm_ora/issues/1).

## Web UI

The **Open Web UI** button uses Home Assistant Ingress and shows add-on health plus the latest cached vehicle summary. Remote controls are exposed by the native Home Assistant integration rather than the add-on web page.

## Security

The add-on does not publish a LAN port, does not use host networking, does not request Docker API access, and does not use `full_access`. The internal API requires a generated bearer token, Ingress pages are restricted to Home Assistant's ingress proxy, and the container ships with a custom AppArmor profile.

This repository publishes stable releases only for now; no canary branch is offered.
