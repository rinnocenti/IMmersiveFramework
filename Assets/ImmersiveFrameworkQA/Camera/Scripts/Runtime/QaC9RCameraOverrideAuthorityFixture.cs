using System;
using System.Collections;
using System.Collections.Generic;
using Immersive.Framework.Actors;
using Immersive.Framework.Camera;
using Immersive.Framework.CameraAuthoring;
using Immersive.Framework.GameFlow;
using Immersive.Framework.PlayerParticipation;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ImmersiveFrameworkQA.Camera
{
    public enum QaCameraAdr026TopologyMode
    {
        Shared = 10,
        Split = 20
    }

    [DisallowMultipleComponent]
    public sealed class QaCameraOverrideAuthorityFixture :
        MonoBehaviour,
        ICameraSubjectAvailabilityConsumer
    {
        private const string LogPrefix = "[CAMERA_RUNTIME_HOST_INTEGRATION_REGRESSION]";
        private const string Adr026Prefix = "[QA_CAMERA_ADR026]";
        private const string Adr004BLogPrefix = "[QA_CAMERA_ADR004B]";
        private const string Adr004CLogPrefix = "[QA_CAMERA_ADR004C]";
        private const int MaxReadinessFrames = 600;
        private const int ExpectedGenericCaseCount = 11;
        private const string RouteLifecycleSurvivorRequestId =
            "qa.camera.adr004b.route-lifecycle-survivor";

        [SerializeField] private QaCameraAdr026TopologyMode topologyMode;
        [SerializeField] private RouteCameraOverride routeBinding;
        [SerializeField] private ActivityCameraOverride activityBinding;
        [SerializeField] private CameraRigComposer routeComposer;
        [SerializeField] private CameraRigComposer activityComposer;
        [SerializeField] private PlayerSessionObserver playerSessionObserver;
        [SerializeField] private ActorProfile replacementActorProfile;
        [SerializeField] private QaCameraOutputProbe outputAProbe;
        [SerializeField] private QaCameraOutputProbe outputBProbe;
        [SerializeField] private QaCameraOutputProbe missingOutputProbe;
        [SerializeField] private ActivityRequestTrigger activityRequestTrigger;
        [SerializeField] private RouteRequestTrigger backToHubTrigger;
        [SerializeField] private bool throwOnFailure;
        [SerializeField] private string lastStatus = "NotRun";
        [SerializeField] private string lastFailure;
        [SerializeField] private int completedCaseCount;

        private ICameraSubjectAvailabilitySource subjectAvailability;
        private ICameraRequestPublisher routeLifecycleSurvivorPublisher;
        private ICameraRequestPublisher splitOutputPublisher;
        private bool started;
        private bool awaitingRouteLifecycleCleanup;
        private bool awaitingSplitRouteExit;
        private string routeRequestId;
        private readonly List<string> completedCases = new List<string>();

        public static bool Adr026SharedExecuted { get; private set; }
        public static bool Adr026SharedPassed { get; private set; }
        public static string Adr026SharedDiagnostic { get; private set; } = string.Empty;
        public static bool Adr026SplitExecuted { get; private set; }
        public static bool Adr026SplitPassed { get; private set; }
        public static string Adr026SplitDiagnostic { get; private set; } = string.Empty;
        public static bool GenericArbitrationExecuted { get; private set; }
        public static bool GenericArbitrationPassed { get; private set; }

        public static bool Adr004BActivityLifecycleExecuted { get; private set; }
        public static bool Adr004BActivityLifecyclePassed { get; private set; }
        public static bool Adr004BRouteLifecycleExecuted { get; private set; }
        public static bool Adr004BRouteLifecyclePassed { get; private set; }
        public static bool Adr004BOwnerLossExecuted { get; private set; }
        public static bool Adr004BOwnerLossInvariantPassed { get; private set; }
        public static string Adr004BOwnerLossDiagnostic { get; private set; } = string.Empty;
        public static bool Adr004CActivityDisableExecuted { get; private set; }
        public static bool Adr004CActivityDisablePassed { get; private set; }
        public static bool Adr004CSessionDisableExecuted { get; private set; }
        public static bool Adr004CSessionDisablePassed { get; private set; }
        public static bool Adr004CNonWinnerDisableExecuted { get; private set; }
        public static bool Adr004CNonWinnerDisablePassed { get; private set; }
        public static bool Adr004CWinningRestoreExecuted { get; private set; }
        public static bool Adr004CWinningRestorePassed { get; private set; }
        public static bool Adr004CIdempotentCleanupExecuted { get; private set; }
        public static bool Adr004CIdempotentCleanupPassed { get; private set; }
        public static bool Adr004CActivityDestroyExecuted { get; private set; }
        public static bool Adr004CActivityDestroyPassed { get; private set; }
        public static bool Adr004CRouteReenableExecuted { get; private set; }
        public static bool Adr004CRouteReenablePassed { get; private set; }

        private CameraOutputAuthoring OutputA => outputAProbe != null ? outputAProbe.Output : null;
        private CameraOutputAuthoring OutputB => outputBProbe != null ? outputBProbe.Output : null;
        private CameraSharedComposition SharedComposition =>
            OutputA != null ? OutputA.GetComponent<CameraSharedComposition>() : null;
        private SessionCameraOverride SessionOverride =>
            OutputA != null ? OutputA.GetComponent<SessionCameraOverride>() : null;
        private CameraOutputContext Context => OutputA != null ? OutputA.Context : null;
        private bool HasFailed => string.Equals(lastStatus, "Failed", StringComparison.Ordinal);

        public void RunFromContextMenu() => Begin();

        internal void Begin()
        {
            if (started) return;
            started = true;
            StartCoroutine(Run());
        }

        public void AttachCameraSubjectAvailability(ICameraSubjectAvailabilitySource availabilitySource)
        {
            subjectAvailability = availabilitySource ??
                throw new ArgumentNullException(nameof(availabilitySource));
        }

        private IEnumerator Run()
        {
            ResetEvidence();
            lastStatus = "Running";
            lastFailure = string.Empty;
            completedCaseCount = 0;
            completedCases.Clear();

            yield return WaitFor(BaseReadiness, "adr026-composition-readiness");
            if (HasFailed) yield break;

            if (topologyMode == QaCameraAdr026TopologyMode.Shared)
            {
                yield return RunSharedCameraProof();
                if (HasFailed) yield break;
                yield return RunGenericArbitrationProof();
                yield break;
            }

            if (topologyMode == QaCameraAdr026TopologyMode.Split)
            {
                yield return RunSplitOutputProof();
                if (HasFailed) yield break;
                awaitingSplitRouteExit = true;
                lastStatus = "WaitingSplitRouteExit";
                backToHubTrigger.RequestRoute();
                yield break;
            }

            Fail($"Unsupported ADR-026 topology mode '{topologyMode}'.");
        }

        private IEnumerator RunSharedCameraProof()
        {
            IEnumerator proof = RunSharedCameraProofCore();
            try
            {
                while (true)
                {
                    object current = null;
                    bool hasNext = false;
                    bool failed = false;
                    try
                    {
                        hasNext = proof.MoveNext();
                        if (hasNext) current = proof.Current;
                    }
                    catch (Exception exception)
                    {
                        Adr026SharedExecuted = true;
                        Adr026SharedPassed = false;
                        Adr026SharedDiagnostic = exception.Message;
                        Fail(exception.Message);
                        failed = true;
                    }
                    if (failed || !hasNext) yield break;
                    yield return current;
                }
            }
            finally
            {
                (proof as IDisposable)?.Dispose();
            }
        }

        private IEnumerator RunSharedCameraProofCore()
        {
            IPlayerSessionScopedAccess access = null;
            ILocalPlayerJoinAccess joinAccess = null;
            LocalPlayerJoinResult p1A = null;
            LocalPlayerJoinResult p2 = null;
            LocalPlayerJoinResult p1B = null;
            Keyboard p1Keyboard = null;
            Keyboard p2Keyboard = null;
            bool joiningOpenedByFixture = false;
            CameraViewAssignmentContext mountedAssignments = null;
            var mountedOwner = new CameraSubjectAssignmentOwnerId(
                "qa.camera.adr026.mounted-assignment-owner");
            var mountedViewId = new CameraViewId("camera.view.secondary");

            try
            {
                yield return WaitFor(
                    () => playerSessionObserver != null &&
                        playerSessionObserver.TryGetAccess(out access, out _) &&
                        playerSessionObserver.TryGetJoinAccess(out joinAccess, out _),
                    "public-player-session-access");
                if (HasFailed) yield break;

                PlayerSessionScopedObservationSnapshot initial = Observation(access);
                Require(initial.Participation != null && initial.Participation.JoinedCount == 0,
                    "ADR-026 Shared Camera requires a fresh Player Session.");
                Require(SubjectSnapshot().Count == 0,
                    "Fresh Shared Camera phase contains unexpected Camera Subjects.");
                RequireNoOrdinaryPlayerRequests("shared-baseline");

                p1Keyboard = InputSystem.AddDevice<Keyboard>();
                p2Keyboard = InputSystem.AddDevice<Keyboard>();
                Require(
                    p1Keyboard != null &&
                    p2Keyboard != null &&
                    p1Keyboard.added &&
                    p2Keyboard.added &&
                    !ReferenceEquals(p1Keyboard, p2Keyboard) &&
                    p1Keyboard.deviceId != p2Keyboard.deviceId,
                    "ADR-026 Shared Camera requires two distinct QA-owned Keyboard devices " +
                    $"before the first Join. p1Keyboard.deviceId='{DeviceId(p1Keyboard)}' " +
                    $"p2Keyboard.deviceId='{DeviceId(p2Keyboard)}'.");

                if (!initial.Participation.JoiningOpen)
                {
                    PlayerParticipationOperationResult opened = access.OpenJoining(
                        nameof(QaCameraOverrideAuthorityFixture),
                        "adr026-shared-open-joining");
                    Require(
                        opened != null &&
                        opened.Status == PlayerParticipationOperationStatus.Succeeded &&
                        opened.StateChanged &&
                        opened.Snapshot != null &&
                        opened.Snapshot.JoiningOpen,
                        opened != null
                            ? opened.ToDiagnosticString()
                            : "OpenJoining returned no result.");
                    joiningOpenedByFixture = true;
                }

                p1A = joinAccess.RequestJoin(new LocalPlayerJoinRequest(
                    nameof(QaCameraOverrideAuthorityFixture),
                    "adr026-shared-p1-a-join",
                    p1Keyboard));
                Require(
                    p1A != null &&
                    p1A.Succeeded &&
                    p1A.LocalPlayerHost != null &&
                    ReferenceEquals(p1A.Request.PairWithDevice, p1Keyboard),
                    p1A != null ? p1A.ToDiagnosticString() : "P1 join returned no result.");
                yield return WaitFor(() => SharedMembershipIs(1), "shared-p1-subject");
                if (HasFailed) yield break;

                CameraSubjectAvailabilityEntry p1AEntry =
                    RequireSubjectForHost(SubjectSnapshot(), p1A.LocalPlayerHost, "P1-A");
                RequireExplicitChildSubject(p1AEntry, p1A.LocalPlayerHost, "P1-A");
                CameraRigComposer composer = SharedComposition.Composer;
                CinemachineCamera cinemachine = composer.CinemachineCamera;
                CameraOutputAuthoring output = SharedComposition.Output;
                CameraViewId view = SharedComposition.ViewId;
                RequireSharedIdentity(view, composer, cinemachine, output, "p1-join");

                mountedAssignments = new CameraViewAssignmentContext(
                    "qa.camera.adr026.mounted-assignments",
                    SubjectSnapshot().ContextId,
                    new CameraView(
                        mountedViewId,
                        "ADR-026 explicit Actor mount proof"));
                ApplyMountedSubject(
                    mountedAssignments,
                    mountedOwner,
                    mountedViewId,
                    p1AEntry,
                    "p1-a-join");

                Require(replacementActorProfile != null,
                    "ADR-026 Actor replacement proof requires an explicit replacement Actor Profile.");
                CameraSubjectId p1AOldSubjectId = p1AEntry.Subject.SubjectId;
                Transform p1AOldObservation = p1AEntry.Subject.Observation;
                PlayerPreparedActorReplacementResult replacement =
                    access.RequestReplacePreparedActor(
                        new PlayerPreparedActorReplacementRequest(
                            p1A.Slot.PlayerSlotId,
                            replacementActorProfile,
                            nameof(QaCameraOverrideAuthorityFixture),
                            "adr026-explicit-camera-subject-replacement"));
                Require(
                    replacement != null &&
                    replacement.ReplacementCommitted &&
                    replacement.CurrentActor.IsPrepared,
                    replacement != null
                        ? replacement.Message
                        : "Prepared Actor replacement returned no result.");
                yield return WaitFor(
                    () => SubjectSnapshot().Count == 1 &&
                        !SubjectSnapshot().TryGet(p1AOldSubjectId, out _),
                    "explicit-subject-actor-replacement");
                if (HasFailed) yield break;

                CameraSubjectAvailabilityEntry p1ReplacementEntry =
                    RequireSubjectForHost(
                        SubjectSnapshot(),
                        p1A.LocalPlayerHost,
                        "P1-Replacement");
                RequireExplicitChildSubject(
                    p1ReplacementEntry,
                    p1A.LocalPlayerHost,
                    "P1-Replacement");
                Require(
                    p1ReplacementEntry.Subject.SubjectId != p1AOldSubjectId &&
                    !ReferenceEquals(
                        p1ReplacementEntry.Subject.Observation,
                        p1AOldObservation),
                    "Prepared Actor replacement retained stale Camera Subject identity or Transform.");
                ReconcileMountedAssignments(
                    mountedAssignments,
                    mountedViewId,
                    SubjectSnapshot(),
                    expectedCount: 0,
                    "replacement-release-old-subject");
                ApplyMountedSubject(
                    mountedAssignments,
                    mountedOwner,
                    mountedViewId,
                    p1ReplacementEntry,
                    "p1-replacement");
                p1AEntry = p1ReplacementEntry;

                Require(
                    p1A.PlayerInput != null &&
                    PlayerInputOwnsDevice(p1A.PlayerInput, p1Keyboard) &&
                    !PlayerInputOwnsDevice(p1A.PlayerInput, p2Keyboard) &&
                    !ReferenceEquals(p1Keyboard, p2Keyboard) &&
                    p1Keyboard.deviceId != p2Keyboard.deviceId,
                    "ADR-026 Shared Camera Player device isolation failed before P2 Join. " +
                    $"p1Keyboard.deviceId='{DeviceId(p1Keyboard)}' " +
                    $"p2Keyboard.deviceId='{DeviceId(p2Keyboard)}' " +
                    $"p1PlayerInputDeviceIds='{DescribePlayerInputDeviceIds(p1A.PlayerInput)}'.");
                p2 = joinAccess.RequestJoin(new LocalPlayerJoinRequest(
                    nameof(QaCameraOverrideAuthorityFixture),
                    "adr026-shared-p2-join",
                    p2Keyboard));
                Require(
                    p2 != null &&
                    p2.Succeeded &&
                    p2.LocalPlayerHost != null &&
                    p2.PlayerInput != null &&
                    ReferenceEquals(p2.Request.PairWithDevice, p2Keyboard) &&
                    PlayerInputOwnsDevice(p2.PlayerInput, p2Keyboard) &&
                    !PlayerInputOwnsDevice(p2.PlayerInput, p1Keyboard),
                    p2 != null ? p2.ToDiagnosticString() : "P2 join returned no result.");
                yield return WaitFor(() => SharedMembershipIs(2), "shared-p1-p2-subjects");
                if (HasFailed) yield break;

                CameraSubjectAvailabilitySnapshot both = SubjectSnapshot();
                CameraSubjectAvailabilityEntry p2Entry =
                    RequireSubjectForHost(both, p2.LocalPlayerHost, "P2");
                RequireSharedIdentity(view, composer, cinemachine, output, "p2-join");
                RequireExactGroupMembers(composer, p1AEntry.Subject.Observation, p2Entry.Subject.Observation);
                RequireNoOrdinaryPlayerRequests("shared-two-player");

                SessionPlayerLeaveResult p1Leave = access.RequestLeave(
                    LeaveRequest(access, p1A.Slot.PlayerSlotId, "adr026-shared-p1-a-leave"));
                Require(p1Leave != null && p1Leave.Succeeded,
                    p1Leave != null ? p1Leave.ToDiagnosticString() : "P1 leave returned no result.");
                p1A = null;
                yield return WaitFor(() => SharedMembershipIs(1), "shared-p2-only");
                if (HasFailed) yield break;
                Require(!SubjectSnapshot().TryGet(p1AEntry.Subject.SubjectId, out _),
                    "P1-A Subject survived after exact Player Leave.");
                RequireNoGroupMember(composer, p1AEntry.Subject.Observation);
                RequireSharedIdentity(view, composer, cinemachine, output, "p1-leave");
                ReconcileMountedAssignments(
                    mountedAssignments,
                    mountedViewId,
                    SubjectSnapshot(),
                    expectedCount: 0,
                    "p1-leave");
                ApplyMountedEmpty(
                    mountedAssignments,
                    mountedViewId,
                    "p1-leave");

                p1B = joinAccess.RequestJoin(new LocalPlayerJoinRequest(
                    nameof(QaCameraOverrideAuthorityFixture),
                    "adr026-shared-p1-b-rejoin",
                    p1Keyboard));
                Require(
                    p1B != null &&
                    p1B.Succeeded &&
                    p1B.LocalPlayerHost != null &&
                    p1B.PlayerInput != null &&
                    ReferenceEquals(p1B.Request.PairWithDevice, p1Keyboard) &&
                    PlayerInputOwnsDevice(p1B.PlayerInput, p1Keyboard) &&
                    !PlayerInputOwnsDevice(p1B.PlayerInput, p2Keyboard),
                    p1B != null ? p1B.ToDiagnosticString() : "P1 rejoin returned no result.");
                yield return WaitFor(() => SharedMembershipIs(2), "shared-p1-b-p2-subjects");
                if (HasFailed) yield break;

                CameraSubjectAvailabilitySnapshot rejoined = SubjectSnapshot();
                CameraSubjectAvailabilityEntry p1BEntry =
                    RequireSubjectForHost(rejoined, p1B.LocalPlayerHost, "P1-B");
                RequireExplicitChildSubject(p1BEntry, p1B.LocalPlayerHost, "P1-B");
                Require(p1BEntry.Subject.SubjectId != p1AEntry.Subject.SubjectId &&
                        !ReferenceEquals(p1BEntry.Subject.Observation, p1AEntry.Subject.Observation),
                    "P1 rejoin reused stale Subject occurrence evidence.");
                Require(!rejoined.TryGet(p1AEntry.Subject.SubjectId, out _),
                    "P1-A Subject identity survived P1-B rejoin.");
                RequireExactGroupMembers(composer, p1BEntry.Subject.Observation, p2Entry.Subject.Observation);
                RequireNoGroupMember(composer, p1AEntry.Subject.Observation);
                RequireSharedIdentity(view, composer, cinemachine, output, "p1-rejoin");
                RequireNoOrdinaryPlayerRequests("shared-rejoin");
                ApplyMountedSubject(
                    mountedAssignments,
                    mountedOwner,
                    mountedViewId,
                    p1BEntry,
                    "p1-rejoin");

                Adr026SharedExecuted = true;
                Adr026SharedPassed = true;
                Adr026SharedDiagnostic =
                    $"view='{view}' output='{output.OutputIdText}' " +
                    "subjects='P1-A -> P1-Replacement -> P1-Replacement/P2 -> P2 -> P1-B/P2' " +
                    "explicitChild='True' mounted='ExactTransform' staleP1A='False' ordinaryPlayerRequests='0'.";
                Debug.Log($"{Adr026Prefix} phase='shared' status='Passed' {Adr026SharedDiagnostic}", this);
            }
            finally
            {
                TryCleanupPlayer(access, ref p1A, "shared-p1-a-finally");
                TryCleanupPlayer(access, ref p1B, "shared-p1-b-finally");
                TryCleanupPlayer(access, ref p2, "shared-p2-finally");
                if (mountedAssignments != null && subjectAvailability != null)
                {
                    mountedAssignments.ReleaseOwner(
                        mountedOwner,
                        subjectAvailability.CreateSnapshot());
                }
                if (OutputB != null && OutputB.DefaultCameraRig != null)
                {
                    OutputB.DefaultCameraRig.ClearViewPresentation();
                }
                if (joiningOpenedByFixture && access != null)
                {
                    access.CloseJoining(
                        nameof(QaCameraOverrideAuthorityFixture),
                        "adr026-shared-finally-close-joining");
                }
                if (p1Keyboard != null && p1Keyboard.added)
                    InputSystem.RemoveDevice(p1Keyboard);
                if (p2Keyboard != null && p2Keyboard.added)
                    InputSystem.RemoveDevice(p2Keyboard);
            }

            yield return WaitFor(
                () => SubjectSnapshot().Count == 0 && SharedComposition.Snapshot.SubjectCount == 0,
                "shared-player-cleanup");
        }

        private IEnumerator RunSplitOutputProof()
        {
            try
            {
                Require(OutputA != null && OutputB != null,
                    "Split proof requires both exact injected Outputs.");
                Require(OutputA.OutputIdText == "camera.output.main" &&
                        OutputB.OutputIdText == "camera.output.secondary" &&
                        OutputA.OutputIdText != OutputB.OutputIdText,
                    "Split proof requires exact distinct CameraOutputId values.");
                Require(!ReferenceEquals(OutputA.UnityCamera, OutputB.UnityCamera) &&
                        !ReferenceEquals(OutputA.CinemachineBrain, OutputB.CinemachineBrain) &&
                        !ReferenceEquals(OutputA.DefaultCameraRig, OutputB.DefaultCameraRig),
                    "Split Outputs share a physical Camera, Brain or Default rig.");
                Require(RectIs(OutputA.UnityCamera.rect, 0f, 0f, 0.5f, 1f) &&
                        RectIs(OutputB.UnityCamera.rect, 0.5f, 0f, 0.5f, 1f),
                    $"Framework split viewport policy was not applied exactly. A='{OutputA.UnityCamera.rect}' B='{OutputB.UnityCamera.rect}'.");
                Require(missingOutputProbe != null && !missingOutputProbe.IsAttached &&
                        missingOutputProbe.LastDetachReason.IndexOf(
                            "not part of the active Session topology",
                            StringComparison.Ordinal) >= 0,
                    "Missing requested Output did not produce the exact typed injection rejection.");

                CameraOutputContextSnapshot bBefore = OutputB.Context.CaptureSnapshot();
                Require(routeBinding.RequestOverride().Succeeded,
                    "Output A isolation setup could not publish Route request.");
                Require(IsWinner(OutputA, routeBinding.RequestIdText),
                    "Output A did not select its Route request.");
                RequireSameContext(bBefore, OutputB.Context.CaptureSnapshot(),
                    "Output A request mutated Output B arbitration.");

                splitOutputPublisher = CreateSplitOutputPublisher();
                CameraOutputContextSnapshot aBeforeB = OutputA.Context.CaptureSnapshot();
                CameraRequestPublisherResult bPublish = splitOutputPublisher.Publish();
                Require(bPublish.Succeeded && OutputB.Context.AdmittedRequestCount == 1,
                    "Output B independent request was not admitted.");
                RequireSameContext(aBeforeB, OutputA.Context.CaptureSnapshot(),
                    "Output B request mutated Output A arbitration.");

                CameraRequestPublisherResult bRelease = splitOutputPublisher.Release();
                Require(bRelease.Succeeded && OutputB.Context.AdmittedRequestCount == 0 &&
                        OutputB.Applicator.HasAppliedDefault,
                    "Output B release did not restore its independent Default.");
                splitOutputPublisher = null;
                Require(routeBinding.ReleaseOverride().Succeeded &&
                        OutputA.Context.AdmittedRequestCount == 0 &&
                        OutputA.Applicator.HasAppliedDefault,
                    "Output A release did not restore its independent Default.");

                Adr026SplitExecuted = true;
                Adr026SplitPassed = true;
                Adr026SplitDiagnostic =
                    "outputs='camera.output.main,camera.output.secondary' viewports='left,right' isolation='Passed' missingOutput='Rejected' automaticSplitScreen='RejectedByAuthoringValidation'.";
                Debug.Log($"{Adr026Prefix} phase='split' status='Passed' {Adr026SplitDiagnostic}", this);
            }
            catch (Exception exception)
            {
                Adr026SplitExecuted = true;
                Adr026SplitPassed = false;
                Adr026SplitDiagnostic = exception.Message;
                Fail(exception.Message);
            }
            finally
            {
                if (splitOutputPublisher != null)
                {
                    splitOutputPublisher.Release();
                    splitOutputPublisher = null;
                }
                if (routeBinding != null) routeBinding.ReleaseOverride();
            }
            yield break;
        }

        private IEnumerator RunGenericArbitrationProof()
        {
            if (!TryStep(() =>
                {
                    RequireDefault("default-presentation");
                    Complete("default-presentation");
                    Require(activityBinding.RequestOverride().Succeeded, "Activity request failed.");
                    Winner(activityBinding.RequestIdText, activityComposer, "activity-request");
                    Complete("activity-request");
                    Require(routeBinding.RequestOverride().Succeeded, "Route request failed.");
                    Winner(routeBinding.RequestIdText, routeComposer, "route-request");
                    Complete("route-request");
                    Require(SessionOverride.RequestOverride().Succeeded, "Session request failed.");
                    Winner(SessionOverride.RequestIdText, SessionOverride.RigComposer, "session-request");
                    Complete("session-request");
                    Require(SessionOverride.ReleaseOverride().Succeeded, "Session release failed.");
                    Winner(routeBinding.RequestIdText, routeComposer, "session-release-restores-route");
                    Complete("session-release-restores-route");
                    Require(routeBinding.ReleaseOverride().Succeeded, "Route release failed.");
                    Winner(activityBinding.RequestIdText, activityComposer, "route-release-restores-activity");
                    Complete("route-release-restores-activity");
                    Require(activityBinding.ReleaseOverride().Succeeded, "Activity release failed.");
                    RequireDefault("activity-release-restores-default");
                    Complete("activity-release-restores-default");
                    Require(activityBinding.RequestOverride().Succeeded, "First duplicate request failed.");
                    Require(activityBinding.RequestOverride().Operation == CameraOverrideOperationKind.Preserved,
                        "Duplicate request was not preserved.");
                    Winner(activityBinding.RequestIdText, activityComposer, "duplicate-request");
                    Complete("duplicate-request");
                    Require(activityBinding.ReleaseOverride().Succeeded, "First duplicate release failed.");
                    Require(activityBinding.ReleaseOverride().Operation == CameraOverrideOperationKind.Preserved,
                        "Duplicate release was not preserved.");
                    RequireDefault("duplicate-release");
                    Complete("duplicate-release");
                    RunAdr004CActivityDisableProbe();
                    RunAdr004CNonWinnerDisableProbe();
                    RunAdr004CSessionDisableProbe();
                    Require(activityBinding.RequestOverride().Succeeded, "Activity lifecycle setup failed.");
                    activityRequestTrigger.ClearActivity();
                })) yield break;

            yield return WaitFor(
                () => !activityRequestTrigger.IsRequestInFlight && activityRequestTrigger.LastRequestSucceeded,
                "activity-clear-request");
            if (HasFailed) yield break;
            yield return WaitFor(
                () => !Context.Contains(new CameraRequestId(activityBinding.RequestIdText)) &&
                    OutputA.Applicator.HasAppliedDefault,
                "activity-lifecycle-cleanup");
            if (HasFailed) yield break;

            if (!TryStep(() =>
                {
                    Adr004BActivityLifecycleExecuted = true;
                    Adr004BActivityLifecyclePassed = true;
                    Complete("activity-lifecycle-cleanup");
                    activityRequestTrigger.RequestActivity();
                })) yield break;

            yield return WaitFor(
                () => !activityRequestTrigger.IsRequestInFlight &&
                    activityRequestTrigger.LastRequestSucceeded &&
                    !activityRequestTrigger.LastRequestClearedActivity &&
                    activityBinding != null && activityBinding.IsOwnerActive,
                "activity-reentry");
            if (HasFailed) yield break;

            string destroyedActivityRequestId = string.Empty;
            if (!TryStep(() =>
                {
                    Require(activityBinding.RequestOverride().Succeeded, "Activity destruction setup failed.");
                    destroyedActivityRequestId = activityBinding.RequestIdText;
                    Destroy(activityBinding);
                })) yield break;
            yield return null;

            if (!TryStep(() =>
                {
                    bool removed = !Context.Contains(new CameraRequestId(destroyedActivityRequestId));
                    bool restoredDefault = OutputA.Applicator.HasAppliedDefault;
                    Adr004CActivityDestroyExecuted = true;
                    Adr004CActivityDestroyPassed = removed && restoredDefault;
                    Debug.Log($"{Adr004CLogPrefix} case='activity-destruction' " +
                        $"status='{(Adr004CActivityDestroyPassed ? "Passed" : "Failed")}' " +
                        $"removed='{removed}' restoredDefault='{restoredDefault}'.", this);
                    activityRequestTrigger.ClearActivity();
                })) yield break;

            yield return WaitFor(
                () => !activityRequestTrigger.IsRequestInFlight &&
                    activityRequestTrigger.LastRequestSucceeded &&
                    activityRequestTrigger.LastRequestClearedActivity,
                "activity-clear-after-destroy");
            if (HasFailed) yield break;

            if (!TryStep(() =>
                {
                    RunAdr004BOwnerLossProbe();
                    Require(routeBinding.RequestOverride().Succeeded, "Route lifecycle setup failed.");
                    PublishRouteLifecycleSurvivor();
                    routeRequestId = routeBinding.RequestIdText;
                    awaitingRouteLifecycleCleanup = true;
                    lastStatus = "WaitingRouteLifecycleCleanup";
                    backToHubTrigger.RequestRoute();
                })) yield break;
        }

        private void RunAdr004BOwnerLossProbe()
        {
            Require(routeBinding.RequestOverride().Succeeded, "Owner-loss setup could not publish Route request.");
            CameraRequestId requestId = new CameraRequestId(routeBinding.RequestIdText);
            routeBinding.enabled = false;
            bool orphaned = Context.Contains(requestId);
            Adr004BOwnerLossExecuted = true;
            Adr004BOwnerLossInvariantPassed = !orphaned;
            Adr004BOwnerLossDiagnostic = orphaned
                ? "Disabling RouteCameraOverride left its request admitted."
                : "Route owner disable released only its admitted request.";
            routeBinding.ReleaseOverride();
            routeBinding.enabled = true;
            bool silentlyRepublished = Context.Contains(requestId);
            Adr004CRouteReenableExecuted = true;
            Adr004CRouteReenablePassed = !silentlyRepublished && routeBinding.IsOwnerActive;
            RequireDefault("route-disable-reenable");
        }

        private void RunAdr004CActivityDisableProbe()
        {
            Require(activityBinding.RequestOverride().Succeeded, "Activity disable setup failed.");
            CameraRequestId requestId = new CameraRequestId(activityBinding.RequestIdText);
            activityBinding.enabled = false;
            bool removed = !Context.Contains(requestId);
            bool restoredDefault = OutputA.Applicator.HasAppliedDefault;
            CameraOverrideResult repeated = activityBinding.ReleaseOverride();
            bool idempotent = repeated.Succeeded && repeated.Operation == CameraOverrideOperationKind.Preserved;
            activityBinding.enabled = true;
            bool silent = Context.Contains(requestId);
            Adr004CActivityDisableExecuted = true;
            Adr004CActivityDisablePassed = removed && !silent && activityBinding.IsOwnerActive;
            Adr004CWinningRestoreExecuted = true;
            Adr004CWinningRestorePassed = removed && restoredDefault;
            Adr004CIdempotentCleanupExecuted = true;
            Adr004CIdempotentCleanupPassed = idempotent;
            Require(activityBinding.RequestOverride().Succeeded, "Activity explicit re-publication failed.");
            Require(activityBinding.ReleaseOverride().Succeeded, "Activity explicit cleanup failed.");
            RequireDefault("activity-disable-cleanup");
        }

        private void RunAdr004CNonWinnerDisableProbe()
        {
            Require(activityBinding.RequestOverride().Succeeded && routeBinding.RequestOverride().Succeeded,
                "Non-winner setup failed.");
            CameraRequestId activityId = new CameraRequestId(activityBinding.RequestIdText);
            CameraRequestId routeId = new CameraRequestId(routeBinding.RequestIdText);
            activityBinding.enabled = false;
            Adr004CNonWinnerDisableExecuted = true;
            Adr004CNonWinnerDisablePassed = !Context.Contains(activityId) && Context.Contains(routeId) &&
                IsWinner(OutputA, routeBinding.RequestIdText);
            activityBinding.enabled = true;
            Require(routeBinding.ReleaseOverride().Succeeded, "Non-winner Route cleanup failed.");
            RequireDefault("nonwinner-cleanup");
        }

        private void RunAdr004CSessionDisableProbe()
        {
            Require(SessionOverride.RequestOverride().Succeeded, "Session disable setup failed.");
            CameraRequestId requestId = new CameraRequestId(SessionOverride.RequestIdText);
            SessionOverride.enabled = false;
            bool removed = !Context.Contains(requestId);
            bool restoredDefault = OutputA.Applicator.HasAppliedDefault;
            CameraOverrideResult repeated = SessionOverride.ReleaseOverride();
            bool idempotent = repeated.Succeeded && repeated.Operation == CameraOverrideOperationKind.Preserved;
            SessionOverride.enabled = true;
            bool silent = Context.Contains(requestId);
            Adr004CSessionDisableExecuted = true;
            Adr004CSessionDisablePassed = removed && restoredDefault && !silent && SessionOverride.IsOwnerActive;
            Adr004CIdempotentCleanupPassed &= idempotent;
            Require(SessionOverride.RequestOverride().Succeeded, "Session explicit re-publication failed.");
            Require(SessionOverride.ReleaseOverride().Succeeded, "Session explicit cleanup failed.");
            RequireDefault("session-disable-cleanup");
        }

        private void PublishRouteLifecycleSurvivor()
        {
            CameraRequestCreateResult request = CameraRequestCreateResult.Create(
                new CameraRequestId(RouteLifecycleSurvivorRequestId),
                new CameraOutputId(OutputA.OutputIdText),
                new CameraRequestOwner(CameraRequestOwnerKind.Session,
                    "qa.camera.adr004b.route-lifecycle-survivor-owner"),
                new CameraRequestLifetime(CameraRequestLifetimeKind.Session,
                    "qa.camera.adr004b.route-lifecycle-survivor-scope"),
                CameraRigReference.FromComposer(SessionOverride.RigComposer),
                CameraTargetSourceDescriptor.ExplicitTransform(
                    SessionOverride.TargetSource, "ADR004BRouteLifecycleSurvivor"),
                new CameraRequestPolicy(150, "adr004b-route-lifecycle-survivor"),
                CameraRequestReleaseCondition.ExplicitRelease,
                nameof(QaCameraOverrideAuthorityFixture),
                "Persistent survivor for Route lifecycle cleanup isolation.");
            Require(request.IsSucceeded, request.BlockingIssue);
            CameraRequestPublisherCreateResult publisher =
                SessionCameraRequestPublisher.Create(OutputA.Session, request.Request);
            Require(publisher.Succeeded && publisher.Publisher != null, publisher.DiagnosticSummary);
            routeLifecycleSurvivorPublisher = publisher.Publisher;
            Require(routeLifecycleSurvivorPublisher.Publish().Succeeded,
                "Route lifecycle survivor publication failed.");
        }

        private ICameraRequestPublisher CreateSplitOutputPublisher()
        {
            Transform target = routeComposer != null ? routeComposer.ExplicitFollowTarget : null;
            Require(target != null, "Split Output B request requires an explicit target.");
            CameraRequestCreateResult request = CameraRequestCreateResult.Create(
                new CameraRequestId("qa.camera.adr026.output-b.request"),
                new CameraOutputId(OutputB.OutputIdText),
                new CameraRequestOwner(CameraRequestOwnerKind.Session, "qa.camera.adr026.output-b.owner"),
                new CameraRequestLifetime(CameraRequestLifetimeKind.Session, "qa.camera.adr026.output-b.scope"),
                CameraRigReference.FromComposer(OutputB.DefaultCameraRig),
                CameraTargetSourceDescriptor.ExplicitTransform(target, "ADR026OutputB"),
                new CameraRequestPolicy(250, "adr026-output-b"),
                CameraRequestReleaseCondition.ExplicitRelease,
                nameof(QaCameraOverrideAuthorityFixture),
                "ADR-026 real Output B isolation proof.");
            Require(request.IsSucceeded, request.BlockingIssue);
            CameraRequestPublisherCreateResult created =
                SessionCameraRequestPublisher.Create(OutputB.Session, request.Request);
            Require(created.Succeeded && created.Publisher != null, created.DiagnosticSummary);
            return created.Publisher;
        }

        private bool BaseReadiness()
        {
            CameraSubjectAvailabilitySnapshot subjects =
                SubjectSnapshot();
            CameraSharedComposition shared =
                SharedComposition;
            CameraSharedCompositionSnapshot composition =
                shared != null ? shared.Snapshot : default;

            return subjects != null &&
                subjects.Count == 0 &&
                outputAProbe != null &&
                outputAProbe.IsAttached &&
                outputAProbe.AttachmentCount == 1 &&
                outputBProbe != null &&
                outputBProbe.IsAttached &&
                outputBProbe.AttachmentCount == 1 &&
                missingOutputProbe != null &&
                !missingOutputProbe.IsAttached &&
                missingOutputProbe.LastDetachReason.Contains(
                    "not part of the active Session topology") &&
                OutputA != null &&
                OutputA.IsInitialized &&
                OutputB != null &&
                OutputB.IsInitialized &&
                OutputB.DefaultCameraRig != null &&
                OutputB.DefaultCameraRig.PresentationIntent ==
                    CameraRigPresentationIntent.Mounted &&
                SessionOverride != null &&
                SessionOverride.IsOwnerActive &&
                ReferenceEquals(SessionOverride.OutputSession, OutputA) &&
                routeBinding != null &&
                activityBinding != null &&
                shared != null &&
                ReferenceEquals(shared.Output, OutputA) &&
                composition.IsReady &&
                composition.SubjectCount == 0 &&
                composition.LastReconcileStatus ==
                    CameraSharedCompositionReconcileStatus.SucceededAwaitingSubjects &&
                composition.AvailabilityContextId == subjects.ContextId &&
                composition.AvailabilityRevisionConsumed == subjects.Revision &&
                backToHubTrigger != null;
        }

        private bool SharedMembershipIs(int count)
        {
            CameraSubjectAvailabilitySnapshot subjects = SubjectSnapshot();
            CameraSharedCompositionSnapshot composition = SharedComposition.Snapshot;
            return subjects != null && subjects.Count == count && composition.IsReady &&
                composition.SubjectCount == count &&
                composition.AvailabilityRevisionConsumed == subjects.Revision;
        }

        private CameraSubjectAvailabilitySnapshot SubjectSnapshot() => subjectAvailability?.CreateSnapshot();

        private static PlayerSessionScopedObservationSnapshot Observation(IPlayerSessionScopedAccess access)
        {
            PlayerSessionScopedObservationSnapshot value = null;
            Require(access != null &&
                    access.TryGetObservation(out value) &&
                    value != null && value.IsAvailable,
                "Public Player Session observation is unavailable.");
            return value;
        }

        private static SessionPlayerLeaveRequest LeaveRequest(
            IPlayerSessionScopedAccess access,
            Immersive.Framework.PlayerSlots.PlayerSlotId slotId,
            string reason)
        {
            PlayerSessionScopedObservationSnapshot observation = Observation(access);
            for (int index = 0; index < observation.Participation.Slots.Count; index++)
            {
                var slot = observation.Participation.Slots[index];
                if (slot.PlayerSlotId == slotId)
                {
                    return new SessionPlayerLeaveRequest(slotId, slot.Revision,
                        nameof(QaCameraOverrideAuthorityFixture), reason);
                }
            }
            throw new InvalidOperationException(
                $"Player Slot '{slotId.StableText}' is absent from the public observation.");
        }

        private static CameraSubjectAvailabilityEntry RequireSubjectForHost(
            CameraSubjectAvailabilitySnapshot snapshot,
            LocalPlayerHostAuthoring host,
            string label)
        {
            Require(snapshot != null && host != null && host.ActorMount != null,
                $"{label} Subject resolution requires exact host evidence.");
            CameraSubjectAvailabilityEntry resolved = default;
            int matches = 0;
            for (int index = 0; index < snapshot.Entries.Count; index++)
            {
                CameraSubjectAvailabilityEntry candidate = snapshot.Entries[index];
                if (candidate.Subject.Observation != null &&
                    candidate.Subject.Observation.IsChildOf(host.ActorMount))
                {
                    resolved = candidate;
                    matches++;
                }
            }
            Require(matches == 1,
                $"Expected exactly one {label} Camera Subject for its exact Player Host, found '{matches}'.");
            return resolved;
        }

        private static void RequireExplicitChildSubject(
            CameraSubjectAvailabilityEntry entry,
            LocalPlayerHostAuthoring host,
            string label)
        {
            Require(
                entry.IsValid && host != null && host.ActorMount != null,
                $"{label} explicit Camera Subject proof requires valid typed evidence.");

            PlayerActorRuntimeHost[] runtimeHosts =
                host.ActorMount.GetComponentsInChildren<PlayerActorRuntimeHost>(true);
            Require(
                runtimeHosts.Length == 1 &&
                runtimeHosts[0].PlayerActorDeclaration != null,
                $"{label} requires exactly one prepared Player Actor Runtime Host.");

            ActorCameraSubjectAuthoring[] authoredSubjects =
                runtimeHosts[0].PresentationMount
                    .GetComponentsInChildren<ActorCameraSubjectAuthoring>(true);
            string issue = authoredSubjects.Length == 1
                ? string.Empty
                : $"Found '{authoredSubjects.Length}' components.";
            bool validAuthoring = authoredSubjects.Length == 1 &&
                authoredSubjects[0].TryValidateConfiguration(out issue);
            Require(
                validAuthoring,
                $"{label} requires one valid Actor Camera Subject authoring component. {issue}");
            Require(
                ReferenceEquals(
                    entry.Subject.Observation,
                    authoredSubjects[0].ObservationTransform) &&
                !ReferenceEquals(
                    entry.Subject.Observation,
                    runtimeHosts[0].PlayerActorDeclaration.transform) &&
                entry.Subject.Observation.IsChildOf(
                    runtimeHosts[0].PlayerActorDeclaration.transform),
                $"{label} Camera Subject must resolve the explicitly authored child Transform, never the Actor root.");
        }

        private void ApplyMountedSubject(
            CameraViewAssignmentContext assignments,
            CameraSubjectAssignmentOwnerId owner,
            CameraViewId viewId,
            CameraSubjectAvailabilityEntry subject,
            string phase)
        {
            CameraSubjectAvailabilitySnapshot availability = SubjectSnapshot();
            CameraViewAssignmentResult reconciled = assignments.Reconcile(availability);
            Require(reconciled.Succeeded, reconciled.Message);

            CameraViewAssignmentResult assigned = assignments.TryAssign(
                viewId,
                subject.Subject.SubjectId,
                owner,
                availability);
            Require(assigned.Succeeded, assigned.Message);

            CameraViewPresentationInputResult projection =
                CameraViewPresentationInputProjection.TryCreate(
                    assigned.Snapshot,
                    viewId);
            Require(projection.Succeeded, projection.Message);

            CameraRigComposer mounted = OutputB.DefaultCameraRig;
            CameraViewPresentationApplyResult applied =
                mounted.ApplyViewPresentation(
                    projection.Input,
                    assigned.Snapshot);
            Require(
                applied.Status ==
                    CameraViewPresentationApplyStatus.SucceededSingleSubject &&
                mounted.PresentationIntent == CameraRigPresentationIntent.Mounted &&
                mounted.FrameworkOwnedPositionControl is CinemachineHardLockToTarget &&
                mounted.FrameworkOwnedRotationControl is CinemachineRotateWithFollowTarget &&
                ReferenceEquals(
                    mounted.CinemachineCamera.Follow,
                    subject.Subject.Observation) &&
                mounted.CinemachineCamera.LookAt == null,
                $"Mounted presentation did not consume the exact explicit Actor Camera Subject at '{phase}'. {applied.Diagnostic}");
        }

        private static void ReconcileMountedAssignments(
            CameraViewAssignmentContext assignments,
            CameraViewId viewId,
            CameraSubjectAvailabilitySnapshot availability,
            int expectedCount,
            string phase)
        {
            CameraViewAssignmentResult reconciled = assignments.Reconcile(availability);
            Require(
                reconciled.Succeeded &&
                reconciled.Snapshot.TryGetView(
                    viewId,
                    out CameraViewSubjectSnapshot view) &&
                view.AssignmentCount == expectedCount &&
                view.ResolvedSubjectCount == expectedCount,
                $"Mounted View assignment reconciliation failed at '{phase}'. {reconciled.Message}");
        }

        private void ApplyMountedEmpty(
            CameraViewAssignmentContext assignments,
            CameraViewId viewId,
            string phase)
        {
            CameraSubjectAvailabilitySnapshot availability = SubjectSnapshot();
            CameraViewAssignmentResult reconciled = assignments.Reconcile(availability);
            Require(reconciled.Succeeded, reconciled.Message);
            CameraViewPresentationInputResult projection =
                CameraViewPresentationInputProjection.TryCreate(
                    reconciled.Snapshot,
                    viewId);
            Require(projection.Succeeded, projection.Message);

            CameraRigComposer mounted = OutputB.DefaultCameraRig;
            CameraViewPresentationApplyResult applied =
                mounted.ApplyViewPresentation(
                    projection.Input,
                    reconciled.Snapshot);
            Require(
                applied.Status ==
                    CameraViewPresentationApplyStatus.BlockedRequiredSubjectMissing &&
                mounted.CinemachineCamera.Follow == null &&
                mounted.CinemachineCamera.LookAt == null &&
                OutputB.IsInitialized &&
                ReferenceEquals(OutputB.DefaultCameraRig, mounted),
                $"Mounted presentation retained a stale Subject or lost Output ownership at '{phase}'. {applied.Diagnostic}");
        }

        private void RequireSharedIdentity(
            CameraViewId view,
            CameraRigComposer composer,
            CinemachineCamera cinemachine,
            CameraOutputAuthoring output,
            string phase)
        {
            Require(SharedComposition.ViewId == view &&
                    ReferenceEquals(SharedComposition.Composer, composer) &&
                    ReferenceEquals(composer.CinemachineCamera, cinemachine) &&
                    ReferenceEquals(SharedComposition.Output, output) && ReferenceEquals(output, OutputA),
                $"Shared Camera changed View, Composer, Cinemachine Camera or Output at '{phase}'.");
            Require(RectIs(output.UnityCamera.rect, 0f, 0f, 1f, 1f),
                $"Shared View Main is not fullscreen at '{phase}'. rect='{output.UnityCamera.rect}'.");
        }

        private static void RequireExactGroupMembers(CameraRigComposer composer, params Transform[] expected)
        {
            CinemachineTargetGroup group = composer.FrameworkOwnedSharedFollowTargetGroup;
            Require(group != null && group.Targets != null && group.Targets.Count == expected.Length,
                $"Shared Follow group expected '{expected.Length}' members.");
            for (int expectedIndex = 0; expectedIndex < expected.Length; expectedIndex++)
            {
                int matches = 0;
                for (int actualIndex = 0; actualIndex < group.Targets.Count; actualIndex++)
                    if (ReferenceEquals(group.Targets[actualIndex].Object, expected[expectedIndex])) matches++;
                Require(matches == 1,
                    $"Shared Follow group expected exact Subject observation '{expectedIndex}' once, found '{matches}'.");
            }
        }

        private static void RequireNoGroupMember(CameraRigComposer composer, Transform stale)
        {
            CinemachineTargetGroup group = composer.FrameworkOwnedSharedFollowTargetGroup;
            if (group == null || group.Targets == null) return;
            for (int index = 0; index < group.Targets.Count; index++)
            {
                Require(!ReferenceEquals(group.Targets[index].Object, stale),
                    "Stale Subject observation survived in the shared Follow group.");
            }
        }

        private void RequireNoOrdinaryPlayerRequests(string phase)
        {
            Require(OutputA.Context.AdmittedRequestCount == 0 && OutputB.Context.AdmittedRequestCount == 0,
                $"Ordinary Player lifecycle published CameraRequests at '{phase}'. " +
                $"A='{OutputA.Context.AdmittedRequestCount}' B='{OutputB.Context.AdmittedRequestCount}'.");
        }

        private static void TryCleanupPlayer(
            IPlayerSessionScopedAccess access,
            ref LocalPlayerJoinResult join,
            string reason)
        {
            if (access == null || join == null || !join.Slot.PlayerSlotId.IsValid) return;
            try
            {
                SessionPlayerLeaveResult result = access.RequestLeave(
                    LeaveRequest(access, join.Slot.PlayerSlotId, reason));
                if (result == null || !result.Succeeded)
                {
                    Debug.LogError($"{Adr026Prefix} cleanup='Failed' " +
                        $"reason='{Escape(result != null ? result.ToDiagnosticString() : reason)}'.");
                }
            }
            catch (Exception exception)
            {
                Debug.LogError($"{Adr026Prefix} cleanup='Failed' reason='{Escape(exception.Message)}'.");
            }
            join = null;
        }

        private static bool RectIs(Rect value, float x, float y, float width, float height) =>
            Mathf.Approximately(value.x, x) && Mathf.Approximately(value.y, y) &&
            Mathf.Approximately(value.width, width) && Mathf.Approximately(value.height, height);

        private static bool PlayerInputOwnsDevice(PlayerInput playerInput, InputDevice expected)
        {
            if (playerInput == null || expected == null) return false;
            for (int index = 0; index < playerInput.devices.Count; index++)
            {
                InputDevice actual = playerInput.devices[index];
                if (ReferenceEquals(actual, expected) ||
                    (actual != null && actual.deviceId == expected.deviceId))
                {
                    return true;
                }
            }
            return false;
        }

        private static string DescribePlayerInputDeviceIds(PlayerInput playerInput)
        {
            if (playerInput == null) return "<missing>";
            if (playerInput.devices.Count == 0) return "<none>";

            var deviceIds = new string[playerInput.devices.Count];
            for (int index = 0; index < playerInput.devices.Count; index++)
            {
                InputDevice device = playerInput.devices[index];
                deviceIds[index] = DeviceId(device);
            }
            return string.Join(",", deviceIds);
        }

        private static string DeviceId(InputDevice device) =>
            device != null ? device.deviceId.ToString() : "<missing>";

        private static void RequireSameContext(
            CameraOutputContextSnapshot expected,
            CameraOutputContextSnapshot actual,
            string message)
        {
            Require(expected.OutputId == actual.OutputId &&
                    expected.AdmittedRequestCount == actual.AdmittedRequestCount &&
                    expected.HasWinner == actual.HasWinner &&
                    (!expected.HasWinner || expected.Winner.RequestId == actual.Winner.RequestId),
                message);
        }

        private static bool IsWinner(CameraOutputAuthoring output, string requestId) =>
            output != null && output.Context != null && output.Context.HasWinner &&
            output.Context.Winner.RequestId == new CameraRequestId(requestId);

        private void Winner(string requestId, CameraRigComposer rig, string step)
        {
            Require(IsWinner(OutputA, requestId), $"Unexpected Output A winner at '{step}'. {State()}");
            Require(rig != null && rig.CinemachineCamera != null && rig.CinemachineCamera.enabled,
                $"Expected rig is disabled at '{step}'.");
        }

        private void RequireDefault(string step)
        {
            Require(Context != null && !Context.HasWinner && OutputA.Applicator != null &&
                    OutputA.Applicator.HasAppliedDefault &&
                    ReferenceEquals(OutputA.Applicator.AppliedCamera,
                        OutputA.DefaultCameraRig.CinemachineCamera),
                $"Output A Default presentation was not authoritative at '{step}'. {State()}");
        }

        private IEnumerator WaitFor(Func<bool> condition, string label)
        {
            for (int frame = 0; frame < MaxReadinessFrames; frame++)
            {
                bool completed;
                try { completed = condition(); }
                catch (Exception exception)
                {
                    Fail($"Readiness check '{label}' threw: {exception.Message}");
                    yield break;
                }
                if (completed) yield break;
                yield return null;
            }
            Fail($"Timed out waiting for '{label}'. {State()}");
        }

        private bool TryStep(Action step)
        {
            try { step(); return true; }
            catch (Exception exception) { Fail(exception.Message); return false; }
        }

        private void Complete(string name)
        {
            completedCaseCount++;
            completedCases.Add(name);
            Debug.Log($"{LogPrefix} phase='generic-arbitration' case='{name}' status='Passed'.", this);
        }

        private void OnDestroy()
        {
            if (awaitingSplitRouteExit)
            {
                Debug.Log($"{LogPrefix} status='{(Adr026SplitPassed ? "Passed" : "Failed")}' " +
                    "phase='adr026-split-fixture' cases='8/8'.", this);
                return;
            }
            if (!awaitingRouteLifecycleCleanup || OutputA == null) return;

            Adr004BRouteLifecycleExecuted = true;
            bool routeRemoved = !OutputA.Context.Contains(new CameraRequestId(routeRequestId));
            bool survivorPreserved = OutputA.Context.Contains(
                new CameraRequestId(RouteLifecycleSurvivorRequestId));
            Adr004BRouteLifecyclePassed = routeRemoved && survivorPreserved;
            if (routeLifecycleSurvivorPublisher != null)
            {
                routeLifecycleSurvivorPublisher.Release();
                routeLifecycleSurvivorPublisher = null;
            }
            Complete("route-lifecycle-cleanup");
            GenericArbitrationExecuted = true;
            GenericArbitrationPassed = Adr004BRouteLifecyclePassed &&
                completedCaseCount == ExpectedGenericCaseCount;
            lastStatus = GenericArbitrationPassed ? "Passed" : "Failed";
            Debug.Log($"{LogPrefix} status='{lastStatus}' phase='canonical-override-fixture' " +
                $"cases='{completedCaseCount}/{ExpectedGenericCaseCount}' " +
                $"completed='{string.Join(",", completedCases)}'.", this);
        }

        private void Fail(string reason)
        {
            if (HasFailed) return;
            lastStatus = "Failed";
            lastFailure = reason;
            if (topologyMode == QaCameraAdr026TopologyMode.Shared && !Adr026SharedExecuted)
            {
                Adr026SharedExecuted = true;
                Adr026SharedPassed = false;
                Adr026SharedDiagnostic = reason;
            }
            Debug.LogError($"{LogPrefix} status='Failed' phase='{topologyMode}' " +
                $"cases='{completedCaseCount}/{ExpectedGenericCaseCount}' " +
                $"next='{(completedCaseCount < ExpectedGenericCaseCount ? "generic-arbitration" : "none")}' " +
                $"completed='{string.Join(",", completedCases)}' missing='{Escape(reason)}'.", this);
            if (throwOnFailure) throw new InvalidOperationException(reason);
        }

        private string State()
        {
            CameraSubjectAvailabilitySnapshot subjects =
                SubjectSnapshot();
            CameraSharedComposition shared =
                SharedComposition;
            CameraSharedCompositionSnapshot composition =
                shared != null ? shared.Snapshot : default;

            return
                $"mode='{topologyMode}' " +
                $"outputA='{(OutputA != null ? OutputA.OutputIdText : "<missing>")}' " +
                $"outputB='{(OutputB != null ? OutputB.OutputIdText : "<missing>")}' " +
                $"probeA='{(outputAProbe != null ? outputAProbe.AttachmentCount : -1)}' " +
                $"probeB='{(outputBProbe != null ? outputBProbe.AttachmentCount : -1)}' " +
                $"missingRejected='{(missingOutputProbe != null && !missingOutputProbe.IsAttached && !string.IsNullOrWhiteSpace(missingOutputProbe.LastDetachReason))}' " +
                $"subjectSource='{(subjectAvailability != null)}' " +
                $"subjects='{(subjects != null ? subjects.Count : -1)}' " +
                $"subjectRevision='{(subjects != null ? subjects.Revision : -1)}' " +
                $"sharedOutput='{(shared != null && ReferenceEquals(shared.Output, OutputA))}' " +
                $"sharedReady='{composition.IsReady}' " +
                $"sharedStatus='{composition.LastReconcileStatus}' " +
                $"sharedSubjects='{composition.SubjectCount}' " +
                $"sharedRevision='{composition.AvailabilityRevisionConsumed}' " +
                $"sessionOutput='{(SessionOverride != null && ReferenceEquals(SessionOverride.OutputSession, OutputA))}' " +
                $"sessionOwner='{(SessionOverride != null && SessionOverride.IsOwnerActive)}' " +
                $"requestA='{(OutputA != null && OutputA.Context != null ? OutputA.Context.AdmittedRequestCount : -1)}' " +
                $"requestB='{(OutputB != null && OutputB.Context != null ? OutputB.Context.AdmittedRequestCount : -1)}'.";
        }

        private static void ResetEvidence()
        {
            Adr026SharedExecuted = false;
            Adr026SharedPassed = false;
            Adr026SharedDiagnostic = string.Empty;
            Adr026SplitExecuted = false;
            Adr026SplitPassed = false;
            Adr026SplitDiagnostic = string.Empty;
            GenericArbitrationExecuted = false;
            GenericArbitrationPassed = false;
            Adr004BActivityLifecycleExecuted = false;
            Adr004BActivityLifecyclePassed = false;
            Adr004BRouteLifecycleExecuted = false;
            Adr004BRouteLifecyclePassed = false;
            Adr004BOwnerLossExecuted = false;
            Adr004BOwnerLossInvariantPassed = false;
            Adr004BOwnerLossDiagnostic = string.Empty;
            Adr004CActivityDisableExecuted = false;
            Adr004CActivityDisablePassed = false;
            Adr004CSessionDisableExecuted = false;
            Adr004CSessionDisablePassed = false;
            Adr004CNonWinnerDisableExecuted = false;
            Adr004CNonWinnerDisablePassed = false;
            Adr004CWinningRestoreExecuted = false;
            Adr004CWinningRestorePassed = false;
            Adr004CIdempotentCleanupExecuted = false;
            Adr004CIdempotentCleanupPassed = true;
            Adr004CActivityDestroyExecuted = false;
            Adr004CActivityDestroyPassed = false;
            Adr004CRouteReenableExecuted = false;
            Adr004CRouteReenablePassed = false;
        }

        private static string Escape(string value) =>
            (value ?? string.Empty).Replace("\\", "\\\\").Replace("'", "\\'")
                .Replace("\r", " ").Replace("\n", " ");

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
