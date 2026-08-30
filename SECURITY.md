# Security Policy

GWM for Home Assistant handles GWM account credentials, access and refresh tokens, a generated add-on API token, vehicle location, and optional remote vehicle commands. Please treat security and privacy issues with care.

## Supported Versions

Security fixes are intended for the latest published release and the current `main` branch.

Older releases are not actively supported unless a maintainer says otherwise in a specific issue or release note.

## Reporting a Vulnerability

Please do not open a public issue with exploit details, working proof-of-concept code, private logs, tokens, security PINs, VINs, exact vehicle locations, or personal Home Assistant configuration.

If GitHub private vulnerability reporting is available for this repository, use the **Report a vulnerability** button on the Security tab.

If private vulnerability reporting is not available, open a minimal public issue that says you have a security concern and asks the maintainer to arrange private disclosure. Do not include sensitive details in that issue.

## What to Include

When reporting a vulnerability privately, include as much of the following as you can safely share:

- A clear description of the issue.
- The affected version or commit.
- Whether the issue affects the add-on, the custom integration, or both.
- Steps to reproduce in a safe test environment.
- The expected impact.
- Any relevant logs with secrets, VINs, and private configuration removed.
- Suggested mitigations, if you know them.

## Security-Sensitive Areas

Please use extra care when changing or reviewing:

- GWM username, password, security PIN, access token, refresh token, or generated API token handling.
- Bundled bootstrap certificates, transformed private keys, app-derived signing values, and their provenance records.
- `/data/options.json` and `/data/state.json` storage.
- Add-on API authorization.
- Supervisor discovery payloads.
- Home Assistant config flow, reauth, reconfigure, repairs, and diagnostics.
- Remote climate, lock, unlock, and close-window commands.
- Add-on networking, container permissions, and exposed ports.
- Logs and diagnostics containing raw GWM API payloads, VINs, or precise locations.

I do not accept replacement protocol material through a public vulnerability report or issue. Contact me privately and follow the evidence controls in [Third-Party and Protocol Material Notice](THIRD_PARTY_NOTICES.md).

## Responsible Testing

Test security reports and fixes only in an environment you own or have permission to use. Do not attempt to access, modify, unlock, locate, or control another person's vehicle, GWM account, Home Assistant instance, credentials, logs, or devices.

Remote command testing should be performed only with explicit owner consent and in a safe location where you can observe the vehicle.

## Public Disclosure

Please give the maintainer reasonable time to investigate and fix confirmed vulnerabilities before publishing details publicly. Coordinated disclosure helps protect users while a fix is prepared.
