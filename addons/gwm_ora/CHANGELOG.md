# Changelog

All notable changes to this project will be documented in this file.

This project follows semantic versioning. HACS uses the latest GitHub release tag as the remote version, so every released version must have both a tag and a GitHub release.

## [Unreleased]

## [0.2.15] - 2026-07-20

### Added

- Australia/New Zealand support for the `aus` region: account login against the `aus-h5-gateway` using GWM's `bt-auth` request signing, new-device e-mail verification (enter the emailed code in the `verification_code` option, save, and restart), and token refresh. Vehicle discovery and status polling now work for ANZ accounts. Set `region: aus` and the account's registration country (for example `NZ`).

### Fixed

- Accept numeric fields that the ANZ gateway returns as JSON strings (for example `securityTime`), so responses deserialize correctly.
- A failed optional `vehicleBasicsInfo` request no longer aborts a poll; vehicle status is still published from the core endpoints.
- Recover from `607501` ("logged in elsewhere") by performing a full re-login. The ANZ backend is single-session, and refreshing a token does not reclaim a session taken by another device, so the add-on now re-authenticates instead of getting stuck.

## [0.2.14] - 2026-06-24

### Fixed

- Fixed the add-on ingress vehicles table so long VIN values wrap without pushing aside SOC, range, and updated columns.

## [0.2.13] - 2026-06-23

### Changed

- Prepared a fresh release after passing HACS and Hassfest validation for HACS default repository submission.

## [0.2.12] - 2026-06-23

### Changed

- Expanded the README and add-on documentation with detailed GWM verification-code setup instructions and screenshots.
- Added special thanks to `zivillian/ora2mqtt` for the original trailblazing work that inspired this integration.

## [0.2.11] - 2026-06-19

### Fixed

- Added the add-on-local `CHANGELOG.md` expected by the Home Assistant Apps/Add-ons UI.
- Added a quality check to keep the add-on changelog synchronized with the repository changelog.

## [0.2.10] - 2026-06-19

### Added

- Added required HACS validation and Hassfest GitHub Actions for HACS default repository readiness.
- Added repository quality checks that keep the HACS metadata and validation workflows in place.

### Changed

- Simplified `hacs.json` to supported HACS manifest keys only.

## [0.2.9] - 2026-06-19

### Fixed

- Restored live remote command status progress in Home Assistant by tracking add-on command IDs until terminal state.
- Updated `/api/v1/vehicles` to overlay the latest remote command status instead of returning only the status captured during the last vehicle cloud poll.
- Refreshed vehicle data immediately after a completed remote command so A/C, lock, and window state can update without waiting for the normal polling interval.

### Changed

- Enabled the remote command status sensor by default because it is the main progress indicator for long-running GWM commands.
- Rewrote the README for normal Home Assistant users and removed developer/release-oriented sections.
- Replaced the README banner with a higher-resolution ORA/GWM image.

## [0.2.8] - 2026-06-19

### Added

- Added a startup log line with the running add-on version and architecture to make stale Home Assistant Docker builds easy to identify.

## [0.2.7] - 2026-06-19

### Added

- Added Home Assistant Ingress support with a small authenticated add-on status page.
- Added a custom AppArmor profile for the add-on container.

### Changed

- Documented the add-on presentation/security posture in line with the Home Assistant app presentation guide.

## [0.2.6] - 2026-06-19

### Added

- Added optional `verification_code` add-on setup support for GWM SMS/e-mail verification when the add-on device is not trusted yet.

### Fixed

- Declared the `gwm_ora` Supervisor discovery service in add-on metadata so discovery publishing is accepted by Supervisor.
- Switched the add-on ASP.NET binding configuration from `ASPNETCORE_URLS` to `ASPNETCORE_HTTP_PORTS` to avoid the startup port override warning.
- Reduced repeated GWM verification failures to a concise action-required warning instead of repeated stack traces.

## [0.2.5] - 2026-06-19

### Fixed

- Fixed Home Assistant add-on option saving by replacing the `country` schema from `str(2,2)` with a regex validator compatible with Supervisor's current schema validation.
- Made `security_pin` truly optional in the add-on metadata by removing its default option value while keeping it available in the setup form.

## [0.2.4] - 2026-06-19

### Fixed

- Fixed Supervisor local add-on builds by moving the .NET add-on source and OpenSSL configuration into `addons/gwm_ora`, which is the actual Docker build context used by Home Assistant Supervisor.
- Updated the add-on build CI workflow to use the same `addons/gwm_ora` Docker context as Supervisor.

## [0.2.3] - 2026-06-19

### Fixed

- Fixed Supervisor local add-on builds by copying `gwm_root.pem` from the Docker build stage instead of reading it again from the source context in the runtime stage.

### Added

- Added a non-publishing multi-architecture add-on Docker build check in CI.

## [0.2.2] - 2026-06-19

### Fixed

- Corrected the maintainer name to Yoav Mor in repository metadata and license text.
- Removed the stale README image-workflow badge after switching the add-on to local Supervisor builds.

## [0.2.1] - 2026-06-19

### Changed

- Removed GHCR image publishing and the add-on `image` setting so Home Assistant Supervisor builds the standalone add-on locally from the repository Dockerfile.
- Added Home Assistant local-build labels to the add-on Dockerfile.

## [0.2.0] - 2026-06-19

### Added

- Added Home Assistant Integration Quality Scale tracking with `custom_components/gwm_ora/quality_scale.yaml`.
- Added Gold-track documentation for supported devices, data updates, diagnostics, troubleshooting, use cases, examples, known limitations, and removal.
- Added GitHub community health files: Code of Conduct, Contributing, Security, Support, issue forms, and pull request template.
- Added reconfigure and reauthentication flows for manual/development add-on API connection updates.
- Added Home Assistant repair issue creation when the add-on API token is rejected.
- Added dynamic entity creation for vehicles discovered after initial setup.
- Added entity icon translations and disabled-by-default diagnostic timestamp/command-status entities.

### Changed

- Distinguished add-on authentication failures from remote-command permission failures.
- Wrapped remote command entity failures in translated Home Assistant errors.
- Declared platform parallel update behavior for all integration platforms.

## [0.1.0] - 2026-06-19

### Added

- Initial native Home Assistant add-on for GWM ORA cloud polling and remote commands.
- Initial Home Assistant custom integration with Supervisor discovery and manual development setup.
- Native sensor, binary sensor, device tracker, climate, lock, and button entities.
- Token-protected internal add-on API with persistent add-on state under `/data`.
- Multi-architecture add-on image builds for `amd64`, `aarch64`, and `armv7`.
- Brand assets and installation documentation for add-on store and HACS setup.
