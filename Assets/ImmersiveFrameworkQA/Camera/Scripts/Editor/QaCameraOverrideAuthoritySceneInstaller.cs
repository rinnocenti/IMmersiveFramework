using System;
using System.Collections.Generic;
using System.IO;
using Immersive.Framework.ActivityFlow;
using Immersive.Framework.Actors;
using Immersive.Framework.Authoring;
using Immersive.Framework.Camera;
using Immersive.Framework.CameraAuthoring;
using Immersive.Framework.ContentFlow;
using Immersive.Framework.Editor.CameraAuthoring;
using Immersive.Framework.GameFlow;
using Immersive.Framework.PlayerParticipation;
using Immersive.Framework.RouteLifecycle;
using ImmersiveFrameworkQA.Hub;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ImmersiveFrameworkQA.Camera.Editor
{
    internal static class QaCameraOverrideAuthoritySceneInstaller
    {
        private const string Root = "Assets/ImmersiveFrameworkQA";
        private const string CameraRoot = Root + "/Camera";
        private const string ScenePath =
            CameraRoot + "/Scenes/QA_PlayerCameraArbitration.unity";
        private const string RoutePath =
            CameraRoot + "/Routes/QA_PlayerCameraArbitrationRoute.asset";
        private const string ActivityPath =
            CameraRoot + "/Activities/QA_PlayerCameraArbitrationActivity.asset";
        private const string HubScenePath =
            Root + "/Hub/Scenes/QA_Hub.unity";
        private const string HubRoutePath =
            Root + "/Hub/Routes/QA_HubRoute.asset";
        private const string ReplacementActorProfilePath =
            CameraRoot + "/Profiles/QA_SharedCameraReplacementActor.asset";
        private const string ReplacementPresentationPath =
            CameraRoot + "/Prefabs/QA_SharedCameraReplacementPresentation.prefab";
        private const string ReplacementActorProfileId =
            "actor-profile.qa.camera.shared-replacement";
        private const string HubDomain = "Camera";
        private const string HubLabel = "Camera Override Authority";
        private const string HubTriggerName =
            "RouteTrigger_Camera__Override_Authority";
        private const string CoordinatorName =
            "QA__RouteCompletionCoordinator";

        internal static void Install(QaCameraAdr026TopologyMode mode)
        {
            RepairAssets();
            RepairScene(mode);
            EnsureBuildScene();
            RepairHub();
        }

        private static void RepairAssets()
        {
            RepairReplacementActor();
            ActivityAsset activity = LoadOrCreate<ActivityAsset>(ActivityPath);
            Set(activity, "activityName",
                "QA C9R Camera Override Authority Activity");
            Set(activity, "activityId",
                "qa.c9r.camera.override.authority.activity");
            Set(activity, "description",
                "ADR-026 shared Player Subject lifecycle plus generic Camera arbitration proof.");
            Set(activity, "playerParticipationProjectionMode",
                (int)ActivityParticipationProjectionMode.AllJoinedSlots);
            Set(activity, "playerParticipationZeroParticipantPolicy",
                (int)ActivityParticipationZeroParticipantPolicy.Allowed);
            Set(activity, "playerParticipationRequirementLevel",
                (int)PlayerParticipationRequirementLevel.GameplayReady);

            RouteAsset route = LoadOrCreate<RouteAsset>(RoutePath);
            Set(route, "routeId",
                "qa.c9r.camera.override.authority.route");
            Set(route, "routeName",
                "QA C9R Camera Override Authority");
            Set(route, "primaryScenePath", ScenePath);
            Set(route, "primarySceneName",
                Path.GetFileNameWithoutExtension(ScenePath));
            Set(route, "startupActivity", activity);
            Set(route, "description",
                "ADR-026 shared/multi-output proof plus Activity, Route and Session Camera arbitration.");

            if (!activity.HasValidActivityId)
            {
                throw new InvalidOperationException(
                    "C9R Camera Activity identity is invalid after setup materialization.");
            }

            if (!route.HasValidRouteId)
            {
                throw new InvalidOperationException(
                    "C9R Camera Route identity is invalid after setup materialization.");
            }
        }

        private static void RepairReplacementActor()
        {
            if (!AssetDatabase.IsValidFolder(CameraRoot + "/Profiles"))
                AssetDatabase.CreateFolder(CameraRoot, "Profiles");
            if (!AssetDatabase.IsValidFolder(CameraRoot + "/Prefabs"))
                AssetDatabase.CreateFolder(CameraRoot, "Prefabs");

            var staging = new GameObject("QA_SharedCameraReplacementPresentation");
            try
            {
                staging.AddComponent<PlayerGameplayInputReader>();
                var observation = new GameObject("Camera Subject");
                observation.transform.SetParent(staging.transform, false);
                observation.transform.localPosition = new Vector3(0f, 1.6f, 0f);
                ActorCameraSubjectAuthoring subject =
                    staging.AddComponent<ActorCameraSubjectAuthoring>();
                Set(subject, "observationTransform", observation.transform);

                GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                visual.name = "Visual";
                visual.transform.SetParent(staging.transform, false);
                visual.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f);
                UnityEngine.Object.DestroyImmediate(visual.GetComponent<Collider>());
                if (PrefabUtility.SaveAsPrefabAsset(staging, ReplacementPresentationPath) == null)
                    throw new InvalidOperationException(
                        "ADR-026 replacement Presentation could not be saved.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(staging);
            }

            GameObject presentation = Require<GameObject>(ReplacementPresentationPath);
            ActorProfile profile = LoadOrCreate<ActorProfile>(ReplacementActorProfilePath);
            profile.name = "QA_SharedCameraReplacementActor";
            Set(profile, "actorProfileId", ReplacementActorProfileId);
            Set(profile, "displayName", "QA Shared Camera Replacement Actor");
            Set(profile, "description", "Dedicated ADR-026 replacement Actor, independent of Player Slot defaults.");
            Set(profile, "actorKind", (int)ActorKind.Player);
            Set(profile, "actorRole", (int)ActorRole.Protagonist);
            Set(profile, "presentationPrefab", presentation);
            AssetDatabase.SaveAssetIfDirty(profile);

            ActorCameraSubjectAuthoring[] subjects =
                presentation.GetComponentsInChildren<ActorCameraSubjectAuthoring>(true);
            if (profile.ActorProfileId != ActorProfileId.From(ReplacementActorProfileId) ||
                presentation.GetComponentsInChildren<PlayerGameplayInputReader>(true).Length != 1 ||
                subjects.Length != 1 || subjects[0].transform != presentation.transform ||
                !subjects[0].TryResolveObservation(presentation.transform, out Transform resolved, out _) ||
                resolved == presentation.transform || !resolved.IsChildOf(presentation.transform))
            {
                throw new InvalidOperationException(
                    "ADR-026 replacement fixture requires its own Actor identity, one gameplay reader and one explicit child Camera Subject.");
            }
        }

        private static void RepairScene(QaCameraAdr026TopologyMode mode)
        {
            Scene scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null
                ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single)
                : EditorSceneManager.NewScene(
                    NewSceneSetup.EmptyScene,
                    NewSceneMode.Single);

            RemoveSupersededRepairRoots(scene);
            RemoveLocalOutputAndLegacyRoots(scene);

            RouteAsset route = Require<RouteAsset>(RoutePath);
            ActivityAsset activity = Require<ActivityAsset>(ActivityPath);
            RouteAsset hubRoute = Require<RouteAsset>(HubRoutePath);
            ActorProfile replacementActorProfile =
                Require<ActorProfile>(ReplacementActorProfilePath);

            Transform routeTarget = Target(
                scene,
                "QA_C9R_RouteTarget",
                new Vector3(0f, 1f, 0f));
            Transform activityTarget = Target(
                scene,
                "QA_C9R_ActivityTarget",
                new Vector3(-2f, 1f, 0f));

            CameraRigComposer routeRig = Composer(
                scene,
                "QA_C9R_RouteRig",
                "Route Cinemachine Camera");
            CameraRigComposer activityRig = Composer(
                scene,
                "QA_C9R_ActivityRig",
                "Activity Cinemachine Camera");

            GameObject routeRoot = RootObject(
                scene,
                "QA_C9R_RouteContent");
            RouteContentContribution routeContent =
                Component<RouteContentContribution>(routeRoot);
            Set(routeContent, "route", route);
            Set(routeContent, "localContentId", "qa.c9r.route-content");
            Set(routeContent, "requiredness",
                (int)FrameworkContentRequiredness.Required);

            RouteCameraOverride routeBinding =
                Component<RouteCameraOverride>(routeRoot);
            Configure(
                routeBinding,
                "assignedRoute",
                route,
                "qa.c9r.route",
                "qa.camera.request.c9r.route",
                routeRig,
                routeTarget,
                200,
                "route");

            RemoveObsoletePlayerCameraRoots(scene);

            GameObject activityRoot = Child(
                routeRoot.transform,
                "QA_C9R_ActivityContent");
            ActivityContentContribution activityContent =
                Component<ActivityContentContribution>(activityRoot);
            Set(activityContent, "activity", activity);
            Set(activityContent, "localContentId", "qa.c9r.activity-content");
            Set(activityContent, "requiredness",
                (int)FrameworkContentRequiredness.Required);
            ActivityVisibilityRule adapter =
                Component<ActivityVisibilityRule>(activityRoot);
            Set(adapter, "activities",
                new UnityEngine.Object[] { activity });
            Set(adapter, "matchMode",
                (int)ActivityVisibilityMatchMode.VisibleWhenAnyListedActivityIsActive);
            Set(adapter, "noActiveActivityPolicy",
                (int)ActivityVisibilityNoActivePolicy.Hidden);
            if (adapter.Activities.Count != 1 ||
                !ReferenceEquals(adapter.Activities[0], activity))
            {
                throw new InvalidOperationException(
                    "C9R Activity visibility adapter did not materialize the canonical single Activity owner.");
            }

            ActivityCameraOverride activityBinding =
                Component<ActivityCameraOverride>(activityRoot);
            Configure(
                activityBinding,
                "assignedActivity",
                activity,
                "qa.c9r.activity",
                "qa.camera.request.c9r.activity",
                activityRig,
                activityTarget,
                100,
                "activity");

            GameObject controls = RootObject(scene, "QA_C9R_Controls");
            ActivityRequestTrigger activityTrigger =
                Component<ActivityRequestTrigger>(controls);
            activityTrigger.TargetActivity = activity;
            Set(activityTrigger, "reason",
                "qa.c9r.activity.lifecycle-cleanup");

            RouteRequestTrigger backTrigger =
                Component<RouteRequestTrigger>(controls);
            backTrigger.TargetRoute = hubRoute;
            Set(backTrigger, "reason",
                "qa.c9r.route.lifecycle-cleanup");

            QaCameraOverrideAuthorityFixture fixture =
                Component<QaCameraOverrideAuthorityFixture>(controls);
            PlayerSessionObserver playerObserver =
                Component<PlayerSessionObserver>(controls);
            Set(playerObserver, "scope",
                (int)LocalPlayerProvisioningConsumerScope.Route);

            QaCameraOutputProbe outputAProbe = OutputProbe(
                controls.transform,
                "QA_ADR026_OutputA_Probe",
                QaCameraPersistentTopologyBuilder.RequireDefinition<CameraOutputDefinition>(
                    QaCameraPersistentTopologyBuilder.OutputAPath));
            QaCameraOutputProbe outputBProbe = OutputProbe(
                controls.transform,
                "QA_ADR026_OutputB_Probe",
                QaCameraPersistentTopologyBuilder.RequireDefinition<CameraOutputDefinition>(
                    QaCameraPersistentTopologyBuilder.OutputBPath));
            QaCameraOutputProbe missingOutputProbe = OutputProbe(
                controls.transform,
                "QA_ADR026_MissingOutput_Probe",
                QaCameraPersistentTopologyBuilder.RequireDefinition<CameraOutputDefinition>(
                    QaCameraPersistentTopologyBuilder.MissingOutputPath));

            Set(fixture, "topologyMode", (int)mode);
            Set(fixture, "routeBinding", routeBinding);
            Set(fixture, "activityBinding", activityBinding);
            Set(fixture, "routeComposer", routeRig);
            Set(fixture, "activityComposer", activityRig);
            Set(fixture, "playerSessionObserver", playerObserver);
            Set(fixture, "replacementActorProfile", replacementActorProfile);
            Set(fixture, "outputAProbe", outputAProbe);
            Set(fixture, "outputBProbe", outputBProbe);
            Set(fixture, "missingOutputProbe", missingOutputProbe);
            Set(fixture, "activityRequestTrigger", activityTrigger);
            Set(fixture, "backToHubTrigger", backTrigger);
            Set(fixture, "throwOnFailure", false);

            ValidateCanonicalSceneComposition(
                scene,
                route,
                activity,
                routeContent,
                activityContent,
                adapter,
                routeBinding,
                activityBinding,
                fixture,
                playerObserver,
                outputAProbe,
                outputBProbe,
                missingOutputProbe);

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new InvalidOperationException(
                    "C9R Camera authority scene could not be saved.");
            }
        }

        private static CameraRigComposer Composer(
            Scene scene,
            string rootName,
            string cameraName)
        {
            GameObject root = RootObject(scene, rootName);
            CameraRigComposer composer = Component<CameraRigComposer>(root);
            CinemachineCamera camera = Component<CinemachineCamera>(
                Child(root.transform, cameraName));
            camera.enabled = false;

            QaCameraPersistentTopologyBuilder.AssignComposerBeforeApplyRebuild(
                composer,
                QaCameraPersistentTopologyBuilder.RequireBehavior<FollowCameraRigBehaviorDefinition>(
                    QaCameraPersistentTopologyBuilder.FollowBehaviorPath),
                camera,
                rootName);

            CameraRigComposerApplyRebuildResult result =
                CameraRigComposerApplyRebuildUtility.ApplyOrRebuild(
                    composer,
                    false,
                    false);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Could not materialize C9R rig '{rootName}'. {result.BlockingIssue}");
            }

            camera.enabled = false;
            return composer;
        }

        private static void Configure(
            ScopedCameraOverride binding,
            string ownerProperty,
            UnityEngine.Object owner,
            string scope,
            string request,
            CameraRigComposer rig,
            Transform target,
            int precedence,
            string tieBreaker)
        {
            Set(binding, "outputDefinition",
                QaCameraPersistentTopologyBuilder.RequireDefinition<CameraOutputDefinition>(
                    QaCameraPersistentTopologyBuilder.OutputAPath));
            Set(binding, ownerProperty, owner);
            Set(binding, "scopeId", scope);
            Set(binding, "requestId", request);
            Set(binding, "rigComposer", rig);
            Set(binding, "targetSource", target);
            Set(binding, "precedence", precedence);
            Set(binding, "tieBreakerId", tieBreaker);
            Set(binding, "logDiagnostics", true);
        }

        private static void RepairHub()
        {
            Scene scene = EditorSceneManager.OpenScene(
                HubScenePath,
                OpenSceneMode.Single);
            QaHubPanel panel = Single<QaHubPanel>(scene);
            GameObject triggerObject =
                Find(scene, HubTriggerName) ??
                Find(scene, "RouteTrigger_Camera_Runtime_Host_Integration_Regression") ??
                Find(scene, "RouteTrigger_Camera_C9L_Player_Arbitration") ??
                new GameObject(HubTriggerName);
            triggerObject.name = HubTriggerName;
            triggerObject.transform.SetParent(panel.transform, false);

            RouteRequestTrigger trigger =
                Component<RouteRequestTrigger>(triggerObject);
            trigger.TargetRoute = Require<RouteAsset>(RoutePath);
            Set(trigger, "reason",
                "qa.hub.route.camera_override_authority");

            GameObject coordinatorObject =
                Find(scene, CoordinatorName) ??
                Find(scene, "QA_C9L_RouteCompletionCoordinator") ??
                new GameObject(CoordinatorName);
            coordinatorObject.name = CoordinatorName;
            coordinatorObject.transform.SetParent(null, false);
            SceneManager.MoveGameObjectToScene(coordinatorObject, scene);
            QaCameraOverrideAuthorityCompletionCoordinator coordinator =
                Component<QaCameraOverrideAuthorityCompletionCoordinator>(
                    coordinatorObject);
            Set(coordinator, "routeTrigger", trigger);

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root == coordinatorObject)
                {
                    continue;
                }

                if (root.name == "QA_C9L_RouteCompletionCoordinator" ||
                    root.name == CoordinatorName)
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }

            var so = new SerializedObject(panel);
            so.Update();
            SerializedProperty entries = Property(so, "entries");
            int retained = -1;
            for (int index = entries.arraySize - 1; index >= 0; index--)
            {
                string label = entries.GetArrayElementAtIndex(index)
                    .FindPropertyRelative("label").stringValue;
                if (label != HubLabel &&
                    label != " Camera Override Authority" &&
                    label != "Camera /  Override Authority" &&
                    label != "C9L Player Camera Arbitration" &&
                    label != "Camera Runtime Host Integration Regression")
                {
                    continue;
                }

                if (retained < 0)
                {
                    retained = index;
                    continue;
                }

                entries.DeleteArrayElementAtIndex(index);
                if (index < retained)
                {
                    retained--;
                }
            }

            if (retained < 0)
            {
                retained = entries.arraySize;
                entries.arraySize++;
            }

            SerializedProperty entry =
                entries.GetArrayElementAtIndex(retained);
            entry.FindPropertyRelative("domain").stringValue = HubDomain;
            entry.FindPropertyRelative("label").stringValue = HubLabel;
            entry.FindPropertyRelative("routeRequestTrigger")
                .objectReferenceValue = trigger;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(panel);

            if (!EditorSceneManager.SaveScene(scene, HubScenePath))
            {
                throw new InvalidOperationException(
                    "QA Hub scene could not be saved after C9R Camera setup.");
            }
        }

        private static void RemoveSupersededRepairRoots(Scene scene)
        {
            string[] supersededRootNames =
            {
                "QA_RouteTarget",
                "QA_PlayerTarget",
                "QA_PlayerLookAt",
                "QA_ActivityTarget",
                "QA_RouteRig",
                "QA_PlayerRig",
                "QA_ActivityRig",
                "QA_RouteContent",
                "QA_LocalPlayer",
                "QA__Controls"
            };

            foreach (string name in supersededRootNames)
            {
                GameObject root = FindRoot(scene, name);
                if (root != null)
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }

            GameObject canonicalRouteRoot =
                FindRoot(scene, "QA_C9R_RouteContent");
            if (canonicalRouteRoot == null)
            {
                return;
            }

            for (int index = canonicalRouteRoot.transform.childCount - 1;
                 index >= 0;
                 index--)
            {
                Transform child = canonicalRouteRoot.transform.GetChild(index);
                if (child.name == "QA__ActivityContent")
                {
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                }
            }
        }

        private static void ValidateCanonicalSceneComposition(
            Scene scene,
            RouteAsset route,
            ActivityAsset activity,
            RouteContentContribution routeContent,
            ActivityContentContribution activityContent,
            ActivityVisibilityRule visibility,
            RouteCameraOverride routeBinding,
            ActivityCameraOverride activityBinding,
            QaCameraOverrideAuthorityFixture fixture,
            PlayerSessionObserver playerObserver,
            QaCameraOutputProbe outputAProbe,
            QaCameraOutputProbe outputBProbe,
            QaCameraOutputProbe missingOutputProbe)
        {
            if (!ReferenceEquals(Single<RouteContentContribution>(scene), routeContent) ||
                !ReferenceEquals(Single<ActivityContentContribution>(scene), activityContent) ||
                !ReferenceEquals(Single<ActivityVisibilityRule>(scene), visibility) ||
                !ReferenceEquals(Single<RouteCameraOverride>(scene), routeBinding) ||
                !ReferenceEquals(Single<ActivityCameraOverride>(scene), activityBinding) ||
                !ReferenceEquals(Single<QaCameraOverrideAuthorityFixture>(scene), fixture) ||
                !ReferenceEquals(Single<PlayerSessionObserver>(scene), playerObserver))
            {
                throw new InvalidOperationException(
                    "C9R Camera scene contains duplicate or unexpected lifecycle fixture owners after repair.");
            }

            if (!ReferenceEquals(routeContent.Route, route) ||
                routeContent.LocalContentIdText != "qa.c9r.route-content" ||
                routeContent.Requiredness != FrameworkContentRequiredness.Required)
            {
                throw new InvalidOperationException(
                    "C9R Route content binding is not materialized with the canonical owner, local id and requiredness.");
            }

            if (!ReferenceEquals(activityContent.Activity, activity) ||
                !activity.HasValidActivityId ||
                !ReferenceEquals(activityContent.Activity, activityBinding.AssignedActivity) ||
                activityContent.gameObject != activityBinding.gameObject ||
                activityContent.gameObject != visibility.gameObject ||
                activityContent.transform.parent != routeContent.transform ||
                !activityContent.TryGetLocalContentId(out _) ||
                activityContent.LocalContentIdText != "qa.c9r.activity-content" ||
                activityContent.Requiredness != FrameworkContentRequiredness.Required)
            {
                throw new InvalidOperationException(
                    "C9R Activity content boundary must contain its Camera override and visibility rule under Route content, with the canonical Activity, explicit local id and Required contribution.");
            }

            if (visibility.Activities.Count != 1 ||
                !ReferenceEquals(visibility.Activities[0], activity) ||
                visibility.MatchMode != ActivityVisibilityMatchMode.VisibleWhenAnyListedActivityIsActive ||
                visibility.NoActiveActivityPolicy != ActivityVisibilityNoActivePolicy.Hidden)
            {
                throw new InvalidOperationException(
                    "C9R Activity visibility binding is not materialized with exactly one canonical Activity owner.");
            }

            if (!ReferenceEquals(routeBinding.AssignedRoute, route) ||
                routeBinding.ScopeId != "qa.c9r.route" ||
                routeBinding.RequestIdText != "qa.camera.request.c9r.route")
            {
                throw new InvalidOperationException(
                    "C9R Route Camera override identity or owner is invalid after repair.");
            }

            if (playerObserver.Scope != LocalPlayerProvisioningConsumerScope.Route ||
                !ReferenceEquals(
                    outputAProbe.OutputDefinition,
                    QaCameraPersistentTopologyBuilder.RequireDefinition<CameraOutputDefinition>(
                        QaCameraPersistentTopologyBuilder.OutputAPath)) ||
                !ReferenceEquals(
                    outputBProbe.OutputDefinition,
                    QaCameraPersistentTopologyBuilder.RequireDefinition<CameraOutputDefinition>(
                        QaCameraPersistentTopologyBuilder.OutputBPath)) ||
                !ReferenceEquals(
                    missingOutputProbe.OutputDefinition,
                    QaCameraPersistentTopologyBuilder.RequireDefinition<CameraOutputDefinition>(
                        QaCameraPersistentTopologyBuilder.MissingOutputPath)) ||
                All<QaCameraOutputProbe>(scene).Count != 3)
            {
                throw new InvalidOperationException(
                    "ADR-026 Player Session observation or exact Output probes are invalid after repair.");
            }

            if (!ReferenceEquals(activityBinding.AssignedActivity, activity) ||
                activityBinding.ScopeId != "qa.c9r.activity" ||
                activityBinding.RequestIdText != "qa.camera.request.c9r.activity")
            {
                throw new InvalidOperationException(
                    "C9R Activity Camera override identity or owner is invalid after repair.");
            }

        }

        private static QaCameraOutputProbe OutputProbe(
            Transform parent,
            string name,
            CameraOutputDefinition outputDefinition)
        {
            QaCameraOutputProbe probe =
                Component<QaCameraOutputProbe>(Child(parent, name));
            Set(probe, "outputDefinition", outputDefinition);
            Set(probe, "output", null);
            Set(probe, "lastDetachReason", string.Empty);
            Set(probe, "attachmentCount", 0);
            return probe;
        }

        private static void RemoveObsoletePlayerCameraRoots(Scene scene)
        {
            string[] names =
            {
                "QA_C9R_LocalPlayer",
                "QA_C9R_PlayerTarget",
                "QA_C9R_PlayerLookAt",
                "QA_C9R_PlayerRig"
            };
            for (int index = 0; index < names.Length; index++)
            {
                GameObject root = FindRoot(scene, names[index]);
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static GameObject FindRoot(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == name)
                {
                    return root;
                }
            }

            return null;
        }

        private static void RemoveLocalOutputAndLegacyRoots(Scene scene)
        {
            var remove = new HashSet<GameObject>();
            foreach (CameraOutputAuthoring item in
                     All<CameraOutputAuthoring>(scene))
            {
                remove.Add(item.gameObject);
            }
            foreach (CinemachineBrain item in All<CinemachineBrain>(scene))
            {
                remove.Add(item.gameObject);
            }
            foreach (UnityEngine.Camera item in All<UnityEngine.Camera>(scene))
            {
                remove.Add(item.gameObject);
            }
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name.StartsWith("QA_C9L_", StringComparison.Ordinal))
                {
                    remove.Add(root);
                }
            }
            foreach (GameObject item in remove)
            {
                UnityEngine.Object.DestroyImmediate(item);
            }
        }

        private static Transform Target(
            Scene scene,
            string name,
            Vector3 position)
        {
            GameObject item = RootObject(scene, name);
            item.transform.position = position;
            return item.transform;
        }

        private static GameObject RootObject(Scene scene, string name)
        {
            GameObject item = Find(scene, name);
            if (item != null)
            {
                return item;
            }

            item = new GameObject(name);
            SceneManager.MoveGameObjectToScene(item, scene);
            return item;
        }

        private static GameObject Child(Transform parent, string name)
        {
            for (int index = 0; index < parent.childCount; index++)
            {
                if (parent.GetChild(index).name == name)
                {
                    return parent.GetChild(index).gameObject;
                }
            }

            var item = new GameObject(name);
            item.transform.SetParent(parent, false);
            return item;
        }

        private static T Component<T>(GameObject item)
            where T : Component
        {
            T component = item.GetComponent<T>();
            return component != null ? component : item.AddComponent<T>();
        }

        private static List<T> All<T>(Scene scene)
            where T : Component
        {
            var result = new List<T>();
            foreach (T item in Resources.FindObjectsOfTypeAll<T>())
            {
                if (item != null && item.gameObject.scene == scene)
                {
                    result.Add(item);
                }
            }
            return result;
        }

        private static T Single<T>(Scene scene)
            where T : Component
        {
            List<T> all = All<T>(scene);
            if (all.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Expected one {typeof(T).Name} in '{scene.name}', found '{all.Count}'.");
            }
            return all[0];
        }

        private static GameObject Find(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == name)
                {
                    return root;
                }
                foreach (Transform child in
                         root.GetComponentsInChildren<Transform>(true))
                {
                    if (child.name == name)
                    {
                        return child.gameObject;
                    }
                }
            }
            return null;
        }

        private static void EnsureBuildScene()
        {
            var scenes = new List<EditorBuildSettingsScene>(
                EditorBuildSettings.scenes);

            for (int index = 0; index < scenes.Count; index++)
            {
                if (!string.Equals(
                        scenes[index].path,
                        ScenePath,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (!scenes[index].enabled)
                {
                    scenes[index] = new EditorBuildSettingsScene(ScenePath, true);
                    EditorBuildSettings.scenes = scenes.ToArray();
                }

                return;
            }

            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static T LoadOrCreate<T>(string path)
            where T : ScriptableObject
        {
            T value = AssetDatabase.LoadAssetAtPath<T>(path);
            if (value != null)
            {
                return value;
            }
            value = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(value, path);
            return value;
        }

        private static T Require<T>(string path)
            where T : UnityEngine.Object
        {
            return AssetDatabase.LoadAssetAtPath<T>(path) ??
                throw new InvalidOperationException(
                    $"Required asset is missing at '{path}'.");
        }

        private static void Set(
            UnityEngine.Object target,
            string property,
            object value)
        {
            var so = new SerializedObject(target);
            so.Update();
            SerializedProperty item = Property(so, property);
            if (value == null)
            {
                if (item.propertyType != SerializedPropertyType.ObjectReference)
                {
                    throw new InvalidOperationException(
                        $"Serialized property '{property}' on '{target.GetType().Name}' " +
                        $"does not accept a null object reference. type='{item.propertyType}'.");
                }

                item.objectReferenceValue = null;
            }
            else if (value is UnityEngine.Object reference)
            {
                item.objectReferenceValue = reference;
            }
            else if (value is UnityEngine.Object[] references)
            {
                if (!item.isArray)
                {
                    throw new InvalidOperationException(
                        $"Serialized property '{property}' is not an array on '{target.GetType().Name}'.");
                }

                item.arraySize = references.Length;
                for (int index = 0; index < references.Length; index++)
                {
                    item.GetArrayElementAtIndex(index).objectReferenceValue =
                        references[index];
                }
            }
            else if (value is string text)
            {
                item.stringValue = text;
            }
            else if (value is int number)
            {
                item.intValue = number;
            }
            else if (value is bool flag)
            {
                item.boolValue = flag;
            }
            else
            {
                throw new InvalidOperationException(
                    $"Unsupported value for '{property}'.");
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static SerializedProperty Property(
            SerializedObject serialized,
            string property)
        {
            return serialized.FindProperty(property) ??
                throw new InvalidOperationException(
                    $"Serialized property '{property}' was not found on '{serialized.targetObject.GetType().Name}'.");
        }
    }
}
