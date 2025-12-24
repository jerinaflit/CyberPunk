using System;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using CyberPunk.Hero;
using CyberPunk.Core;
using UnityEngine.InputSystem;
using UObject = UnityEngine.Object;

namespace CyberPunk.Editor
{
    [InitializeOnLoad]
    public class ProjectFixer : EditorWindow
    {
        static ProjectFixer()
        {
            // Log to confirm the script is compiled and loaded
            Debug.Log("🔧 CyberPunk Tools Loaded. Look for the 'CyberPunk' menu at the top.");
        }

        [MenuItem("CyberPunk/✨ Fix & Optimize Project")]
        public static void ShowWindow()
        {
            GetWindow<ProjectFixer>("Project Fixer");
        }

        private void OnGUI()
        {
            GUILayout.Label("CyberPunk Project Auto-Fixer", EditorStyles.boldLabel);
            GUILayout.Space(10);

            if (GUILayout.Button("1. Reimport All Assets (Apply Rules)", GUILayout.Height(30)))
            {
                ReimportAll();
            }

            if (GUILayout.Button("2. Setup Scene (Hero & Camera)", GUILayout.Height(30)))
            {
                SetupScene();
            }

            if (GUILayout.Button("3. Create/Link Database", GUILayout.Height(30)))
            {
                CreateDatabase();
            }

            if (GUILayout.Button("4. ✨ BUILD FULL SCENE (Rue + Hero)", GUILayout.Height(40)))
            {
                BuildFullScene();
            }

            GUILayout.Space(20);
            GUILayout.Label("Status:", EditorStyles.boldLabel);
            GUILayout.Label("Click a button to perform actions.");
        }

        private static void ReimportAll()
        {
            AssetDatabase.ImportAsset("Assets/_Project", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            Debug.Log("✅ [ProjectFixer] Reimport Complete.");
        }

        private static void SetupScene()
        {
            // Cleanup: remove any Legacy Animation components in the scene (they cause 'must be marked as Legacy' warnings)
            // This typically happens when clips/sprites were dragged into the Hierarchy, creating objects like "hero1_idle1_000".
            foreach (var legacy in UObject.FindObjectsByType<Animation>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (legacy == null) continue;
                var go = legacy.gameObject;
                if (go == null) continue;

                // Only nuke obvious hero garbage, not unrelated legacy uses.
                if (go.name.StartsWith("hero1_", StringComparison.OrdinalIgnoreCase) || go.name.Contains("_idle") || go.name.Contains("_walk") || go.name.Contains("_turn"))
                {
                    UObject.DestroyImmediate(go);
                }
            }

            // 1. Find or Create Hero
            var hero = GameObject.Find("Hero");
            if (hero == null)
            {
                hero = new GameObject("Hero");
                Debug.Log("⚠️ [ProjectFixer] Created new 'Hero' object (was missing).");
            }

            // 2. Setup Hero Components
            var rb = hero.GetComponent<Rigidbody2D>();
            if (rb == null) rb = hero.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;

            // Fix Scale and Sorting Order
            hero.transform.localScale = new Vector3(1.0f, 1.0f, 1f);
            var sr = hero.GetComponent<SpriteRenderer>();
            if (sr == null) sr = hero.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 10; // Ensure in front of everything

            // Position Hero on the street (Lower Y)
            hero.transform.position = new Vector3(-5f, -3.5f, 0f); 

            // RECURSIVELY Remove Legacy Animation components (The root cause of warnings)
            foreach (var legacy in hero.GetComponentsInChildren<Animation>(true))
            {
                UObject.DestroyImmediate(legacy);
            }

            // Remove garbage children created by drag-and-drop (e.g. "hero1_idle1_000")
            // Iterate backwards to avoid modification issues
            for (int i = hero.transform.childCount - 1; i >= 0; i--)
            {
                var child = hero.transform.GetChild(i);
                if (child.name.Contains("hero1_idle") || child.name.Contains("hero1_walk"))
                {
                    UObject.DestroyImmediate(child.gameObject);
                    Debug.Log($"🗑️ [ProjectFixer] Removed garbage child object: {child.name}");
                }
            }

            var controller = hero.GetComponent<SimpleHeroController>();
            if (controller == null) controller = hero.AddComponent<SimpleHeroController>();
            
            // Force assign Animator
            var anim = hero.GetComponent<Animator>();
            if (anim == null) anim = hero.AddComponent<Animator>();
            controller.animator = anim;
            controller.spriteRenderer = sr;
            EditorUtility.SetDirty(controller);

            var input = hero.GetComponent<PlayerInput>();
            if (input == null) input = hero.AddComponent<PlayerInput>();
            
            // Try to load actions
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
            if (actions != null)
            {
                input.actions = actions;
                input.defaultActionMap = "Player";
            }
            else
            {
                Debug.LogWarning("⚠️ [ProjectFixer] Could not find 'InputSystem_Actions.inputactions'.");
            }

            // 3. Setup Camera
            var cam = Camera.main;
            if (cam == null)
            {
                var camObj = new GameObject("Main Camera");
                camObj.tag = "MainCamera";
                cam = camObj.AddComponent<Camera>();
                Debug.Log("⚠️ [ProjectFixer] Created new Main Camera.");
            }
            
            // Center Camera on Hero initially
            cam.transform.position = new Vector3(hero.transform.position.x, hero.transform.position.y, -10f);

            var predCam = cam.GetComponent<PredictiveCamera>();
            if (predCam == null) predCam = cam.gameObject.AddComponent<PredictiveCamera>();
            
            predCam.target = hero.transform;

            Debug.Log("✅ [ProjectFixer] Scene Setup Complete: Hero positioned on street & Camera linked.");
        }

        private static void CreateDatabase()
        {
            var folderPath = "Assets/_Project/Data";
            var assetPath = folderPath + "/HotspotDatabase.asset";

            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                AssetDatabase.CreateFolder("Assets/_Project", "Data");
            }

            var db = AssetDatabase.LoadAssetAtPath<HotspotDatabase>(assetPath);
            if (db == null)
            {
                db = ScriptableObject.CreateInstance<HotspotDatabase>();
                AssetDatabase.CreateAsset(db, assetPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"✅ [ProjectFixer] Created new Database at {assetPath}");
            }
            else
            {
                Debug.Log("ℹ️ [ProjectFixer] Database already exists.");
            }
            
            // Try to assign to BootHotspotDemo
            var boot = UObject.FindFirstObjectByType<BootHotspotDemo>();
            if (boot != null)
            {
                boot.hotspotDatabase = db;
                EditorUtility.SetDirty(boot);
                Debug.Log("✅ [ProjectFixer] Assigned Database to BootHotspotDemo.");
            }
            else
            {
                Debug.LogWarning("⚠️ [ProjectFixer] Could not find 'BootHotspotDemo' in the scene to link the database.");
            }
        }

        private static void BuildFullScene()
        {
            // 1. Setup Basic Scene Elements
            SetupScene(); // Creates Camera & Hero base

            // 2. Build Parallax Background
            var root = GameObject.Find("Environment_Rue");
            if (root != null) UObject.DestroyImmediate(root);
            
            root = new GameObject("Environment_Rue");
            
            // Order: Far -> Near
            CreateParallaxLayer(root, "rue_bg_far",     0.9f, -1);
            CreateParallaxLayer(root, "rue_bg_midfar",  0.7f, 0);
            CreateParallaxLayer(root, "rue_bg_mid",     0.5f, 1);
            CreateParallaxLayer(root, "rue_bg_main",    0.2f, 2); // Main ground usually
            CreateParallaxLayer(root, "rue_fg_static",  0.0f, 5); // Foreground

            // 3. Setup Animator
            SetupHeroAnimator();

            // 4. Fix Mask Texture & Assign to BootHotspotDemo
            FixMaskAndAssign();

            Debug.Log("✨ [ProjectFixer] Full Scene Built! Press Play.");
        }

        private static void FixMaskAndAssign()
        {
            string walkMaskPath = "Assets/_Project/Art/Masks/Walkmasks/rue_mask.png";
            string hotspotMaskPath = "Assets/_Project/Art/Masks/Hotspots/rue_hot.png";

            FixReadableMask(walkMaskPath);
            FixReadableMask(hotspotMaskPath);

            var boot = UObject.FindFirstObjectByType<BootHotspotDemo>();
            if (boot == null)
            {
                var game = GameObject.Find("Game");
                if (game == null) game = new GameObject("Game");
                boot = game.AddComponent<BootHotspotDemo>();
                Debug.Log("✅ [ProjectFixer] Created BootHotspotDemo on 'Game'.");
            }

            var walkTex = AssetDatabase.LoadAssetAtPath<Texture2D>(walkMaskPath);
            var hotTex = AssetDatabase.LoadAssetAtPath<Texture2D>(hotspotMaskPath);
            if (walkTex != null) boot.walkMaskImage = walkTex;
            if (hotTex != null) boot.hotspotMaskImage = hotTex;

            if (boot.hero == null)
                boot.hero = UObject.FindFirstObjectByType<SimpleHeroController>();

            EditorUtility.SetDirty(boot);
            Debug.Log("✅ [ProjectFixer] Assigned rue_mask (walk) + rue_hot (hotspot) to BootHotspotDemo.");
        }

        private static void FixReadableMask(string assetPath)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                Debug.LogWarning($"⚠️ [ProjectFixer] Could not find mask at {assetPath}");
                return;
            }

            bool needs = !importer.isReadable
                         || importer.textureCompression != TextureImporterCompression.Uncompressed
                         || importer.npotScale != TextureImporterNPOTScale.None;

            if (!needs) return;

            importer.isReadable = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.filterMode = FilterMode.Point;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.sRGBTexture = false;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
            Debug.Log($"✅ [ProjectFixer] Fixed mask settings: {assetPath}");
        }

        private static void CreateParallaxLayer(GameObject parent, string texName, float parallaxFactor, int sortOrder)
        {
            // Find texture
            string[] guids = AssetDatabase.FindAssets(texName + " t:Texture2D");
            if (guids.Length == 0)
            {
                Debug.LogWarning($"Could not find texture: {texName}");
                return;
            }
            
            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            
            GameObject go = new GameObject(texName);
            go.transform.SetParent(parent.transform);
            go.transform.localPosition = Vector3.zero;
            
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path); // Assuming single sprite for BG
            if (sr.sprite == null)
            {
                // If texture is not sprite, try to load sub-asset
                var sprites = AssetDatabase.LoadAllAssetsAtPath(path);
                foreach (var s in sprites)
                {
                    if (s is Sprite sp)
                    {
                        sr.sprite = sp;
                        break;
                    }
                }
            }
            sr.sortingOrder = sortOrder;

            var pl = go.AddComponent<ParallaxLayer>();
            pl.parallaxFactor = parallaxFactor;
        }

        private static void SetupHeroAnimator()
        {
            var hero = GameObject.Find("Hero");
            if (hero == null) return;

            var anim = hero.GetComponent<Animator>();
            if (anim == null) anim = hero.AddComponent<Animator>();

            string folderPath = "Assets/_Project/Animations/Characters/hero1";
            string controllerPath = folderPath + "/hero1.controller";

            // 1. Load or Create Controller
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            
            // Check if broken (no layers or broken state machine)
            bool isBroken = controller != null && (controller.layers.Length == 0 || controller.layers[0].stateMachine == null);

            if (controller == null || isBroken)
            {
                Debug.Log("⚠️ [ProjectFixer] Recreating invalid Animator Controller...");
                controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            }

            anim.runtimeAnimatorController = controller;

            // 2. Setup Parameters
            bool hasIsMoving = false;
            foreach(var p in controller.parameters)
                if(p.name == "IsMoving") hasIsMoving = true;

            if (!hasIsMoving)
                controller.AddParameter("IsMoving", AnimatorControllerParameterType.Bool);

            // 3. Setup States & Transitions (Auto-wiring)
            var rootStateMachine = controller.layers[0].stateMachine;

            // Find Clips
            AnimationClip idle1 = AssetDatabase.LoadAssetAtPath<AnimationClip>(folderPath + "/hero1_idle1.anim");
            AnimationClip idle2 = AssetDatabase.LoadAssetAtPath<AnimationClip>(folderPath + "/hero1_idle2.anim");
            AnimationClip walk = AssetDatabase.LoadAssetAtPath<AnimationClip>(folderPath + "/hero1_walk.anim");
            AnimationClip turn = AssetDatabase.LoadAssetAtPath<AnimationClip>(folderPath + "/hero1_turn.anim");

            // Ensure Mecanim (not Legacy)
            if (idle1 != null) { idle1.legacy = false; EditorUtility.SetDirty(idle1); }
            if (idle2 != null) { idle2.legacy = false; EditorUtility.SetDirty(idle2); }
            if (walk != null)  { walk.legacy = false;  EditorUtility.SetDirty(walk); }
            if (turn != null)  { turn.legacy = false;  EditorUtility.SetDirty(turn); }

            if (idle1 == null || walk == null)
            {
                Debug.LogWarning("⚠️ [ProjectFixer] Missing clips: hero1_idle1 and/or hero1_walk. Skipping auto-wiring.");
                return;
            }

            // Create States if they don't exist (We clear and rebuild to be safe)
            rootStateMachine.states = new ChildAnimatorState[0]; 

            var idle1State = rootStateMachine.AddState("Idle1");
            idle1State.motion = idle1;

            AnimatorState idle2State = null;
            if (idle2 != null)
            {
                idle2State = rootStateMachine.AddState("Idle2");
                idle2State.motion = idle2;
            }

            var walkState = rootStateMachine.AddState("Walk");
            walkState.motion = walk;

            AnimatorState turnState = null;
            if (turn != null)
            {
                turnState = rootStateMachine.AddState("Turn");
                turnState.motion = turn;
            }

            rootStateMachine.defaultState = idle1State;

            // Parameters
            EnsureParam(controller, "IsMoving", AnimatorControllerParameterType.Bool);
            EnsureParam(controller, "Idle2", AnimatorControllerParameterType.Bool);
            EnsureParam(controller, "Turn", AnimatorControllerParameterType.Trigger);

            // Idle1 <-> Walk
            var idle1ToWalk = idle1State.AddTransition(walkState);
            idle1ToWalk.AddCondition(AnimatorConditionMode.If, 0, "IsMoving");
            idle1ToWalk.duration = 0;
            idle1ToWalk.hasExitTime = false;

            var walkToIdle1 = walkState.AddTransition(idle1State);
            walkToIdle1.AddCondition(AnimatorConditionMode.IfNot, 0, "IsMoving");
            // Let the current walk cycle finish before returning to Idle.
            walkToIdle1.duration = 0f;
            walkToIdle1.hasExitTime = true;
            walkToIdle1.exitTime = 1f;

            // Idle2 wiring (optional)
            if (idle2State != null)
            {
                var idle1ToIdle2 = idle1State.AddTransition(idle2State);
                idle1ToIdle2.AddCondition(AnimatorConditionMode.If, 0, "Idle2");
                idle1ToIdle2.duration = 0;
                idle1ToIdle2.hasExitTime = false;

                var idle2ToIdle1 = idle2State.AddTransition(idle1State);
                idle2ToIdle1.AddCondition(AnimatorConditionMode.IfNot, 0, "Idle2");
                // Let Idle2 finish before going back to Idle1.
                idle2ToIdle1.duration = 0f;
                idle2ToIdle1.hasExitTime = true;
                idle2ToIdle1.exitTime = 1f;

                // Idle2 -> Walk
                var idle2ToWalk = idle2State.AddTransition(walkState);
                idle2ToWalk.AddCondition(AnimatorConditionMode.If, 0, "IsMoving");
                idle2ToWalk.duration = 0;
                idle2ToWalk.hasExitTime = false;
            }

            // Turn: AnyState -> Turn (trigger), then back to Walk/Idle
            if (turnState != null)
            {
                var anyToTurn = rootStateMachine.AddAnyStateTransition(turnState);
                anyToTurn.AddCondition(AnimatorConditionMode.If, 0, "Turn");
                anyToTurn.duration = 0;
                anyToTurn.hasExitTime = false;

                var turnToWalk = turnState.AddTransition(walkState);
                turnToWalk.AddCondition(AnimatorConditionMode.If, 0, "IsMoving");
                turnToWalk.hasExitTime = true;
                turnToWalk.exitTime = 1f;
                turnToWalk.duration = 0;

                var turnToIdle1 = turnState.AddTransition(idle1State);
                turnToIdle1.AddCondition(AnimatorConditionMode.IfNot, 0, "IsMoving");
                turnToIdle1.hasExitTime = true;
                turnToIdle1.exitTime = 1f;
                turnToIdle1.duration = 0;
            }

            Debug.Log("✅ [ProjectFixer] Animator Repaired & Wired (Idle1/Idle2/Walk/Turn). ");
        }

        private static void EnsureParam(AnimatorController controller, string name, AnimatorControllerParameterType type)
        {
            foreach (var p in controller.parameters)
                if (p.name == name && p.type == type) return;

            controller.AddParameter(name, type);
        }
    }
}
