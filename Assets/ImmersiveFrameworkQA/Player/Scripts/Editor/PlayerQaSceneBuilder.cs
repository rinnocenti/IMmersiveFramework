using System;
using System.IO;
using Immersive.Framework.Actors;
using Immersive.Framework.Authoring;
using Immersive.Framework.Editor.PlayerParticipation;
using Immersive.Framework.GameFlow;
using Immersive.Framework.PlayerParticipation;
using Immersive.Framework.PlayerSlots;
using Immersive.Framework.UnityInput;
using ImmersiveFrameworkQA.Hub;
using ImmersiveFrameworkQA.Player;
using ImmersiveFrameworkQA.Player.Internal.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace ImmersiveFrameworkQA.Player.Editor
{
    /// <summary>
    /// Idempotent creator for the canonical Player QA functional: fixtures,
    /// Route/Activity, scenes, Hub entry and Manager-Provisioned persistent wiring.
    /// </summary>
    public static class PlayerQaSceneBuilder
    {
        private const string Prefix = "[QA_PLAYER_SETUP]";

        public static void ConfigurePlayerQa()
        {
            try
            {
                EnsureSharedFixtures();
                ActivityAsset startup = EnsureActivity(
                    PlayerQaPaths.StartupActivityPath,
                    "QA Player Startup Activity",
                    "Startup Activity for the canonical Player QA route.",
                    ActivityPlayerRelocationPolicy.ApplyExplicitRelocation,
                    PlayerParticipationRequirementLevel.JoinedSlots);
                ActivityAsset relocate = EnsureActivity(
                    PlayerQaPaths.RelocateActivityPath,
                    "QA Player Relocate Activity",
                    "Dedicated Activity used to prove Player Actor lifecycle preparation and explicit relocation.",
                    ActivityPlayerRelocationPolicy.ApplyExplicitRelocation,
                    PlayerParticipationRequirementLevel.LogicalActorsPrepared);
                ActivityAsset gameplayReady = EnsureActivity(
                    PlayerQaPaths.GameplayReadyActivityPath,
                    "QA Player Gameplay Ready Activity",
                    "Dedicated Activity used to prove Player gameplay projection and Presentation input reader binding.",
                    ActivityPlayerRelocationPolicy.NoRelocation,
                    PlayerParticipationRequirementLevel.GameplayReady);
                EnsureActivity(
                    PlayerQaPaths.EmptyActivityPath,
                    "QA Player Empty Activity",
                    "Activity with no Player relocation bindings.",
                    ActivityPlayerRelocationPolicy.NoRelocation,
                    PlayerParticipationRequirementLevel.JoinedSlots);
                RouteAsset primaryRoute = EnsureRoute(
                    PlayerQaPaths.PrimaryRoutePath,
                    "QA Player",
                    PlayerQaPaths.PrimaryScenePath,
                    "Canonical Manager-Provisioned Player QA route.",
                    startup);
                RouteAsset sceneRoute = EnsureRoute(
                    PlayerQaPaths.SceneProvidedRoutePath,
                    "QA Player Scene-Provided",
                    PlayerQaPaths.SceneProvidedScenePath,
                    "Scene-Provided Player QA route.",
                    startup);
                RouteAsset hubRoute = AssetDatabase.LoadAssetAtPath<RouteAsset>(
                    PlayerQaPaths.HubRoutePath);

                CreatePrimaryScene(
                    primaryRoute,
                    sceneRoute,
                    hubRoute,
                    startup,
                    relocate,
                    gameplayReady);
                CreateSceneProvidedScene(sceneRoute, primaryRoute, hubRoute, startup);
                EnsureSceneInBuildSettings(PlayerQaPaths.PrimaryScenePath);
                EnsureSceneInBuildSettings(PlayerQaPaths.SceneProvidedScenePath);

                QaManagerProvisionedPlayerFixture.PrepareAndValidate();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                PlayerQaAuthoringRegression.Validate(emitResult: false);
                PlayerQaPauseCompositionRegression.Execute(out _);

                Debug.Log(
                    $"{Prefix} status='Applied' primaryScene='QA_Player' " +
                    "sceneProvided='QA_PlayerSceneProvided' " +
                    "next='Open QA_Hub, enter Play Mode, open Player QA, Run All Player QA'.");
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"{Prefix} status='Failed' reason='{Escape(exception.GetBaseException().Message)}'.");
                throw;
            }
        }

        public static void EnsureSharedFixtures()
        {
            EnsureFolders();
            InputActionAsset actions = EnsureInputActions();
            GameObject defaultPresentation = EnsurePresentation(
                PlayerQaPaths.DefaultPresentationPath,
                "QA_DefaultPresentation",
                PrimitiveType.Capsule,
                gameplayInputReaderCount: 1);
            GameObject alternatePresentation = EnsurePresentation(
                PlayerQaPaths.AlternatePresentationPath,
                "QA_AlternatePresentation",
                PrimitiveType.Cube,
                gameplayInputReaderCount: 1);
            GameObject noGameplayReaderPresentation = EnsurePresentation(
                PlayerQaPaths.NoGameplayReaderPresentationPath,
                "QA_NoGameplayReaderPresentation",
                PrimitiveType.Sphere,
                gameplayInputReaderCount: 0);
            GameObject ambiguousGameplayReaderPresentation = EnsurePresentation(
                PlayerQaPaths.AmbiguousGameplayReaderPresentationPath,
                "QA_AmbiguousGameplayReaderPresentation",
                PrimitiveType.Cylinder,
                gameplayInputReaderCount: 2);
            ActorProfile defaultActor = EnsureActorProfile(
                PlayerQaPaths.DefaultActorPath,
                "QA_DefaultActor",
                PlayerQaPaths.DefaultActorId,
                "QA Default Actor",
                defaultPresentation);
            ActorProfile alternateActor = EnsureActorProfile(
                PlayerQaPaths.AlternateActorPath,
                "QA_AlternateActor",
                PlayerQaPaths.AlternateActorId,
                "QA Alternate Actor",
                alternatePresentation);
            ActorProfile noGameplayReaderActor = EnsureActorProfile(
                PlayerQaPaths.NoGameplayReaderActorPath,
                "QA_NoGameplayReaderActor",
                PlayerQaPaths.NoGameplayReaderActorId,
                "QA No Gameplay Reader Actor",
                noGameplayReaderPresentation);
            ActorProfile ambiguousGameplayReaderActor = EnsureActorProfile(
                PlayerQaPaths.AmbiguousGameplayReaderActorPath,
                "QA_AmbiguousGameplayReaderActor",
                PlayerQaPaths.AmbiguousGameplayReaderActorId,
                "QA Ambiguous Gameplay Reader Actor",
                ambiguousGameplayReaderPresentation);
            PlayerSlotProfile playerOne = EnsureSlotProfile(
                PlayerQaPaths.PlayerOneSlotPath,
                "QA_PlayerSlot_P1",
                PlayerQaPaths.PlayerOneSlotId,
                "QA Player P1",
                0,
                defaultActor);
            PlayerSlotProfile playerTwo = EnsureSlotProfile(
                PlayerQaPaths.PlayerTwoSlotPath,
                "QA_PlayerSlot_P2",
                PlayerQaPaths.PlayerTwoSlotId,
                "QA Player P2",
                1,
                alternateActor);
            AssetDatabase.SaveAssets();
            playerOne = Load<PlayerSlotProfile>(PlayerQaPaths.PlayerOneSlotPath);
            playerTwo = Load<PlayerSlotProfile>(PlayerQaPaths.PlayerTwoSlotPath);
            GameObject runtimeHost = EnsureRuntimeHost();
            EnsureManagerLocalPlayerHost(actions, runtimeHost);
            EnsureSceneLocalPlayerHost(
                actions,
                runtimeHost,
                playerOne,
                defaultActor,
                defaultPresentation);
            EnsureSessionProfile(
                PlayerQaPaths.ManagerSessionPath,
                "QA_PlayerSession_Manager",
                playerOne,
                playerTwo,
                initialJoiningOpen: true,
                PlayerHostProvisioningMode.ManagerProvisioned,
                PlayerActorResolutionPolicy.ResolveConfiguredDefault);
            EnsureSessionProfile(
                PlayerQaPaths.SceneSessionPath,
                "QA_PlayerSession_Scene",
                playerOne,
                playerTwo,
                initialJoiningOpen: false,
                PlayerHostProvisioningMode.SceneProvided,
                PlayerActorResolutionPolicy.ResolveConfiguredDefault);
            EnsureSessionProfile(
                PlayerQaPaths.ClosedUnresolvedSessionPath,
                "QA_PlayerSession_JoinClosed_LeaveUnresolved",
                playerOne,
                playerTwo,
                initialJoiningOpen: false,
                PlayerHostProvisioningMode.ManagerProvisioned,
                PlayerActorResolutionPolicy.LeaveUnresolved);
            AssetDatabase.SaveAssets();
        }

        private static void CreatePrimaryScene(
            RouteAsset primaryRoute,
            RouteAsset sceneRoute,
            RouteAsset hubRoute,
            ActivityAsset startup,
            ActivityAsset relocate,
            ActivityAsset gameplayReady)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "QA_Player";
            CreateCamera(new Color(0.04f, 0.05f, 0.07f, 1f));
            CreateLight();

            var root = new GameObject("QA_PlayerRoot");
            PlayerSlotProfile playerOne = Load<PlayerSlotProfile>(PlayerQaPaths.PlayerOneSlotPath);
            PlayerSlotProfile playerTwo = Load<PlayerSlotProfile>(PlayerQaPaths.PlayerTwoSlotPath);
            ActorProfile defaultActor = Load<ActorProfile>(PlayerQaPaths.DefaultActorPath);
            ActorProfile alternateActor = Load<ActorProfile>(PlayerQaPaths.AlternateActorPath);
            ActorProfile noGameplayReaderActor = Load<ActorProfile>(
                PlayerQaPaths.NoGameplayReaderActorPath);
            ActorProfile ambiguousGameplayReaderActor = Load<ActorProfile>(
                PlayerQaPaths.AmbiguousGameplayReaderActorPath);
            GameObject managerHostPrefab = Load<GameObject>(PlayerQaPaths.ManagerHostPath);

            Transform p1Anchor = CreateAnchor(root.transform, "QA_Player_SpatialAnchor_P1", new Vector3(-1.5f, 0f, 0f));
            Transform p2Anchor = CreateAnchor(root.transform, "QA_Player_SpatialAnchor_P2", new Vector3(1.5f, 0f, -1.5f));
            Transform relocateAnchor = CreateAnchor(root.transform, "QA_Player_RelocationAnchor_P1", new Vector3(1.5f, 0f, 0f));

            RoutePlayerSpatialEntryAuthoring spatial = root.AddComponent<RoutePlayerSpatialEntryAuthoring>();
            ConfigureSpatial(spatial, (playerOne, p1Anchor), (playerTwo, p2Anchor));

            ActivityPlayerRelocationAuthoring relocation = root.AddComponent<ActivityPlayerRelocationAuthoring>();
            ConfigureRelocation(relocation, relocate, playerOne, relocateAnchor);

            var panelObject = new GameObject("QA_PlayerPanel");
            panelObject.transform.SetParent(root.transform, false);
            PlayerQaPanel panel = panelObject.AddComponent<PlayerQaPanel>();

            PlayerQaScopedAccessProbe probe = CreateConsumer<PlayerQaScopedAccessProbe>(
                panelObject.transform,
                "QA_Player_ScopedAccess",
                LocalPlayerProvisioningConsumerScope.Route);
            PlayerSessionObserver observer = CreateConsumer<PlayerSessionObserver>(
                panelObject.transform,
                "QA_Player_Observer",
                LocalPlayerProvisioningConsumerScope.Route);
            PlayerQaScopedAccessProbe activityScope = CreateConsumer<PlayerQaScopedAccessProbe>(
                panelObject.transform,
                "QA_Player_ActivityScopedAccess",
                LocalPlayerProvisioningConsumerScope.Activity);

            PlayerSessionJoinCommandTrigger join = CreateConsumer<PlayerSessionJoinCommandTrigger>(
                panelObject.transform, "QA_Player_Join", LocalPlayerProvisioningConsumerScope.Route);
            PlayerSessionLeaveCommandTrigger leave = CreateConsumer<PlayerSessionLeaveCommandTrigger>(
                panelObject.transform, "QA_Player_Leave", LocalPlayerProvisioningConsumerScope.Route);
            SetObject(leave, "playerSlot", playerOne);
            PlayerSessionSelectActorCommandTrigger selectActor =
                CreateConsumer<PlayerSessionSelectActorCommandTrigger>(
                    panelObject.transform, "QA_Player_SelectActor", LocalPlayerProvisioningConsumerScope.Route);
            SetObject(selectActor, "playerSlot", playerOne);
            SetObject(selectActor, "actorProfile", defaultActor);
            PlayerSessionDefaultActorSelectionCommandTrigger defaultActorCommand =
                CreateConsumer<PlayerSessionDefaultActorSelectionCommandTrigger>(
                    panelObject.transform, "QA_Player_SelectDefaultActor", LocalPlayerProvisioningConsumerScope.Route);
            SetObject(defaultActorCommand, "playerSlot", playerOne);
            PlayerSessionReplaceActorSelectionCommandTrigger replace =
                CreateConsumer<PlayerSessionReplaceActorSelectionCommandTrigger>(
                    panelObject.transform, "QA_Player_ReplaceActor", LocalPlayerProvisioningConsumerScope.Route);
            SetObject(replace, "playerSlot", playerOne);
            SetObject(replace, "actorProfile", alternateActor);
            PlayerSessionClearActorSelectionCommandTrigger clear =
                CreateConsumer<PlayerSessionClearActorSelectionCommandTrigger>(
                    panelObject.transform, "QA_Player_ClearActor", LocalPlayerProvisioningConsumerScope.Route);
            SetObject(clear, "playerSlot", playerOne);
            PlayerSessionOpenJoiningCommandTrigger openJoining =
                CreateConsumer<PlayerSessionOpenJoiningCommandTrigger>(
                    panelObject.transform, "QA_Player_OpenJoining", LocalPlayerProvisioningConsumerScope.Route);
            PlayerSessionCloseJoiningCommandTrigger closeJoining =
                CreateConsumer<PlayerSessionCloseJoiningCommandTrigger>(
                    panelObject.transform, "QA_Player_CloseJoining", LocalPlayerProvisioningConsumerScope.Route);

            ActivityRequestTrigger relocateTrigger = CreateActivityTrigger(
                panelObject.transform,
                "ActivityTrigger_PlayerRelocate",
                relocate,
                "qa.player.actor-lifecycle");
            ActivityRequestTrigger gameplayReadyTrigger = CreateActivityTrigger(
                panelObject.transform,
                "ActivityTrigger_PlayerGameplayReady",
                gameplayReady,
                "qa.player.gameplay-ready");

            RouteRequestTrigger hubTrigger = CreateRouteTrigger(
                panelObject.transform, "RouteTrigger_Hub", hubRoute, "qa.player.return-hub");
            CreateRouteTrigger(
                panelObject.transform,
                "RouteTrigger_PlayerSceneProvided",
                sceneRoute,
                "qa.player.scene-provided");

            panel.Configure(
                probe,
                observer,
                activityScope,
                join,
                leave,
                selectActor,
                defaultActorCommand,
                replace,
                clear,
                openJoining,
                closeJoining,
                managerHostPrefab.GetComponent<LocalPlayerHostAuthoring>(),
                playerOne,
                playerTwo,
                defaultActor,
                alternateActor,
                noGameplayReaderActor,
                ambiguousGameplayReaderActor,
                spatial,
                relocation,
                startup,
                relocate,
                gameplayReady,
                relocateTrigger,
                gameplayReadyTrigger,
                hubTrigger);

            EditorSceneManager.SaveScene(scene, PlayerQaPaths.PrimaryScenePath);
        }

        private static void CreateSceneProvidedScene(
            RouteAsset sceneRoute,
            RouteAsset primaryRoute,
            RouteAsset hubRoute,
            ActivityAsset startup)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "QA_PlayerSceneProvided";
            CreateCamera(new Color(0.06f, 0.04f, 0.07f, 1f));
            CreateLight();

            var root = new GameObject("QA_PlayerSceneProvidedRoot");
            GameObject sceneHostPrefab = Load<GameObject>(PlayerQaPaths.SceneHostPath);
            GameObject instance = PrefabUtility.InstantiatePrefab(sceneHostPrefab, root.transform) as GameObject;
            if (instance != null)
            {
                instance.name = "QA_SceneLocalPlayerHost";
            }

            Transform p1Anchor = CreateAnchor(root.transform, "QA_PlayerScene_SpatialAnchor_P1", new Vector3(0f, 0f, 0f));
            PlayerSlotProfile playerOne = Load<PlayerSlotProfile>(PlayerQaPaths.PlayerOneSlotPath);
            RoutePlayerSpatialEntryAuthoring spatial = root.AddComponent<RoutePlayerSpatialEntryAuthoring>();
            ConfigureSpatial(spatial, (playerOne, p1Anchor));
            ActivityPlayerRelocationAuthoring relocation = root.AddComponent<ActivityPlayerRelocationAuthoring>();
            ConfigureRelocation(relocation, startup, playerOne, p1Anchor);

            var panelObject = new GameObject("QA_PlayerSceneProvidedPanel");
            panelObject.transform.SetParent(root.transform, false);
            CreateConsumer<PlayerQaScopedAccessProbe>(
                panelObject.transform,
                "QA_PlayerScene_ScopedAccess",
                LocalPlayerProvisioningConsumerScope.Route);
            CreateConsumer<PlayerSessionObserver>(
                panelObject.transform,
                "QA_PlayerScene_Observer",
                LocalPlayerProvisioningConsumerScope.Route);
            CreateRouteTrigger(panelObject.transform, "RouteTrigger_Hub", hubRoute, "qa.player.scene.return-hub");
            CreateRouteTrigger(panelObject.transform, "RouteTrigger_Player", primaryRoute, "qa.player.manager");

            var returnPanel = panelObject.AddComponent<QaHubReturnPanel>();
            returnPanel.Configure(
                panelObject.transform.Find("RouteTrigger_Hub").GetComponent<RouteRequestTrigger>(),
                "Player Scene-Provided QA",
                "Scene-Provided admission is proven by the authored scene host when the Session profile is SceneProvided.");

            EditorSceneManager.SaveScene(scene, PlayerQaPaths.SceneProvidedScenePath);
        }

        internal static InputActionAsset EnsureInputActions()
        {
            var authored = ScriptableObject.CreateInstance<InputActionAsset>();
            try
            {
                authored.name = PlayerQaPaths.InputActionsName;
                InputActionMap gameplay = authored.AddActionMap("Gameplay");
                InputAction activate = gameplay.AddAction("Activate", InputActionType.Button);
                activate.AddBinding("<Keyboard>/space");
                InputActionMap global = authored.AddActionMap("Global");
                InputAction pause = global.AddAction("Pause", InputActionType.Button);
                pause.AddBinding("<Keyboard>/escape");

                string absolutePath = Path.Combine(
                    Directory.GetParent(Application.dataPath).FullName,
                    PlayerQaPaths.InputActionsPath);
                Directory.CreateDirectory(Path.GetDirectoryName(absolutePath) ?? string.Empty);
                string temporaryPath = absolutePath + ".tmp";
                File.WriteAllText(temporaryPath, authored.ToJson());
                if (File.Exists(absolutePath))
                {
                    File.Replace(temporaryPath, absolutePath, null);
                }
                else
                {
                    File.Move(temporaryPath, absolutePath);
                }

                AssetDatabase.ImportAsset(
                    PlayerQaPaths.InputActionsPath,
                    ImportAssetOptions.ForceSynchronousImport);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(authored);
            }

            return Load<InputActionAsset>(PlayerQaPaths.InputActionsPath);
        }

        private static GameObject EnsurePresentation(
            string path,
            string name,
            PrimitiveType primitive,
            int gameplayInputReaderCount)
        {
            if (gameplayInputReaderCount < 0 || gameplayInputReaderCount > 2)
            {
                throw new ArgumentOutOfRangeException(nameof(gameplayInputReaderCount));
            }

            return RebuildPrefab(path, name, root =>
            {
                if (gameplayInputReaderCount == 1)
                {
                    root.AddComponent<PlayerGameplayInputReader>();
                }
                else if (gameplayInputReaderCount == 2)
                {
                    var firstReader = new GameObject("GameplayReader_A");
                    firstReader.transform.SetParent(root.transform, false);
                    firstReader.AddComponent<PlayerGameplayInputReader>();
                    var secondReader = new GameObject("GameplayReader_B");
                    secondReader.transform.SetParent(root.transform, false);
                    secondReader.AddComponent<PlayerGameplayInputReader>();
                }

                GameObject visual = GameObject.CreatePrimitive(primitive);
                visual.name = "Visual";
                visual.transform.SetParent(root.transform, false);
                visual.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f);
                Collider collider = visual.GetComponent<Collider>();
                if (collider != null)
                {
                    UnityEngine.Object.DestroyImmediate(collider);
                }
            });
        }

        private static ActorProfile EnsureActorProfile(
            string path,
            string name,
            string actorProfileId,
            string displayName,
            GameObject presentation)
        {
            ActorProfile profile = LoadOrCreate<ActorProfile>(path, name);
            SetString(profile, "actorProfileId", actorProfileId);
            SetString(profile, "displayName", displayName);
            SetString(profile, "description", "Canonical Player QA Actor Profile.");
            SetEnum(profile, "actorKind", ActorKind.Player);
            SetEnum(profile, "actorRole", ActorRole.Protagonist);
            SetObject(profile, "presentationPrefab", presentation);
            return profile;
        }

        private static PlayerSlotProfile EnsureSlotProfile(
            string path,
            string name,
            string playerSlotId,
            string displayName,
            int displayOrder,
            ActorProfile defaultActor)
        {
            PlayerSlotProfile profile = LoadOrCreate<PlayerSlotProfile>(path, name);
            SetString(profile, "playerSlotId", playerSlotId);
            SetString(profile, "displayName", displayName);
            SetString(profile, "description", "Canonical Player QA Slot Profile.");
            SetInt(profile, "displayOrder", displayOrder);
            SetObject(profile, "defaultActorProfile", defaultActor);
            AssetDatabase.SaveAssetIfDirty(profile);
            profile = Load<PlayerSlotProfile>(path);
            if (!profile.TryGetPlayerSlotId(out PlayerSlotId resolved, out string issue) ||
                profile.PlayerSlotIdText != playerSlotId)
            {
                throw new InvalidOperationException(
                    $"Player QA Slot Profile '{name}' did not persist identity '{playerSlotId}'. {issue}");
            }

            if (resolved.StableText != $"PlayerSlot:{playerSlotId}")
            {
                throw new InvalidOperationException(
                    $"Player QA Slot Profile '{name}' resolved '{resolved.StableText}', expected 'PlayerSlot:{playerSlotId}'.");
            }

            return profile;
        }

        private static GameObject EnsureRuntimeHost()
        {
            return RebuildPrefab(
                PlayerQaPaths.RuntimeHostPath,
                "QA_PlayerActorRuntimeHost",
                root =>
                {
                    PlayerActorRuntimeHost host = root.AddComponent<PlayerActorRuntimeHost>();
                    PlayerActorDeclaration declaration = root.AddComponent<PlayerActorDeclaration>();
                    SetString(declaration, "displayName", "QA Player Actor");
                    SetString(declaration, "reason", "qa.player.runtime-host");
                    var presentationMount = new GameObject("PresentationMount");
                    presentationMount.transform.SetParent(root.transform, false);
                    SetObject(host, "playerActorDeclaration", declaration);
                    SetObject(host, "presentationMount", presentationMount.transform);
                });
        }

        private static void EnsureManagerLocalPlayerHost(
            InputActionAsset actions,
            GameObject runtimeHostPrefab)
        {
            RebuildPrefab(
                PlayerQaPaths.ManagerHostPath,
                "QA_ManagerLocalPlayerHost",
                root => ConfigureLocalPlayerHost(
                    root,
                    actions,
                    runtimeHostPrefab,
                    includeSceneAdmission: false,
                    null,
                    null,
                    null));
        }

        private static void EnsureSceneLocalPlayerHost(
            InputActionAsset actions,
            GameObject runtimeHostPrefab,
            PlayerSlotProfile playerSlot,
            ActorProfile actorProfile,
            GameObject presentationPrefab)
        {
            RebuildPrefab(
                PlayerQaPaths.SceneHostPath,
                "QA_SceneLocalPlayerHost",
                root => ConfigureLocalPlayerHost(
                    root,
                    actions,
                    runtimeHostPrefab,
                    includeSceneAdmission: true,
                    playerSlot,
                    actorProfile,
                    presentationPrefab));
        }

        private static void ConfigureLocalPlayerHost(
            GameObject root,
            InputActionAsset actions,
            GameObject runtimeHostPrefab,
            bool includeSceneAdmission,
            PlayerSlotProfile playerSlot,
            ActorProfile actorProfile,
            GameObject presentationPrefab)
        {
            PlayerInput playerInput = root.AddComponent<PlayerInput>();
            playerInput.actions = actions;
            playerInput.defaultActionMap = "Gameplay";

            LocalPlayerHostAuthoring localHost = root.AddComponent<LocalPlayerHostAuthoring>();
            var actorMount = new GameObject("ActorMount");
            actorMount.transform.SetParent(root.transform, false);
            PlayerActorRuntimeHost runtimeHostPrefabComponent =
                runtimeHostPrefab.GetComponent<PlayerActorRuntimeHost>();
            SetObject(localHost, "playerInput", playerInput);
            SetObject(localHost, "actorMount", actorMount.transform);
            SetObject(localHost, "playerActorRuntimeHostPrefab", runtimeHostPrefabComponent);

            UnityPlayerInputGateAdapter gate = root.AddComponent<UnityPlayerInputGateAdapter>();
            SetObject(gate, "playerInput", playerInput);
            InputActionMap gameplay = actions.FindActionMap("Gameplay", false);
            if (gameplay != null)
            {
                SerializedObject serializedGate = new SerializedObject(gate);
                SerializedProperty mapReference = serializedGate.FindProperty("gameplayActionMap");
                if (mapReference != null)
                {
                    mapReference.FindPropertyRelative("actionAsset").objectReferenceValue = actions;
                    mapReference.FindPropertyRelative("actionMapId").stringValue = gameplay.id.ToString("D");
                    mapReference.FindPropertyRelative("cachedActionMapName").stringValue = gameplay.name;
                    serializedGate.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            if (!includeSceneAdmission)
            {
                return;
            }

            SceneProvidedLocalPlayerAuthoring admission =
                root.AddComponent<SceneProvidedLocalPlayerAuthoring>();
            GameObject runtimeHostObject = PrefabUtility.InstantiatePrefab(
                runtimeHostPrefab,
                actorMount.transform) as GameObject;
            PlayerActorRuntimeHost sceneRuntimeHost =
                runtimeHostObject.GetComponent<PlayerActorRuntimeHost>();
            PrefabUtility.InstantiatePrefab(
                presentationPrefab,
                sceneRuntimeHost.PresentationMount);
            SetObject(admission, "localPlayerHost", localHost);
            SetObject(admission, "playerSlotProfile", playerSlot);
            SetObject(admission, "actorProfile", actorProfile);
            SceneProvidedLocalPlayerAuthoringResult validation =
                SceneProvidedLocalPlayerAuthoringUtility.Validate(admission, false);
            if (!validation.Succeeded)
            {
                throw new InvalidOperationException(
                    "Player QA Scene-Provided composition is invalid. " +
                    validation.Message);
            }
        }

        private static PlayerSessionProfile EnsureSessionProfile(
            string path,
            string name,
            PlayerSlotProfile playerOne,
            PlayerSlotProfile playerTwo,
            bool initialJoiningOpen,
            PlayerHostProvisioningMode hostProvisioning,
            PlayerActorResolutionPolicy actorResolutionPolicy)
        {
            PlayerSessionProfile profile = LoadOrCreate<PlayerSessionProfile>(path, name);
            var serialized = new SerializedObject(profile);
            SerializedProperty slots = serialized.FindProperty("supportedSlots");
            slots.arraySize = 2;
            slots.GetArrayElementAtIndex(0).objectReferenceValue = playerOne;
            slots.GetArrayElementAtIndex(1).objectReferenceValue = playerTwo;
            serialized.FindProperty("initialJoiningOpen").boolValue = initialJoiningOpen;
            serialized.FindProperty("hostProvisioning").intValue = (int)hostProvisioning;
            serialized.FindProperty("actorResolutionPolicy").intValue = (int)actorResolutionPolicy;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(profile);
            if (!profile.TryValidate(out string issue))
            {
                throw new InvalidOperationException($"Player QA Session Profile '{name}' is invalid. {issue}");
            }

            return profile;
        }

        private static ActivityAsset EnsureActivity(
            string path,
            string activityName,
            string description,
            ActivityPlayerRelocationPolicy relocationPolicy,
            PlayerParticipationRequirementLevel requirementLevel)
        {
            ActivityAsset asset = LoadOrCreate<ActivityAsset>(path, Path.GetFileNameWithoutExtension(path));
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("activityId").stringValue =
                "qa." + Path.GetFileNameWithoutExtension(path).Replace("_", ".").ToLowerInvariant();
            serialized.FindProperty("activityName").stringValue = activityName;
            serialized.FindProperty("description").stringValue = description;
            serialized.FindProperty("playerParticipationProjectionMode").intValue =
                (int)ActivityParticipationProjectionMode.AllJoinedSlots;
            serialized.FindProperty("playerParticipationZeroParticipantPolicy").intValue =
                (int)ActivityParticipationZeroParticipantPolicy.Allowed;
            serialized.FindProperty("playerParticipationRequirementLevel").intValue =
                (int)requirementLevel;
            serialized.FindProperty("playerRelocationPolicy").intValue = (int)relocationPolicy;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static RouteAsset EnsureRoute(
            string path,
            string routeName,
            string scenePath,
            string description,
            ActivityAsset startupActivity)
        {
            RouteAsset asset = LoadOrCreate<RouteAsset>(path, Path.GetFileNameWithoutExtension(path));
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("routeName").stringValue = routeName;
            serialized.FindProperty("primaryScenePath").stringValue = scenePath;
            serialized.FindProperty("primarySceneName").stringValue = Path.GetFileNameWithoutExtension(scenePath);
            serialized.FindProperty("startupActivity").objectReferenceValue = startupActivity;
            serialized.FindProperty("playerSpatialEntryPolicy").intValue =
                (int)RoutePlayerSpatialEntryPolicy.ApplyExplicitPlacement;
            serialized.FindProperty("description").stringValue = description;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static void ConfigureSpatial(
            RoutePlayerSpatialEntryAuthoring spatial,
            params (PlayerSlotProfile Slot, Transform Anchor)[] entries)
        {
            var serialized = new SerializedObject(spatial);
            SerializedProperty bindings = serialized.FindProperty("bindings");
            bindings.arraySize = entries.Length;
            for (int index = 0; index < entries.Length; index++)
            {
                SerializedProperty binding = bindings.GetArrayElementAtIndex(index);
                binding.FindPropertyRelative("playerSlotProfile").objectReferenceValue = entries[index].Slot;
                binding.FindPropertyRelative("placementAnchor").objectReferenceValue = entries[index].Anchor;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureRelocation(
            ActivityPlayerRelocationAuthoring relocation,
            ActivityAsset activity,
            PlayerSlotProfile playerOne,
            Transform anchor)
        {
            var serialized = new SerializedObject(relocation);
            SerializedProperty bindings = serialized.FindProperty("bindings");
            bindings.arraySize = 1;
            SerializedProperty binding = bindings.GetArrayElementAtIndex(0);
            binding.FindPropertyRelative("activity").objectReferenceValue = activity;
            binding.FindPropertyRelative("playerSlotProfile").objectReferenceValue = playerOne;
            binding.FindPropertyRelative("relocationAnchor").objectReferenceValue = anchor;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static T CreateConsumer<T>(
            Transform parent,
            string name,
            LocalPlayerProvisioningConsumerScope scope)
            where T : PlayerSessionScopedAccessConsumer
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            T component = go.AddComponent<T>();
            SetEnum(component, "scope", scope);
            return component;
        }

        private static ActivityRequestTrigger CreateActivityTrigger(
            Transform parent,
            string name,
            ActivityAsset activity,
            string reason)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            ActivityRequestTrigger trigger = go.AddComponent<ActivityRequestTrigger>();
            if (activity != null)
            {
                SetObject(trigger, "targetActivity", activity);
            }

            SetString(trigger, "reason", reason);
            return trigger;
        }

        private static RouteRequestTrigger CreateRouteTrigger(
            Transform parent,
            string name,
            RouteAsset route,
            string reason)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RouteRequestTrigger trigger = go.AddComponent<RouteRequestTrigger>();
            if (route != null)
            {
                SetObject(trigger, "targetRoute", route);
            }

            SetString(trigger, "reason", reason);
            return trigger;
        }

        private static Transform CreateAnchor(Transform parent, string name, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            return go.transform;
        }

        private static void CreateCamera(Color background)
        {
            var cameraRoot = new GameObject("QA_PlayerCamera");
            Camera camera = cameraRoot.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = background;
            cameraRoot.tag = "MainCamera";
            cameraRoot.transform.position = new Vector3(0f, 1.6f, -8f);
        }

        private static void CreateLight()
        {
            var lightRoot = new GameObject("QA_PlayerLight");
            Light light = lightRoot.AddComponent<Light>();
            light.type = LightType.Directional;
            lightRoot.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        private static GameObject RebuildPrefab(string path, string name, Action<GameObject> configure)
        {
            var staging = new GameObject(name);
            try
            {
                configure(staging);
                PrefabUtility.SaveAsPrefabAsset(staging, path);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(staging);
            }

            return Load<GameObject>(path);
        }

        private static T LoadOrCreate<T>(string path, string name)
            where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(asset, path);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                asset = AssetDatabase.LoadAssetAtPath<T>(path);
            }

            if (asset == null)
            {
                throw new InvalidOperationException($"Could not create Player QA asset '{path}'.");
            }

            if (asset.name != name)
            {
                asset.name = name;
                EditorUtility.SetDirty(asset);
            }

            return asset;
        }

        private static T Load<T>(string path)
            where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                throw new InvalidOperationException($"Required Player QA fixture is missing at '{path}'.");
            }

            return asset;
        }

        private static void EnsureFolders()
        {
            EnsureFolder("Assets", "ImmersiveFrameworkQA");
            EnsureFolder("Assets/ImmersiveFrameworkQA", "Player");
            EnsureFolder(PlayerQaPaths.Root, "Prefabs");
            EnsureFolder(PlayerQaPaths.Root, "Profiles");
            EnsureFolder(PlayerQaPaths.Root, "Input");
            EnsureFolder(PlayerQaPaths.Root, "Activities");
            EnsureFolder(PlayerQaPaths.Root, "Routes");
            EnsureFolder(PlayerQaPaths.Root, "Scenes");
            EnsureFolder(PlayerQaPaths.Root, "Scripts");
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }

        private static void EnsureSceneInBuildSettings(string scenePath)
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            for (int index = 0; index < scenes.Length; index++)
            {
                if (scenes[index].path == scenePath)
                {
                    if (!scenes[index].enabled)
                    {
                        scenes[index].enabled = true;
                        EditorBuildSettings.scenes = scenes;
                    }

                    return;
                }
            }

            var expanded = new EditorBuildSettingsScene[scenes.Length + 1];
            Array.Copy(scenes, expanded, scenes.Length);
            expanded[scenes.Length] = new EditorBuildSettingsScene(scenePath, true);
            EditorBuildSettings.scenes = expanded;
        }

        private static void SetObject(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
        {
            SerializedObject serialized = BeginSerialized(target);
            SerializedProperty property = RequireProperty(serialized, propertyName);
            property.objectReferenceValue = value;
            ApplySerialized(target, serialized);
        }

        private static void SetString(UnityEngine.Object target, string propertyName, string value)
        {
            SerializedObject serialized = BeginSerialized(target);
            SerializedProperty property = RequireProperty(serialized, propertyName);
            property.stringValue = value;
            ApplySerialized(target, serialized);
        }

        private static void SetInt(UnityEngine.Object target, string propertyName, int value)
        {
            SerializedObject serialized = BeginSerialized(target);
            SerializedProperty property = RequireProperty(serialized, propertyName);
            property.intValue = value;
            ApplySerialized(target, serialized);
        }

        private static void SetEnum<TEnum>(UnityEngine.Object target, string propertyName, TEnum value)
            where TEnum : struct, Enum
        {
            SerializedObject serialized = BeginSerialized(target);
            SerializedProperty property = RequireProperty(serialized, propertyName);
            int index = Array.IndexOf(property.enumNames, value.ToString());
            if (index < 0)
            {
                property.intValue = Convert.ToInt32(value);
            }
            else
            {
                property.enumValueIndex = index;
            }

            ApplySerialized(target, serialized);
        }

        private static SerializedObject BeginSerialized(UnityEngine.Object target)
        {
            var serialized = new SerializedObject(target);
            serialized.Update();
            return serialized;
        }

        private static SerializedProperty RequireProperty(SerializedObject serialized, string propertyName)
        {
            return serialized.FindProperty(propertyName)
                ?? throw new InvalidOperationException(
                    $"Serialized property '{propertyName}' was not found on '{serialized.targetObject.GetType().Name}'.");
        }

        private static void ApplySerialized(UnityEngine.Object target, SerializedObject serialized)
        {
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssetIfDirty(target);
        }

        private static string Escape(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("'", "\\'")
                .Replace("\r", " ")
                .Replace("\n", " ");
        }
    }
}
