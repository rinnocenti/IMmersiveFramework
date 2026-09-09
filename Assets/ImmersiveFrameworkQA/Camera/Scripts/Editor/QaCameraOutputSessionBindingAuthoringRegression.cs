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
    /// Regression for the persistent Camera Output authoring surface.
    /// The regression uses reflection only for package-internal Editor APIs.
    /// </summary>
    internal static class QaCameraOutputAuthoringAuthoringRegression
    {
        private const string MenuPath =
            "Immersive Framework/QA/Regressions/Camera/Run Camera Output Session Binding Authoring Regression";

        private const string ValidatorTypeName =
            "Immersive.Framework.Editor.CameraAuthoring.CameraOutputSessionAuthoringValidator";
        private const string EditorTypeName =
            "Immersive.Framework.Editor.Camera.Bindings.CameraOutputAuthoringEditor";
        private const string BrainTypeName =
            "Unity.Cinemachine.CinemachineBrain";

        private const BindingFlags InstanceAny =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags StaticAny =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        [MenuItem(MenuPath, priority = 236)]
        private static void Run()
        {
            IReadOnlyList<string> completed = RunForCertification();

            Debug.Log(
                "[QA_CAMERA_OUTPUT_SESSION_BINDING_AUTHORING] " +
                "status='Passed' " +
                $"cases='{completed.Count}' " +
                $"evidence='{string.Join(",", completed)}'.");
        }

        internal static IReadOnlyList<string> RunForCertification()
        {
            var completed = new List<string>();

            VerifyAutomaticId(completed);
            VerifyResetPreservesExistingId(completed);
            VerifyGenerateOnlyWhenMissing(completed);
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

        private static void VerifyAutomaticId(
            ICollection<string> completed)
        {
            var root = new GameObject("QA_CameraOutput_AutomaticId");
            root.SetActive(false);
            try
            {
                CameraOutputAuthoring binding =
                    root.AddComponent<CameraOutputAuthoring>();
                Require(
                    !string.IsNullOrWhiteSpace(binding.OutputIdText),
                    "A newly created Camera Output Session Binding did not receive an Output ID.");
                completed.Add("automatic-id");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void VerifyResetPreservesExistingId(
            ICollection<string> completed)
        {
            var root = new GameObject("QA_CameraOutput_PreserveId");
            root.SetActive(false);
            try
            {
                CameraOutputAuthoring binding =
                    root.AddComponent<CameraOutputAuthoring>();
                SetOutputId(binding, "qa.camera.output.existing");

                MethodInfo reset = typeof(CameraOutputAuthoring).GetMethod(
                    "Reset",
                    InstanceAny);
                Require(reset != null,
                    "Camera Output Reset hook is unavailable.");
                reset.Invoke(binding, null);

                Require(
                    binding.OutputIdText == "qa.camera.output.existing",
                    "Reset replaced an existing Camera Output ID.");
                completed.Add("existing-id-preserved");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void VerifyGenerateOnlyWhenMissing(
            ICollection<string> completed)
        {
            var root = new GameObject("QA_CameraOutput_GenerateId");
            root.SetActive(false);
            UnityEditor.Editor editor = null;

            try
            {
                CameraOutputAuthoring binding =
                    root.AddComponent<CameraOutputAuthoring>();
                SetOutputId(binding, string.Empty);

                Type editorType = ResolveType(EditorTypeName);
                editor = UnityEditor.Editor.CreateEditor(binding, editorType);
                Require(editor != null,
                    "Camera Output custom Editor could not be created.");

                MethodInfo generate = editorType.GetMethod(
                    "GenerateOutputId",
                    InstanceAny);
                Require(generate != null,
                    "Camera Output Generate action is unavailable.");

                generate.Invoke(editor, null);
                string generated = binding.OutputIdText;
                Require(
                    !string.IsNullOrWhiteSpace(generated),
                    "Generate did not fill an empty Camera Output ID.");

                SetOutputId(binding, "qa.camera.output.preserved");
                generate.Invoke(editor, null);
                Require(
                    binding.OutputIdText == "qa.camera.output.preserved",
                    "Generate replaced a populated Camera Output ID.");

                completed.Add("generate-missing-only");
            }
            finally
            {
                if (editor != null)
                {
                    UnityEngine.Object.DestroyImmediate(editor);
                }

                UnityEngine.Object.DestroyImmediate(root);
            }
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

            try
            {
                var outputRoot = new GameObject($"QA_{caseName}_Output");
                outputRoot.SetActive(false);
                roots.Add(outputRoot);

                CameraOutputAuthoring binding =
                    outputRoot.AddComponent<CameraOutputAuthoring>();
                SetOutputId(binding, $"qa.camera.output.{caseName}");
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

                AssignOutputReferences(binding, camera, brain, defaultRig);
                ValidationProbe validation = Validate(binding);

                Require(
                    validation.IsValid == expectedValid,
                    $"Case '{caseName}' returned unexpected validity='{validation.IsValid}'.");

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

        private static void SetOutputId(
            CameraOutputAuthoring binding,
            string value)
        {
            var serialized = new SerializedObject(binding);
            SerializedProperty property = serialized.FindProperty("outputId");
            Require(property != null,
                "Camera Output ID serialized field is unavailable.");
            property.stringValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AssignOutputReferences(
            CameraOutputAuthoring binding,
            UnityEngine.Camera camera,
            Component brain,
            CameraRigComposer defaultRig)
        {
            var serialized = new SerializedObject(binding);
            SerializedProperty cameraProperty =
                serialized.FindProperty("unityCamera");
            SerializedProperty brainProperty =
                serialized.FindProperty("cinemachineBrain");
            SerializedProperty defaultRigProperty =
                serialized.FindProperty("defaultCameraRig");

            Require(cameraProperty != null && brainProperty != null &&
                    defaultRigProperty != null,
                "Camera Output component references are unavailable.");

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
