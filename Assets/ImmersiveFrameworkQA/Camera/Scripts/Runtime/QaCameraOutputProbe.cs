using Immersive.Framework.Camera;
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
        [SerializeField] private string outputId;
        [SerializeField] private CameraOutputAuthoring output;
        [SerializeField] private string lastDetachReason;
        [SerializeField] private int attachmentCount;

        public string OutputIdText =>
            string.IsNullOrWhiteSpace(outputId) ? string.Empty : outputId.Trim();

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
