# ADR-004B Camera Negative Integrity QA

Status: **CERTIFIED — 18/18**  
Last updated: **2026-08-10**

## Purpose

Certify the negative and transactional integrity boundaries required by
`IF-ADR-004B — Camera Negative Integrity Certification` without creating a
parallel Camera runtime.

The regression reuses the canonical C9R Camera Override Authority lifecycle for
real Activity/Route evidence and existing Camera authoring regressions for
persistent-output validation.

## Source provenance

Original QA authoring bases:

```text
QAFramework
  f4ce36335878113e4b64e79d337c0645f6499707

com.immersive.framework
  bbaf05dbc7442290de8916fe312acd77a11f2b58
```

Current repository heads at documentation closure:

```text
QAFramework
  c7f3443df9a95011220db5d584de7afb94e331ec
  Cam-Pass

com.immersive.framework
  baecd612c79fe4dabfde5be8d7cf17f3b6b4a3ea
  Adr004
```

The final 004B log intentionally distinguishes its package base from the applied
`IF-ADR-004C` package patch.

## Setup

Use the existing Camera setup. 004B adds no setup, scene, fixture, context,
service or output authority.

```text
Immersive Framework > QA > Setup > Camera >
Install Camera Override Authority QA
```

## Execution

1. Enter Play Mode from QA Hub.
2. Run `Camera Override Authority`.
3. Wait for C9R to return to Hub and emit its 11-case PASS.
4. Stay in the same Play Mode session.
5. Run:

```text
Immersive Framework > QA > Regressions > Camera >
Run ADR-004B Negative Integrity Certification
```

Cases 14-16 consume C9R evidence from that same Play Mode session. Missing C9R
evidence blocks those delegated cases rather than assuming them.

## Matrix ownership

```text
01-13  QaCameraAdr004BNegativeIntegrityRegression
14     canonical C9R Activity lifecycle cleanup
15     canonical C9R Route lifecycle cleanup + Session survivor preservation
16     focused C9R abnormal Route-owner disable probe
17     QaPersistentCameraPresentationCompositionRegression
18     QaCameraOutputSessionBindingAuthoringRegression
```

Cases 11-13 use deterministic fixture-controlled invalid rig state to prove
physical apply rollback and explicit rollback failure without adding a production
fault-injection service.

## Case 15 evidence boundary

Route cleanup owns the Route request only. The C9R proof requires:

```text
Route request absent
+ synthetic Session survivor still admitted
+ winner remains arbitration-owned
```

It deliberately does not require the synthetic survivor to be the winner because
the persistent Session Camera may legitimately be republished during transition
and win by precedence.

The earlier `winner == synthetic survivor` assertion was a QA false negative and
was removed without changing package behavior.

## Case 16 history

The first valid owner-loss probe reproduced:

```text
[QA_CAMERA_ADR004B]
case='16-abnormal-owner-loss'
operation='DisableRouteOwner'
admittedBefore='2'
admittedAfter='2'
orphan='True'
```

That result correctly prevented certification and opened:

```text
IF-ADR-004C — Camera Owner Lifetime Integrity
```

The package was then hardened at the scoped publication/component lifetime
boundary. 004B was not weakened to hide the failure.

## Final certification

After the 004C package correction:

```text
[CAMERA_RUNTIME_HOST_INTEGRATION_REGRESSION]
status='Passed'
cases='11'

[QA_CAMERA_ADR004C]
status='Passed'
cases='10/10'
failed='0'

[QA_CAMERA_ADR004B]
status='Passed'
cases='18/18'
failed='0'
blocked='0'
packagePatch='IF-ADR-004C'
verdict='ADR-004B CAMERA NEGATIVE INTEGRITY CERTIFIED'
```

Case 16 now reports:

```text
admittedBefore='2'
admittedAfter='1'
orphan='False'
```

## What 004B certifies

- deterministic precedence and tie-break behavior;
- duplicate RequestId and wrong OutputId blocking;
- repeated Publish/Release idempotence;
- winner restoration and out-of-order release;
- admission/release physical-apply rollback;
- explicit rollback failure;
- normal Activity/Route cleanup;
- abnormal Route owner loss without orphaning;
- duplicate persistent OutputId validation;
- invalid output reference validation;
- no fallback Camera authority introduced by QA.

The obsolete synthetic Local Player Camera-request binding was removed by the
ADR-026 QA migration. Player flow is now asserted only as a Camera Subject source.

## Verdict

```text
ADR-004B
  CERTIFIED 18/18

Product defect discovered by initial case 16
  RESOLVED by IF-ADR-004C

Current ADR-026 runtime certification
  PENDING fresh Shared/Split Unity run
```
