using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Immersive.Framework.Camera;
using Immersive.Framework.CameraAuthoring;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;

namespace ImmersiveFrameworkQA.Camera.Editor
{
    /// <summary>
    /// Consumer regression for the current physical Camera Output authoring
    /// surface. Output identity is owned by CameraOutputDefinition; this proof
    /// covers Inspector validation of Camera, CinemachineBrain and Default Rig.
    /// </summary>
    internal static class QaCameraOutputAuthoringAuthoringRegression
    {
        private const string MenuPath =
            "Immersive Framework/QA/Regressions/Camera/Run Camera Output Physical Authoring Regression";

        private const string ValidatorTypeName =
            "Immersive.Framework.Editor.CameraAuthoring.CameraOutputSessionAuthoringValidator";
        private const string BrainTypeName =
            "Unity.Cinemachine.CinemachineBrain";

        private const BindingFlags InstanceAny =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags StaticAny =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        [MenuItem(MenuPath, true)]
        private static bool ValidateRun() => !EditorApplication.isPlaying;

        [MenuItem(MenuPath, priority = 236)]
        private static void Run()
        {
            IReadOnlyList<string> completed = RunForCertification();

            Debug.Log(
                "[QA_CAMERA_OUTPUT_PHYSICAL_AUTHORING] " +
                "status='Passed' " +
                $"cases='{completed.Count}' " +
                $"evidence='{string.Join(",", completed)}'.");
        }

        internal static IReadOnlyList<string> RunForCertification()
        {
            var completed = new List<string>();

            VerifyValidationCase(completed, "valid-composition", true, true, true, true);
            VerifyValidationCase(completed, "missing-camera", false, true, true, false);
            VerifyValidationCase(completed, "missing-brain", true, false, true, false);
            VerifyValidationCase(completed, "split-camera-brain", true, true, false, false);

            return completed;
        }

        internal static IReadOnlyList<string> RunAdr004BInvalidReferenceCertification()
        {
            var completed = new List<string>();
            VerifyValidationCase(completed, "missing-camera", false, true, true, false);
            VerifyValidationCase(completed, "missing-brain", true, false, true, false);
            VerifyValidationCase(completed, "split-camera-brain", true, true, false, false);
            return completed;
        }

        private static void VerifyValidationCase(
            ICollection<string> completed,
            string caseName,
            bool includeCamera,
            bool includeBrain,
            bool sameObject,
            bool expectedValid)
        {
            var roots = new List<GameObject>();
            CameraOutputDefinition definition = null;

            try
            {
                var outputRoot = new GameObject($"QA_{caseName}_Output");
                outputRoot.SetActive(false);
                roots.Add(outputRoot);

                definition = QaCameraAuthoringFixtures.CreateOutputDefinition();
                CameraOutputAuthoring binding =
                    outputRoot.AddComponent<CameraOutputAuthoring>();
                var rigRoot = new GameObject($"QA_{caseName}_DefaultRig");
                rigRoot.transform.SetParent(outputRoot.transform, false);
                CameraRigComposer defaultRig =
                    rigRoot.AddComponent<CameraRigComposer>();
                CinemachineCamera cinemachine =
                    rigRoot.AddComponent<CinemachineCamera>();
                defaultRig.EditorSetGeneratedReference(cinemachine);

                UnityEngine.Camera camera = includeCamera
                    ? outputRoot.AddComponent<UnityEngine.Camera>()
                    : null;

                Component brain = null;
                if (includeBrain)
                {
                    GameObject brainRoot = outputRoot;
                    if (!sameObject)
                    {
                        brainRoot = new GameObject($"QA_{caseName}_Brain");
                        brainRoot.SetActive(false);
                        roots.Add(brainRoot);
                    }

                    brain = brainRoot.AddComponent(ResolveType(BrainTypeName));
                }

                AssignOutputReferences(binding, definition, camera, brain, defaultRig);
                ValidationProbe validation = Validate(binding);

                Require(
                    validation.IsValid == expectedValid,
                    $"Case '{caseName}' returned unexpected validity='{validation.IsValid}' diagnostics='{validation.Diagnostics}'.");

                if (!expectedValid)
                {
                    Require(
                        validation.BlockingIssueCount > 0 &&
                        !string.IsNullOrWhiteSpace(validation.Diagnostics),
                        $"Case '{caseName}' blocked without actionable authoring diagnostics.");
                }

                completed.Add(caseName);
            }
            finally
            {
                for (int index = roots.Count - 1; index >= 0; index--)
                {
                    UnityEngine.Object.DestroyImmediate(roots[index]);
                }

                QaCameraAuthoringFixtures.Destroy(definition);
            }
        }

        private static ValidationProbe Validate(
            CameraOutputAuthoring binding)
        {
            Type validatorType = ResolveType(ValidatorTypeName);
            MethodInfo validate = validatorType.GetMethod(
                "Validate",
                StaticAny);
            Require(validate != null,
                "Camera Output authoring validator is unavailable.");

            object result = validate.Invoke(null, new object[] { binding });
            Require(result != null,
                "Camera Output authoring validator returned no result.");

            Type resultType = result.GetType();
            PropertyInfo isValid = resultType.GetProperty("IsValid", InstanceAny);
            PropertyInfo issueCount = resultType.GetProperty("BlockingIssueCount", InstanceAny);
            PropertyInfo issues = resultType.GetProperty("BlockingIssues", InstanceAny);
            Require(
                isValid != null && issueCount != null && issues != null,
                "Camera Output validation result does not expose complete blocking evidence.");

            var diagnostics = new List<string>();
            if (issues.GetValue(result) is IEnumerable enumerable)
            {
                foreach (object item in enumerable)
                {
                    string text = item as string;
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        diagnostics.Add(text.Trim());
                    }
                }
            }

            return new ValidationProbe(
                (bool)isValid.GetValue(result),
                (int)issueCount.GetValue(result),
                string.Join(" | ", diagnostics));
        }

        private static void AssignOutputReferences(
            CameraOutputAuthoring binding,
            CameraOutputDefinition definition,
            UnityEngine.Camera camera,
            Component brain,
            CameraRigComposer defaultRig)
        {
            var serialized = new SerializedObject(binding);
            SerializedProperty definitionProperty =
                serialized.FindProperty("outputDefinition");
            SerializedProperty cameraProperty =
                serialized.FindProperty("unityCamera");
            SerializedProperty brainProperty =
                serialized.FindProperty("cinemachineBrain");
            SerializedProperty defaultRigProperty =
                serialized.FindProperty("defaultCameraRig");

            Require(definitionProperty != null && cameraProperty != null &&
                    brainProperty != null && defaultRigProperty != null,
                "Camera Output component references are unavailable.");

            definitionProperty.objectReferenceValue = definition;
            cameraProperty.objectReferenceValue = camera;
            brainProperty.objectReferenceValue = brain;
            defaultRigProperty.objectReferenceValue = defaultRig;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Type ResolveType(string fullName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int index = 0; index < assemblies.Length; index++)
            {
                Type type = assemblies[index].GetType(fullName, false);
                if (type != null)
                {
                    return type;
                }
            }

            throw new InvalidOperationException(
                $"Type '{fullName}' is unavailable.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private readonly struct ValidationProbe
        {
            public ValidationProbe(
                bool isValid,
                int blockingIssueCount,
                string diagnostics)
            {
                IsValid = isValid;
                BlockingIssueCount = blockingIssueCount;
                Diagnostics = diagnostics ?? string.Empty;
            }

            public bool IsValid { get; }
            public int BlockingIssueCount { get; }
            public string Diagnostics { get; }
        }
    }
}
