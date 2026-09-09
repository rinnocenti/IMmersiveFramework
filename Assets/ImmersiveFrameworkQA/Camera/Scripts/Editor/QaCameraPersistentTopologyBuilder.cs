using System;
using System.Collections.Generic;
using Immersive.Framework.Camera;
using Immersive.Framework.CameraAuthoring;
using Immersive.Framework.Editor.CameraAuthoring;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace ImmersiveFrameworkQA.Camera.Editor
{
    /// <summary>
    /// Deterministically rebuilds only the Camera-owned portion of the shared QA
    /// persistent-content scene. It intentionally does not repair existing ADR-026
    /// output roots because the repository baseline may contain broken serialized
    /// component references from an interrupted/obsolete migration.
    /// </summary>
    internal static class QaCameraPersistentTopologyBuilder
    {
        private const string GlobalScenePath =
            "Assets/ImmersiveFrameworkQA/UnityBuildSurface/Scenes/QA_UIGlobal.unity";
        private const string OutputARootName = "QA ADR026 Camera Output A";
        private const string OutputBRootName = "QA ADR026 Camera Output B";
        private const string PolicyRootName = "QA ADR026 Camera View Output Policy";
        private const string LegacyOutputRootName = "QA C9R Session Camera Output";
        private const string OutputAId = "camera.output.main";
        private const string OutputBId = "camera.output.secondary";

        internal static void Build(QaCameraAdr026TopologyMode mode)
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException(
                    "ADR-026 persistent Camera topology can only be rebuilt in Edit Mode.");

            Scene scene = EditorSceneManager.OpenScene(GlobalScenePath, OpenSceneMode.Single);
            RemoveOwnedCameraTopology(scene);

            CameraOutputAuthoring outputA = CreateOutput(
                scene,
                OutputARootName,
                OutputAId,
                "Main",
                CameraRigPresentationIntent.Follow);
            CameraOutputAuthoring outputB = CreateOutput(
                scene,
                OutputBRootName,
                OutputBId,
                "Secondary",
                CameraRigPresentationIntent.Mounted);

            ConfigureSessionOverride(outputA);
            ConfigureSharedComposition(outputA, mode);
            ConfigurePolicy(scene, mode);
            DisableAutomaticInputSplitScreen(scene);
            ValidateInMemory(scene, outputA, outputB, mode);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, GlobalScenePath))
                throw new InvalidOperationException(
                    "QA_UIGlobal Camera topology rebuild could not be saved.");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                "[QA_CAMERA_BASELINE] status='Built' " +
                $"topology='{mode}' outputs='2' " +
                $"outputA='{Describe(outputA)}' outputB='{Describe(outputB)}'.");
        }

        private static CameraOutputAuthoring CreateOutput(
            Scene scene,
            string rootName,
            string outputId,
            string label,
            CameraRigPresentationIntent intent)
        {
            var root = new GameObject(rootName);
            SceneManager.MoveGameObjectToScene(root, scene);

            // Physical output is created first and never delegated to CameraRigComposer.
            UnityEngine.Camera unityCamera = root.AddComponent<UnityEngine.Camera>();
            CinemachineBrain brain = root.AddComponent<CinemachineBrain>();
            CameraOutputAuthoring output = root.AddComponent<CameraOutputAuthoring>();

            var targetObject = new GameObject($"QA ADR026 {label} Target");
            targetObject.transform.SetParent(root.transform, false);
            Transform target = targetObject.transform;

            var rigRoot = new GameObject($"QA ADR026 {label} Default Rig");
            rigRoot.transform.SetParent(root.transform, false);
            CameraRigComposer composer = rigRoot.AddComponent<CameraRigComposer>();

            var cameraObject = new GameObject($"QA ADR026 {label} Cinemachine Camera");
            cameraObject.transform.SetParent(rigRoot.transform, false);
            CinemachineCamera cinemachine = cameraObject.AddComponent<CinemachineCamera>();

            Set(composer, "presentationIntent", (int)intent);
            Set(composer, "targetSourceKind", (int)CameraTargetSourceKind.ExplicitTransform);
            Set(composer, "targetSource", null);
            Set(composer, "explicitFollowTarget", target);
            Set(composer, "explicitLookAtTarget", target);
            Set(composer, "followRequirement", (int)(intent == CameraRigPresentationIntent.Fixed
                ? CameraTargetRequirement.Optional
                : CameraTargetRequirement.Required));
            Set(composer, "lookAtRequirement", (int)CameraTargetRequirement.Optional);
            Set(composer, "cinemachineCamera", cinemachine);
            Set(composer, "logApplyRebuildDiagnostics", false);

            CameraRigComposerApplyRebuildResult materialization =
                CameraRigComposerApplyRebuildUtility.ApplyOrRebuild(
                    composer,
                    logDiagnostics: false,
                    useUndo: false);
            if (!materialization.Succeeded)
                throw new InvalidOperationException(
                    $"ADR-026 {label} Default rig could not be materialized. " +
                    materialization.BlockingIssue);

            cinemachine.enabled = false;

            // Bind the physical output only after rig materialization. This prevents any
            // authoring/rebuild work from participating in output-reference assignment.
            Set(output, "outputId", outputId);
            Set(output, "unityCamera", unityCamera);
            Set(output, "cinemachineBrain", brain);
            Set(output, "defaultCameraRig", composer);
            Set(output, "initializeOnAwake", true);
            Set(output, "logDiagnostics", true);

            EditorUtility.SetDirty(unityCamera);
            EditorUtility.SetDirty(brain);
            EditorUtility.SetDirty(composer);
            EditorUtility.SetDirty(output);
            EditorUtility.SetDirty(root);

            if (!ReferenceEquals(output.UnityCamera, unityCamera) ||
                !ReferenceEquals(output.CinemachineBrain, brain) ||
                !ReferenceEquals(output.DefaultCameraRig, composer))
            {
                throw new InvalidOperationException(
                    $"ADR-026 Output {label} failed immediate physical reference binding. " +
                    $"camera='{(output.UnityCamera != null)}' " +
                    $"brain='{(output.CinemachineBrain != null)}' " +
                    $"defaultRig='{(output.DefaultCameraRig != null)}'.");
            }

            return output;
        }

        private static void ConfigureSessionOverride(CameraOutputAuthoring outputA)
        {
            SessionCameraOverride value = outputA.gameObject.AddComponent<SessionCameraOverride>();
            Set(value, "outputId", OutputAId);
            Set(value, "scopeId", "qa.c9r.session.camera");
            Set(value, "requestId", "qa.camera.request.c9r.session");
            Set(value, "rigComposer", outputA.DefaultCameraRig);
            Set(value, "targetSource", outputA.DefaultCameraRig.ExplicitFollowTarget);
            Set(value, "precedence", 300);
            Set(value, "tieBreakerId", "session");
            Set(value, "logDiagnostics", true);
        }

        private static void ConfigureSharedComposition(
            CameraOutputAuthoring outputA,
            QaCameraAdr026TopologyMode mode)
        {
            CameraSharedComposition value =
                outputA.gameObject.AddComponent<CameraSharedComposition>();
            Set(value, "viewId", mode == QaCameraAdr026TopologyMode.Shared
                ? "camera.view.main"
                : "camera.view.split.a");
            Set(value, "viewDescription", "ADR-026 canonical shared Player Camera View");
            Set(value, "assignmentContextId", "qa.camera.adr026.assignments");
            Set(value, "assignmentOwnerId", "qa.camera.adr026.shared-composition");
            Set(value, "subjectPolicy",
                (int)CameraSharedCompositionSubjectPolicyKind.AllAvailableSubjects);
            Set(value, "outputId", OutputAId);
            Set(value, "composer", outputA.DefaultCameraRig);
        }

        private static void ConfigurePolicy(
            Scene scene,
            QaCameraAdr026TopologyMode mode)
        {
            var root = new GameObject(PolicyRootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            CameraViewOutputPolicyAuthoring policy =
                root.AddComponent<CameraViewOutputPolicyAuthoring>();

            CameraViewOutputBinding[] bindings = mode == QaCameraAdr026TopologyMode.Shared
                ? new[]
                {
                    Binding("camera.view.main", OutputAId, 0f, 0f, 1f, 1f),
                    Binding("camera.view.secondary", OutputBId, 0f, 0f, 1f, 1f)
                }
                : new[]
                {
                    Binding("camera.view.split.a", OutputAId, 0f, 0f, 0.5f, 1f),
                    Binding("camera.view.split.b", OutputBId, 0.5f, 0f, 0.5f, 1f)
                };

            policy.Configure(bindings);
            EditorUtility.SetDirty(policy);
            EditorUtility.SetDirty(root);
        }

        private static CameraViewOutputBinding Binding(
            string viewId,
            string outputId,
            float x,
            float y,
            float width,
            float height) =>
            new CameraViewOutputBinding(
                new CameraViewId(viewId),
                new CameraOutputId(outputId),
                new CameraViewport(x, y, width, height));

        private static void RemoveOwnedCameraTopology(Scene scene)
        {
            // Camera outputs in QA_UIGlobal are owned by this Camera setup surface. Remove
            // the whole authored Camera topology and rebuild it instead of attempting to
            // preserve broken component references from the old migration.
            var destroy = new HashSet<GameObject>();

            foreach (CameraOutputAuthoring output in FindInScene<CameraOutputAuthoring>(scene))
                if (output != null) destroy.Add(output.gameObject);

            foreach (CameraViewOutputPolicyAuthoring policy in
                     FindInScene<CameraViewOutputPolicyAuthoring>(scene))
                if (policy != null) destroy.Add(policy.gameObject);

            foreach (SessionCameraOverride sessionOverride in
                     FindInScene<SessionCameraOverride>(scene))
                if (sessionOverride != null) destroy.Add(sessionOverride.gameObject);

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root == null) continue;
                if (root.name == OutputARootName ||
                    root.name == OutputBRootName ||
                    root.name == PolicyRootName ||
                    root.name == LegacyOutputRootName)
                {
                    destroy.Add(root);
                }
            }

            foreach (GameObject candidate in destroy)
                if (candidate != null) UnityEngine.Object.DestroyImmediate(candidate);
        }

        private static void DisableAutomaticInputSplitScreen(Scene scene)
        {
            foreach (PlayerInputManager manager in FindInScene<PlayerInputManager>(scene))
            {
                var serialized = new SerializedObject(manager);
                serialized.Update();
                SerializedProperty property = serialized.FindProperty("m_SplitScreen") ??
                    throw new InvalidOperationException(
                        "PlayerInputManager split-screen serialized field was not found.");
                property.boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(manager);
            }
        }

        private static void ValidateInMemory(
            Scene scene,
            CameraOutputAuthoring outputA,
            CameraOutputAuthoring outputB,
            QaCameraAdr026TopologyMode mode)
        {
            List<CameraOutputAuthoring> outputs = FindInScene<CameraOutputAuthoring>(scene);
            List<CameraViewOutputPolicyAuthoring> policies =
                FindInScene<CameraViewOutputPolicyAuthoring>(scene);
            List<SessionCameraOverride> sessionOverrides =
                FindInScene<SessionCameraOverride>(scene);

            if (outputs.Count != 2 || policies.Count != 1 || sessionOverrides.Count != 1)
                throw new InvalidOperationException(
                    "Rebuilt QA_UIGlobal Camera topology has invalid cardinality. " +
                    $"outputs='{outputs.Count}' policies='{policies.Count}' " +
                    $"sessionOverrides='{sessionOverrides.Count}'.");

            ValidateOutput(outputA, "A", OutputAId);
            ValidateOutput(outputB, "B", OutputBId);

            if (ReferenceEquals(outputA.UnityCamera, outputB.UnityCamera) ||
                ReferenceEquals(outputA.CinemachineBrain, outputB.CinemachineBrain) ||
                ReferenceEquals(outputA.DefaultCameraRig, outputB.DefaultCameraRig))
                throw new InvalidOperationException(
                    "Rebuilt ADR-026 outputs are not physically independent.");

            if (!policies[0].TryBuildTopology(out CameraViewOutputTopology topology, out string issue) ||
                topology == null || topology.BindingCount != 2)
                throw new InvalidOperationException(
                    $"Rebuilt ADR-026 View-to-Output topology is invalid for '{mode}'. {issue}");

            foreach (PlayerInputManager manager in FindInScene<PlayerInputManager>(scene))
                if (manager.splitScreen)
                    throw new InvalidOperationException(
                        "PlayerInputManager automatic split-screen must remain disabled.");
        }

        private static void ValidateOutput(
            CameraOutputAuthoring output,
            string label,
            string expectedId)
        {
            if (output == null || output.OutputIdText != expectedId)
                throw new InvalidOperationException(
                    $"ADR-026 Output {label} identity is invalid. expected='{expectedId}'.");
            if (output.UnityCamera == null)
                throw new InvalidOperationException(
                    $"ADR-026 Output {label} has no explicit Unity Camera after rebuild.");
            if (output.CinemachineBrain == null)
                throw new InvalidOperationException(
                    $"ADR-026 Output {label} has no explicit CinemachineBrain after rebuild.");
            if (output.DefaultCameraRig == null)
                throw new InvalidOperationException(
                    $"ADR-026 Output {label} has no explicit Default Camera Rig after rebuild.");
            if (!ReferenceEquals(output.UnityCamera.gameObject, output.CinemachineBrain.gameObject))
                throw new InvalidOperationException(
                    $"ADR-026 Output {label} Camera and Brain must share one GameObject.");
        }

        private static List<T> FindInScene<T>(Scene scene) where T : Component
        {
            var results = new List<T>();
            foreach (T value in Resources.FindObjectsOfTypeAll<T>())
                if (value != null && value.gameObject.scene == scene) results.Add(value);
            return results;
        }

        private static void Set(UnityEngine.Object target, string propertyName, object value)
        {
            var serialized = new SerializedObject(target);
            serialized.Update();
            SerializedProperty property = serialized.FindProperty(propertyName) ??
                throw new InvalidOperationException(
                    $"Serialized property '{propertyName}' was not found on '{target.GetType().Name}'.");

            if (value == null) property.objectReferenceValue = null;
            else if (value is UnityEngine.Object reference) property.objectReferenceValue = reference;
            else if (value is string text) property.stringValue = text;
            else if (value is int number) property.intValue = number;
            else if (value is bool flag) property.boolValue = flag;
            else throw new InvalidOperationException(
                $"Unsupported serialized value for '{propertyName}'.");

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static string Describe(CameraOutputAuthoring output) =>
            $"id={output.OutputIdText};camera={output.UnityCamera.name};" +
            $"brain={output.CinemachineBrain.name};rig={output.DefaultCameraRig.name}";
    }
}
