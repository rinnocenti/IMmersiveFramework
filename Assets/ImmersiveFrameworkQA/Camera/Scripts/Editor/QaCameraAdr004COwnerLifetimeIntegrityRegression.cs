using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ImmersiveFrameworkQA.Camera.Editor
{
    /// <summary>
    /// IF-ADR-004C owner-lifetime certification.
    /// Consumes evidence produced by the canonical C9R fixture in the same
    /// Play Mode session. It does not create a second Camera runtime or fixture.
    /// </summary>
    internal static class QaCameraAdr004COwnerLifetimeIntegrityRegression
    {
        private const string MenuPath =
            "Immersive Framework/QA/Regressions/Camera/Run ADR-004C Owner Lifetime Integrity Certification";
        private const string LogPrefix = "[QA_CAMERA_ADR004C]";
        private const int ExpectedCaseCount = 10;

        [MenuItem(MenuPath, true)]
        private static bool ValidateRun() => EditorApplication.isPlaying;

        [MenuItem(MenuPath, priority = 238)]
        private static void Run()
        {
            RunCertification();
        }

        internal static bool RunCertification()
        {
            var results = new List<CaseResult>(ExpectedCaseCount);

            Execute(results, "01-activity-normal-exit", Case01ActivityNormalExit);
            Execute(results, "02-route-normal-exit", Case02RouteNormalExit);
            Execute(results, "03-session-disable-cleanup", Case03SessionDisableCleanup);
            Execute(results, "04-route-disable-cleanup", Case04RouteDisableCleanup);
            Execute(results, "05-activity-disable-cleanup", Case05ActivityDisableCleanup);
            Execute(results, "06-activity-destruction-cleanup", Case06ActivityDestructionCleanup);
            Execute(results, "07-nonwinner-owner-only", Case07NonWinnerOwnerOnly);
            Execute(results, "08-winning-owner-restores-next", Case08WinningOwnerRestoresNext);
            Execute(results, "09-cleanup-idempotent", Case09CleanupIdempotent);
            Execute(results, "10-reenable-no-silent-republish", Case10ReenableNoSilentRepublish);

            Require(
                results.Count == ExpectedCaseCount,
                $"ADR-004C case count changed. expected='{ExpectedCaseCount}' actual='{results.Count}'.");

            int passed = results.Count(item => item.Passed);
            int failed = results.Count - passed;

            if (failed == 0)
            {
                Debug.Log(
                    $"{LogPrefix} status='Passed' cases='{passed}/{ExpectedCaseCount}' " +
                    "failed='0' " +
                    "verdict='ADR-004C CAMERA OWNER LIFETIME INTEGRITY CERTIFIED'.");
                return true;
            }

            Debug.LogError(
                $"{LogPrefix} status='Failed' cases='{passed}/{ExpectedCaseCount}' " +
                $"failed='{failed}' " +
                "verdict='ADR-004C NOT CERTIFIED — OWNER LIFETIME INTEGRITY FAILURE'.");
            return false;
        }

        private static string Case01ActivityNormalExit()
        {
            Require(QaCameraOverrideAuthorityFixture.Adr004BActivityLifecycleExecuted,
                "Canonical C9R Activity lifecycle evidence has not executed in this Play Mode session.");
            Require(QaCameraOverrideAuthorityFixture.Adr004BActivityLifecyclePassed,
                "Canonical Activity exit did not release its Camera request.");
            return "operation='NormalActivityExit' owner='Activity' cleanup='OwnerOnly'.";
        }

        private static string Case02RouteNormalExit()
        {
            Require(QaCameraOverrideAuthorityFixture.Adr004BRouteLifecycleExecuted,
                "Canonical C9R Route lifecycle evidence has not executed in this Play Mode session.");
            Require(QaCameraOverrideAuthorityFixture.Adr004BRouteLifecyclePassed,
                "Canonical Route exit did not release its Camera request while preserving Session-owned state.");
            return "operation='NormalRouteExit' owner='Route' cleanup='OwnerOnly'.";
        }

        private static string Case03SessionDisableCleanup()
        {
            Require(QaCameraOverrideAuthorityFixture.Adr004CSessionDisableExecuted,
                "Canonical C9R Session disable evidence has not executed.");
            Require(QaCameraOverrideAuthorityFixture.Adr004CSessionDisablePassed,
                "Session disable did not release its request or restore the Output Default without silent re-publication.");
            return "operation='DisableSessionBinding' owner='Session' request='Released' restored='OutputDefault' reenable='ExplicitOnly'.";
        }

        private static string Case04RouteDisableCleanup()
        {
            Require(QaCameraOverrideAuthorityFixture.Adr004BOwnerLossExecuted,
                "Canonical C9R Route abnormal-disable evidence has not executed.");
            Require(QaCameraOverrideAuthorityFixture.Adr004BOwnerLossInvariantPassed,
                QaCameraOverrideAuthorityFixture.Adr004BOwnerLossDiagnostic);
            return "operation='DisableRouteBinding' owner='Route' orphan='False'.";
        }

        private static string Case05ActivityDisableCleanup()
        {
            Require(QaCameraOverrideAuthorityFixture.Adr004CActivityDisableExecuted,
                "Canonical C9R Activity abnormal-disable evidence has not executed.");
            Require(QaCameraOverrideAuthorityFixture.Adr004CActivityDisablePassed,
                "Activity disable did not release its request while preserving logical Activity ownership for explicit re-publication.");
            return "operation='DisableActivityBinding' owner='Activity' request='Released' logicalOwner='Preserved'.";
        }

        private static string Case06ActivityDestructionCleanup()
        {
            Require(QaCameraOverrideAuthorityFixture.Adr004CActivityDestroyExecuted,
                "Canonical C9R Activity destruction evidence has not executed.");
            Require(QaCameraOverrideAuthorityFixture.Adr004CActivityDestroyPassed,
                "Destroying an active ActivityCameraOverride left its request admitted or failed to restore the next winner.");
            return "operation='DestroyActivityBinding' owner='Activity' request='Released' inheritedScopedLifetime='Verified'.";
        }

        private static string Case07NonWinnerOwnerOnly()
        {
            Require(QaCameraOverrideAuthorityFixture.Adr004CNonWinnerDisableExecuted,
                "Canonical C9R non-winner disable evidence has not executed.");
            Require(QaCameraOverrideAuthorityFixture.Adr004CNonWinnerDisablePassed,
                "Disabling a non-winning Activity owner removed or disturbed another owner's Route request.");
            return "operation='DisableNonWinnerActivity' cleanup='OwnerOnly' winner='RoutePreserved'.";
        }

        private static string Case08WinningOwnerRestoresNext()
        {
            Require(QaCameraOverrideAuthorityFixture.Adr004CWinningRestoreExecuted,
                "Canonical C9R winning-owner loss evidence has not executed.");
            Require(QaCameraOverrideAuthorityFixture.Adr004CWinningRestorePassed,
                "Removing the winning Activity owner did not restore the explicit Output Default.");
            return "operation='DisableWinningActivity' resultingPresentation='OutputDefault'.";
        }

        private static string Case09CleanupIdempotent()
        {
            Require(QaCameraOverrideAuthorityFixture.Adr004CIdempotentCleanupExecuted,
                "Canonical C9R idempotent cleanup evidence has not executed.");
            Require(QaCameraOverrideAuthorityFixture.Adr004CIdempotentCleanupPassed,
                "Repeated cleanup after binding disable was not preserved/idempotent.");
            return "operation='DisableThenExplicitRelease' repeatedCleanup='Preserved'.";
        }

        private static string Case10ReenableNoSilentRepublish()
        {
            Require(QaCameraOverrideAuthorityFixture.Adr004CRouteReenableExecuted,
                "Canonical C9R Route re-enable evidence has not executed.");
            Require(QaCameraOverrideAuthorityFixture.Adr004CRouteReenablePassed,
                "Route binding re-enable silently re-published or lost its still-valid logical owner state.");
            Require(QaCameraOverrideAuthorityFixture.Adr004CActivityDisablePassed,
                "Activity re-enable boundary did not preserve explicit-only publication semantics.");
            Require(QaCameraOverrideAuthorityFixture.Adr004CSessionDisablePassed,
                "Session re-enable boundary did not preserve explicit-only publication semantics.");
            return "operation='ReenableBindings' publication='ExplicitOnly' routeOwner='StillValid' sessionOwner='Reactivated'.";
        }

        private static void Execute(
            ICollection<CaseResult> results,
            string id,
            Func<string> body)
        {
            try
            {
                string evidence = body();
                results.Add(new CaseResult(id, true));
                Debug.Log($"{LogPrefix} case='{id}' status='Passed' {evidence}");
            }
            catch (Exception exception)
            {
                results.Add(new CaseResult(id, false));
                Debug.LogError(
                    $"{LogPrefix} case='{id}' status='Failed' " +
                    $"diagnostic='{Escape(exception.GetBaseException().Message)}'.");
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static string Escape(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("'", "\\'")
                .Replace("\r", " ")
                .Replace("\n", " ");
        }

        private readonly struct CaseResult
        {
            public CaseResult(string id, bool passed)
            {
                Id = id;
                Passed = passed;
            }

            public string Id { get; }
            public bool Passed { get; }
        }
    }
}
