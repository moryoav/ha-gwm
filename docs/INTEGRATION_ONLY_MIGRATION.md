# Integration-Only Migration Ledger

This document is the durable plan, behavior contract, decision log, and test ledger for replacing the companion .NET add-on with a standalone Home Assistant integration.

## Status

- Working branch: `feature/integration-only`
- Branch point: `1184737` (`Update README GWM logo to SVG`)
- Current checkpoint: Task 3 complete; Gate A passed
- Next checkpoint: Task 4 — async, typed, HA-independent client foundation (not yet approved)
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

### Gate A — Technical feasibility (passed 2026-08-24)

After Task 3, Python must reproduce the offline signing/certificate vectors and retrieve a sanitized live vehicle snapshot directly from at least one GWM region using scoped TLS. Failure pauses the migration for reassessment.

### Gate B — Read-only integration

After Task 10, a native config flow and direct coordinator must provide stable read-only entities without the add-on, with correct reauthentication, availability, unloading, and redaction.

### Gate C — Write parity

After Task 15, commands and charging control must pass fixture tests, lifecycle/restart tests, and the explicitly approved live regional matrix available to the project.

### Gate D — Cutover readiness

After Task 18, packaging, installation migration, documentation, complete tests, and fresh-install validation must pass before the add-on is removed from the supported architecture.

## Roadmap

- [x] Task 1 — Create branch, record baseline, architecture, and parity contract.
- [x] Task 2 — Build offline Python crypto, signing, and scoped-TLS POC.
- [x] Task 3 — Run a live read-only direct-cloud POC in an available region.
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
| D-010 | 2026-08-24 | Keep the POC in a repository-root `gwm_ora_client` package with no HA imports. | Prove the protocol boundary independently while packaging for HACS versus PyPI remains deferred. |
| D-011 | 2026-08-24 | Apply `DEFAULT@SECLEVEL=0` only to a newly created GWM `SSLContext`, retaining Python's default protocol bounds, hostname checks, certificate verification, and system trust. | The legacy SHA-1 CA chain requires OpenSSL authentication level 0, but unrelated HA HTTPS traffic must retain its normal security policy. |
| D-012 | 2026-08-24 | Pass the unchanged OEM CA bundles directly to OpenSSL after strict PEM/DER-envelope validation. | Modern `cryptography` rejects invalid characters in the legacy CA subjects; rewriting signed certificates would invalidate them, while OpenSSL accepts the original bundles at the required scoped security level. |
| D-013 | 2026-08-24 | Make the live POC reuse-only and limit it to vehicle discovery plus one status read. | Existing add-on state avoids login, refresh, verification, enrollment, and ANZ session-reclaim side effects; omitting user-profile retrieval minimizes personal data handled by the proof. |
| D-014 | 2026-08-24 | Treat the cloud `vin` field as an opaque bounded vehicle identifier and require canonical percent encoding. | GWM distinguishes its internal encoded identifier from the displayed VIN; strict round-trip encoding preserves compatibility without permitting query injection. |

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

## Task 2 Feasibility Evidence

Evidence captured on 2026-08-24 from `feature/integration-only` before the Task 2 checkpoint commit. All work was offline: no GWM endpoint, account, credential, token, VIN, or vehicle command was used.

Environment:

- Ubuntu under WSL2 with CPython 3.13.13.
- Home Assistant 2026.2.3 for the complete integration suite.
- `cryptography` 46.0.5 and OpenSSL 3.5.6.
- A separate dependency-minimal run installed only `cryptography` and pytest for the client POC tests; Home Assistant was not installed in that environment.

Results:

- Reproduced all 13 existing C# signature vectors: EU `gwm-auth`, EU `bt-auth`, ANZ `bt-auth`, and Russia `gwm-auth`.
- Preserved the regional query differences, exact JSON-body signing, nonce case, app profiles, .NET whitespace behavior, and whole-string percent encoding.
- Parsed both general client certificates, reversed both transformed private exponents, recovered valid RSA-2048 CRT keys, matched their certificate public keys, and signed/verified fixed messages.
- Validated the three-certificate EU and Russia PEM envelopes and regional subject markers without altering the signed legacy CA data.
- Generated a fixed-input enrollment CSR with the expected subject order and values, RSA-2048 key, SHA-256 with PKCS#1 v1.5 signature, Base64 DER CSR, and Base64 PKCS#8 key.
- Loaded both regional CA bundles and recovered client identities into dedicated GWM contexts while retaining hostname verification, `CERT_REQUIRED`, and Python's default TLS protocol bounds.
- Proved that constructing a GWM context changes neither `OPENSSL_CONF`, Python's default HTTPS-context factory/cipher policy, nor the security level and ciphers of a fresh default context.
- Added strict rejection for incomplete, corrupt, or trailing-garbage CA bundles and mismatched or structurally changed transformed keys.
- Kept URLs, signed bodies, CSRs, and private keys out of result-object representations.

The actual legacy-chain reason was also reproduced with frozen-time offline OpenSSL verification for both EU and Russia:

```text
openssl verify -attime 1786119079 -auth_level 2 ...
error 68 at 1 depth lookup: CA signature digest algorithm too weak

openssl verify -attime 1786119079 -auth_level 0 ...
gwm_general.cer: OK
gwm_general_rus.cer: OK
```

Validation:

```text
# Dependency-minimal client run (no Home Assistant installed)
python -m pytest tests/python/client
44 passed

python -m ruff check gwm_ora_client custom_components tests/python
All checks passed!

python -m compileall -q gwm_ora_client custom_components tests/python
# no output; exit 0

python -m pytest tests/python
81 passed, 1 warning

dotnet test --no-restore --configuration Release
128 passed, 0 failed, 0 skipped
```

The Python warning remains the same upstream `aiohttp.web.Application` inheritance deprecation recorded at baseline. The .NET build likewise retains only the baseline `CS8632` warnings.

## Task 3 Live Feasibility Evidence

Evidence captured on 2026-08-24 from `feature/integration-only`. The proof reused a temporary copy of existing EU/IL add-on state and performed no login, token refresh, verification, certificate enrollment, or vehicle mutation.

Live result:

- Retrieved the account's vehicle list directly from the EU app gateway and then retrieved one vehicle's last-status snapshot.
- Sent exactly two allowlisted HTTPS `GET` requests: `globalapp/vehicle/acquireVehicles` and `vehicle/getLastStatus`.
- Authenticated with the existing access token and matching device identity; no password, refresh token, verification code, security PIN, login, refresh, certificate enrollment, session reclaim, or user-profile request was used.
- Loaded the existing enrolled EU client identity and direct issuing intermediate into a dedicated mutual-TLS context at OpenSSL security level 0.
- Proved during the live run that hostname verification and `CERT_REQUIRED` remained enabled and that a fresh default TLS context retained its original security level, protocol bounds, cipher fingerprint, and HTTPS-context factory.
- Received a non-empty discovery result and a status response containing item values plus timestamp, location, battery-SOC, and charging-signal fields. Only presence booleans were retained; identifiers, exact values, vehicle or status-item counts, model details, timestamps, coordinates, full request headers, query-bearing URLs, and raw response bodies were not recorded.
- Used no redirects, proxies, cookies, retries, write-capable route, or fallback authentication path.

Operational handling and restoration:

- Created an add-on-only temporary Supervisor backup to obtain a consistent state copy outside the repository.
- Stopped the add-on immediately around the two cloud reads and restarted it in the orchestration's unconditional recovery path.
- Verified afterward that the add-on was `started`, automatic boot remained enabled, and the active Home Assistant config entry was `loaded`.
- Removed the exact temporary Supervisor backup and the local backup, nested add-on archive, and extracted state file. No live credential, issued certificate/key, vehicle identifier, location, or cloud response remains in the worktree or temporary evidence.

Validation:

```text
python -m pytest tests/python/client
104 passed

python -m ruff check gwm_ora_client custom_components tests/python
All checks passed!

python -m compileall -q gwm_ora_client custom_components tests/python
# no output; exit 0

python -m pytest tests/python
141 passed, 1 warning

dotnet test --no-restore --configuration Release
128 passed, 0 failed, 0 skipped
```

The Python warning and .NET nullable-annotation warnings are the unchanged baseline warnings. Gate A is passed for direct EU vehicle reads. ANZ and Russia remain offline-parity-only until their later regional checkpoints.

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

### Task 2 — Offline Python crypto, signing, and scoped TLS POC

Status: complete on 2026-08-24.

Delivered:

- Added HA-independent, offline signing, certificate/key recovery, CSR, and TLS-context primitives under `gwm_ora_client`.
- Ported every current C# signing golden vector and added canonicalization edge coverage.
- Proved recovery and use of the existing EU and Russia transformed bootstrap keys.
- Proved that the real regional legacy CA material and matching identities load into context-local OpenSSL policy without changing process defaults.
- Added architectural, malformed-input, and sensitive-representation tests.
- Extended CI and contributor check scopes to include the standalone client package and made `cryptography` an explicit CI dependency.
- Made no HA integration/add-on runtime change and performed no network I/O.

### Task 3 — Live read-only direct-cloud POC

Status: complete on 2026-08-24; Gate A passed for EU.

Delivered:

- Added a disposable, HA-independent, reuse-only live runner with exactly two post-signing allowlisted GET routes.
- Added fail-closed regional host, method, path, query, country, header, response-size, redirect, proxy, retry, and error-category boundaries.
- Kept ordinary TLS for ANZ and restricted the legacy mutual-TLS context to EU/Russia on a POSIX runtime with protected temporary identity files.
- Added offline tests for all three regional routes/signatures/TLS policies, state isolation, opaque vehicle identifiers, canonical encoding, certificate-chain selection, temporary-key cleanup, redaction, malformed responses, and mutation/session endpoint rejection.
- Completed a sanitized live EU discovery and last-status read using existing add-on state, without login, refresh, enrollment, commands, or user-profile retrieval.
- Restored the running add-on and loaded integration entry and removed all temporary live material.
- Replaced captured or real-looking vehicle identifiers in existing vectors, regional tests, and service examples with explicit synthetic fixtures.
- Made no released add-on or integration runtime-path change.

### Next checkpoint (requires explicit approval)

Task 4 will turn the disposable proof into an async, typed, HA-independent client foundation. It will define production transport, regional strategy, models, exception taxonomy, cancellation/timeouts, and test fixtures while retaining the existing add-on/integration runtime path. Task 4 will not yet implement production login flows, switch Home Assistant to direct cloud access, send vehicle commands, publish anything, or remove the Task 3 safety evidence.

## Open Risks and Questions

- Cross-architecture confirmation of the scoped GWM SSL context; Linux x86-64/OpenSSL 3.5.6 is proven offline, while supported ARM architectures remain untested.
- The bundled EU general bootstrap certificate expires on 2027-01-04 and needs a renewal/provenance plan before production cutover.
- Modern `cryptography` rejects invalid PrintableString characters in the legacy OEM CA subjects; the POC validates their envelopes and lets OpenSSL consume the original signed bytes instead.
- Exact live parity of undocumented authentication and response behavior in ANZ and Russia; EU read transport is now proven live, while production EU authentication remains unimplemented.
- Availability of safe test accounts/vehicles for every regional read and write matrix.
- ANZ’s single-active-session behavior during side-by-side migration testing.
- Safe handling and future renewal of bundled bootstrap certificates and OEM-derived key material.
- Licensing/provenance of code and resources derived from earlier reverse-engineering work.
- Whether the final client is bundled for HACS or published as a separately versioned Python dependency.
- Whether existing users perform one fresh authentication or use a temporary secured state-export path.
- Durable reconciliation semantics for a command accepted immediately before HA reload, shutdown, or loss of connectivity.
