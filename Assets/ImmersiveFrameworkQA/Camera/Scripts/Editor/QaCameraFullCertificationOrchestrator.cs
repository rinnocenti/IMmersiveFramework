using System;
using Immersive.Framework.GameFlow;
using ImmersiveFrameworkQA.GameFlow.Internal.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ImmersiveFrameworkQA.Camera.Editor
{
    /// <summary>
    /// Coordinates the existing Camera rail across fresh Shared and Split Framework boots.
    /// Camera topology preparation is verified from disk and the shared QA baseline is
    /// restored after both successful and failed certification runs.
    /// </summary>
    internal static class QaCameraFullCertificationOrchestrator
    {
        private const string MenuPath = "Immersive Framework/QA/Camera/Run Full Camera QA";
        private const string Prefix = "[QA_CAMERA_FULL]";
        private const string HubScenePath =
            "Assets/ImmersiveFrameworkQA/Hub/Scenes/QA_Hub.unity";
        private const string CanonicalScenePath =
            "Assets/ImmersiveFrameworkQA/Camera/Scenes/QA_PlayerCameraArbitration.unity";
        private const string CameraRouteTriggerName =
            "RouteTrigger_Camera__Override_Authority";
        private const string PhaseKey = "ImmersiveFrameworkQA.QA_CAMERA_FULL.Phase";
        private const string FailureKey = "ImmersiveFrameworkQA.QA_CAMERA_FULL.Failure";
        private const double TimeoutSeconds = 180d;
        private const int EstablishedCaseCount = 11 + 18 + 10;

        private enum Phase
        {
            Idle = 0,
            RunningShared = 10,
            SharedPassed = 20,
            RunningSplit = 30,
            Certified = 40,
            Failed = 50
        }

        private enum WatchStage
        {
            WaitingForHub,
            WaitingForFrameworkReady,
            WaitingForCameraSceneEnter,
            WaitingForCameraSceneExit
        }

        private static WatchStage watchStage;
        private static double startedAt;
        private static bool watching;
        private static RouteRequestTrigger pendingRouteTrigger;
        private static string pendingReadinessStage = "framework-boot-unavailable";
        private static string pendingReadinessMessage =
            "Framework readiness has not been observed yet.";

        [InitializeOnLoadMethod]
        private static void RegisterHook()
        {
            EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        }

        [MenuItem(MenuPath, true)]
        private static bool ValidateRun() => !EditorApplication.isPlaying &&
            CurrentPhase is Phase.Idle or Phase.Certified or Phase.Failed;

        [MenuItem(MenuPath, priority = 230)]
        private static void Run()
        {
            SessionState.EraseString(FailureKey);
            try
            {
                // Shared topology preparation is also the canonical repository-wide
                // baseline repair. The guard reloads QA_UIGlobal from disk and refuses
                // to continue if Camera/Brain/DefaultRig references were not persisted.
                QaCameraPersistentBaselineGuard.PrepareAndVerify(
                    QaCameraAdr026TopologyMode.Shared);

                SetPhase(Phase.RunningShared);
                Debug.Log($"{Prefix} status='Running' phase='Shared' " +
                    $"expectedEstablishedCases='{EstablishedCaseCount}' adr026Phases='2'.");
                EditorApplication.isPlaying = true;
            }
            catch (Exception exception)
            {
                Fail("prepare-shared", exception.GetBaseException().Message);
            }
        }

        private static void HandlePlayModeStateChanged(PlayModeStateChange state)
        {
            Phase phase = CurrentPhase;
            if (state == PlayModeStateChange.EnteredPlayMode &&
                phase is Phase.RunningShared or Phase.RunningSplit)
            {
                BeginWatching();
                return;
            }

            if (state == PlayModeStateChange.EnteredEditMode && phase == Phase.SharedPassed)
            {
                EditorApplication.delayCall += PrepareSplitPhase;
                return;
            }

            if (state == PlayModeStateChange.EnteredEditMode &&
                phase is Phase.RunningShared or Phase.RunningSplit)
            {
                Fail(
                    "play-mode-interrupted",
                    $"Play Mode exited before Camera certification phase '{phase}' completed.");
            }
        }

        private static void BeginWatching()
        {
            StopWatching();
            watching = true;
            watchStage = WatchStage.WaitingForHub;
            startedAt = EditorApplication.timeSinceStartup;
            pendingRouteTrigger = null;
            pendingReadinessStage = "framework-boot-unavailable";
            pendingReadinessMessage =
                "QA Hub scene has not finished loading the canonical Camera Route trigger.";
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            try
            {
                TickCore();
            }
            catch (Exception exception)
            {
                Fail("runtime-orchestration", exception.GetBaseException().Message);
                if (EditorApplication.isPlaying)
                {
                    EditorApplication.isPlaying = false;
                }
            }
        }

        private static void TickCore()
        {
            if (!watching || !EditorApplication.isPlaying)
            {
                StopWatching();
                return;
            }

            if (EditorApplication.timeSinceStartup - startedAt > TimeoutSeconds)
            {
                bool waitingOnReadiness =
                    watchStage is WatchStage.WaitingForHub or WatchStage.WaitingForFrameworkReady;
                string stage = waitingOnReadiness
                    ? pendingReadinessStage
                    : "runtime-timeout";
                string reason = waitingOnReadiness
                    ? pendingReadinessMessage
                    : DescribeEvidence();
                Fail(stage, reason);
                EditorApplication.isPlaying = false;
                return;
            }

            if (watchStage == WatchStage.WaitingForHub)
            {
                pendingReadinessStage = "framework-boot-unavailable";
                pendingReadinessMessage =
                    "QA Hub scene has not finished loading the canonical Camera Route trigger.";
                if (!TryResolveHubTrigger(out RouteRequestTrigger trigger))
                {
                    return;
                }

                pendingRouteTrigger = trigger;
                watchStage = WatchStage.WaitingForFrameworkReady;
                return;
            }

            if (watchStage == WatchStage.WaitingForFrameworkReady)
            {
                if (pendingRouteTrigger == null)
                {
                    watchStage = WatchStage.WaitingForHub;
                    return;
                }

                // The Camera Route can only be requested after the Framework has started
                // Game Flow with the Hub Activity ready and after the trigger has received
                // its canonical route-runtime port binding.
                if (!QaH2FrameworkReadiness.TryGetReady(out string frameworkDiagnostic))
                {
                    pendingReadinessStage = "framework-boot-unavailable";
                    pendingReadinessMessage =
                        "Framework has not finished starting Game Flow with a ready Hub Activity. " +
                        frameworkDiagnostic;
                    return;
                }

                if (!pendingRouteTrigger.HasRouteRuntimeBinding)
                {
                    pendingReadinessStage = "route-runtime-unavailable";
                    pendingReadinessMessage =
                        "Camera Route trigger has not been bound to the Game Flow route runtime port. " +
                        pendingRouteTrigger.RouteRuntimeBindingDiagnostic;
                    return;
                }

                Require(!pendingRouteTrigger.IsRequestInFlight,
                    "Camera Route trigger already has a request in flight.");
                pendingRouteTrigger.RequestRoute();
                watchStage = WatchStage.WaitingForCameraSceneEnter;
                return;
            }

            Scene cameraScene = SceneManager.GetSceneByPath(CanonicalScenePath);
            if (watchStage == WatchStage.WaitingForCameraSceneEnter)
            {
                if (cameraScene.IsValid() && cameraScene.isLoaded)
                {
                    watchStage = WatchStage.WaitingForCameraSceneExit;
                }
                return;
            }

            if (cameraScene.IsValid() && cameraScene.isLoaded)
            {
                if (CurrentPhase == Phase.RunningShared &&
                    QaCameraOverrideAuthorityFixture.Adr026SharedExecuted &&
                    !QaCameraOverrideAuthorityFixture.Adr026SharedPassed)
                {
                    throw new InvalidOperationException(
                        QaCameraOverrideAuthorityFixture.Adr026SharedDiagnostic);
                }

                if (CurrentPhase == Phase.RunningSplit &&
                    QaCameraOverrideAuthorityFixture.Adr026SplitExecuted &&
                    !QaCameraOverrideAuthorityFixture.Adr026SplitPassed)
                {
                    throw new InvalidOperationException(
                        QaCameraOverrideAuthorityFixture.Adr026SplitDiagnostic);
                }

                return;
            }

            try
            {
                if (CurrentPhase == Phase.RunningShared)
                {
                    CompleteSharedPhase();
                }
                else if (CurrentPhase == Phase.RunningSplit)
                {
                    CompleteSplitPhase();
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Unexpected Camera certification phase '{CurrentPhase}'.");
                }
            }
            catch (Exception exception)
            {
                Fail("runtime-evidence", exception.GetBaseException().Message);
                EditorApplication.isPlaying = false;
            }
        }

        private static void CompleteSharedPhase()
        {
            Require(QaCameraOverrideAuthorityFixture.Adr026SharedExecuted &&
                    QaCameraOverrideAuthorityFixture.Adr026SharedPassed,
                "ADR-026 Shared Camera proof failed. " +
                QaCameraOverrideAuthorityFixture.Adr026SharedDiagnostic);
            Require(QaCameraOverrideAuthorityFixture.GenericArbitrationExecuted &&
                    QaCameraOverrideAuthorityFixture.GenericArbitrationPassed,
                "Generic Camera arbitration did not complete.");
            Require(AllLegacyEvidenceExecuted() && AllLegacyEvidencePassed(),
                "ADR-004 lifecycle evidence is incomplete. " + DescribeEvidence());
            Require(QaCameraAdr004BNegativeIntegrityRegression.RunCertification(),
                "ADR-004B Negative Integrity certification failed.");
            Require(QaCameraAdr004COwnerLifetimeIntegrityRegression.RunCertification(),
                "ADR-004C Owner Lifetime certification failed.");

            StopWatching();
            SetPhase(Phase.SharedPassed);
            Debug.Log($"{Prefix} status='SharedPassed' next='FreshSplitBoot'.");
            EditorApplication.isPlaying = false;
        }

        private static void PrepareSplitPhase()
        {
            if (EditorApplication.isPlaying || CurrentPhase != Phase.SharedPassed)
            {
                return;
            }

            try
            {
                QaCameraPersistentBaselineGuard.PrepareAndVerify(
                    QaCameraAdr026TopologyMode.Split);
                SetPhase(Phase.RunningSplit);
                Debug.Log($"{Prefix} status='Running' phase='Split'.");
                EditorApplication.isPlaying = true;
            }
            catch (Exception exception)
            {
                Fail("prepare-split", exception.GetBaseException().Message);
            }
        }

        private static void CompleteSplitPhase()
        {
            Require(QaCameraOverrideAuthorityFixture.Adr026SplitExecuted &&
                    QaCameraOverrideAuthorityFixture.Adr026SplitPassed,
                "ADR-026 Split/Multi-Output proof failed. " +
                QaCameraOverrideAuthorityFixture.Adr026SplitDiagnostic);

            StopWatching();
            SetPhase(Phase.Certified);
            QaCameraPersistentBaselineGuard.RequestRestore("camera-full-certified");

            Debug.Log($"{Prefix} status='Completed' verdict='CAMERA QA CERTIFIED' " +
                "subjectsOccurrenceSafety='PASS' sharedCamera='PASS' playerCameraDecoupling='PASS' " +
                "multiOutput='PASS' outputIsolation='PASS' viewOutputBinding='PASS' " +
                "viewportSplitTopology='PASS' genericArbitration='PASS' negativeValidation='PASS' " +
                $"mandatoryEstablishedCases='{EstablishedCaseCount}' " +
                $"executedEstablishedCases='{EstablishedCaseCount}' " +
                $"passedEstablishedCases='{EstablishedCaseCount}' adr026Phases='2/2' dimensions='9/9' " +
                "next='RestoreCanonicalBaseline' missing='<none>' cleanup='Requested'.");
            EditorApplication.isPlaying = false;
        }

        private static bool TryResolveHubTrigger(out RouteRequestTrigger resolved)
        {
            resolved = null;
            Scene hub = SceneManager.GetSceneByPath(HubScenePath);
            if (!hub.IsValid() || !hub.isLoaded)
            {
                return false;
            }

            int matches = 0;
            foreach (GameObject root in hub.GetRootGameObjects())
            {
                foreach (RouteRequestTrigger candidate in
                    root.GetComponentsInChildren<RouteRequestTrigger>(true))
                {
                    if (candidate == null ||
                        candidate.gameObject.name != CameraRouteTriggerName)
                    {
                        continue;
                    }

                    matches++;
                    resolved = candidate;
                }
            }

            Require(matches == 1 && resolved != null,
                $"Expected one authored Camera Route trigger, found '{matches}'.");
            return true;
        }

        private static bool AllLegacyEvidenceExecuted() =>
            QaCameraOverrideAuthorityFixture.Adr004BActivityLifecycleExecuted &&
            QaCameraOverrideAuthorityFixture.Adr004BRouteLifecycleExecuted &&
            QaCameraOverrideAuthorityFixture.Adr004BOwnerLossExecuted &&
            QaCameraOverrideAuthorityFixture.Adr004CActivityDisableExecuted &&
            QaCameraOverrideAuthorityFixture.Adr004CSessionDisableExecuted &&
            QaCameraOverrideAuthorityFixture.Adr004CNonWinnerDisableExecuted &&
            QaCameraOverrideAuthorityFixture.Adr004CWinningRestoreExecuted &&
            QaCameraOverrideAuthorityFixture.Adr004CIdempotentCleanupExecuted &&
            QaCameraOverrideAuthorityFixture.Adr004CActivityDestroyExecuted &&
            QaCameraOverrideAuthorityFixture.Adr004CRouteReenableExecuted;

        private static bool AllLegacyEvidencePassed() =>
            QaCameraOverrideAuthorityFixture.Adr004BActivityLifecyclePassed &&
            QaCameraOverrideAuthorityFixture.Adr004BRouteLifecyclePassed &&
            QaCameraOverrideAuthorityFixture.Adr004BOwnerLossInvariantPassed &&
            QaCameraOverrideAuthorityFixture.Adr004CActivityDisablePassed &&
            QaCameraOverrideAuthorityFixture.Adr004CSessionDisablePassed &&
            QaCameraOverrideAuthorityFixture.Adr004CNonWinnerDisablePassed &&
            QaCameraOverrideAuthorityFixture.Adr004CWinningRestorePassed &&
            QaCameraOverrideAuthorityFixture.Adr004CIdempotentCleanupPassed &&
            QaCameraOverrideAuthorityFixture.Adr004CActivityDestroyPassed &&
            QaCameraOverrideAuthorityFixture.Adr004CRouteReenablePassed;

        private static string DescribeEvidence() =>
            $"shared='{QaCameraOverrideAuthorityFixture.Adr026SharedExecuted}/" +
            $"{QaCameraOverrideAuthorityFixture.Adr026SharedPassed}' " +
            $"split='{QaCameraOverrideAuthorityFixture.Adr026SplitExecuted}/" +
            $"{QaCameraOverrideAuthorityFixture.Adr026SplitPassed}' " +
            $"generic='{QaCameraOverrideAuthorityFixture.GenericArbitrationExecuted}/" +
            $"{QaCameraOverrideAuthorityFixture.GenericArbitrationPassed}' " +
            $"activityExit='{QaCameraOverrideAuthorityFixture.Adr004BActivityLifecycleExecuted}/" +
            $"{QaCameraOverrideAuthorityFixture.Adr004BActivityLifecyclePassed}' " +
            $"routeExit='{QaCameraOverrideAuthorityFixture.Adr004BRouteLifecycleExecuted}/" +
            $"{QaCameraOverrideAuthorityFixture.Adr004BRouteLifecyclePassed}'.";

        private static Phase CurrentPhase =>
            (Phase)SessionState.GetInt(PhaseKey, (int)Phase.Idle);

        private static void SetPhase(Phase phase) =>
            SessionState.SetInt(PhaseKey, (int)phase);

        private static void Fail(string stage, string reason)
        {
            StopWatching();
            SetPhase(Phase.Failed);
            SessionState.SetString(FailureKey, reason ?? string.Empty);
            QaCameraPersistentBaselineGuard.RequestRestore(
                $"camera-full-failed:{stage}");

            Debug.LogError($"{Prefix} status='Failed' verdict='CAMERA QA NOT CERTIFIED' " +
                $"stage='{stage}' next='FixAndRerun' missing='{Escape(reason)}' " +
                "cleanup='RequestedCanonicalRestore'.");
        }

        private static void StopWatching()
        {
            EditorApplication.update -= Tick;
            watching = false;
            startedAt = 0d;
        }

        private static string Escape(string value) =>
            (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("'", "\\'")
                .Replace("\r", " ")
                .Replace("\n", " ");

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
