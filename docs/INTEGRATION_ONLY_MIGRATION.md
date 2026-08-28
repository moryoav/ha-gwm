# Integration-Only Migration Ledger

This document is the durable plan, behavior contract, decision log, and test ledger for replacing the companion .NET add-on with a standalone Home Assistant integration.

## Status

- Working branch: `feature/integration-only`
- Branch point: `1184737` (`Update README GWM logo to SVG`)
- Current checkpoint: Task 10 complete; Russia production authentication/read parity is proven offline, while live Russia behavior remains deliberately unverified
- Next checkpoint: Task 11 — four-region normalized snapshot/model mapping (not yet approved)
- Synchronized local `main`: `9daff32` (`v0.12.0`) through a two-parent merge without rebasing or selective cherry-picks
- Task 10 completed offline authentication and three-read parity for every supported region without changing Home Assistant, the add-on/proxy runtime, persistence, commands, or charging

Work proceeds one explicitly approved task at a time. At the end of every task, update this document, run the checks appropriate to that checkpoint, create one focused commit, push it to `feature/integration-only`, report the result, and stop. Do not begin the next task without a new user green light.

## Working Agreement

- Keep the existing add-on path functional until the final cutover.
- Prefer targeted tests at ordinary checkpoints and full suites at the major gates below.
- Never commit account credentials, verification codes, tokens, certificates issued to a user, private keys, VINs, locations, or unsanitized cloud responses.
- Live read-only tests require explicit approval for the corresponding task.
- Live climate, lock, unlock, window, and charging-plan operations require an additional explicit confirmation immediately before testing them.
- Push each completed task's focused commit to `feature/integration-only`. Do not publish packages, open a pull request, merge, or release without separate approval.
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

### Gate A-CN — China transport and production-read feasibility (offline portions passed 2026-08-28; live activation prerequisite pending)

After Task 8, Python must reproduce all three China crypto/signing families and complete a bounded end-to-end synthetic-service discovery/status round trip using the selected transport. Before `cn` can be enabled in a direct Home Assistant flow or the add-on can be retired for a China user, a sanitized live read-only validation must also pass using either an existing session or a separately approved SMS-login procedure. Lack of suitable China access does not block work for the other regions, but it does block claiming China cutover readiness.

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
- [x] Task 7 — Merge the reviewed released `main` series into this branch and re-establish the full baseline.
- [x] Task 8 — Prove the isolated China crypto, transport, and reuse-only read path.
- [x] Task 9 — Implement China production SMS authentication, multi-service sessions, and read parity.
- [x] Task 10 — Implement Russia production authentication and read parity.
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

- Task 7 was synchronization only. It merged the complete released mainline series, semantically reviewed auto-merged documentation/translations, re-established the full Python and .NET baselines, and made no direct-cloud Python behavior change.
- Task 8 is the early China stop/go POC. Port deterministic crypto/time vectors, exact app-like request bytes, 32-character device identity, bounded gzip handling, and the three-service transport boundary; prove only discovery and status with synthetic services and, if separately approved and available, a reused live session. Do not request or submit an SMS code.
- Task 9 turns that proof into immutable production authentication/read behavior: SMS continuation and throttling, exact error/risk-control handling, G-App refresh, bounded BeanTech/AutoAI initialization, partial-session publication, corrected discovery routing, NavInfo-only enforcement, sanitized China fixtures, and typed reads. It remains HA-independent, non-persistent, and command-free.
- Task 10 retained the previously planned Russia authentication/read checkpoint. Moving it after the China feasibility work prevented the overseas-client shape from hiding a transport or strategy blocker introduced by the newly released region.
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
| D-030 | 2026-08-28 | Keep the Task 8 China proof reuse-only, read-only, and separate from the production overseas client. | Discovery needs existing G-App and BeanTech state while status needs existing AutoAI state; a dedicated minimal snapshot and closed two-read client prove that boundary without adding China to `Region`, importing durable state, requesting SMS, refreshing tokens, or exposing mutations. |
| D-031 | 2026-08-28 | Select an isolated bounded-gzip `aiohttp` adapter for the offline China proof without claiming HTTP/2 or live compatibility. | The C# app profile prefers HTTP/2 but permits a lower version. Python's HTTP/1.1 adapter proves exact bytes, ordinary verified TLS, ambient-state isolation, and bounded synthetic gzip; only a separately approved live read can establish whether the service accepts that fallback or an HTTP/2-capable dependency is required. |
| D-032 | 2026-08-28 | Treat BeanTech as a distinct modeled/signing boundary, but defer BeanTech HTTP traffic to production China authentication. | Reused-session discovery transmits the BeanTech access token to G-App and its signing algorithm has a golden vector; discovery/status themselves call only G-App and direct AutoAI, so inventing a BeanTech POC request would exceed the two-read feasibility scope. |
| D-033 | 2026-08-28 | Implement production China access as a separate public `ChinaClient`, not as another overseas `RegionProtocol`. | G-App, BeanTech, and AutoAI have a multi-service session, gzip transport, routes, crypto, continuations, and status translation that cannot fit the overseas single-token client without weakening regional isolation. Task 12 can select the strategy without making Task 9 depend on Home Assistant or persistence. |
| D-034 | 2026-08-28 | Treat only HTTP 401/403 as evidence that China authentication was rejected; preserve state for unknown application failures. | No sanitized evidence identifies G-App, BeanTech, or AutoAI token-expiry application codes. Code `1013` is instead an explicit risk-control stop, `429` remains rate limiting, and unknown codes must neither retire state nor trigger refresh, SMS delivery, login, or retries. |
| D-035 | 2026-08-28 | Publish a valid G-App-only partial revision after downstream initialization fails, and install a read session only after both platform logins and forced discovery succeed. | Rotated G-App tokens must not be lost or mixed with stale or one-sided BeanTech/AutoAI state. Only the two idempotent platform initializers may retry, limited to network and HTTP 502/503/504 failures, three attempts under the same deadline; all other operations remain single-attempt. |
| D-036 | 2026-08-28 | Push every completed checkpoint commit to `feature/integration-only` before stopping. | The user granted standing approval so each weekly checkpoint is backed up and visible without granting permission to publish, merge, or release. |
| D-037 | 2026-08-28 | Implement Russia as its own authentication and static-identity strategy inside the existing overseas `GwmClient`. | Russia shares the overseas single-token/read model and routes, but its login/SMS payloads and fixed regional mTLS identity are distinct from EU enrollment and ANZ session reclaim. A separate strategy preserves those boundaries without inventing a fourth client shape. |
| D-038 | 2026-08-28 | Classify Russia authentication side effects only from endpoint-scoped evidence. | Exact raw `110641` requests verification only after password login; submitted-code application errors remain unknown, only HTTP 401/403 proves code or token rejection, `429` remains rate limiting, and every other failure stops without refresh, login, SMS, retry, or state retirement. |
| D-039 | 2026-08-28 | Preflight and exact-bind the static Russia bootstrap identity before every authentication network side effect. | Russia app reads use the bundled general RU identity rather than per-user enrollment. Exact RU subject/issuer, key, chain, validity, and scoped legacy TLS validation prevents swapped EU material or local certificate failure from causing a login or SMS request. |

## Post-Branch Main Drift Review

Review captured on 2026-08-27 without merging or rebasing. The merge base was `1184737`; local `main` was `9daff32` (`v0.12.0`), the completed client tip was `ec80c4b`, and planning commit `5f74459` recorded the revised roadmap before synchronization.

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

Task 7 synchronized this released series as a whole on 2026-08-28 and re-ran both language baselines. No part of the series was merged, cherry-picked, or ported during the earlier review, and no China protocol code was ported into Python during synchronization.

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

The Python warning and .NET nullable-annotation warnings are the unchanged baseline warnings. Gate A is passed for direct EU vehicle reads. At this checkpoint ANZ and Russia were offline-parity-only; Tasks 6 and 10 have since completed their production-shaped offline authentication/read checkpoints without claiming live validation.

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

The dependency-minimal Ruff, mypy, compile, and 321-test client gate also passed under WSL/Linux with Python 3.13. The Python warning and .NET nullable-annotation warnings are the unchanged baseline warnings. Tasks 5, 6, 9, and 10 have since added production-shaped offline authentication/read behavior for every region; Task 11 must now complete the normalized four-region snapshot mapping before Home Assistant can use those clients directly.

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

## Task 7 Main Synchronization Evidence

Evidence captured on 2026-08-28 from `feature/integration-only`. Task 7 performed no GWM, Home Assistant, add-on, account, phone-app, vehicle, command, charging, or release network operation.

Delivered:

- Merged the complete local `main` release history through `9daff32` (`v0.12.0`) as a two-parent merge. No rebase, selective China cherry-pick, duplicate patch, or history rewrite was used.
- Brought the existing add-on/proxy path forward with the released China foundation and follow-up fixes for Alpine time handling, transport fidelity, corrected discovery routing, VV6 status mapping, account-context invalidation, heating, extended controls, and charging/reference behavior.
- Preserved main's idempotent release workflow and documentation updates while retaining the integration-only branch's standalone-client CI, strict typing, synthetic-fixture guidance, and synthetic VIN examples.
- Verified that all main-only source/configuration/test paths match `main`, all feature-only paths remain intact, the patch-equivalent regional-guide/issue-link changes are not duplicated, and no `gwm_ora_client` implementation or client fixture changed. Only the documented Task 7 semantic wording corrections differ from the auto-merged documentation/translation result.
- Semantically reviewed the auto-merged documentation and translations. Clarified that China is partially live-validated rather than wholly untested, and made the remote-command error describe a security PIN only where the selected region requires one.
- Kept Task 8 entirely deferred: no China crypto, transport, session, signing, read, authentication, persistence, or Home Assistant direct-cloud behavior was implemented in Python.

Validation:

```text
git diff --cached --check
# no output; exit 0

# Dependency-minimal HA-independent client suite (Windows, CPython 3.13.13)
python -m pytest tests/python/client
487 passed

python -m mypy gwm_ora_client
Success: no issues found in 15 source files

python -m ruff check gwm_ora_client custom_components tests/python
All checks passed!

python -m compileall -q gwm_ora_client custom_components tests/python
# no output; exit 0

python -m pytest tests/python
525 passed, 1 warning

dotnet test --configuration Release
149 passed, 0 failed, 0 skipped
```

The Python warning and .NET nullable-annotation warnings are the unchanged baseline warnings. Integration JSON and add-on YAML parsed successfully; configuration, manifest, and quality assertions agree on version `0.12.0`. The existing untracked analysis artifacts remained outside the merge.

## Task 8 China Feasibility Evidence

Evidence captured on 2026-08-28 from `feature/integration-only`. Task 8 performed no GWM, Home Assistant, add-on, account, SMS, phone-app, vehicle, command, charging, or release operation and did not inspect or import live session state.

Delivered:

- Ported the G-App `G_A` AES-256-CBC/OpenSSL envelope, G-App default SHA-256 signing, BeanTech SHA-256/Java-URL signing, AutoAI HMAC-SHA1 signing, MD5/SHA helpers, and fixed UTC+08:00 protocol time into a deterministic HA-independent module. The authoritative C# golden vectors, both G-App key IDs, fixed-salt OpenSSL ciphertext, padding boundaries, malformed wrappers, invalid UTF-8, canonicalization edges, and secret-safe exceptions are covered offline.
- Added an immutable minimal reused-read snapshot containing only the 32-character normalized device identity and the five existing G-App/BeanTech/AutoAI values actually transmitted by discovery/status. It has no phone, refresh/SSO token, loader, persistence, login, SMS, refresh, command, or charging surface.
- Added a closed `ChinaPocClient` with exactly G-App vehicle discovery and direct AutoAI/NavInfo status. It emits the exact compact discovery body, System.Text.Json-compatible AutoAI wrapper and percent-encoded target, fixed app headers and times, enforces prior discovery plus case-insensitive NavInfo membership, and retains only privacy-minimized typed POC results.
- Added a separate China-only `aiohttp` adapter using ordinary verified TLS 1.2+, exact G-App and AutoAI route/method/header validation, no redirects, ambient proxies, cookies, default authentication, hidden headers, retries, or implicit decompression, and independent compressed-wire/decompressed-body ceilings. It accepts one valid gzip member and rejects unsupported/multiple encodings, duplicate security-relevant headers, truncated/corrupt/trailing/concatenated streams, decompression bombs, lying lengths, and unsafe external-session state.
- Modeled all three protocol services without inventing a third read call: the synthetic round trip sends G-App discovery then direct AutoAI status; BeanTech's distinct signing vector and use of its access token are proven, while BeanTech HTTP initialization remains Task 9 work.
- Added a versioned, fully synthetic request/response contract with static discovery signature, exact full AutoAI URL, fixed timestamps, two synthetic vehicles, and a non-empty status. The selected adapter completed the gzip-compressed synthetic discovery-to-status round trip end to end.
- Kept the POC outside `gwm_ora_client.__all__`, the production `GwmClient`/`Region` strategy, Home Assistant, persistence, and released add-on/proxy behavior. Failed re-discovery and AutoAI authentication revoke status eligibility; full logical operations share one deadline/lock and are covered for cancellation, close, concurrency, parser, error-category, and redaction behavior.
- Recorded the transport result conservatively: bounded China gzip over HTTP/1.1 is proven synthetically, but no live China request was approved. HTTP/2 preference, live gateway acceptance, and Gate A-CN cutover readiness remain explicitly unresolved.

Validation:

```text
# Task 8 plus cross-fixture boundary tests (Windows, CPython 3.13.13)
py -3.13 -m pytest -q tests/python/client/test_boundaries.py tests/python/client/test_china_crypto.py tests/python/client/test_china_transport.py tests/python/client/test_china_poc.py
174 passed

py -3.13 -m pytest tests/python/client
647 passed

py -3.13 -m mypy gwm_ora_client
Success: no issues found in 18 source files

py -3.13 -m ruff check gwm_ora_client custom_components tests/python
All checks passed!

py -3.13 -m compileall -q gwm_ora_client custom_components tests/python
# no output; exit 0

py -3.13 -m pytest tests/python
685 passed, 1 warning

dotnet test --configuration Release
149 passed, 0 failed, 0 skipped

# Dependency-minimal WSL/Linux, CPython 3.13.13
python -m pytest tests/python/client
647 passed

python -m mypy gwm_ora_client
Success: no issues found in 18 source files

ruff check gwm_ora_client tests/python/client
All checks passed!
```

The Python warning and .NET nullable-annotation warnings remain the unchanged baseline warnings. The live portion of Gate A-CN is deliberately not passed: a separately approved sanitized read must still establish service acceptance and the HTTP-version decision before direct China setup or China-inclusive add-on retirement.

## Task 9 China Production Authentication and Read Evidence

Evidence captured on 2026-08-28 from `feature/integration-only`. Task 9 used only versioned synthetic contracts and local fake transports. It made no live SMS, authentication, refresh, discovery, status, Home Assistant, add-on, account, phone-app, vehicle, command, charging, publish, merge, or release request and used no live credential or session state.

Delivered:

- Added the public HA-independent `ChinaClient` and immutable, secret-safe account/device-bound credentials, complete or G-App-only state, and finite authentication/verification/initialization/risk-control outcomes.
- Implemented exact G-App SMS request/login/refresh, concurrent BeanTech and AutoAI initialization, corrected G-App discovery, and direct AutoAI status through a closed seven-operation adapter. Exact routes, canonical bodies, encrypted envelopes, headers, signatures, UTC+08:00 timestamps, gzip limits, and service-specific tokens are fixed-contract tested.
- Preserved the prior BeanTech token solely for signing/sending a refresh request, then discarded all downstream state before publishing rotated G-App tokens. Canonical and C# alias refresh response names are covered, and no one-sided downstream state can be constructed or published.
- Added ten-minute SMS request throttling, per-account in-memory one-shot code submission, definite HTTP 401/403 rejected-code continuation, exact `1013` risk-control handling, and conservative unknown-code/`429` behavior without automatic SMS, refresh, retry, or state retirement.
- Limited retries to BeanTech/AutoAI initialization network failures and HTTP 502/503/504, three attempts with one-second delays under one deadline; sibling results are atomic and forced discovery must pass before a complete session is installed.
- Ported the full C# China status signal matrix into immutable shared cloud DTOs, required a recognized status shape, tolerated malformed optional tank metadata, retained no raw response, and enforced prior corrected-route discovery plus NavInfo-only status routing.
- Strengthened synthetic-fixture guards for encrypted/embedded phones, codes, tokens, device identifiers, vehicle identifiers, and coordinates; added timestamp correlation, signature recomputation, explicit eighth-operation rejection, malformed-schema classification, cancellation/session-preservation, and hostile transport coverage.
- Kept Home Assistant, persistence, commands, charging, packaging, released add-on/proxy behavior, and the overseas `GwmClient`/`Region` strategy unchanged.

Validation:

```text
# Task 9 production China client/status/transport/boundary tests (Windows, CPython 3.13.13)
py -3.13 -m pytest -q tests/python/client/test_china_client.py tests/python/client/test_china_status.py tests/python/client/test_china_transport.py tests/python/client/test_boundaries.py
222 passed

py -3.13 -m pytest -q tests/python/client
794 passed

py -3.13 -m pytest -q tests/python
832 passed, 1 warning

py -3.13 -m mypy gwm_ora_client
Success: no issues found in 20 source files

py -3.13 -m ruff check gwm_ora_client custom_components tests/python
All checks passed!

py -3.13 -m compileall -q gwm_ora_client custom_components tests/python
# no output; exit 0

dotnet test --configuration Release --nologo --verbosity quiet
149 passed, 0 failed, 0 skipped

# Dependency-minimal WSL/Linux, CPython 3.13
python -m pytest -q tests/python/client
794 passed

python -m mypy gwm_ora_client
Success: no issues found in 20 source files

ruff check gwm_ora_client tests/python/client
All checks passed!
```

The Python warning and .NET nullable-annotation warnings remain the unchanged baseline warnings. Gate A-CN remains deliberately incomplete: Task 9 proves production-shaped authentication and reads offline, but only a separately approved sanitized live procedure can establish gateway acceptance, account behavior, and whether the HTTP/1.1 fallback is sufficient before China can be exposed in a direct Home Assistant flow.

## Task 10 Russia Production Authentication and Read Evidence

Evidence captured on 2026-08-28 from `feature/integration-only`. Task 10 used only versioned synthetic contracts, local fake transports, and the already committed OEM bootstrap resources. It made no live login, verification-code delivery, token refresh, profile, vehicle read, Home Assistant, add-on, account, phone-app, command, charging, publish, merge, or release request and used no live credential or session state.

Delivered:

- Added immutable, secret-safe `RussiaCredentials`, `RussiaAuthState`, authenticated and verification continuation outcomes, and `GwmClient.authenticate_russia`. The finite continuation remains Home Assistant-independent and non-persistent, consumes a submitted code once without retaining it, and installs only a fully validated read session.
- Implemented the closed five-operation Russia H5 authentication surface: plaintext password login with `countryCode` omitted and agreements `[1,2,18,19]`, direct `loginWithSMS`, SMS-code delivery, token refresh without an `accessToken` header, and access-token profile validation. Exact .NET-compatible bodies, property order/types, routes, headers, full untruncated device identity, `gwm-auth` signing, and ordinary H5 TLS are fixed-contract tested.
- Validated stored access first, rotated a rejected token pair through a single refresh, forced profile validation before publishing refreshed or newly logged-in state, and fell back to password login only after definite HTTP 401/403 rejection. There are no retries or ANZ-style session-reclaim side effects.
- Limited the exact `110641` verification challenge to password login. Submitted-code application errors remain unknown; only HTTP 401/403 returns a rejected-code continuation. Unknown or whitespace-mutated codes, `429`, other HTTP failures, malformed responses, TLS/network failures, and cancellation cannot request another code, retry, or trigger an unproven fallback.
- Added Russia-specific static mTLS identity handling that exact-binds the bundled `LGWGWM-AD-RU-GENERAL` / `RU` leaf and General SubCA issuer before delegating to the hardened bounded key, RSA, chain, validity, protected-temporary-file, and scoped `SECLEVEL=0` mechanics. Swapped or malformed local material fails before any authentication or SMS network side effect.
- Completed an authentication-to-discovery/status/basics synthetic round trip through the existing overseas read client. Russia retains exact large integer identifiers as strings, rejects booleans/floats at string-or-number boundaries, preserves numeric-string status values, uses the static mTLS session for every APP request, retires only the matching session on HTTP 401/403, and preserves newer concurrent replacements.
- Added versioned fully synthetic Russia authentication and read fixtures with exact golden signatures and public identity digests only. Expanded cross-fixture guards for SMS-code aliases, closed numeric sentinels, raw certificate PEM/field aliases, and transformed/private-key aliases; no raw certificate, key, credential, token, VIN, location, or response capture was added.
- Kept Home Assistant flows/coordinator/entities, durable state, normalized snapshot mapping, commands, charging, packaging, migration, and the released add-on/proxy runtime unchanged.

Validation:

```text
# Task 10 Russia auth/identity/fixture/boundary/client matrix (Windows, CPython 3.13.13)
py -3.13 -m pytest -q tests/python/client/test_russia_auth.py tests/python/client/test_russia_identity.py tests/python/client/test_russia_fixtures.py tests/python/client/test_boundaries.py tests/python/client/test_client.py
198 passed

py -3.13 -m pytest -q tests/python/client
887 passed

py -3.13 -m pytest -q tests/python
925 passed, 1 warning

py -3.13 -m mypy gwm_ora_client
Success: no issues found in 22 source files

py -3.13 -m ruff check gwm_ora_client custom_components tests/python
All checks passed!

py -3.13 -m compileall -q gwm_ora_client custom_components tests/python
# no output; exit 0

dotnet test --configuration Release --nologo --verbosity quiet
149 passed, 0 failed, 0 skipped

# Dependency-minimal WSL/Linux, CPython 3.13.13
python -m pytest -q tests/python/client
887 passed

python -m mypy gwm_ora_client
Success: no issues found in 22 source files

ruff check gwm_ora_client tests/python/client
All checks passed!
```

The Python warning and .NET nullable-annotation warnings remain the unchanged baseline warnings. Russia authentication and reads are production-shaped and offline-parity complete, but undocumented live token expiry, wrong-code behavior, response variation, gateway acceptance, and supported ARM TLS behavior remain deliberately unclaimed until separately approved sanitized validation is available.

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

### Task 7 — Main synchronization and baseline re-establishment

Status: complete on 2026-08-28.

Delivered:

- Merged local `main` through `9daff32` (`v0.12.0`) as the complete released history and retained a genuine two-parent merge.
- Preserved both the released China add-on/proxy behavior and all Task 1–6 standalone-client code, tests, CI, documentation, and synthetic evidence.
- Semantically reviewed the auto-merged documentation/translations and corrected two region-sensitive descriptions.
- Re-established the unchanged 487-test client gate, expanded 525-test Python baseline, and expanded 149-test .NET baseline.
- Made no live request and no China direct-cloud Python behavior change.

### Task 8 — Isolated China crypto, transport, and reuse-only read proof

Status: complete on 2026-08-28; Gate A-CN's offline synthetic portion passed, live activation prerequisite pending.

Delivered:

- Ported all three China service crypto/signing families and fixed UTC+08:00 time with authoritative and independently fixed-salt golden vectors.
- Added a minimal immutable existing-session snapshot and an isolated two-operation discovery/status POC with exact app-like bytes, 32-character device identity, prior-discovery/NavInfo enforcement, typed privacy-minimized results, and no authentication or mutation APIs.
- Added a dedicated ordinary-TLS China adapter with a closed G-App/AutoAI route registry, ambient-state isolation, one monotonic logical-operation deadline, and independently bounded single-member gzip decompression.
- Proved a fully synthetic gzip G-App discovery to direct AutoAI status round trip, exact full request targets/headers/signatures, strict envelopes/parsers, lifecycle/concurrency, revocation, redaction, and hostile transport inputs across 155 new Task 8 tests plus five expanded fixture-boundary cases.
- Proved BeanTech's signing boundary without sending an unnecessary BeanTech request; its session initialization and HTTP routes remain deferred to Task 9.
- Kept `GwmClient`, `Region`, Home Assistant, add-on/proxy runtime behavior, durable state, SMS, refresh, commands, charging, live access, publishing, and pushing unchanged.
- Selected bounded HTTP/1.1 `aiohttp` for the offline proof only. No live China request was authorized, so HTTP/2 necessity and live compatibility remain unresolved Gate A-CN activation conditions.

### Task 9 — China production authentication, multi-service sessions, and read parity

Status: complete on 2026-08-28; offline production parity passed, Gate A-CN live activation prerequisite pending.

Delivered:

- Added a separate public, HA-independent `ChinaClient` with immutable phone/device-bound authentication state and finite authenticated, verification, initialization, and risk-control results.
- Implemented exact SMS delivery/login, G-App refresh, BeanTech SSO initialization, AutoAI proxy login, corrected G-App discovery, and direct AutoAI status as a closed seven-operation protocol surface.
- Added ten-minute SMS throttling and in-memory one-shot code consumption without retaining a submitted code; only HTTP 401/403 is treated as definite authentication rejection, while unknown application codes, `429`, and risk-control `1013` stop without hidden retries or side effects.
- Kept refreshed or newly logged-in G-App state separate from downstream services, initialized BeanTech and AutoAI atomically, and published a G-App-only partial continuation when downstream initialization or forced discovery failed.
- Limited retries to BeanTech/AutoAI initialization network failures and HTTP 502/503/504, at most three attempts with one-second delays under the original monotonic deadline. SMS, refresh, discovery, status, schema, TLS, redirect, risk, throttling, and unknown failures remain single-attempt.
- Required forced corrected-route discovery before installing a complete read session, retained only typed vehicle metadata, enforced discovered NavInfo membership, and ported the complete C# China read-status signal matrix into the shared immutable cloud-status shape without retaining raw responses.
- Added a versioned fully synthetic authentication/read contract, exact request bytes and signatures for all seven operations, hostile response/route/session/privacy tests, and stronger cross-fixture guards for account, code, token, device, vehicle, and coordinate material.
- Kept `GwmClient`, `Region`, Home Assistant flows/coordinator/entities, persistence, commands, charging, packaging, add-on/proxy behavior, and live China access unchanged.

### Task 10 — Russia production authentication and read parity

Status: complete on 2026-08-28; offline production parity passed, live behavior deliberately unverified.

Delivered:

- Added Russia-specific immutable credentials/state/results and a serialized `GwmClient.authenticate_russia` continuation without introducing Home Assistant or persistence dependencies.
- Implemented exact password login, direct SMS-code login, SMS delivery, headerless token refresh, and profile validation on the Russia H5 gateway with the existing `gwm-auth` signer and full device identity.
- Added conservative stored-token validation, rotation, verification throttling, profile-before-publication, cancellation/deadline behavior, and revision-safe session replacement with no retries or inferred application-code fallbacks.
- Exact-bound the static Russian general certificate/key/chain to a protected, scoped legacy mTLS context and preflighted it before all authentication or verification side effects.
- Completed fixture-tested Russia discovery, status, and basics parity, including exact large numeric identifiers, stringified status scalars, boolean rejection, opaque vehicle identifiers, and HTTP 401/403 session retirement.
- Added fully synthetic auth/read contracts and expanded privacy guards so raw certificate/key material, codes, credentials, tokens, VINs, locations, and response captures cannot enter fixtures unnoticed.
- Kept Home Assistant, normalized snapshot mapping, durable state, commands, charging, packaging, migration, add-on/proxy behavior, and live Russia access unchanged.

### Next checkpoint (requires explicit approval)

Task 11 will port and fixture-test the complete four-region normalized snapshot/model mapping without adding Home Assistant flows, persistence, commands, or charging behavior. Task 11 must not begin without a new user green light; any live cloud access still requires separate explicit approval.

## Open Risks and Questions

- Cross-architecture confirmation of the scoped GWM SSL context; Linux x86-64/OpenSSL 3.5.6 is proven offline, while supported ARM architectures remain untested.
- The bundled EU general bootstrap certificate expires on 2027-01-04 and needs a renewal/provenance plan before production cutover.
- The bundled Russia general bootstrap certificate expires on 2030-04-21 and likewise needs a renewal/provenance plan before that identity can expire in supported installations.
- Modern `cryptography` rejects invalid PrintableString characters in the legacy OEM CA subjects; the POC validates their envelopes and lets OpenSSL consume the original signed bytes instead.
- EU authentication is implemented and exhaustively fixture-tested offline, but its undocumented application-level token-expiry and wrong-verification-code values remain unverified. Until sanitized evidence establishes those codes, only HTTP 401/403 retires token state and unknown application errors propagate without fallback side effects.
- Exact live parity of undocumented authentication and response behavior in ANZ and Russia remains unverified; EU read transport is proven live, while Task 5 EU auth, Task 6 ANZ auth/read, and Task 10 Russia auth/read semantics were deliberately not exercised live.
- The overseas Python transport still deliberately rejects compressed responses. The separate China adapter, expanded through Task 9, proves independently bounded gzip over HTTP/1.1 against synthetic services, but `aiohttp` cannot prefer HTTP/2 and no live China read was approved; a sanitized live validation must decide whether the service accepts the permitted fallback or an isolated HTTP/2-capable dependency is required before Gate A-CN activation.
- China authentication now crosses three services in the standalone client, but its exact live error-code behavior remains unverified. Unknown G-App, BeanTech, or AutoAI application codes therefore do not retire authentication, trigger SMS delivery/login, or discard a recoverable G-App-only partial; risk-control `1013` remains an explicit stop directing the user to the official app.
- BeanTech and AutoAI initialization now use the narrowly bounded three-attempt policy selected in D-035. It is fixture-proven only; live validation must confirm gateway behavior, while SMS delivery/login, refresh, reads, schema/TLS/risk failures, and commands retain no automatic retry.
- China live evidence is currently limited to a contributed NavInfo WEY VV6 and selected reads/controls. Gate A-CN needs suitable sanitized access before direct China setup can be exposed, while heating, extended controls, charging, other platforms/models, and broader response encodings need their own later evidence.
- The cloud DTOs intentionally retain only the fields required to establish the protocol boundary. All four regions now have sanitized response fixtures; Task 11's complete four-region normalized-snapshot mapping remains outstanding.
- ANZ's exact basics `607099` response is now a typed raw-client optional-endpoint failure. The Task 13 coordinator must deliberately map it to empty basics to preserve the add-on polling service's nonfatal behavior.
- Whether Task 12 should use one dedicated cookie-free client session per config entry or a policy-validated HA-owned session while preserving scoped TLS and unload ownership.
- Availability of safe test accounts/vehicles for every regional read and write matrix.
- ANZ side-by-side session effects remain untested. Task 6 prevents every password login without explicit one-shot consent and prevents automatic `607501` reclaim loops, but Task 12 must explain that consent clearly and the project still recommends a dedicated shared vehicle account.
- ANZ `110641`, current token-expiry/rotation behavior, verification delivery/expiry, AU-versus-NZ differences, and unknown `checkSMSCode` failures lack sanitized current-service evidence; unknown errors stop without attempting the final login.
- Russia application-level token-expiry and wrong/expired verification-code values lack sanitized evidence. Exact `110641` is therefore limited to the password-login challenge, submitted-code application failures remain unknown, and only HTTP 401/403 can retire or reject authentication state.
- Safe handling and future renewal of bundled bootstrap certificates and OEM-derived key material.
- Licensing/provenance of code, certificates, China app-derived signing material, and other resources derived from reverse-engineering work.
- Whether the final client is bundled for HACS or published as a separately versioned Python dependency.
- Whether existing users perform one fresh authentication or use a temporary secured state-export path.
- Blocking certificate/key workers finish protected temporary-file cleanup before propagating cancellation, so a cancelled authentication may return after its nominal deadline even though no network stage may continue past that deadline.
- Durable reconciliation semantics for a command accepted immediately before HA reload, shutdown, or loss of connectivity.
