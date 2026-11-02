using System.Collections.Generic;
using System.IO;
using ShearAndGrow.Economy;
using ShearAndGrow.Shearing;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ShearAndGrow.Processing.Editor
{
    /// <summary>
    /// One-click (re)build of the wool-processing MVP content: config & haptic assets, materials, the Hub / WoolWash /
    /// WoolDry scenes and the build order (Hub first). Existing tuned assets are updated in place, never deleted.
    /// </summary>
    public static class WoolProcessingSceneBuilder
    {
        const string Root = "Assets/ShearAndGrow/Features/Processing";
        const string ShearRoot = "Assets/ShearAndGrow/Features/Shearing";
        const string BalancePath = "Assets/ShearAndGrow/Core/Config/GameBalanceData.asset";
        const string ShearingScene = ShearRoot + "/Scenes/Shearing_Feature_Test.unity";

        [MenuItem("Shear & Grow/Processing/Build Hub, Wash & Dry Scenes")]
        public static void BuildAll()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildAllNoPrompt();
        }

        public static void BuildAllNoPrompt()
        {
            foreach (var d in new[] { "/Config", "/Config/Haptics", "/Materials", "/Scenes" }) Directory.CreateDirectory(Root + d);
            AssetDatabase.Refresh();
            var assets = CreateAssets();
            AssetDatabase.SaveAssets(); // persist new configs/materials before scene switches unload them
            BuildProcessingScene(ProcessingStage.Wash, assets);
            BuildProcessingScene(ProcessingStage.Dry, assets);
            BuildHubScene(assets);
            SetBuildOrder();
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(Root + "/Scenes/Hub.unity");
            Debug.Log("[Processing] Built Hub, WoolWash and WoolDry scenes. Build order: Hub, Shearing, WoolWash, WoolDry.");
        }

        // ------------------------------------------------------------------ assets

        sealed class Assets
        {
            public ProcessingConfig Wash, Dry;
            public GameBalanceData Balance;
            public GraphicsQualityConfig Quality;
            public VolumeProfile Volume;
            public HapticsConfig ShearHaptics;
            public Material Wool, WaterDrop, AirStream, Fluff, NozzleBody, Brass, Hose, DryerBody, DryerAccent, Straw, Dust,
                Wood, Wall, Floor, Grate, Rack, Window, Hay, Cloth;
        }

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a == null) { a = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(a, path); }
            return a;
        }

        static Material Mat(string name, string shader)
        {
            string path = Root + "/Materials/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var s = Shader.Find(shader);
            if (m == null) { m = new Material(s); AssetDatabase.CreateAsset(m, path); }
            else if (m.shader != s) m.shader = s;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Soft(string name, Color c, float rim = 0.3f, float spec = 0.12f, float smooth = 0.3f, float metal = 0f)
        {
            var m = Mat(name, "ShearAndGrow/SoftCharacter");
            m.SetColor("_BaseColor", c);
            m.SetColor("_ShadeColor", Color.Lerp(c, new Color(0.45f, 0.32f, 0.3f), 0.45f));
            m.SetFloat("_FuzzStrength", 0f);
            m.SetFloat("_RimStrength", rim);
            m.SetFloat("_SpecStrength", spec);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            m.SetFloat("_Wrap", 0.45f);
            return m;
        }

        static Material Particle(string name, Color tint, Texture2D tex)
        {
            var m = Mat(name, "Universal Render Pipeline/Particles/Unlit");
            if (tex != null) m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", tint);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetOverrideTag("RenderType", "Transparent");
            return m;
        }

        static HapticProfile Profile(string folder, string name, float main, float cont, float sharp, float contact, float chunk, float completion,
            float minInterval, float update, float cooldown, float floor, float android, float ios)
        {
            var p = LoadOrCreate<HapticProfile>(folder + "/" + name + ".asset");
            p.DisplayName = name.Substring(name.LastIndexOf('_') + 1);
            p.MainIntensity = main; p.ContinuousIntensity = cont; p.Sharpness = sharp;
            p.ContactImpactStrength = contact; p.ChunkImpactStrength = chunk; p.CompletionStrength = completion;
            p.CompletionGap = 0.16f; p.MinIntervalBetweenPulses = minInterval; p.ContinuousUpdateInterval = update;
            p.ChunkCooldown = cooldown; p.TextureFloor = floor; p.AndroidScale = android; p.IOSScale = ios;
            EditorUtility.SetDirty(p);
            return p;
        }

        /// <summary>
        /// NewScene(Single) unloads unreferenced, non-dirty assets, which turns cached references into fake nulls.
        /// Re-resolve every path-loaded shared asset after each scene switch.
        /// </summary>
        static void ResolveShared(Assets a)
        {
            a.Balance = AssetDatabase.LoadAssetAtPath<GameBalanceData>(BalancePath);
            a.Quality = AssetDatabase.LoadAssetAtPath<GraphicsQualityConfig>(ShearRoot + "/Config/GraphicsQuality/GraphicsQualityConfig.asset");
            a.Volume = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ShearRoot + "/Config/GraphicsQuality/ShearingVolumeProfile.asset");
            a.ShearHaptics = AssetDatabase.LoadAssetAtPath<HapticsConfig>(ShearRoot + "/Config/Haptics/HapticsConfig.asset");
            a.Fluff = AssetDatabase.LoadAssetAtPath<Material>(ShearRoot + "/Materials/Wool/M_FluffParticle.mat");
            a.Wood = AssetDatabase.LoadAssetAtPath<Material>(ShearRoot + "/Materials/Floors/M_WoodFloor.mat");
            a.Wall = AssetDatabase.LoadAssetAtPath<Material>(ShearRoot + "/Materials/Floors/M_BarnWall.mat");
            a.Floor = a.Wood;
            a.Window = AssetDatabase.LoadAssetAtPath<Material>(ShearRoot + "/Materials/Floors/M_WindowLight.mat");
            a.Hay = AssetDatabase.LoadAssetAtPath<Material>(ShearRoot + "/Materials/Floors/M_Hay.mat");
            a.Wool = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/M_ProcessWool.mat");
            a.Wash = AssetDatabase.LoadAssetAtPath<ProcessingConfig>(Root + "/Config/WashConfig.asset");
            a.Dry = AssetDatabase.LoadAssetAtPath<ProcessingConfig>(Root + "/Config/DryConfig.asset");
            foreach (var (name, val) in new (string, Object)[] { ("Balance", a.Balance), ("Quality", a.Quality), ("Volume", a.Volume), ("ShearHaptics", a.ShearHaptics),
                         ("Fluff", a.Fluff), ("Wood", a.Wood), ("Wall", a.Wall), ("Window", a.Window), ("Hay", a.Hay), ("Wool", a.Wool) })
                if (val == null) Debug.LogError("[Processing] Builder could not load shared asset: " + name);
        }

        static Material LocalMat(string name) => AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/" + name + ".mat");

        /// <summary>Re-resolves everything after NewScene (shared + materials created by this builder).</summary>
        static void Resolve(Assets a)
        {
            ResolveShared(a);
            a.WaterDrop = LocalMat("M_WaterDrop"); a.AirStream = LocalMat("M_AirStream");
            a.NozzleBody = LocalMat("M_NozzleBody"); a.Brass = LocalMat("M_NozzleBrass"); a.Hose = LocalMat("M_Hose");
            a.DryerBody = LocalMat("M_DryerBody"); a.DryerAccent = LocalMat("M_DryerAccent");
            a.Straw = LocalMat("M_Straw"); a.Dust = LocalMat("M_Dust");
            a.Grate = LocalMat("M_Grate"); a.Rack = LocalMat("M_RackWood"); a.Cloth = LocalMat("M_TableCloth");
        }

        static Assets CreateAssets()
        {
            var a = new Assets();
            ResolveShared(a);

            // Haptics: soft & flowing for water, light pulsing & airy for the dryer (Soft / Satisfying / Powerful,
            // same index as the player's chosen profile). Values are starting points for device tuning.
            string hf = Root + "/Config/Haptics";
            var washCfg = LoadOrCreate<HapticsConfig>(hf + "/WashHapticsConfig.asset");
            washCfg.Profiles = new[]
            {
                Profile(hf, "Wash_Soft",       0.55f, 0.40f, 0.18f, 0.45f, 0.55f, 0.70f, 0.09f, 0.12f, 0.5f, 0.45f, 1.1f, 0.9f),
                Profile(hf, "Wash_Satisfying", 0.75f, 0.50f, 0.25f, 0.55f, 0.70f, 0.85f, 0.08f, 0.11f, 0.4f, 0.50f, 1.2f, 0.95f),
                Profile(hf, "Wash_Powerful",   0.90f, 0.62f, 0.32f, 0.70f, 0.85f, 1.00f, 0.07f, 0.10f, 0.35f, 0.55f, 1.25f, 1f),
            };
            washCfg.DefaultProfile = 1; washCfg.StopTimeout = 0.15f;
            EditorUtility.SetDirty(washCfg);
            var dryCfg = LoadOrCreate<HapticsConfig>(hf + "/DryHapticsConfig.asset");
            dryCfg.Profiles = new[]
            {
                Profile(hf, "Dry_Soft",       0.50f, 0.32f, 0.30f, 0.40f, 0.60f, 0.75f, 0.11f, 0.15f, 0.5f, 0.40f, 1.1f, 0.9f),
                Profile(hf, "Dry_Satisfying", 0.70f, 0.42f, 0.38f, 0.50f, 0.75f, 0.90f, 0.10f, 0.14f, 0.4f, 0.45f, 1.2f, 0.95f),
                Profile(hf, "Dry_Powerful",   0.85f, 0.52f, 0.45f, 0.65f, 0.90f, 1.00f, 0.09f, 0.13f, 0.35f, 0.50f, 1.25f, 1f),
            };
            dryCfg.DefaultProfile = 1; dryCfg.StopTimeout = 0.18f;
            EditorUtility.SetDirty(dryCfg);

            a.Wash = LoadOrCreate<ProcessingConfig>(Root + "/Config/WashConfig.asset");
            a.Wash.Stage = ProcessingStage.Wash; a.Wash.Input = WoolType.Dirty; a.Wash.Haptics = washCfg;
            a.Wash.Title = "Wash Wool"; a.Wash.HintText = "Hold & drag to wash"; a.Wash.ProgressLabel = "Washed";
            a.Wash.EmptyText = "No dirty wool yet.\nShear a sheep first!";
            EditorUtility.SetDirty(a.Wash);
            a.Dry = LoadOrCreate<ProcessingConfig>(Root + "/Config/DryConfig.asset");
            a.Dry.Stage = ProcessingStage.Dry; a.Dry.Input = WoolType.Wet; a.Dry.Haptics = dryCfg;
            a.Dry.Title = "Dry Wool"; a.Dry.HintText = "Hold & drag to dry"; a.Dry.ProgressLabel = "Dried";
            a.Dry.EmptyText = "No wet wool yet.\nWash some wool first!";
            EditorUtility.SetDirty(a.Dry);

            var dot = AssetDatabase.LoadAssetAtPath<Texture2D>(ShearRoot + "/Materials/Wool/T_FluffDot.png");
            a.Wool = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/M_ProcessWool.mat") ?? Mat("M_ProcessWool", "ShearAndGrow/ProcessWool");
            a.WaterDrop = Particle("M_WaterDrop", new Color(0.86f, 0.95f, 1f, 0.85f), dot);
            a.AirStream = Particle("M_AirStream", new Color(1f, 1f, 1f, 0.06f), null);
            a.AirStream.SetFloat("_Cull", 0f); // visible from inside the cone too
            a.Fluff = AssetDatabase.LoadAssetAtPath<Material>(ShearRoot + "/Materials/Wool/M_FluffParticle.mat");
            a.NozzleBody = Soft("M_NozzleBody", new Color(0.30f, 0.72f, 0.56f));
            a.Brass = Soft("M_NozzleBrass", new Color(0.95f, 0.74f, 0.36f), 0.35f, 0.6f, 0.65f, 0.6f);
            a.Hose = Soft("M_Hose", new Color(0.28f, 0.62f, 0.44f));
            a.DryerBody = Soft("M_DryerBody", new Color(0.98f, 0.66f, 0.62f), 0.35f, 0.3f, 0.55f);
            a.DryerAccent = Soft("M_DryerAccent", new Color(0.98f, 0.93f, 0.84f), 0.3f, 0.2f, 0.4f);
            a.Straw = Soft("M_Straw", new Color(0.96f, 0.80f, 0.40f), 0.2f);
            a.Dust = Soft("M_Dust", new Color(0.50f, 0.40f, 0.30f), 0.1f, 0.02f, 0.1f);
            a.Wood = AssetDatabase.LoadAssetAtPath<Material>(ShearRoot + "/Materials/Floors/M_WoodFloor.mat");
            a.Wall = AssetDatabase.LoadAssetAtPath<Material>(ShearRoot + "/Materials/Floors/M_BarnWall.mat");
            a.Floor = a.Wood;
            a.Window = AssetDatabase.LoadAssetAtPath<Material>(ShearRoot + "/Materials/Floors/M_WindowLight.mat");
            a.Hay = AssetDatabase.LoadAssetAtPath<Material>(ShearRoot + "/Materials/Floors/M_Hay.mat");
            a.Grate = Soft("M_Grate", new Color(0.36f, 0.34f, 0.33f), 0.2f, 0.4f, 0.5f, 0.4f);
            a.Rack = Soft("M_RackWood", new Color(0.78f, 0.58f, 0.40f), 0.2f, 0.1f, 0.3f);
            a.Cloth = Soft("M_TableCloth", new Color(0.86f, 0.42f, 0.36f), 0.2f, 0.05f, 0.2f);
            return a;
        }

        // ------------------------------------------------------------------ common scene pieces

        static void SetRefs(Object target, params (string field, Object value)[] refs)
        {
            var so = new SerializedObject(target);
            foreach (var (f, v) in refs)
            {
                var p = so.FindProperty(f);
                if (p == null) { Debug.LogError("[Processing] Missing field " + f + " on " + target.GetType().Name); continue; }
                p.objectReferenceValue = v;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static GameObject Prim(string name, PrimitiveType type, Transform parent, Material mat, Vector3 pos, Vector3 euler, Vector3 scale, bool shadows = true)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localEulerAngles = euler;
            go.transform.localScale = scale;
            Object.DestroyImmediate(go.GetComponent<Collider>()); // no physics needed (analytic raycasts)
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            return go;
        }

        static Camera CommonScene(Assets a, string econName = "Economy")
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.80f, 0.76f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.64f, 0.54f, 0.44f);
            RenderSettings.ambientGroundColor = new Color(0.38f, 0.30f, 0.24f);
            RenderSettings.fog = false;

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 40f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 30f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.30f, 0.23f, 0.18f);
            camGo.AddComponent<AudioListener>();
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            var key = new GameObject("Key Light (warm, soft shadows)").AddComponent<Light>();
            key.type = LightType.Directional; key.color = new Color(1f, 0.9f, 0.76f); key.intensity = 1.3f;
            key.shadows = LightShadows.Soft; key.shadowStrength = 0.65f; key.shadowBias = 0.02f; key.shadowNormalBias = 0.3f;
            key.transform.rotation = Quaternion.Euler(52f, -140f, 0f);
            var fill = new GameObject("Fill Light (cool, no shadows)").AddComponent<Light>();
            fill.type = LightType.Point; fill.color = new Color(0.75f, 0.82f, 1f); fill.intensity = 1.2f; fill.range = 6f;
            fill.transform.position = new Vector3(-1.4f, 1.8f, -1.6f);

            var vol = new GameObject("Global Volume").AddComponent<Volume>();
            vol.isGlobal = true; vol.sharedProfile = a.Volume;

            var es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();

            var econ = new GameObject(econName);
            SetRefs(econ.AddComponent<EconomyBootstrap>(), ("_balance", a.Balance));
            return cam;
        }

        static void Barn(Transform env, Assets a, float tableTop)
        {
            Prim("Floor", PrimitiveType.Plane, env, a.Floor, new Vector3(0, -0.85f, 0), Vector3.zero, new Vector3(0.6f, 1, 0.6f));
            Prim("BackWall", PrimitiveType.Cube, env, a.Wall, new Vector3(0, 0.6f, 1.6f), Vector3.zero, new Vector3(6f, 3.2f, 0.1f));
            Prim("Window", PrimitiveType.Cube, env, a.Window, new Vector3(-0.7f, 0.9f, 1.54f), Vector3.zero, new Vector3(0.9f, 0.6f, 0.03f), false);
            Prim("TableTop", PrimitiveType.Cube, env, a.Wood, new Vector3(0, tableTop - 0.03f, 0.05f), Vector3.zero, new Vector3(1.5f, 0.06f, 1.0f));
            Prim("TableApron", PrimitiveType.Cube, env, a.Rack, new Vector3(0, tableTop - 0.1f, -0.44f), Vector3.zero, new Vector3(1.46f, 0.1f, 0.04f));
            Prim("Hay", PrimitiveType.Cube, env, a.Hay, new Vector3(1.05f, tableTop - 0.4f, 1.1f), new Vector3(0, 18f, 0), new Vector3(0.8f, 0.5f, 0.5f));
        }

        // ------------------------------------------------------------------ processing scenes

        static void BuildProcessingScene(ProcessingStage stage, Assets a)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Resolve(a);
            var cam = CommonScene(a);
            bool wash = stage == ProcessingStage.Wash;
            var cfg = wash ? a.Wash : a.Dry;
            const float tableTop = 0f;

            var env = new GameObject("Environment").transform;
            Barn(env, a, tableTop);
            if (wash)
            {
                // shallow wooden tub with a metal grate the water drains through
                Prim("TubRimFront", PrimitiveType.Cube, env, a.Rack, new Vector3(0, 0.025f, -0.31f), Vector3.zero, new Vector3(0.78f, 0.05f, 0.04f));
                Prim("TubRimBack", PrimitiveType.Cube, env, a.Rack, new Vector3(0, 0.025f, 0.31f), Vector3.zero, new Vector3(0.78f, 0.05f, 0.04f));
                Prim("TubRimLeft", PrimitiveType.Cube, env, a.Rack, new Vector3(-0.39f, 0.025f, 0), Vector3.zero, new Vector3(0.04f, 0.05f, 0.66f));
                Prim("TubRimRight", PrimitiveType.Cube, env, a.Rack, new Vector3(0.39f, 0.025f, 0), Vector3.zero, new Vector3(0.04f, 0.05f, 0.66f));
                for (int i = 0; i < 13; i++)
                    Prim("GrateBar_" + i, PrimitiveType.Cube, env, a.Grate, new Vector3(-0.36f + i * 0.06f, 0.004f, 0), Vector3.zero, new Vector3(0.012f, 0.008f, 0.6f), false);
                Prim("Bucket", PrimitiveType.Cylinder, env, a.Grate, new Vector3(0.56f, 0.09f, 0.3f), Vector3.zero, new Vector3(0.16f, 0.09f, 0.16f));
            }
            else
            {
                // drying rack: wooden frame with a taut net under the wool
                Prim("RackFront", PrimitiveType.Cube, env, a.Rack, new Vector3(0, 0.02f, -0.32f), Vector3.zero, new Vector3(0.8f, 0.04f, 0.035f));
                Prim("RackBack", PrimitiveType.Cube, env, a.Rack, new Vector3(0, 0.02f, 0.32f), Vector3.zero, new Vector3(0.8f, 0.04f, 0.035f));
                Prim("RackLeft", PrimitiveType.Cube, env, a.Rack, new Vector3(-0.4f, 0.02f, 0), Vector3.zero, new Vector3(0.035f, 0.04f, 0.68f));
                Prim("RackRight", PrimitiveType.Cube, env, a.Rack, new Vector3(0.4f, 0.02f, 0), Vector3.zero, new Vector3(0.035f, 0.04f, 0.68f));
                for (int i = 0; i < 9; i++)
                {
                    Prim("NetX_" + i, PrimitiveType.Cube, env, a.DryerAccent, new Vector3(-0.36f + i * 0.09f, 0.003f, 0), Vector3.zero, new Vector3(0.004f, 0.004f, 0.62f), false);
                    Prim("NetZ_" + i, PrimitiveType.Cube, env, a.DryerAccent, new Vector3(0, 0.003f, -0.28f + i * 0.07f), Vector3.zero, new Vector3(0.76f, 0.004f, 0.004f), false);
                }
            }

            var heapGo = new GameObject("WoolHeap");
            heapGo.transform.position = new Vector3(0, tableTop + 0.006f, 0);
            var heap = heapGo.AddComponent<WoolVisualStateController>();
            SetRefs(heap, ("_material", a.Wool));

            var toolGo = new GameObject(wash ? "HoseNozzle" : "HairDryer");
            var tool = toolGo.AddComponent<ProcessingToolView>();
            SetRefs(tool, ("_bodyMaterial", wash ? a.NozzleBody : a.DryerBody), ("_accentMaterial", wash ? a.Brass : a.DryerAccent),
                ("_streamMaterial", wash ? a.WaterDrop : a.AirStream), ("_hoseMaterial", a.Hose));

            var systems = new GameObject("ProcessingSystems").transform;
            GameObject Sys(string n) { var g = new GameObject(n); g.transform.SetParent(systems, false); return g; }
            var input = Sys("Input").AddComponent<ToolPointerInput>();
            var vfx = Sys("Vfx").AddComponent<ProcessingVfx>();
            SetRefs(vfx, ("_dropMaterial", a.WaterDrop), ("_fluffMaterial", a.Fluff));
            var audio = Sys("Audio").AddComponent<ProcessingAudioDirector>();
            var haptics = Sys("Haptics").AddComponent<ProcessingHapticsDriver>();
            var hud = Sys("UI").AddComponent<ProcessingHUD>();
            var debris = Sys("Debris").AddComponent<WashDebris>();
            SetRefs(debris, ("_strawMaterial", a.Straw), ("_dustMaterial", a.Dust));
            var session = Sys("Session").AddComponent<WoolProcessingSession>();
            SetRefs(session, ("_config", cfg), ("_quality", a.Quality), ("_camera", cam), ("_heap", heap), ("_tool", tool), ("_input", input),
                ("_vfx", vfx), ("_audio", audio), ("_haptics", haptics), ("_hud", hud), ("_debris", debris));
            // initial camera pose (refit at runtime for the device aspect)
            cam.transform.SetPositionAndRotation(new Vector3(0, 1.7f, -1.0f), Quaternion.Euler(58f, 0, 0));

            string path = Root + "/Scenes/" + (wash ? SceneNavigator.Wash : SceneNavigator.Dry) + ".unity";
            EditorSceneManager.SaveScene(scene, path);
        }

        // ------------------------------------------------------------------ hub

        static void BuildHubScene(Assets a)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Resolve(a);
            var cam = CommonScene(a);
            var env = new GameObject("Environment").transform;
            Barn(env, a, 0f);
            Prim("Cloth", PrimitiveType.Cube, env, a.Cloth, new Vector3(0, 0.001f, 0.05f), Vector3.zero, new Vector3(1.2f, 0.004f, 0.42f), false);

            var showcaseGo = new GameObject("WoolShowcase");
            var showcase = showcaseGo.AddComponent<HubShowcase>();
            var piles = new WoolVisualStateController[3];
            for (int i = 0; i < 3; i++)
            {
                var p = new GameObject(new[] { "DirtyPile", "WetPile", "CleanPile" }[i]);
                p.transform.SetParent(showcaseGo.transform, false);
                p.transform.localPosition = new Vector3((i - 1) * 0.4f, 0.006f, 0.05f);
                piles[i] = p.AddComponent<WoolVisualStateController>();
                SetRefs(piles[i], ("_material", a.Wool));
            }
            var so = new SerializedObject(showcase);
            so.FindProperty("_look").objectReferenceValue = a.Wash;
            so.FindProperty("_material").objectReferenceValue = a.Wool;
            var arr = so.FindProperty("_piles"); arr.arraySize = 3;
            for (int i = 0; i < 3; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = piles[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            // camera: piles sit in the band between the resource chips and the action buttons (portrait)
            cam.fieldOfView = 36f;
            cam.transform.SetPositionAndRotation(new Vector3(0f, 0.95f, -1.55f), Quaternion.Euler(16f, 0f, 0f));

            var systems = new GameObject("HubSystems").transform;
            var uiGo = new GameObject("UI"); uiGo.transform.SetParent(systems, false);
            var ui = uiGo.AddComponent<HubUI>();
            var sell = uiGo.AddComponent<SellPanel>();
            var hapGo = new GameObject("Haptics"); hapGo.transform.SetParent(systems, false);
            var hap = hapGo.AddComponent<ProcessingHapticsDriver>();
            var ctlGo = new GameObject("Hub"); ctlGo.transform.SetParent(systems, false);
            var ctl = ctlGo.AddComponent<HubController>();
            SetRefs(ctl, ("_ui", ui), ("_sell", sell), ("_showcase", showcase), ("_haptics", hap), ("_hapticsConfig", a.ShearHaptics));
            EditorSceneManager.SaveScene(scene, Root + "/Scenes/" + SceneNavigator.Hub + ".unity");
        }

        // ------------------------------------------------------------------ build order

        static void SetBuildOrder()
        {
            var wanted = new List<string>
            {
                Root + "/Scenes/" + SceneNavigator.Hub + ".unity",
                ShearingScene,
                Root + "/Scenes/" + SceneNavigator.Wash + ".unity",
                Root + "/Scenes/" + SceneNavigator.Dry + ".unity",
            };
            var list = new List<EditorBuildSettingsScene>();
            foreach (var p in wanted) list.Add(new EditorBuildSettingsScene(p, true));
            foreach (var s in EditorBuildSettings.scenes)
                if (!wanted.Contains(s.path)) list.Add(s); // keep any other scenes (e.g. SampleScene) after ours
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
