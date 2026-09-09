# Immersive Framework QA

This project contains technical QA for the Immersive Framework. The QA source of
truth is the current product contract, not historical cut numbering.

## Principles

```text
QA proves the framework.
QA does not create a parallel runtime.
```

- use public framework APIs and canonical lifecycle composition;
- setup materializes authored configuration;
- fixture owns references/scope;
- regression executes contracts and records evidence;
- orchestrators coordinate already-valid regressions and do not reproduce setup
  or assertions;
- capture baseline before mutation;
- acquire scoped endpoints while the scope is valid;
- cleanup is idempotent and follows ownership in reverse order;
- do not use reflection, service locators, global opportunistic lookup or silent
  fallbacks to make a smoke pass.

## Current domain surfaces

- `Player/`: one Player functional. Setup creates fixtures, Hub opens the Player
  QA scene, and the panel runs the full public Player contract in Play Mode.
  Pause/Input/Gate authoring remains a focused Edit Mode proof in the same
  functional. PauseP1 product-binding assets stay adjacent for the Pause Hub
  entry; Game Flow keeps Activity participation regressions.
- `Camera/`: Camera rig materialization, ADR-026 Shared/Split topology,
  output-scoped authority, ADR-004B negative integrity and ADR-004C owner lifetime
  certification.
- `GameFlow/`: Route/Activity request, reset/restart and lifecycle contracts.
  The focused ADR-005 Pause runtime regression remains adjacent in
  `GameFlow/InternalEditor/QaPauseRuntimeBindingSmoke.cs` because it proves Pause
  runtime authority, PlayerInput/Gate behavior and the Pause + Activity Restart
  interaction on the existing GameFlow lifecycle. It is complementary to the
  static Pause/Input/Gate composition proof in `Player/Scripts/Editor`; there is no
  separate canonical `Pause/` QA domain directory in the current tree.
- `ActivityFlow/`: Activity transaction/readiness behavior.
- `InputMode/`, `Transition/`, `Loading/`, `Reset/`, `Audio/` and other focused
  product domains retain their own canonical technical proofs where those
  directories actually exist.

Historical inventories may list removed or merged smokes. They are evidence of
past state, not the current execution surface.

## Camera QA — current architecture

Camera uses the existing Hub/Route/Activity fixture for two fresh ADR-026 boots:

```text
Shared
  -> players publish subject availability only
  -> one shared CameraView selects joined subjects occurrence-safely
  -> no ordinary Player CameraRequest exists

Split
  -> two CameraViews bind to two distinct CameraOutputs
  -> viewport topology and output arbitration remain isolated

Generic authority
  -> Output Default / Activity / Route / Session precedence and cleanup
```

ADR-004B and ADR-004C remain focused certifications on the same authority rail.
Historical PASS counts are not evidence for the ADR-026 implementation; a fresh
Unity run is required before recording certification.

### Camera setup

```text
Immersive Framework > QA > Setup > Camera >
Install Camera Override Authority QA
```

The canonical setup prepares Shared mode. Dedicated setup commands prepare
ADR-026 Shared or Split mode, always repairing the existing fixture and
persistent presentation composition rather than creating a parallel authority.

### Camera execution order

```text
Immersive Framework > QA > Camera > Run Full Camera QA
```

The orchestrator runs ADR-022, then a fresh Shared boot, returns through the Hub,
executes generic authority plus ADR-004B/004C evidence, exits Play Mode, authors
Split topology and starts a second fresh boot. Manual execution can instead use
the existing Hub entry after preparing either topology.

### Camera certification documents

- `Camera/Documentation/C9R-CAMERA-OVERRIDE-AUTHORITY-QA.md`
- `Camera/Documentation/ADR004C-CAMERA-OWNER-LIFETIME-INTEGRITY.md`
- `Camera/Documentation/ADR004B-CAMERA-NEGATIVE-INTEGRITY-QA.md`
- `Camera/Documentation/CAMERA-QA-CONSOLIDATION-CLOSURE-2026-08-10.md`

### Camera consolidation

The current Camera QA intentionally does not restore historical reflection-based
or Local Player Camera-request smokes. See the Camera consolidation closure
document for the retained surface and ADR-026 migration classification.

## Player QA canonical architecture

Player is one QA feature with one setup, Hub entry, primary scene and
consolidated panel — the same shape as Audio, Pooling and Lifecycle.

```text
1. Immersive Framework > QA > Setup > Player > Configure Player QA
2. Open Assets/ImmersiveFrameworkQA/Hub/Scenes/QA_Hub.unity
3. Enter Play Mode
4. Open Player QA
5. Run All Player QA
6. Inspect the consolidated PASS / FAIL output
```

`Run Full Player QA` in Edit Mode validates authoring and Pause/Input/Gate
composition. The same menu in Play Mode runs the panel suite, requesting the
Player route from Hub when needed.

See `Player/README.md`.

### Pause/Input/Gate composition certification

The current single-player authoring authority is:

```text
UnityPlayerInputGateAdapter
  -> owns PlayerInput
  -> owns Gameplay Action Map
  -> owns physical Gate writes

PlayerPauseInput
  -> owns Pause Action
  -> derives PlayerInput and Gameplay Action Map from the Gate Adapter
```

Focused Edit Mode regression:

```text
Immersive Framework
  > QA
    > Player
      > Pause
        > Run Pause Input Gate Composition
```

This Edit Mode regression does not replace the Play Mode Pause lifecycle proof.
`GameFlow/InternalEditor/QaPauseRuntimeBindingSmoke.cs` remains responsible for
Pause state transitions, Gate application/restoration, lifecycle release/teardown
and Pause + Activity Restart behavior.

## Identity Authority

Identity QA follows authored-definition authority and stable identity contracts.
Route/Activity authored definitions are not made equivalent merely because text
or scene data matches. Stable IDs are persistence/diagnostic evidence where the
owning product contract defines them.

## Historical QA documents

Documents such as `Documentation/QA-SMOKE-CONSOLIDATION-AUDIT.md` are historical
point-in-time audits. Their old file counts, menus and classifications are not a
request to recreate removed smokes. Current domain documentation and source code
are authoritative for execution.
