# Camera QA Consolidation Closure — updated 2026-09-08

Status: **current operational map after ADR-026 migration**

## Retained surfaces

```text
C9M Follow Pipeline
  local CameraRigComposer materialization

Camera Output Session Binding Authoring Regression
  output identity, references and Default rig validation

Persistent Camera Presentation Composition Regression
  one-or-more distinct Outputs plus View-to-Output topology

C9R / ADR-026 canonical fixture
  Shared subjects, occurrence safety, Split/Multi-Output and generic authority

ADR-004C Owner Lifetime Integrity
  abnormal component publication lifetime

ADR-004B Negative Integrity
  deterministic negatives, rollback and delegated lifecycle evidence
```

## Audit classification

```text
keep
  Hub -> Route -> Activity -> canonical Camera scene -> fixture -> Hub
  Activity/Route/Session precedence and ADR-004B/004C cleanup evidence

migrate
  single-output composition -> 1..N Outputs with exact unique identities
  reflection validation -> public topology/authoring contracts
  Player baseline restoration -> authored Output Default restoration

remove
  QaC9RLocalPlayerCameraRequestBinding
  LocalPlayerCameraRequestPublisher dependencies
  LocalPlayer owner/lifetime assertions
  assumption that a second valid Output is an error

extend
  Shared two-player join/leave/rejoin occurrence proof
  Split View-to-Output binding, viewport and output-isolation proof
```

The owning surface remains `QAFramework/Camera`; the Framework package owns the
runtime contracts and is consumed read-only by this QA cut.

## Current execution

```text
Immersive Framework > QA > Camera > Run Full Camera QA
  -> ADR-022 Edit Mode regression
  -> author Shared topology in Edit Mode
  -> fresh Play Mode / Hub / canonical fixture / ADR-004B / ADR-004C
  -> return to Edit Mode and author Split topology
  -> fresh Play Mode / Hub / canonical fixture
  -> one final multidimensional diagnostic
```

Historical inventories remain point-in-time evidence. Historical C9R/ADR-004
PASS lines must not be used as ADR-026 certification. A fresh Unity run is still
required.
