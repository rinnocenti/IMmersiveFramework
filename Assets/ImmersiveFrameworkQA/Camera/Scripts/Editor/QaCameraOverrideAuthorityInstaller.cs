using System;
using ImmersiveFrameworkQA.Player.Editor;
using UnityEditor;
using UnityEngine;

namespace ImmersiveFrameworkQA.Camera.Editor
{
    /// <summary>
    /// Public/editor entry point for the canonical Camera QA setup.
    /// Persistent Camera topology is rebuilt deterministically instead of attempting
    /// to repair potentially broken serialized ADR-026 output roots in place.
    /// </summary>
    internal static class QaCameraOverrideAuthorityInstaller
    {
        [MenuItem("Immersive Framework/QA/Setup/Camera/Install Camera Override Authority QA")]
        private static void InstallCanonical() =>
            Install(QaCameraAdr026TopologyMode.Shared);

        [MenuItem("Immersive Framework/QA/Setup/Camera/Prepare ADR-026 Shared Phase")]
        private static void InstallShared() =>
            Install(QaCameraAdr026TopologyMode.Shared);

        [MenuItem("Immersive Framework/QA/Setup/Camera/Prepare ADR-026 Split Phase")]
        private static void InstallSplit() =>
            Install(QaCameraAdr026TopologyMode.Split);

        internal static void Install(QaCameraAdr026TopologyMode mode)
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException(
                    "ADR-026 Camera setup must run in Edit Mode before a fresh Framework boot.");

            try
            {
                PlayerQaSceneBuilder.EnsureSharedFixtures();
                QaCameraPersistentTopologyBuilder.Build(mode);
                QaCameraOverrideAuthoritySceneInstaller.Install(mode);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Debug.Log(
                    "[_CAMERA_OVERRIDE_AUTHORITY_SETUP] " +
                    $"status='Succeeded' adr026Mode='{mode}' outputs='2' " +
                    "persistentTopology='RebuiltAndSaved' sceneRail='Repaired'.");
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "[_CAMERA_OVERRIDE_AUTHORITY_SETUP] " +
                    $"status='Failed' adr026Mode='{mode}' " +
                    $"exception='{exception.GetType().Name}' " +
                    $"message='{Escape(exception.GetBaseException().Message)}'.");
                throw;
            }
        }

        private static string Escape(string value) =>
            (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("'", "\\'")
                .Replace("\r", " ")
                .Replace("\n", " ");
    }
}
