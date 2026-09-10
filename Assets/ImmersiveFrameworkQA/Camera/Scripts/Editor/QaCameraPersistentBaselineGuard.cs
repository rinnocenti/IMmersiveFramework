using System;
using System.Collections.Generic;
using Immersive.Framework.Camera;
using Immersive.Framework.CameraAuthoring;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ImmersiveFrameworkQA.Camera.Editor
{
    /// <summary>
    /// Keeps the shared QA persistent-content Camera topology deterministic.
    /// Camera certification may temporarily author Shared or Split topology, but
    /// the repository-wide QA baseline is always restored to the canonical Shared
    /// topology after the run. Every preparation is verified from a disk reload.
    /// </summary>
    internal static class QaCameraPersistentBaselineGuard
    {
        private const string Prefix = "[QA_CAMERA_BASELINE]";
        private const string GlobalScenePath =
            "Assets/ImmersiveFrameworkQA/UnityBuildSurface/Scenes/QA_UIGlobal.unity";
        private const string HubScenePath =
            "Assets/ImmersiveFrameworkQA/Hub/Scenes/QA_Hub.unity";
        private const string PendingRestoreKey =
            "ImmersiveFrameworkQA.QA_CAMERA_BASELINE.PendingRestore";
        private const string PendingRestoreReasonKey =
            "ImmersiveFrameworkQA.QA_CAMERA_BASELINE.PendingRestoreReason";

        [InitializeOnLoadMethod]
        private static void Register()
        {
            EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;

            if (!EditorApplication.isPlaying &&
                SessionState.GetBool(PendingRestoreKey, false))
            {
                EditorApplication.delayCall -= RestorePending;
                EditorApplication.delayCall += RestorePending;
            }
        }

        [MenuItem(
            "Immersive Framework/QA/Setup/Camera/Restore Canonical Camera Baseline",
            priority = 205)]
        private static void RestoreFromMenu()
        {
            try
            {
                RestoreCanonicalBaseline();
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"{Prefix} status='Failed' operation='ManualRestore' " +
                    $"exception='{exception.GetType().Name}' " +
                    $"message='{Escape(exception.GetBaseException().Message)}'.");
                throw;
            }
        }

        internal static void PrepareAndVerify(QaCameraAdr026TopologyMode mode)
        {
            if (EditorApplication.isPlaying)
            {
                throw new InvalidOperationException(
                    "Camera persistent topology can only be prepared and verified in Edit Mode.");
            }

            QaCameraOverrideAuthorityInstaller.Install(mode);
            VerifyPersistedTopology(mode);
        }

        internal static void RestoreCanonicalBaseline()
        {
            PrepareAndVerify(QaCameraAdr026TopologyMode.Shared);
            SessionState.SetBool(PendingRestoreKey, false);
            SessionState.EraseString(PendingRestoreReasonKey);

            Debug.Log(
                $"{Prefix} status='Restored' topology='Shared' " +
                "persisted='True' outputs='2' hub='Restored'.");
        }

        internal static void RequestRestore(string reason)
        {
            SessionState.SetBool(PendingRestoreKey, true);
            SessionState.SetString(PendingRestoreReasonKey, reason ?? string.Empty);

            if (!EditorApplication.isPlaying)
            {
                EditorApplication.delayCall -= RestorePending;
                EditorApplication.delayCall += RestorePending;
            }
        }

        private static void HandlePlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode ||
                !SessionState.GetBool(PendingRestoreKey, false))
            {
                return;
            }

            EditorApplication.delayCall -= RestorePending;
            EditorApplication.delayCall += RestorePending;
        }

        private static void RestorePending()
        {
            if (EditorApplication.isPlaying ||
                !SessionState.GetBool(PendingRestoreKey, false))
            {
                return;
            }

            string reason = SessionState.GetString(
                PendingRestoreReasonKey,
                "unspecified");

            // Clear before attempting restoration so a broken installer cannot create
            // an endless editor callback loop. A failed restore remains explicit in logs
            // and can be retried from the menu after the underlying defect is corrected.
            SessionState.SetBool(PendingRestoreKey, false);
            SessionState.EraseString(PendingRestoreReasonKey);

            try
            {
                RestoreCanonicalBaseline();
                Debug.Log(
                    $"{Prefix} status='RestoredAfterRun' reason='{Escape(reason)}'.");
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"{Prefix} status='RestoreFailed' reason='{Escape(reason)}' " +
                    $"exception='{exception.GetType().Name}' " +
                    $"message='{Escape(exception.GetBaseException().Message)}'.");
            }
        }

        private static void VerifyPersistedTopology(QaCameraAdr026TopologyMode mode)
        {
            // QaCameraOverrideAuthorityInstaller finishes by opening the Hub. Reopening
            // QA_UIGlobal here therefore proves the serialized scene on disk rather than
            // merely re-reading the in-memory objects that the installer just mutated.
            Scene scene = EditorSceneManager.OpenScene(
                GlobalScenePath,
                OpenSceneMode.Single);

            List<CameraOutputAuthoring> outputs =
                FindInScene<CameraOutputAuthoring>(scene);
            if (outputs.Count != 2)
            {
                throw new InvalidOperationException(
                    "Persisted QA_UIGlobal Camera topology requires exactly two outputs " +
                    $"for ADR-026 preparation. actual='{outputs.Count}' mode='{mode}'.");
            }

            var definitionA = QaCameraPersistentTopologyBuilder.RequireDefinition<CameraOutputDefinition>(
                QaCameraPersistentTopologyBuilder.OutputAPath);
            var definitionB = QaCameraPersistentTopologyBuilder.RequireDefinition<CameraOutputDefinition>(
                QaCameraPersistentTopologyBuilder.OutputBPath);
            CameraOutputAuthoring outputA = RequireOutput(outputs, definitionA);
            CameraOutputAuthoring outputB = RequireOutput(outputs, definitionB);
            ValidateOutput(outputA, "A", definitionA);
            ValidateOutput(outputB, "B", definitionB);

            if (ReferenceEquals(outputA.UnityCamera, outputB.UnityCamera) ||
                ReferenceEquals(outputA.CinemachineBrain, outputB.CinemachineBrain) ||
                ReferenceEquals(outputA.DefaultCameraRig, outputB.DefaultCameraRig))
            {
                throw new InvalidOperationException(
                    "Persisted ADR-026 outputs are not physically independent.");
            }

            List<CameraViewOutputPolicyAuthoring> policies =
                FindInScene<CameraViewOutputPolicyAuthoring>(scene);
            if (policies.Count != 1)
            {
                throw new InvalidOperationException(
                    "Persisted QA_UIGlobal requires exactly one advanced Camera View Output Policy. " +
                    $"policies='{policies.Count}' mode='{mode}'.");
            }

            CameraSharedComposition composition =
                outputA.GetComponent<CameraSharedComposition>();
            string viewAPath = mode == QaCameraAdr026TopologyMode.Shared
                ? QaCameraPersistentTopologyBuilder.MainViewPath
                : QaCameraPersistentTopologyBuilder.SplitAViewPath;
            string viewBPath = mode == QaCameraAdr026TopologyMode.Shared
                ? QaCameraPersistentTopologyBuilder.SecondaryViewPath
                : QaCameraPersistentTopologyBuilder.SplitBViewPath;
            QaCameraPersistentTopologyBuilder.ValidateAggregateAuthoredTopology(
                composition,
                policies[0],
                outputA,
                outputB,
                QaCameraPersistentTopologyBuilder.RequireDefinition<CameraViewDefinition>(viewAPath),
                QaCameraPersistentTopologyBuilder.RequireDefinition<CameraViewDefinition>(viewBPath),
                QaCameraPersistentTopologyBuilder.SimpleViewport(mode),
                QaCameraPersistentTopologyBuilder.AdvancedViewport(mode));

            // Capture persisted evidence before opening the Hub. OpenSceneMode.Single destroys
            // all objects from QA_UIGlobal, so retaining CameraOutputAuthoring/Camera references
            // past this point would produce Unity MissingReferenceException diagnostics.
            string outputADescription = Describe(outputA);
            string outputBDescription = Describe(outputB);

            Debug.Log(
                $"{Prefix} status='Verified' topology='{mode}' persisted='True' " +
                $"outputA='{outputADescription}' outputB='{outputBDescription}'.");

            // Leave the canonical Hub open after structural verification so the next
            // Framework Play Mode boot starts from the same authored rail as the rest of QA.
            EditorSceneManager.OpenScene(HubScenePath, OpenSceneMode.Single);
        }

        private static CameraOutputAuthoring RequireOutput(
            List<CameraOutputAuthoring> outputs,
            CameraOutputDefinition definition)
        {
            CameraOutputAuthoring match = null;
            for (int index = 0; index < outputs.Count; index++)
            {
                CameraOutputAuthoring candidate = outputs[index];
                if (candidate == null || !ReferenceEquals(candidate.OutputDefinition, definition))
                {
                    continue;
                }

                if (match != null)
                {
                    throw new InvalidOperationException(
                        $"Persisted QA_UIGlobal contains duplicate Camera Output definition '{definition.name}'.");
                }

                match = candidate;
            }

            return match ?? throw new InvalidOperationException(
                $"Persisted QA_UIGlobal is missing Camera Output definition '{definition.name}'.");
        }

        private static void ValidateOutput(
            CameraOutputAuthoring output,
            string label,
            CameraOutputDefinition expectedDefinition)
        {
            if (!ReferenceEquals(output.OutputDefinition, expectedDefinition) ||
                output.UnityCamera == null ||
                output.CinemachineBrain == null ||
                output.DefaultCameraRig == null)
            {
                throw new InvalidOperationException(
                    $"Persisted Camera Output {label} ('{expectedDefinition.name}') is incomplete. " +
                    $"camera='{(output.UnityCamera != null)}' " +
                    $"brain='{(output.CinemachineBrain != null)}' " +
                    $"defaultRig='{(output.DefaultCameraRig != null)}'.");
            }

            if (!ReferenceEquals(
                    output.UnityCamera.gameObject,
                    output.CinemachineBrain.gameObject))
            {
                throw new InvalidOperationException(
                    $"Persisted Camera Output {label} Camera and CinemachineBrain are not " +
                    "owned by the same GameObject.");
            }

            if (output.DefaultCameraRig.BehaviorDefinition == null)
            {
                throw new InvalidOperationException(
                    $"Persisted Camera Output {label} Default Camera Rig requires a Camera Rig Behavior Definition.");
            }

            if (!output.DefaultCameraRig.BehaviorDefinition.TryValidate(out string behaviorIssue))
            {
                throw new InvalidOperationException(
                    $"Persisted Camera Output {label} Default Camera Rig Behavior Definition is invalid. {behaviorIssue}");
            }
        }

        private static string Describe(CameraOutputAuthoring output) =>
            $"id={output.OutputIdText};camera={output.UnityCamera.name};" +
            $"brain={output.CinemachineBrain.name};rig={output.DefaultCameraRig.name}";

        private static List<T> FindInScene<T>(Scene scene) where T : Component
        {
            var results = new List<T>();
            foreach (T item in Resources.FindObjectsOfTypeAll<T>())
            {
                if (item != null && item.gameObject.scene == scene)
                {
                    results.Add(item);
                }
            }

            return results;
        }

        private static string Escape(string value) =>
            (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("'", "\\'")
                .Replace("\r", " ")
                .Replace("\n", " ");
    }
}
