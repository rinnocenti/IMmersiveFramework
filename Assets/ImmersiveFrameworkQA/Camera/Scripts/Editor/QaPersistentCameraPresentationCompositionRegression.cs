using System;
using System.Collections.Generic;
using Immersive.Framework.Camera;
using Immersive.Framework.CameraAuthoring;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;

namespace ImmersiveFrameworkQA.Camera.Editor
{
    /// <summary>
    /// ADR-026 public composition negatives that complement, rather than duplicate,
    /// the real boot/viewport/injection proof in the canonical Camera fixture.
    /// </summary>
    internal static class QaPersistentCameraPresentationCompositionRegression
    {
        private const string MenuPath =
            "Immersive Framework/QA/Regressions/Camera/Run Persistent Camera Presentation Composition Regression";

        [MenuItem(MenuPath, priority = 235)]
        private static void Run()
        {
            IReadOnlyList<string> completed = RunForCertification();
            Debug.Log("[QA_PERSISTENT_CAMERA_PRESENTATION_COMPOSITION] " +
                $"status='Passed' cases='{completed.Count}/{completed.Count}' " +
                $"evidence='{string.Join(",", completed)}'.");
        }

        internal static IReadOnlyList<string> RunForCertification()
        {
            var completed = new List<string>();
            VerifyOutputTopology(completed, "one-output", 1, false, true);
            VerifyOutputTopology(completed, "two-distinct-outputs", 2, false, true);
            VerifyOutputTopology(completed, "zero-outputs", 0, false, false);
            VerifyOutputTopology(completed, "duplicate-output-id", 2, true, false);
            VerifyViewTopology(completed, "valid-split-viewports", false, false, true);
            VerifyViewTopology(completed, "conflicting-output-binding", true, false, false);
            VerifyViewTopology(completed, "invalid-viewport", false, true, false);
            VerifySimpleAdvancedDistinctOutputs(completed);
            VerifySimpleAdvancedSameOutputConflict(completed);
            return completed;
        }

        internal static IReadOnlyList<string> RunAdr004BDuplicateOutputCertification()
        {
            var completed = new List<string>();
            VerifyOutputTopology(completed, "duplicate-output-id", 2, true, false);
            return completed;
        }

        private static void VerifyOutputTopology(
            ICollection<string> completed,
            string caseName,
            int outputCount,
            bool duplicateId,
            bool expectedSuccess)
        {
            var roots = new List<GameObject>();
            var definitions = new List<CameraOutputDefinition>();
            CameraOutputSessionTopology topology = null;
            try
            {
                var outputs = new List<CameraOutputAuthoring>();
                string duplicateStableId = duplicateId
                    ? "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
                    : null;
                for (int index = 0; index < outputCount; index++)
                {
                    CameraOutputDefinition definition =
                        QaCameraAuthoringFixtures.CreateOutputDefinition(
                            duplicateStableId ?? Guid.NewGuid().ToString("N"));
                    definitions.Add(definition);
                    outputs.Add(CreateOutput(roots, definition, index));
                }

                bool succeeded = CameraOutputSessionTopology.TryCreate(
                    outputs, out topology, out string diagnostic);
                Require(succeeded == expectedSuccess,
                    $"Case '{caseName}' returned unexpected success='{succeeded}' diagnostic='{diagnostic}'.");
                if (succeeded)
                {
                    Require(topology != null && topology.OutputCount == outputCount,
                        $"Case '{caseName}' did not retain exact output cardinality.");
                }
                else
                {
                    Require(!string.IsNullOrWhiteSpace(diagnostic),
                        $"Case '{caseName}' blocked without diagnostic.");
                }
                completed.Add(caseName);
            }
            finally
            {
                topology?.Dispose();
                for (int index = roots.Count - 1; index >= 0; index--)
                    UnityEngine.Object.DestroyImmediate(roots[index]);
                for (int index = definitions.Count - 1; index >= 0; index--)
                    QaCameraAuthoringFixtures.Destroy(definitions[index]);
            }
        }

        private static CameraOutputAuthoring CreateOutput(
            ICollection<GameObject> roots,
            CameraOutputDefinition outputDefinition,
            int index)
        {
            var root = new GameObject($"QA_ADR026_Output_{index}");
            root.SetActive(false);
            roots.Add(root);
            UnityEngine.Camera camera = root.AddComponent<UnityEngine.Camera>();
            CinemachineBrain brain = root.AddComponent<CinemachineBrain>();
            var rigRoot = new GameObject($"Rig_{index}");
            rigRoot.transform.SetParent(root.transform, false);
            CameraRigComposer composer = rigRoot.AddComponent<CameraRigComposer>();
            CinemachineCamera cinemachine = rigRoot.AddComponent<CinemachineCamera>();
            composer.EditorSetGeneratedReference(cinemachine);
            CameraOutputAuthoring output = root.AddComponent<CameraOutputAuthoring>();
            Set(output, "outputDefinition", outputDefinition);
            Set(output, "unityCamera", camera);
            Set(output, "cinemachineBrain", brain);
            Set(output, "defaultCameraRig", composer);
            Set(output, "initializeOnAwake", false);
            Set(output, "logDiagnostics", false);
            return output;
        }

        private static void VerifyViewTopology(
            ICollection<string> completed,
            string caseName,
            bool conflict,
            bool invalidViewport,
            bool expectedSuccess)
        {
            CameraOutputId outputA = new CameraOutputId("qa.camera.output.a");
            CameraOutputId outputB = new CameraOutputId(
                conflict ? "qa.camera.output.a" : "qa.camera.output.b");
            CameraViewport secondViewport = invalidViewport
                ? new CameraViewport(0.5f, 0f, 0.75f, 1f)
                : new CameraViewport(0.5f, 0f, 0.5f, 1f);
            CameraViewOutputBinding[] bindings =
            {
                new CameraViewOutputBinding(
                    new CameraViewId("qa.camera.view.a"), outputA,
                    new CameraViewport(0f, 0f, 0.5f, 1f)),
                new CameraViewOutputBinding(
                    new CameraViewId("qa.camera.view.b"), outputB, secondViewport)
            };
            bool succeeded = CameraViewOutputTopology.TryCreate(
                bindings, out CameraViewOutputTopology topology, out string diagnostic);
            Require(succeeded == expectedSuccess,
                $"Case '{caseName}' returned unexpected success='{succeeded}' diagnostic='{diagnostic}'.");
            Require(succeeded
                    ? topology != null && topology.BindingCount == 2
                    : !string.IsNullOrWhiteSpace(diagnostic),
                $"Case '{caseName}' returned incomplete topology evidence.");
            completed.Add(caseName);
        }

        private static void VerifySimpleAdvancedDistinctOutputs(ICollection<string> completed)
        {
            var roots = new List<GameObject>();
            var definitions = new List<ScriptableObject>();
            try
            {
                CameraViewDefinition viewA = QaCameraAuthoringFixtures.CreateViewDefinition();
                CameraViewDefinition viewB = QaCameraAuthoringFixtures.CreateViewDefinition();
                CameraOutputDefinition outputA = QaCameraAuthoringFixtures.CreateOutputDefinition();
                CameraOutputDefinition outputB = QaCameraAuthoringFixtures.CreateOutputDefinition();
                definitions.Add(viewA);
                definitions.Add(viewB);
                definitions.Add(outputA);
                definitions.Add(outputB);

                CameraSharedComposition composition = CreateComposition(
                    roots, viewA, outputA, new CameraViewport(0f, 0f, 1f, 1f));
                CameraViewOutputPolicyAuthoring policy = CreatePolicy(
                    roots, viewB, outputB, new CameraViewport(0f, 0f, 1f, 1f));

                Require(composition.TryCreateAssociationBinding(
                        out CameraViewOutputBinding simple, out string simpleIssue),
                    $"Simple association failed. {simpleIssue}");
                Require(policy.TryBuildTopology(
                        out CameraViewOutputTopology advanced, out string advancedIssue) &&
                    advanced != null && advanced.BindingCount == 1,
                    $"Advanced policy must author one distinct Output association. {advancedIssue}");
                Require(!advanced.TryGetBinding(simple.OutputId, out _),
                    "Advanced policy bound the simple Output.");
                Require(advanced.TryGetBinding(viewB.ViewId, outputB.OutputId, out CameraViewOutputBinding policyBinding),
                    "Advanced policy did not project the Secondary Output association.");

                bool succeeded = CameraViewOutputTopology.TryCreate(
                    new[] { simple, policyBinding },
                    out CameraViewOutputTopology aggregate,
                    out string diagnostic);
                Require(succeeded && aggregate != null && aggregate.BindingCount == 2,
                    $"Simple + advanced distinct Outputs must form a valid aggregate. diagnostic='{diagnostic}'.");
                Require(
                    aggregate.TryGetBinding(viewA.ViewId, outputA.OutputId, out _) &&
                    aggregate.TryGetBinding(viewB.ViewId, outputB.OutputId, out _),
                    "Aggregate topology did not retain exact simple and advanced associations.");
                completed.Add("simple-advanced-distinct-outputs");
            }
            finally
            {
                for (int index = roots.Count - 1; index >= 0; index--)
                    UnityEngine.Object.DestroyImmediate(roots[index]);
                for (int index = definitions.Count - 1; index >= 0; index--)
                    QaCameraAuthoringFixtures.Destroy(definitions[index]);
            }
        }

        private static void VerifySimpleAdvancedSameOutputConflict(ICollection<string> completed)
        {
            var roots = new List<GameObject>();
            var definitions = new List<ScriptableObject>();
            try
            {
                CameraViewDefinition viewA = QaCameraAuthoringFixtures.CreateViewDefinition();
                CameraViewDefinition viewB = QaCameraAuthoringFixtures.CreateViewDefinition();
                CameraOutputDefinition output = QaCameraAuthoringFixtures.CreateOutputDefinition();
                definitions.Add(viewA);
                definitions.Add(viewB);
                definitions.Add(output);

                CameraSharedComposition composition = CreateComposition(
                    roots, viewA, output, new CameraViewport(0f, 0f, 1f, 1f));
                CameraViewOutputPolicyAuthoring policy = CreatePolicy(
                    roots, viewB, output, new CameraViewport(0.5f, 0f, 0.5f, 1f));

                Require(composition.TryCreateAssociationBinding(
                        out CameraViewOutputBinding simple, out string simpleIssue),
                    $"Simple association failed. {simpleIssue}");
                Require(policy.TryBuildTopology(
                        out CameraViewOutputTopology advanced, out string advancedIssue) &&
                    advanced != null && advanced.BindingCount == 1,
                    $"Advanced policy failed. {advancedIssue}");
                Require(advanced.TryGetBinding(simple.OutputId, out CameraViewOutputBinding policyBinding),
                    "Conflict case requires the advanced policy to target the same Output.");

                bool succeeded = CameraViewOutputTopology.TryCreate(
                    new[] { simple, policyBinding },
                    out _,
                    out string diagnostic);
                Require(!succeeded,
                    "Simple + advanced associations for the same Output must block.");
                Require(!string.IsNullOrWhiteSpace(diagnostic) && diagnostic.IndexOf(
                        "conflicting bindings", StringComparison.Ordinal) >= 0,
                    $"Same-Output conflict did not report conflicting bindings. diagnostic='{diagnostic}'.");
                completed.Add("simple-advanced-same-output-conflict");
            }
            finally
            {
                for (int index = roots.Count - 1; index >= 0; index--)
                    UnityEngine.Object.DestroyImmediate(roots[index]);
                for (int index = definitions.Count - 1; index >= 0; index--)
                    QaCameraAuthoringFixtures.Destroy(definitions[index]);
            }
        }

        private static CameraSharedComposition CreateComposition(
            ICollection<GameObject> roots,
            CameraViewDefinition view,
            CameraOutputDefinition output,
            CameraViewport viewport)
        {
            var root = new GameObject("QA_ADR027D_SimpleAssociation");
            root.SetActive(false);
            roots.Add(root);
            CameraSharedComposition composition = root.AddComponent<CameraSharedComposition>();
            composition.Configure(
                view,
                output,
                CameraSharedCompositionSubjectPolicyKind.AllAvailableSubjects,
                viewport);
            return composition;
        }

        private static CameraViewOutputPolicyAuthoring CreatePolicy(
            ICollection<GameObject> roots,
            CameraViewDefinition view,
            CameraOutputDefinition output,
            CameraViewport viewport)
        {
            var root = new GameObject("QA_ADR027D_AdvancedPolicy");
            root.SetActive(false);
            roots.Add(root);
            var binding = new CameraViewOutputBindingAuthoring();
            binding.Configure(view, output, viewport);
            CameraViewOutputPolicyAuthoring policy = root.AddComponent<CameraViewOutputPolicyAuthoring>();
            policy.Configure(new[] { binding });
            return policy;
        }

        private static void Set(UnityEngine.Object target, string name, object value)
        {
            var serialized = new SerializedObject(target);
            serialized.Update();
            SerializedProperty property = serialized.FindProperty(name) ??
                throw new InvalidOperationException($"Missing serialized property '{name}'.");
            if (value is bool flag) property.boolValue = flag;
            else if (value is UnityEngine.Object reference) property.objectReferenceValue = reference;
            else throw new InvalidOperationException($"Unsupported value for '{name}'.");
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
