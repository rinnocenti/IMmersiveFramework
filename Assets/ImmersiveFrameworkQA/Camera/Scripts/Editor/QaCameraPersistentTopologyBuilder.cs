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
        internal const string OutputAPath = DefinitionFolder + "/MainOutput.asset";
        internal const string OutputBPath = DefinitionFolder + "/SecondaryOutput.asset";
        internal const string MissingOutputPath = DefinitionFolder + "/MissingOutput.asset";
        internal const string MainViewPath = DefinitionFolder + "/MainView.asset";
        internal const string SecondaryViewPath = DefinitionFolder + "/SecondaryView.asset";
        internal const string SplitAViewPath = DefinitionFolder + "/SplitAView.asset";
        internal const string SplitBViewPath = DefinitionFolder + "/SplitBView.asset";
        internal const string FollowBehaviorPath = DefinitionFolder + "/FollowBehavior.asset";
        internal const string MountedBehaviorPath = DefinitionFolder + "/MountedBehavior.asset";
        private const string DefinitionFolder = "Assets/ImmersiveFrameworkQA/Camera/Definitions";

        internal static CameraViewport SharedSimpleViewport => new CameraViewport(0f, 0f, 1f, 1f);
        internal static CameraViewport SharedAdvancedViewport => new CameraViewport(0f, 0f, 1f, 1f);
        internal static CameraViewport SplitSimpleViewport => new CameraViewport(0f, 0f, 0.5f, 1f);
        internal static CameraViewport SplitAdvancedViewport => new CameraViewport(0.5f, 0f, 0.5f, 1f);

        internal static T RequireDefinition<T>(string path) where T : ScriptableObject
        {
            var definition = AssetDatabase.LoadAssetAtPath<T>(path);
            if (definition == null)
                throw new InvalidOperationException($"Required Camera definition is missing at '{path}'. Run Camera setup.");
            string issue = CameraDefinitionIdentityEditorUtility.Validate(definition);
            if (!string.IsNullOrEmpty(issue))
                throw new InvalidOperationException($"Camera definition '{path}' is invalid. {issue}");
            return definition;
        }

        private static T CreateDefinitionIfMissing<T>(string path) where T : ScriptableObject
        {
            if (AssetDatabase.LoadMainAssetAtPath(path) == null)
            {
                var definition = ScriptableObject.CreateInstance<T>();
                CameraDefinitionIdentityEditorUtility.GenerateMissingId(definition);
                AssetDatabase.CreateAsset(definition, path);
            }
            return RequireDefinition<T>(path);
        }

        internal static T RequireBehavior<T>(string path) where T : CameraRigBehaviorDefinition
        {
            var definition = AssetDatabase.LoadAssetAtPath<T>(path);
            if (definition == null)
                throw new InvalidOperationException(
                    $"Required Camera Rig Behavior definition is missing at '{path}'. Run Camera setup.");
            if (!definition.TryValidate(out string issue))
                throw new InvalidOperationException(
                    $"Camera Rig Behavior definition '{path}' is invalid. {issue}");
            return definition;
        }

        private static T CreateBehaviorIfMissing<T>(string path) where T : CameraRigBehaviorDefinition
        {
            if (AssetDatabase.LoadMainAssetAtPath(path) == null)
            {
                var definition = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(definition, path);
            }
            return RequireBehavior<T>(path);
        }

        internal static void Build(QaCameraAdr026TopologyMode mode)
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException(
                    "ADR-026 persistent Camera topology can only be rebuilt in Edit Mode.");

            if (!AssetDatabase.IsValidFolder(DefinitionFolder))
                AssetDatabase.CreateFolder("Assets/ImmersiveFrameworkQA/Camera", "Definitions");
            CreateDefinitionIfMissing<CameraOutputDefinition>(OutputAPath);
            CreateDefinitionIfMissing<CameraOutputDefinition>(OutputBPath);
            CreateDefinitionIfMissing<CameraOutputDefinition>(MissingOutputPath);
            CreateBehaviorIfMissing<FollowCameraRigBehaviorDefinition>(FollowBehaviorPath);
            CreateBehaviorIfMissing<MountedCameraRigBehaviorDefinition>(MountedBehaviorPath);
            string viewAPath = mode == QaCameraAdr026TopologyMode.Shared
                ? MainViewPath
                : SplitAViewPath;
            string viewBPath = mode == QaCameraAdr026TopologyMode.Shared
                ? SecondaryViewPath
                : SplitBViewPath;
            CreateDefinitionIfMissing<CameraViewDefinition>(viewAPath);
            CreateDefinitionIfMissing<CameraViewDefinition>(viewBPath);
            AssetDatabase.SaveAssets();

            Scene scene = EditorSceneManager.OpenScene(GlobalScenePath, OpenSceneMode.Single);
            RemoveOwnedCameraTopology(scene);

            var outputADefinition = RequireDefinition<CameraOutputDefinition>(OutputAPath);
            var outputBDefinition = RequireDefinition<CameraOutputDefinition>(OutputBPath);
            var followBehavior = RequireBehavior<FollowCameraRigBehaviorDefinition>(FollowBehaviorPath);
            var mountedBehavior = RequireBehavior<MountedCameraRigBehaviorDefinition>(MountedBehaviorPath);
            var viewA = RequireDefinition<CameraViewDefinition>(viewAPath);
            var viewB = RequireDefinition<CameraViewDefinition>(viewBPath);

            CameraOutputAuthoring outputA = CreateOutput(
                scene,
                OutputARootName,
                outputADefinition,
                "Main",
                followBehavior);
            CameraOutputAuthoring outputB = CreateOutput(
                scene,
                OutputBRootName,
                outputBDefinition,
                "Secondary",
                mountedBehavior);

            ConfigureSessionOverride(outputA);
            ConfigureSharedComposition(outputA, viewA, SimpleViewport(mode));
            ConfigurePolicy(scene, viewB, outputBDefinition, AdvancedViewport(mode));
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
            CameraOutputDefinition outputDefinition,
            string label,
            CameraRigBehaviorDefinition behavior)
        {
            var root = new GameObject(rootName);
            SceneManager.MoveGameObjectToScene(root, scene);

            // Physical output is created first and never delegated to CameraRigComposer.
            UnityEngine.Camera unityCamera = root.AddComponent<UnityEngine.Camera>();
            CinemachineBrain brain = root.AddComponent<CinemachineBrain>();
            CameraOutputAuthoring output = root.AddComponent<CameraOutputAuthoring>();

            var rigRoot = new GameObject($"QA ADR026 {label} Default Rig");
            rigRoot.transform.SetParent(root.transform, false);
            CameraRigComposer composer = rigRoot.AddComponent<CameraRigComposer>();

            var cameraObject = new GameObject($"QA ADR026 {label} Cinemachine Camera");
            cameraObject.transform.SetParent(rigRoot.transform, false);
            CinemachineCamera cinemachine = cameraObject.AddComponent<CinemachineCamera>();

            AssignComposerBeforeApplyRebuild(composer, behavior, cinemachine, label);

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
            Set(output, "outputDefinition", outputDefinition);
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
            Set(value, "outputDefinition", outputA.OutputDefinition);
            Set(value, "scopeId", "qa.c9r.session.camera");
            Set(value, "requestId", "qa.camera.request.c9r.session");
            Set(value, "rigComposer", outputA.DefaultCameraRig);
            Set(value, "targetSource", outputA.transform);
            Set(value, "precedence", 300);
            Set(value, "tieBreakerId", "session");
            Set(value, "logDiagnostics", true);
        }

        private static void ConfigureSharedComposition(
            CameraOutputAuthoring outputA,
            CameraViewDefinition view,
            CameraViewport viewport)
        {
            CameraSharedComposition value =
                outputA.gameObject.AddComponent<CameraSharedComposition>();
            value.Configure(
                view,
                outputA.OutputDefinition,
                CameraSharedCompositionSubjectPolicyKind.AllAvailableSubjects,
                viewport);
            EditorUtility.SetDirty(value);
        }

        private static void ConfigurePolicy(
            Scene scene,
            CameraViewDefinition viewB,
            CameraOutputDefinition outputB,
            CameraViewport viewport)
        {
            var root = new GameObject(PolicyRootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            CameraViewOutputPolicyAuthoring policy =
                root.AddComponent<CameraViewOutputPolicyAuthoring>();
            policy.Configure(new[] { Binding(viewB, outputB, viewport) });
            EditorUtility.SetDirty(policy);
            EditorUtility.SetDirty(root);
        }

        private static CameraViewOutputBindingAuthoring Binding(
            CameraViewDefinition view,
            CameraOutputDefinition output,
            CameraViewport viewport)
        {
            var binding = new CameraViewOutputBindingAuthoring();
            binding.Configure(view, output, viewport);
            return binding;
        }

        internal static CameraViewport SimpleViewport(QaCameraAdr026TopologyMode mode) =>
            mode == QaCameraAdr026TopologyMode.Shared ? SharedSimpleViewport : SplitSimpleViewport;

        internal static CameraViewport AdvancedViewport(QaCameraAdr026TopologyMode mode) =>
            mode == QaCameraAdr026TopologyMode.Shared ? SharedAdvancedViewport : SplitAdvancedViewport;

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

            ValidateOutput(
                outputA,
                "A",
                RequireDefinition<CameraOutputDefinition>(OutputAPath),
                RequireBehavior<FollowCameraRigBehaviorDefinition>(FollowBehaviorPath));
            ValidateOutput(
                outputB,
                "B",
                RequireDefinition<CameraOutputDefinition>(OutputBPath),
                RequireBehavior<MountedCameraRigBehaviorDefinition>(MountedBehaviorPath));

            if (ReferenceEquals(outputA.UnityCamera, outputB.UnityCamera) ||
                ReferenceEquals(outputA.CinemachineBrain, outputB.CinemachineBrain) ||
                ReferenceEquals(outputA.DefaultCameraRig, outputB.DefaultCameraRig))
                throw new InvalidOperationException(
                    "Rebuilt ADR-026 outputs are not physically independent.");

            CameraSharedComposition composition = outputA.GetComponent<CameraSharedComposition>();
            string viewAPath = mode == QaCameraAdr026TopologyMode.Shared ? MainViewPath : SplitAViewPath;
            string viewBPath = mode == QaCameraAdr026TopologyMode.Shared ? SecondaryViewPath : SplitBViewPath;
            ValidateAggregateAuthoredTopology(
                composition,
                policies[0],
                outputA,
                outputB,
                RequireDefinition<CameraViewDefinition>(viewAPath),
                RequireDefinition<CameraViewDefinition>(viewBPath),
                SimpleViewport(mode),
                AdvancedViewport(mode));

            foreach (PlayerInputManager manager in FindInScene<PlayerInputManager>(scene))
                if (manager.splitScreen)
                    throw new InvalidOperationException(
                        "PlayerInputManager automatic split-screen must remain disabled.");
        }

        internal static void ValidateAggregateAuthoredTopology(
            CameraSharedComposition composition,
            CameraViewOutputPolicyAuthoring policy,
            CameraOutputAuthoring outputA,
            CameraOutputAuthoring outputB,
            CameraViewDefinition viewA,
            CameraViewDefinition viewB,
            CameraViewport simpleViewport,
            CameraViewport advancedViewport)
        {
            if (composition == null)
                throw new InvalidOperationException(
                    "ADR-026 simple Camera association requires CameraSharedComposition on Output A.");
            if (policy == null)
                throw new InvalidOperationException(
                    "ADR-026 advanced Camera association requires exactly one Camera View Output Policy.");
            if (!ReferenceEquals(composition.ViewDefinition, viewA) ||
                !ReferenceEquals(composition.OutputDefinition, outputA.OutputDefinition))
                throw new InvalidOperationException(
                    "Simple Camera association is not bound to the exact Output A View and Output definitions.");
            if (!composition.TryCreateAssociationBinding(
                    out CameraViewOutputBinding simple, out string simpleIssue) ||
                simple.Viewport != simpleViewport)
                throw new InvalidOperationException(
                    $"Simple Camera association did not project the expected Output A viewport. {simpleIssue}");

            if (!policy.TryBuildTopology(out CameraViewOutputTopology advanced, out string advancedIssue) ||
                advanced == null || advanced.BindingCount != 1)
                throw new InvalidOperationException(
                    $"Advanced Camera policy must author exactly one Secondary Output association. {advancedIssue}");
            if (advanced.TryGetBinding(outputA.OutputId, out _))
                throw new InvalidOperationException(
                    "Advanced Camera policy must not bind Output A; Output A is owned by the simple association.");
            if (!advanced.TryGetBinding(viewB.ViewId, outputB.OutputId, out CameraViewOutputBinding policyBinding) ||
                policyBinding.Viewport != advancedViewport)
                throw new InvalidOperationException(
                    "Advanced Camera policy did not project the exact Secondary View to Output B association.");
            if (!policy.TryValidateOutputs(new[] { outputA, outputB }, out string outputIssue))
                throw new InvalidOperationException(outputIssue);

            CameraViewOutputBinding[] aggregate =
            {
                simple,
                policyBinding
            };
            if (!CameraViewOutputTopology.TryCreate(
                    aggregate, out CameraViewOutputTopology topology, out string topologyIssue) ||
                topology == null || topology.BindingCount != 2)
                throw new InvalidOperationException(
                    $"Aggregate Camera View-to-Output topology is invalid. {topologyIssue}");
            if (!topology.TryGetBinding(viewA.ViewId, outputA.OutputId, out CameraViewOutputBinding boundA) ||
                boundA.Viewport != simpleViewport ||
                !topology.TryGetBinding(viewB.ViewId, outputB.OutputId, out CameraViewOutputBinding boundB) ||
                boundB.Viewport != advancedViewport)
                throw new InvalidOperationException(
                    "Aggregate Camera topology did not retain exact simple Output A and advanced Output B associations.");
        }

        internal static void AssignComposerBeforeApplyRebuild(
            CameraRigComposer composer,
            CameraRigBehaviorDefinition behavior,
            CinemachineCamera cinemachine,
            string diagnosticLabel)
        {
            if (composer == null)
                throw new InvalidOperationException(
                    $"ADR-026 {diagnosticLabel} Default rig is missing CameraRigComposer.");
            if (behavior == null)
                throw new InvalidOperationException(
                    $"ADR-026 {diagnosticLabel} Default rig was not given a Camera Rig Behavior Definition. " +
                    "Create and load the Behavior asset before CreateOutput.");

            var serialized = new SerializedObject(composer);
            serialized.Update();
            SerializedProperty behaviorProperty = serialized.FindProperty("behaviorDefinition") ??
                throw new InvalidOperationException(
                    "Serialized property 'behaviorDefinition' was not found on CameraRigComposer.");
            SerializedProperty cameraProperty = serialized.FindProperty("cinemachineCamera") ??
                throw new InvalidOperationException(
                    "Serialized property 'cinemachineCamera' was not found on CameraRigComposer.");
            SerializedProperty logProperty = serialized.FindProperty("logApplyRebuildDiagnostics") ??
                throw new InvalidOperationException(
                    "Serialized property 'logApplyRebuildDiagnostics' was not found on CameraRigComposer.");

            behaviorProperty.objectReferenceValue = behavior;
            cameraProperty.objectReferenceValue = cinemachine;
            logProperty.boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(composer);

            if (!ReferenceEquals(composer.BehaviorDefinition, behavior))
                throw new InvalidOperationException(
                    $"ADR-026 {diagnosticLabel} CameraRigComposer did not receive the exact Behavior Definition before Apply/Rebuild. " +
                    $"passed='{behavior.name}' assigned='{(composer.BehaviorDefinition != null ? composer.BehaviorDefinition.name : "<null>")}'.");
        }

        private static void ValidateOutput(
            CameraOutputAuthoring output,
            string label,
            CameraOutputDefinition expectedDefinition,
            CameraRigBehaviorDefinition expectedBehavior)
        {
            if (output == null || !ReferenceEquals(output.OutputDefinition, expectedDefinition))
                throw new InvalidOperationException(
                    $"ADR-026 Output {label} definition reference is invalid.");
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
            if (expectedBehavior == null)
                throw new InvalidOperationException(
                    $"ADR-026 Output {label} expected Camera Rig Behavior Definition is missing.");
            if (!ReferenceEquals(output.DefaultCameraRig.BehaviorDefinition, expectedBehavior))
                throw new InvalidOperationException(
                    $"ADR-026 Output {label} Default Camera Rig must use the exact '{expectedBehavior.name}' Behavior Definition.");
            if (!expectedBehavior.TryValidate(out string behaviorIssue))
                throw new InvalidOperationException(
                    $"ADR-026 Output {label} Default Camera Rig Behavior Definition is invalid. {behaviorIssue}");
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
