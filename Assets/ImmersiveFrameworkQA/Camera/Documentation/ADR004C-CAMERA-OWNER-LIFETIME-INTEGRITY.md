# IF-ADR-004C — Camera Owner Lifetime Integrity

Status: **CERTIFIED — 10/10**  
Last updated: **2026-08-10**  
Triggered by: **ADR-004B case 16**

## Trigger evidence

Canonical C9R composition reproduced an admitted Route request surviving
abnormal component disable:

```text
case='16-abnormal-owner-loss'
operation='DisableRouteOwner'
request='qa.camera.request.c9r.route'
admittedBefore='2'
admittedAfter='2'
orphan='True'
```

Normal Activity and Route lifecycle exits still passed. The first causal
divergence was therefore component/publication lifetime, not Route/Activity Game
Flow ownership.

## Ownership decision

Two lifetimes are distinct:

```text
logical owner lifetime
  Route    -> Route enter/exit
  Activity -> Activity enter/exit
  Session  -> Session binding availability

publication/component lifetime
  ScopedCameraOverrideBinding
    -> publisher
    -> overrideActive
```

A temporary Route/Activity component disable must not synthesize logical owner
exit. The fix therefore belongs to the existing scoped publication owner.

## Package correction

```text
ScopedCameraOverrideBinding.OnDisable
  -> release owned publication only

ScopedCameraOverrideBinding.OnDestroy
  -> final idempotent publication release
```

Those shared hooks do not set Route/Activity `ownerActive = false` and do not
re-publish on re-enable.

Session is different because its component owns Session availability:

```text
SessionCameraOverrideBinding.OnDisable
  -> EndOwnerScope(...)

SessionCameraOverrideBinding.OnDestroy
  -> EndOwnerScope(...)
```

Package files:

```text
Runtime/Camera/Bindings/ScopedCameraOverrideBinding.cs
Runtime/Camera/Bindings/SessionCameraOverrideBinding.cs
```

No manager, service, registry, context, runtime host, fallback or alternate
lifecycle was added.

## QA reuse

No new setup or lifecycle fixture was created. C9R remains the canonical Camera
fixture and retains its 11-case positive contract. 004C probes additional owner
lifetime boundaries during the same execution and the Editor regression consumes
that evidence afterwards.

Menu:

```text
Immersive Framework > QA > Regressions > Camera >
Run ADR-004C Owner Lifetime Integrity Certification
```

## Certified matrix

```text
01 Activity normal exit
02 Route normal exit
03 Session disable cleanup
04 Route abnormal disable cleanup
05 Activity abnormal disable cleanup
06 Activity destruction cleanup
07 non-winner owner-only cleanup
08 winning owner restores next
09 cleanup idempotent
10 re-enable without silent republish
```

## Executed certification

```text
[QA_CAMERA_ADR004C]
status='Passed'
cases='10/10'
failed='0'
packageBaseRevision='bbaf05dbc7442290de8916fe312acd77a11f2b58'
verdict='ADR-004C CAMERA OWNER LIFETIME INTEGRITY CERTIFIED'
```

The original 004B owner-loss probe now reports:

```text
admittedBefore='2'
admittedAfter='1'
orphan='False'
```

C9R remained green and 004B then certified all negative-integrity cases:

```text
C9R      11/11 PASS
004C     10/10 PASS
004B     18/18 PASS
```

## Certified behavior

- normal lifecycle exit remains authoritative for logical Route/Activity end;
- component disable/destroy cannot leave its Camera publication orphaned;
- Session disable/destroy ends Session scope;
- non-winning owner loss does not perturb the current winner;
- winning owner loss restores the next valid request;
- repeated cleanup is idempotent;
- re-enable does not silently publish;
- explicit publication remains possible only while logical owner state is valid.

The obsolete synthetic Local Player Camera-request binding was removed by the
ADR-026 QA migration. These cases now restore the authored Output Default rig.

## Verdict

```text
IF-ADR-004C
  ACCEPTED / IMPLEMENTED / CERTIFIED
  cases=10/10

IF-ADR-004B after 004C
  CERTIFIED 18/18

C9R positive lifecycle
  PASS 11/11
```
