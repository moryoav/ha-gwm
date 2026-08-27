# Integration-Only Migration Ledger

This document is the durable plan, behavior contract, decision log, and test ledger for replacing the companion .NET add-on with a standalone Home Assistant integration.

## Status

- Working branch: `feature/integration-only`
- Branch point: `1184737` (`Update README GWM logo to SVG`)
- Current checkpoint: Task 6 complete; the 2026-08-27 `main` drift and China scope review is recorded; Gate A remains passed
- Next checkpoint: Task 7 — synchronize released `main`/`v0.12.0` behavior into this branch (not yet approved)
- Reviewed local `main`: `9daff32` (`v0.12.0`); the integration-only branch has not merged or ported that runtime behavior yet
- Released add-on and integration runtime behavior changed so far on this branch: none

Work proceeds one explicitly approved task at a time. At the end of every task, update this document, run the checks appropriate to that checkpoint, create one focused local commit, report the result, and stop. Do not begin the next task without a new user green light.

## Working Agreement

- Keep the existing add-on path functional until the final cutover.
- Prefer targeted tests at ordinary checkpoints and full suites at the major gates below.
- Never commit account credentials, verification codes, tokens, certificates issued to a user, private keys, VINs, locations, or unsanitized cloud responses.
- Live read-only tests require explicit approval for the corresponding task.
- Live climate, lock, unlock, window, and charging-plan operations require an additional explicit confirmation immediately before testing them.
- Do not publish packages, push the branch, open a pull request, merge, or release without separate approval.
- If `main` moves materially during this long-lived effort, review and record the drift before synchronizing it. Because this published feature branch is long-lived, prefer an explicit merge checkpoint over rebasing or selective cherry-picking of a dependent release series.

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
- The configured region alongside the cached vehicle-list response.
- Command-status lookup.
- Climate, lock/unlock, and close-window command submission.
- Experimental China-only engine, horn/light, tailgate, and sunroof command submission.
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
| Mainland China (experimental) | `region: cn`, `country: CN`, and the registered phone number | SMS-only G-App login, separate G-App/BeanTech/AutoAI sessions, three signing/encryption schemes, 32-character device identity, fixed UTC+08:00 timestamps, app-like transport, NavInfo-only discovery/status reads, China status translation, no-PIN commands, and China charging behavior |

Regional logic must remain strategy-like and separately tested. China is a separate multi-service strategy, not a fourth overseas-gateway policy. A fix for one region or service must not silently alter another region’s signing, serialization, headers, endpoints, transport, session, or result interpretation.

### Configuration

The current user-facing configuration comprises:

- Account registration country.
- Cloud region: `eu`, `aus`, `rus`, or experimental `cn`.
- Account username/e-mail and password outside China; registered phone number and no password for China.
- One-time verification code when required; China always starts a fresh login through an SMS continuation.
- Vehicle security PIN for remote controls outside China; the China app protocol does not send a PIN.
- Independent opt-ins for remote commands and charging control.
- Cloud polling interval from 30 to 3600 seconds.
- Logging level.

The target replaces add-on configuration-and-restart steps with native config, verification, reauthentication, and options flows. Sensitive values must never appear in logs or diagnostics.

### Persistent state

The current add-on persists:

- Stable generated device ID.
- Access and refresh tokens.
- GWM and bean user identifiers.
- A separate China session containing the G-App, BeanTech, and AutoAI tokens and identifiers.
- Enrolled client certificate and private key.
- Verification-code request timestamp.
- An authentication-context binding used to prevent state reuse after a region, country, account, or password change.
- Exact per-VIN charging plans written by this project.
- Local API token and Supervisor discovery UUID, which become obsolete after cutover.

State writes must remain serialized and crash-safe. Changing the region, country, account, or password must invalidate every account-bound token, identifier, certificate, verification throttle, partial China session, owned charging plan, and command-journal entry before any new login. China must also be able to publish a bounded partial continuation when G-App tokens rotate but BeanTech/AutoAI initialization later fails. The future command journal must persist accepted command identifiers before relying on background result polling.

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

Remote controls remain behind explicit opt-in. Europe, ANZ, and Russia also require a configured security PIN; the China protocol does not send one:

- Climate on/off, temperature, and operation time; China additionally exposes heating and uses a separate parameter-update operation when HVAC is already running.
- Door lock and unlock.
- Close all windows.
- China-only remote engine start/stop, horn, light flash, combined vehicle search, tailgate open/close, and sunroof close/tilt/half/full controls.
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
- Preserve China’s separate AutoAI weekly-schedule command, fixed-China-time conversion, weekday encoding, and synthesized plan-read semantics without claiming live validation that has not occurred.

### Security and privacy

- The legacy `DEFAULT@SECLEVEL=0` requirement may only be applied to a dedicated GWM SSL context. It must never alter Home Assistant’s process-wide OpenSSL configuration or default HTTPS behavior.
- China’s required gzip response profile must use bounded decompression and remain isolated from the overseas clients; an HTTP/2-capable dependency may be added only after the China transport POC establishes that it is needed and can retain the same redirect, proxy, cookie, timeout, and redaction boundaries.
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

Task 8 is a second, China-specific feasibility checkpoint. It will remain reuse-only and read-only: no SMS request or login, command, charging write, Home Assistant wiring, durable credential import, or released runtime-path change is permitted. Any live China read still requires explicit approval and a separately agreed sanitized-state procedure.

## Acceptance Gates

### Gate A — Technical feasibility (passed 2026-08-24)

After Task 3, Python must reproduce the offline signing/certificate vectors and retrieve a sanitized live vehicle snapshot directly from at least one GWM region using scoped TLS. Failure pauses the migration for reassessment.

### Gate A-CN — China transport feasibility

After Task 8, Python must reproduce all three China crypto/signing families and complete a bounded end-to-end synthetic-service discovery/status round trip using the selected transport. Before `cn` can be enabled in a direct Home Assistant flow or the add-on can be retired for a China user, a sanitized live read-only validation must also pass using either an existing session or an explicitly approved Task 9 SMS login. Lack of suitable China access does not block work for the other regions, but it does block claiming China cutover readiness.

### Gate B — Read-only integration

After Task 14, native config flows, account-bound state, and a direct coordinator must provide stable read-only entities without the add-on, with correct reauthentication, restart behavior, availability, unloading, and redaction.

### Gate C — Write parity

After Task 19, commands and charging control must pass fixture tests, lifecycle/restart tests, and the explicitly approved live regional matrix available to the project. Experimental China operations remain labeled as such until separately live-validated.

### Gate D — Cutover readiness

After Task 22, packaging, installation migration, documentation, complete tests, and fresh-install validation must pass before the add-on is removed from the supported architecture. Gate A-CN must also be fully passed before China users can be included in that cutover.

## Roadmap

- [x] Task 1 — Create branch, record baseline, architecture, and parity contract.
- [x] Task 2 — Build offline Python crypto, signing, and scoped-TLS POC.
- [x] Task 3 — Run a live read-only direct-cloud POC in an available region.
- [x] Task 4 — Harden the POC into an async, typed, HA-independent client foundation.
- [x] Task 5 — Implement EU production authentication and read parity.
- [x] Task 6 — Implement ANZ production authentication and read parity.
- [ ] Task 7 — Merge the reviewed released `main` series into this branch and re-establish the full baseline.
- [ ] Task 8 — Prove the isolated China crypto, transport, and reuse-only read path.
- [ ] Task 9 — Implement China production SMS authentication, multi-service sessions, and read parity.
- [ ] Task 10 — Implement Russia production authentication and read parity.
- [ ] Task 11 — Port and fixture-test four-region normalized snapshot/model mapping.
- [ ] Task 12 — Add direct-cloud config, verification, reauth, reconfigure, and options flows.
- [ ] Task 13 — Add the direct read-only coordinator and existing entity platforms.
- [ ] Task 14 — Add persistent account-bound client state and a restart-safe command journal.
- [ ] Task 15 — Add climate command parity, including China heating and in-place parameter updates.
- [ ] Task 16 — Add lock/unlock and close-window parity, including isolated China no-PIN behavior.
- [ ] Task 17 — Add China-only engine, horn/light, tailgate, and sunroof controls and HA buttons.
- [ ] Task 18 — Add charging-control parity, including China weekly-schedule behavior.
- [ ] Task 19 — Complete four-region hardening and the lifecycle/write parity matrix.
- [ ] Task 20 — Resolve packaging, dependency, licensing, certificate, and protocol-material provenance.
- [ ] Task 21 — Implement the approved existing-installation migration path.
- [ ] Task 22 — Remove add-on/proxy code and complete final validation and documentation.

The changed checkpoints stay intentionally narrow:

- Task 7 is synchronization only. Merge the complete released mainline series, semantically review auto-merged documentation/translations, run the full Python and .NET baselines, and make no direct-cloud Python behavior change.
- Task 8 is the early China stop/go POC. Port deterministic crypto/time vectors, exact app-like request bytes, 32-character device identity, bounded gzip handling, and the three-service transport boundary; prove only discovery and status with synthetic services and, if separately approved and available, a reused live session. Do not request or submit an SMS code.
- Task 9 turns that proof into immutable production authentication/read behavior: SMS continuation and throttling, exact error/risk-control handling, G-App refresh, bounded BeanTech/AutoAI initialization, partial-session publication, corrected discovery routing, NavInfo-only enforcement, sanitized China fixtures, and typed reads. It remains HA-independent, non-persistent, and command-free.
- Task 10 retains the previously planned Russia authentication/read checkpoint. Moving it after the China feasibility work prevents the current overseas-only client shape from hiding a transport or strategy blocker introduced by the newly released region.
- Tasks 11–22 preserve the original read integration, persistence, command, charging, packaging, migration, and cutover progression while expanding every relevant fixture and gate to four regions. China-only write surfaces remain a separate Task 17 so their experimental status and live-safety approvals cannot be obscured by already-supported commands.

## Decision Log

| ID | Date | Decision | Reason |
| --- | --- | --- | --- |
| D-001 | 2026-08-22 | Develop on `feature/integration-only`. | Isolate the long-running migration from `main`. |
| D-002 | 2026-08-22 | Advance only one explicitly approved task at a time. | Bound weekly quota usage and keep checkpoints reviewable. |
| D-003 | 2026-08-22 | Keep the add-on functional until final cutover. | Preserve a working reference and rollback path while parity is incomplete. |
| D-004 | 2026-08-22 | Prove offline crypto/TLS and one live read before production migration. | Resolve the principal technical risk before making broad changes. |
| D-005 | 2026-08-22 | Make the protocol client HA-independent and async. | Separate GWM protocol concerns from HA lifecycle/entity concerns and keep future packaging possible. |
| D-006 | 2026-08-22 | Never apply legacy TLS settings globally. | A process-wide security downgrade inside HA is unacceptable. |
| D-007 | 2026-08-22 | Preserve separate regional strategies and tests. | EU, ANZ, Russia, and China differ in authentication, signing, TLS/transport, payloads, session shape, and response behavior. |
| D-008 | 2026-08-22 | Require separate approval before every live write test. | Remote commands and charging schedules affect a real vehicle. |
| D-009 | 2026-08-22 | Defer PyPI/bundling and installation-migration decisions until after the POC. | Avoid release and migration work before feasibility is demonstrated. |
| D-010 | 2026-08-24 | Keep the POC in a repository-root `gwm_ora_client` package with no HA imports. | Prove the protocol boundary independently while packaging for HACS versus PyPI remains deferred. |
| D-011 | 2026-08-24 | Apply `DEFAULT@SECLEVEL=0` only to a newly created GWM `SSLContext`, retaining Python's default protocol bounds, hostname checks, certificate verification, and system trust. | The legacy SHA-1 CA chain requires OpenSSL authentication level 0, but unrelated HA HTTPS traffic must retain its normal security policy. |
| D-012 | 2026-08-24 | Pass the unchanged OEM CA bundles directly to OpenSSL after strict PEM/DER-envelope validation. | Modern `cryptography` rejects invalid characters in the legacy CA subjects; rewriting signed certificates would invalidate them, while OpenSSL accepts the original bundles at the required scoped security level. |
| D-013 | 2026-08-24 | Make the live POC reuse-only and limit it to vehicle discovery plus one status read. | Existing add-on state avoids login, refresh, verification, enrollment, and ANZ session-reclaim side effects; omitting user-profile retrieval minimizes personal data handled by the proof. |
| D-014 | 2026-08-24 | Treat the cloud `vin` field as an opaque bounded vehicle identifier and require canonical percent encoding. | GWM distinguishes its internal encoded identifier from the displayed VIN; strict round-trip encoding preserves compatibility without permitting query injection. |
| D-015 | 2026-08-24 | Expose only typed discovery, last-status, and vehicle-basics reads from the Task 4 client. | A closed operation registry hardens the POC without creating an arbitrary method/path escape hatch or prematurely enabling session and vehicle mutations. |
| D-016 | 2026-08-24 | Use immutable per-request session snapshots and independent immutable regional protocol policies. | Token or TLS-identity replacement can affect future requests without mutating an in-flight request, while EU, ANZ, and Russia retain separate origin, signing, header, query, decoding, device-ID, country, and TLS contracts. |
| D-017 | 2026-08-24 | Use `aiohttp` with a single monotonic deadline, bounded streaming, and redirects, environment proxies, cookies, content decoding, and retries disabled. | The integration needs native async I/O and reliable cancellation without leaking signed URLs or allowing hidden network behavior; compressed responses remain rejected until bounded decompression is deliberately implemented. |
| D-018 | 2026-08-24 | Make strict typing and a dependency-minimal client suite explicit CI gates. | Directly testing against `aiohttp`, `cryptography`, and pytest without Home Assistant proves the protocol package remains independently usable; strict mypy checking catches boundary drift before HA wiring begins. |
| D-019 | 2026-08-24 | Model EU login, verification, refresh, and certificate enrollment as a finite immutable continuation that publishes a read session only after complete validation. | Task 12 can drive native config and reauthentication flows without persisting partial tokens, identities, passwords, or verification codes, while Task 14 remains the sole owner of durable state writes. |
| D-020 | 2026-08-24 | Classify EU authentication failures conservatively from closed evidence-backed conditions. | HTTP 401/403 retires rejected state, 429 remains rate limiting, other non-2xx responses remain HTTP failures, exact known verification challenges drive continuation, and unknown application codes propagate instead of causing login or code-request side effects. |
| D-021 | 2026-08-24 | Validate required bootstrap material before fresh-auth network traffic and renew stored identities only for identity-specific failures. | Known local CA/bootstrap errors must fail before login or verification side effects; malformed or expiring issued identities can be replaced without treating local TLS configuration failures as a reason to enroll another certificate. |
| D-022 | 2026-08-25 | Require an explicit one-shot caller opt-in before every ANZ password login and retain that requirement through verification continuation. | ANZ permits only one active session; even a first or fallback login can supersede the official app or add-on. Validation and safe refresh may proceed without consent, while detected `607501` conflicts retire only the rejected revision and never trigger an automatic login loop. |
| D-023 | 2026-08-25 | Keep ANZ application-code side effects exact, endpoint-scoped, and conservative. | Only exact raw `309702`/`110641` challenges, historical captured-R&D `308011` code rejection, `607501` session conflict, and basics-only `607099` have special meanings. Unknown, whitespace-mutated, transient, HTTP, schema, and transport failures cannot trigger login, code delivery, reclaim, or optional-endpoint fallback. |
| D-024 | 2026-08-25 | Expose ANZ basics `607099` as `GwmOptionalEndpointError` at the raw client boundary. | The Task 13 coordinator can reproduce the add-on polling service's empty-basics fallback without hiding the regional protocol outcome from direct client callers; every other region, operation, and code still propagates. |
| D-025 | 2026-08-27 | Synchronize the full released `main` series in its own merge checkpoint before more protocol work. | The feature branch is already published, and China’s initial implementation plus its Alpine, transport, gateway, mapping, account-reset, and command fixes form a dependent release series that should not be partially cherry-picked or rebased away. |
| D-026 | 2026-08-27 | Implement China as an isolated multi-service strategy sharing only safe lifecycle, model, and error boundaries with the overseas client. | G-App, BeanTech, and AutoAI use different sessions, signing/encryption, envelopes, routes, time semantics, and command-result flow; forcing them into the current overseas `RegionProtocol`/single-token shape would couple unrelated behavior. |
| D-027 | 2026-08-27 | Retire China transport risk before production SMS authentication. | The existing Python transport rejects compressed responses and does not provide HTTP/2, while the observed China app profile advertises gzip and prefers HTTP/2. A bounded read-only POC can decide the transport without requesting codes or mutating an account or vehicle. |
| D-028 | 2026-08-27 | Bind all durable authentication and ownership state to the complete account context and clear it atomically when that context changes. | Main now prevents tokens, certificates, partial China sessions, verification throttles, charging ownership, or command outcomes from crossing region, country, account, or password changes; the direct integration must preserve that safety behavior without persisting a reversible credential fingerprint. |
| D-029 | 2026-08-27 | Preserve evidence labels for China capabilities instead of treating fixture parity as live validation. | Discovery, VV6 status, cooling, lock/unlock, and close-window behavior have initial live evidence; heating, extended controls, charging, other models/platforms, and broader response encodings remain experimental and require distinct approvals and results. |

## Post-Branch Main Drift Review

Review captured on 2026-08-27 without merging or rebasing. The merge base remains `1184737`; local `main` is `9daff32` (`v0.12.0`) and this branch is `ec80c4b` before this ledger-only commit.

Main contains twelve commits after the branch point. Two are patch-equivalent to changes already carried here: `cd8ffa7` corresponds to `cba0873` (regional reverse-engineering guide), and `10d6761` corresponds to `030199e` (new-region issue chooser). The ten unreconciled commits are:

- `d179610` — initial experimental mainland-China region, authentication, reads, basic controls, charging, fixtures, configuration, and documentation.
- `ee73524` — idempotent GitHub release creation/update behavior.
- `2d7e324` — Alpine-safe UTC+08:00 timestamps, one-time SMS-code handling, and preservation of rotated partial China tokens.
- `a41857d` — Ko-fi documentation expansion.
- `3699037` — app-like China User-Agent/HTTP-version preference and privacy-safe failure diagnostics.
- `4d39bff` — gzip-only response handling, exact JSON content headers/length, and expanded credential-safe gateway diagnostics.
- `a0e1977` — live-corrected vehicle discovery through the G-App gateway.
- `c45d0ff` — live-verified WEY VV6 SOC, fuel/range, and network-type-2 lock mappings.
- `f2159d0` — cross-region account-context binding and atomic authentication/ownership-state reset.
- `9daff32` — China heating, in-place HVAC parameter changes, eleven China-only HA buttons, and the engine, horn/light, tailgate, and sunroof command matrix.

Technical conclusions from the review:

- Main’s China cloud implementation remains inside the add-on. The existing HA integration still uses the local proxy and has no native China config/authentication flow.
- China spans G-App account/discovery, BeanTech session/result polling, and AutoAI/NavInfo status/commands. It uses ordinary verified TLS but separate crypto, tokens, envelopes, header profiles, and fixed-China-time semantics.
- The current Python client’s single overseas session and `RegionProtocol` abstraction cannot represent China safely without a separate strategy. Its transport also deliberately rejects compression and currently offers no HTTP/2 path, so transport feasibility must be proved before production SMS authentication.
- Discovery and status currently fail closed for any China vehicle whose `belongPlatform` is not `navinfo`. That limitation must remain explicit in the direct client and UI.
- Initial live evidence exists for SMS-backed access, NavInfo discovery, VV6 status, cooling, lock/unlock, and closing windows. Heating, extended controls, charging writes, other models/platforms, and broader status encodings remain experimental.
- A dry-run merge against `9daff32` predicts no textual conflicts. `README.md`, `CONTRIBUTING.md`, the issue chooser, and `custom_components/gwm_ora/translations/en.json` still require semantic review so both the released China material and the integration-only development guidance survive the auto-merge.

Task 7 will synchronize this released series as a whole and re-run both language baselines. No part of the series was merged, cherry-picked, or ported during this review.

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

## Task 4 Async Client Foundation Evidence

Evidence captured on 2026-08-24 from `feature/integration-only`. All Task 4 execution was offline: no GWM endpoint, account state, Home Assistant instance, add-on, vehicle, or command was contacted or changed. The disposable Task 3 runner remains isolated and unchanged.

Delivered foundation:

- Added a lifecycle-managed `GwmClient` with typed `acquire_vehicles`, `get_last_status`, and `get_vehicle_basics` methods. It requires an existing immutable authenticated-session snapshot; no login, refresh, verification, enrollment, session reclaim, persistence, or HA wiring exists yet.
- Added separately testable immutable EU, ANZ, and Russia policies for the known H5, auth, app, and certificate origins, regional signers, static/dynamic headers, country and device-ID rules, TLS roles, query canonicalization, and scalar-tolerance boundaries.
- Added redaction-safe cloud vehicle, status-item, status, and basics DTOs without the now-renumbered Task 11 normalized-snapshot mapping. Discovery parsing deliberately discards unrelated license, engine, ICCID, location, and other unknown fields; status values are recursively frozen for later mapping.
- Added a dedicated `aiohttp` transport with exact encoded URLs, an injected policy-validated `SSLContext`, no redirects/proxies/cookies/retries/automatic decompression, bounded streaming, response cleanup, owned-versus-external lifecycle, and no background tasks. Identity provisioning remains part of the regional authentication checkpoints.
- Applied one absolute event-loop deadline across lock acquisition, transport, response streaming, envelope parsing, and typed decoding. Caller cancellation propagates unchanged; overlapping account requests are serialized.
- Added fixed-message configuration, route, lifecycle, transport, HTTP, authentication, rate-limit, API, protocol, and schema exceptions that retain no URL, headers, body, cloud description, identifier, token, TLS material, or underlying aiohttp exception.
- Added strict UTF-8 JSON and envelope handling that rejects redirects, oversized/compressed responses, duplicate keys, `NaN`/infinity, excessive depth, numeric-zero success codes, missing data, and region-invalid typed payloads.
- Added versioned, explicitly synthetic request/region fixtures and offline coverage for all three regions and all three read operations, including opaque encoded identifiers and Russia's integer-to-string precision boundary.
- Added explicit `aiohttp` and mypy CI dependencies, a dependency-minimal client job, recursive HA-import boundary checks, and strict mypy checking for the reusable package while excluding only the disposable Task 3 runner.
- Made no released add-on or integration runtime-path change and performed no network I/O.

Validation:

```text
# Dependency-minimal HA-independent client suite
python -m pytest tests/python/client
321 passed

python -m mypy gwm_ora_client
Success: no issues found in 11 source files

python -m ruff check gwm_ora_client custom_components tests/python
All checks passed!

python -m compileall -q gwm_ora_client custom_components tests/python
# no output; exit 0

python -m pytest tests/python
358 passed, 1 warning

dotnet test --no-restore --configuration Release
128 passed, 0 failed, 0 skipped
```

The dependency-minimal Ruff, mypy, compile, and 321-test client gate also passed under WSL/Linux with Python 3.13. The Python warning and .NET nullable-annotation warnings are the unchanged baseline warnings. The async overseas read surface is production-structured; Tasks 5 and 6 have since added EU/ANZ authentication, while Tasks 9 and 10 must add China and Russia authentication/session behavior and expand sanitized regional response parity before Home Assistant can use every region directly.

## Task 5 EU Authentication and Read-Parity Evidence

Evidence captured on 2026-08-24 from `feature/integration-only`. Task 5 was entirely offline: no GWM endpoint, account, Home Assistant instance, add-on, vehicle, or command was contacted or changed. In particular, login, verification-code delivery, token refresh, and certificate enrollment were not exercised against the live service.

Delivered:

- Added the six closed EU authentication operations for password login, verification-code request and check, token refresh, user-profile validation, and certificate enrollment. Their origins, versions, methods, signing profiles, headers, TLS roles, exact property order, and exact UTF-8 request bytes are covered by a versioned synthetic fixture.
- Added a bounded encoder matching the current .NET `System.Text.Json` default escaping contract and changed the private transport boundary to send pre-serialized POST bytes without implicit JSON re-encoding. GET requests remain bodyless and neither transport path enables retries or redirects.
- Added immutable, redaction-safe EU credentials, continuation state, authenticated result, and verification-required result types. State is bound to a pseudonymous account digest, country, and stable normalized device ID; passwords and submitted verification codes are never retained.
- Implemented conservative access validation, refresh rotation, fresh login, verification throttling, and atomic session publication. A refreshed token is not published before profile validation, definitive token or identity rejection retires the old session, and transient certificate renewal failure may preserve a still-accepted session.
- Added strict issued and bootstrap identity handling: bounded canonical DER/key decoding, RSA/key/issuer/validity/extension checks, leaf-signature verification against the direct intermediate, final-day bootstrap usability, context-local legacy TLS, protected temporary identity files, and cleanup before cancellation completes.
- Preflighted required bootstrap and CA material before fresh-auth traffic, retained one monotonic deadline across blocking crypto and every network stage, serialized authentication and reads on the existing client lock, and checked the deadline after synchronous envelope decoding. Secret-file cleanup deliberately completes before cancellation is re-raised.
- Added a versioned sanitized EU discovery/status/basics response fixture and an end-to-end offline test that authenticates, publishes the new token and issued context, and performs all three typed reads while discarding unrelated account and vehicle data.
- Kept persistence, Home Assistant config/reauth flows, normalized snapshot mapping, ANZ/Russia authentication, commands, charging, packaging, and migration deferred to their planned checkpoints. No existing add-on or integration runtime path was changed.

Validation:

```text
# Dependency-minimal HA-independent client suite
python -m pytest tests/python/client
421 passed

# WSL/Linux Task 5 auth/identity/boundary/client/transport matrix
python -m pytest \
  tests/python/client/test_eu_auth.py \
  tests/python/client/test_eu_identity.py \
  tests/python/client/test_boundaries.py \
  tests/python/client/test_client.py \
  tests/python/client/test_transport.py
188 passed

python -m mypy gwm_ora_client
Success: no issues found in 14 source files

python -m ruff check gwm_ora_client custom_components tests/python
All checks passed!

python -m compileall -q gwm_ora_client custom_components tests/python
# no output; exit 0

python -m pytest tests/python
458 passed, 1 warning

dotnet test --no-restore --configuration Release
128 passed, 0 failed, 0 skipped
```

The Python warning and .NET nullable-annotation warnings are the unchanged baseline warnings. Linux-specific protected-file creation and cleanup are also covered. Live EU read transport remains proven by Task 3, while Task 5's undocumented login, token-rejection, verification, refresh, and enrollment response semantics remain explicitly offline-unverified.

## Task 6 ANZ Authentication and Read-Parity Evidence

Evidence captured on 2026-08-25 from `feature/integration-only`. Task 6 was entirely offline: no GWM endpoint, account, credential, token, Home Assistant instance, add-on, phone-app session, vehicle, or command was contacted or changed. In particular, no ANZ password login, verification-code delivery, refresh, session reclaim, or vehicle read was attempted live.

Delivered:

- Added the five closed ANZ authentication endpoints for password login, verification-code request and check, token refresh, and session/profile validation. Every operation is restricted to the ANZ H5 v1 origin, ordinary verified TLS, ANZ `bt-auth`, the regional header profile, exact 16-character API device identity, and versioned synthetic request bytes with the legacy null placeholders and property order.
- Added immutable, redaction-safe ANZ credentials, account-bound continuation state, authenticated, verification-required, and session-reclaim-required results. State retains only the normalized account binding, country, device ID, token pair, verification throttle timestamp, and a non-secret single-session consent marker; profile/login PII is discarded.
- Required explicit one-shot consent before every fresh ANZ password login. Existing-token validation and successful refresh remain non-disruptive, but state-less setup, account changes, rejected-token fallback, and exact `607501` conflicts return a typed continuation before login. The marker survives verification, and an immediate post-login `607501` returns without another login, preventing add-on/phone/Home Assistant eviction loops.
- Preserved the ANZ refresh wire quirk that sends the old access token in both the request header and body. Rotated tokens remain unpublished until a follow-up profile probe accepts them, and revision guards prevent an older attempt from erasing or overwriting a concurrently replaced session.
- Classified only exact raw evidence-backed application codes: `309702` and offline-unverified `110641` request verification, historical contributor R&D identifies `308011` as a wrong/expired code, and `607501` denotes a single-session conflict only on token-gated operations. Unknown and whitespace-mutated codes, `302000`, `607099` outside optional basics, `607124`, rate limits, HTTP failures, and malformed/transport failures produce no authentication side effect.
- Added a versioned sanitized ANZ discovery/status/basics fixture, including ANZ numeric-string tolerance with strict string fields, exact ANZ query canonicalization, ordinary TLS, and PII discard. Exact ANZ basics `607099` becomes `GwmOptionalEndpointError`; exact ANZ read `607501` retires only the matching read session and never logs in.
- Kept the polling-layer empty-basics fallback, persistence, Home Assistant flows/coordinator/entities, normalized snapshot mapping, Russia authentication, commands, charging, packaging, and migration deferred to their planned checkpoints. No existing add-on or integration runtime path changed.

Validation:

```text
# Dependency-minimal HA-independent client suite (Windows, CPython 3.13.13)
python -m pytest tests/python/client
487 passed

# Linux/WSL CI-equivalent dependency-minimal client suite (CPython 3.13.13)
python -m pytest tests/python/client
487 passed

python -m mypy gwm_ora_client
Success: no issues found in 15 source files

python -m ruff check gwm_ora_client custom_components tests/python
All checks passed!

python -m compileall -q gwm_ora_client custom_components tests/python
# no output; exit 0

python -m pytest tests/python
524 passed, 1 warning

dotnet test --no-restore --configuration Release
128 passed, 0 failed, 0 skipped
```

Ruff, mypy, compile, and the 487-test client gate also passed under WSL/Linux. The Python warning and .NET nullable-annotation warnings are the unchanged baseline warnings. Current live ANZ authentication, exact token-expiry codes, `110641`, verification delivery/expiry, `607099`, AU-versus-NZ response differences, and concurrent phone-session effects remain deliberately unverified; the implementation fails closed around those gaps.

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

### Task 4 — Async typed client foundation

Status: complete on 2026-08-24.

Delivered:

- Replaced the disposable POC's architectural role with separate production modules for configuration, errors, immutable cloud DTOs, regional policy, private wire contracts, async transport, and the typed read client; retained the POC itself as Task 3 evidence.
- Defined the full known regional origin/signing/TLS matrix while exposing only the three read operations and an immutable existing-session snapshot.
- Added strict route, header, TLS, response, error-redaction, timeout, cancellation, non-overlap, lifecycle, and scalar-decoding boundaries while preserving finite raw cloud values for the now-renumbered Task 11 availability normalization.
- Added versioned synthetic fixtures and expanded the dependency-minimal suite to 321 tests (217 added after Task 3), with strict package type checking and dedicated CI coverage.
- Kept authentication, persistence, normalized snapshot mapping, Home Assistant wiring, commands, charging, packaging, and migration deferred to their planned checkpoints.
- Made no live request and no add-on or integration runtime-path change.

### Task 5 — EU production authentication and read parity

Status: complete on 2026-08-24.

Delivered:

- Implemented the closed EU password-login, verification continuation, refresh, profile-validation, and certificate enrollment/renewal state machine with exact signed wire bytes and scoped TLS roles.
- Added immutable account-bound continuation state and atomic, revision-guarded future-session publication without adding persistence or Home Assistant coupling.
- Added strict bootstrap/issued identity validation, renewal and invalidation boundaries, protected temporary-file handling, and cleanup-aware cancellation.
- Added versioned synthetic auth contracts and EU discovery/status/basics responses, including one authentication-to-all-reads parity test.
- Preserved conservative error classification: no undocumented application code triggers token fallback, verification delivery, or credential rejection without evidence.
- Made no live request and no add-on or integration runtime-path change.

### Task 6 — ANZ production authentication and read parity

Status: complete on 2026-08-25.

Delivered:

- Implemented exact ANZ password-login, verification, refresh-with-old-token-header, profile-validation, and default-TLS session publication contracts.
- Added an immutable account-bound continuation with explicit consent before every password login that could claim ANZ's single active session; exact `607501` retires matching state without automatic reclaim or loops.
- Added exact-code-only verification/rejection policy, post-login and post-refresh validation, revision-safe session retirement/publication, and secret-safe failure handling.
- Added versioned synthetic ANZ auth and discovery/status/basics fixtures plus regional response, scalar-tolerance, optional-basics, read-conflict, deadline, concurrency, mutation, and redaction tests.
- Kept `607099` as a typed raw-client optional-endpoint outcome; the future coordinator remains responsible for the add-on polling path's empty-basics fallback.
- Made no live request and no add-on or integration runtime-path change.

### Next checkpoint (requires explicit approval)

Task 7 will merge the complete local `main` release series through `9daff32` (`v0.12.0`) into `feature/integration-only`, semantically verify the auto-merged documentation/translations, and re-run the full Python and .NET baselines. It will not port China into `gwm_ora_client`, alter the direct-cloud architecture, perform network I/O, publish or push anything, or begin Task 8. If `main` advances again before approval, its new drift must be reviewed before the Task 7 merge.

## Open Risks and Questions

- Cross-architecture confirmation of the scoped GWM SSL context; Linux x86-64/OpenSSL 3.5.6 is proven offline, while supported ARM architectures remain untested.
- The bundled EU general bootstrap certificate expires on 2027-01-04 and needs a renewal/provenance plan before production cutover.
- Modern `cryptography` rejects invalid PrintableString characters in the legacy OEM CA subjects; the POC validates their envelopes and lets OpenSSL consume the original signed bytes instead.
- EU authentication is implemented and exhaustively fixture-tested offline, but its undocumented application-level token-expiry and wrong-verification-code values remain unverified. Until sanitized evidence establishes those codes, only HTTP 401/403 retires token state and unknown application errors propagate without fallback side effects.
- Exact live parity of undocumented authentication and response behavior in ANZ and Russia remains unverified; EU read transport is proven live, while Task 5 EU auth and Task 6 ANZ auth/read semantics were deliberately not exercised live.
- The current Python `aiohttp` transport rejects compressed responses and has no HTTP/2 client support. Task 8 must prove whether bounded China-only gzip over HTTP/1.1 is accepted by the corrected gateway or whether an isolated HTTP/2-capable dependency is required without weakening existing transport boundaries.
- China authentication crosses three services. Unknown G-App, BeanTech, or AutoAI application codes must not be treated as token rejection, trigger SMS delivery/login, or discard a still-recoverable partial session without sanitized evidence. Risk-control `1013` remains an explicit stop that directs the user to the official app.
- Main retries BeanTech and AutoAI initialization three times. The Python port must decide from fixtures/evidence whether narrowly bounded retries of those idempotent initialization calls are safe under one deadline; it must not introduce general retries for SMS delivery, SMS login, reads, or commands.
- China live evidence is currently limited to a contributed NavInfo WEY VV6 and selected reads/controls. Gate A-CN needs suitable sanitized access before direct China setup can be exposed, while heating, extended controls, charging, other platforms/models, and broader response encodings need their own later evidence.
- The cloud DTOs intentionally retain only the fields required to establish the overseas protocol boundary. EU and ANZ now have sanitized regional response fixtures; China production fixtures, Russia response parity, and Task 11's complete four-region normalized-snapshot mapping remain outstanding.
- ANZ's exact basics `607099` response is now a typed raw-client optional-endpoint failure. The Task 13 coordinator must deliberately map it to empty basics to preserve the add-on polling service's nonfatal behavior.
- Whether Task 12 should use one dedicated cookie-free client session per config entry or a policy-validated HA-owned session while preserving scoped TLS and unload ownership.
- Availability of safe test accounts/vehicles for every regional read and write matrix.
- ANZ side-by-side session effects remain untested. Task 6 prevents every password login without explicit one-shot consent and prevents automatic `607501` reclaim loops, but Task 12 must explain that consent clearly and the project still recommends a dedicated shared vehicle account.
- ANZ `110641`, current token-expiry/rotation behavior, verification delivery/expiry, AU-versus-NZ differences, and unknown `checkSMSCode` failures lack sanitized current-service evidence; unknown errors stop without attempting the final login.
- Safe handling and future renewal of bundled bootstrap certificates and OEM-derived key material.
- Licensing/provenance of code, certificates, China app-derived signing material, and other resources derived from reverse-engineering work.
- Whether the final client is bundled for HACS or published as a separately versioned Python dependency.
- Whether existing users perform one fresh authentication or use a temporary secured state-export path.
- Blocking certificate/key workers finish protected temporary-file cleanup before propagating cancellation, so a cancelled authentication may return after its nominal deadline even though no network stage may continue past that deadline.
- Durable reconciliation semantics for a command accepted immediately before HA reload, shutdown, or loss of connectivity.
