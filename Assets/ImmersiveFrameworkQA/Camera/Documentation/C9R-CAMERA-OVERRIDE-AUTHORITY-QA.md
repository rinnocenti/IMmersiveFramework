# C9R — Camera Authority and ADR-026 QA

Status: **canonical Camera lifecycle and topology regression**
Last updated: **2026-09-08**

## Product contract

Ordinary players publish subject availability. They do not own or publish a
`CameraRequest`. A `CameraSharedComposition` selects the active subjects from the
joined-player set and rejects stale player occurrences after leave/rejoin.

Generic Camera authority is output-scoped:

```text
Output Default   baseline
Activity         100
Route            200
Session          300
```

Higher precedence wins; release or owner exit restores the next valid request and
ultimately the authored Output Default rig.

## Existing execution rail

```text
QA Hub
  -> RouteTrigger_Camera__Override_Authority
  -> QA_PlayerCameraArbitrationRoute
  -> startup Activity
  -> QA_PlayerCameraArbitration scene
  -> QaCameraOverrideAuthorityFixture
  -> back-to-Hub Route
```

The fixture is repaired in place. There is no second Camera service, context,
manager or parallel orchestration path.

## ADR-026 Shared phase

The persistent composition authors two distinct Outputs. Shared Camera Composition
binds Main View to Output A fullscreen. The advanced Policy binds only Secondary
View to Output B fullscreen. The fixture proves:

- P1 join creates one live subject and target-group member;
- P2 join creates the second member;
- P1 leave removes its exact occurrence while P2 remains;
- P1 rejoin creates a new occurrence and never revives stale P1-A;
- View, composer, Cinemachine camera and Output identity stay stable;
- ordinary Player flow publishes zero Camera requests on both Outputs;
- cleanup leaves both players and closes joining.

## ADR-026 Split/Multi-Output phase

The second fresh boot uses CAMERA-027-D coexistence: Shared Camera Composition
binds Split A View to Output A on the left half, and the advanced Policy binds
only Split B View to Output B on the right half. It proves distinct
Camera/Brain/default-rig materialization, exact binding cardinality, missing-output
rejection, independent request arbitration and restoration to each Output's own
Default rig.

## Setup and execution

```text
Immersive Framework > QA > Setup > Camera > Prepare ADR-026 Shared Phase
Immersive Framework > QA > Setup > Camera > Prepare ADR-026 Split Phase
Immersive Framework > QA > Camera > Run Full Camera QA
```

The full runner executes ADR-026 Shared and Split topology, generic
Activity/Route/Session authority, ADR-004B negative integrity and ADR-004C owner
lifetime. Camera Rig Behavior Definitions and View/Output definitions are the
normal authoring authority. It changes topology only in Edit Mode and enters a
fresh Play Mode session for each ADR-026 phase.

Expected terminal evidence is causal and machine-readable:

```text
[QA_CAMERA_ADR026] status='Passed' phase='Shared' ...
[QA_CAMERA_ADR026] status='Passed' phase='Split' ...
[QA_CAMERA_FULL] status='Completed' verdict='CAMERA QA CERTIFIED' ...
```

Do not record PASS from static inspection. Certification requires a clean Unity
compile/import and the full runtime run.
