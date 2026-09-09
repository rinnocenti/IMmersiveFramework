using System;
using System.Collections.Generic;
using System.Reflection;
using Immersive.Framework.Actors;
using Immersive.Framework.CameraAuthoring;
using Immersive.Framework.Authoring;
using Immersive.Framework.Editor.PlayerParticipation;
using Immersive.Framework.PlayerParticipation;
using Immersive.Framework.PlayerSlots;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ImmersiveFrameworkQA.Player.Editor
{
    /// <summary>
    /// Edit Mode structural/authoring contract for the canonical Player QA
    /// fixtures. It does not start Play Mode or materialize a live Session.
    /// </summary>
    public static class PlayerQaAuthoringRegression
    {
        private const string Prefix = "[QA_PLAYER_AUTHORING]";
        private const string MenuPath =
            "Immersive Framework/QA/Player/Run Authoring Contract";
        private const int ExpectedCaseCount = 14;

        [MenuItem(MenuPath, true)]
        private static bool ValidateRun() =>
            !EditorApplication.isPlayingOrWillChangePlaymode;

        [MenuItem(MenuPath)]
        private static void RunFromMenu()
        {
            Validate(emitResult: true);
        }

        internal static void Validate(bool emitResult)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException(
                    "Player QA authoring contract must run in Edit Mode.");
            }

            var completed = new List<string>(ExpectedCaseCount);
            try
            {
                InputActionAsset actions = Require<InputActionAsset>(PlayerQaPaths.InputActionsPath);
                PlayerSlotProfile playerOne = Require<PlayerSlotProfile>(PlayerQaPaths.PlayerOneSlotPath);
                PlayerSlotProfile playerTwo = Require<PlayerSlotProfile>(PlayerQaPaths.PlayerTwoSlotPath);
                ActorProfile defaultActor = Require<ActorProfile>(PlayerQaPaths.DefaultActorPath);
                ActorProfile alternateActor = Require<ActorProfile>(PlayerQaPaths.AlternateActorPath);
                ActorProfile noGameplayReaderActor = Require<ActorProfile>(
                    PlayerQaPaths.NoGameplayReaderActorPath);
                ActorProfile ambiguousGameplayReaderActor = Require<ActorProfile>(
                    PlayerQaPaths.AmbiguousGameplayReaderActorPath);
                GameObject defaultPresentation = Require<GameObject>(PlayerQaPaths.DefaultPresentationPath);
                GameObject alternatePresentation = Require<GameObject>(PlayerQaPaths.AlternatePresentationPath);
                GameObject noGameplayReaderPresentation = Require<GameObject>(
                    PlayerQaPaths.NoGameplayReaderPresentationPath);
                GameObject ambiguousGameplayReaderPresentation = Require<GameObject>(
                    PlayerQaPaths.AmbiguousGameplayReaderPresentationPath);
                GameObject runtimeHostPrefab = Require<GameObject>(PlayerQaPaths.RuntimeHostPath);
                GameObject managerHostPrefab = Require<GameObject>(PlayerQaPaths.ManagerHostPath);
                GameObject sceneHostPrefab = Require<GameObject>(PlayerQaPaths.SceneHostPath);
                PlayerSessionProfile managerSession = Require<PlayerSessionProfile>(PlayerQaPaths.ManagerSessionPath);
                PlayerSessionProfile sceneSession = Require<PlayerSessionProfile>(PlayerQaPaths.SceneSessionPath);
                PlayerSessionProfile closedSession =
                    Require<PlayerSessionProfile>(PlayerQaPaths.ClosedUnresolvedSessionPath);
                ActivityAsset startupActivity =
                    Require<ActivityAsset>(PlayerQaPaths.StartupActivityPath);
                ActivityAsset relocateActivity =
                    Require<ActivityAsset>(PlayerQaPaths.RelocateActivityPath);
                ActivityAsset gameplayReadyActivity =
                    Require<ActivityAsset>(PlayerQaPaths.GameplayReadyActivityPath);

                ValidateInput(actions);
                completed.Add("input");

                ValidateProfiles(
                    playerOne,
                    playerTwo,
                    defaultActor,
                    alternateActor,
                    noGameplayReaderActor,
                    ambiguousGameplayReaderActor);
                completed.Add("profiles");

                ValidatePresentations(defaultPresentation, alternatePresentation);
                completed.Add("presentations");

                ValidateRequiredCameraSubjectTransform();
                completed.Add("required-camera-subject-transform");

                ValidateReaderCardinalityFixtures(
                    noGameplayReaderPresentation,
                    ambiguousGameplayReaderPresentation,
                    noGameplayReaderActor,
                    ambiguousGameplayReaderActor);
                completed.Add("reader-cardinality-fixtures");

                PlayerActorRuntimeHost runtimeHost = ValidateRuntimeHost(runtimeHostPrefab);
                completed.Add("runtime-host");

                ValidateManagerHost(managerHostPrefab, actions, runtimeHost);
                completed.Add("manager-host");

                ValidateSceneHost(
                    sceneHostPrefab,
                    actions,
                    playerOne,
                    defaultActor,
                    defaultPresentation,
                    runtimeHostPrefab);
                completed.Add("scene-host");

                ValidateSession(
                    managerSession,
                    playerOne,
                    playerTwo,
                    true,
                    PlayerHostProvisioningMode.ManagerProvisioned,
                    PlayerActorResolutionPolicy.ResolveConfiguredDefault);
                ValidateSession(
                    sceneSession,
                    playerOne,
                    playerTwo,
                    false,
                    PlayerHostProvisioningMode.SceneProvided,
                    PlayerActorResolutionPolicy.ResolveConfiguredDefault);
                completed.Add("session-profiles");

                ValidateSession(
                    closedSession,
                    playerOne,
                    playerTwo,
                    false,
                    PlayerHostProvisioningMode.ManagerProvisioned,
                    PlayerActorResolutionPolicy.LeaveUnresolved);
                completed.Add("leave-unresolved-profile");

                ValidateActivityContracts(
                    startupActivity,
                    relocateActivity,
                    gameplayReadyActivity);
                completed.Add("activity-contracts");

                ValidateExplicitCommandSurface();
                completed.Add("explicit-command-surface");

                ValidateGameplayInputReader();
                completed.Add("gameplay-input-reader");

                ValidatePresentationEmbodimentVariation();
                completed.Add("presentation-embodiment-variation");

                Require(completed.Count == ExpectedCaseCount,
                    "Player QA authoring case count changed unexpectedly.");

                if (emitResult)
                {
                    Debug.Log(
                        $"{Prefix} status='Passed' verdict='AuthoringComplete' " +
                        $"cases='{completed.Count}/{ExpectedCaseCount}' " +
                        $"completed='{string.Join(",", completed)}'.");
                }
            }
            catch (Exception exception)
            {
                if (emitResult)
                {
                    Debug.LogError(
                        $"{Prefix} status='Failed' verdict='AuthoringInvalid' " +
                        $"cases='{completed.Count}/{ExpectedCaseCount}' " +
                        $"completed='{string.Join(",", completed)}' " +
                        $"missing='{Escape(exception.Message)}'.");
                }

                throw;
            }
        }

        private static void ValidateActivityContracts(
            ActivityAsset startupActivity,
            ActivityAsset relocateActivity,
            ActivityAsset gameplayReadyActivity)
        {
            Require(
                startupActivity.PlayerParticipationProjectionMode ==
                    ActivityParticipationProjectionMode.AllJoinedSlots &&
                startupActivity.PlayerParticipationZeroParticipantPolicy ==
                    ActivityParticipationZeroParticipantPolicy.Allowed &&
                startupActivity.PlayerParticipationRequirementLevel ==
                    PlayerParticipationRequirementLevel.JoinedSlots,
                "Startup Activity must remain a Session-membership-only Player contract.");

            Require(
                relocateActivity.PlayerParticipationProjectionMode ==
                    ActivityParticipationProjectionMode.AllJoinedSlots &&
                relocateActivity.PlayerParticipationZeroParticipantPolicy ==
                    ActivityParticipationZeroParticipantPolicy.Allowed &&
                relocateActivity.PlayerParticipationRequirementLevel ==
                    PlayerParticipationRequirementLevel.LogicalActorsPrepared,
                "Relocate Activity must own the explicit Player Actor preparation/materialization contract.");

            Require(
                gameplayReadyActivity.PlayerParticipationProjectionMode ==
                    ActivityParticipationProjectionMode.AllJoinedSlots &&
                gameplayReadyActivity.PlayerParticipationZeroParticipantPolicy ==
                    ActivityParticipationZeroParticipantPolicy.Allowed &&
                gameplayReadyActivity.PlayerParticipationRequirementLevel ==
                    PlayerParticipationRequirementLevel.GameplayReady,
                "Gameplay Ready Activity must own the explicit Player gameplay projection contract.");
        }

        private static void ValidateInput(InputActionAsset actions)
        {
            Require(actions != null, "Player QA Input Actions asset is missing.");
            InputActionMap gameplay = actions.FindActionMap("Gameplay", false);
            Require(gameplay != null, "Player QA Input Actions asset has no Gameplay map.");
            InputAction activate = gameplay.FindAction("Activate", false);
            Require(activate != null, "Player QA Gameplay map has no Activate action.");
            Require(
                activate.bindings.Count >= 1 &&
                activate.bindings[0].effectivePath == "<Keyboard>/space",
                "Player QA Activate action must keep its explicit space binding.");
            InputActionMap global = actions.FindActionMap("Global", false);
            Require(global != null && global.FindAction("Pause", false) != null,
                "Player QA Input Actions asset must author a Global Pause action.");
        }

        private static void ValidateProfiles(
            PlayerSlotProfile playerOne,
            PlayerSlotProfile playerTwo,
            ActorProfile defaultActor,
            ActorProfile alternateActor,
            ActorProfile noGameplayReaderActor,
            ActorProfile ambiguousGameplayReaderActor)
        {
            Require(
                playerOne.PlayerSlotIdText == PlayerQaPaths.PlayerOneSlotId &&
                playerTwo.PlayerSlotIdText == PlayerQaPaths.PlayerTwoSlotId &&
                playerOne.PlayerSlotId != playerTwo.PlayerSlotId,
                "Player QA Slot identities must remain distinct and stable.");
            Require(
                defaultActor.ActorProfileIdText == PlayerQaPaths.DefaultActorId &&
                alternateActor.ActorProfileIdText == PlayerQaPaths.AlternateActorId &&
                noGameplayReaderActor.ActorProfileIdText ==
                    PlayerQaPaths.NoGameplayReaderActorId &&
                ambiguousGameplayReaderActor.ActorProfileIdText ==
                    PlayerQaPaths.AmbiguousGameplayReaderActorId,
                "Player QA Actor identities must remain stable.");
            Require(
                playerOne.DefaultActorProfile == defaultActor &&
                playerTwo.DefaultActorProfile == alternateActor,
                "Player QA Slot Profiles must retain their explicit default Actor Profiles.");
        }

        private static void ValidatePresentations(
            GameObject defaultPresentation,
            GameObject alternatePresentation)
        {
            Require(defaultPresentation != alternatePresentation,
                "Default and Alternate Presentation fixtures must be distinct prefabs.");
            ValidatePresentation(
                defaultPresentation,
                "Default",
                "QA_DefaultPresentation");
            ValidatePresentation(
                alternatePresentation,
                "Alternate",
                "QA_AlternatePresentation");
        }

        private static void ValidateRequiredCameraSubjectTransform()
        {
            var root = new GameObject("QA Missing Camera Subject Transform");
            try
            {
                ActorCameraSubjectAuthoring authoring =
                    root.AddComponent<ActorCameraSubjectAuthoring>();
                Require(
                    !authoring.TryValidateConfiguration(out string issue) &&
                    issue.IndexOf("requires an explicit Camera Subject Transform", StringComparison.Ordinal) >= 0 &&
                    authoring.ObservationTransform == null,
                    "Authored Camera Subject without its required Transform must block without falling back to the Actor root.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void ValidatePresentation(
            GameObject presentation,
            string label,
            string expectedName)
        {
            Require(
                PrefabUtility.IsPartOfPrefabAsset(presentation) &&
                presentation.name == expectedName,
                $"{label} Presentation must remain the correctly named prefab asset.");
            Require(
                presentation.GetComponentsInChildren<PlayerInput>(true).Length == 0 &&
                presentation.GetComponentsInChildren<PlayerActorRuntimeHost>(true).Length == 0 &&
                presentation.GetComponentsInChildren<PlayerActorDeclaration>(true).Length == 0 &&
                presentation.GetComponentsInChildren<PlayerGameplayInputReader>(true).Length == 1,
                $"{label} Presentation must contain exactly one PlayerGameplayInputReader and no Player Actor infrastructure.");

            ActorCameraSubjectAuthoring[] cameraSubjects =
                presentation.GetComponentsInChildren<ActorCameraSubjectAuthoring>(true);
            string cameraSubjectIssue = string.Empty;
            Require(
                cameraSubjects.Length == 1 &&
                cameraSubjects[0].gameObject == presentation &&
                cameraSubjects[0].TryValidateConfiguration(out cameraSubjectIssue) &&
                cameraSubjects[0].ObservationTransform != presentation.transform &&
                cameraSubjects[0].ObservationTransform.IsChildOf(presentation.transform),
                $"{label} Presentation requires one explicit child Camera Subject Transform. {cameraSubjectIssue}");
        }

        private static void ValidateReaderCardinalityFixtures(
            GameObject noGameplayReaderPresentation,
            GameObject ambiguousGameplayReaderPresentation,
            ActorProfile noGameplayReaderActor,
            ActorProfile ambiguousGameplayReaderActor)
        {
            Require(
                noGameplayReaderActor.PresentationPrefab == noGameplayReaderPresentation &&
                ambiguousGameplayReaderActor.PresentationPrefab ==
                    ambiguousGameplayReaderPresentation,
                "Player QA reader-cardinality Actor Profiles must retain their exact Presentation fixtures.");
            ValidatePresentationReaderCardinality(
                noGameplayReaderPresentation,
                "No-reader",
                0);
            ValidatePresentationReaderCardinality(
                ambiguousGameplayReaderPresentation,
                "Ambiguous",
                2);
        }

        private static void ValidatePresentationReaderCardinality(
            GameObject presentation,
            string label,
            int expectedReaderCount)
        {
            Require(
                presentation.GetComponentsInChildren<PlayerGameplayInputReader>(true).Length ==
                    expectedReaderCount &&
                presentation.GetComponentsInChildren<PlayerActorDeclaration>(true).Length == 0 &&
                presentation.GetComponentsInChildren<PlayerInput>(true).Length == 0 &&
                presentation.GetComponentsInChildren<PlayerActorRuntimeHost>(true).Length == 0,
                $"{label} Presentation must contain exactly '{expectedReaderCount}' PlayerGameplayInputReader components and no Player Actor infrastructure.");
        }

        private static PlayerActorRuntimeHost ValidateRuntimeHost(GameObject runtimeHostPrefab)
        {
            PlayerActorRuntimeHost runtimeHost =
                runtimeHostPrefab.GetComponent<PlayerActorRuntimeHost>();
            string issue = string.Empty;
            Require(
                runtimeHost != null &&
                runtimeHost.TryValidateConfiguration(out issue),
                $"Player QA Runtime Host prefab is invalid. {issue}");
            Require(
                runtimeHostPrefab.name == "QA_PlayerActorRuntimeHost" &&
                runtimeHostPrefab.GetComponentsInChildren<PlayerInput>(true).Length == 0 &&
                runtimeHostPrefab.GetComponentsInChildren<PlayerGameplayInputReader>(true).Length == 0 &&
                runtimeHostPrefab.GetComponents<PlayerActorRuntimeHost>().Length == 1 &&
                runtimeHostPrefab.GetComponents<PlayerActorDeclaration>().Length == 1 &&
                runtimeHost.PresentationMount != null &&
                runtimeHost.PresentationMount.parent == runtimeHost.transform &&
                runtimeHost.PresentationMount.childCount == 0 &&
                runtimeHost.PlayerActorDeclaration != null,
                "Runtime Host prefab must keep exactly one root Player Actor host and declaration; PlayerInput is absent and Presentation Mount is empty.");
            RequireEmptyAuthoredActorId(
                runtimeHost.PlayerActorDeclaration,
                "Runtime Host prefab");
            return runtimeHost;
        }

        private static void ValidateManagerHost(
            GameObject managerHostPrefab,
            InputActionAsset actions,
            PlayerActorRuntimeHost runtimeHost)
        {
            LocalPlayerHostAuthoring host = RequireSingle<LocalPlayerHostAuthoring>(managerHostPrefab);
            PlayerInput playerInput = RequireSingle<PlayerInput>(managerHostPrefab);
            Require(
                host.TryValidateConfiguration(out string issue),
                $"Manager Local Player Host is invalid. {issue}");
            Require(
                playerInput.actions == actions &&
                playerInput.defaultActionMap == "Gameplay" &&
                host.PlayerActorRuntimeHostPrefab == runtimeHost,
                "Manager Local Player Host lost its Input or Runtime Host prefab reference.");
            Require(
                host.ActorMount.childCount == 0 &&
                managerHostPrefab.GetComponent<SceneProvidedLocalPlayerAuthoring>() == null,
                "Manager Local Player Host must keep an empty Actor Mount and no Scene admission.");
        }

        private static void ValidateSceneHost(
            GameObject sceneHostPrefab,
            InputActionAsset actions,
            PlayerSlotProfile playerOne,
            ActorProfile defaultActor,
            GameObject defaultPresentation,
            GameObject runtimeHostPrefab)
        {
            LocalPlayerHostAuthoring host = RequireSingle<LocalPlayerHostAuthoring>(sceneHostPrefab);
            PlayerInput playerInput = RequireSingle<PlayerInput>(sceneHostPrefab);
            SceneProvidedLocalPlayerAuthoring admission =
                RequireSingle<SceneProvidedLocalPlayerAuthoring>(sceneHostPrefab);
            PlayerActorRuntimeHost sceneRuntimeHost =
                RequireSingleInChildren<PlayerActorRuntimeHost>(host.ActorMount);
            Require(
                playerInput.actions == actions &&
                admission.LocalPlayerHost == host &&
                admission.PlayerSlotProfile == playerOne &&
                admission.ActorProfile == defaultActor,
                "Scene-Provided Local Player canonical intent is incomplete or foreign.");
            Require(
                host.TryValidateAdmissionConfiguration(sceneRuntimeHost, true, out string hostIssue),
                $"Scene-Provided Local Player host is invalid. {hostIssue}");
            Require(
                host.ActorMount.childCount == 1 &&
                sceneRuntimeHost.transform.parent == host.ActorMount,
                "Scene-Provided Runtime Host must be the exact direct Actor Mount child.");
            Require(
                sceneRuntimeHost.PresentationMount != null &&
                sceneRuntimeHost.PresentationMount.parent == sceneRuntimeHost.transform &&
                sceneRuntimeHost.PresentationMount.childCount == 1,
                "Scene-Provided Runtime Host must have one direct Presentation Mount child.");
            GameObject presentation =
                sceneRuntimeHost.PresentationMount.GetChild(0).gameObject;
            Require(
                sceneRuntimeHost.PlayerActorDeclaration != null,
                "Scene-Provided Runtime Host must contain a Player Actor declaration.");
            RequireEmptyAuthoredActorId(
                sceneRuntimeHost.PlayerActorDeclaration,
                "Scene-Provided Player Actor declaration");
            Require(
                SourcePrefab(sceneRuntimeHost.gameObject) == runtimeHostPrefab &&
                SourcePrefab(presentation) == defaultPresentation,
                "Scene-Provided Local Player must retain exact authored Runtime Host and Presentation prefab provenance.");
            SceneProvidedLocalPlayerAuthoringResult validation =
                SceneProvidedLocalPlayerAuthoringUtility.Validate(admission, false);
            Require(
                validation.Succeeded,
                $"Scene-Provided Local Player validation failed. {validation.Message}");
        }

        private static void ValidateSession(
            PlayerSessionProfile profile,
            PlayerSlotProfile playerOne,
            PlayerSlotProfile playerTwo,
            bool initialJoiningOpen,
            PlayerHostProvisioningMode hostProvisioning,
            PlayerActorResolutionPolicy actorResolution)
        {
            Require(profile.TryValidate(out string issue),
                $"Player Session Profile '{profile.name}' is invalid. {issue}");
            Require(
                profile.SupportedSlotCount == 2 &&
                profile.SupportedSlots[0] == playerOne &&
                profile.SupportedSlots[1] == playerTwo &&
                profile.InitialJoiningOpen == initialJoiningOpen &&
                profile.HostProvisioning == hostProvisioning &&
                profile.ActorResolutionPolicy == actorResolution,
                $"Player Session Profile '{profile.name}' lost its authored Slot universe or policies.");
        }

        private static void ValidateExplicitCommandSurface()
        {
            Type[] commands =
            {
                typeof(PlayerSessionOpenJoiningCommandTrigger),
                typeof(PlayerSessionCloseJoiningCommandTrigger),
                typeof(PlayerSessionJoinCommandTrigger),
                typeof(PlayerSessionSelectActorCommandTrigger),
                typeof(PlayerSessionDefaultActorSelectionCommandTrigger),
                typeof(PlayerSessionReplaceActorSelectionCommandTrigger),
                typeof(PlayerSessionClearActorSelectionCommandTrigger),
                typeof(PlayerSessionLeaveCommandTrigger)
            };

            Require(commands.Length == 8,
                "Player Session explicit command surface must remain eight distinct components.");
            var unique = new HashSet<Type>(commands);
            Require(unique.Count == commands.Length,
                "Player Session commands must not collapse into a shared operation enum type.");

            var host = new GameObject("QA Player Command Surface");
            try
            {
                host.SetActive(false);
                for (int index = 0; index < commands.Length; index++)
                {
                    var command = host.AddComponent(commands[index]) as PlayerSessionCommandTriggerBase;
                    Require(command != null, $"Command '{commands[index].Name}' did not materialize.");
                    var serialized = new SerializedObject(command);
                    Require(
                        serialized.FindProperty("operation") == null &&
                        serialized.FindProperty("command") == null,
                        $"Command '{commands[index].Name}' must not serialize an operation enum.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private static void ValidateGameplayInputReader()
        {
            Require(
                typeof(PlayerGameplayInputReader).GetCustomAttributes(
                    typeof(DisallowMultipleComponent), true).Length > 0,
                "PlayerGameplayInputReader must prevent duplicate consumers on one Actor.");
            var host = new GameObject("QA Player Gameplay Input Reader");
            try
            {
                host.SetActive(false);
                PlayerGameplayInputReader reader = host.AddComponent<PlayerGameplayInputReader>();
                Require(
                    host.GetComponent<PlayerActorDeclaration>() == null,
                    "PlayerGameplayInputReader must be authorable without a local PlayerActorDeclaration.");
                var serialized = new SerializedObject(reader);
                Require(
                    serialized.FindProperty("playerInput") == null &&
                    serialized.FindProperty("actions") == null &&
                    !HasSerializedInputOwnershipField(),
                    "PlayerGameplayInputReader must not serialize PlayerInput or a raw Input Action Asset.");
                Require(
                    !reader.HasCurrentGameplayBinding && !reader.GameplayReady,
                    "Unbound PlayerGameplayInputReader must stay fail-closed for binding and readiness.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private static bool HasSerializedInputOwnershipField()
        {
            FieldInfo[] fields = typeof(PlayerGameplayInputReader).GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int index = 0; index < fields.Length; index++)
            {
                FieldInfo field = fields[index];
                bool isSerialized =
                    (!field.IsStatic && field.IsPublic &&
                     !Attribute.IsDefined(field, typeof(NonSerializedAttribute))) ||
                    Attribute.IsDefined(field, typeof(SerializeField));
                if (isSerialized &&
                    (typeof(PlayerInput).IsAssignableFrom(field.FieldType) ||
                     typeof(InputActionAsset).IsAssignableFrom(field.FieldType)))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ValidatePresentationEmbodimentVariation()
        {
            GameObject noBody = CreateRuntimeHost("QA Player Actor no body", false);
            GameObject characterControllerPresentation = CreateRuntimeHost(
                "QA Player Actor presentation controller", true);
            try
            {
                PlayerActorRuntimeHost noBodyHost = noBody.GetComponent<PlayerActorRuntimeHost>();
                Require(
                    noBodyHost.TryValidateConfiguration(out string noBodyIssue),
                    $"Generic Player Actor Runtime Host without a physical body must be valid. {noBodyIssue}");

                PlayerActorRuntimeHost characterControllerPresentationHost =
                    characterControllerPresentation.GetComponent<PlayerActorRuntimeHost>();
                Require(
                    characterControllerPresentationHost.TryValidateConfiguration(
                        out string characterControllerPresentationIssue),
                    "Player Actor Runtime Host must not distinguish a Presentation body technology. " +
                    characterControllerPresentationIssue);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(noBody);
                UnityEngine.Object.DestroyImmediate(characterControllerPresentation);
            }
        }

        private static GameObject CreateRuntimeHost(
            string name,
            bool addPresentationCharacterController)
        {
            var root = new GameObject(name);
            PlayerActorRuntimeHost host = root.AddComponent<PlayerActorRuntimeHost>();
            PlayerActorDeclaration declaration = root.AddComponent<PlayerActorDeclaration>();
            var presentationMount = new GameObject("PresentationMount");
            presentationMount.transform.SetParent(root.transform, false);
            if (addPresentationCharacterController)
            {
                var presentation = new GameObject("Presentation");
                presentation.transform.SetParent(presentationMount.transform, false);
                presentation.AddComponent<CharacterController>();
            }

            var serialized = new SerializedObject(host);
            serialized.FindProperty("playerActorDeclaration").objectReferenceValue = declaration;
            serialized.FindProperty("presentationMount").objectReferenceValue =
                presentationMount.transform;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return root;
        }

        private static T Require<T>(string path)
            where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Require(asset != null,
                $"Required Player QA fixture is missing at '{path}'. Run Configure Player QA.");
            return asset;
        }

        private static void RequireEmptyAuthoredActorId(
            PlayerActorDeclaration declaration,
            string owner)
        {
            Require(declaration != null,
                $"{owner} is missing its Player Actor declaration.");

            var serialized = new SerializedObject(declaration);
            serialized.Update();
            SerializedProperty actorId = serialized.FindProperty("actorId");
            Require(actorId != null,
                $"{owner} Player Actor declaration is missing serialized 'actorId'.");
            Require(string.IsNullOrWhiteSpace(actorId.stringValue),
                $"{owner} Player Actor declaration must keep serialized 'actorId' empty before runtime occurrence identity is established.");
        }

        private static T RequireSingle<T>(GameObject root)
            where T : Component
        {
            T[] components = root.GetComponents<T>();
            Require(
                components.Length == 1,
                $"Fixture '{root.name}' requires exactly one '{typeof(T).Name}', found '{components.Length}'.");
            return components[0];
        }

        private static T RequireSingleInChildren<T>(Transform root)
            where T : Component
        {
            T[] components = root.GetComponentsInChildren<T>(true);
            Require(
                components.Length == 1,
                $"Fixture subtree '{root.name}' requires exactly one '{typeof(T).Name}', found '{components.Length}'.");
            return components[0];
        }

        private static GameObject SourcePrefab(GameObject instance)
        {
            GameObject root = PrefabUtility.GetNearestPrefabInstanceRoot(instance);
            GameObject source = root != null
                ? PrefabUtility.GetCorrespondingObjectFromOriginalSource(root)
                : PrefabUtility.GetCorrespondingObjectFromOriginalSource(instance);
            return source != null ? source.transform.root.gameObject : null;
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
    }
}
