# Contributing to GWM for Home Assistant

Thanks for your interest in improving GWM for Home Assistant.

This project has two main parts:

- `addons/gwm_ora`: the Home Assistant add-on that owns GWM account setup, token storage, polling, Supervisor discovery, and remote commands.
- `custom_components/gwm_ora`: the Home Assistant custom integration that exposes native entities and talks only to the add-on API.

Contributions are welcome, including bug reports, documentation improvements, compatibility fixes, security hardening, mapping corrections, and feature ideas.

## Before You Start

Please open an issue before starting large or risky changes. This helps avoid duplicated work and gives maintainers a chance to discuss the approach first.

Small fixes, documentation updates, and clearly scoped bug fixes can usually go straight to a pull request.

## Reporting Bugs

When reporting a bug, please include:

- The GWM for Home Assistant version.
- Your Home Assistant version.
- Whether you installed through HACS, manually, or from a development branch.
- Your architecture, such as `amd64`, `aarch64`, or `armv7`.
- Whether the issue affects the add-on, the integration, or both.
- Clear steps to reproduce the issue.
- Relevant Home Assistant and add-on logs.
- What you expected to happen.
- What actually happened.

Please remove GWM credentials, add-on API tokens, refresh tokens, security PINs, VINs, exact vehicle locations, private URLs, and personal Home Assistant configuration before sharing logs or screenshots.

## Suggesting Features

Feature requests are welcome. Please describe:

- The problem you want to solve.
- The Home Assistant workflow you expect to use.
- Whether the change belongs in the add-on, the custom integration, or both.
- The vehicle model/region involved, if relevant.
- Any safety, security, or privacy concerns the feature may introduce.

Features that expand remote vehicle control should include a clear safety rationale and must preserve explicit user opt-in.

## Development Setup

Clone the repository:

```bash
git clone https://github.com/moryoav/ha-gwm.git
cd ha-gwm
```

The repository layout is:

```text
addons/gwm_ora/                 Home Assistant add-on metadata, container packaging, and source
addons/gwm_ora/src/GwmOra.Addon/ .NET add-on service
addons/gwm_ora/src/LibGwmApi/   GWM API client adapted from ora2mqtt behavior
custom_components/gwm_ora/      Home Assistant custom integration
tests/                          .NET and Python tests
.github/workflows/              CI and release workflows
```

For local Home Assistant testing, install or copy the integration into:

```text
/config/custom_components/gwm_ora
```

For add-on testing, add this repository as a Home Assistant add-on repository and use a development branch when needed.

## Pull Request Guidelines

Please keep pull requests focused. A good pull request should:

- Explain what changed and why.
- Mention any related issue.
- Keep unrelated formatting or refactoring out of the change.
- Update documentation when behavior, installation, options, entities, commands, or add-on configuration changes.
- Update `CHANGELOG.md` for user-facing changes.
- Update version fields when preparing a release.
- Include screenshots when changing Home Assistant UI text or setup flow behavior.
- Avoid committing credentials, tokens, security PINs, VINs, private locations, private logs, or personal Home Assistant configuration.

## Testing

Before opening a pull request, test the parts you changed as much as practical.

Run:

```powershell
dotnet test
python -m ruff check custom_components tests/python
python -m compileall custom_components tests/python
python -m pytest tests/python
```

For integration changes, verify that Home Assistant can:

- Load the `gwm_ora` integration.
- Complete Supervisor discovery or manual setup.
- Reload or reconfigure the config entry.
- Create entities under the expected vehicle device.
- Download diagnostics without leaking the add-on API token.
- Mark entities unavailable when the add-on is unavailable.

For add-on changes, verify that the add-on can:

- Start successfully after required options are configured.
- Read `/data/options.json`.
- Persist generated state under `/data`.
- Publish Supervisor discovery.
- Serve the internal add-on API.
- Poll vehicle data without MQTT.

Remote commands should be tested only on a real vehicle with explicit user opt-in. Be physically near the vehicle when testing lock, climate, or window commands.

## Security Notes

This project handles GWM account credentials, vehicle cloud tokens, vehicle location, and optional remote commands.

Please be especially careful with changes involving:

- Username, password, security PIN, access token, refresh token, or API token handling.
- Supervisor discovery payloads.
- Add-on network exposure and container permissions.
- Remote climate, lock, unlock, or close-window commands.
- Diagnostics redaction.
- Logs that may include VINs, precise locations, command IDs, tokens, or raw GWM API payloads.

If you believe you found a security vulnerability, do not open a public issue with exploit details. Follow `SECURITY.md`.

## Documentation

Please update documentation when changing user-facing behavior. Depending on the change, this may include:

- `README.md`
- `custom_components/gwm_ora/README.md`
- `addons/gwm_ora/README.md`
- `addons/gwm_ora/DOCS.md`
- `CHANGELOG.md`

Use plain, direct language and include Home Assistant examples where they make the workflow easier to understand.

## Releases

HACS uses GitHub releases for update detection. Release pull requests should:

- Move `CHANGELOG.md` entries from `Unreleased` into the target version.
- Update `addons/gwm_ora/config.yaml`.
- Update `custom_components/gwm_ora/manifest.json`.
- Push a `vX.Y.Z` tag after the release commit lands.

The release workflow creates the GitHub release from the matching changelog section. The add-on is built locally by Home Assistant Supervisor from the repository Dockerfile.

## Code of Conduct

Please be respectful, constructive, and patient. This project controls real vehicle-facing features, and contributions should help Home Assistant users operate those features safely.
