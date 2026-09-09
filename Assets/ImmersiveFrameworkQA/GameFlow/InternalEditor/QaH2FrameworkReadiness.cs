using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Immersive.Framework.ApplicationLifecycle;
using Immersive.Framework.PlayerParticipation;
using UnityEngine;

namespace ImmersiveFrameworkQA.GameFlow.Internal.Editor
{
    public static class QaH2FrameworkReadiness
    {
        internal static async Task RequireStartedRouteAsync(
            FrameworkRuntimeHost host,
            int frameBudget)
        {
            if (host == null)
                throw new InvalidOperationException(
                    "Framework runtime startup requires the resolved FrameworkRuntimeHost.");
            if (frameBudget <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(frameBudget),
                    "Framework runtime startup frame budget must be positive.");

            string lastDiagnostic =
                "FrameworkRuntimeHost StartAsync completion has not been observed.";
            for (int frame = 0; frame < frameBudget; frame++)
            {
                if (host == null)
                    throw new InvalidOperationException(
                        "FrameworkRuntimeHost was destroyed before StartAsync completed. " +
                        lastDiagnostic);

                FrameworkRuntimeState state = host.State;
                if (state.GameFlowStarted && state.CurrentRoute != null)
                    return;

                lastDiagnostic =
                    $"gameFlowStarted='{state.GameFlowStarted}' " +
                    $"route='{state.CurrentRouteName}' " +
                    $"activity='{state.CurrentActivityName}' " +
                    $"activityReady='{state.IsActivityReady}'.";
                await Awaitable.NextFrameAsync();
            }

            throw new TimeoutException(
                "FrameworkRuntimeHost did not complete StartAsync with a current Route " +
                $"within '{frameBudget}' frames. {lastDiagnostic}");
        }

        internal static bool TryResolveUniqueHost(out FrameworkRuntimeHost host) =>
            TryResolveUniqueHost(out host, out _);

        internal static bool TryResolveUniqueHost(
            out FrameworkRuntimeHost host,
            out string diagnostic)
        {
            host = null;
            FrameworkRuntimeHost[] candidates =
                Resources.FindObjectsOfTypeAll<FrameworkRuntimeHost>();
            var loaded = new List<FrameworkRuntimeHost>();
            var seen = new HashSet<FrameworkRuntimeHost>();

            for (int index = 0; index < candidates.Length; index++)
            {
                FrameworkRuntimeHost candidate = candidates[index];
                if (candidate == null ||
                    !candidate.gameObject.scene.IsValid() ||
                    !candidate.gameObject.scene.isLoaded ||
                    !seen.Add(candidate))
                    continue;

                loaded.Add(candidate);
            }

            if (loaded.Count == 0)
            {
                diagnostic = "host='unavailable' candidates='0'.";
                return false;
            }

            if (loaded.Count != 1)
            {
                var details = new List<string>(loaded.Count);
                for (int index = 0; index < loaded.Count; index++)
                {
                    FrameworkRuntimeHost candidate = loaded[index];
                    details.Add(
                        $"object='{candidate.name}' scene='{candidate.gameObject.scene.name}'.");
                }

                diagnostic =
                    $"host='ambiguous' candidates='{loaded.Count}' details='{string.Join(" ", details)}'";
                return false;
            }

            host = loaded[0];
            diagnostic =
                $"host='resolved' candidates='1' object='{host.name}' scene='{host.gameObject.scene.name}'.";
            return true;
        }

        public static bool TryResolveUniqueHost(
            out Component host,
            out string diagnostic)
        {
            host = null;
            if (!TryResolveUniqueHost(
                    out FrameworkRuntimeHost resolved,
                    out diagnostic))
                return false;

            host = resolved;
            return true;
        }

        public static bool TryGetReady(out string diagnostic)
        {
            if (!TryResolveUniqueHost(
                    out FrameworkRuntimeHost host,
                    out diagnostic))
                return false;

            FrameworkRuntimeState state = host.State;
            diagnostic =
                $"{diagnostic} gameFlowStarted='{state.GameFlowStarted}' route='{state.CurrentRouteName}' activity='{state.CurrentActivityName}' activityReady='{state.IsActivityReady}'.";

            return state.GameFlowStarted &&
                state.CurrentRoute != null &&
                state.CurrentActivity != null &&
                state.IsActivityReady;
        }

        public static bool TryGetPlayerGameplaySnapshot(
            out PlayerGameplayRuntimeHostSnapshot snapshot)
        {
            snapshot = null;
            if (!TryResolveUniqueHost(out FrameworkRuntimeHost host))
                return false;

            PlayerGameplayRuntimeHostModule module =
                host.GetComponent<PlayerGameplayRuntimeHostModule>();
            return module != null && module.TryGetSnapshot(out snapshot);
        }
    }
}
