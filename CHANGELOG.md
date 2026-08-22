# Changelog

All notable changes to this project will be documented in this file.

This project follows semantic versioning. HACS uses the latest GitHub release tag as the remote version, so every released version must have both a tag and a GitHub release.

## [Unreleased]

### Added

- Optional charging-schedule control for all regions, behind a new `enable_charging_control` opt-in (default off, separate from remote commands, no security PIN). Adds a **Scheduled charging** switch and `gwm_ora.set_charging_plan` / `gwm_ora.clear_charging_plan` services that set or clear a charging window (`vehicleCharge/setChargingPlan`) — a car-side lever for solar-excess charging. The services accept the VIN shown on the device, and a plan whose window starts in the future pauses charging until then; turning the switch off clears the plan so the car returns to charging whenever it is plugged in. When the opt-in is off the write is rejected (403), and any leftover plan the add-on itself set is cleared on startup so it can't silently block charging (a plan set in the GWM app is never touched). Verified end-to-end on an ANZ ORA 5; EU uses the same code path (ANZ additionally sends the required `vin` request header) but is not yet independently tested.

## [0.9.0] - 2026-08-22

### Changed

- Renamed the user-facing project, add-on, and integration from **GWM ORA** to **GWM** to reflect support for compatible vehicles available through the official GWM app.
- Renamed the GitHub repository from `ha-gwm_ora` to `ha-gwm` and updated installation buttons, documentation, metadata, badges, and community links to the new URL.
- Replaced ORA-specific examples and presentation assets with generic GWM names and official GWM branding.
- Automatically rename existing config entries that still use the old default title while preserving user-customized titles.
- Kept the `gwm_ora` integration domain, add-on slug, folders, discovery identifier, API environment variables, and internal code namespaces unchanged so existing installations continue working without identifier migration or reconfiguration.

## [0.8.0] - 2026-08-21

### Added

- Added Russia cloud support with `region: rus` and `country: RU`, including authentication, verification, vehicle discovery, status polling, and remote A/C, lock, unlock, and close-window commands.
- Added Russia-specific request signing, client certificates, gateway routing, and tolerant string-or-number response decoding.

### Fixed

- Applied the Russia-specific security PIN check, command type, VIN headers, close-window payload, and result polling behavior without changing the existing EU or AU/NZ command paths.
- Kept AU/NZ response parsing isolated from Russia's flexible response format and aligned Russia verification-code logins with the correct agreements.

Thanks to [@AlexandrErohin](https://github.com/AlexandrErohin) for implementing and testing Russia support.

## [0.7.0] - 2026-08-21

### Added

- Added optional fuel level and fuel range sensors, door and trunk binary sensors, rear defroster and GPS authorization states, seat heating and ventilation levels, steering-wheel and windscreen heater states, and disabled diagnostic tire, window-learning, engine, and sunroof state-code sensors.
- Added market-safe driver/passenger door data and aliases for the already released window data so the same entities work on left-hand-drive and right-hand-drive cars.
- Added defensive telemetry and entity-contract tests for missing, malformed, duplicate, non-finite, and unsupported vehicle values.

### Changed

- Keep car-specific fuel, comfort, and raw diagnostic entities disabled by default where appropriate. Missing signals remain unknown and do not interrupt polling or other entities.
- Label engine and sunroof values as raw state codes because those mappings still need live confirmation. Steering-wheel heating and front-seat heating and ventilation mappings include live ANZ ORA 5 validation.

### Fixed

- Prevent malformed or duplicate GWM status items, invalid seat levels, non-finite numbers, and out-of-range timestamps from breaking an entire vehicle refresh or API response.
- Use driver/passenger naming for the contributed door mappings instead of physical left/right labels that invert between LHD and RHD markets.

Thanks to [@AlexandrErohin](https://github.com/AlexandrErohin) for the initial sensor implementation and [@wilberforce](https://github.com/wilberforce) for the decoded mappings, RHD guidance, live testing, and front-seat ventilation contribution.

## [0.6.1] - 2026-08-10

### Fixed

- Show neutral polling progress for GWM result code `2000` instead of the backend's misleading failure and retry message while the add-on automatically waits for the vehicle result.

## [0.6.0] - 2026-08-09

### Fixed

- AU/NZ: send the required `vin` request header when polling remote-command results (`getRemoteCtrlResultT5`). Without it the ANZ gateway rejected the poll with `002 Missing request header 'vin'`, so a command that actually succeeded on the vehicle was reported as failed in Home Assistant. Verified end-to-end on an ANZ ORA 5. EU is unaffected.

## [0.5.1] - 2026-08-09

### Added

- Added a translated **Charging status** sensor with disconnected, connected, charging, waiting, and error states, including evcc setup documentation.

### Fixed

- Keep the **Charging active** binary sensor available and off for known GWM waiting and error states.

## [0.5.0] - 2026-08-09

### Changed

- New v2 Authentication code for EU region.

## [0.4.0] - 2026-08-02

### Added

- Added a Home Assistant **Climate run time** number entity that saves a 5-to-30-minute duration for the next A/C command.

### Fixed

- Correctly convert the saved GWM climate duration between the cloud settings endpoint's seconds and the vehicle command's minutes.

## [0.3.0] - 2026-08-02

### Added

- Australia/New Zealand support for the `aus` region: account login against the `aus-h5-gateway` using GWM's `bt-auth` request signing, new-device e-mail verification, token refresh, vehicle discovery, and status polling. Set `region: aus` and the account's registration country (for example `AU` or `NZ`). The existing `eu` behavior remains unchanged.

### Changed

- Keep lock, climate, and close-window controls available for `aus` accounts when remote commands are explicitly enabled with a security PIN.
- Document the ANZ single-session limitation and recommend a dedicated shared vehicle account.

### Fixed

- Accept numeric fields that the ANZ gateway returns as JSON strings, such as `securityTime`, while retaining strict EU deserialization.
- Canonicalize signed ANZ GET parameters using the GWM app-family ordering, lowercase-key, and concatenation rules, while removing empty query parameters rejected by the gateway.
- Treat only the known ANZ `607099` response from optional `vehicleBasicsInfo` calls as non-fatal, including climate-command preflight; all EU and other GWM API errors still surface.
- Recover from ANZ `607501` ("logged in elsewhere") responses with a full re-login because refreshing a token does not reclaim a session taken by another device.

## [0.2.16] - 2026-08-02

### Changed

- Simplified HACS installation instructions now that GWM ORA is available in the default HACS catalog.
- Aligned the integration manifest and add-on metadata with the `v0.2.16` release.

## [0.2.15] - 2026-08-01

### Changed

- Removed duplicate repository-root integration brand assets while keeping the canonical copies alongside the custom integration.

### Fixed

- Aligned the integration manifest and add-on metadata with the `v0.2.15` release.

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
