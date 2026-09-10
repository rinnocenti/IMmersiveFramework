using System;
using Immersive.Framework.CameraAuthoring;
using UnityEngine;

namespace ImmersiveFrameworkQA.Camera.Editor
{
    /// <summary>
    /// Transient Camera definition assets for Editor QA. Definitions are the
    /// current identity authority; callers must destroy the returned objects.
    /// </summary>
    internal static class QaCameraAuthoringFixtures
    {
        internal static CameraOutputDefinition CreateOutputDefinition() =>
            CreateOutputDefinition(Guid.NewGuid().ToString("N"));

        internal static CameraViewDefinition CreateViewDefinition() =>
            CreateViewDefinition(Guid.NewGuid().ToString("N"));

        internal static CameraOutputDefinition CreateOutputDefinition(string stableId)
        {
            var definition = ScriptableObject.CreateInstance<CameraOutputDefinition>();
            JsonUtility.FromJsonOverwrite("{\"stableId\":\"" + stableId + "\"}", definition);
            if (!definition.HasValidId)
            {
                UnityEngine.Object.DestroyImmediate(definition);
                throw new InvalidOperationException(
                    $"Transient Camera Output definition identity '{stableId}' is invalid.");
            }

            return definition;
        }

        internal static CameraViewDefinition CreateViewDefinition(string stableId)
        {
            var definition = ScriptableObject.CreateInstance<CameraViewDefinition>();
            JsonUtility.FromJsonOverwrite("{\"stableId\":\"" + stableId + "\"}", definition);
            if (!definition.HasValidId)
            {
                UnityEngine.Object.DestroyImmediate(definition);
                throw new InvalidOperationException(
                    $"Transient Camera View definition identity '{stableId}' is invalid.");
            }

            return definition;
        }

        internal static void Destroy(UnityEngine.Object value)
        {
            if (value != null)
                UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
