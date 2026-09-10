using System;
using System.Collections.Generic;
using System.Reflection;
using Immersive.Framework.Camera;
using UnityEditor;
using UnityEngine;

namespace ImmersiveFrameworkQA.Camera.Editor
{
    /// <summary>
    /// Regression for automatic and explicit Session Camera Override identities.
    /// New components receive identities through Reset; existing identities are
    /// never replaced automatically or by Generate Missing IDs.
    /// </summary>
    internal static class QaSessionCameraOverrideIdentityAuthoringRegression
    {
        private const string MenuPath =
            "Immersive Framework/QA/Regressions/Camera/Run Session Camera Override Identity Authoring Regression";

        private const string EditorTypeName =
            "Immersive.Framework.Editor.Camera.Bindings.SessionCameraOverrideEditor";

        private const BindingFlags InstanceAny =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [MenuItem(MenuPath, priority = 237)]
        private static void Run()
        {
            var completed = new List<string>();

            VerifyAutomaticIds(completed);
            VerifyResetPreservesExistingIds(completed);
            VerifyResetFillsOnlyMissingIds(completed);
            VerifyEditorGenerateFillsOnlyMissingIds(completed);

            Debug.Log(
                "[QA_SESSION_CAMERA_OVERRIDE_IDENTITY_AUTHORING] " +
                "status='Passed' " +
                $"cases='{completed.Count}' " +
                $"evidence='{string.Join(",", completed)}'.");
        }

        private static void VerifyAutomaticIds(
            ICollection<string> completed)
        {
            var root = new GameObject("QA_SessionCamera_AutomaticIds");

            try
            {
                SessionCameraOverride binding =
                    root.AddComponent<SessionCameraOverride>();

                Require(HasText(binding.ScopeId),
                    "A new Session Camera Override did not receive a Scope ID.");
                Require(HasText(binding.RequestIdText),
                    "A new Session Camera Override did not receive a Request ID.");
                Require(HasText(binding.TieBreakerId),
                    "A new Session Camera Override did not receive a Tie Breaker ID.");
                Require(
                    binding.ScopeId != binding.RequestIdText &&
                    binding.ScopeId != binding.TieBreakerId &&
                    binding.RequestIdText != binding.TieBreakerId,
                    "Session Camera Override automatic IDs must be distinct.");

                completed.Add("automatic-three-ids");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void VerifyResetPreservesExistingIds(
            ICollection<string> completed)
        {
            var root = new GameObject("QA_SessionCamera_PreserveIds");

            try
            {
                SessionCameraOverride binding =
                    root.AddComponent<SessionCameraOverride>();

                SetIds(
                    binding,
                    "qa.session.scope.existing",
                    "qa.session.request.existing",
                    "qa.session.tie.existing");

                InvokeReset(binding);

                Require(binding.ScopeId == "qa.session.scope.existing",
                    "Reset replaced an existing Session Scope ID.");
                Require(binding.RequestIdText == "qa.session.request.existing",
                    "Reset replaced an existing Session Request ID.");
                Require(binding.TieBreakerId == "qa.session.tie.existing",
                    "Reset replaced an existing Session Tie Breaker ID.");

                completed.Add("reset-preserves-existing");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void VerifyResetFillsOnlyMissingIds(
            ICollection<string> completed)
        {
            var root = new GameObject("QA_SessionCamera_ResetMissingOnly");

            try
            {
                SessionCameraOverride binding =
                    root.AddComponent<SessionCameraOverride>();

                SetIds(
                    binding,
                    "qa.session.scope.preserved",
                    string.Empty,
                    "qa.session.tie.preserved");

                InvokeReset(binding);

                Require(binding.ScopeId == "qa.session.scope.preserved",
                    "Reset replaced the populated Session Scope ID.");
                Require(HasText(binding.RequestIdText),
                    "Reset did not fill the missing Session Request ID.");
                Require(binding.TieBreakerId == "qa.session.tie.preserved",
                    "Reset replaced the populated Session Tie Breaker ID.");

                completed.Add("reset-missing-only");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void VerifyEditorGenerateFillsOnlyMissingIds(
            ICollection<string> completed)
        {
            var root = new GameObject("QA_SessionCamera_EditorGenerate");
            UnityEditor.Editor editor = null;

            try
            {
                SessionCameraOverride binding =
                    root.AddComponent<SessionCameraOverride>();

                SetIds(
                    binding,
                    "qa.session.scope.editor.preserved",
                    string.Empty,
                    "qa.session.tie.editor.preserved");

                Type editorType = ResolveType(EditorTypeName);
                editor = UnityEditor.Editor.CreateEditor(binding, editorType);
                Require(editor != null,
                    "Session Camera Override custom Editor could not be created.");

                MethodInfo generate = editorType.GetMethod(
                    "GenerateMissingIds",
                    InstanceAny);
                Require(generate != null,
                    "Session Camera Override Generate Missing IDs action is unavailable.");

                generate.Invoke(editor, null);

                // GenerateMissingIds participates in the same SerializedObject
                // transaction completed by OnInspectorGUI. Reflection bypasses
                // that final apply, so the regression must complete the exact
                // Inspector transaction before reading the target component.
                editor.serializedObject.ApplyModifiedProperties();
                editor.serializedObject.UpdateIfRequiredOrScript();

                Require(binding.ScopeId == "qa.session.scope.editor.preserved",
                    "Generate Missing IDs replaced the Session Scope ID.");
                Require(HasText(binding.RequestIdText),
                    "Generate Missing IDs did not fill the Session Request ID.");
                Require(binding.TieBreakerId == "qa.session.tie.editor.preserved",
                    "Generate Missing IDs replaced the Session Tie Breaker ID.");

                completed.Add("editor-generate-missing-only");
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

        private static void InvokeReset(
            SessionCameraOverride binding)
        {
            MethodInfo reset = typeof(SessionCameraOverride).GetMethod(
                "Reset",
                InstanceAny);
            Require(reset != null,
                "Session Camera Override Reset hook is unavailable.");
            reset.Invoke(binding, null);
        }

        private static void SetIds(
            SessionCameraOverride binding,
            string scopeId,
            string requestId,
            string tieBreakerId)
        {
            var serialized = new SerializedObject(binding);
            serialized.Update();
            serialized.FindProperty("scopeId").stringValue = scopeId;
            serialized.FindProperty("requestId").stringValue = requestId;
            serialized.FindProperty("tieBreakerId").stringValue = tieBreakerId;
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

        private static bool HasText(string value) =>
            !string.IsNullOrWhiteSpace(value);

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
