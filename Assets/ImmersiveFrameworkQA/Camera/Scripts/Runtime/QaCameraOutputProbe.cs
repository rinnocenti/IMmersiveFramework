using Immersive.Framework.Camera;
using Immersive.Framework.CameraAuthoring;
using UnityEngine;

namespace ImmersiveFrameworkQA.Camera
{
    /// <summary>
    /// Composition-bound QA endpoint for one exact Framework Camera Output.
    /// It observes only the output explicitly injected by the Session topology.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class QaCameraOutputProbe :
        MonoBehaviour,
        ICameraOutputSessionConsumer
    {
        [SerializeField] private CameraOutputDefinition outputDefinition;
        [SerializeField] private CameraOutputAuthoring output;
        [SerializeField] private string lastDetachReason;
        [SerializeField] private int attachmentCount;

        public CameraOutputDefinition OutputDefinition => outputDefinition;

        public string OutputIdText =>
            outputDefinition != null && outputDefinition.HasValidId
                ? outputDefinition.OutputId.Value
                : string.Empty;

        public CameraOutputId RequestedOutputId =>
            new CameraOutputId(OutputIdText);

        public CameraOutputAuthoring Output => output;
        public bool IsAttached => output != null;
        public string LastDetachReason => lastDetachReason ?? string.Empty;
        public int AttachmentCount => attachmentCount;

        public void AttachOutputSession(CameraOutputAuthoring binding)
        {
            output = binding;
            lastDetachReason = string.Empty;
            attachmentCount++;
        }

        public void DetachOutputSession(string reason)
        {
            output = null;
            lastDetachReason = reason ?? string.Empty;
        }
    }
}
