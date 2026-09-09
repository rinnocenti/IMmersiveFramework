using System;
using System.Collections.Generic;
using System.Linq;
using Immersive.Framework.Camera;
using Immersive.Framework.CameraAuthoring;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;

namespace ImmersiveFrameworkQA.Camera.Editor
{
    /// <summary>
    /// IF-ADR-004B negative/transactional integrity certification.
    /// Cases 14-16 consume evidence produced by the canonical C9R lifecycle fixture
    /// in the same Play Mode session. Cases 17-18 delegate to the existing canonical
    /// authoring regressions rather than reproducing their internal validation logic.
    /// </summary>
    internal static class QaCameraAdr004BNegativeIntegrityRegression
    {
        private const string MenuPath =
            "Immersive Framework/QA/Regressions/Camera/Run ADR-004B Negative Integrity Certification";
        private const string LogPrefix = "[QA_CAMERA_ADR004B]";
        private const int ExpectedCaseCount = 18;

        [MenuItem(MenuPath, true)]
        private static bool ValidateRun() => EditorApplication.isPlaying;

        [MenuItem(MenuPath, priority = 237)]
        private static void Run()
        {
            RunCertification();
        }

        internal static bool RunCertification()
        {
            var results = new List<CaseResult>(ExpectedCaseCount);

            Execute(results, "01-higher-precedence", Case01HigherPrecedence);
            Execute(results, "02-equal-precedence-deterministic", Case02EqualPrecedenceDeterministic);
            Execute(results, "03-equal-precedence-missing-tie", Case03MissingTieBreaker);
            Execute(results, "04-equal-precedence-duplicate-tie", Case04DuplicateTieBreaker);
            Execute(results, "05-duplicate-request-id", Case05DuplicateRequestId);
            Execute(results, "06-wrong-output-id", Case06WrongOutputId);
            Execute(results, "07-repeated-publish", Case07RepeatedPublish);
            Execute(results, "08-repeated-release", Case08RepeatedRelease);
            Execute(results, "09-release-current-winner", Case09ReleaseCurrentWinner);
            Execute(results, "10-out-of-order-release", Case10OutOfOrderRelease);
            Execute(results, "11-admission-apply-failure-rollback", Case11AdmissionApplyFailureRollback);
            Execute(results, "12-release-replacement-failure-rollback", Case12ReleaseReplacementFailureRollback);
            Execute(results, "13-rollback-failure-explicit", Case13RollbackFailureExplicit);
            Execute(results, "14-activity-lifecycle-exit", Case14ActivityLifecycleExit);
            Execute(results, "15-route-lifecycle-exit", Case15RouteLifecycleExit);
            Execute(results, "16-abnormal-owner-loss", Case16AbnormalOwnerLoss);
            Execute(results, "17-duplicate-output-id", Case17DuplicatePersistentOutput);
            Execute(results, "18-invalid-output-binding-references", Case18InvalidOutputBindingReferences);

            Require(
                results.Count == ExpectedCaseCount,
                $"ADR-004B case count changed. expected='{ExpectedCaseCount}' actual='{results.Count}'.");

            int passed = results.Count(item => item.Status == CaseStatus.Passed);
            int failed = results.Count(item => item.Status == CaseStatus.Failed);
            int blocked = results.Count(item => item.Status == CaseStatus.Blocked);

            if (failed == 0 && blocked == 0 && passed == ExpectedCaseCount)
            {
                Debug.Log(
                    $"{LogPrefix} status='Passed' cases='{passed}/{ExpectedCaseCount}' " +
                    "failed='0' blocked='0' " +
                    "verdict='ADR-004B CAMERA NEGATIVE INTEGRITY CERTIFIED'.");
                return true;
            }

            string status = failed > 0 ? "Failed" : "Blocked";
            string verdict =
                results.Any(item =>
                    item.Id == "16-abnormal-owner-loss" &&
                    item.Status == CaseStatus.Failed)
                    ? "ADR-004B NOT CERTIFIED — OWNER LOSS ORPHAN REPRODUCED; OPEN IF-ADR-004C"
                    : blocked > 0
                        ? "ADR-004B NOT CERTIFIED — REQUIRED CANONICAL EVIDENCE NOT EXECUTED"
                        : "ADR-004B NOT CERTIFIED — NEGATIVE INTEGRITY FAILURE";

            Debug.LogError(
                $"{LogPrefix} status='{status}' cases='{passed}/{ExpectedCaseCount}' " +
                $"failed='{failed}' blocked='{blocked}' " +
                $"verdict='{verdict}'.");
            return false;
        }

        private static string Case01HigherPrecedence()
        {
            using var fixture = new SyntheticFixture("case01");
            RigHandle low = fixture.CreateRig("low", true);
            RigHandle high = fixture.CreateRig("high", true);
            ICameraRequestPublisher lowPublisher = fixture.CreatePublisher(
                fixture.CreateRequest("case01.low", low.Composer, 50, "low"));
            ICameraRequestPublisher highPublisher = fixture.CreatePublisher(
                fixture.CreateRequest("case01.high", high.Composer, 300, "high"));

            CameraRequestPublisherResult lowResult = lowPublisher.Publish();
            CameraRequestPublisherResult highResult = highPublisher.Publish();

            Require(lowResult.Succeeded && highResult.Succeeded,
                "Higher-precedence setup publication failed.");
            fixture.RequireWinner("case01.high", high.Camera, 2);

            return fixture.Evidence(
                "Admit",
                "case01.high",
                300,
                "high",
                "case01.low",
                "case01.high",
                highResult,
                false);
        }

        private static string Case02EqualPrecedenceDeterministic()
        {
            string forwardWinner;
            string reverseWinner;

            using (var forward = new SyntheticFixture("case02.forward"))
            {
                RigHandle a = forward.CreateRig("a", true);
                RigHandle b = forward.CreateRig("b", true);
                ICameraRequestPublisher bPublisher = forward.CreatePublisher(
                    forward.CreateRequest("case02.b", b.Composer, 100, "b"));
                ICameraRequestPublisher aPublisher = forward.CreatePublisher(
                    forward.CreateRequest("case02.a", a.Composer, 100, "a"));
                Require(bPublisher.Publish().Succeeded && aPublisher.Publish().Succeeded,
                    "Forward equal-precedence publication failed.");
                forward.RequireWinner("case02.a", a.Camera, 2);
                forwardWinner = forward.WinnerId;
            }

            using (var reverse = new SyntheticFixture("case02.reverse"))
            {
                RigHandle a = reverse.CreateRig("a", true);
                RigHandle b = reverse.CreateRig("b", true);
                ICameraRequestPublisher aPublisher = reverse.CreatePublisher(
                    reverse.CreateRequest("case02.a", a.Composer, 100, "a"));
                ICameraRequestPublisher bPublisher = reverse.CreatePublisher(
                    reverse.CreateRequest("case02.b", b.Composer, 100, "b"));
                Require(aPublisher.Publish().Succeeded && bPublisher.Publish().Succeeded,
                    "Reverse equal-precedence publication failed.");
                reverse.RequireWinner("case02.a", a.Camera, 2);
                reverseWinner = reverse.WinnerId;
            }

            Require(
                forwardWinner == reverseWinner && forwardWinner == "case02.a",
                "Equal-precedence winner depended on publication timing.");

            return
                "operation='AdmitBothOrders' precedence='100' tieA='a' tieB='b' " +
                $"forwardWinner='{forwardWinner}' reverseWinner='{reverseWinner}' deterministic='True'.";
        }

        private static string Case03MissingTieBreaker()
        {
            using var fixture = new SyntheticFixture("case03");
            RigHandle baseline = fixture.CreateRig("baseline", true);
            RigHandle conflicting = fixture.CreateRig("missing-tie", true);
            ICameraRequestPublisher baselinePublisher = fixture.CreatePublisher(
                fixture.CreateRequest("case03.baseline", baseline.Composer, 100, "a"));
            Require(baselinePublisher.Publish().Succeeded,
                "Missing tie-breaker baseline publication failed.");

            ICameraRequestPublisher conflictingPublisher = fixture.CreatePublisher(
                fixture.CreateRequest("case03.conflict", conflicting.Composer, 100, string.Empty));
            CameraRequestPublisherResult result = conflictingPublisher.Publish();

            Require(result.IsRejected,
                "Equal-precedence request without a tie-breaker was not rejected.");
            Require(HasIssue(result.Issues, "camera.output-context.tie-breaker.missing"),
                "Missing tie-breaker rejection did not expose the canonical issue code.");
            fixture.RequireWinner("case03.baseline", baseline.Camera, 1);
            Require(!fixture.Contains("case03.conflict"),
                "Rejected missing-tie request mutated admitted state.");

            return fixture.Evidence(
                "AdmitRejected",
                "case03.conflict",
                100,
                "<missing>",
                "case03.baseline",
                "case03.baseline",
                result,
                false);
        }

        private static string Case04DuplicateTieBreaker()
        {
            using var fixture = new SyntheticFixture("case04");
            RigHandle baseline = fixture.CreateRig("baseline", true);
            RigHandle conflicting = fixture.CreateRig("duplicate-tie", true);
            ICameraRequestPublisher baselinePublisher = fixture.CreatePublisher(
                fixture.CreateRequest("case04.baseline", baseline.Composer, 100, "same"));
            Require(baselinePublisher.Publish().Succeeded,
                "Duplicate tie-breaker baseline publication failed.");

            ICameraRequestPublisher conflictingPublisher = fixture.CreatePublisher(
                fixture.CreateRequest("case04.conflict", conflicting.Composer, 100, "same"));
            CameraRequestPublisherResult result = conflictingPublisher.Publish();

            Require(result.IsRejected,
                "Equal-precedence request with a duplicate tie-breaker was not rejected.");
            Require(HasIssue(result.Issues, "camera.output-context.tie-breaker.duplicate"),
                "Duplicate tie-breaker rejection did not expose the canonical issue code.");
            fixture.RequireWinner("case04.baseline", baseline.Camera, 1);
            Require(!fixture.Contains("case04.conflict"),
                "Rejected duplicate-tie request mutated admitted state.");

            return fixture.Evidence(
                "AdmitRejected",
                "case04.conflict",
                100,
                "same",
                "case04.baseline",
                "case04.baseline",
                result,
                false);
        }

        private static string Case05DuplicateRequestId()
        {
            using var fixture = new SyntheticFixture("case05");
            RigHandle baseline = fixture.CreateRig("baseline", true);
            RigHandle replacement = fixture.CreateRig("replacement", true);
            ICameraRequestPublisher first = fixture.CreatePublisher(
                fixture.CreateRequest("case05.duplicate", baseline.Composer, 100, "a"));
            Require(first.Publish().Succeeded,
                "Duplicate RequestId baseline publication failed.");

            ICameraRequestPublisher duplicate = fixture.CreatePublisher(
                fixture.CreateRequest("case05.duplicate", replacement.Composer, 300, "b"));
            CameraRequestPublisherResult result = duplicate.Publish();

            Require(result.IsRejected,
                "Duplicate RequestId admission was not rejected.");
            Require(HasIssue(result.Issues, "camera.output-context.request-duplicate"),
                "Duplicate RequestId rejection did not expose the canonical issue code.");
            fixture.RequireWinner("case05.duplicate", baseline.Camera, 1);

            return fixture.Evidence(
                "AdmitRejected",
                "case05.duplicate",
                300,
                "b",
                "case05.duplicate",
                "case05.duplicate",
                result,
                false);
        }

        private static string Case06WrongOutputId()
        {
            using var fixture = new SyntheticFixture("case06");
            RigHandle baseline = fixture.CreateRig("baseline", true);
            RigHandle wrong = fixture.CreateRig("wrong-output", true);
            ICameraRequestPublisher baselinePublisher = fixture.CreatePublisher(
                fixture.CreateRequest("case06.baseline", baseline.Composer, 100, "a"));
            Require(baselinePublisher.Publish().Succeeded,
                "Wrong OutputId baseline publication failed.");

            CameraRequest wrongRequest = fixture.CreateRequest(
                "case06.wrong",
                wrong.Composer,
                300,
                "wrong",
                "qa.camera.adr004b.other-output");
            CameraRequestPublisherCreateResult creation =
                SessionCameraRequestPublisher.Create(fixture.Session, wrongRequest);

            Require(creation.IsBlocked,
                "Wrong OutputId publisher creation was not blocked.");
            Require(HasIssue(creation.Issues, "camera.request-publisher.output-mismatch"),
                "Wrong OutputId blocking did not expose the canonical issue code.");
            fixture.RequireWinner("case06.baseline", baseline.Camera, 1);

            return
                $"operation='PublisherCreateBlocked' request='case06.wrong' output='{wrongRequest.OutputId}' " +
                $"expectedOutput='{fixture.OutputId}' previousWinner='case06.baseline' resultingWinner='{fixture.WinnerId}' " +
                $"issue='{FirstIssue(creation.Issues)}' admitted='{fixture.AdmittedIds}'.";
        }

        private static string Case07RepeatedPublish()
        {
            using var fixture = new SyntheticFixture("case07");
            RigHandle rig = fixture.CreateRig("published", true);
            ICameraRequestPublisher publisher = fixture.CreatePublisher(
                fixture.CreateRequest("case07.request", rig.Composer, 100, "publish"));

            CameraRequestPublisherResult first = publisher.Publish();
            CameraRequestPublisherResult second = publisher.Publish();

            Require(first.OperationKind == CameraRequestPublisherOperationKind.Published,
                "First Publish did not publish.");
            Require(second.OperationKind == CameraRequestPublisherOperationKind.Preserved,
                "Repeated Publish was not preserved.");
            Require(!second.HasSessionResult,
                "Repeated Publish reached the output session unexpectedly.");
            fixture.RequireWinner("case07.request", rig.Camera, 1);

            return fixture.Evidence(
                "PublishPreserved",
                "case07.request",
                100,
                "publish",
                "case07.request",
                "case07.request",
                second,
                false);
        }

        private static string Case08RepeatedRelease()
        {
            using var fixture = new SyntheticFixture("case08");
            RigHandle rig = fixture.CreateRig("released", true);
            ICameraRequestPublisher publisher = fixture.CreatePublisher(
                fixture.CreateRequest("case08.request", rig.Composer, 100, "release"));
            Require(publisher.Publish().Succeeded,
                "Repeated Release setup publication failed.");

            CameraRequestPublisherResult first = publisher.Release();
            CameraRequestPublisherResult second = publisher.Release();

            Require(first.OperationKind == CameraRequestPublisherOperationKind.Released,
                "First Release did not release.");
            Require(second.OperationKind == CameraRequestPublisherOperationKind.Preserved,
                "Repeated Release was not preserved.");
            Require(!second.HasSessionResult,
                "Repeated Release reached the output session unexpectedly.");
            Require(fixture.Context.AdmittedRequestCount == 0 && !fixture.Context.HasWinner,
                "Repeated Release mutated the already-released context.");
            Require(!fixture.Applicator.HasAppliedRequest,
                "Repeated Release left a physical camera applied.");

            return fixture.Evidence(
                "ReleasePreserved",
                "case08.request",
                100,
                "release",
                "<none>",
                "<none>",
                second,
                false);
        }

        private static string Case09ReleaseCurrentWinner()
        {
            using var fixture = new SyntheticFixture("case09");
            RigHandle low = fixture.CreateRig("low", true);
            RigHandle high = fixture.CreateRig("high", true);
            ICameraRequestPublisher lowPublisher = fixture.CreatePublisher(
                fixture.CreateRequest("case09.low", low.Composer, 100, "low"));
            ICameraRequestPublisher highPublisher = fixture.CreatePublisher(
                fixture.CreateRequest("case09.high", high.Composer, 300, "high"));
            Require(lowPublisher.Publish().Succeeded && highPublisher.Publish().Succeeded,
                "Release winner setup publication failed.");
            fixture.RequireWinner("case09.high", high.Camera, 2);

            CameraRequestPublisherResult release = highPublisher.Release();

            Require(release.OperationKind == CameraRequestPublisherOperationKind.Released,
                "Current winner release failed.");
            fixture.RequireWinner("case09.low", low.Camera, 1);

            return fixture.Evidence(
                "ReleaseWinner",
                "case09.high",
                300,
                "high",
                "case09.high",
                "case09.low",
                release,
                false);
        }

        private static string Case10OutOfOrderRelease()
        {
            using var fixture = new SyntheticFixture("case10");
            RigHandle high = fixture.CreateRig("high", true);
            RigHandle low = fixture.CreateRig("low", true);
            ICameraRequestPublisher highPublisher = fixture.CreatePublisher(
                fixture.CreateRequest("case10.high", high.Composer, 300, "high"));
            ICameraRequestPublisher lowPublisher = fixture.CreatePublisher(
                fixture.CreateRequest("case10.low", low.Composer, 100, "low"));
            Require(highPublisher.Publish().Succeeded && lowPublisher.Publish().Succeeded,
                "Out-of-order release setup publication failed.");
            fixture.RequireWinner("case10.high", high.Camera, 2);

            CameraRequestPublisherResult release = lowPublisher.Release();

            Require(release.OperationKind == CameraRequestPublisherOperationKind.Released,
                "Non-winning request release failed.");
            fixture.RequireWinner("case10.high", high.Camera, 1);

            return fixture.Evidence(
                "ReleaseNonWinner",
                "case10.low",
                100,
                "low",
                "case10.high",
                "case10.high",
                release,
                false);
        }

        private static string Case11AdmissionApplyFailureRollback()
        {
            using var fixture = new SyntheticFixture("case11");
            RigHandle baseline = fixture.CreateRig("baseline", true);
            RigHandle invalid = fixture.CreateRig("invalid", false);
            ICameraRequestPublisher baselinePublisher = fixture.CreatePublisher(
                fixture.CreateRequest("case11.baseline", baseline.Composer, 100, "baseline"));
            Require(baselinePublisher.Publish().Succeeded,
                "Admission rollback baseline publication failed.");
            fixture.RequireWinner("case11.baseline", baseline.Camera, 1);

            ICameraRequestPublisher invalidPublisher = fixture.CreatePublisher(
                fixture.CreateRequest("case11.invalid", invalid.Composer, 300, "invalid"));
            CameraRequestPublisherResult result = invalidPublisher.Publish();

            Require(result.IsRejected && result.HasSessionResult,
                "Invalid winning rig admission was not rejected by the session.");
            Require(result.SessionResult.WasRolledBack,
                "Admission physical-apply failure did not report RolledBack.");
            Require(
                result.SessionResult.HasRollbackContextResult &&
                result.SessionResult.HasRollbackApplyResult &&
                result.SessionResult.RollbackContextResult.Succeeded &&
                result.SessionResult.RollbackApplyResult.Succeeded,
                "Admission rollback evidence is incomplete.");
            Require(HasIssue(result.Issues,
                    "camera.output-session.application-failed-rolled-back"),
                "Admission rollback did not expose the canonical rollback issue.");
            Require(!fixture.Contains("case11.invalid"),
                "Admission rollback left the failed request admitted.");
            fixture.RequireWinner("case11.baseline", baseline.Camera, 1);

            return fixture.Evidence(
                "AdmitApplyFailed",
                "case11.invalid",
                300,
                "invalid",
                "case11.baseline",
                "case11.baseline",
                result,
                true);
        }

        private static string Case12ReleaseReplacementFailureRollback()
        {
            using var fixture = new SyntheticFixture("case12");
            RigHandle high = fixture.CreateRig("high", true);
            RigHandle invalidLow = fixture.CreateRig("invalid-low", false);
            ICameraRequestPublisher highPublisher = fixture.CreatePublisher(
                fixture.CreateRequest("case12.high", high.Composer, 300, "high"));
            ICameraRequestPublisher invalidLowPublisher = fixture.CreatePublisher(
                fixture.CreateRequest("case12.invalid-low", invalidLow.Composer, 100, "invalid-low"));

            Require(highPublisher.Publish().Succeeded,
                "Release rollback winning baseline publication failed.");
            Require(invalidLowPublisher.Publish().Succeeded,
                "Invalid non-winning replacement could not be admitted for release rollback proof.");
            fixture.RequireWinner("case12.high", high.Camera, 2);

            CameraRequestPublisherResult release = highPublisher.Release();

            Require(release.IsRejected && release.HasSessionResult,
                "Release replacement apply failure was not rejected.");
            Require(release.SessionResult.WasRolledBack,
                "Release replacement physical-apply failure did not report RolledBack.");
            Require(
                release.SessionResult.HasRollbackContextResult &&
                release.SessionResult.HasRollbackApplyResult &&
                release.SessionResult.RollbackContextResult.Succeeded &&
                release.SessionResult.RollbackApplyResult.Succeeded,
                "Release rollback evidence is incomplete.");
            Require(HasIssue(release.Issues,
                    "camera.output-session.application-failed-rolled-back"),
                "Release rollback did not expose the canonical rollback issue.");
            fixture.RequireWinner("case12.high", high.Camera, 2);

            return fixture.Evidence(
                "ReleaseApplyFailed",
                "case12.high",
                300,
                "high",
                "case12.high",
                "case12.high",
                release,
                true);
        }

        private static string Case13RollbackFailureExplicit()
        {
            using var fixture = new SyntheticFixture("case13");
            RigHandle high = fixture.CreateRig("high", true);
            RigHandle invalidLow = fixture.CreateRig("invalid-low", false);
            ICameraRequestPublisher highPublisher = fixture.CreatePublisher(
                fixture.CreateRequest("case13.high", high.Composer, 300, "high"));
            ICameraRequestPublisher invalidLowPublisher = fixture.CreatePublisher(
                fixture.CreateRequest("case13.invalid-low", invalidLow.Composer, 100, "invalid-low"));

            Require(highPublisher.Publish().Succeeded,
                "Rollback-failure baseline publication failed.");
            Require(invalidLowPublisher.Publish().Succeeded,
                "Rollback-failure invalid non-winner admission failed.");
            fixture.RequireWinner("case13.high", high.Camera, 2);

            UnityEngine.Object.DestroyImmediate(high.Camera.gameObject);
            CameraRequestPublisherResult release = highPublisher.Release();

            Require(release.IsRejected && release.HasSessionResult,
                "Rollback-failure release was not rejected.");
            Require(release.SessionResult.RollbackFailed,
                "Double physical failure was not reported as RollbackFailed.");
            Require(
                release.SessionResult.HasRollbackContextResult &&
                release.SessionResult.HasRollbackApplyResult,
                "RollbackFailed result omitted rollback evidence.");
            Require(HasIssue(release.Issues,
                    "camera.output-session.rollback-failed"),
                "RollbackFailed result omitted the canonical blocking issue.");
            Require(fixture.Contains("case13.high") && fixture.Contains("case13.invalid-low"),
                "RollbackFailed logical evidence did not retain both admitted requests for diagnosis.");
            Require(fixture.WinnerId == "case13.high",
                "RollbackFailed logical winner was not restored even though physical restoration failed.");

            return fixture.Evidence(
                "ReleaseRollbackFailed",
                "case13.high",
                300,
                "high",
                "case13.high",
                fixture.WinnerId,
                release,
                true);
        }

        private static string Case14ActivityLifecycleExit()
        {
            if (!QaCameraOverrideAuthorityFixture.Adr004BActivityLifecycleExecuted)
            {
                throw new PrerequisiteException(
                    "Canonical C9R Activity lifecycle evidence has not executed in this Play Mode session.");
            }

            Require(QaCameraOverrideAuthorityFixture.Adr004BActivityLifecyclePassed,
                "Canonical C9R Activity lifecycle cleanup did not preserve/restore the valid lower-precedence request.");

            return
                "operation='CanonicalC9RActivityExit' owner='Activity' lifetime='Activity' " +
                "resultingWinner='qa.camera.request.player' delegated='C9R' cleanup='OwnerOnly'.";
        }

        private static string Case15RouteLifecycleExit()
        {
            if (!QaCameraOverrideAuthorityFixture.Adr004BRouteLifecycleExecuted)
            {
                throw new PrerequisiteException(
                    "Canonical C9R Route lifecycle evidence has not executed in this Play Mode session.");
            }

            Require(QaCameraOverrideAuthorityFixture.Adr004BRouteLifecyclePassed,
                "Canonical C9R Route lifecycle cleanup did not remove only the Route request while preserving the Session survivor.");

            return
                "operation='CanonicalC9RRouteExit' owner='Route' lifetime='Route' " +
                "routeRequest='Released' sessionSurvivor='Preserved' " +
                "winner='ArbitrationOwned' delegated='C9R' cleanup='OwnerOnly'.";
        }

        private static string Case16AbnormalOwnerLoss()
        {
            if (!QaCameraOverrideAuthorityFixture.Adr004BOwnerLossExecuted)
            {
                throw new PrerequisiteException(
                    "Canonical C9R abnormal owner-loss probe has not executed in this Play Mode session.");
            }

            Require(
                QaCameraOverrideAuthorityFixture.Adr004BOwnerLossInvariantPassed,
                QaCameraOverrideAuthorityFixture.Adr004BOwnerLossDiagnostic);

            return
                "operation='DisableRouteOwner' owner='Route' lifetime='Route' orphan='False' " +
                $"diagnostic='{Escape(QaCameraOverrideAuthorityFixture.Adr004BOwnerLossDiagnostic)}'.";
        }

        private static string Case17DuplicatePersistentOutput()
        {
            IReadOnlyList<string> evidence =
                QaPersistentCameraPresentationCompositionRegression.RunAdr004BDuplicateOutputCertification();
            Require(evidence != null && evidence.Contains("duplicate-output-id"),
                "Canonical Persistent Camera composition regression did not execute the duplicate Output ID blocking case.");

            return
                "operation='OutputTopologyValidation' outputs='2' duplicateId='True' expected='Blocked' " +
                "delegated='QaPersistentCameraPresentationCompositionRegression' diagnostic='Actionable'.";
        }

        private static string Case18InvalidOutputBindingReferences()
        {
            IReadOnlyList<string> evidence =
                QaCameraOutputAuthoringAuthoringRegression.RunAdr004BInvalidReferenceCertification();
            Require(
                evidence != null &&
                evidence.Contains("missing-camera") &&
                evidence.Contains("missing-brain") &&
                evidence.Contains("split-camera-brain"),
                "Canonical Camera Output authoring regression did not execute all invalid-reference cases.");

            return
                "operation='OutputAuthoringValidation' missingCamera='Blocked' missingBrain='Blocked' " +
                "splitCameraBrain='Blocked' delegated='QaCameraOutputAuthoringAuthoringRegression' " +
                "diagnostic='Actionable' fallbackLookup='None'.";
        }

        private static void Execute(
            ICollection<CaseResult> results,
            string id,
            Func<string> test)
        {
            try
            {
                string evidence = test() ?? string.Empty;
                var result = new CaseResult(id, CaseStatus.Passed, evidence);
                results.Add(result);
                Debug.Log(
                    $"{LogPrefix} case='{id}' status='Passed' {evidence}");
            }
            catch (PrerequisiteException exception)
            {
                string evidence = Escape(exception.Message);
                results.Add(new CaseResult(id, CaseStatus.Blocked, evidence));
                Debug.LogWarning(
                    $"{LogPrefix} case='{id}' status='Blocked' diagnostic='{evidence}'.");
            }
            catch (Exception exception)
            {
                string evidence = Escape(exception.GetBaseException().Message);
                results.Add(new CaseResult(id, CaseStatus.Failed, evidence));
                Debug.LogError(
                    $"{LogPrefix} case='{id}' status='Failed' diagnostic='{evidence}'.");
            }
        }

        private static bool HasIssue(CameraIssue[] issues, string code)
        {
            if (issues == null)
            {
                return false;
            }

            for (int index = 0; index < issues.Length; index++)
            {
                if (string.Equals(issues[index].Code, code, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string FirstIssue(CameraIssue[] issues)
        {
            if (issues == null || issues.Length == 0)
            {
                return "<none>";
            }

            return $"{issues[0].Code}:{Escape(issues[0].Message)}";
        }

        private static string Escape(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("'", "\\'")
                .Replace("\r", " ")
                .Replace("\n", " ");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private enum CaseStatus
        {
            Passed,
            Failed,
            Blocked
        }

        private readonly struct CaseResult
        {
            public CaseResult(string id, CaseStatus status, string evidence)
            {
                Id = id;
                Status = status;
                Evidence = evidence ?? string.Empty;
            }

            public string Id { get; }
            public CaseStatus Status { get; }
            public string Evidence { get; }
        }

        private sealed class PrerequisiteException : Exception
        {
            public PrerequisiteException(string message)
                : base(message)
            {
            }
        }

        private readonly struct RigHandle
        {
            public RigHandle(CameraRigComposer composer, CinemachineCamera camera)
            {
                Composer = composer;
                Camera = camera;
            }

            public CameraRigComposer Composer { get; }
            public CinemachineCamera Camera { get; }
        }

        private sealed class SyntheticFixture : IDisposable
        {
            private readonly GameObject root;
            private readonly Transform target;
            private readonly CameraOutputAuthoring binding;

            public SyntheticFixture(string caseId)
            {
                root = new GameObject($"QA_ADR004B_{caseId}");
                root.SetActive(false);

                UnityEngine.Camera unityCamera =
                    root.AddComponent<UnityEngine.Camera>();
                unityCamera.enabled = false;
                CinemachineBrain brain = root.AddComponent<CinemachineBrain>();

                binding = root.AddComponent<CameraOutputAuthoring>();
                Set(binding, "outputId", $"qa.camera.adr004b.{caseId}.output");
                Set(binding, "unityCamera", unityCamera);
                Set(binding, "cinemachineBrain", brain);
                Set(binding, "initializeOnAwake", false);
                Set(binding, "logDiagnostics", false);

                var defaultRigRoot = new GameObject("Rig_default");
                defaultRigRoot.transform.SetParent(root.transform, false);
                CameraRigComposer defaultComposer =
                    defaultRigRoot.AddComponent<CameraRigComposer>();
                var defaultCameraObject = new GameObject("Cinemachine_default");
                defaultCameraObject.transform.SetParent(defaultRigRoot.transform, false);
                CinemachineCamera defaultCamera =
                    defaultCameraObject.AddComponent<CinemachineCamera>();
                defaultCamera.enabled = false;
                defaultComposer.EditorSetGeneratedReference(defaultCamera);
                Set(binding, "defaultCameraRig", defaultComposer);

                var targetObject = new GameObject("Target");
                targetObject.transform.SetParent(root.transform, false);
                target = targetObject.transform;

                Require(binding.TryGetSession(out CameraOutputSession session, out string diagnostic),
                    $"Synthetic Camera output did not initialize. {diagnostic}");
                Session = session;
            }

            public CameraOutputSession Session { get; }
            public CameraOutputContext Context => binding.Context;
            public CameraOutputRigApplicator Applicator => binding.Applicator;
            public string OutputId => binding.OutputIdText;
            public string WinnerId =>
                Context != null && Context.HasWinner
                    ? Context.Winner.RequestId.Value
                    : "<none>";
            public string AdmittedIds =>
                Context == null
                    ? "<missing-context>"
                    : string.Join(",",
                        Context.CaptureSnapshot()
                            .AdmittedRequestIds
                            .Select(item => item.Value));

            public RigHandle CreateRig(string label, bool materializeCamera)
            {
                var rigRoot = new GameObject($"Rig_{label}");
                rigRoot.transform.SetParent(root.transform, false);
                CameraRigComposer composer =
                    rigRoot.AddComponent<CameraRigComposer>();

                if (!materializeCamera)
                {
                    return new RigHandle(composer, null);
                }

                var cameraObject = new GameObject($"Cinemachine_{label}");
                cameraObject.transform.SetParent(rigRoot.transform, false);
                CinemachineCamera camera =
                    cameraObject.AddComponent<CinemachineCamera>();
                camera.enabled = false;
                composer.EditorSetGeneratedReference(camera);
                return new RigHandle(composer, camera);
            }

            public CameraRequest CreateRequest(
                string requestId,
                CameraRigComposer composer,
                int precedence,
                string tieBreaker,
                string outputId = null)
            {
                CameraRequestCreateResult created =
                    CameraRequestCreateResult.Create(
                        new CameraRequestId(requestId),
                        new CameraOutputId(outputId ?? OutputId),
                        new CameraRequestOwner(
                            CameraRequestOwnerKind.Session,
                            $"qa.camera.adr004b.owner.{requestId}"),
                        new CameraRequestLifetime(
                            CameraRequestLifetimeKind.Session,
                            $"qa.camera.adr004b.scope.{requestId}"),
                        CameraRigReference.FromComposer(composer),
                        CameraTargetSourceDescriptor.ExplicitTransform(
                            target,
                            requestId),
                        new CameraRequestPolicy(precedence, tieBreaker),
                        CameraRequestReleaseCondition.ExplicitRelease,
                        nameof(QaCameraAdr004BNegativeIntegrityRegression),
                        $"ADR-004B synthetic request '{requestId}'.");

                Require(created.IsSucceeded,
                    $"Camera request '{requestId}' could not be created. {created.BlockingIssue}");
                return created.Request;
            }

            public ICameraRequestPublisher CreatePublisher(CameraRequest request)
            {
                CameraRequestPublisherCreateResult created =
                    SessionCameraRequestPublisher.Create(Session, request);
                Require(created.Succeeded && created.Publisher != null,
                    $"Publisher creation failed for request '{request.RequestId}'. {created.DiagnosticSummary}");
                return created.Publisher;
            }

            public bool Contains(string requestId)
            {
                return Context != null &&
                    Context.Contains(new CameraRequestId(requestId));
            }

            public void RequireWinner(
                string requestId,
                CinemachineCamera camera,
                int admittedCount)
            {
                Require(Context != null && Context.HasWinner,
                    $"Expected winner '{requestId}', but context has no winner.");
                Require(Context.Winner.RequestId.Value == requestId,
                    $"Expected winner '{requestId}', found '{WinnerId}'.");
                Require(Context.AdmittedRequestCount == admittedCount,
                    $"Expected '{admittedCount}' admitted request(s), found '{Context.AdmittedRequestCount}'.");
                Require(
                    Applicator != null &&
                    Applicator.HasAppliedRequest &&
                    Applicator.AppliedRequestId.Value == requestId &&
                    Applicator.AppliedCamera == camera &&
                    camera != null &&
                    camera.enabled,
                    $"Physical output is not synchronized to winner '{requestId}'.");
            }

            public string Evidence(
                string operation,
                string requestId,
                int precedence,
                string tieBreaker,
                string previousWinner,
                string resultingWinner,
                CameraRequestPublisherResult result,
                bool rollbackExpected)
            {
                string contextOperation = result.HasSessionResult
                    ? result.SessionResult.ContextResult.OperationKind.ToString()
                    : "NotInvoked";
                string physicalApply =
                    result.HasSessionResult && result.SessionResult.HasApplyResult
                        ? result.SessionResult.ApplyResult.Kind.ToString()
                        : "NotInvoked";
                string rollbackResult =
                    result.HasSessionResult && result.SessionResult.HasRollbackApplyResult
                        ? result.SessionResult.RollbackApplyResult.Kind.ToString()
                        : "NotAttempted";

                return
                    $"operation='{operation}' request='{requestId}' owner='Session' lifetime='Session' " +
                    $"output='{OutputId}' precedence='{precedence}' tieBreaker='{tieBreaker}' " +
                    $"previousWinner='{previousWinner}' resultingWinner='{resultingWinner}' " +
                    $"publisherResult='{result.OperationKind}' contextResult='{contextOperation}' " +
                    $"physicalApply='{physicalApply}' rollbackAttempted='{rollbackExpected}' " +
                    $"rollbackResult='{rollbackResult}' issue='{FirstIssue(result.Issues)}' " +
                    $"admittedCount='{(Context == null ? -1 : Context.AdmittedRequestCount)}' admittedIds='{AdmittedIds}'.";
            }

            public void Dispose()
            {
                if (root != null)
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }

            private static void Set(
                UnityEngine.Object targetObject,
                string propertyName,
                object value)
            {
                var serialized = new SerializedObject(targetObject);
                serialized.Update();
                SerializedProperty property =
                    serialized.FindProperty(propertyName)
                    ?? throw new InvalidOperationException(
                        $"Serialized property '{propertyName}' is unavailable on '{targetObject.GetType().Name}'.");

                switch (value)
                {
                    case string text:
                        property.stringValue = text;
                        break;
                    case bool flag:
                        property.boolValue = flag;
                        break;
                    case UnityEngine.Object reference:
                        property.objectReferenceValue = reference;
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"Unsupported serialized value for '{propertyName}'.");
                }

                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }
}
