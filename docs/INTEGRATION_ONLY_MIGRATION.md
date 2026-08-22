# Integration-Only Migration Ledger

This document is the durable plan, behavior contract, decision log, and test ledger for replacing the companion .NET add-on with a standalone Home Assistant integration.

## Status

- Working branch: `feature/integration-only`
- Branch point: `1184737` (`Update README GWM logo to SVG`)
- Current checkpoint: Task 1 complete
- Next checkpoint: Task 2 — offline Python crypto, signing, and TLS proof of concept
- Runtime behavior changed so far: none

Work proceeds one explicitly approved task at a time. At the end of every task, update this document, run the checks appropriate to that checkpoint, create one focused local commit, report the result, and stop. Do not begin the next task without a new user green light.

## Working Agreement

- Keep the existing add-on path functional until the final cutover.
- Prefer targeted tests at ordinary checkpoints and full suites at the major gates below.
- Never commit account credentials, verification codes, tokens, certificates issued to a user, private keys, VINs, locations, or unsanitized cloud responses.
- Live read-only tests require explicit approval for the corresponding task.
- Live climate, lock, unlock, window, and charging-plan operations require an additional explicit confirmation immediately before testing them.
- Do not publish packages, push the branch, open a pull request, merge, or release without separate approval.
- If `main` moves materially during this long-lived effort, report the drift before merging or rebasing it into this branch.

## Architecture

### Current architecture

```text
GWM cloud
    ^
    | regional HTTPS, request signing, authentication, and mutual TLS
    v
.NET 10 add-on
    - account options and mutable session/certificate state
    - regional GWM protocol implementation
    - periodic vehicle polling and snapshot mapping
    - remote commands and charging plans
    - in-memory vehicle/command cache
    - authenticated local HTTP API
    - Supervisor discovery and Ingress status page
    ^
    | HTTP on the Supervisor network
    v
Home Assistant custom integration
    - Supervisor or manual host/port/token configuration
    - 30-second polling of the add-on cache
    - Home Assistant devices, entities, actions, and diagnostics
```

The add-on polls GWM at a configurable interval of 30–3600 seconds, with a 60-second default. The integration independently polls the add-on cache every 30 seconds. Relevant entry points are:

- Add-on startup and local API: [`Program.cs`](../addons/gwm_ora/src/GwmOra.Addon/Program.cs)
- Add-on cloud polling: [`VehiclePollingWorker.cs`](../addons/gwm_ora/src/GwmOra.Addon/Gwm/VehiclePollingWorker.cs)
- Add-on state: [`AddonState.cs`](../addons/gwm_ora/src/GwmOra.Addon/Configuration/AddonState.cs)
- Integration local client: [`api.py`](../custom_components/gwm_ora/api.py)
- Integration coordinator: [`coordinator.py`](../custom_components/gwm_ora/coordinator.py)

### Target architecture

```text
GWM cloud
    ^
    | regional HTTPS, request signing, authentication, and scoped mutual TLS
    v
Async Python GWM client library
    - no Home Assistant imports
    - protocol, models, authentication, signing, certificates, and commands
    ^
    | typed async API and exceptions
    v
Home Assistant integration
    - config, verification, reauthentication, and options flows
    - one account-level DataUpdateCoordinator poll
    - per-entry persistent state and command reconciliation
    - devices, entities, actions, availability, and diagnostics
```

The migration ports the GWM behavior, not the daemon shape. The ASP.NET server, local bearer-token boundary, Supervisor discovery, duplicate cache polling, and Ingress page do not move into Python.

## Current Ownership and Event Flow

| Concern | Current owner | Target owner |
| --- | --- | --- |
| Account setup and feature opt-ins | Add-on options | HA config and options flows |
| Login and verification | Add-on authentication service | Async client driven by HA config/reauth flows |
| Access/refresh tokens and device identity | Add-on state JSON | Per-config-entry HA storage |
| Client certificates and private keys | Add-on state JSON | Per-config-entry HA storage with strict redaction |
| Regional request signing and API calls | `LibGwmApi` | HA-independent async Python client |
| Vehicle polling | Add-on worker, then integration cache polling | One `DataUpdateCoordinator` poll |
| Snapshot mapping | Add-on mapper | Async Python client/model layer |
| Vehicle entities | Integration | Integration, retaining existing entity contracts |
| Remote command execution | Add-on background task | Lifecycle-managed async client task plus durable reconciliation |
| Charging-plan ownership tracking | Add-on state JSON | Per-entry persistent state |
| Add-on health page and discovery | Add-on/Supervisor | Removed; use HA diagnostics, repairs, and config flows |

The current local API contract contains:

- Health, cached vehicle list, and explicit refresh.
- Command-status lookup.
- Climate, lock/unlock, and close-window command submission.
- Charging-plan read, set, and clear behavior.

That HTTP contract is a migration reference, not part of the final architecture.

## Behavior-Parity Contract

The add-on must not be retired until the Python path preserves the behaviors below or an intentional change is documented and approved.

### Supported regions

| Region | Configuration | Behavior that must remain isolated and tested |
| --- | --- | --- |
| Europe/Israel | `region: eu` and the account registration country | EU v2 password login and verification, token refresh, EU request canonicalization/signing, general-certificate bootstrap, per-device certificate enrollment, mutual TLS, vehicle reads, and commands |
| Australia/New Zealand | `region: aus` and the exact account registration country | ANZ login and verification, `bt-auth` behavior, single-active-session recovery, ANZ query canonicalization, tolerant API quirks, required VIN headers, vehicle reads, and commands |
| Russia | `region: rus`, normally with `country: RU` | Russia login and verification payloads, Russian certificate chain and mutual TLS, regional routing/headers/signing, string-or-number decoding, vehicle reads, and Russia-specific command/result behavior |

Regional logic must remain strategy-like and separately tested. A fix for one region must not silently alter another region’s signing, serialization, headers, endpoints, or result interpretation.

### Configuration

The current user-facing configuration comprises:

- Account registration country.
- Cloud region: `eu`, `aus`, or `rus`.
- Account username/e-mail and password.
- One-time verification code when required.
- Vehicle security PIN for remote controls.
- Independent opt-ins for remote commands and charging control.
- Cloud polling interval from 30 to 3600 seconds.
- Logging level.

The target replaces add-on configuration-and-restart steps with native config, verification, reauthentication, and options flows. Sensitive values must never appear in logs or diagnostics.

### Persistent state

The current add-on persists:

- Stable generated device ID.
- Access and refresh tokens.
- GWM and bean user identifiers.
- Enrolled client certificate and private key.
- Verification-code request timestamp.
- Exact per-VIN charging plans written by this project.
- Local API token and Supervisor discovery UUID, which become obsolete after cutover.

State writes must remain serialized and crash-safe. Changing accounts must invalidate account-specific certificates and tokens. The future command journal must persist accepted command identifiers before relying on background result polling.

### Vehicle discovery and normalized snapshots

The integration must continue to discover multiple vehicles dynamically and identify them by VIN. Existing entity code consumes the normalized snapshot contract defined in [`ApiModels.cs`](../addons/gwm_ora/src/GwmOra.Addon/Models/ApiModels.cs), including:

- Identity, manufacturer, model, serial number, and location.
- Acquisition, vehicle-update, and local-refresh timestamps.
- Capabilities and command status.
- Battery SOC, electric range, fuel level/range, charging state, charge-plug state, remaining charging time, and SOCE.
- Tire pressures, tire temperatures, odometer, and cabin temperature.
- A/C, locks, doors, windows, trunk, sunroof, circulation, and defrosters.
- GPS authorization, tire warning states, and window learning states.
- Steering-wheel, windscreen, and seat heating/ventilation values.
- Engine/raw state codes and the complete raw-item map for diagnostics and future mapping.
- Climate mode/action, target/current temperature, bounds, step, and operation time.

The existing HA platforms must remain available: sensor, binary sensor, device tracker, climate, lock, button, number, and switch. Missing regional/model values remain unknown or unavailable without breaking other entities.

### Polling and availability

- Use one coordinated GWM poll per account instead of one add-on poll plus one integration cache poll.
- Preserve a responsible configurable interval and prevent overlapping account refreshes.
- Distinguish invalid authentication from transient connectivity, malformed responses, and rate limiting.
- Trigger HA reauthentication for rejected credentials/tokens.
- Mark data unavailable appropriately rather than treating indefinitely stale cached data as a successful cloud update.
- Preserve dynamic addition of newly discovered vehicles and avoid destructive removal on a transient cloud omission.

### Remote vehicle commands

Remote controls remain behind both explicit opt-in and a configured security PIN:

- Climate on/off, temperature, and operation time.
- Door lock and unlock.
- Close all windows.
- Region-specific payload construction, prerequisite calls, VIN headers, pending codes, timeouts, and result selection.
- User-visible command progress and an immediate vehicle refresh after successful completion.

The direct implementation must not blindly reproduce an in-memory fire-and-forget task. It must:

1. Distinguish local validation, GWM acceptance, and final vehicle outcome.
2. Persist enough accepted-command metadata to resume or reconcile result polling after an HA reload/restart.
3. Never automatically resend a command whose outcome is uncertain.
4. Cancel and clean up tasks correctly on entry unload while retaining a recoverable status.

### Charging control

Charging control remains a separate opt-in and does not require the remote-control PIN:

- Read the current charging plan.
- Set a validated start/end window.
- Clear the plan so charging can begin on plug-in.
- Provide the existing switch and `gwm_ora.set_charging_plan` / `gwm_ora.clear_charging_plan` actions.
- Track the exact plan written by this project.
- During cleanup, remove only a still-matching owned plan and never delete a schedule replaced or changed by the official GWM app.

### Security and privacy

- The legacy `DEFAULT@SECLEVEL=0` requirement may only be applied to a dedicated GWM SSL context. It must never alter Home Assistant’s process-wide OpenSSL configuration or default HTTPS behavior.
- Offload blocking certificate, key, and filesystem work from the HA event loop.
- Redact passwords, PINs, tokens, client certificates, private keys, identifiers, VINs, location, and other personal data from diagnostics and logs.
- Keep request signing deterministic and covered by golden vectors without exposing secrets in test output.

## POC Non-Goals

Tasks 1–3 intentionally do not:

- Change the released add-on or integration runtime path.
- Send climate, lock, window, or charging commands.
- Persist live credentials or cloud responses in the repository.
- Attempt automated migration from add-on state.
- Publish a Python package.
- Remove or deprecate any current feature.

## Acceptance Gates

### Gate A — Technical feasibility

After Task 3, Python must reproduce the offline signing/certificate vectors and retrieve a sanitized live vehicle snapshot directly from at least one GWM region using scoped TLS. Failure pauses the migration for reassessment.

### Gate B — Read-only integration

After Task 10, a native config flow and direct coordinator must provide stable read-only entities without the add-on, with correct reauthentication, availability, unloading, and redaction.

### Gate C — Write parity

After Task 15, commands and charging control must pass fixture tests, lifecycle/restart tests, and the explicitly approved live regional matrix available to the project.

### Gate D — Cutover readiness

After Task 18, packaging, installation migration, documentation, complete tests, and fresh-install validation must pass before the add-on is removed from the supported architecture.

## Roadmap

- [x] Task 1 — Create branch, record baseline, architecture, and parity contract.
- [ ] Task 2 — Build offline Python crypto, signing, and scoped-TLS POC.
- [ ] Task 3 — Run a live read-only direct-cloud POC in an available region.
- [ ] Task 4 — Harden the POC into an async, typed, HA-independent client foundation.
- [ ] Task 5 — Implement EU production authentication and read parity.
- [ ] Task 6 — Implement ANZ production authentication and read parity.
- [ ] Task 7 — Implement Russia production authentication and read parity.
- [ ] Task 8 — Port and fixture-test normalized snapshot/model mapping.
- [ ] Task 9 — Add direct-cloud config, verification, reauth, reconfigure, and options flows.
- [ ] Task 10 — Add the direct read-only coordinator and existing entity platforms.
- [ ] Task 11 — Add persistent client state and a restart-safe command journal.
- [ ] Task 12 — Add climate command parity.
- [ ] Task 13 — Add lock/unlock and close-window parity.
- [ ] Task 14 — Add charging-control parity.
- [ ] Task 15 — Complete hardening and the regional/lifecycle parity matrix.
- [ ] Task 16 — Resolve packaging, dependency, licensing, and certificate provenance.
- [ ] Task 17 — Implement the approved existing-installation migration path.
- [ ] Task 18 — Remove add-on/proxy code and complete final validation and documentation.

## Decision Log

| ID | Date | Decision | Reason |
| --- | --- | --- | --- |
| D-001 | 2026-08-22 | Develop on `feature/integration-only`. | Isolate the long-running migration from `main`. |
| D-002 | 2026-08-22 | Advance only one explicitly approved task at a time. | Bound weekly quota usage and keep checkpoints reviewable. |
| D-003 | 2026-08-22 | Keep the add-on functional until final cutover. | Preserve a working reference and rollback path while parity is incomplete. |
| D-004 | 2026-08-22 | Prove offline crypto/TLS and one live read before production migration. | Resolve the principal technical risk before making broad changes. |
| D-005 | 2026-08-22 | Make the protocol client HA-independent and async. | Separate GWM protocol concerns from HA lifecycle/entity concerns and keep future packaging possible. |
| D-006 | 2026-08-22 | Never apply legacy TLS settings globally. | A process-wide security downgrade inside HA is unacceptable. |
| D-007 | 2026-08-22 | Preserve separate regional strategies and tests. | EU, ANZ, and Russia differ in authentication, signing, TLS, payloads, and response behavior. |
| D-008 | 2026-08-22 | Require separate approval before every live write test. | Remote commands and charging schedules affect a real vehicle. |
| D-009 | 2026-08-22 | Defer PyPI/bundling and installation-migration decisions until after the POC. | Avoid release and migration work before feasibility is demonstrated. |

## Baseline Evidence

Baseline captured on 2026-08-22 from commit `1184737` before runtime changes.

### Python

Environment:

- Ubuntu under WSL2, matching the Linux CI platform.
- CPython 3.13.13.
- Home Assistant 2026.2.3.
- pytest 9.1.1 and pytest-asyncio 1.4.0.
- Ruff 0.16.4.

Results:

```text
python -m ruff check custom_components tests/python
All checks passed!

python -m pytest tests/python
37 passed, 1 warning in 4.35s
```

The warning is an upstream `aiohttp.web.Application` inheritance deprecation emitted while importing Home Assistant’s HTTP component; there were no project warnings or failures.

### .NET

Environment:

- Windows x64.
- .NET SDK 10.0.303 and .NET runtime 10.0.11.
- Release configuration.

Results:

```text
dotnet restore
dotnet test --no-restore --configuration Release
128 passed, 0 failed, 0 skipped
```

Restore/build emitted existing `CS8632` warnings where nullable annotations appear in the `LibGwmApi` project while nullable context is disabled. They did not fail the build or tests.

## Checkpoint Log

### Task 1 — Branch, baseline, and migration ledger

Status: complete on 2026-08-22.

Delivered:

- Created `feature/integration-only` from clean `main` at `1184737`.
- Ran the repository’s CI-equivalent Python lint/tests and .NET restore/tests.
- Recorded the current two-process architecture and the integration-only target.
- Captured the regional, data, entity, command, charging, security, and persistence parity contract.
- Recorded decisions, acceptance gates, risks, and the remaining task sequence.
- Made no runtime behavior changes.

### Next approved checkpoint

Task 2 will add only offline, library-shaped Python POC code and deterministic tests for signing, transformed-key recovery, certificates/CSR, and a scoped GWM TLS context. It will not connect to GWM, change HA runtime behavior, send commands, or store live secrets.

## Open Risks and Questions

- Whether every GWM legacy TLS requirement can be scoped to a dedicated Python SSL context on all supported HA architectures.
- Exact live parity of undocumented authentication and response behavior across EU, ANZ, and Russia.
- Availability of safe test accounts/vehicles for every regional read and write matrix.
- ANZ’s single-active-session behavior during side-by-side migration testing.
- Safe handling and future renewal of bundled bootstrap certificates and OEM-derived key material.
- Licensing/provenance of code and resources derived from earlier reverse-engineering work.
- Whether the final client is bundled for HACS or published as a separately versioned Python dependency.
- Whether existing users perform one fresh authentication or use a temporary secured state-export path.
- Durable reconciliation semantics for a command accepted immediately before HA reload, shutdown, or loss of connectivity.
