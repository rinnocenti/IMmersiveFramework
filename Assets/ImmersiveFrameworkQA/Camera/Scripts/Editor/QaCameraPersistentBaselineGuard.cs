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
        private const string OutputAId = "camera.output.main";
        private const string OutputBId = "camera.output.secondary";

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

            CameraOutputAuthoring outputA = RequireOutput(outputs, OutputAId);
            CameraOutputAuthoring outputB = RequireOutput(outputs, OutputBId);
            ValidateOutput(outputA, "A", OutputAId);
            ValidateOutput(outputB, "B", OutputBId);

            if (ReferenceEquals(outputA.UnityCamera, outputB.UnityCamera) ||
                ReferenceEquals(outputA.CinemachineBrain, outputB.CinemachineBrain) ||
                ReferenceEquals(outputA.DefaultCameraRig, outputB.DefaultCameraRig))
            {
                throw new InvalidOperationException(
                    "Persisted ADR-026 outputs are not physically independent.");
            }

            List<CameraViewOutputPolicyAuthoring> policies =
                FindInScene<CameraViewOutputPolicyAuthoring>(scene);
            string topologyIssue = string.Empty;
            CameraViewOutputTopology topology = null;
            bool topologyValid = policies.Count == 1 &&
                policies[0].TryBuildTopology(out topology, out topologyIssue) &&
                topology != null &&
                topology.BindingCount == 2;
            if (!topologyValid)
            {
                throw new InvalidOperationException(
                    "Persisted QA_UIGlobal Camera View-to-Output topology is invalid. " +
                    $"policies='{policies.Count}' mode='{mode}' issue='{topologyIssue ?? string.Empty}'.");
            }

            if (mode == QaCameraAdr026TopologyMode.Shared)
            {
                CameraSharedComposition composition =
                    outputA.GetComponent<CameraSharedComposition>();
                if (composition == null)
                {
                    throw new InvalidOperationException(
                        "Persisted canonical Shared Camera composition is missing from Output A.");
                }

                var serialized = new SerializedObject(composition);
                serialized.Update();
                SerializedProperty viewId = serialized.FindProperty("viewId");
                SerializedProperty outputId = serialized.FindProperty("outputId");
                SerializedProperty composer = serialized.FindProperty("composer");
                if (viewId == null || outputId == null || composer == null ||
                    viewId.stringValue != "camera.view.main" ||
                    outputId.stringValue != OutputAId ||
                    !ReferenceEquals(composer.objectReferenceValue, outputA.DefaultCameraRig))
                {
                    throw new InvalidOperationException(
                        "Persisted canonical Shared Camera composition is not bound exactly " +
                        "to View 'camera.view.main', Output A and Output A Default rig.");
                }
            }

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
            string outputId)
        {
            CameraOutputAuthoring match = null;
            for (int index = 0; index < outputs.Count; index++)
            {
                CameraOutputAuthoring candidate = outputs[index];
                if (candidate == null || candidate.OutputIdText != outputId)
                {
                    continue;
                }

                if (match != null)
                {
                    throw new InvalidOperationException(
                        $"Persisted QA_UIGlobal contains duplicate Camera Output Id '{outputId}'.");
                }

                match = candidate;
            }

            return match ?? throw new InvalidOperationException(
                $"Persisted QA_UIGlobal is missing Camera Output Id '{outputId}'.");
        }

        private static void ValidateOutput(
            CameraOutputAuthoring output,
            string label,
            string expectedOutputId)
        {
            if (output.OutputIdText != expectedOutputId ||
                output.UnityCamera == null ||
                output.CinemachineBrain == null ||
                output.DefaultCameraRig == null)
            {
                throw new InvalidOperationException(
                    $"Persisted Camera Output {label} ('{expectedOutputId}') is incomplete. " +
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
