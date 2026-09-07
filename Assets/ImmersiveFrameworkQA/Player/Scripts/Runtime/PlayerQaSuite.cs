using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Immersive.Framework.Actors;
using Immersive.Framework.GameFlow;
using Immersive.Framework.PlayerParticipation;
using Immersive.Framework.PlayerSlots;
using Immersive.Framework.RuntimeContent;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace ImmersiveFrameworkQA.Player
{
    internal static class PlayerQaSuite
    {
        private const int FrameBudget = 360;
        private const string Source = nameof(PlayerQaSuite);

        internal sealed class Result
        {
            internal readonly List<string> Completed = new List<string>(16);
            internal readonly List<string> Blocked = new List<string>(2);
            internal string FailedCase;
            internal string FailureMessage;
            internal int Passed;
            internal int Failed;
            internal LocalPlayerHostAuthoring PlayerOneHost;
            internal PlayerGameplayInputReader CurrentGameplayReader;
            internal PlayerGameplayInputBindingToken LastReleasedGameplayBinding;
            internal bool PreviousReaderOccurrenceReleased;
            internal Keyboard MembershipKeyboard;

            internal bool Ok => Failed == 0 && string.IsNullOrEmpty(FailedCase);
            internal bool IsCertified => Ok && Blocked.Count == 0;

            internal void Pass(string caseId)
            {
                Completed.Add(caseId);
                Passed++;
            }

            internal void Fail(string caseId, string expected, string actual, string message)
            {
                FailedCase = caseId;
                Failed++;
                FailureMessage =
                    $"{caseId}: expected '{expected}', actual '{actual}'. {message}";
            }

            internal void Block(string caseId, string capability, string currentBehavior)
            {
                Blocked.Add(
                    $"{caseId}: capability='{capability}' current='{currentBehavior}'");
            }
        }

        internal static IEnumerator Run(PlayerQaPanel fixture, Action<Result> completed)
        {
            var result = new Result();
            try
            {
                yield return RunCases(fixture, completed, result);
            }
            finally
            {
                if (result.MembershipKeyboard != null)
                {
                    if (TryGetAccess(fixture, out IPlayerSessionScopedAccess access, out _) &&
                        TryFindSlot(fixture, ExpectedSlotTwoId(fixture), out PlayerSessionScopedSlotObservation p2) &&
                        p2.IsJoined && p2.HasInputOwnershipEvidence &&
                        OwnershipContainsDevice(p2.InputOwnership, result.MembershipKeyboard.deviceId))
                    {
                        access.RequestLeave(new SessionPlayerLeaveRequest(
                            p2.Slot.PlayerSlotId, p2.Slot.Revision, Source, "player-qa-membership-device-cleanup"));
                    }

                    RemoveOwnedDevice(result.MembershipKeyboard);
                    result.MembershipKeyboard = null;
                }
            }
        }

        private static IEnumerator RunCases(PlayerQaPanel fixture, Action<Result> completed, Result result)
        {
            if (fixture == null)
            {
                result.Fail("fixture", "configured panel", "null", "Player QA panel is missing.");
                completed?.Invoke(result);
                yield break;
            }

            yield return RunGroup(result, "access", () => ProveAccess(fixture, result));
            if (!result.Ok)
            {
                completed?.Invoke(result);
                yield break;
            }

            yield return WaitThen(result, "join", () => ProveJoin(fixture, result));
            if (!result.Ok)
            {
                completed?.Invoke(result);
                yield break;
            }

            yield return WaitThen(result, "observation", () => ProveObservation(fixture, result));
            if (!result.Ok)
            {
                completed?.Invoke(result);
                yield break;
            }

            // P1 canonical lifecycle: one retained occurrence reaches GameplayReady.
            yield return WaitThen(result, "actor-default", () => ProveDefaultActor(fixture, result));
            if (!result.Ok)
            {
                completed?.Invoke(result);
                yield break;
            }

            yield return WaitThen(result, "actor-lifecycle", () => ProveActorLifecycle(fixture, result));
            if (!result.Ok)
            {
                completed?.Invoke(result);
                yield break;
            }

            yield return WaitThen(result, "gameplay-ready-reader", () => ProveGameplayReadyReader(fixture, result));
            if (!result.Ok)
            {
                completed?.Invoke(result);
                yield break;
            }

            // P1 adversarial/cardinality: fresh occurrences always clean up before the next proof.
            yield return WaitThen(result, "reader-cardinality", () => ProveReaderCardinality(fixture, result));
            if (!result.Ok)
            {
                completed?.Invoke(result);
                yield break;
            }

            // ADR-024 positive proof: replace the prepared Actor inside the retained P1 occurrence.
            yield return WaitThen(result, "actor-replace", () => ProveReplaceActor(fixture, result));
            if (!result.Ok)
            {
                completed?.Invoke(result);
                yield break;
            }

            // P2 membership lifecycle: Join -> Joining control -> Leave -> Rejoin -> cleanup.
            yield return WaitThen(result, "second-player", () => ProveSecondPlayer(fixture, result));
            if (!result.Ok)
            {
                completed?.Invoke(result);
                yield break;
            }

            yield return WaitThen(result, "joining-control", () => ProveJoiningControl(fixture, result));
            if (!result.Ok)
            {
                completed?.Invoke(result);
                yield break;
            }

            yield return WaitThen(result, "commands", () => ProveCommands(fixture, result));
            if (!result.Ok)
            {
                completed?.Invoke(result);
                yield break;
            }

            yield return WaitThen(result, "leave", () => ProveLeave(fixture, result));
            if (!result.Ok)
            {
                completed?.Invoke(result);
                yield break;
            }

            yield return WaitThen(result, "rejoin", () => ProveRejoin(fixture, result));
            if (!result.Ok)
            {
                completed?.Invoke(result);
                yield break;
            }

            yield return WaitThen(result, "negatives", () => ProveNegatives(fixture, result));
            if (!result.Ok)
            {
                completed?.Invoke(result);
                yield break;
            }

            yield return WaitThen(result, "input-ownership", () => ProveInputOwnership(fixture, result));
            if (!result.Ok)
            {
                completed?.Invoke(result);
                yield break;
            }

            yield return RunGroup(result, "spatial", () => ProveSpatial(fixture, result));
            if (!result.Ok)
            {
                completed?.Invoke(result);
                yield break;
            }

            yield return RunGroup(result, "relocation", () => ProveRelocation(fixture, result));
            completed?.Invoke(result);
        }

        private static IEnumerator RunGroup(Result result, string caseId, Action body)
        {
            try
            {
                body();
                if (result.Ok)
                {
                    result.Pass(caseId);
                }
            }
            catch (Exception exception)
            {
                result.Fail(caseId, "no exception", exception.GetType().Name, exception.Message);
            }

            yield return null;
        }

        private static IEnumerator WaitThen(Result result, string caseId, Func<IEnumerator> body)
        {
            Exception caught = null;
            IEnumerator routine = null;
            try
            {
                routine = body();
            }
            catch (Exception exception)
            {
                caught = exception;
            }

            if (caught != null)
            {
                result.Fail(caseId, "no exception", caught.GetType().Name, caught.Message);
                yield break;
            }

            while (true)
            {
                bool moveNext;
                try
                {
                    moveNext = routine.MoveNext();
                }
                catch (Exception exception)
                {
                    result.Fail(caseId, "no exception", exception.GetType().Name, exception.Message);
                    (routine as IDisposable)?.Dispose();
                    yield break;
                }

                if (!moveNext)
                {
                    break;
                }

                if (!result.Ok)
                {
                    (routine as IDisposable)?.Dispose();
                    yield break;
                }

                yield return routine.Current;

                if (!result.Ok)
                {
                    (routine as IDisposable)?.Dispose();
                    yield break;
                }
            }

            if (result.Ok)
            {
                result.Pass(caseId);
            }
        }

        private static void ProveAccess(PlayerQaPanel fixture, Result result)
        {
            Require(result, "access",
                fixture.Probe != null &&
                fixture.Observer != null &&
                fixture.ActivityScopeProbe != null,
                "Route and Activity scoped probes assigned",
                "missing",
                "Player QA scene requires Route-scoped access/observation and an Activity-scoped access probe.");
            if (!result.Ok)
            {
                return;
            }

            Require(result, "access",
                fixture.Probe.Scope == LocalPlayerProvisioningConsumerScope.Route &&
                fixture.Observer.Scope == LocalPlayerProvisioningConsumerScope.Route,
                "Route-scoped access consumers",
                $"{fixture.Probe.Scope}/{fixture.Observer.Scope}",
                "The canonical Player QA access probe and observer must remain Route-scoped.");
            if (!result.Ok)
            {
                return;
            }

            bool activityScopeBound = fixture.ActivityScopeProbe.TryGetAccess(
                out IPlayerSessionScopedAccess activityScopeAccess,
                out string activityScopeIssue);
            Require(result, "access",
                fixture.ActivityScopeProbe.Scope == LocalPlayerProvisioningConsumerScope.Activity &&
                activityScopeBound &&
                activityScopeAccess != null &&
                activityScopeAccess.Snapshot.IsAvailable,
                "Activity-scoped access bound to the active Activity lifecycle scope",
                activityScopeBound
                    ? "available"
                    : activityScopeIssue,
                "Activity scope is a Framework lifecycle scope; it is not inferred from nominal scene ownership.");
        }

        private static IEnumerator ProveJoin(PlayerQaPanel fixture, Result result)
        {
            IPlayerSessionScopedAccess access = null;
            yield return WaitFor(
                result,
                "join",
                () => fixture.Probe.TryGetAccess(out access, out _) &&
                      access != null &&
                      access.Snapshot.IsAvailable,
                "Timed out waiting for Route-scoped IPlayerSessionScopedAccess.");

            Require(result, "join", access != null && access.Snapshot.IsAvailable,
                "scoped access available", access == null ? "null" : access.Snapshot.Diagnostic,
                "Timed out waiting for Route-scoped IPlayerSessionScopedAccess.");
            if (!result.Ok)
            {
                yield break;
            }

            Require(result, "join", access.Snapshot.HasJoinCapability,
                "HasJoinCapability", "false",
                "Manager-Provisioned Player QA requires ILocalPlayerJoinAccess.");
            if (!result.Ok)
            {
                yield break;
            }

            Require(result, "join",
                fixture.Probe.TryGetJoinAccess(out ILocalPlayerJoinAccess joinAccess, out string joinIssue) &&
                joinAccess != null,
                "join access", joinIssue,
                "ILocalPlayerJoinAccess is unavailable.");
            if (!result.Ok)
            {
                yield break;
            }

            if (TryFindSlot(fixture, ExpectedSlotId(fixture), out PlayerSessionScopedSlotObservation existing) &&
                existing.IsJoined)
            {
                Require(result, "join",
                    existing.HasHostEvidence,
                    "joined P1 with Host evidence",
                    existing.HasHostEvidence ? "present" : "missing",
                    "Existing joined P1 does not expose retained Session Host evidence.");
                yield break;
            }

            LocalPlayerJoinResult join = joinAccess.RequestJoin(
                new LocalPlayerJoinRequest(Source, "player-qa-join-p1"));
            Require(result, "join", join != null && join.Succeeded,
                "SucceededJoined", join == null ? "null" : $"{join.Status} {join.Message}",
                "RequestJoin failed.");
            if (!result.Ok)
            {
                yield break;
            }

            Require(result, "join",
                join.Slot.PlayerSlotId == ExpectedSlotId(fixture) &&
                join.HasLocalPlayerHostEvidence &&
                join.LocalPlayerHost != null &&
                join.PlayerInput != null,
                "P1 host evidence",
                join.Slot.PlayerSlotId.IsValid ? join.Slot.PlayerSlotId.StableText : "invalid",
                "Join did not expose complete public Host evidence.");
            if (!result.Ok)
            {
                yield break;
            }

            ValidateManagerHost(result, fixture, join.LocalPlayerHost);
            if (result.Ok)
            {
                result.PlayerOneHost = join.LocalPlayerHost;
            }
            yield return WaitForSlot(
                result,
                "join",
                fixture,
                slot => slot.IsJoined &&
                        slot.HasHostEvidence);
        }

        private static IEnumerator ProveObservation(PlayerQaPanel fixture, Result result)
        {
            yield return WaitForSlot(result, "observation", fixture, slot => slot.IsJoined);
            if (!TryGetAccess(fixture, out IPlayerSessionScopedAccess access, out string issue))
            {
                result.Fail("observation", "available access", issue, issue);
                yield break;
            }

            Require(result, "observation",
                access.TryGetObservation(out PlayerSessionScopedObservationSnapshot fromAccess) &&
                fixture.Observer.TryGetObservation(out PlayerSessionScopedObservationSnapshot fromObserver) &&
                fromAccess != null &&
                fromObserver != null &&
                fromAccess.IsAvailable &&
                fromObserver.IsAvailable &&
                fromAccess.SessionRevision == fromObserver.SessionRevision,
                "matching observer/access snapshots",
                "diverged",
                "PlayerSessionObserver and IPlayerSessionScopedAccess did not expose the same Session.");
            if (!result.Ok)
            {
                yield break;
            }

            PlayerSessionScopedSlotObservation slot = FindSlot(fromAccess, ExpectedSlotId(fixture));
            Require(result, "observation", slot.IsJoined,
                "P1 joined", slot.Slot.AllocationState.ToString(),
                "Observation does not show the joined P1 Slot.");
            if (!result.Ok)
            {
                yield break;
            }

            bool sawChange = false;
            bool sawDesignerJoining = false;
            void OnChanged(PlayerSessionChange change)
            {
                if (change != null &&
                    (change.Kind == PlayerSessionChangeKind.SlotAllocationChanged ||
                     change.Kind == PlayerSessionChangeKind.ActorSelectionChanged ||
                     change.Kind == PlayerSessionChangeKind.JoiningChanged))
                {
                    sawChange = true;
                }
            }

            void OnJoiningChanged()
            {
                sawDesignerJoining = true;
            }

            fixture.Observer.Changed += OnChanged;
            fixture.Observer.OnJoiningClosed.AddListener(OnJoiningChanged);
            fixture.Observer.OnJoiningOpened.AddListener(OnJoiningChanged);
            try
            {
                PlayerParticipationOperationResult closed = access.CloseJoining(
                    Source, "player-qa-observe-close");
                PlayerParticipationOperationResult opened = access.OpenJoining(
                    Source, "player-qa-observe-open");
                Require(result, "observation",
                    closed != null && closed.Completed &&
                    opened != null && opened.Completed,
                    "joining close/open mutation",
                    $"close='{(closed != null ? closed.Status.ToString() : "null")}' " +
                    $"open='{(opened != null ? opened.Status.ToString() : "null")}'",
                    "Could not mutate Joining state to prove Player Session observation.");
                if (!result.Ok)
                {
                    yield break;
                }

                yield return WaitFor(
                    result,
                    "observation",
                    () => sawChange && sawDesignerJoining,
                    "PlayerSessionObserver did not emit both Changed and a designer Joining event after Close/Open Joining.");
            }
            finally
            {
                fixture.Observer.Changed -= OnChanged;
                fixture.Observer.OnJoiningClosed.RemoveListener(OnJoiningChanged);
                fixture.Observer.OnJoiningOpened.RemoveListener(OnJoiningChanged);
            }

            Require(result, "observation", sawChange && sawDesignerJoining,
                "observer change and designer joining event",
                $"changed='{sawChange}' designerJoining='{sawDesignerJoining}'",
                "Player Session observation did not emit the expected committed change surfaces.");
        }

        private static IEnumerator ProveDefaultActor(PlayerQaPanel fixture, Result result)
        {
            if (!TryGetAccess(fixture, out IPlayerSessionScopedAccess access, out string issue))
            {
                result.Fail("actor-default", "available access", issue, issue);
                yield break;
            }

            if (!TryFindSlot(fixture, ExpectedSlotId(fixture), out PlayerSessionScopedSlotObservation current))
            {
                result.Fail("actor-default", "joined P1", "missing", "P1 Slot observation is missing.");
                yield break;
            }

            if (current.Slot.SelectedActorProfile != fixture.DefaultActor)
            {
                PlayerActorSelectionResult selection = access.RequestSelectDefaultActor(
                    ExpectedSlotId(fixture),
                    current.Slot.SelectionRevision,
                    Source,
                    "player-qa-default-actor");
                Require(result, "actor-default",
                    selection != null && selection.Succeeded,
                    "default actor selected",
                    selection == null ? "null" : $"{selection.Status} {selection.Message}",
                    "RequestSelectDefaultActor failed.");
                if (!result.Ok)
                {
                    yield break;
                }
            }

            yield return WaitForSlot(
                result,
                "actor-default",
                fixture,
                slot => slot.Slot.SelectedActorProfile == fixture.DefaultActor);

            if (!result.Ok)
            {
                yield break;
            }

            Require(result, "actor-default",
                fixture.DefaultActor != null &&
                fixture.DefaultActor.PresentationPrefab != null,
                "default ActorProfile with PresentationPrefab",
                fixture.DefaultActor != null && fixture.DefaultActor.PresentationPrefab != null
                    ? fixture.DefaultActor.PresentationPrefab.name
                    : "missing",
                "Default Actor selection must retain an explicit ActorProfile PresentationPrefab.");
            if (!result.Ok)
            {
                yield break;
            }

        }
        private static IEnumerator ProveReplaceActor(PlayerQaPanel fixture, Result result)
        {
            if (fixture.AlternateActor == null)
            {
                result.Fail("actor-replace", "alternate actor", "null",
                    "Player QA requires the Alternate Actor Profile.");
                yield break;
            }

            if (!TryGetAccess(fixture, out IPlayerSessionScopedAccess access, out string issue))
            {
                result.Fail("actor-replace", "available access", issue, issue);
                yield break;
            }

            Require(result, "actor-replace",
                access.TryGetObservation(out PlayerSessionScopedObservationSnapshot beforeObservation) &&
                beforeObservation != null && beforeObservation.IsAvailable &&
                beforeObservation.HasCurrentActivityOccurrence,
                "available scoped observation with current Activity occurrence",
                access.Snapshot.Diagnostic,
                "Prepared Actor replacement requires current Session and Activity evidence before the mutation.");
            if (!result.Ok)
            {
                yield break;
            }

            PlayerSessionScopedSlotObservation before =
                FindSlot(beforeObservation, ExpectedSlotId(fixture));
            Require(result, "actor-replace",
                before.IsJoined &&
                before.Slot.SelectedActorProfile == fixture.DefaultActor &&
                before.IsLogicalActorPrepared &&
                before.IsPhysicallyMaterialized &&
                before.HasCurrentActorEvidence &&
                before.CurrentActor.HasCurrentActor &&
                before.HasHostEvidence &&
                before.HasGameplayAdmissionEvidence &&
                before.GameplayAdmission.IsAdmitted &&
                before.GameplayAdmission.GameplayReady,
                "joined GameplayReady P1 with prepared Default Actor",
                before.Slot.PlayerSlotId.IsValid
                    ? $"slot={before.Slot.PlayerSlotId.StableText} selected={DescribeObject(before.Slot.SelectedActorProfile)} prepared={before.IsLogicalActorPrepared} gameplay={before.GameplayAdmission.State}"
                    : "P1 missing",
                "ADR-024 proof must start from the canonical fresh P1 GameplayReady occurrence.");
            if (!result.Ok)
            {
                yield break;
            }

            LocalPlayerHostAuthoring previousHost = result.PlayerOneHost;
            PlayerInput previousPlayerInput = previousHost != null ? previousHost.PlayerInput : null;
            PlayerActorRuntimeHost[] previousRuntimeHosts =
                previousHost != null && previousHost.ActorMount != null
                    ? previousHost.ActorMount.GetComponentsInChildren<PlayerActorRuntimeHost>(true)
                    : Array.Empty<PlayerActorRuntimeHost>();
            PlayerActorRuntimeHost previousRuntimeHost = previousRuntimeHosts.Length == 1
                ? previousRuntimeHosts[0]
                : null;
            GameObject previousPresentation =
                previousRuntimeHost != null &&
                previousRuntimeHost.PresentationMount != null &&
                previousRuntimeHost.PresentationMount.childCount == 1
                    ? previousRuntimeHost.PresentationMount.GetChild(0).gameObject
                    : null;

            string previousReaderIssue = string.Empty;
            PlayerGameplayInputReader previousReader = null;
            Require(result, "actor-replace",
                previousHost != null &&
                previousPlayerInput != null &&
                previousRuntimeHost != null &&
                previousPresentation != null &&
                TryResolveCurrentGameplayReader(
                    previousHost,
                    out previousReader,
                    out previousReaderIssue) &&
                ReferenceEquals(previousReader, result.CurrentGameplayReader) &&
                previousReader.HasCurrentGameplayBinding &&
                previousReader.GameplayReady &&
                previousReader.CurrentBindingToken == before.GameplayAdmission.InputBindingToken,
                "one current Default Actor Runtime Host, Presentation and gameplay reader",
                previousReaderIssue,
                "ADR-024 proof requires exact physical and gameplay baseline evidence before replacement.");
            if (!result.Ok)
            {
                yield break;
            }

            int previousPlayerRevision = before.Slot.Revision;
            int previousSelectionRevision = before.Slot.SelectionRevision;
            int previousSessionRevision = beforeObservation.SessionRevision;
            int previousActivityOccurrence = beforeObservation.ActivityOccurrence;
            PlayerActorPreparationToken previousActorToken = before.Preparation.Token;
            PlayerGameplayAdmissionToken previousGameplayToken = before.GameplayAdmission.Token;
            PlayerGameplayInputBindingToken previousInputBinding =
                before.GameplayAdmission.InputBindingToken;
            Vector3 replacementPose = new Vector3(19.25f, 7.5f, -13.75f);
            Quaternion replacementRotation = Quaternion.Euler(7f, 143f, 19f);
            Vector3 previousRuntimeHostPosition = previousRuntimeHost.transform.position;
            Quaternion previousRuntimeHostRotation = previousRuntimeHost.transform.rotation;
            previousPresentation.transform.SetPositionAndRotation(
                replacementPose,
                replacementRotation);

            var request = new PlayerPreparedActorReplacementRequest(
                ExpectedSlotId(fixture),
                fixture.AlternateActor,
                Source,
                "player-qa-replace-prepared-actor",
                previousSelectionRevision,
                previousSessionRevision);

            PlayerPreparedActorReplacementResult replacement =
                access.RequestReplacePreparedActor(request);
            Require(result, "actor-replace",
                replacement != null &&
                replacement.Status ==
                    PlayerPreparedActorReplacementStatus.SucceededReplacedAndGameplayReady &&
                replacement.PlayerSlotId == ExpectedSlotId(fixture) &&
                replacement.ReplacementCommitted &&
                replacement.GameplayReprojected &&
                !replacement.CleanupPending &&
                replacement.ActivityOccurrence == previousActivityOccurrence,
                "SucceededReplacedAndGameplayReady with committed gameplay reprojection and no pending cleanup",
                replacement == null
                    ? "null"
                    : $"status={replacement.Status} committed={replacement.ReplacementCommitted} gameplayReprojected={replacement.GameplayReprojected} cleanupPending={replacement.CleanupPending} activityOccurrence={replacement.ActivityOccurrence} message={replacement.Message} previousActor={(replacement.PreviousActor.IsValid ? replacement.PreviousActor.ToDiagnosticString() : "<unavailable>")} currentActor={(replacement.CurrentActor.IsValid ? replacement.CurrentActor.ToDiagnosticString() : "<unavailable>")} previousGameplay={(replacement.PreviousGameplay.IsValid ? replacement.PreviousGameplay.ToDiagnosticString() : "<unavailable>")} currentGameplay={(replacement.CurrentGameplay.IsValid ? replacement.CurrentGameplay.ToDiagnosticString() : "<unavailable>")}",
                "The public ADR-024 operation did not return its authoritative successful terminal result.");
            if (!result.Ok)
            {
                yield break;
            }

            Require(result, "actor-replace",
                replacement.PreviousActor.IsPrepared &&
                replacement.PreviousActor.Token == previousActorToken &&
                replacement.PreviousActor.SelectionRevision == previousSelectionRevision &&
                replacement.PreviousGameplay.IsAdmitted &&
                replacement.PreviousGameplay.GameplayReady &&
                replacement.PreviousGameplay.Token == previousGameplayToken &&
                replacement.PreviousGameplay.InputBindingToken == previousInputBinding &&
                replacement.CurrentActor.IsPrepared &&
                replacement.CurrentActor.Token.IsValid &&
                replacement.CurrentActor.Token != previousActorToken &&
                replacement.CurrentActor.SelectionRevision > previousSelectionRevision &&
                replacement.CurrentGameplay.IsAdmitted &&
                replacement.CurrentGameplay.GameplayReady &&
                replacement.CurrentGameplay.Token.IsValid &&
                replacement.CurrentGameplay.Token != previousGameplayToken &&
                replacement.CurrentGameplay.InputBindingToken.IsValid &&
                replacement.CurrentGameplay.InputBindingToken != previousInputBinding,
                "typed A-to-B Actor and Gameplay replacement evidence",
                $"previousActor={replacement.PreviousActor.ToDiagnosticString()} currentActor={replacement.CurrentActor.ToDiagnosticString()} previousGameplay={replacement.PreviousGameplay.ToDiagnosticString()} currentGameplay={replacement.CurrentGameplay.ToDiagnosticString()}",
                "ADR-024 terminal evidence did not prove a new prepared Actor and a new Gameplay admission.");
            if (!result.Ok)
            {
                yield break;
            }

            yield return WaitFor(
                result,
                "actor-replace",
                () =>
                    access.TryGetObservation(out PlayerSessionScopedObservationSnapshot projected) &&
                    projected != null && projected.IsAvailable &&
                    projected.ActivityOccurrence == previousActivityOccurrence &&
                    TryFindSlot(
                        fixture,
                        ExpectedSlotId(fixture),
                        out PlayerSessionScopedSlotObservation projectedP1) &&
                    projectedP1.IsJoined &&
                    projectedP1.Slot.Revision > previousPlayerRevision &&
                    projectedP1.Slot.SelectedActorProfile == fixture.AlternateActor &&
                    projectedP1.IsLogicalActorPrepared &&
                    projectedP1.IsPhysicallyMaterialized &&
                    projectedP1.CurrentActor.HasCurrentActor &&
                    projectedP1.Preparation.Token == replacement.CurrentActor.Token &&
                    projectedP1.HasGameplayAdmissionEvidence &&
                    projectedP1.GameplayAdmission.GameplayReady &&
                    projectedP1.GameplayAdmission.Token == replacement.CurrentGameplay.Token &&
                    TryResolveCurrentGameplayReader(
                        previousHost,
                        out PlayerGameplayInputReader projectedReader,
                        out _) &&
                    projectedReader.HasCurrentGameplayBinding &&
                    projectedReader.GameplayReady &&
                    projectedReader.CurrentBindingToken ==
                        projectedP1.GameplayAdmission.InputBindingToken &&
                    TryResolveCurrentPresentation(
                        previousHost,
                        out PlayerActorRuntimeHost projectedRuntimeHost,
                        out GameObject projectedPresentation) &&
                    !ReferenceEquals(projectedRuntimeHost, previousRuntimeHost) &&
                    !ReferenceEquals(projectedPresentation, previousPresentation) &&
                    Vector3.Distance(projectedPresentation.transform.position, replacementPose) < 0.0001f &&
                    Quaternion.Angle(projectedPresentation.transform.rotation, replacementRotation) < 0.001f &&
                    Vector3.Distance(projectedRuntimeHost.transform.position, previousRuntimeHostPosition) < 0.0001f &&
                    Quaternion.Angle(projectedRuntimeHost.transform.rotation, previousRuntimeHostRotation) < 0.001f &&
                    (previousReader == null || !previousReader.HasCurrentGameplayBinding),
                "Timed out waiting for the public observation and Presentation gameplay reader to converge on the ADR-024 replacement result.");
            if (!result.Ok)
            {
                yield break;
            }

            Require(result, "actor-replace",
                access.TryGetObservation(out PlayerSessionScopedObservationSnapshot afterObservation) &&
                afterObservation != null && afterObservation.IsAvailable,
                "post-replacement scoped observation",
                "unavailable",
                "Could not recollect public evidence after prepared Actor replacement.");
            if (!result.Ok)
            {
                yield break;
            }

            PlayerSessionScopedSlotObservation after =
                FindSlot(afterObservation, ExpectedSlotId(fixture));
            TryResolveCurrentPresentation(
                previousHost,
                out PlayerActorRuntimeHost currentRuntimeHost,
                out GameObject currentPresentation);
            string currentReaderIssue = string.Empty;
            bool hasCurrentReader = TryResolveCurrentGameplayReader(
                previousHost,
                out PlayerGameplayInputReader currentReader,
                out currentReaderIssue);

            Require(result, "actor-replace",
                after.IsJoined &&
                after.Slot.Revision > previousPlayerRevision &&
                after.Slot.SelectionRevision == replacement.CurrentActor.SelectionRevision &&
                after.Slot.SelectedActorProfile == fixture.AlternateActor &&
                after.HasHostEvidence &&
                after.HostEvidence.HostBindingIdentity.Equals(before.HostEvidence.HostBindingIdentity) &&
                string.Equals(
                    after.Preparation.SessionContextId,
                    before.Preparation.SessionContextId,
                    StringComparison.Ordinal) &&
                afterObservation.SessionRevision >= previousSessionRevision &&
                afterObservation.ActivityOccurrence == previousActivityOccurrence &&
                after.IsLogicalActorPrepared &&
                after.IsPhysicallyMaterialized &&
                after.CurrentActor.HasCurrentActor &&
                after.Preparation.Token == replacement.CurrentActor.Token &&
                after.Preparation.Token != previousActorToken &&
                after.HasGameplayAdmissionEvidence &&
                after.GameplayAdmission.GameplayReady &&
                after.GameplayAdmission.Token == replacement.CurrentGameplay.Token &&
                after.GameplayAdmission.Token != previousGameplayToken &&
                after.GameplayAdmission.InputBindingToken ==
                    replacement.CurrentGameplay.InputBindingToken &&
                after.GameplayAdmission.InputBindingToken != previousInputBinding,
                "same P1 occurrence/session/activity with Alternate Actor and new Gameplay admission",
                $"occurrence={after.Slot.Revision} selectionRevision={after.Slot.SelectionRevision} sessionRevision={afterObservation.SessionRevision} activityOccurrence={afterObservation.ActivityOccurrence} actor={DescribeObject(after.Slot.SelectedActorProfile)} gameplay={after.GameplayAdmission.ToDiagnosticString()}",
                "Prepared Actor replacement changed scope ownership or failed to recollect the committed B-side evidence.");
            if (!result.Ok)
            {
                yield break;
            }

            Require(result, "actor-replace",
                previousHost != null &&
                previousHost.IsJoined &&
                ReferenceEquals(previousHost, result.PlayerOneHost) &&
                ReferenceEquals(previousHost.PlayerInput, previousPlayerInput) &&
                currentRuntimeHost != null &&
                !ReferenceEquals(currentRuntimeHost, previousRuntimeHost) &&
                currentPresentation != null &&
                !ReferenceEquals(currentPresentation, previousPresentation) &&
                Vector3.Distance(currentPresentation.transform.position, replacementPose) < 0.0001f &&
                Quaternion.Angle(currentPresentation.transform.rotation, replacementRotation) < 0.001f &&
                Vector3.Distance(currentRuntimeHost.transform.position, previousRuntimeHostPosition) < 0.0001f &&
                Quaternion.Angle(currentRuntimeHost.transform.rotation, previousRuntimeHostRotation) < 0.001f &&
                hasCurrentReader &&
                currentReader != null &&
                !ReferenceEquals(currentReader, previousReader) &&
                currentReader.HasCurrentGameplayBinding &&
                currentReader.GameplayReady &&
                currentReader.CurrentBindingToken ==
                    after.GameplayAdmission.InputBindingToken &&
                (previousReader == null || !previousReader.HasCurrentGameplayBinding),
                "same LocalPlayerHost/PlayerInput with replaced Runtime Host, pose-preserved Presentation and current reader",
                currentReaderIssue,
                "ADR-024 must preserve the current Presentation world pose on B without using the Runtime Host as a spatial body.");
            if (!result.Ok)
            {
                yield break;
            }

            bool p2Available = TryFindSlot(
                fixture,
                ExpectedSlotTwoId(fixture),
                out PlayerSessionScopedSlotObservation p2) &&
                !p2.IsJoined;
            bool joiningOpen = afterObservation.Participation != null &&
                afterObservation.Participation.JoiningOpen;
            Require(result, "actor-replace",
                p2Available && joiningOpen,
                "P2 available and Joining open after prepared Actor replacement",
                $"p2Available={p2Available} joiningOpen={joiningOpen}",
                "ADR-024 replacement must not consume P2 membership or mutate Joining policy before the P2 lifecycle phase.");
            if (result.Ok)
            {
                result.CurrentGameplayReader = currentReader;
            }
        }
        private static IEnumerator ProveActorLifecycle(PlayerQaPanel fixture, Result result)
        {
            var trigger = fixture.RelocateActivityTrigger;
            Require(result, "actor-lifecycle",
                trigger != null &&
                fixture.RelocateActivity != null &&
                ReferenceEquals(trigger.TargetActivity, fixture.RelocateActivity),
                "Relocate Activity trigger",
                trigger != null && trigger.TargetActivity != null
                    ? trigger.TargetActivity.ActivityName
                    : "missing",
                "Actor lifecycle proof requires the dedicated Relocate Activity trigger.");
            if (!result.Ok)
            {
                yield break;
            }

            trigger.RequestActivity();
            yield return WaitFor(
                result,
                "actor-lifecycle",
                () => !trigger.IsRequestInFlight &&
                      (trigger.LastRequestSucceeded ||
                       trigger.LastRequestFailed ||
                       trigger.LastRequestIgnored),
                "Timed out waiting for the dedicated Actor lifecycle Activity request.");
            if (!result.Ok)
            {
                yield break;
            }

            Require(result, "actor-lifecycle",
                trigger.LastRequestSucceeded,
                "Relocate Activity request succeeded",
                $"outcome={trigger.LastOutcome} message={trigger.LastMessage}",
                "Dedicated Actor lifecycle Activity request did not succeed.");
            if (!result.Ok)
            {
                yield break;
            }

            yield return WaitForSlot(
                result,
                "actor-lifecycle",
                fixture,
                slot => slot.IsJoined &&
                        slot.Slot.SelectedActorProfile == fixture.DefaultActor &&
                        slot.IsLogicalActorPrepared &&
                        slot.IsPhysicallyMaterialized &&
                        slot.CurrentActor.HasCurrentActor &&
                         slot.HasHostEvidence &&
                         slot.HostEvidence.AssignmentOrigin ==
                             PlayerSlotAssignmentOrigin.ManagerProvisioned);
            if (!result.Ok)
            {
                yield break;
            }

            Transform relocationAnchor = null;
            IReadOnlyList<ActivityPlayerRelocationAuthoring.Binding> relocationBindings =
                fixture.Relocation != null ? fixture.Relocation.Bindings : null;
            if (relocationBindings != null)
            {
                for (int index = 0; index < relocationBindings.Count; index++)
                {
                    ActivityPlayerRelocationAuthoring.Binding binding = relocationBindings[index];
                    if (binding != null && binding.Activity == fixture.RelocateActivity &&
                        binding.PlayerSlotProfile == fixture.PlayerOneSlot)
                    {
                        relocationAnchor = binding.RelocationAnchor;
                        break;
                    }
                }
            }

            Require(result, "actor-lifecycle", relocationAnchor != null,
                "P1 relocation anchor", "missing",
                "Actor lifecycle requires the authored Relocate Activity anchor.");
            if (!result.Ok)
            {
                yield break;
            }

            yield return WaitFor(
                result,
                "actor-lifecycle",
                () => TryResolveCurrentPresentation(
                          result.PlayerOneHost,
                          out PlayerActorRuntimeHost currentRuntimeHost,
                          out GameObject currentPresentation) &&
                      Vector3.Distance(currentPresentation.transform.position, relocationAnchor.position) < 0.0001f &&
                      Quaternion.Angle(currentPresentation.transform.rotation, relocationAnchor.rotation) < 0.001f &&
                      Vector3.Distance(currentRuntimeHost.transform.position, relocationAnchor.position) >= 0.0001f,
                "Timed out waiting for the prepared Presentation to converge on the Activity relocation pose.");
            if (!result.Ok)
            {
                yield break;
            }

            TryResolveCurrentPresentation(
                result.PlayerOneHost,
                out PlayerActorRuntimeHost runtimeHost,
                out GameObject presentation);
            Require(result, "actor-lifecycle",
                relocationAnchor != null && runtimeHost != null && presentation != null &&
                Vector3.Distance(presentation.transform.position, relocationAnchor.position) < 0.0001f &&
                Quaternion.Angle(presentation.transform.rotation, relocationAnchor.rotation) < 0.001f &&
                Vector3.Distance(runtimeHost.transform.position, relocationAnchor.position) >= 0.0001f,
                "Activity relocation applies to exact Presentation, not Runtime Host",
                $"anchor={DescribeObject(relocationAnchor)} presentation={DescribeObject(presentation)} runtimeHost={DescribeObject(runtimeHost)}",
                "Activity relocation must move the exact Presentation spatial root and leave the generic Runtime Host outside the relocation target.");
            if (!result.Ok)
            {
                yield break;
            }

            yield return ProveActivityRelocationAfterRejoin(
                fixture, result, runtimeHost, presentation, relocationAnchor);
        }

        private static IEnumerator ProveActivityRelocationAfterRejoin(
            PlayerQaPanel fixture,
            Result result,
            PlayerActorRuntimeHost previousRuntimeHost,
            GameObject previousPresentation,
            Transform relocationAnchor)
        {
            const string caseId = "actor-lifecycle";
            if (!TryGetAccess(fixture, out IPlayerSessionScopedAccess access, out string issue))
            {
                result.Fail(caseId, "available Route-scoped access", issue, issue);
                yield break;
            }

            Require(result, caseId,
                access.TryGetObservation(out PlayerSessionScopedObservationSnapshot before) &&
                before != null && before.IsAvailable && before.HasCurrentActivityOccurrence,
                "current Activity occurrence before Leave", access.Snapshot.Diagnostic,
                "Relocation Rejoin proof requires an immutable Activity baseline.");
            if (!result.Ok)
            {
                yield break;
            }

            PlayerSlotId slotId = ExpectedSlotId(fixture);
            PlayerSessionScopedSlotObservation previous = FindSlot(before, slotId);
            LocalPlayerHostAuthoring previousHost = result.PlayerOneHost;
            Vector3 anchorPosition = relocationAnchor.position;
            Quaternion anchorRotation = relocationAnchor.rotation;
            Require(result, caseId,
                previous.IsJoined && previous.IsLogicalActorPrepared &&
                previous.IsPhysicallyMaterialized && previous.HasHostEvidence &&
                previousHost != null && previousRuntimeHost != null && previousPresentation != null,
                "prepared P1 occurrence and physical baseline", before.Diagnostic,
                "Capture the current exact P1 occurrence before Leave.");
            if (!result.Ok)
            {
                yield break;
            }

            SessionPlayerLeaveResult leave = access.RequestLeave(
                new SessionPlayerLeaveRequest(
                    slotId, previous.Slot.Revision, Source, "player-qa-relocation-leave"));
            Require(result, caseId, leave != null && leave.Succeeded,
                "exact P1 Leave succeeded",
                leave == null ? "null" : $"{leave.Status} {leave.Message}",
                "Relocation evidence retirement must follow the public Session Player Leave lifecycle.");
            if (!result.Ok)
            {
                yield break;
            }

            yield return WaitFor(result, caseId, () =>
                TryFindSlot(fixture, slotId, out PlayerSessionScopedSlotObservation released) &&
                released.Slot.AllocationState == PlayerSlotAllocationState.Available &&
                !released.Slot.ReservationToken.IsValid &&
                !released.IsJoined && !released.HasHostEvidence &&
                !released.HasInputOwnershipEvidence &&
                !released.IsLogicalActorPrepared && !released.IsPhysicallyMaterialized &&
                !released.HasGameplayAdmissionEvidence && previousPresentation == null &&
                previousRuntimeHost == null && previousHost == null,
                "Leave did not release P1, its prepared Actor, Presentation and Runtime Host.");
            if (!result.Ok)
            {
                yield break;
            }

            result.PlayerOneHost = null;
            PlayerParticipationOperationResult open = access.OpenJoining(
                Source, "player-qa-relocation-rejoin-open");
            Require(result, caseId,
                open != null && open.Completed && open.Snapshot != null && open.Snapshot.JoiningOpen,
                "joining open", open == null ? "null" : $"{open.Status} {open.Message}",
                "Relocation Rejoin requires the public joining policy.");
            if (!result.Ok)
            {
                yield break;
            }

            Require(result, caseId,
                fixture.Probe.TryGetJoinAccess(out ILocalPlayerJoinAccess joinAccess, out string joinIssue) &&
                joinAccess != null,
                "public Join access", joinIssue, "Relocation Rejoin requires the authored Join surface.");
            if (!result.Ok)
            {
                yield break;
            }

            LocalPlayerJoinResult join = joinAccess.RequestJoin(
                new LocalPlayerJoinRequest(Source, "player-qa-relocation-rejoin"));
            Require(result, caseId,
                join != null && join.Succeeded && join.Slot.PlayerSlotId == slotId &&
                join.Slot.Revision > previous.Slot.Revision &&
                join.HasLocalPlayerHostEvidence && join.LocalPlayerHost != null,
                "fresh P1 occurrence in the same stable Slot",
                join == null ? "null" : $"{join.Status} revision={join.Slot.Revision} {join.Message}",
                "Rejoin must create a newer P1 occurrence through the public Join authority.");
            if (!result.Ok)
            {
                yield break;
            }

            result.PlayerOneHost = join.LocalPlayerHost;
            yield return WaitForSlot(result, caseId, fixture, slot => slot.IsJoined && slot.HasHostEvidence);
            if (!result.Ok)
            {
                yield break;
            }

            Require(result, caseId,
                TryFindSlot(fixture, slotId, out PlayerSessionScopedSlotObservation fresh),
                "fresh P1 observation", "missing", "Rejoin observation is unavailable.");
            if (!result.Ok)
            {
                yield break;
            }

            if (fresh.Slot.SelectedActorProfile != fixture.DefaultActor)
            {
                PlayerActorSelectionResult selection = access.RequestSelectDefaultActor(
                    slotId, fresh.Slot.SelectionRevision, Source, "player-qa-relocation-rejoin-default-actor");
                Require(result, caseId, selection != null && selection.Succeeded,
                    "Default Actor selected",
                    selection == null ? "null" : $"{selection.Status} {selection.Message}",
                    "Rejoined P1 must restore its Actor through public selection.");
                if (!result.Ok)
                {
                    yield break;
                }
            }

            yield return WaitFor(result, caseId, () =>
                access.TryGetObservation(out PlayerSessionScopedObservationSnapshot converged) &&
                converged != null && converged.IsAvailable &&
                converged.HasCurrentActivityOccurrence &&
                converged.ActivityOwner == before.ActivityOwner &&
                converged.ActivityOccurrence == before.ActivityOccurrence &&
                TryFindSlot(converged, slotId, out PlayerSessionScopedSlotObservation slot) &&
                slot.IsJoined && slot.Slot.SelectedActorProfile == fixture.DefaultActor &&
                slot.Slot.Revision > previous.Slot.Revision &&
                slot.IsLogicalActorPrepared && slot.IsPhysicallyMaterialized &&
                slot.CurrentActor.HasCurrentActor && slot.HasHostEvidence &&
                slot.HostEvidence.AssignmentToken != previous.HostEvidence.AssignmentToken &&
                TryResolveCurrentPresentation(
                    result.PlayerOneHost,
                    out PlayerActorRuntimeHost currentRuntimeHost,
                    out GameObject currentPresentation) &&
                !ReferenceEquals(currentRuntimeHost, previousRuntimeHost) &&
                !ReferenceEquals(currentPresentation, previousPresentation) &&
                Vector3.Distance(currentPresentation.transform.position, anchorPosition) < 0.0001f &&
                Quaternion.Angle(currentPresentation.transform.rotation, anchorRotation) < 0.001f &&
                Vector3.Distance(currentRuntimeHost.transform.position, anchorPosition) >= 0.0001f,
                "Timed out waiting for the fresh P1 observation and relocated Presentation to converge.");
            if (!result.Ok)
            {
                yield break;
            }

            Require(result, caseId,
                access.TryGetObservation(out PlayerSessionScopedObservationSnapshot after) &&
                after != null && after.IsAvailable && after.HasCurrentActivityOccurrence &&
                after.ActivityOwner == before.ActivityOwner &&
                after.ActivityOccurrence == before.ActivityOccurrence,
                "same Activity owner and occurrence across Leave/Rejoin",
                after != null ? after.Diagnostic : "missing",
                "A new Activity occurrence would mask stale per-Slot relocation evidence.");
            if (!result.Ok)
            {
                yield break;
            }

            PlayerSessionScopedSlotObservation current = FindSlot(after, slotId);
            TryResolveCurrentPresentation(
                result.PlayerOneHost,
                out PlayerActorRuntimeHost runtimeHost,
                out GameObject presentation);
            Require(result, caseId,
                current.IsJoined && current.IsLogicalActorPrepared && current.IsPhysicallyMaterialized &&
                current.Slot.Revision > previous.Slot.Revision && current.HasHostEvidence &&
                current.HostEvidence.AssignmentToken != previous.HostEvidence.AssignmentToken &&
                runtimeHost != null && !ReferenceEquals(runtimeHost, previousRuntimeHost) &&
                presentation != null && !ReferenceEquals(presentation, previousPresentation) &&
                Vector3.Distance(presentation.transform.position, anchorPosition) < 0.0001f &&
                Quaternion.Angle(presentation.transform.rotation, anchorRotation) < 0.001f &&
                Vector3.Distance(runtimeHost.transform.position, anchorPosition) >= 0.0001f,
                "fresh P1 Presentation relocated within the retained Activity occurrence",
                $"activityOccurrence={after.ActivityOccurrence} previousRevision={previous.Slot.Revision} " +
                $"currentRevision={current.Slot.Revision} presentation={DescribeObject(presentation)} " +
                $"runtimeHost={DescribeObject(runtimeHost)} anchor={anchorPosition}",
                "The ended P1 occurrence must not suppress relocation of its successor; Runtime Host is not the spatial body.");
        }

        private static IEnumerator ProveGameplayReadyReader(
            PlayerQaPanel fixture,
            Result result)
        {
            ActivityRequestTrigger trigger = fixture.GameplayReadyActivityTrigger;
            Require(result, "gameplay-ready-reader",
                trigger != null &&
                fixture.GameplayReadyActivity != null &&
                ReferenceEquals(trigger.TargetActivity, fixture.GameplayReadyActivity),
                "GameplayReady Activity trigger",
                trigger != null && trigger.TargetActivity != null
                    ? trigger.TargetActivity.ActivityName
                    : "missing",
                "Player QA requires its dedicated GameplayReady Activity trigger.");
            if (!result.Ok)
            {
                yield break;
            }

            yield return RequestActivity(
                result,
                "gameplay-ready-reader",
                trigger,
                expectSuccess: true,
                "GameplayReady Activity request did not complete successfully.");
            if (!result.Ok)
            {
                yield break;
            }

            EmitGameplayReadyReaderTopologyDiagnostic(result);

            yield return WaitFor(
                result,
                "gameplay-ready-reader",
                () => TryResolveCurrentGameplayReader(
                          result.PlayerOneHost,
                          out PlayerGameplayInputReader reader,
                          out _) &&
                      reader.HasCurrentGameplayBinding &&
                      TryFindSlot(
                          fixture,
                          ExpectedSlotId(fixture),
                          out PlayerSessionScopedSlotObservation slot) &&
                      slot.Slot.SelectedActorProfile == fixture.DefaultActor &&
                      slot.HasGameplayAdmissionEvidence &&
                      slot.GameplayAdmission.IsAdmitted &&
                      reader.CurrentBindingToken == slot.GameplayAdmission.InputBindingToken,
                DescribeGameplayReadyReaderFailure(fixture, result));
            if (!result.Ok)
            {
                yield break;
            }

            string readerIssue = string.Empty;
            Require(result, "gameplay-ready-reader",
                TryResolveCurrentGameplayReader(
                    result.PlayerOneHost,
                    out PlayerGameplayInputReader reader,
                    out readerIssue) &&
                reader.gameObject.GetComponent<PlayerActorDeclaration>() == null &&
                TryFindSlot(
                    fixture,
                    ExpectedSlotId(fixture),
                    out PlayerSessionScopedSlotObservation slot) &&
                reader.HasCurrentGameplayBinding &&
                reader.CurrentBindingToken.IsValid &&
                reader.CurrentBindingToken == slot.GameplayAdmission.InputBindingToken &&
                reader.GameplayReady == slot.GameplayAdmission.GameplayReady,
                "current Default Presentation gameplay reader",
                readerIssue,
                DescribeGameplayReadyReaderFailure(fixture, result));
            if (result.Ok)
            {
                result.CurrentGameplayReader = reader;
            }
        }

        private static IEnumerator ProveReaderCardinality(PlayerQaPanel fixture, Result result)
        {
            Require(result, "reader-cardinality",
                fixture.NoGameplayReaderActor != null &&
                fixture.DefaultActor != null &&
                fixture.AmbiguousGameplayReaderActor != null,
                "zero, one and ambiguous Actor Profiles",
                "missing",
                "Reader cardinality requires the three authored Actor fixtures.");
            if (!result.Ok)
            {
                yield break;
            }

            // A suíte chega com a ocorrência canônica em GameplayReady.
            // Ela termina antes da cardinalidade, pois downgrade de Activity é apenas contextual.
            yield return LeaveReaderCardinalityOccurrence(
                fixture, result, "initial-occurrence");
            if (!result.Ok)
            {
                yield break;
            }

            yield return RunFreshReaderCardinalityFixture(
                fixture, result, "zero", fixture.NoGameplayReaderActor, 0,
                expectGameplayReady: true, leaveAfterProof: true);
            if (!result.Ok)
            {
                yield break;
            }

            yield return RunFreshReaderCardinalityFixture(
                fixture, result, "one", fixture.DefaultActor, 1,
                expectGameplayReady: true, leaveAfterProof: true);
            if (!result.Ok)
            {
                yield break;
            }

            yield return RunFreshReaderCardinalityFixture(
                fixture, result, "ambiguous", fixture.AmbiguousGameplayReaderActor, 2,
                expectGameplayReady: false, leaveAfterProof: true);
            if (!result.Ok)
            {
                yield break;
            }

            // Preserva a pré-condição da prova separada de actor-replace sem alterá-la
            // nem tratar replacement como mecanismo de cardinalidade.
            yield return RunFreshReaderCardinalityFixture(
                fixture, result, "actor-replace-precondition", fixture.DefaultActor, 1,
                expectGameplayReady: true, leaveAfterProof: false);
        }

        private static IEnumerator RunFreshReaderCardinalityFixture(
            PlayerQaPanel fixture,
            Result result,
            string fixtureName,
            ActorProfile actor,
            int expectedReaderCount,
            bool expectGameplayReady,
            bool leaveAfterProof)
        {
            if (!TryGetAccess(fixture, out IPlayerSessionScopedAccess access, out string issue))
            {
                result.Fail("reader-cardinality", "available Route-scoped access", issue, issue);
                yield break;
            }

            ActivityRequestTrigger startupTrigger = fixture.RelocateActivityTrigger;
            Require(result, "reader-cardinality",
                fixture.StartupActivity != null && fixture.StartupActivity.HasValidActivityId &&
                fixture.StartupActivity.PlayerParticipationRequirementLevel ==
                    PlayerParticipationRequirementLevel.JoinedSlots &&
                startupTrigger != null && !startupTrigger.IsRequestInFlight,
                "Startup Activity requiring only JoinedSlots and idle Activity trigger",
                "missing, invalid or busy",
                "Reader cardinality requires a stable explicit-selection context before Join.");
            if (!result.Ok)
            {
                yield break;
            }

            RuntimeContentOwner startupOwner = RuntimeContentOwner.Activity(
                fixture.StartupActivity.ActivityId.StableText,
                fixture.StartupActivity.ActivityName,
                RuntimeDefinitionToken.FromUnityObject(fixture.StartupActivity));
            var previousTarget = startupTrigger.TargetActivity;
            try
            {
                startupTrigger.TargetActivity = fixture.StartupActivity;
                yield return RequestActivity(result, "reader-cardinality", startupTrigger,
                    expectSuccess: true,
                    $"Startup Activity did not complete before the {fixtureName} occurrence Join.");
                if (!result.Ok)
                {
                    yield break;
                }

                yield return WaitFor(result, "reader-cardinality", () =>
                    access.TryGetObservation(out PlayerSessionScopedObservationSnapshot startup) &&
                    startup != null && startup.IsAvailable && startup.HasCurrentActivityOccurrence &&
                    startup.ActivityOwner == startupOwner,
                    "Startup Activity is not the current Activity before the fresh occurrence Join.");
            }
            finally
            {
                startupTrigger.TargetActivity = previousTarget;
            }

            if (!result.Ok)
            {
                yield break;
            }

            PlayerParticipationOperationResult open = access.OpenJoining(
                Source, $"player-qa-reader-cardinality-{fixtureName}-open-joining");
            Require(result, "reader-cardinality",
                open != null && open.Completed && open.Snapshot != null && open.Snapshot.JoiningOpen,
                "joining open before fresh occurrence",
                open == null ? "null" : $"{open.Status} {open.Message}",
                "Reader cardinality could not establish the public joining policy for a fresh Player occurrence.");
            string joinIssue = string.Empty;
            if (!result.Ok || !fixture.Probe.TryGetJoinAccess(
                    out ILocalPlayerJoinAccess joinAccess, out joinIssue) ||
                joinAccess == null)
            {
                if (result.Ok)
                {
                    result.Fail("reader-cardinality", "ILocalPlayerJoinAccess", joinIssue,
                        "Fresh Player occurrence requires the authored Route-scoped join access.");
                }

                yield break;
            }

            LocalPlayerJoinResult join = joinAccess.RequestJoin(
                new LocalPlayerJoinRequest(
                    Source, $"player-qa-reader-cardinality-{fixtureName}-join"));
            Require(result, "reader-cardinality",
                join != null && join.Succeeded &&
                join.Slot.PlayerSlotId == ExpectedSlotId(fixture) &&
                join.LocalPlayerHost != null && join.HasLocalPlayerHostEvidence,
                "fresh joined P1 occurrence with Host evidence",
                join == null ? "null" : $"{join.Status} {join.Message}",
                "Fresh Player occurrence did not join the canonical P1 Slot.");
            if (!result.Ok)
            {
                yield break;
            }

            result.PlayerOneHost = join.LocalPlayerHost;
            result.CurrentGameplayReader = null;
            yield return WaitFor(result, "reader-cardinality", () =>
                TryFindSlot(fixture, ExpectedSlotId(fixture), out PlayerSessionScopedSlotObservation fresh) &&
                fresh.IsJoined &&
                fresh.Slot.Revision == join.Slot.Revision &&
                !fresh.Slot.HasSelectedActor &&
                !fresh.IsLogicalActorPrepared &&
                !fresh.IsPhysicallyMaterialized &&
                join.LocalPlayerHost != null && join.LocalPlayerHost.ActorMount != null &&
                join.LocalPlayerHost.ActorMount.GetComponentsInChildren<PlayerActorRuntimeHost>(true).Length == 0,
                "Fresh Player occurrence did not reach mutable selection with no prepared Actor or Runtime Host.");
            if (!result.Ok || !TryFindSlot(
                    fixture, ExpectedSlotId(fixture), out PlayerSessionScopedSlotObservation mutable))
            {
                yield break;
            }

            PlayerActorSelectionResult selection = access.RequestSelectActorProfile(
                new PlayerActorSelectionRequest(
                    ExpectedSlotId(fixture), actor, Source,
                    $"player-qa-reader-cardinality-{fixtureName}-select",
                    mutable.Slot.SelectionRevision));
            Require(result, "reader-cardinality",
                selection != null && selection.Succeeded && selection.StateChanged &&
                selection.Slot.SelectedActorProfile == actor,
                $"{fixtureName} initial Actor selection before preparation",
                selection == null ? "null" : $"{selection.Status} {selection.Message}",
                "A fresh unselected Player occurrence must use SelectActorProfile before preparation.");
            if (!result.Ok)
            {
                yield break;
            }

            yield return RequestActivity(result, "reader-cardinality", fixture.RelocateActivityTrigger,
                expectSuccess: true,
                $"Relocate Activity did not prepare the {fixtureName} Actor through the canonical lifecycle.");
            yield return WaitFor(result, "reader-cardinality", () =>
                HasPreparedReaderCardinality(
                    fixture, result.PlayerOneHost, actor, expectedReaderCount),
                $"Timed out preparing the {fixtureName} Presentation.");
            if (!result.Ok)
            {
                yield break;
            }

            yield return RequestActivity(result, "reader-cardinality", fixture.GameplayReadyActivityTrigger,
                expectSuccess: true,
                expectGameplayReady
                    ? $"GameplayReady Activity rejected the valid {fixtureName} Presentation."
                    : "GameplayReady Activity did not commit the ambiguous Presentation target.");
            if (!result.Ok)
            {
                yield break;
            }

            if (expectGameplayReady)
            {
                yield return WaitFor(result, "reader-cardinality", () =>
                    HasGameplayReaderCardinality(
                        fixture, result.PlayerOneHost, actor, expectedReaderCount,
                        expectedAdmission: true) &&
                    (expectedReaderCount != 1 ||
                     HasBoundCurrentGameplayReader(fixture, result, actor)),
                    $"Timed out admitting the {fixtureName} Presentation under GameplayReady authority.");
            }
            else
            {
                AmbiguousReaderCardinalityEvidence evidence =
                    CaptureAmbiguousReaderCardinalityEvidence(
                        fixture, result.PlayerOneHost, actor);
                Require(result, "reader-cardinality",
                    evidence.IsSatisfied,
                    "committed GameplayReady Activity with Player reader-cardinality blocking evidence",
                    evidence.Diagnostic,
                    "Ambiguous cardinality did not produce the required committed Activity, Player participant and unbound-reader evidence.");
                EmitAmbiguousReaderCardinalityEvidence(fixture, evidence);
            }
            if (!result.Ok)
            {
                yield break;
            }

            if (expectGameplayReady && expectedReaderCount == 1 &&
                TryResolveCurrentGameplayReader(
                    result.PlayerOneHost, out PlayerGameplayInputReader currentReader, out _))
            {
                result.CurrentGameplayReader = currentReader;
                Require(result, "reader-cardinality",
                    !result.LastReleasedGameplayBinding.IsValid ||
                    currentReader.CurrentBindingToken != result.LastReleasedGameplayBinding,
                    "fresh occurrence gameplay binding token",
                    currentReader.CurrentBindingToken.StableText,
                    "A fresh Player occurrence reused the gameplay binding token released by its predecessor.");
            }

            Require(result, "reader-cardinality",
                !expectGameplayReady || expectedReaderCount != 0 ||
                HasNoBoundCurrentGameplayReader(result.PlayerOneHost),
                "zero-reader Presentation admitted without a current reader binding",
                result.CurrentGameplayReader == null ? "no-current-reader" : result.CurrentGameplayReader.Diagnostic,
                "A zero-reader Presentation retained gameplay input authority.");
            EmitReaderCardinalityDiagnostic(
                fixtureName, actor, join, selection, result,
                DescribeActivityRequestOutcome(fixture.GameplayReadyActivityTrigger),
                leaveOutcome: leaveAfterProof ? "pending" : "not-requested");
            if (leaveAfterProof)
            {
                yield return LeaveReaderCardinalityOccurrence(
                    fixture, result, fixtureName,
                    verifyAllReaderLeaks: !expectGameplayReady);
                if (!expectGameplayReady && result.Ok)
                {
                    Require(result, "reader-cardinality",
                        HasNoP1GameplayAdmissionOrInputToken(fixture),
                        "ambiguous P1 gameplay admission and input token released",
                        DescribeP1GameplayAdmission(fixture),
                        "Public Leave left P1 gameplay admission evidence or an input token after the ambiguous occurrence ended.");
                }
            }
        }

        private static IEnumerator LeaveReaderCardinalityOccurrence(
            PlayerQaPanel fixture,
            Result result,
            string phase,
            bool verifyAllReaderLeaks = false)
        {
            if (!TryGetAccess(fixture, out IPlayerSessionScopedAccess access, out string issue) ||
                !TryFindSlot(fixture, ExpectedSlotId(fixture), out PlayerSessionScopedSlotObservation current) ||
                !current.IsJoined)
            {
                result.Fail("reader-cardinality", "current joined P1 occurrence",
                    string.IsNullOrEmpty(issue) ? "P1 unavailable" : issue,
                    "Reader cardinality can only reset through the public Leave operation for the current occurrence.");
                yield break;
            }

            LocalPlayerHostAuthoring previousHost = result.PlayerOneHost;
            PlayerActorRuntimeHost previousRuntimeHost = null;
            GameObject previousPresentation = null;
            if (previousHost != null && previousHost.ActorMount != null)
            {
                PlayerActorRuntimeHost[] runtimeHosts = previousHost.ActorMount
                    .GetComponentsInChildren<PlayerActorRuntimeHost>(true);
                if (runtimeHosts.Length == 1)
                {
                    previousRuntimeHost = runtimeHosts[0];
                    if (previousRuntimeHost != null &&
                        previousRuntimeHost.PresentationMount != null &&
                        previousRuntimeHost.PresentationMount.childCount == 1)
                    {
                        previousPresentation = previousRuntimeHost.PresentationMount
                            .GetChild(0).gameObject;
                    }
                }
            }

            PlayerGameplayInputReader previousReader = result.CurrentGameplayReader;
            PlayerGameplayInputBindingToken previousBinding = previousReader != null
                ? previousReader.CurrentBindingToken
                : default;
            PlayerGameplayInputReader[] previousReaders = Array.Empty<PlayerGameplayInputReader>();
            string readerCaptureIssue = "previous Host missing";
            bool capturedPreviousReaders = verifyAllReaderLeaks && previousHost != null &&
                TryGetCurrentGameplayReaders(previousHost, out previousReaders, out readerCaptureIssue);
            SessionPlayerLeaveResult leave = access.RequestLeave(
                new SessionPlayerLeaveRequest(
                    ExpectedSlotId(fixture), current.Slot.Revision, Source,
                    $"player-qa-reader-cardinality-{phase}-leave"));
            Require(result, "reader-cardinality", leave != null && leave.Succeeded,
                $"{phase} Leave succeeded",
                leave == null ? "null" : $"{leave.Status} {leave.Message}",
                "Fresh Player occurrence teardown must use the public Session Player Leave authority.");
            if (!result.Ok)
            {
                yield break;
            }

            yield return WaitFor(result, "reader-cardinality", () =>
                TryFindSlot(fixture, ExpectedSlotId(fixture), out PlayerSessionScopedSlotObservation released) &&
                released.Slot.AllocationState == PlayerSlotAllocationState.Available &&
                !released.Slot.ReservationToken.IsValid &&
                !released.IsJoined &&
                !released.HasHostEvidence &&
                !released.HasInputOwnershipEvidence &&
                !released.IsLogicalActorPrepared &&
                !released.IsPhysicallyMaterialized &&
                !released.HasGameplayAdmissionEvidence &&
                (previousReader == null || !previousReader.HasCurrentGameplayBinding) &&
                (!verifyAllReaderLeaks ||
                 (capturedPreviousReaders && HaveReleasedGameplayReaders(previousReaders))) &&
                previousRuntimeHost == null &&
                previousPresentation == null &&
                previousHost == null,
                $"Public Leave did not release the {phase} Actor, Runtime Host, Presentation and gameplay binding.");
            if (verifyAllReaderLeaks)
            {
                Require(result, "reader-cardinality",
                    capturedPreviousReaders && HaveReleasedGameplayReaders(previousReaders) &&
                    (!previousBinding.IsValid || previousReader == null ||
                     !previousReader.HasCurrentGameplayBinding),
                    $"{phase} Actor, Presentation and gameplay reader bindings released",
                    capturedPreviousReaders
                        ? $"readerCount={previousReaders.Length} " +
                          $"currentReader={(previousReader == null ? "destroyed" : previousReader.Diagnostic)}"
                        : readerCaptureIssue,
                    "Public Leave left an Actor/Presentation reader binding, or the canonical readers could not be captured for leak verification.");
            }
            else
            {
                Require(result, "reader-cardinality",
                    !previousBinding.IsValid || previousReader == null ||
                    !previousReader.HasCurrentGameplayBinding,
                    $"{phase} gameplay binding released",
                    previousReader == null ? "destroyed" : previousReader.Diagnostic,
                    "A previous Player occurrence binding remained authoritative after Leave.");
            }
            if (result.Ok)
            {
                if (previousBinding.IsValid)
                {
                    result.LastReleasedGameplayBinding = previousBinding;
                }

                result.PreviousReaderOccurrenceReleased = true;
                result.PlayerOneHost = null;
                result.CurrentGameplayReader = null;
            }
        }

        private static bool HasPreparedReaderCardinality(
            PlayerQaPanel fixture,
            LocalPlayerHostAuthoring host,
            ActorProfile expectedActor,
            int expectedReaderCount)
        {
            return TryFindSlot(
                       fixture,
                       ExpectedSlotId(fixture),
                       out PlayerSessionScopedSlotObservation slot) &&
                   slot.Slot.SelectedActorProfile == expectedActor &&
                   slot.IsLogicalActorPrepared &&
                   slot.IsPhysicallyMaterialized &&
                   TryGetCurrentGameplayReaderCount(
                       host,
                       out int readerCount,
                       out _) &&
                   readerCount == expectedReaderCount;
        }

        private static bool HasGameplayReaderCardinality(
            PlayerQaPanel fixture,
            LocalPlayerHostAuthoring host,
            ActorProfile expectedActor,
            int expectedReaderCount,
            bool expectedAdmission)
        {
            return TryFindSlot(
                       fixture,
                       ExpectedSlotId(fixture),
                       out PlayerSessionScopedSlotObservation slot) &&
                   slot.Slot.SelectedActorProfile == expectedActor &&
                   slot.HasGameplayAdmissionEvidence &&
                   slot.GameplayAdmission.IsAdmitted == expectedAdmission &&
                   TryGetCurrentGameplayReaderCount(
                       host,
                       out int readerCount,
                       out _) &&
                   readerCount == expectedReaderCount;
        }

        private static bool HasBoundCurrentGameplayReader(
            PlayerQaPanel fixture,
            Result result,
            ActorProfile expectedActor)
        {
            return TryResolveCurrentGameplayReader(
                       result.PlayerOneHost,
                       out PlayerGameplayInputReader reader,
                       out _) &&
                   reader.HasCurrentGameplayBinding &&
                   TryFindSlot(
                       fixture,
                       ExpectedSlotId(fixture),
                       out PlayerSessionScopedSlotObservation slot) &&
                   slot.Slot.SelectedActorProfile == expectedActor &&
                   slot.HasGameplayAdmissionEvidence &&
                   slot.GameplayAdmission.IsAdmitted &&
                   reader.CurrentBindingToken == slot.GameplayAdmission.InputBindingToken;
        }

        private static bool HasNoBoundCurrentGameplayReader(LocalPlayerHostAuthoring host)
        {
            if (!TryGetCurrentGameplayReaders(
                    host,
                    out PlayerGameplayInputReader[] readers,
                    out _))
            {
                return false;
            }

            for (int index = 0; index < readers.Length; index++)
            {
                if (readers[index] != null && readers[index].HasCurrentGameplayBinding)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HaveReleasedGameplayReaders(
            PlayerGameplayInputReader[] readers)
        {
            if (readers == null)
            {
                return false;
            }

            for (int index = 0; index < readers.Length; index++)
            {
                PlayerGameplayInputReader reader = readers[index];
                if (reader != null && reader.HasCurrentGameplayBinding)
                {
                    return false;
                }
            }

            return true;
        }

        private sealed class AmbiguousReaderCardinalityEvidence
        {
            internal bool RequestSucceeded;
            internal bool ConfiguredTarget;
            internal bool GameplayReadyPlayerParticipationConfigured;
            internal bool CanonicalPresentationExists;
            internal bool P1SlotFound;
            internal bool P1SelectedActorMatches;
            internal bool GameplayAdmissionAbsent;
            internal bool InputTokenAbsent;
            internal int ReaderCount;
            internal int CurrentBoundReaderCount;
            internal string ActivityRequestOutcome;
            internal string ReaderTopology;

            // O Framework não expõe o estado interno do participante de Activity nesta superfície.
            // A QA prova apenas as consequências públicas estáveis da cardinalidade ambígua.
            internal bool IsSatisfied =>
                RequestSucceeded &&
                ConfiguredTarget &&
                GameplayReadyPlayerParticipationConfigured &&
                CanonicalPresentationExists &&
                P1SlotFound &&
                P1SelectedActorMatches &&
                GameplayAdmissionAbsent &&
                InputTokenAbsent &&
                ReaderCount == 2 &&
                CurrentBoundReaderCount == 0;

            internal string Diagnostic =>
                $"requestSucceeded='{RequestSucceeded}' configuredTarget='{ConfiguredTarget}' " +
                $"gameplayReadyPlayerParticipationConfigured='{GameplayReadyPlayerParticipationConfigured}' " +
                $"canonicalPresentationExists='{CanonicalPresentationExists}' " +
                $"p1SlotFound='{P1SlotFound}' p1SelectedActorMatches='{P1SelectedActorMatches}' " +
                $"gameplayAdmissionAbsent='{GameplayAdmissionAbsent}' inputTokenAbsent='{InputTokenAbsent}' " +
                $"readerCount='{ReaderCount}' currentBoundReaderCount='{CurrentBoundReaderCount}' " +
                $"readerTopology='{ReaderTopology}'";
        }

        private static AmbiguousReaderCardinalityEvidence
            CaptureAmbiguousReaderCardinalityEvidence(
                PlayerQaPanel fixture,
                LocalPlayerHostAuthoring host,
                ActorProfile expectedActor)
        {
            ActivityRequestTrigger trigger = fixture != null
                ? fixture.GameplayReadyActivityTrigger
                : null;
            var evidence = new AmbiguousReaderCardinalityEvidence
            {
                ActivityRequestOutcome = trigger != null ? trigger.LastOutcome.ToString() : "missing",
                ReaderTopology = "missing",
                ReaderCount = -1,
                CurrentBoundReaderCount = -1,
                RequestSucceeded = trigger != null && trigger.LastRequestSucceeded,
                ConfiguredTarget = fixture != null &&
                    fixture.GameplayReadyActivity != null &&
                    fixture.GameplayReadyActivity.HasValidActivityId &&
                    trigger != null &&
                    ReferenceEquals(trigger.TargetActivity, fixture.GameplayReadyActivity)
            };

            evidence.GameplayReadyPlayerParticipationConfigured = fixture != null &&
                fixture.GameplayReadyActivity != null &&
                fixture.GameplayReadyActivity.PlayerParticipationRequirementLevel ==
                    PlayerParticipationRequirementLevel.GameplayReady;

            evidence.P1SlotFound = TryFindSlot(
                fixture, ExpectedSlotId(fixture), out PlayerSessionScopedSlotObservation slot);
            evidence.P1SelectedActorMatches = evidence.P1SlotFound &&
                slot.Slot.SelectedActorProfile == expectedActor;
            evidence.GameplayAdmissionAbsent = evidence.P1SlotFound &&
                !slot.HasGameplayAdmissionEvidence;
            evidence.InputTokenAbsent = evidence.GameplayAdmissionAbsent &&
                !slot.GameplayAdmission.InputBindingToken.IsValid;

            if (TryGetCurrentGameplayReaders(
                    host,
                    out PlayerGameplayInputReader[] readers,
                    out string readerTopology))
            {
                evidence.CanonicalPresentationExists = true;
                evidence.ReaderTopology = readerTopology;
                evidence.ReaderCount = readers.Length;
                evidence.CurrentBoundReaderCount = 0;
                for (int index = 0; index < readers.Length; index++)
                {
                    if (readers[index] != null && readers[index].HasCurrentGameplayBinding)
                    {
                        evidence.CurrentBoundReaderCount++;
                    }
                }
            }
            else
            {
                evidence.ReaderTopology = readerTopology;
            }

            return evidence;
        }

        private static void EmitAmbiguousReaderCardinalityEvidence(
            PlayerQaPanel fixture,
            AmbiguousReaderCardinalityEvidence evidence)
        {
            Debug.Log(
                "[QA_PLAYER_AMBIGUOUS_EVIDENCE] " +
                $"predicateSatisfied='{evidence.IsSatisfied}' " +
                $"requestSucceeded='{evidence.RequestSucceeded}' " +
                $"configuredTarget='{evidence.ConfiguredTarget}' " +
                $"gameplayReadyPlayerParticipationConfigured='{evidence.GameplayReadyPlayerParticipationConfigured}' " +
                $"canonicalPresentationExists='{evidence.CanonicalPresentationExists}' " +
                $"p1SlotFound='{evidence.P1SlotFound}' " +
                $"p1SelectedActorMatches='{evidence.P1SelectedActorMatches}' " +
                $"gameplayAdmissionAbsent='{evidence.GameplayAdmissionAbsent}' " +
                $"inputTokenAbsent='{evidence.InputTokenAbsent}' " +
                $"readerCountAmbiguous='{evidence.ReaderCount == 2}' " +
                $"noReaderCurrentBound='{evidence.CurrentBoundReaderCount == 0}' " +
                $"activityRequestOutcome='{evidence.ActivityRequestOutcome}' " +
                $"readerCount='{evidence.ReaderCount}' " +
                $"currentBoundReaderCount='{evidence.CurrentBoundReaderCount}' " +
                $"readerTopology='{EscapeDiagnosticValue(evidence.ReaderTopology)}'",
                fixture);
        }

        private static bool HasNoP1GameplayAdmissionOrInputToken(PlayerQaPanel fixture)
        {
            return TryFindSlot(
                       fixture,
                       ExpectedSlotId(fixture),
                       out PlayerSessionScopedSlotObservation slot) &&
                   !slot.HasGameplayAdmissionEvidence &&
                   !slot.GameplayAdmission.InputBindingToken.IsValid;
        }

        private static string DescribeP1GameplayAdmission(PlayerQaPanel fixture)
        {
            if (!TryFindSlot(
                    fixture,
                    ExpectedSlotId(fixture),
                    out PlayerSessionScopedSlotObservation slot))
            {
                return "P1 slot missing";
            }

            return $"hasGameplayAdmission='{slot.HasGameplayAdmissionEvidence}' " +
                   $"inputTokenValid='{slot.GameplayAdmission.InputBindingToken.IsValid}'";
        }


        private static string EscapeDiagnosticValue(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("'", "\\'")
                .Replace("\r", " ")
                .Replace("\n", " ");
        }

        private static void EmitReaderCardinalityDiagnostic(
            string fixtureName,
            ActorProfile actor,
            LocalPlayerJoinResult join,
            PlayerActorSelectionResult selection,
            Result result,
            string gameplayProjectionOutcome,
            string leaveOutcome)
        {
            LocalPlayerHostAuthoring host = result != null ? result.PlayerOneHost : null;
            PlayerActorRuntimeHost runtimeHost = null;
            GameObject presentation = null;
            PlayerGameplayInputReader boundReader = null;
            int readerCount = -1;
            if (host != null && host.ActorMount != null)
            {
                PlayerActorRuntimeHost[] runtimeHosts = host.ActorMount
                    .GetComponentsInChildren<PlayerActorRuntimeHost>(true);
                if (runtimeHosts.Length == 1)
                {
                    runtimeHost = runtimeHosts[0];
                    if (runtimeHost != null && runtimeHost.PresentationMount != null &&
                        runtimeHost.PresentationMount.childCount == 1)
                    {
                        presentation = runtimeHost.PresentationMount.GetChild(0).gameObject;
                    }
                }

                if (TryGetCurrentGameplayReaders(host, out PlayerGameplayInputReader[] readers, out _))
                {
                    readerCount = readers.Length;
                    for (int index = 0; index < readers.Length; index++)
                    {
                        if (readers[index] != null && readers[index].HasCurrentGameplayBinding)
                        {
                            boundReader = readers[index];
                            break;
                        }
                    }
                }
            }

            Debug.Log(
                $"[QA_PLAYER_READER_CARDINALITY] fixture='{fixtureName}' " +
                $"actor='{DescribeObject(actor)}' " +
                $"playerOccurrence='{(join != null ? join.Slot.Revision.ToString() : "unavailable")}' " +
                $"joinOutcome='{(join != null ? join.Status.ToString() : "not-requested")}' " +
                "selectionOperation='SelectActorProfile' " +
                $"selectionOutcome='{(selection != null ? selection.Status.ToString() : "not-requested")}' " +
                $"preparedActor='{(runtimeHost != null)}' " +
                $"runtimeHost='{DescribeObject(runtimeHost)}' " +
                $"presentation='{DescribeObject(presentation)}' " +
                $"readerCount='{readerCount}' " +
                $"boundReader='{DescribeObject(boundReader)}' " +
                $"stalePreviousReaderBound='{(result != null && !result.PreviousReaderOccurrenceReleased)}' " +
                $"gameplayProjectionOutcome='{gameplayProjectionOutcome}' " +
                $"leaveOutcome='{leaveOutcome}'.");
        }


        private static string DescribeActivityRequestOutcome(ActivityRequestTrigger trigger)
        {
            return trigger == null
                ? "missing"
                : trigger.LastOutcome.ToString();
        }

        private static IEnumerator ProveSecondPlayer(PlayerQaPanel fixture, Result result)
        {
            if (!TryFindSlot(fixture, ExpectedSlotId(fixture), out PlayerSessionScopedSlotObservation p1) ||
                !p1.IsJoined)
            {
                result.Fail(
                    "second-player",
                    "joined P1 before initial P2 Join",
                    p1.Slot.PlayerSlotId.IsValid
                        ? p1.Slot.AllocationState.ToString()
                        : "missing",
                    "Second-player provisioning requires the canonical retained P1 occurrence.");
                yield break;
            }

            if (fixture.PlayerTwoSlot == null)
            {
                result.Fail("second-player", "P2 slot", "null", "Player QA requires the P2 Slot Profile.");
                yield break;
            }

            if (!fixture.Probe.TryGetJoinAccess(out ILocalPlayerJoinAccess joinAccess, out string joinIssue))
            {
                result.Fail("second-player", "join access", joinIssue, joinIssue);
                yield break;
            }

            if (TryFindSlot(fixture, ExpectedSlotTwoId(fixture), out PlayerSessionScopedSlotObservation existing) &&
                existing.IsJoined)
            {
                result.Fail(
                    "second-player",
                    "P2 available before second Join",
                    existing.Slot.AllocationState.ToString(),
                    "Second-player proof cannot certify a Join that was already consumed by an earlier case.");
                yield break;
            }

            result.MembershipKeyboard = InputSystem.AddDevice<Keyboard>();
            Keyboard membershipKeyboard = result.MembershipKeyboard;
            Require(result, "second-player",
                membershipKeyboard != null && membershipKeyboard.added,
                "distinct explicit QA Keyboard device",
                "missing",
                "Second-player provisioning requires its own device; P1's current device must not be shared.");
            if (!result.Ok)
            {
                yield break;
            }

            LocalPlayerJoinResult join = joinAccess.RequestJoin(
                new LocalPlayerJoinRequest(
                    Source,
                    "player-qa-join-p2",
                    membershipKeyboard));
            Require(result, "second-player", join != null && join.Succeeded &&
                join.HasLocalPlayerHostEvidence && join.LocalPlayerHost != null &&
                join.PlayerInput != null,
                "P2 joined",
                join == null ? "null" : $"{join.Status} {join.Message}",
                "Second-player RequestJoin failed.");
            if (!result.Ok)
            {
                yield break;
            }

            Require(result, "second-player",
                join.Slot.PlayerSlotId == ExpectedSlotTwoId(fixture) &&
                join.Slot.PlayerSlotId != ExpectedSlotId(fixture),
                "distinct P2 slot",
                join.Slot.PlayerSlotId.IsValid ? join.Slot.PlayerSlotId.StableText : "invalid",
                "Second join did not allocate the distinct P2 Slot.");
            yield return WaitForSlot(
                result,
                "second-player",
                fixture,
                slot => slot.IsJoined && slot.HasHostEvidence &&
                        !slot.IsLogicalActorPrepared && !slot.IsPhysicallyMaterialized,
                ExpectedSlotTwoId(fixture));
        }

        private static IEnumerator ProveJoiningControl(PlayerQaPanel fixture, Result result)
        {
            if (!TryGetAccess(fixture, out IPlayerSessionScopedAccess access, out string issue))
            {
                result.Fail("joining-control", "available access", issue, issue);
                yield break;
            }

            if (!TryFindSlot(
                    fixture,
                    ExpectedSlotTwoId(fixture),
                    out PlayerSessionScopedSlotObservation occupiedP2) ||
                !occupiedP2.IsJoined)
            {
                result.Fail(
                    "joining-control",
                    "joined P2 from second-player",
                    occupiedP2.Slot.PlayerSlotId.IsValid
                        ? occupiedP2.Slot.AllocationState.ToString()
                        : "missing",
                    "Joining control must consume the explicit P2 occurrence created by second-player.");
                yield break;
            }

            SessionPlayerLeaveResult prepareLeave = access.RequestLeave(
                new SessionPlayerLeaveRequest(
                    ExpectedSlotTwoId(fixture),
                    occupiedP2.Slot.Revision,
                    Source,
                    "player-qa-prepare-joining-control"));
            Require(result, "joining-control",
                prepareLeave != null && prepareLeave.Succeeded,
                "P2 available before JoiningClosed proof",
                prepareLeave == null
                    ? "null"
                    : $"{prepareLeave.Status} {prepareLeave.Message}",
                "Joining control could not release its owned P2 occurrence.");
            if (!result.Ok)
            {
                yield break;
            }

            yield return WaitFor(
                result,
                "joining-control",
                () => TryFindSlot(
                          fixture,
                          ExpectedSlotTwoId(fixture),
                          out PlayerSessionScopedSlotObservation availableP2) &&
                      availableP2.Slot.AllocationState == PlayerSlotAllocationState.Available &&
                      !availableP2.Slot.ReservationToken.IsValid &&
                      !availableP2.IsJoined &&
                      !availableP2.HasHostEvidence &&
                      !availableP2.HasInputOwnershipEvidence,
                "Timed out releasing P2 before JoiningClosed proof.");

            PlayerParticipationOperationResult closed = access.CloseJoining(Source, "player-qa-close-joining");
            Require(result, "joining-control", closed != null && closed.Completed,
                "joining closed",
                closed == null ? "null" : $"{closed.Status} {closed.Message}",
                "CloseJoining failed.");
            if (!result.Ok)
            {
                yield break;
            }

            Require(result, "joining-control",
                fixture.Probe.TryGetJoinAccess(
                    out ILocalPlayerJoinAccess joinAccess,
                    out string joinIssue) &&
                joinAccess != null,
                "join access",
                joinIssue,
                "ILocalPlayerJoinAccess is unavailable during JoiningClosed proof.");
            if (!result.Ok)
            {
                yield break;
            }

            LocalPlayerJoinResult rejected = joinAccess.RequestJoin(
                new LocalPlayerJoinRequest(Source, "player-qa-join-while-closed"));
            Require(result, "joining-control",
                rejected != null &&
                rejected.Status == LocalPlayerJoinStatus.RejectedJoiningClosed,
                "RejectedJoiningClosed",
                rejected == null ? "null" : rejected.Status.ToString(),
                "Join while closed was rejected for a reason other than JoiningClosed.");
            if (!result.Ok)
            {
                yield break;
            }

            PlayerParticipationOperationResult opened = access.OpenJoining(Source, "player-qa-open-joining");
            Require(result, "joining-control", opened != null && opened.Completed,
                "joining opened",
                opened == null ? "null" : $"{opened.Status} {opened.Message}",
                "OpenJoining failed.");
            if (!result.Ok)
            {
                yield break;
            }

            Keyboard membershipKeyboard = result.MembershipKeyboard;
            Require(result, "joining-control",
                membershipKeyboard != null && membershipKeyboard.added,
                "explicit QA Keyboard device for P2 restoration",
                "missing",
                "Joining control must restore the P2 occurrence consumed to prove JoiningClosed.");
            if (!result.Ok)
            {
                yield break;
            }

            LocalPlayerJoinResult restored = joinAccess.RequestJoin(
                new LocalPlayerJoinRequest(
                    Source,
                    "player-qa-restore-p2-after-joining-control",
                    membershipKeyboard));
            Require(result, "joining-control",
                restored != null && restored.Succeeded &&
                restored.Slot.PlayerSlotId == ExpectedSlotTwoId(fixture),
                "P2 restored after JoiningClosed proof",
                restored == null ? "null" : $"{restored.Status} {restored.Message}",
                "Joining control must restore the P2 lifecycle continuity required by the public Leave proof.");
            if (!result.Ok)
            {
                yield break;
            }

            yield return WaitForSlot(
                result,
                "joining-control",
                fixture,
                slot => slot.IsJoined && slot.HasHostEvidence,
                ExpectedSlotTwoId(fixture));
            if (!result.Ok || !access.TryGetObservation(
                    out PlayerSessionScopedObservationSnapshot restoredObservation) ||
                restoredObservation == null ||
                restoredObservation.Participation == null ||
                !restoredObservation.Participation.JoiningOpen)
            {
                if (result.Ok)
                {
                    result.Fail(
                        "joining-control",
                        "Joining open after P2 restoration",
                        "closed or observation unavailable",
                        "Joining control must restore both the P2 occurrence and open Joining for the following Leave proof.");
                }

                yield break;
            }
        }

        private static IEnumerator ProveCommands(PlayerQaPanel fixture, Result result)
        {
            Require(result, "commands",
                fixture.JoinCommand != null &&
                fixture.LeaveCommand != null &&
                fixture.SelectActorCommand != null &&
                fixture.DefaultActorCommand != null &&
                fixture.ReplaceActorCommand != null &&
                fixture.ClearActorCommand != null &&
                fixture.OpenJoiningCommand != null &&
                fixture.CloseJoiningCommand != null,
                "all command triggers", "missing",
                "Player QA scene is missing one or more official Player Session command triggers.");
            if (!result.Ok)
            {
                yield break;
            }

            Require(result, "commands",
                fixture.JoinCommand.TryValidateConfiguration(out string joinIssue),
                "join command valid", joinIssue, joinIssue);
            Require(result, "commands",
                fixture.LeaveCommand.TryValidateConfiguration(out string leaveIssue),
                "leave command valid", leaveIssue, leaveIssue);
            Require(result, "commands",
                fixture.SelectActorCommand.TryValidateConfiguration(out string selectIssue),
                "select-actor command valid", selectIssue, selectIssue);
            Require(result, "commands",
                fixture.DefaultActorCommand.TryValidateConfiguration(out string defaultIssue),
                "default-actor command valid", defaultIssue, defaultIssue);
            Require(result, "commands",
                fixture.ReplaceActorCommand.TryValidateConfiguration(out string replaceIssue),
                "replace-actor command valid", replaceIssue, replaceIssue);
            Require(result, "commands",
                fixture.ClearActorCommand.TryValidateConfiguration(out string clearIssue),
                "clear-actor command valid", clearIssue, clearIssue);
            Require(result, "commands",
                fixture.OpenJoiningCommand.TryValidateConfiguration(out string openJoiningIssue),
                "open-joining command valid", openJoiningIssue, openJoiningIssue);
            Require(result, "commands",
                fixture.CloseJoiningCommand.TryValidateConfiguration(out string closeJoiningIssue),
                "close-joining command valid", closeJoiningIssue, closeJoiningIssue);
            if (!result.Ok)
            {
                yield break;
            }

            bool p2Joined = TryFindSlot(
                fixture,
                ExpectedSlotTwoId(fixture),
                out PlayerSessionScopedSlotObservation p2) &&
                p2.IsJoined && p2.HasHostEvidence;
            bool joiningOpen = TryGetAccess(
                    fixture,
                    out IPlayerSessionScopedAccess access,
                    out _) &&
                access.TryGetObservation(out PlayerSessionScopedObservationSnapshot observation) &&
                observation != null && observation.Participation != null &&
                observation.Participation.JoiningOpen;
            Require(result, "commands",
                p2Joined && joiningOpen,
                "configuration-only commands preserve P2 Joined and Joining open",
                $"p2Joined={p2Joined} joiningOpen={joiningOpen}",
                "Command configuration validation must not mutate Player membership or Joining policy before Leave.");
            yield return null;
        }

        private static IEnumerator ProveLeave(PlayerQaPanel fixture, Result result)
        {
            if (!TryGetAccess(fixture, out IPlayerSessionScopedAccess access, out string issue))
            {
                result.Fail("leave", "available access", issue, issue);
                yield break;
            }

            if (!TryFindSlot(fixture, ExpectedSlotTwoId(fixture), out PlayerSessionScopedSlotObservation p2) ||
                !p2.IsJoined)
            {
                result.Fail(
                    "leave",
                    "joined P2 before Leave",
                    p2.Slot.PlayerSlotId.IsValid
                        ? p2.Slot.AllocationState.ToString()
                        : "missing",
                    "Leave proof requires the P2 produced by the second-player case.");
                yield break;
            }

            SessionPlayerLeaveResult leave = access.RequestLeave(
                new SessionPlayerLeaveRequest(
                    ExpectedSlotTwoId(fixture),
                    p2.Slot.Revision,
                    Source,
                    "player-qa-leave-p2"));
            Require(result, "leave", leave != null && leave.Succeeded,
                "P2 left",
                leave == null ? "null" : $"{leave.Status} {leave.Message}",
                "RequestLeave for P2 failed.");
            if (!result.Ok)
            {
                yield break;
            }

            yield return WaitFor(
                result,
                "leave",
                () => TryFindSlot(fixture, ExpectedSlotTwoId(fixture), out PlayerSessionScopedSlotObservation after) &&
                      after.Slot.AllocationState == PlayerSlotAllocationState.Available &&
                      !after.Slot.ReservationToken.IsValid &&
                      !after.IsJoined &&
                      !after.Slot.HasSelectedActor &&
                      !after.HasHostEvidence &&
                      !after.HasInputOwnershipEvidence &&
                      !after.IsLogicalActorPrepared &&
                      !after.IsPhysicallyMaterialized &&
                      !after.HasGameplayAdmissionEvidence,
                "Timed out waiting for P2 to leave.");
        }

        private static IEnumerator ProveRejoin(PlayerQaPanel fixture, Result result)
        {
            if (!TryGetAccess(fixture, out IPlayerSessionScopedAccess access, out string accessIssue))
            {
                result.Fail("rejoin", "available access", accessIssue, accessIssue);
                yield break;
            }

            if (!fixture.Probe.TryGetJoinAccess(out ILocalPlayerJoinAccess joinAccess, out string joinIssue))
            {
                result.Fail("rejoin", "join access", joinIssue, joinIssue);
                yield break;
            }

            if (!TryFindSlot(fixture, ExpectedSlotTwoId(fixture), out PlayerSessionScopedSlotObservation existing) ||
                existing.IsJoined)
            {
                result.Fail(
                    "rejoin",
                    "P2 available after Leave",
                    existing.Slot.AllocationState.ToString(),
                    "Rejoin proof requires Leave to have released P2 first.");
                yield break;
            }

            Keyboard membershipKeyboard = result.MembershipKeyboard;
            Require(result, "rejoin",
                membershipKeyboard != null && membershipKeyboard.added,
                "explicit QA Keyboard device",
                "missing",
                "Rejoin must reuse P2's distinct device only after its previous occurrence Leaves.");
            if (!result.Ok)
            {
                yield break;
            }

            int releasedRevision = existing.Slot.Revision;
            LocalPlayerJoinResult join = joinAccess.RequestJoin(
                new LocalPlayerJoinRequest(
                    Source,
                    "player-qa-rejoin-p2",
                    membershipKeyboard));
            Require(result, "rejoin", join != null && join.Succeeded,
                "P2 rejoined",
                join == null ? "null" : $"{join.Status} {join.Message}",
                "Rejoin after Leave failed.");
            yield return WaitForSlot(
                result,
                "rejoin",
                fixture,
                slot => slot.IsJoined &&
                        slot.HasHostEvidence &&
                        slot.Slot.Revision > releasedRevision &&
                        !slot.Slot.HasSelectedActor &&
                        !slot.IsLogicalActorPrepared &&
                        !slot.IsPhysicallyMaterialized &&
                        !slot.HasGameplayAdmissionEvidence,
                ExpectedSlotTwoId(fixture));
            if (!result.Ok || !TryFindSlot(
                    fixture,
                    ExpectedSlotTwoId(fixture),
                    out PlayerSessionScopedSlotObservation rejoinedP2))
            {
                yield break;
            }

            Require(result, "rejoin",
                rejoinedP2.Slot.Revision > releasedRevision &&
                rejoinedP2.HasHostEvidence &&
                !rejoinedP2.Slot.HasSelectedActor &&
                !rejoinedP2.IsLogicalActorPrepared &&
                !rejoinedP2.IsPhysicallyMaterialized &&
                !rejoinedP2.HasGameplayAdmissionEvidence,
                "fresh unprepared P2 occurrence with new revision",
                $"previousRevision={releasedRevision} currentRevision={rejoinedP2.Slot.Revision}",
                "Rejoin must establish a newer P2 occurrence without stale selection, Actor, Host projection or Gameplay admission.");
            if (!result.Ok)
            {
                yield break;
            }

            SessionPlayerLeaveResult cleanup = access.RequestLeave(
                new SessionPlayerLeaveRequest(
                    ExpectedSlotTwoId(fixture),
                    rejoinedP2.Slot.Revision,
                    Source,
                    "player-qa-rejoin-p2-cleanup"));
            Require(result, "rejoin", cleanup != null && cleanup.Succeeded,
                "rejoined P2 cleanup succeeded",
                cleanup == null ? "null" : $"{cleanup.Status} {cleanup.Message}",
                "Rejoin proof must not leak the temporary P2 occurrence into later cases.");
            if (!result.Ok)
            {
                yield break;
            }

            yield return WaitForSlot(
                result,
                "rejoin",
                fixture,
                slot => slot.Slot.AllocationState == PlayerSlotAllocationState.Available &&
                        !slot.Slot.ReservationToken.IsValid &&
                        !slot.IsJoined &&
                        !slot.HasHostEvidence &&
                        !slot.HasInputOwnershipEvidence &&
                        !slot.IsLogicalActorPrepared &&
                        !slot.IsPhysicallyMaterialized &&
                        !slot.HasCurrentActorEvidence &&
                        !slot.HasGameplayAdmissionEvidence,
                ExpectedSlotTwoId(fixture));
            if (result.Ok)
            {
                RemoveOwnedDevice(result.MembershipKeyboard);
                result.MembershipKeyboard = null;
            }
        }

        private static IEnumerator ProveNegatives(PlayerQaPanel fixture, Result result)
        {
            if (!TryGetAccess(fixture, out IPlayerSessionScopedAccess access, out string issue))
            {
                result.Fail("negatives", "available access", issue, issue);
                yield break;
            }

            if (!TryFindSlot(fixture, ExpectedSlotId(fixture), out PlayerSessionScopedSlotObservation p1) ||
                !p1.IsJoined)
            {
                result.Fail(
                    "negatives",
                    "joined P1 for stale-revision proof",
                    p1.Slot.PlayerSlotId.IsValid
                        ? p1.Slot.AllocationState.ToString()
                        : "missing",
                    "Negative stale-revision proof requires the canonical joined P1.");
                yield break;
            }

            Require(result, "negatives",
                p1.Slot.Revision > 0,
                "positive joined P1 revision",
                p1.Slot.Revision.ToString(),
                "Cannot construct a prior stale revision from the current P1 Slot revision.");
            if (!result.Ok)
            {
                yield break;
            }

            SessionPlayerLeaveResult stale = access.RequestLeave(
                new SessionPlayerLeaveRequest(
                    ExpectedSlotId(fixture),
                    p1.Slot.Revision - 1,
                    Source,
                    "player-qa-stale-leave"));
            Require(result, "negatives", stale != null && !stale.Succeeded,
                "stale leave rejected",
                stale == null ? "null" : $"{stale.Status} {stale.Message}",
                "Stale Leave occurrence revision was not rejected.");
            if (!result.Ok)
            {
                yield break;
            }


            yield return null;
        }

        private static IEnumerator ProveInputOwnership(PlayerQaPanel fixture, Result result)
        {
            var resources = new InputOwnershipResources();
            var proof = new InputOwnershipProof();
            var cleanup = new InputOwnershipCleanup();
            try
            {
                yield return RunInputOwnershipProof(fixture, result, resources, proof);
                yield return CleanupInputOwnership(fixture, resources, cleanup);
                EmitInputOwnershipDiagnostic(fixture, resources, proof, cleanup);
                ApplyInputOwnershipResult(result, proof, cleanup, fixture);
            }
            finally
            {
                LastResortCleanupInputOwnership(fixture, resources);
            }
        }

        private static IEnumerator RunInputOwnershipProof(
            PlayerQaPanel fixture,
            Result result,
            InputOwnershipResources resources,
            InputOwnershipProof proof)
        {
            if (!TryGetAccess(fixture, out IPlayerSessionScopedAccess access, out string accessIssue) ||
                access == null)
            {
                proof.Record("available Session observation", accessIssue, accessIssue);
                yield break;
            }

            if (!access.TryGetObservation(out PlayerSessionScopedObservationSnapshot start) ||
                start == null ||
                !start.IsAvailable)
            {
                proof.Record(
                    "available Session observation",
                    start == null ? "null" : start.Diagnostic,
                    "input-ownership requires the Route-scoped Session observation left by negatives.");
                yield break;
            }

            PlayerSessionScopedSlotObservation p1 = FindSlot(start, ExpectedSlotId(fixture));
            PlayerSessionScopedSlotObservation p2 = FindSlot(start, ExpectedSlotTwoId(fixture));
            bool joiningOpen = start.Participation != null && start.Participation.JoiningOpen;
            bool relocateIdle = fixture.RelocateActivityTrigger == null ||
                !fixture.RelocateActivityTrigger.IsRequestInFlight;
            bool gameplayReadyIdle = fixture.GameplayReadyActivityTrigger == null ||
                !fixture.GameplayReadyActivityTrigger.IsRequestInFlight;
            Keyboard currentKeyboard = Keyboard.current;
            if (!proof.Check(
                    joiningOpen &&
                    p1.IsJoined &&
                    p1.HasHostEvidence &&
                    p1.HostEvidence.AssignmentOrigin ==
                        PlayerSlotAssignmentOrigin.ManagerProvisioned &&
                    p1.Slot.Revision > 0 &&
                    p2.Slot.AllocationState == PlayerSlotAllocationState.Available &&
                    !p2.Slot.ReservationToken.IsValid &&
                    !p2.IsJoined &&
                    !p2.HasHostEvidence &&
                    !p2.HasInputOwnershipEvidence &&
                    relocateIdle &&
                    gameplayReadyIdle &&
                    currentKeyboard != null,
                    "joined Manager-Provisioned P1, available P2, Joining open, idle Activities, Keyboard.current",
                    $"joiningOpen={joiningOpen} p1Joined={p1.IsJoined} p1Host={p1.HasHostEvidence} " +
                    $"p1Origin={(p1.HasHostEvidence ? p1.HostEvidence.AssignmentOrigin.ToString() : "none")} " +
                    $"p1Revision={p1.Slot.Revision} p2Joined={p2.IsJoined} p2Host={p2.HasHostEvidence} " +
                    $"relocateInFlight={!relocateIdle} gameplayReadyInFlight={!gameplayReadyIdle} " +
                    $"keyboard={(currentKeyboard != null)}",
                    "input-ownership must start from the negatives leftover: retained P1, released P2, open Joining and no Activity request in flight."))
            {
                yield break;
            }

            LocalPlayerHostAuthoring leftoverHost = result.PlayerOneHost;
            PlayerGameplayInputReader leftoverReader = result.CurrentGameplayReader;
            SessionPlayerLeaveResult leftoverLeave = access.RequestLeave(
                new SessionPlayerLeaveRequest(
                    ExpectedSlotId(fixture),
                    p1.Slot.Revision,
                    Source,
                    "player-qa-input-ownership-leave-leftover-p1"));
            if (!proof.Check(
                    leftoverLeave != null && leftoverLeave.Succeeded,
                    "leftover P1 Leave succeeded",
                    leftoverLeave == null ? "null" : $"{leftoverLeave.Status} {leftoverLeave.Message}",
                    "input-ownership must publicly Leave the leftover P1 occurrence before creating temporary devices."))
            {
                yield break;
            }

            yield return WaitUntil(
                proof,
                () => TryFindSlot(fixture, ExpectedSlotId(fixture), out PlayerSessionScopedSlotObservation releasedP1) &&
                      TryFindSlot(fixture, ExpectedSlotTwoId(fixture), out PlayerSessionScopedSlotObservation releasedP2) &&
                      releasedP1.Slot.AllocationState == PlayerSlotAllocationState.Available &&
                      !releasedP1.Slot.ReservationToken.IsValid &&
                      !releasedP1.IsJoined &&
                      !releasedP1.HasHostEvidence &&
                      !releasedP1.HasInputOwnershipEvidence &&
                      !releasedP1.HasGameplayAdmissionEvidence &&
                      releasedP2.Slot.AllocationState == PlayerSlotAllocationState.Available &&
                      !releasedP2.Slot.ReservationToken.IsValid &&
                      !releasedP2.IsJoined &&
                      !releasedP2.HasHostEvidence &&
                      !releasedP2.HasInputOwnershipEvidence &&
                      (leftoverHost == null) &&
                      (leftoverReader == null || !leftoverReader.HasCurrentGameplayBinding),
                "Timed out waiting for leftover P1 terminal release before the ADR-025 fixture.");
            if (proof.HasFailure)
            {
                yield break;
            }

            result.PlayerOneHost = null;
            result.CurrentGameplayReader = null;

            ActivityRequestTrigger gameplayReadyTrigger = fixture.GameplayReadyActivityTrigger;
            if (!proof.Check(
                    gameplayReadyTrigger != null,
                    "GameplayReady Activity trigger",
                    "null",
                    "input-ownership requires the existing GameplayReady Activity trigger to clear the leftover current Activity."))
            {
                yield break;
            }

            gameplayReadyTrigger.ClearActivity();
            yield return WaitUntil(
                proof,
                () => !gameplayReadyTrigger.IsRequestInFlight,
                "Timed out waiting for GameplayReady ClearActivity to finish.");
            if (proof.HasFailure)
            {
                yield break;
            }

            yield return WaitUntil(
                proof,
                () => access.TryGetObservation(out PlayerSessionScopedObservationSnapshot cleared) &&
                      cleared != null &&
                      cleared.IsAvailable &&
                      !cleared.HasCurrentActivityOccurrence &&
                      !gameplayReadyTrigger.IsRequestInFlight,
                "Timed out waiting for GameplayReady to stop being the current Activity occurrence.");
            if (proof.HasFailure)
            {
                yield break;
            }

            if (!proof.Check(
                    gameplayReadyTrigger.LastRequestSucceeded ||
                    gameplayReadyTrigger.LastRequestIgnored,
                    "ClearActivity succeeded or already inactive",
                    $"outcome={gameplayReadyTrigger.LastOutcome} message={gameplayReadyTrigger.LastMessage}",
                    "ClearActivity must leave no current Activity occurrence for the ADR-025 GameplayReady re-enter."))
            {
                yield break;
            }

            resources.OriginalKeyboard = Keyboard.current;
            if (!proof.Check(
                    resources.OriginalKeyboard != null,
                    "Keyboard.current before temporary devices",
                    "null",
                    "Temporary Keyboards cannot be created without the Editor Keyboard remaining distinct from the fixture devices."))
            {
                yield break;
            }

            resources.DeviceA = InputSystem.AddDevice<Keyboard>();
            resources.DeviceB = InputSystem.AddDevice<Keyboard>();
            if (!proof.Check(
                    resources.DeviceA != null &&
                    resources.DeviceB != null &&
                    resources.DeviceA.added &&
                    resources.DeviceB.added &&
                    !ReferenceEquals(resources.DeviceA, resources.DeviceB) &&
                    resources.DeviceA.deviceId != resources.DeviceB.deviceId &&
                    !ReferenceEquals(resources.DeviceA, resources.OriginalKeyboard) &&
                    !ReferenceEquals(resources.DeviceB, resources.OriginalKeyboard),
                    "two distinct added temporary Keyboards",
                    $"deviceA={(resources.DeviceA != null ? resources.DeviceA.deviceId.ToString() : "null")} " +
                    $"deviceB={(resources.DeviceB != null ? resources.DeviceB.deviceId.ToString() : "null")} " +
                    $"original={(resources.OriginalKeyboard != null ? resources.OriginalKeyboard.deviceId.ToString() : "null")}",
                    "ADR-025 fixture requires two retained temporary Keyboard devices that are not Keyboard.current."))
            {
                yield break;
            }

            if (!proof.Check(
                    fixture.JoinCommand != null,
                    "existing Join command trigger",
                    "null",
                    "input-ownership must use the authored PlayerSessionJoinCommandTrigger."))
            {
                yield break;
            }

            yield return ProveInputOwnershipNegatives(fixture, resources, proof);
            if (proof.HasFailure)
            {
                yield break;
            }

            yield return ProveInputOwnershipJoins(fixture, resources, proof);
            if (proof.HasFailure)
            {
                yield break;
            }

            yield return ProveInputOwnershipGameplayIsolation(fixture, resources, proof);
            if (proof.HasFailure)
            {
                yield break;
            }

            yield return ProveInputOwnershipLeaveRejoin(fixture, access, resources, proof);
        }

        private static IEnumerator ProveInputOwnershipNegatives(
            PlayerQaPanel fixture,
            InputOwnershipResources resources,
            InputOwnershipProof proof)
        {
            int invocationBeforeNull = fixture.JoinCommand.InvocationCount;
            fixture.JoinCommand.InvokeFromDevice(null);
            if (!IsRejectedInvalidDeviceJoin(fixture.JoinCommand) ||
                fixture.JoinCommand.InvocationCount <= invocationBeforeNull)
            {
                LocalPlayerJoinResult nullJoin = fixture.JoinCommand.LastJoinResult;
                proof.Record(
                    "RejectedInvalidRequest",
                    nullJoin == null
                        ? $"outcome={fixture.JoinCommand.LastOutcome}"
                        : $"{nullJoin.Status} {nullJoin.Message}",
                    "InvokeFromDevice(null) must reject without joining a Slot.");
                yield break;
            }

            if (!proof.Check(
                    !AnyJoinedOwnership(fixture),
                    "no Joined Slot, Host or ownership after null device Join",
                    DescribeJoinedOwnership(fixture),
                    "Rejected explicit-device Join must not create a Player, Host or ownership evidence."))
            {
                yield break;
            }

            resources.RemovedProbe = InputSystem.AddDevice<Keyboard>();
            if (!proof.Check(
                    resources.RemovedProbe != null &&
                    resources.RemovedProbe.added &&
                    resources.RemovedProbe.deviceId != resources.DeviceA.deviceId &&
                    resources.RemovedProbe.deviceId != resources.DeviceB.deviceId,
                    "third temporary Keyboard for removed-device rejection",
                    resources.RemovedProbe == null ? "null" : resources.RemovedProbe.deviceId.ToString(),
                    "Removed-device Join rejection requires a probe Keyboard that is not deviceA or deviceB."))
            {
                yield break;
            }

            InputSystem.RemoveDevice(resources.RemovedProbe);
            if (!proof.Check(
                    !resources.RemovedProbe.added,
                    "removed probe Keyboard",
                    "still added",
                    "InvokeFromDevice(removedDevice) requires the probe to be removed from the Input System first."))
            {
                yield break;
            }

            int invocationBeforeRemoved = fixture.JoinCommand.InvocationCount;
            fixture.JoinCommand.InvokeFromDevice(resources.RemovedProbe);
            if (!IsRejectedInvalidDeviceJoin(fixture.JoinCommand) ||
                fixture.JoinCommand.InvocationCount <= invocationBeforeRemoved)
            {
                LocalPlayerJoinResult removedJoin = fixture.JoinCommand.LastJoinResult;
                proof.Record(
                    "RejectedInvalidRequest",
                    removedJoin == null
                        ? $"outcome={fixture.JoinCommand.LastOutcome}"
                        : $"{removedJoin.Status} {removedJoin.Message}",
                    "InvokeFromDevice(removedDevice) must reject without fallback Join.");
                yield break;
            }

            proof.Check(
                !AnyJoinedOwnership(fixture),
                "no Joined Slot, Host or ownership after removed-device Join",
                DescribeJoinedOwnership(fixture),
                "Rejected removed-device Join must not create a Player, Host or ownership evidence.");
            yield return null;
        }

        private static IEnumerator ProveInputOwnershipJoins(
            PlayerQaPanel fixture,
            InputOwnershipResources resources,
            InputOwnershipProof proof)
        {
            if (!TryGetAccess(
                    fixture,
                    out IPlayerSessionScopedAccess access,
                    out string accessIssue))
            {
                proof.Record(
                    "available scoped observation for input ownership Joins",
                    accessIssue,
                    "ADR-025 Join convergence requires the authoritative Route-scoped observation.");
                yield break;
            }

            fixture.JoinCommand.InvokeFromDevice(resources.DeviceA);
            LocalPlayerJoinResult joinA = fixture.JoinCommand.LastJoinResult;
            if (!proof.Check(
                    joinA != null &&
                    joinA.Succeeded &&
                    joinA.Slot.PlayerSlotId.IsValid &&
                    joinA.LocalPlayerHost != null &&
                    joinA.PlayerInput != null,
                    "SucceededJoined from InvokeFromDevice(deviceA)",
                    joinA == null ? "null" : $"{joinA.Status} {joinA.Message}",
                    "Device A must Join through the existing PlayerSessionJoinCommandTrigger."))
            {
                yield break;
            }

            resources.SlotA = joinA.Slot.PlayerSlotId;
            resources.RevisionA = joinA.Slot.Revision;
            resources.HostA = joinA.LocalPlayerHost;
            resources.PlayerInputA = joinA.PlayerInput;
            yield return WaitUntil(
                proof,
                () => access.TryGetObservation(out PlayerSessionScopedObservationSnapshot current) &&
                      current != null && current.IsAvailable &&
                      TryFindSlot(current, resources.SlotA, out PlayerSessionScopedSlotObservation slotA) &&
                      slotA.IsJoined &&
                      slotA.HasHostEvidence &&
                      slotA.HasInputOwnershipEvidence &&
                      OwnershipContainsDevice(slotA.InputOwnership, resources.DeviceA.deviceId) &&
                      TryFindSingleAvailableSlot(
                          current,
                          resources.SlotA,
                          out PlayerSessionScopedSlotObservation availableB) &&
                      availableB.Slot.AllocationState == PlayerSlotAllocationState.Available &&
                      !availableB.Slot.ReservationToken.IsValid &&
                      !availableB.HasHostEvidence &&
                      !availableB.HostEvidence.AssignmentToken.IsValid &&
                      !availableB.HasInputOwnershipEvidence &&
                      availableB.HasPreparationEvidence && availableB.Preparation.IsUnprepared && !availableB.IsLogicalActorPrepared &&
                      !availableB.HasCurrentActorEvidence &&
                      !availableB.HasGameplayAdmissionEvidence &&
                      !availableB.IsPhysicallyMaterialized,
                "Timed out waiting for Slot A Host/device ownership and the current Available Slot B before the duplicate-device proof.");
            if (proof.HasFailure)
            {
                yield break;
            }

            ProveDuplicateInputDeviceRejected(fixture, resources, proof);
            if (proof.HasFailure)
            {
                yield break;
            }

            fixture.JoinCommand.InvokeFromDevice(resources.DeviceB);
            LocalPlayerJoinResult joinB = fixture.JoinCommand.LastJoinResult;
            if (!proof.Check(
                    joinB != null &&
                    joinB.Succeeded &&
                    joinB.Slot.PlayerSlotId.IsValid &&
                    joinB.Slot.PlayerSlotId != resources.SlotA &&
                    joinB.LocalPlayerHost != null &&
                    joinB.PlayerInput != null &&
                    !ReferenceEquals(joinB.LocalPlayerHost, resources.HostA) &&
                    !ReferenceEquals(joinB.PlayerInput, resources.PlayerInputA),
                    "SucceededJoined distinct Slot/Host/PlayerInput from InvokeFromDevice(deviceB)",
                    joinB == null
                        ? "null"
                        : $"{joinB.Status} slot={joinB.Slot.PlayerSlotId.StableText} {joinB.Message}",
                    "Device B must Join a second current Slot through the same Join trigger."))
            {
                yield break;
            }

            resources.SlotB = joinB.Slot.PlayerSlotId;
            resources.RevisionB = joinB.Slot.Revision;
            resources.HostB = joinB.LocalPlayerHost;
            resources.PlayerInputB = joinB.PlayerInput;
            yield return WaitUntil(
                proof,
                () => access.TryGetObservation(out PlayerSessionScopedObservationSnapshot current) &&
                      current != null && current.IsAvailable &&
                      TryFindSlot(current, resources.SlotA, out PlayerSessionScopedSlotObservation slotA) &&
                      TryFindSlot(current, resources.SlotB, out PlayerSessionScopedSlotObservation slotB) &&
                      slotA.IsJoined &&
                      slotA.HasHostEvidence &&
                      slotA.HasInputOwnershipEvidence &&
                      OwnershipContainsDevice(slotA.InputOwnership, resources.DeviceA.deviceId) &&
                      !OwnershipContainsDevice(slotA.InputOwnership, resources.DeviceB.deviceId) &&
                      slotB.IsJoined &&
                      slotB.HasHostEvidence &&
                      slotB.HasInputOwnershipEvidence &&
                      OwnershipContainsDevice(slotB.InputOwnership, resources.DeviceB.deviceId) &&
                      !OwnershipContainsDevice(slotB.InputOwnership, resources.DeviceA.deviceId),
                "Timed out waiting for both joined Slots to expose isolated Host/device ownership.");
            if (proof.HasFailure)
            {
                yield break;
            }

            if (!access.TryGetObservation(out PlayerSessionScopedObservationSnapshot observation) ||
                observation == null)
            {
                proof.Record(
                    "available scoped observation after both Joins",
                    access.Snapshot.Diagnostic,
                    "Could not recollect the converged ADR-025 ownership observation.");
                yield break;
            }

            PlayerSessionScopedSlotObservation observedA = FindSlot(observation, resources.SlotA);
            PlayerSessionScopedSlotObservation observedB = FindSlot(observation, resources.SlotB);
            resources.HostBindingA = observedA.HostEvidence.HostBindingIdentity;
            resources.HostBindingB = observedB.HostEvidence.HostBindingIdentity;
            resources.AssignmentA = observedA.HostEvidence.AssignmentToken;
            resources.AssignmentB = observedB.HostEvidence.AssignmentToken;
            if (!proof.Check(
                    observedA.IsJoined &&
                    observedB.IsJoined &&
                    observedA.HasHostEvidence &&
                    observedB.HasHostEvidence &&
                    observedA.HasInputOwnershipEvidence &&
                    observedB.HasInputOwnershipEvidence,
                    "both current Slots Joined with Host and input ownership evidence",
                    $"aJoined={observedA.IsJoined} aHost={observedA.HasHostEvidence} " +
                    $"aOwnership={observedA.HasInputOwnershipEvidence} bJoined={observedB.IsJoined} " +
                    $"bHost={observedB.HasHostEvidence} bOwnership={observedB.HasInputOwnershipEvidence}",
                    "Public Session observation must expose current input ownership for both ADR-025 Players before gameplay."))
            {
                yield break;
            }

            bool aOwnsA = OwnershipContainsDevice(observedA.InputOwnership, resources.DeviceA.deviceId);
            bool aOwnsB = OwnershipContainsDevice(observedA.InputOwnership, resources.DeviceB.deviceId);
            bool bOwnsB = OwnershipContainsDevice(observedB.InputOwnership, resources.DeviceB.deviceId);
            bool bOwnsA = OwnershipContainsDevice(observedB.InputOwnership, resources.DeviceA.deviceId);
            if (!proof.Check(
                    resources.DeviceA.deviceId != resources.DeviceB.deviceId &&
                    aOwnsA && !aOwnsB && bOwnsB && !bOwnsA,
                    "Slot A owns only deviceA and Slot B owns only deviceB",
                    $"deviceA={resources.DeviceA.deviceId} deviceB={resources.DeviceB.deviceId} " +
                    $"aOwnsA={aOwnsA} aOwnsB={aOwnsB} bOwnsB={bOwnsB} bOwnsA={bOwnsA}",
                    "Public input ownership must isolate DeviceId evidence per current Slot."))
            {
                yield break;
            }

            int indexA = observedA.InputOwnership.UnityPlayerIndex;
            int indexB = observedB.InputOwnership.UnityPlayerIndex;
            resources.PlayerIndexA = indexA;
            resources.PlayerIndexB = indexB;
            if (!proof.Check(
                    indexA != indexB &&
                    resources.PlayerInputA != null &&
                    resources.PlayerInputB != null &&
                    indexA == resources.PlayerInputA.playerIndex &&
                    indexB == resources.PlayerInputB.playerIndex,
                    "distinct Unity playerIndex values matching each Host PlayerInput",
                    $"ownershipA={indexA} ownershipB={indexB} " +
                    $"playerInputA={(resources.PlayerInputA != null ? resources.PlayerInputA.playerIndex.ToString() : "null")} " +
                    $"playerInputB={(resources.PlayerInputB != null ? resources.PlayerInputB.playerIndex.ToString() : "null")}",
                    "Simultaneous ADR-025 Players must expose distinct Unity playerIndex values that match their PlayerInput."))
            {
                yield break;
            }

            string schemeA = EffectiveControlScheme(resources.PlayerInputA);
            string schemeB = EffectiveControlScheme(resources.PlayerInputB);
            proof.Check(
                string.Equals(observedA.InputOwnership.ControlScheme, schemeA, StringComparison.Ordinal) &&
                string.Equals(observedB.InputOwnership.ControlScheme, schemeB, StringComparison.Ordinal),
                "public ControlScheme matches PlayerInput.currentControlScheme",
                $"ownershipA='{observedA.InputOwnership.ControlScheme}' playerInputA='{schemeA}' " +
                $"ownershipB='{observedB.InputOwnership.ControlScheme}' playerInputB='{schemeB}'",
                "Ownership ControlScheme must match the effective PlayerInput scheme, including an empty scheme.");
        }

        private static void ProveDuplicateInputDeviceRejected(
            PlayerQaPanel fixture,
            InputOwnershipResources resources,
            InputOwnershipProof proof)
        {
            if (!TryGetAccess(fixture, out IPlayerSessionScopedAccess access, out string issue) ||
                !access.TryGetObservation(out PlayerSessionScopedObservationSnapshot before) ||
                before == null || !before.IsAvailable)
            {
                proof.Record("current ownership before duplicate Join", issue,
                    "Duplicate-device proof requires an authoritative baseline.");
                return;
            }

            PlayerSessionScopedSlotObservation beforeA = FindSlot(before, resources.SlotA);
            if (!TryFindSingleAvailableSlot(
                    before,
                    resources.SlotA,
                    out PlayerSessionScopedSlotObservation beforeB))
            {
                proof.Record(
                    "exactly one current Available Slot before duplicate Join",
                    before.Diagnostic,
                    "Duplicate-device proof must resolve Slot B from the current public Session snapshot.");
                return;
            }

            resources.SlotB = beforeB.Slot.PlayerSlotId;
            resources.RevisionB = beforeB.Slot.Revision;
            if (!proof.Check(
                    resources.SlotA == ExpectedSlotId(fixture) &&
                    resources.SlotB.IsValid &&
                    beforeA.IsJoined && beforeA.HasHostEvidence && beforeA.HasInputOwnershipEvidence &&
                    OwnershipContainsDevice(beforeA.InputOwnership, resources.DeviceA.deviceId) &&
                    !beforeB.IsJoined &&
                    beforeB.Slot.AllocationState == PlayerSlotAllocationState.Available &&
                    !beforeB.Slot.ReservationToken.IsValid &&
                    !beforeB.HasHostEvidence && !beforeB.HostEvidence.AssignmentToken.IsValid &&
                    !beforeB.HasInputOwnershipEvidence && beforeB.HasPreparationEvidence && beforeB.Preparation.IsUnprepared && !beforeB.IsLogicalActorPrepared &&
                    !beforeB.HasCurrentActorEvidence &&
                    !beforeB.HasGameplayAdmissionEvidence && !beforeB.IsPhysicallyMaterialized &&
                    resources.HostA != null && resources.PlayerInputA != null &&
                    resources.HostA.transform.parent != null,
                    "Device A owned by P1; P2 Available without ownership",
                    before.Diagnostic,
                    "Duplicate-device rejection must be tested before Device B joins."))
            {
                return;
            }

            Transform hostParent = resources.HostA.transform.parent;
            int hostCount = hostParent.GetComponentsInChildren<LocalPlayerHostAuthoring>(true).Length;
            int playerInputCount = PlayerInput.all.Count;
            fixture.JoinCommand.InvokeFromDevice(resources.DeviceA);
            LocalPlayerJoinResult duplicate = fixture.JoinCommand.LastJoinResult;
            if (!proof.Check(
                    duplicate != null && duplicate.Status == LocalPlayerJoinStatus.RejectedDeviceAlreadyOwned &&
                    !duplicate.OperationId.IsValid &&
                    !duplicate.HasReservationEvidence && !duplicate.HasCommitEvidence &&
                    !duplicate.HasRollbackEvidence && !duplicate.HasLocalPlayerHostEvidence &&
                    duplicate.PlayerInput == null &&
                    duplicate.CallbackConfirmation == LocalPlayerJoinCallbackConfirmation.None,
                    "RejectedDeviceAlreadyOwned before reservation/provisioning/callback",
                    duplicate == null ? "null" : duplicate.ToDiagnosticString(),
                    "The same live Device A must not admit another Player."))
            {
                return;
            }

            if (!access.TryGetObservation(out PlayerSessionScopedObservationSnapshot after) ||
                after == null || !after.IsAvailable)
            {
                proof.Record("observation after duplicate rejection", "unavailable",
                    "Duplicate Join must preserve Session observation.");
                return;
            }

            PlayerSessionScopedSlotObservation afterA = FindSlot(after, resources.SlotA);
            PlayerSessionScopedSlotObservation afterB = FindSlot(after, resources.SlotB);
            bool aPreserved = afterA.Slot.Equals(beforeA.Slot) && afterA.IsJoined &&
                afterA.HasHostEvidence && afterA.HasInputOwnershipEvidence &&
                afterA.HostEvidence.HostBindingIdentity.Equals(beforeA.HostEvidence.HostBindingIdentity) &&
                afterA.HostEvidence.AssignmentToken.Equals(beforeA.HostEvidence.AssignmentToken) &&
                resources.HostA != null && resources.HostA.IsJoined &&
                ReferenceEquals(resources.HostA.PlayerInput, resources.PlayerInputA) &&
                SameInputOwnership(beforeA.InputOwnership, afterA.InputOwnership) &&
                OwnershipContainsDevice(afterA.InputOwnership, resources.DeviceA.deviceId);
            bool bPreserved = beforeB.Slot.PlayerSlotId == resources.SlotB &&
                afterB.Slot.PlayerSlotId == resources.SlotB &&
                beforeB.Slot.AllocationState == PlayerSlotAllocationState.Available &&
                afterB.Slot.AllocationState == PlayerSlotAllocationState.Available &&
                afterB.Slot.Revision == beforeB.Slot.Revision &&
                !beforeB.Slot.ReservationToken.IsValid && !afterB.Slot.ReservationToken.IsValid &&
                !beforeB.HasHostEvidence && !afterB.HasHostEvidence &&
                !beforeB.HostEvidence.AssignmentToken.IsValid &&
                !afterB.HostEvidence.AssignmentToken.IsValid &&
                !beforeB.HasInputOwnershipEvidence && !afterB.HasInputOwnershipEvidence &&
                beforeB.HasPreparationEvidence && beforeB.Preparation.IsUnprepared && !beforeB.IsLogicalActorPrepared && afterB.HasPreparationEvidence && afterB.Preparation.IsUnprepared && !afterB.IsLogicalActorPrepared &&
                !beforeB.HasCurrentActorEvidence && !afterB.HasCurrentActorEvidence &&
                !beforeB.HasGameplayAdmissionEvidence && !afterB.HasGameplayAdmissionEvidence &&
                !beforeB.IsPhysicallyMaterialized && !afterB.IsPhysicallyMaterialized;
            bool topologyPreserved = after.SessionRevision == before.SessionRevision &&
                after.Slots.Count == before.Slots.Count &&
                after.ActivityOwner == before.ActivityOwner && after.ActivityOccurrence == before.ActivityOccurrence &&
                after.Participation.JoiningOpen == before.Participation.JoiningOpen &&
                PlayerInput.all.Count == playerInputCount &&
                hostParent.GetComponentsInChildren<LocalPlayerHostAuthoring>(true).Length == hostCount;
            proof.Check(aPreserved && bPreserved && topologyPreserved,
                "A unchanged; B Available; no extra PlayerInput/Host or Slot revision",
                $"slotA='{resources.SlotA.StableText}' slotB='{resources.SlotB.StableText}' " +
                $"revisionA={beforeA.Slot.Revision} revisionB={beforeB.Slot.Revision} " +
                $"aPreserved={aPreserved} bPreserved={bPreserved} topologyPreserved={topologyPreserved} " +
                $"sessionBefore={before.SessionRevision} sessionAfter={after.SessionRevision}",
                "Duplicate-device rejection must not mutate current ownership or reserve the next Slot.");
        }

        private static bool SameInputOwnership(
            LocalPlayerInputOwnershipSummary before,
            LocalPlayerInputOwnershipSummary after)
        {
            if (before.UnityPlayerIndex != after.UnityPlayerIndex ||
                before.ControlScheme != after.ControlScheme || before.Devices.Count != after.Devices.Count)
            {
                return false;
            }

            for (int index = 0; index < before.Devices.Count; index++)
            {
                if (!before.Devices[index].Equals(after.Devices[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private static IEnumerator ProveInputOwnershipGameplayIsolation(
            PlayerQaPanel fixture,
            InputOwnershipResources resources,
            InputOwnershipProof proof)
        {
            ActivityRequestTrigger trigger = fixture.GameplayReadyActivityTrigger;
            if (!proof.Check(
                    trigger != null &&
                    fixture.GameplayReadyActivity != null &&
                    ReferenceEquals(trigger.TargetActivity, fixture.GameplayReadyActivity),
                    "GameplayReady Activity trigger",
                    trigger != null && trigger.TargetActivity != null
                        ? trigger.TargetActivity.ActivityName
                        : "missing",
                    "Two-player ADR-025 preparation must reuse the existing GameplayReady Activity trigger."))
            {
                yield break;
            }

            trigger.RequestActivity();
            yield return WaitUntil(
                proof,
                () => !trigger.IsRequestInFlight &&
                      (trigger.LastRequestSucceeded ||
                       trigger.LastRequestFailed ||
                       trigger.LastRequestIgnored),
                "Timed out waiting for the ADR-025 GameplayReady Activity request.");
            if (proof.HasFailure)
            {
                yield break;
            }

            if (!proof.Check(
                    trigger.LastRequestSucceeded,
                    "GameplayReady Activity request succeeded",
                    $"outcome={trigger.LastOutcome} message={trigger.LastMessage}",
                    "GameplayReady must actually enter for the two ADR-025 Players."))
            {
                CaptureInputOwnershipGameplayReadyRequest(resources, trigger);
                CaptureInputOwnershipSlotGameplayEvidence(fixture, resources);
                EmitInputOwnershipGameplayReadyDiagnostic(fixture, resources);
                yield break;
            }

            CaptureInputOwnershipGameplayReadyRequest(resources, trigger);
            CaptureInputOwnershipSlotGameplayEvidence(fixture, resources);
            if (IsInputOwnershipGameplayReadyBlocked(resources))
            {
                EmitInputOwnershipGameplayReadyDiagnostic(fixture, resources);
            }

            yield return WaitUntil(
                proof,
                () => IsInputOwnershipSlotGameplayReaderReady(
                          fixture, resources.SlotA, resources.HostA) &&
                      IsInputOwnershipSlotGameplayReaderReady(
                          fixture, resources.SlotB, resources.HostB),
                "Timed out waiting for both ADR-025 Slots to become GameplayReady with their configured default Actors. " +
                $"activityReadiness='{DescribeDiagnostic(resources.ActivityReadiness)}' " +
                $"blockingIssueCount='{DescribeDiagnostic(resources.BlockingIssueCount)}' " +
                $"blockingFailureKind='{DescribeDiagnostic(resources.BlockingFailureKind)}' " +
                $"blockingFailureMessage='{DescribeDiagnostic(resources.BlockingFailureMessage)}'.");
            CaptureInputOwnershipSlotGameplayEvidence(fixture, resources);
            if (proof.HasFailure)
            {
                EmitInputOwnershipGameplayReadyDiagnostic(fixture, resources);
                yield break;
            }

            bool resolvedA = TryResolveCurrentGameplayReader(
                resources.HostA, out PlayerGameplayInputReader readerA, out string readerAIssue);
            bool resolvedB = TryResolveCurrentGameplayReader(
                resources.HostB, out PlayerGameplayInputReader readerB, out string readerBIssue);
            if (!resolvedA || !resolvedB)
            {
                proof.Record(
                    "Host-local gameplay reader for each ADR-025 Player",
                    $"readerA='{readerAIssue}' readerB='{readerBIssue}'",
                    "Each ADR-025 Host must resolve exactly one current Presentation reader.");
                yield break;
            }

            resources.ReaderA = readerA;
            resources.ReaderB = readerB;
            if (!TryFindSlot(fixture, resources.SlotA, out PlayerSessionScopedSlotObservation slotA) ||
                !TryFindSlot(fixture, resources.SlotB, out PlayerSessionScopedSlotObservation slotB))
            {
                proof.Record(
                    "current Slot observations after GameplayReady",
                    "missing",
                    "Could not recollect Slot A/B after GameplayReady.");
                yield break;
            }

            resources.BindingA = slotA.GameplayAdmission.InputBindingToken;
            resources.BindingB = slotB.GameplayAdmission.InputBindingToken;
            if (!proof.Check(
                    readerA.HasCurrentGameplayBinding &&
                    readerB.HasCurrentGameplayBinding &&
                    readerA.CurrentBindingToken.IsValid &&
                    readerB.CurrentBindingToken.IsValid &&
                    readerA.CurrentBindingToken == slotA.GameplayAdmission.InputBindingToken &&
                    readerB.CurrentBindingToken == slotB.GameplayAdmission.InputBindingToken &&
                    !ReferenceEquals(readerA, readerB),
                    "each Host-local reader bound to its own Slot gameplay token",
                    $"readerA={readerA.CurrentBindingToken.StableText} " +
                    $"slotA={slotA.GameplayAdmission.InputBindingToken.StableText} " +
                    $"readerB={readerB.CurrentBindingToken.StableText} " +
                    $"slotB={slotB.GameplayAdmission.InputBindingToken.StableText}",
                    "Readers must be resolved from their own Host and bound to their own current gameplay token."))
            {
                yield break;
            }

            InputActionAsset actions = resources.PlayerInputA != null ? resources.PlayerInputA.actions : null;
            InputAction activate = actions != null ? actions.FindAction("Gameplay/Activate", false) : null;
            resources.ActivateReference = activate != null ? InputActionReference.Create(activate) : null;
            if (!proof.Check(
                    resources.ActivateReference != null &&
                    resources.ActivateReference.action != null,
                    "runtime InputActionReference for Gameplay/Activate",
                    "missing",
                    "ADR-025 isolation must derive Gameplay/Activate from the live PlayerInput.actions instance."))
            {
                yield break;
            }

            NeutralizeInputOwnershipKeyboards(resources);
            yield return null;
            bool idleA = ReaderIsPressed(readerA, resources.ActivateReference);
            bool idleB = ReaderIsPressed(readerB, resources.ActivateReference);
            QueueKeyboardSpace(resources.DeviceA, true);
            QueueKeyboardSpace(resources.DeviceB, false);
            InputSystem.Update();
            yield return null;
            resources.AToA = ReaderIsPressed(readerA, resources.ActivateReference);
            resources.AToB = ReaderIsPressed(readerB, resources.ActivateReference);
            QueueKeyboardSpace(resources.DeviceA, false);
            QueueKeyboardSpace(resources.DeviceB, true);
            InputSystem.Update();
            yield return null;
            resources.BToA = ReaderIsPressed(readerA, resources.ActivateReference);
            resources.BToB = ReaderIsPressed(readerB, resources.ActivateReference);
            NeutralizeInputOwnershipKeyboards(resources);
            yield return null;
            if (!proof.Check(
                    !idleA && !idleB &&
                    resources.AToA && !resources.AToB &&
                    resources.BToB && !resources.BToA,
                    "aToA=true aToB=false bToB=true bToA=false",
                    $"idleA={idleA} idleB={idleB} aToA={resources.AToA} aToB={resources.AToB} " +
                    $"bToB={resources.BToB} bToA={resources.BToA}",
                    "Device A must drive only Reader A and Device B must drive only Reader B."))
            {
                yield break;
            }

            if (!TryGetAccess(fixture, out IPlayerSessionScopedAccess access, out string issue) ||
                !access.TryGetObservation(out PlayerSessionScopedObservationSnapshot afterGameplay) ||
                afterGameplay == null)
            {
                proof.Record("scoped observation after gameplay preparation", issue, issue);
                yield break;
            }

            PlayerSessionScopedSlotObservation ownedA = FindSlot(afterGameplay, resources.SlotA);
            PlayerSessionScopedSlotObservation ownedB = FindSlot(afterGameplay, resources.SlotB);
            proof.Check(
                ownedA.HasInputOwnershipEvidence &&
                ownedB.HasInputOwnershipEvidence &&
                OwnershipContainsDevice(ownedA.InputOwnership, resources.DeviceA.deviceId) &&
                !OwnershipContainsDevice(ownedA.InputOwnership, resources.DeviceB.deviceId) &&
                OwnershipContainsDevice(ownedB.InputOwnership, resources.DeviceB.deviceId) &&
                !OwnershipContainsDevice(ownedB.InputOwnership, resources.DeviceA.deviceId),
                "physical ownership unchanged after GameplayReady",
                $"aOwnsA={OwnershipContainsDevice(ownedA.InputOwnership, resources.DeviceA.deviceId)} " +
                $"bOwnsB={OwnershipContainsDevice(ownedB.InputOwnership, resources.DeviceB.deviceId)}",
                "Gameplay preparation must not change public DeviceId ownership.");
        }

        private static IEnumerator ProveInputOwnershipLeaveRejoin(
            PlayerQaPanel fixture,
            IPlayerSessionScopedAccess access,
            InputOwnershipResources resources,
            InputOwnershipProof proof)
        {
            if (!access.TryGetObservation(out PlayerSessionScopedObservationSnapshot preLeaveSnapshot) ||
                preLeaveSnapshot == null)
            {
                proof.Record(
                    "pre-Leave scoped observation",
                    "unavailable",
                    "Leave A requires a retained public snapshot for immutability proof.");
                yield break;
            }

            PlayerSessionScopedSlotObservation preA = FindSlot(preLeaveSnapshot, resources.SlotA);
            PlayerSessionScopedSlotObservation preB = FindSlot(preLeaveSnapshot, resources.SlotB);
            resources.HostBindingB = preB.HostEvidence.HostBindingIdentity;
            resources.AssignmentB = preB.HostEvidence.AssignmentToken;
            LocalPlayerHostAuthoring previousHostA = resources.HostA;
            PlayerInput previousPlayerInputA = resources.PlayerInputA;
            PlayerGameplayInputReader previousReaderA = resources.ReaderA;
            PlayerGameplayInputBindingToken previousBindingA = previousReaderA != null
                ? previousReaderA.CurrentBindingToken
                : default;
            PlayerHostBindingIdentity previousHostBindingA = preA.HostEvidence.HostBindingIdentity;
            PlayerSlotAssignmentToken previousAssignmentA = preA.HostEvidence.AssignmentToken;
            int previousRevisionA = preA.Slot.Revision;
            int previousRevisionB = preB.Slot.Revision;
            resources.RevisionBBeforeLeaveA = previousRevisionB;
            LocalPlayerHostAuthoring hostB = resources.HostB;
            PlayerGameplayInputReader readerB = resources.ReaderB;
            PlayerGameplayInputBindingToken bindingB = readerB != null
                ? readerB.CurrentBindingToken
                : default;

            SessionPlayerLeaveResult leaveA = access.RequestLeave(
                new SessionPlayerLeaveRequest(
                    resources.SlotA,
                    previousRevisionA,
                    Source,
                    "player-qa-input-ownership-leave-a"));
            if (!proof.Check(
                    leaveA != null && leaveA.Succeeded,
                    "Leave A succeeded",
                    leaveA == null ? "null" : $"{leaveA.Status} {leaveA.Message}",
                    "ADR-025 Leave A must use the public SlotId + occurrence revision contract."))
            {
                yield break;
            }

            yield return WaitUntil(
                proof,
                () => TryFindSlot(fixture, resources.SlotA, out PlayerSessionScopedSlotObservation afterA) &&
                      TryFindSlot(fixture, resources.SlotB, out PlayerSessionScopedSlotObservation afterB) &&
                      !afterA.IsJoined &&
                      !afterA.HasHostEvidence &&
                      !afterA.HasInputOwnershipEvidence &&
                      !afterA.HasGameplayAdmissionEvidence &&
                      (previousHostA == null) &&
                      (previousReaderA == null || !previousReaderA.HasCurrentGameplayBinding) &&
                      afterB.IsJoined &&
                      afterB.HasHostEvidence &&
                      afterB.HasInputOwnershipEvidence,
                () => DescribeLeaveATerminalTimeout(fixture, resources, previousHostA, previousReaderA),
                "Timed out waiting for Leave A terminal release while keeping B observable.");
            if (proof.HasFailure)
            {
                yield break;
            }

            if (!access.TryGetObservation(out PlayerSessionScopedObservationSnapshot postLeaveSnapshot) ||
                postLeaveSnapshot == null)
            {
                proof.Record(
                    "post-Leave scoped observation",
                    "unavailable",
                    "Could not read the fresh observation after Leave A.");
                yield break;
            }

            PlayerSessionScopedSlotObservation postA = FindSlot(postLeaveSnapshot, resources.SlotA);
            PlayerSessionScopedSlotObservation postB = FindSlot(postLeaveSnapshot, resources.SlotB);
            PlayerSessionScopedSlotObservation historicalA = FindSlot(preLeaveSnapshot, resources.SlotA);
            PlayerSessionScopedSlotObservation historicalB = FindSlot(preLeaveSnapshot, resources.SlotB);
            bool snapshotImmutable =
                !postA.HasInputOwnershipEvidence &&
                historicalA.HasInputOwnershipEvidence &&
                OwnershipContainsDevice(historicalA.InputOwnership, resources.DeviceA.deviceId) &&
                historicalB.HasInputOwnershipEvidence &&
                OwnershipContainsDevice(historicalB.InputOwnership, resources.DeviceB.deviceId);
            resources.SnapshotImmutable = snapshotImmutable;
            if (!proof.Check(
                    snapshotImmutable,
                    "old snapshot keeps A ownership; new snapshot does not",
                    $"postAOwnership={postA.HasInputOwnershipEvidence} " +
                    $"historicalAOwnership={historicalA.HasInputOwnershipEvidence} " +
                    $"historicalBOwnership={historicalB.HasInputOwnershipEvidence}",
                    "Leave A must not mutate the retained historical snapshot."))
            {
                yield break;
            }

            resources.RevisionBAfterLeaveA = postB.Slot.Revision;

            bool bHasHostEvidence = postB.HasHostEvidence;
            bool bHostBindingMatches =
                bHasHostEvidence &&
                postB.HostEvidence.HostBindingIdentity.Equals(resources.HostBindingB);
            bool bAssignmentMatches =
                bHasHostEvidence &&
                postB.HostEvidence.AssignmentToken.Equals(resources.AssignmentB);
            bool bHostReferenceAlive = hostB != null;
            bool bHostIsJoined =
                bHostReferenceAlive &&
                hostB.IsJoined;
            bool bHostPreserved =
                bHasHostEvidence &&
                bHostBindingMatches &&
                bAssignmentMatches &&
                bHostReferenceAlive &&
                bHostIsJoined;
            string bCurrentHostBindingIdentity = bHasHostEvidence
                ? postB.HostEvidence.HostBindingIdentity.ToString()
                : "<none>";
            string bExpectedHostBindingIdentity = resources.HostBindingB.ToString();
            string bCurrentAssignmentToken = bHasHostEvidence
                ? postB.HostEvidence.AssignmentToken.ToString()
                : "<none>";
            string bExpectedAssignmentToken = resources.AssignmentB.ToString();
            bool bOwnershipPreserved =
                postB.HasInputOwnershipEvidence &&
                OwnershipContainsDevice(postB.InputOwnership, resources.DeviceB.deviceId) &&
                !OwnershipContainsDevice(postB.InputOwnership, resources.DeviceA.deviceId);
            bool bReaderPreserved =
                readerB != null &&
                readerB.HasCurrentGameplayBinding &&
                readerB.CurrentBindingToken == bindingB &&
                postB.HasGameplayAdmissionEvidence &&
                postB.GameplayAdmission.InputBindingToken == readerB.CurrentBindingToken;
            resources.BHasHostEvidence = bHasHostEvidence;
            resources.BHostBindingMatches = bHostBindingMatches;
            resources.BAssignmentMatches = bAssignmentMatches;
            resources.BHostReferenceAlive = bHostReferenceAlive;
            resources.BHostIsJoined = bHostIsJoined;
            resources.BCurrentHostBindingIdentity = bCurrentHostBindingIdentity;
            resources.BExpectedHostBindingIdentity = bExpectedHostBindingIdentity;
            resources.BCurrentAssignmentToken = bCurrentAssignmentToken;
            resources.BExpectedAssignmentToken = bExpectedAssignmentToken;
            resources.BHostPreserved = bHostPreserved;
            resources.BOwnershipPreserved = bOwnershipPreserved;
            resources.BReaderPreserved = bReaderPreserved;

            bool bPreserved =
                postB.IsJoined &&
                postB.Slot.PlayerSlotId == resources.SlotB &&
                bHostPreserved &&
                bOwnershipPreserved &&
                bReaderPreserved;
            resources.BPreserved = bPreserved;
            if (!proof.Check(
                    bPreserved,
                    "B remains the same current occurrence after Leave A",
                    $"joined={postB.IsJoined} hostPreserved={bHostPreserved} " +
                    $"bHasHostEvidence={bHasHostEvidence} bHostBindingMatches={bHostBindingMatches} " +
                    $"bAssignmentMatches={bAssignmentMatches} bHostReferenceAlive={bHostReferenceAlive} " +
                    $"bHostIsJoined={bHostIsJoined} " +
                    $"bCurrentHostBindingIdentity={bCurrentHostBindingIdentity} " +
                    $"bExpectedHostBindingIdentity={bExpectedHostBindingIdentity} " +
                    $"bCurrentAssignmentToken={bCurrentAssignmentToken} " +
                    $"bExpectedAssignmentToken={bExpectedAssignmentToken} " +
                    $"ownershipPreserved={bOwnershipPreserved} readerPreserved={bReaderPreserved} " +
                    $"revisionBeforeLeaveA={previousRevisionB} revisionAfterLeaveA={postB.Slot.Revision}",
                    "Leave A must preserve Player B Host, ownership and reader binding; Slot revision is diagnostic, not an occurrence identity."))
            {
                yield break;
            }

            NeutralizeInputOwnershipKeyboards(resources);
            QueueKeyboardSpace(resources.DeviceB, true);
            InputSystem.Update();
            yield return null;
            bool bStillDriven = ReaderIsPressed(readerB, resources.ActivateReference);
            resources.BInputOperational = bStillDriven;
            NeutralizeInputOwnershipKeyboards(resources);
            if (!proof.Check(
                    bStillDriven,
                    "Device B still drives Reader B after Leave A",
                    "inactive",
                    "Leave A must not stop Device B from driving Reader B."))
            {
                yield break;
            }

            resources.LeaveA = true;
            fixture.JoinCommand.InvokeFromDevice(resources.DeviceA);
            LocalPlayerJoinResult rejoinA = fixture.JoinCommand.LastJoinResult;
            if (!proof.Check(
                    rejoinA != null &&
                    rejoinA.Succeeded &&
                    rejoinA.LocalPlayerHost != null &&
                    rejoinA.PlayerInput != null,
                    "Rejoin A succeeded through InvokeFromDevice(deviceA)",
                    rejoinA == null ? "null" : $"{rejoinA.Status} {rejoinA.Message}",
                    "Rejoin A must use the same explicit-device Join surface."))
            {
                yield break;
            }

            resources.SlotA = rejoinA.Slot.PlayerSlotId;
            resources.RevisionA = rejoinA.Slot.Revision;
            resources.HostA = rejoinA.LocalPlayerHost;
            resources.PlayerInputA = rejoinA.PlayerInput;
            yield return WaitUntil(
                proof,
                () => TryFindSlot(fixture, resources.SlotA, out PlayerSessionScopedSlotObservation rejoined) &&
                      rejoined.IsJoined &&
                      rejoined.HasHostEvidence &&
                      rejoined.Slot.Revision > previousRevisionA &&
                      rejoined.HasInputOwnershipEvidence &&
                      OwnershipContainsDevice(rejoined.InputOwnership, resources.DeviceA.deviceId) &&
                      !OwnershipContainsDevice(rejoined.InputOwnership, resources.DeviceB.deviceId) &&
                      (!previousHostBindingA.IsValid ||
                       !rejoined.HostEvidence.HostBindingIdentity.Equals(previousHostBindingA) ||
                       !rejoined.HostEvidence.AssignmentToken.Equals(previousAssignmentA)),
                "Timed out waiting for rejoined A current observation.");
            if (proof.HasFailure)
            {
                yield break;
            }

            if (!access.TryGetObservation(out PlayerSessionScopedObservationSnapshot afterRejoin) ||
                afterRejoin == null)
            {
                proof.Record("observation after Rejoin A", "unavailable", "Could not observe rejoined A.");
                yield break;
            }

            PlayerSessionScopedSlotObservation currentA = FindSlot(afterRejoin, resources.SlotA);
            PlayerSessionScopedSlotObservation currentB = FindSlot(afterRejoin, resources.SlotB);
            bool freshOccurrence =
                currentA.Slot.Revision > previousRevisionA &&
                resources.HostA != null &&
                !ReferenceEquals(resources.HostA, previousHostA) &&
                resources.PlayerInputA != null &&
                !ReferenceEquals(resources.PlayerInputA, previousPlayerInputA) &&
                ReferenceEquals(rejoinA.Request.PairWithDevice, resources.DeviceA) &&
                currentA.HasInputOwnershipEvidence &&
                OwnershipContainsDevice(currentA.InputOwnership, resources.DeviceA.deviceId) &&
                !OwnershipContainsDevice(currentA.InputOwnership, resources.DeviceB.deviceId) &&
                (!previousHostBindingA.IsValid ||
                 !currentA.HostEvidence.HostBindingIdentity.Equals(previousHostBindingA) ||
                 !currentA.HostEvidence.AssignmentToken.Equals(previousAssignmentA));
            bool staleRejected =
                currentA.Slot.Revision != previousRevisionA &&
                !ReferenceEquals(resources.HostA, previousHostA) &&
                (previousBindingA.IsValid
                    ? !currentA.HasGameplayAdmissionEvidence ||
                      currentA.GameplayAdmission.InputBindingToken != previousBindingA
                    : true);
            resources.RejoinA = freshOccurrence;
            resources.StaleOwnershipRejected = staleRejected;
            if (!proof.Check(
                    freshOccurrence && staleRejected,
                    "rejoined A is a new Host/occurrence and is not the pre-Leave correlation",
                    $"previousRevision={previousRevisionA} currentRevision={currentA.Slot.Revision} " +
                    $"sameHost={ReferenceEquals(resources.HostA, previousHostA)} " +
                    $"hostBindingEqual={currentA.HostEvidence.HostBindingIdentity.Equals(previousHostBindingA)}",
                    "After terminal Leave, Device A must be eligible again with fresh PlayerInput/Host ownership."))
            {
                yield break;
            }

            bool bUnchangedHostPreserved =
                currentB.HasHostEvidence &&
                currentB.HostEvidence.HostBindingIdentity.Equals(resources.HostBindingB) &&
                currentB.HostEvidence.AssignmentToken.Equals(resources.AssignmentB) &&
                hostB != null &&
                hostB.IsJoined;
            bool bUnchangedOwnershipPreserved =
                currentB.HasInputOwnershipEvidence &&
                OwnershipContainsDevice(currentB.InputOwnership, resources.DeviceB.deviceId) &&
                !OwnershipContainsDevice(currentB.InputOwnership, resources.DeviceA.deviceId);
            bool bUnchangedReaderPreserved =
                readerB != null &&
                readerB.HasCurrentGameplayBinding &&
                readerB.CurrentBindingToken == bindingB;
            bool bUnchanged =
                currentB.IsJoined &&
                currentB.Slot.PlayerSlotId == resources.SlotB &&
                bUnchangedHostPreserved &&
                bUnchangedOwnershipPreserved &&
                bUnchangedReaderPreserved;
            proof.Check(
                bUnchanged,
                "B unchanged through Rejoin A",
                $"joined={currentB.IsJoined} hostPreserved={bUnchangedHostPreserved} " +
                $"ownershipPreserved={bUnchangedOwnershipPreserved} readerPreserved={bUnchangedReaderPreserved} " +
                $"sameHostRef={ReferenceEquals(resources.HostB, hostB)} " +
                $"revisionBeforeLeaveA={previousRevisionB} revisionAfterRejoinA={currentB.Slot.Revision}",
                "Rejoin A must not recreate or disturb Player B; Slot revision is diagnostic, not an occurrence identity.");
        }

        private static IEnumerator CleanupInputOwnership(
            PlayerQaPanel fixture,
            InputOwnershipResources resources,
            InputOwnershipCleanup cleanup)
        {
            NeutralizeInputOwnershipKeyboards(resources);

            if (!TryGetAccess(fixture, out IPlayerSessionScopedAccess access, out string accessIssue))
            {
                cleanup.Record($"cleanup could not get Session access: {accessIssue}");
            }
            else
            {
                yield return LeaveInputOwnershipSlotIfJoined(
                    fixture, access, resources.SlotA, cleanup, "A");
                yield return LeaveInputOwnershipSlotIfJoined(
                    fixture, access, resources.SlotB, cleanup, "B");
                yield return WaitUntilCleanup(
                    cleanup,
                    () => (!resources.SlotA.IsValid || SlotHasNoCurrentPlayer(fixture, resources.SlotA)) &&
                          (!resources.SlotB.IsValid || SlotHasNoCurrentPlayer(fixture, resources.SlotB)),
                    "Timed out waiting for ADR-025 Players to release during cleanup.");
            }

            if (resources.ActivateReference != null)
            {
                UnityEngine.Object.Destroy(resources.ActivateReference);
                resources.ActivateReference = null;
            }

            RemoveOwnedDevice(resources.DeviceA);
            RemoveOwnedDevice(resources.DeviceB);
            RemoveOwnedDevice(resources.RemovedProbe);

            bool devicesRemoved =
                (resources.DeviceA == null || !resources.DeviceA.added) &&
                (resources.DeviceB == null || !resources.DeviceB.added) &&
                (resources.RemovedProbe == null || !resources.RemovedProbe.added);
            bool joiningOpen = TryGetAccess(fixture, out IPlayerSessionScopedAccess joiningAccess, out _) &&
                joiningAccess.TryGetObservation(out PlayerSessionScopedObservationSnapshot observation) &&
                observation != null &&
                observation.Participation != null &&
                observation.Participation.JoiningOpen;
            bool noAdrPlayers =
                SlotHasNoCurrentPlayer(fixture, resources.SlotA) &&
                SlotHasNoCurrentPlayer(fixture, resources.SlotB);
            if (!devicesRemoved || !joiningOpen || !noAdrPlayers)
            {
                cleanup.Record(
                    $"cleanup residual state devicesRemoved={devicesRemoved} joiningOpen={joiningOpen} " +
                    $"noAdrPlayers={noAdrPlayers} " +
                    $"joined='{DescribeJoinedOwnership(fixture)}'");
            }
            else
            {
                resources.CleanupSucceeded = true;
            }
        }

        private static IEnumerator LeaveInputOwnershipSlotIfJoined(
            PlayerQaPanel fixture,
            IPlayerSessionScopedAccess access,
            PlayerSlotId slotId,
            InputOwnershipCleanup cleanup,
            string label)
        {
            if (!slotId.IsValid ||
                !TryFindSlot(fixture, slotId, out PlayerSessionScopedSlotObservation current) ||
                !current.IsJoined)
            {
                yield break;
            }

            SessionPlayerLeaveResult leave = access.RequestLeave(
                new SessionPlayerLeaveRequest(
                    slotId,
                    current.Slot.Revision,
                    Source,
                    $"player-qa-input-ownership-cleanup-{label}"));
            if (leave == null || !leave.Succeeded)
            {
                cleanup.Record(
                    $"cleanup Leave {label} failed: " +
                    (leave == null ? "null" : $"{leave.Status} {leave.Message}"));
            }

            yield return null;
        }

        private static void LastResortCleanupInputOwnership(
            PlayerQaPanel fixture,
            InputOwnershipResources resources)
        {
            NeutralizeInputOwnershipKeyboards(resources);
            if (TryGetAccess(fixture, out IPlayerSessionScopedAccess access, out _))
            {
                LeaveInputOwnershipSlotNow(fixture, access, resources.SlotA);
                LeaveInputOwnershipSlotNow(fixture, access, resources.SlotB);
            }

            if (resources.ActivateReference != null)
            {
                UnityEngine.Object.Destroy(resources.ActivateReference);
                resources.ActivateReference = null;
            }

            RemoveOwnedDevice(resources.DeviceA);
            RemoveOwnedDevice(resources.DeviceB);
            RemoveOwnedDevice(resources.RemovedProbe);
        }

        private static void LeaveInputOwnershipSlotNow(
            PlayerQaPanel fixture,
            IPlayerSessionScopedAccess access,
            PlayerSlotId slotId)
        {
            if (!slotId.IsValid ||
                !TryFindSlot(fixture, slotId, out PlayerSessionScopedSlotObservation current) ||
                !current.IsJoined)
            {
                return;
            }

            access.RequestLeave(
                new SessionPlayerLeaveRequest(
                    slotId,
                    current.Slot.Revision,
                    Source,
                    "player-qa-input-ownership-last-resort-leave"));
        }

        private static void ApplyInputOwnershipResult(
            Result result,
            InputOwnershipProof proof,
            InputOwnershipCleanup cleanup,
            PlayerQaPanel fixture)
        {
            if (proof.HasFailure)
            {
                result.Fail("input-ownership", proof.Expected, proof.Actual, proof.Message);
                if (cleanup.Failed)
                {
                    Debug.LogError(
                        $"[QA_PLAYER_INPUT_OWNERSHIP] cleanup='failed' " +
                        $"primary='{proof.Message}' cleanup='{cleanup.Failure}'",
                        fixture);
                }

                return;
            }

            if (cleanup.Failed)
            {
                result.Fail(
                    "input-ownership",
                    "cleanup succeeded",
                    "cleanup failed",
                    cleanup.Failure);
            }
        }

        private static void EmitInputOwnershipDiagnostic(
            PlayerQaPanel fixture,
            InputOwnershipResources resources,
            InputOwnershipProof proof,
            InputOwnershipCleanup cleanup)
        {
            Debug.Log(
                "[QA_PLAYER_INPUT_OWNERSHIP] " +
                $"slotA='{(resources.SlotA.IsValid ? resources.SlotA.StableText : "none")}' " +
                $"slotB='{(resources.SlotB.IsValid ? resources.SlotB.StableText : "none")}' " +
                $"revisionA='{resources.RevisionA}' revisionB='{resources.RevisionB}' " +
                $"deviceAId='{(resources.DeviceA != null ? resources.DeviceA.deviceId.ToString() : "none")}' " +
                $"deviceBId='{(resources.DeviceB != null ? resources.DeviceB.deviceId.ToString() : "none")}' " +
                $"playerIndexA='{resources.PlayerIndexA}' playerIndexB='{resources.PlayerIndexB}' " +
                $"aToA='{resources.AToA}' aToB='{resources.AToB}' bToB='{resources.BToB}' bToA='{resources.BToA}' " +
                $"bindingA='{(resources.BindingA.IsValid ? resources.BindingA.StableText : "none")}' " +
                $"bindingB='{(resources.BindingB.IsValid ? resources.BindingB.StableText : "none")}' " +
                $"activityRequestOutcome='{DescribeDiagnostic(resources.ActivityRequestOutcome)}' " +
                $"activityState='{DescribeDiagnostic(resources.ActivityState)}' " +
                $"activityReadiness='{DescribeDiagnostic(resources.ActivityReadiness)}' " +
                $"blockingIssueCount='{DescribeDiagnostic(resources.BlockingIssueCount)}' " +
                $"slotAExpectedActor='{DescribeDiagnostic(resources.SlotAExpectedActor)}' " +
                $"slotASelectedActor='{DescribeDiagnostic(resources.SlotASelectedActor)}' " +
                $"slotAPrepared='{resources.SlotAPrepared}' slotAMaterialized='{resources.SlotAMaterialized}' " +
                $"slotAHasGameplayAdmission='{resources.SlotAHasGameplayAdmission}' " +
                $"slotAGameplayReady='{resources.SlotAGameplayReady}' " +
                $"slotBExpectedActor='{DescribeDiagnostic(resources.SlotBExpectedActor)}' " +
                $"slotBSelectedActor='{DescribeDiagnostic(resources.SlotBSelectedActor)}' " +
                $"slotBPrepared='{resources.SlotBPrepared}' slotBMaterialized='{resources.SlotBMaterialized}' " +
                $"slotBHasGameplayAdmission='{resources.SlotBHasGameplayAdmission}' " +
                $"slotBGameplayReady='{resources.SlotBGameplayReady}' " +
                $"blockingFailureKind='{DescribeDiagnostic(resources.BlockingFailureKind)}' " +
                $"blockingFailureMessage='{DescribeDiagnostic(resources.BlockingFailureMessage)}' " +
                $"blockingFailureOwner='{DescribeDiagnostic(resources.BlockingFailureOwner)}' " +
                $"leaveA='{resources.LeaveA}' bPreserved='{resources.BPreserved}' " +
                $"snapshotImmutable='{resources.SnapshotImmutable}' rejoinA='{resources.RejoinA}' " +
                $"staleOwnershipRejected='{resources.StaleOwnershipRejected}' " +
                $"bRevisionBeforeLeaveA='{resources.RevisionBBeforeLeaveA}' bRevisionAfterLeaveA='{resources.RevisionBAfterLeaveA}' " +
                $"bHostPreserved='{resources.BHostPreserved}' " +
                $"bHasHostEvidence='{resources.BHasHostEvidence}' " +
                $"bHostBindingMatches='{resources.BHostBindingMatches}' " +
                $"bAssignmentMatches='{resources.BAssignmentMatches}' " +
                $"bHostReferenceAlive='{resources.BHostReferenceAlive}' " +
                $"bHostIsJoined='{resources.BHostIsJoined}' " +
                $"bCurrentHostBindingIdentity='{EscapeDiagnosticValue(resources.BCurrentHostBindingIdentity)}' " +
                $"bExpectedHostBindingIdentity='{EscapeDiagnosticValue(resources.BExpectedHostBindingIdentity)}' " +
                $"bCurrentAssignmentToken='{EscapeDiagnosticValue(resources.BCurrentAssignmentToken)}' " +
                $"bExpectedAssignmentToken='{EscapeDiagnosticValue(resources.BExpectedAssignmentToken)}' " +
                $"bOwnershipPreserved='{resources.BOwnershipPreserved}' " +
                $"bReaderPreserved='{resources.BReaderPreserved}' bInputOperational='{resources.BInputOperational}' " +
                $"cleanup='{(cleanup.Failed ? "failed" : resources.CleanupSucceeded ? "succeeded" : "incomplete")}' " +
                $"proof='{(proof.HasFailure ? "failed" : "succeeded")}'.",
                fixture);
        }

        private static IEnumerator WaitUntil(
            InputOwnershipProof proof,
            Func<bool> predicate,
            string timeoutMessage)
        {
            if (proof.HasFailure)
            {
                yield break;
            }

            for (int frame = 0; frame < FrameBudget; frame++)
            {
                if (predicate())
                {
                    yield break;
                }

                yield return null;
            }

            proof.Record("condition met", "timeout", timeoutMessage);
        }

        private static IEnumerator WaitUntil(
            InputOwnershipProof proof,
            Func<bool> predicate,
            Func<string> timeoutDiagnostic,
            string timeoutMessage)
        {
            if (proof.HasFailure)
            {
                yield break;
            }

            for (int frame = 0; frame < FrameBudget; frame++)
            {
                if (predicate())
                {
                    yield break;
                }

                yield return null;
            }

            proof.Record(
                "condition met",
                timeoutDiagnostic != null ? timeoutDiagnostic() : "timeout",
                timeoutMessage);
        }

        private static IEnumerator WaitUntilCleanup(
            InputOwnershipCleanup cleanup,
            Func<bool> predicate,
            string timeoutMessage)
        {
            for (int frame = 0; frame < FrameBudget; frame++)
            {
                if (predicate())
                {
                    yield break;
                }

                yield return null;
            }

            cleanup.Record(timeoutMessage);
        }

        private static bool IsRejectedInvalidDeviceJoin(PlayerSessionJoinCommandTrigger joinCommand)
        {
            LocalPlayerJoinResult join = joinCommand != null ? joinCommand.LastJoinResult : null;
            return join != null &&
                join.Status == LocalPlayerJoinStatus.RejectedInvalidRequest &&
                string.Equals(joinCommand.LastOutcome, "Rejected", StringComparison.Ordinal) &&
                join.LocalPlayerHost == null &&
                !join.HasLocalPlayerHostEvidence;
        }

        private static bool AnyJoinedOwnership(PlayerQaPanel fixture)
        {
            return (TryFindSlot(fixture, ExpectedSlotId(fixture), out PlayerSessionScopedSlotObservation p1) &&
                    (p1.IsJoined || p1.HasHostEvidence || p1.HasInputOwnershipEvidence)) ||
                   (TryFindSlot(fixture, ExpectedSlotTwoId(fixture), out PlayerSessionScopedSlotObservation p2) &&
                    (p2.IsJoined || p2.HasHostEvidence || p2.HasInputOwnershipEvidence));
        }

        private static string DescribeJoinedOwnership(PlayerQaPanel fixture)
        {
            bool p1Found = TryFindSlot(
                fixture, ExpectedSlotId(fixture), out PlayerSessionScopedSlotObservation p1);
            bool p2Found = TryFindSlot(
                fixture, ExpectedSlotTwoId(fixture), out PlayerSessionScopedSlotObservation p2);
            return $"p1Joined={(p1Found && p1.IsJoined)} p1Host={(p1Found && p1.HasHostEvidence)} " +
                   $"p1Ownership={(p1Found && p1.HasInputOwnershipEvidence)} " +
                   $"p2Joined={(p2Found && p2.IsJoined)} p2Host={(p2Found && p2.HasHostEvidence)} " +
                   $"p2Ownership={(p2Found && p2.HasInputOwnershipEvidence)}";
        }

        private static bool OwnershipContainsDevice(
            LocalPlayerInputOwnershipSummary ownership,
            int deviceId)
        {
            IReadOnlyList<LocalPlayerInputDeviceSummary> devices = ownership.Devices;
            for (int index = 0; index < devices.Count; index++)
            {
                if (devices[index].DeviceId == deviceId)
                {
                    return true;
                }
            }

            return false;
        }

        private static string DescribeOwnershipDeviceIds(LocalPlayerInputOwnershipSummary ownership)
        {
            IReadOnlyList<LocalPlayerInputDeviceSummary> devices = ownership.Devices;
            if (devices.Count == 0)
            {
                return "none";
            }

            string[] ids = new string[devices.Count];
            for (int index = 0; index < devices.Count; index++)
            {
                ids[index] = devices[index].DeviceId.ToString();
            }

            return string.Join(",", ids);
        }

        private static string DescribeLeaveATerminalTimeout(
            PlayerQaPanel fixture,
            InputOwnershipResources resources,
            LocalPlayerHostAuthoring previousHostA,
            PlayerGameplayInputReader previousReaderA)
        {
            bool foundA = TryFindSlot(fixture, resources.SlotA, out PlayerSessionScopedSlotObservation diagA);
            bool foundB = TryFindSlot(fixture, resources.SlotB, out PlayerSessionScopedSlotObservation diagB);
            string aPart = foundA
                ? $"aJoined={diagA.IsJoined} aHasHostEvidence={diagA.HasHostEvidence} " +
                  $"aHasInputOwnershipEvidence={diagA.HasInputOwnershipEvidence} " +
                  $"aHasGameplayAdmissionEvidence={diagA.HasGameplayAdmissionEvidence}"
                : "aSlot=notFound";
            string bPart = foundB
                ? $"bJoined={diagB.IsJoined} bHasHostEvidence={diagB.HasHostEvidence} " +
                  $"bHasInputOwnershipEvidence={diagB.HasInputOwnershipEvidence} " +
                  $"bCurrentHostBindingIdentity={(diagB.HasHostEvidence && diagB.HostEvidence.HostBindingIdentity.IsValid ? diagB.HostEvidence.HostBindingIdentity.StableText : "none")} " +
                  $"bExpectedHostBindingIdentity={(resources.HostBindingB.IsValid ? resources.HostBindingB.StableText : "none")} " +
                  $"bCurrentAssignmentToken={(diagB.HasHostEvidence && diagB.HostEvidence.AssignmentToken.IsValid ? diagB.HostEvidence.AssignmentToken.StableText : "none")} " +
                  $"bExpectedAssignmentToken={(resources.AssignmentB.IsValid ? resources.AssignmentB.StableText : "none")} " +
                  $"bCurrentDeviceIds={DescribeOwnershipDeviceIds(diagB.InputOwnership)} " +
                  $"bExpectedDeviceId={(resources.DeviceB != null ? resources.DeviceB.deviceId.ToString() : "none")} " +
                  $"bCurrentPlayerIndex={(diagB.HasInputOwnershipEvidence ? diagB.InputOwnership.UnityPlayerIndex.ToString() : "unavailable")} " +
                  $"bExpectedPlayerIndex={resources.PlayerIndexB}"
                : "bSlot=notFound";
            return $"previousHostAAlive={(previousHostA != null)} " +
                $"previousReaderAHasGameplayBinding={(previousReaderA != null && previousReaderA.HasCurrentGameplayBinding)} " +
                $"{aPart} {bPart}";
        }

        private static string EffectiveControlScheme(PlayerInput playerInput)
        {
            return playerInput != null
                ? playerInput.currentControlScheme ?? string.Empty
                : string.Empty;
        }

        private static bool IsInputOwnershipSlotGameplayReaderReady(
            PlayerQaPanel fixture,
            PlayerSlotId slotId,
            LocalPlayerHostAuthoring host)
        {
            ActorProfile expected = ResolveConfiguredDefaultActor(fixture, slotId);
            return expected != null &&
                TryFindSlot(fixture, slotId, out PlayerSessionScopedSlotObservation slot) &&
                slot.IsJoined &&
                slot.HasHostEvidence &&
                slot.Slot.SelectedActorProfile == expected &&
                slot.IsLogicalActorPrepared &&
                slot.IsPhysicallyMaterialized &&
                slot.HasGameplayAdmissionEvidence &&
                slot.GameplayAdmission.IsAdmitted &&
                slot.GameplayAdmission.GameplayReady &&
                slot.GameplayAdmission.InputBindingToken.IsValid &&
                TryResolveCurrentGameplayReader(
                    host,
                    out PlayerGameplayInputReader reader,
                    out _) &&
                reader.HasCurrentGameplayBinding &&
                reader.GameplayReady &&
                reader.CurrentBindingToken == slot.GameplayAdmission.InputBindingToken;
        }

        private static ActorProfile ResolveConfiguredDefaultActor(
            PlayerQaPanel fixture,
            PlayerSlotId slotId)
        {
            if (fixture == null || !slotId.IsValid)
            {
                return null;
            }

            if (fixture.PlayerOneSlot != null &&
                fixture.PlayerOneSlot.TryGetPlayerSlotId(out PlayerSlotId playerOne, out _) &&
                playerOne == slotId)
            {
                return fixture.PlayerOneSlot.DefaultActorProfile;
            }

            if (fixture.PlayerTwoSlot != null &&
                fixture.PlayerTwoSlot.TryGetPlayerSlotId(out PlayerSlotId playerTwo, out _) &&
                playerTwo == slotId)
            {
                return fixture.PlayerTwoSlot.DefaultActorProfile;
            }

            return null;
        }

        private static void CaptureInputOwnershipGameplayReadyRequest(
            InputOwnershipResources resources,
            ActivityRequestTrigger trigger)
        {
            resources.ActivityRequestOutcome = trigger != null
                ? trigger.LastOutcome.ToString()
                : string.Empty;
            resources.BlockingFailureMessage = trigger != null
                ? trigger.LastMessage
                : string.Empty;
            resources.ActivityState = ExtractQuotedDiagnostic(
                resources.BlockingFailureMessage, "activityState");
            resources.ActivityReadiness = ExtractQuotedDiagnostic(
                resources.BlockingFailureMessage, "activityReadiness");
            resources.BlockingIssueCount = ExtractQuotedDiagnostic(
                resources.BlockingFailureMessage, "activityReadinessIssues");
            if (string.IsNullOrEmpty(resources.BlockingIssueCount))
            {
                resources.BlockingIssueCount = ExtractQuotedDiagnostic(
                    resources.BlockingFailureMessage,
                    "activityContentExecutionBlockingIssues");
            }

            resources.BlockingFailureKind = ExtractQuotedDiagnostic(
                resources.BlockingFailureMessage, "activityReadinessReason");
            resources.BlockingFailureOwner = string.Empty;
        }

        private static void CaptureInputOwnershipSlotGameplayEvidence(
            PlayerQaPanel fixture,
            InputOwnershipResources resources)
        {
            CaptureInputOwnershipSlotGameplayEvidence(
                fixture,
                resources.SlotA,
                out resources.SlotAExpectedActor,
                out resources.SlotASelectedActor,
                out resources.SlotAPrepared,
                out resources.SlotAMaterialized,
                out resources.SlotAHasGameplayAdmission,
                out resources.SlotAGameplayReady);
            CaptureInputOwnershipSlotGameplayEvidence(
                fixture,
                resources.SlotB,
                out resources.SlotBExpectedActor,
                out resources.SlotBSelectedActor,
                out resources.SlotBPrepared,
                out resources.SlotBMaterialized,
                out resources.SlotBHasGameplayAdmission,
                out resources.SlotBGameplayReady);
        }

        private static void CaptureInputOwnershipSlotGameplayEvidence(
            PlayerQaPanel fixture,
            PlayerSlotId slotId,
            out string expectedActor,
            out string selectedActor,
            out bool prepared,
            out bool materialized,
            out bool hasGameplayAdmission,
            out bool gameplayReady)
        {
            expectedActor = DescribeObject(ResolveConfiguredDefaultActor(fixture, slotId));
            selectedActor = "unavailable";
            prepared = false;
            materialized = false;
            hasGameplayAdmission = false;
            gameplayReady = false;
            if (!TryFindSlot(fixture, slotId, out PlayerSessionScopedSlotObservation slot))
            {
                return;
            }

            selectedActor = DescribeObject(slot.Slot.SelectedActorProfile);
            prepared = slot.IsLogicalActorPrepared;
            materialized = slot.IsPhysicallyMaterialized;
            hasGameplayAdmission = slot.HasGameplayAdmissionEvidence &&
                slot.GameplayAdmission.IsAdmitted;
            gameplayReady = slot.HasGameplayAdmissionEvidence &&
                slot.GameplayAdmission.GameplayReady;
        }

        private static bool IsInputOwnershipGameplayReadyBlocked(
            InputOwnershipResources resources)
        {
            return string.Equals(resources.ActivityReadiness, "NotReady", StringComparison.Ordinal) ||
                (!string.IsNullOrEmpty(resources.BlockingIssueCount) &&
                 resources.BlockingIssueCount != "0" &&
                 resources.BlockingIssueCount != "unavailable");
        }

        private static void EmitInputOwnershipGameplayReadyDiagnostic(
            PlayerQaPanel fixture,
            InputOwnershipResources resources)
        {
            Debug.Log(
                "[QA_PLAYER_INPUT_OWNERSHIP] " +
                "gameplayReady='blocked-or-incomplete' " +
                $"activityRequestOutcome='{DescribeDiagnostic(resources.ActivityRequestOutcome)}' " +
                $"activityState='{DescribeDiagnostic(resources.ActivityState)}' " +
                $"activityReadiness='{DescribeDiagnostic(resources.ActivityReadiness)}' " +
                $"blockingIssueCount='{DescribeDiagnostic(resources.BlockingIssueCount)}' " +
                $"slotA='{(resources.SlotA.IsValid ? resources.SlotA.StableText : "none")}' " +
                $"slotAExpectedActor='{DescribeDiagnostic(resources.SlotAExpectedActor)}' " +
                $"slotASelectedActor='{DescribeDiagnostic(resources.SlotASelectedActor)}' " +
                $"slotAPrepared='{resources.SlotAPrepared}' slotAMaterialized='{resources.SlotAMaterialized}' " +
                $"slotAHasGameplayAdmission='{resources.SlotAHasGameplayAdmission}' " +
                $"slotAGameplayReady='{resources.SlotAGameplayReady}' " +
                $"slotB='{(resources.SlotB.IsValid ? resources.SlotB.StableText : "none")}' " +
                $"slotBExpectedActor='{DescribeDiagnostic(resources.SlotBExpectedActor)}' " +
                $"slotBSelectedActor='{DescribeDiagnostic(resources.SlotBSelectedActor)}' " +
                $"slotBPrepared='{resources.SlotBPrepared}' slotBMaterialized='{resources.SlotBMaterialized}' " +
                $"slotBHasGameplayAdmission='{resources.SlotBHasGameplayAdmission}' " +
                $"slotBGameplayReady='{resources.SlotBGameplayReady}' " +
                $"blockingFailureKind='{DescribeDiagnostic(resources.BlockingFailureKind)}' " +
                $"blockingFailureMessage='{DescribeDiagnostic(resources.BlockingFailureMessage)}' " +
                $"blockingFailureOwner='{DescribeDiagnostic(resources.BlockingFailureOwner)}'.",
                fixture);
        }

        private static string ExtractQuotedDiagnostic(string source, string fieldName)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(fieldName))
            {
                return string.Empty;
            }

            string token = fieldName + "='";
            int start = source.IndexOf(token, StringComparison.Ordinal);
            if (start < 0)
            {
                return string.Empty;
            }

            start += token.Length;
            int end = source.IndexOf('\'', start);
            if (end < 0)
            {
                return string.Empty;
            }

            return source.Substring(start, end - start);
        }

        private static string DescribeDiagnostic(string value)
        {
            return string.IsNullOrEmpty(value) ? "unavailable" : value;
        }

        private static bool ReaderIsPressed(
            PlayerGameplayInputReader reader,
            InputActionReference action)
        {
            return reader != null &&
                reader.TryIsPressed(action, out bool isPressed) &&
                isPressed;
        }

        private static void NeutralizeInputOwnershipKeyboards(InputOwnershipResources resources)
        {
            QueueKeyboardSpace(resources.DeviceA, false);
            QueueKeyboardSpace(resources.DeviceB, false);
            if (resources.RemovedProbe != null && resources.RemovedProbe.added)
            {
                QueueKeyboardSpace(resources.RemovedProbe, false);
            }

            InputSystem.Update();
        }

        private static void QueueKeyboardSpace(Keyboard device, bool pressed)
        {
            if (device == null || !device.added)
            {
                return;
            }

            if (pressed)
            {
                InputSystem.QueueStateEvent(device, new KeyboardState(Key.Space));
                return;
            }

            InputSystem.QueueStateEvent(device, new KeyboardState());
        }

        private static void RemoveOwnedDevice(InputDevice device)
        {
            if (device != null && device.added)
            {
                InputSystem.RemoveDevice(device);
            }
        }

        private static bool SlotHasNoCurrentPlayer(PlayerQaPanel fixture, PlayerSlotId slotId)
        {
            return !slotId.IsValid ||
                !TryFindSlot(fixture, slotId, out PlayerSessionScopedSlotObservation slot) ||
                (!slot.IsJoined && !slot.HasHostEvidence && !slot.HasInputOwnershipEvidence);
        }

        private sealed class InputOwnershipResources
        {
            internal Keyboard OriginalKeyboard;
            internal Keyboard DeviceA;
            internal Keyboard DeviceB;
            internal Keyboard RemovedProbe;
            internal InputActionReference ActivateReference;
            internal PlayerSlotId SlotA;
            internal PlayerSlotId SlotB;
            internal int RevisionA;
            internal int RevisionB;
            internal LocalPlayerHostAuthoring HostA;
            internal LocalPlayerHostAuthoring HostB;
            internal PlayerInput PlayerInputA;
            internal PlayerInput PlayerInputB;
            internal PlayerGameplayInputReader ReaderA;
            internal PlayerGameplayInputReader ReaderB;
            internal PlayerHostBindingIdentity HostBindingA;
            internal PlayerHostBindingIdentity HostBindingB;
            internal PlayerSlotAssignmentToken AssignmentA;
            internal PlayerSlotAssignmentToken AssignmentB;
            internal PlayerGameplayInputBindingToken BindingA;
            internal PlayerGameplayInputBindingToken BindingB;
            internal int PlayerIndexA = -1;
            internal int PlayerIndexB = -1;
            internal bool AToA;
            internal bool AToB;
            internal bool BToA;
            internal bool BToB;
            internal int RevisionBBeforeLeaveA;
            internal int RevisionBAfterLeaveA;
            internal bool BHasHostEvidence;
            internal bool BHostBindingMatches;
            internal bool BAssignmentMatches;
            internal bool BHostReferenceAlive;
            internal bool BHostIsJoined;
            internal string BCurrentHostBindingIdentity = "<none>";
            internal string BExpectedHostBindingIdentity = "<none>";
            internal string BCurrentAssignmentToken = "<none>";
            internal string BExpectedAssignmentToken = "<none>";
            internal bool BHostPreserved;
            internal bool BOwnershipPreserved;
            internal bool BReaderPreserved;
            internal bool BInputOperational;
            internal bool LeaveA;
            internal bool BPreserved;
            internal bool SnapshotImmutable;
            internal bool RejoinA;
            internal bool StaleOwnershipRejected;
            internal bool CleanupSucceeded;
            internal string ActivityRequestOutcome;
            internal string ActivityState;
            internal string ActivityReadiness;
            internal string BlockingIssueCount;
            internal string BlockingFailureKind;
            internal string BlockingFailureMessage;
            internal string BlockingFailureOwner;
            internal string SlotAExpectedActor;
            internal string SlotASelectedActor;
            internal bool SlotAPrepared;
            internal bool SlotAMaterialized;
            internal bool SlotAHasGameplayAdmission;
            internal bool SlotAGameplayReady;
            internal string SlotBExpectedActor;
            internal string SlotBSelectedActor;
            internal bool SlotBPrepared;
            internal bool SlotBMaterialized;
            internal bool SlotBHasGameplayAdmission;
            internal bool SlotBGameplayReady;
        }

        private sealed class InputOwnershipProof
        {
            internal string Expected;
            internal string Actual;
            internal string Message;
            internal bool HasFailure => !string.IsNullOrEmpty(Message);

            internal void Record(string expected, string actual, string message)
            {
                if (HasFailure)
                {
                    return;
                }

                Expected = expected;
                Actual = actual;
                Message = message;
            }

            internal bool Check(bool condition, string expected, string actual, string message)
            {
                if (!condition)
                {
                    Record(expected, actual, message);
                }

                return !HasFailure;
            }
        }

        private sealed class InputOwnershipCleanup
        {
            internal string Failure;
            internal bool Failed => !string.IsNullOrEmpty(Failure);

            internal void Record(string message)
            {
                if (Failed)
                {
                    return;
                }

                Failure = message;
            }
        }

        private static void ProveSpatial(PlayerQaPanel fixture, Result result)
        {
            Require(result, "spatial", fixture.SpatialEntry != null,
                "RoutePlayerSpatialEntryAuthoring", "null",
                "Player QA primary scene requires Route Player Spatial Entry authoring.");
            if (!result.Ok)
            {
                return;
            }

            bool bound = false;
            IReadOnlyList<RoutePlayerSpatialEntryAuthoring.Binding> bindings =
                fixture.SpatialEntry.Bindings;
            for (int index = 0; index < bindings.Count; index++)
            {
                RoutePlayerSpatialEntryAuthoring.Binding binding = bindings[index];
                if (binding.PlayerSlotProfile == fixture.PlayerOneSlot &&
                    binding.PlacementAnchor != null)
                {
                    bound = true;
                    break;
                }
            }

            Require(result, "spatial", bound,
                "P1 spatial anchor", "missing",
                "Route spatial entry does not bind P1 to an explicit world anchor.");
        }

        private static void ProveRelocation(PlayerQaPanel fixture, Result result)
        {
            Require(result, "relocation", fixture.Relocation != null && fixture.RelocateActivity != null,
                "ActivityPlayerRelocationAuthoring", "null",
                "Player QA primary scene requires Activity Player Relocation authoring.");
            if (!result.Ok)
            {
                return;
            }

            bool bound = false;
            IReadOnlyList<ActivityPlayerRelocationAuthoring.Binding> bindings =
                fixture.Relocation.Bindings;
            for (int index = 0; index < bindings.Count; index++)
            {
                ActivityPlayerRelocationAuthoring.Binding binding = bindings[index];
                if (binding.Activity == fixture.RelocateActivity &&
                    binding.PlayerSlotProfile == fixture.PlayerOneSlot &&
                    binding.RelocationAnchor != null)
                {
                    bound = true;
                    break;
                }
            }

            Require(result, "relocation", bound,
                "Relocate Activity P1 relocation anchor", "missing",
                "Activity relocation does not bind the dedicated Relocate Activity and P1 to an explicit world anchor.");
        }
        private static void ValidateManagerHost(
            Result result,
            PlayerQaPanel fixture,
            LocalPlayerHostAuthoring actualHost)
        {
            Require(result, "join",
                fixture.ManagerHostTemplate != null &&
                actualHost != null &&
                actualHost != fixture.ManagerHostTemplate &&
                actualHost.IsJoined &&
                actualHost.PlayerInput != null &&
                actualHost.ActorMount != null &&
                actualHost.PlayerActorRuntimeHostPrefab ==
                    fixture.ManagerHostTemplate.PlayerActorRuntimeHostPrefab,
                "canonical manager host instance",
                actualHost == null ? "null" : actualHost.name,
                "Join did not materialize the canonical Manager Local Player Host composition.");
        }

        private static void EmitGameplayReadyReaderTopologyDiagnostic(Result result)
        {
            const string prefix = "[QA_PLAYER_PRESENTATION_DIAGNOSTIC]";
            LocalPlayerHostAuthoring host = result != null ? result.PlayerOneHost : null;
            if (host == null || host.ActorMount == null)
            {
                Debug.LogError(
                    $"{prefix} host='{DescribeObject(host)}' " +
                    $"actorMount='{DescribeObject(host != null ? host.ActorMount : null)}' " +
                    "runtimeHostCount='0' runtimeHost='<unavailable>' " +
                    "declarationCount='0' declaration='<unavailable>' " +
                    "declarationOnRuntimeHostRoot='false' presentationMount='<unavailable>' " +
                    "presentationChildCount='0' runtimeHostReaderCount='0' " +
                    "declarationSubtreeReaderCount='0' presentationMountReaderCount='0' " +
                    "reason='Canonical joined Local Player Host or Actor Mount is unavailable.'",
                    host);
                return;
            }

            PlayerActorRuntimeHost[] runtimeHosts = host.ActorMount
                .GetComponentsInChildren<PlayerActorRuntimeHost>(true);
            PlayerActorRuntimeHost runtimeHost = runtimeHosts.Length == 1
                ? runtimeHosts[0]
                : null;

            PlayerActorDeclaration[] declarations = runtimeHost != null
                ? runtimeHost.GetComponentsInChildren<PlayerActorDeclaration>(true)
                : System.Array.Empty<PlayerActorDeclaration>();
            PlayerActorDeclaration declaration = runtimeHost != null
                ? runtimeHost.PlayerActorDeclaration
                : null;
            Transform presentationMount = runtimeHost != null
                ? runtimeHost.PresentationMount
                : null;

            PlayerGameplayInputReader[] runtimeHostReaders = runtimeHost != null
                ? runtimeHost.GetComponentsInChildren<PlayerGameplayInputReader>(true)
                : System.Array.Empty<PlayerGameplayInputReader>();
            PlayerGameplayInputReader[] declarationReaders = declaration != null
                ? declaration.GetComponentsInChildren<PlayerGameplayInputReader>(true)
                : System.Array.Empty<PlayerGameplayInputReader>();
            PlayerGameplayInputReader[] presentationReaders = presentationMount != null
                ? presentationMount.GetComponentsInChildren<PlayerGameplayInputReader>(true)
                : System.Array.Empty<PlayerGameplayInputReader>();

            var readerEvidence = new List<PlayerGameplayInputReader>();
            AddDistinctReaders(readerEvidence, runtimeHostReaders);
            AddDistinctReaders(readerEvidence, declarationReaders);
            AddDistinctReaders(readerEvidence, presentationReaders);

            var diagnostic = new StringBuilder(prefix);
            diagnostic.Append(" host='").Append(DescribeObject(host)).Append("'")
                .Append(" actorMount='").Append(DescribeObject(host.ActorMount)).Append("'")
                .Append(" runtimeHostCount='").Append(runtimeHosts.Length).Append("'")
                .Append(" runtimeHost='").Append(DescribeObject(runtimeHost)).Append("'")
                .Append(" declarationCount='").Append(declarations.Length).Append("'")
                .Append(" declaration='").Append(DescribeObject(declaration)).Append("'")
                .Append(" declarationOnRuntimeHostRoot='")
                .Append(runtimeHost != null && declaration != null &&
                    ReferenceEquals(declaration.gameObject, runtimeHost.gameObject))
                .Append("'")
                .Append(" declarationTransformEqualsRuntimeHostTransform='")
                .Append(runtimeHost != null && declaration != null &&
                    declaration.transform == runtimeHost.transform)
                .Append("'")
                .Append(" presentationMount='").Append(DescribeObject(presentationMount)).Append("'")
                .Append(" presentationMountIsChildOfRuntimeHost='")
                .Append(runtimeHost != null && presentationMount != null &&
                    presentationMount.IsChildOf(runtimeHost.transform))
                .Append("'")
                .Append(" presentationChildCount='")
                .Append(presentationMount != null ? presentationMount.childCount : 0)
                .Append("'")
                .Append(" runtimeHostReaderCount='").Append(runtimeHostReaders.Length).Append("'")
                .Append(" declarationSubtreeReaderCount='").Append(declarationReaders.Length).Append("'")
                .Append(" presentationMountReaderCount='").Append(presentationReaders.Length).Append("'");

            if (presentationMount != null)
            {
                for (int index = 0; index < presentationMount.childCount; index++)
                {
                    Transform child = presentationMount.GetChild(index);
                    diagnostic.Append(" presentationChild[").Append(index).Append("]='")
                        .Append(DescribeObject(child))
                        .Append("' activeSelf='").Append(child.gameObject.activeSelf)
                        .Append("' activeInHierarchy='").Append(child.gameObject.activeInHierarchy)
                        .Append("'");
                }
            }

            for (int index = 0; index < readerEvidence.Count; index++)
            {
                PlayerGameplayInputReader reader = readerEvidence[index];
                diagnostic.Append(" readerIndex='").Append(index)
                    .Append("' readerGameObject='").Append(DescribeObject(reader != null ? reader.gameObject : null))
                    .Append("' readerInstanceId='");
                if (reader != null)
                {
                    diagnostic.Append(reader.GetEntityId().ToString());
                }
                else
                {
                    diagnostic.Append("<missing>");
                }

                diagnostic.Append("' hasCurrentGameplayBinding='")
                    .Append(reader != null && reader.HasCurrentGameplayBinding)
                    .Append("' gameplayReady='").Append(reader != null && reader.GameplayReady)
                    .Append("' bindingTokenValid='").Append(reader != null && reader.CurrentBindingToken.IsValid)
                    .Append("' readerIsChildOfRuntimeHost='")
                    .Append(reader != null && runtimeHost != null &&
                        reader.transform.IsChildOf(runtimeHost.transform))
                    .Append("' readerIsChildOfDeclaration='")
                    .Append(reader != null && declaration != null &&
                        reader.transform.IsChildOf(declaration.transform))
                    .Append("'");
            }

            Debug.Log(diagnostic.ToString(), host);
        }

        private static void AddDistinctReaders(
            List<PlayerGameplayInputReader> destination,
            PlayerGameplayInputReader[] candidates)
        {
            for (int candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
            {
                PlayerGameplayInputReader candidate = candidates[candidateIndex];
                bool alreadyPresent = false;
                for (int existingIndex = 0; existingIndex < destination.Count; existingIndex++)
                {
                    if (ReferenceEquals(destination[existingIndex], candidate))
                    {
                        alreadyPresent = true;
                        break;
                    }
                }

                if (!alreadyPresent)
                {
                    destination.Add(candidate);
                }
            }
        }

        private static string DescribeObject(UnityEngine.Object value)
        {
            return value != null ? value.name : "<missing>";
        }

        private static string DescribeGameplayReadyReaderFailure(
            PlayerQaPanel fixture,
            Result result)
        {
            if (!TryGetCurrentGameplayReaders(
                    result != null ? result.PlayerOneHost : null,
                    out PlayerGameplayInputReader[] readers,
                    out string topologyIssue))
            {
                return $"GameplayReady reader not found because canonical topology is invalid: {topologyIssue}";
            }

            if (readers.Length == 0)
            {
                return "GameplayReady reader is absent from the Default Presentation hierarchy.";
            }

            if (readers.Length != 1 || readers[0] == null)
            {
                return $"GameplayReady reader topology is invalid: expected exactly one reader, found '{readers.Length}'.";
            }

            PlayerGameplayInputReader reader = readers[0];
            if (!reader.HasCurrentGameplayBinding)
            {
                return "GameplayReady reader was found but remains unbound.";
            }

            if (!reader.CurrentBindingToken.IsValid)
            {
                return "GameplayReady reader is bound but its binding token is invalid.";
            }

            if (!TryFindSlot(
                    fixture,
                    ExpectedSlotId(fixture),
                    out PlayerSessionScopedSlotObservation slot) ||
                !slot.HasGameplayAdmissionEvidence ||
                reader.CurrentBindingToken != slot.GameplayAdmission.InputBindingToken)
            {
                return "GameplayReady reader is bound but its binding token does not match current gameplay admission.";
            }

            if (!reader.GameplayReady)
            {
                return IsCurrentPresentationInactive(result != null ? result.PlayerOneHost : null)
                    ? "GameplayReady reader is bound with a valid token but its Presentation occurrence is inactive."
                    : "GameplayReady reader is bound with a valid token but is not GameplayReady.";
            }

            return "GameplayReady reader verification did not reach the expected terminal state.";
        }

        private static bool TryResolveCurrentGameplayReader(
            LocalPlayerHostAuthoring localPlayerHost,
            out PlayerGameplayInputReader reader,
            out string issue)
        {
            reader = null;
            if (!TryGetCurrentGameplayReaders(localPlayerHost, out PlayerGameplayInputReader[] readers, out issue))
            {
                return false;
            }

            if (readers.Length != 1)
            {
                issue =
                    $"Canonical Player Presentation requires exactly one PlayerGameplayInputReader. Found '{readers.Length}'.";
                return false;
            }

            reader = readers[0];
            return reader != null;
        }

        private static bool TryResolveCurrentPresentation(
            LocalPlayerHostAuthoring localPlayerHost,
            out PlayerActorRuntimeHost runtimeHost,
            out GameObject presentation)
        {
            runtimeHost = null;
            presentation = null;
            if (localPlayerHost == null || localPlayerHost.ActorMount == null)
            {
                return false;
            }

            PlayerActorRuntimeHost[] runtimeHosts =
                localPlayerHost.ActorMount.GetComponentsInChildren<PlayerActorRuntimeHost>(true);
            if (runtimeHosts.Length != 1 || runtimeHosts[0] == null ||
                runtimeHosts[0].PresentationMount == null ||
                runtimeHosts[0].PresentationMount.childCount != 1)
            {
                return false;
            }

            runtimeHost = runtimeHosts[0];
            presentation = runtimeHost.PresentationMount.GetChild(0).gameObject;
            return presentation != null;
        }

        private static bool TryGetCurrentGameplayReaderCount(
            LocalPlayerHostAuthoring localPlayerHost,
            out int readerCount,
            out string issue)
        {
            readerCount = 0;
            if (!TryGetCurrentGameplayReaders(localPlayerHost, out PlayerGameplayInputReader[] readers, out issue))
            {
                return false;
            }

            readerCount = readers.Length;
            return true;
        }

        private static bool TryGetCurrentGameplayReaders(
            LocalPlayerHostAuthoring localPlayerHost,
            out PlayerGameplayInputReader[] readers,
            out string issue)
        {
            readers = System.Array.Empty<PlayerGameplayInputReader>();
            issue = string.Empty;
            if (localPlayerHost == null || localPlayerHost.ActorMount == null)
            {
                issue = "Joined Local Player Host or its Actor Mount is unavailable.";
                return false;
            }

            PlayerActorRuntimeHost[] runtimeHosts = localPlayerHost.ActorMount
                .GetComponentsInChildren<PlayerActorRuntimeHost>(true);
            if (runtimeHosts.Length != 1 || runtimeHosts[0] == null ||
                runtimeHosts[0].transform.parent != localPlayerHost.ActorMount)
            {
                issue =
                    $"Joined Local Player Host requires exactly one direct PlayerActorRuntimeHost. Found '{runtimeHosts.Length}'.";
                return false;
            }

            PlayerActorRuntimeHost runtimeHost = runtimeHosts[0];
            if (!runtimeHost.TryValidateConfiguration(out issue) ||
                runtimeHost.PresentationMount == null ||
                runtimeHost.PresentationMount.parent != runtimeHost.transform ||
                runtimeHost.PresentationMount.childCount != 1)
            {
                issue = string.IsNullOrEmpty(issue)
                    ? "Canonical Player Actor Runtime Host requires one direct Presentation instance."
                    : issue;
                return false;
            }

            readers = runtimeHost.PresentationMount
                .GetComponentsInChildren<PlayerGameplayInputReader>(true);
            return true;
        }

        private static bool IsCurrentPresentationInactive(
            LocalPlayerHostAuthoring localPlayerHost)
        {
            if (localPlayerHost == null || localPlayerHost.ActorMount == null)
            {
                return false;
            }

            PlayerActorRuntimeHost[] runtimeHosts = localPlayerHost.ActorMount
                .GetComponentsInChildren<PlayerActorRuntimeHost>(true);
            if (runtimeHosts.Length != 1 || runtimeHosts[0] == null ||
                runtimeHosts[0].PresentationMount == null ||
                runtimeHosts[0].PresentationMount.childCount != 1)
            {
                return false;
            }

            return !runtimeHosts[0].PresentationMount.GetChild(0).gameObject.activeSelf;
        }

        private static bool TryGetAccess(
            PlayerQaPanel fixture,
            out IPlayerSessionScopedAccess access,
            out string issue)
        {
            access = null;
            issue = "probe missing";
            return fixture.Probe != null &&
                fixture.Probe.TryGetAccess(out access, out issue) &&
                access != null &&
                access.Snapshot.IsAvailable;
        }

        private static bool TryFindSlot(
            PlayerQaPanel fixture,
            PlayerSlotId slotId,
            out PlayerSessionScopedSlotObservation slot)
        {
            slot = default;
            if (fixture.Observer == null ||
                !fixture.Observer.TryGetObservation(out PlayerSessionScopedObservationSnapshot observation) ||
                observation == null ||
                !observation.IsAvailable)
            {
                return false;
            }

            for (int index = 0; index < observation.Slots.Count; index++)
            {
                PlayerSessionScopedSlotObservation candidate = observation.Slots[index];
                if (candidate.Slot.PlayerSlotId == slotId)
                {
                    slot = candidate;
                    return true;
                }
            }

            return false;
        }

        private static bool TryFindSlot(
            PlayerSessionScopedObservationSnapshot observation,
            PlayerSlotId slotId,
            out PlayerSessionScopedSlotObservation slot)
        {
            slot = default;
            if (observation == null || !observation.IsAvailable || !slotId.IsValid)
            {
                return false;
            }

            for (int index = 0; index < observation.Slots.Count; index++)
            {
                PlayerSessionScopedSlotObservation candidate = observation.Slots[index];
                if (candidate.Slot.PlayerSlotId == slotId)
                {
                    slot = candidate;
                    return true;
                }
            }

            return false;
        }

        private static PlayerSessionScopedSlotObservation FindSlot(
            PlayerSessionScopedObservationSnapshot observation,
            PlayerSlotId slotId)
        {
            for (int index = 0; index < observation.Slots.Count; index++)
            {
                PlayerSessionScopedSlotObservation candidate = observation.Slots[index];
                if (candidate.Slot.PlayerSlotId == slotId)
                {
                    return candidate;
                }
            }

            return default;
        }

        private static bool TryFindSingleAvailableSlot(
            PlayerSessionScopedObservationSnapshot observation,
            PlayerSlotId excludedSlotId,
            out PlayerSessionScopedSlotObservation slot)
        {
            slot = default;
            bool found = false;
            if (observation == null || !observation.IsAvailable)
            {
                return false;
            }

            for (int index = 0; index < observation.Slots.Count; index++)
            {
                PlayerSessionScopedSlotObservation candidate = observation.Slots[index];
                if (candidate.Slot.PlayerSlotId == excludedSlotId ||
                    candidate.Slot.AllocationState != PlayerSlotAllocationState.Available)
                {
                    continue;
                }

                if (found)
                {
                    slot = default;
                    return false;
                }

                slot = candidate;
                found = true;
            }

            return found && slot.Slot.PlayerSlotId.IsValid;
        }

        private static IEnumerator WaitForSlot(
            Result result,
            string caseId,
            PlayerQaPanel fixture,
            Func<PlayerSessionScopedSlotObservation, bool> predicate,
            PlayerSlotId? slotId = null)
        {
            PlayerSlotId expected = slotId ?? ExpectedSlotId(fixture);
            for (int frame = 0; frame < FrameBudget; frame++)
            {
                if (TryFindSlot(
                        fixture,
                        expected,
                        out PlayerSessionScopedSlotObservation slot) &&
                    predicate(slot))
                {
                    yield break;
                }

                yield return null;
            }

            if (!result.Ok)
            {
                yield break;
            }

            string diagnostic = DescribeSlotObservation(fixture, expected, caseId, result.PlayerOneHost);
            UnityEngine.Debug.LogError(
                $"[QA_PLAYER_SLOT_OBSERVATION] case='{caseId}' {diagnostic}",
                fixture);
            result.Fail(
                caseId,
                "condition met",
                diagnostic,
                "Timed out waiting for the expected Player Slot observation.");
        }

        private static string DescribeSlotObservation(
            PlayerQaPanel fixture,
            PlayerSlotId expected,
            string caseId,
            LocalPlayerHostAuthoring retainedQaHost)
        {
            string expectedText = expected.IsValid ? expected.StableText : "<invalid>";
            if (fixture == null || fixture.Observer == null)
            {
                return $"status='timeout' expectedSlot='{expectedText}' observer='missing' " +
                       "observationAvailable='false' observedSlots='0' slotFound='false'";
            }

            if (!fixture.Observer.TryGetObservation(
                    out PlayerSessionScopedObservationSnapshot observation) ||
                observation == null)
            {
                return $"status='timeout' expectedSlot='{expectedText}' observer='available' " +
                       "observationAvailable='false' observedSlots='0' slotFound='false'";
            }

            int observedSlots = observation.Slots.Count;
            for (int index = 0; index < observedSlots; index++)
            {
                PlayerSessionScopedSlotObservation candidate = observation.Slots[index];
                if (candidate.Slot.PlayerSlotId != expected)
                {
                    continue;
                }

                string slotText = candidate.Slot.PlayerSlotId.IsValid
                    ? candidate.Slot.PlayerSlotId.StableText
                    : "<invalid>";
                string assignmentOrigin = candidate.HasHostEvidence
                    ? candidate.HostEvidence.AssignmentOrigin.ToString()
                    : "<none>";
                LogActorLifecycleContextDiagnostic(caseId, fixture, observation, candidate, retainedQaHost);
                return $"status='timeout' expectedSlot='{expectedText}' " +
                       $"observationAvailable='{observation.IsAvailable}' " +
                       $"observedSlots='{observedSlots}' slotFound='true' " +
                       $"slot='{slotText}' isJoined='{candidate.IsJoined}' " +
                       $"allocationState='{candidate.Slot.AllocationState}' " +
                       $"hasHostEvidence='{candidate.HasHostEvidence}' " +
                       $"assignmentOrigin='{assignmentOrigin}'";
            }

            return $"status='timeout' expectedSlot='{expectedText}' " +
                   $"observationAvailable='{observation.IsAvailable}' " +
                   $"observedSlots='{observedSlots}' slotFound='false'";
        }

        private static void LogActorLifecycleContextDiagnostic(
            string caseId, PlayerQaPanel fixture,
            PlayerSessionScopedObservationSnapshot observation,
            PlayerSessionScopedSlotObservation slot, LocalPlayerHostAuthoring retainedQaHost)
        {
            if (caseId != "actor-lifecycle")
            {
                return;
            }

            string host = retainedQaHost != null
                ? $"{retainedQaHost.name}#{retainedQaHost.GetEntityId()}"
                : ReferenceEquals(retainedQaHost, null) ? "null" : "destroyed";
            string actor = slot.HasCurrentActorEvidence
                ? slot.CurrentActor.ActorEvidence.ToDiagnosticString()
                : "unavailable";
            UnityEngine.Debug.Log(
                $"[QA_PLAYER_CONTEXT_DIAG] phase='timeout' operation='actor-lifecycle-precondition' " +
                $"slot='{slot.Slot.PlayerSlotId.StableText}' activity='{observation.ActivityOwner.StableText}' " +
                $"occurrence='{observation.ActivityOccurrence}' frame='{Time.frameCount}' " +
                $"joined='{slot.IsJoined}' selectedActorMatchesExpected='{slot.Slot.SelectedActorProfile == fixture.DefaultActor}' " +
                $"selectedActorActual='{EscapeDiagnosticValue(DescribeObject(slot.Slot.SelectedActorProfile))}' selectedActorExpected='{EscapeDiagnosticValue(DescribeObject(fixture.DefaultActor))}' " +
                $"logicalActorPrepared='{slot.IsLogicalActorPrepared}' physicallyMaterialized='{slot.IsPhysicallyMaterialized}' " +
                $"hasCurrentActor='{slot.CurrentActor.HasCurrentActor}' currentActor='{EscapeDiagnosticValue(actor)}' " +
                $"hasHostEvidence='{slot.HasHostEvidence}' assignmentOriginActual='{slot.HostEvidence.AssignmentOrigin}' " +
                $"assignmentOriginExpected='{PlayerSlotAssignmentOrigin.ManagerProvisioned}' " +
                $"assignmentOriginMatchesExpected='{slot.HostEvidence.AssignmentOrigin == PlayerSlotAssignmentOrigin.ManagerProvisioned}' " +
                $"assignmentToken='{slot.HostEvidence.AssignmentToken.StableText}' bindingIdentity='{slot.HostEvidence.HostBindingIdentity.StableText}' " +
                $"qaRetainedHost='{EscapeDiagnosticValue(host)}' qaHostMatchesSlot='{retainedQaHost != null && retainedQaHost.HasJoinedSlot && retainedQaHost.JoinedPlayerSlotId == slot.Slot.PlayerSlotId}' " +
                $"hostEvidenceSource='{EscapeDiagnosticValue(slot.HostEvidence.Source)}' hostEvidenceReason='{EscapeDiagnosticValue(slot.HostEvidence.Reason)}'",
                fixture);
        }

        private static IEnumerator RequestActivity(
            Result result,
            string caseId,
            ActivityRequestTrigger trigger,
            bool expectSuccess,
            string failure)
        {
            Require(result, caseId,
                trigger != null && trigger.TargetActivity != null,
                "configured Activity trigger",
                trigger == null ? "missing" : "target missing",
                failure);
            if (!result.Ok)
            {
                yield break;
            }

            trigger.RequestActivity();
            yield return WaitFor(
                result,
                caseId,
                () => !trigger.IsRequestInFlight &&
                      (trigger.LastRequestSucceeded ||
                       trigger.LastRequestFailed ||
                       trigger.LastRequestIgnored),
                "Timed out waiting for the Activity request outcome.");
            if (!result.Ok)
            {
                yield break;
            }

            bool expectedOutcome = expectSuccess
                ? trigger.LastRequestSucceeded
                : trigger.LastRequestFailed;
            Require(result, caseId,
                expectedOutcome,
                expectSuccess ? "successful Activity request" : "failed Activity request",
                $"outcome={trigger.LastOutcome} message={trigger.LastMessage}",
                failure);
        }

        private static IEnumerator WaitFor(
            Result result,
            string caseId,
            Func<bool> predicate,
            string timeoutMessage)
        {
            for (int frame = 0; frame < FrameBudget; frame++)
            {
                if (predicate())
                {
                    yield break;
                }

                yield return null;
            }

            if (result.Ok)
            {
                result.Fail(caseId, "condition met", "timeout", timeoutMessage);
            }
        }

        private static PlayerSlotId ExpectedSlotId(PlayerQaPanel fixture)
        {
            if (fixture.PlayerOneSlot != null &&
                fixture.PlayerOneSlot.TryGetPlayerSlotId(out PlayerSlotId slotId, out _))
            {
                return slotId;
            }

            return default;
        }

        private static PlayerSlotId ExpectedSlotTwoId(PlayerQaPanel fixture)
        {
            if (fixture.PlayerTwoSlot != null &&
                fixture.PlayerTwoSlot.TryGetPlayerSlotId(out PlayerSlotId slotId, out _))
            {
                return slotId;
            }

            return default;
        }

        private static void Require(
            Result result,
            string caseId,
            bool condition,
            string expected,
            string actual,
            string message)
        {
            if (!condition && result.Ok)
            {
                result.Fail(caseId, expected, actual, message);
            }
        }
    }
}
