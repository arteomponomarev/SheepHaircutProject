using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ShearAndGrow.Shearing.Editor
{
    /// <summary>
    /// Builds the whole Shearing 2.0 vertical slice: config ScriptableObjects, materials, procedural textures and the
    /// test scene "Shearing_Feature_Test". Re-runnable: existing config assets are kept (tuning is preserved).
    /// </summary>
    public static class ShearingSceneBuilder
    {
        public const string Root = "Assets/ShearAndGrow/Features/Shearing";
        public const string ScenePath = Root + "/Scenes/Shearing_Feature_Test.unity";

        [MenuItem("Shear & Grow/Shearing/Build Feature Test Scene")]
        public static void BuildScene()
        {
            EnsureFolders();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var feel = LoadOrCreate<ShearingFeelConfig>(Root + "/Config/ShearingFeel/ShearingFeelConfig.asset");
            var chunk = LoadOrCreate<ChunkPhysicsConfig>(Root + "/Config/ChunkPhysics/ChunkPhysicsConfig.asset");
            var haptics = LoadOrCreate<HapticsConfig>(Root + "/Config/Haptics/HapticsConfig.asset");
            var audio = LoadOrCreate<ShearingAudioConfig>(Root + "/Config/Audio/ShearingAudioConfig.asset");
            var quality = LoadOrCreate<GraphicsQualityConfig>(Root + "/Config/GraphicsQuality/GraphicsQualityConfig.asset");

            // ---------------- textures
            var woodTex = SaveTexture(GenerateWoodTexture(1024), Root + "/Materials/Floors/T_WoodPlanks.png", true);
            var wallTex = SaveTexture(GenerateWallTexture(512), Root + "/Materials/Floors/T_BarnWall.png", true);
            var dotTex = SaveTexture(GenerateSoftDot(64), Root + "/Materials/Wool/T_FluffDot.png", false);

            // ---------------- materials
            var woolShell = Mat("M_WoolShell", "ShearAndGrow/WoolShell", Root + "/Materials/Wool");
            woolShell.SetColor("_WoolRootColor", new Color(0.80f, 0.72f, 0.63f));
            woolShell.SetColor("_WoolTipColor", new Color(1f, 0.965f, 0.9f));
            woolShell.SetFloat("_Thickness", feel.WoolThickness);
            woolShell.SetFloat("_CurlScale", 10.5f);
            woolShell.SetFloat("_DetailScale", 38f);
            woolShell.SetFloat("_MinDensity", 0.45f);

            var undercoat = Soft("M_Undercoat", new Color(0.93f, 0.85f, 0.76f), Root + "/Materials/Undercoat", fuzz: 0.55f, curl: 55f, fuzzScale: 120f, rim: 0.25f);
            var clump = PuffMat("M_WoolClump", Root + "/Materials/Wool");
            clump.enableInstancing = false;
            var woolSoft = PuffMat("M_WoolDecor", Root + "/Materials/Wool");
            var skin = Soft("M_SheepSkin", new Color(0.97f, 0.86f, 0.78f), Root + "/Materials/Undercoat", rim: 0.35f, shade: new Color(0.8f, 0.58f, 0.55f));
            var earInner = Soft("M_EarInner", new Color(1f, 0.66f, 0.68f), Root + "/Materials/Undercoat", rim: 0.2f);
            var hoof = Soft("M_Hoof", new Color(0.33f, 0.24f, 0.2f), Root + "/Materials/Undercoat", rim: 0.15f, spec: 0.25f, smooth: 0.5f);
            var eyeWhite = Soft("M_EyeWhite", new Color(1f, 1f, 0.98f), Root + "/Materials/Undercoat", rim: 0.05f, spec: 0.4f, smooth: 0.8f, wrap: 0.8f);
            var eyeDark = Soft("M_EyeDark", new Color(0.12f, 0.08f, 0.07f), Root + "/Materials/Undercoat", rim: 0.1f, spec: 1.2f, smooth: 0.92f);
            var nose = Soft("M_Nose", new Color(0.93f, 0.52f, 0.56f), Root + "/Materials/Undercoat", rim: 0.2f, spec: 0.4f, smooth: 0.6f);
            var trimBody = Soft("M_TrimmerBody", new Color(0.2f, 0.25f, 0.34f), Root + "/Materials/Clips", rim: 0.3f, spec: 0.5f, smooth: 0.65f);
            var trimAccent = Soft("M_TrimmerBrass", new Color(0.95f, 0.6f, 0.25f), Root + "/Materials/Clips", rim: 0.3f, spec: 0.9f, smooth: 0.75f, metal: 0.8f);
            var trimMetal = Soft("M_TrimmerComb", new Color(0.78f, 0.8f, 0.83f), Root + "/Materials/Clips", rim: 0.4f, spec: 1.6f, smooth: 0.88f, metal: 1f);
            var trimGrip = Soft("M_TrimmerGrip", new Color(0.12f, 0.13f, 0.16f), Root + "/Materials/Clips", rim: 0.2f, spec: 0.15f, smooth: 0.3f);

            var floorMat = Lit("M_WoodFloor", woodTex, new Color(1f, 0.95f, 0.9f), 0.25f, Root + "/Materials/Floors", new Vector2(3f, 3f));
            var wallMat = Lit("M_BarnWall", wallTex, new Color(0.95f, 0.82f, 0.7f), 0.1f, Root + "/Materials/Floors", new Vector2(4f, 2f));
            var hayMat = Lit("M_Hay", null, new Color(0.93f, 0.78f, 0.42f), 0.05f, Root + "/Materials/Floors", Vector2.one);
            var rugMat = Lit("M_Rug", null, new Color(0.55f, 0.66f, 0.5f), 0.05f, Root + "/Materials/Floors", Vector2.one);
            var leafMat = Lit("M_Leaves", null, new Color(0.45f, 0.62f, 0.35f), 0.1f, Root + "/Materials/Floors", Vector2.one);
            var windowMat = Lit("M_WindowLight", null, new Color(1f, 0.93f, 0.78f), 0f, Root + "/Materials/Floors", Vector2.one);
            windowMat.EnableKeyword("_EMISSION");
            windowMat.SetColor("_EmissionColor", new Color(1f, 0.9f, 0.7f) * 2.2f);
            windowMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            var fluffMat = ParticleMat("M_FluffParticle", dotTex, Root + "/Materials/Wool");
            AssetDatabase.SaveAssets();
            // Re-resolve persisted instances (imports above can replace in-memory objects).
            feel = AssetDatabase.LoadAssetAtPath<ShearingFeelConfig>(Root + "/Config/ShearingFeel/ShearingFeelConfig.asset");
            chunk = AssetDatabase.LoadAssetAtPath<ChunkPhysicsConfig>(Root + "/Config/ChunkPhysics/ChunkPhysicsConfig.asset");
            haptics = AssetDatabase.LoadAssetAtPath<HapticsConfig>(Root + "/Config/Haptics/HapticsConfig.asset");
            audio = AssetDatabase.LoadAssetAtPath<ShearingAudioConfig>(Root + "/Config/Audio/ShearingAudioConfig.asset");
            quality = AssetDatabase.LoadAssetAtPath<GraphicsQualityConfig>(Root + "/Config/GraphicsQuality/GraphicsQualityConfig.asset");

            // ---------------- scene (created at the top so NewScene's unused-asset unload can't invalidate references)

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.78f, 0.74f, 0.7f);
            RenderSettings.ambientEquatorColor = new Color(0.62f, 0.52f, 0.42f);
            RenderSettings.ambientGroundColor = new Color(0.36f, 0.28f, 0.22f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.55f, 0.45f, 0.36f);
            RenderSettings.fogStartDistance = 7f;
            RenderSettings.fogEndDistance = 22f;

            // Camera
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 34f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 60f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.32f, 0.25f, 0.2f);
            camGo.AddComponent<AudioListener>();
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var orbit = camGo.AddComponent<ShearingCameraOrbit>();

            // Lights: big soft warm key, gentle cool fill (ambient trilight does the rest), rim from the shaders
            var key = new GameObject("Key Light (warm, soft shadows)").AddComponent<Light>();
            key.type = LightType.Directional;
            key.color = new Color(1f, 0.9f, 0.76f);
            key.intensity = 1.35f;
            key.shadows = LightShadows.Soft;
            key.shadowStrength = 0.72f;
            key.shadowBias = 0.02f;
            key.shadowNormalBias = 0.3f;
            key.transform.rotation = Quaternion.Euler(48f, -128f, 0f);
            var fill = new GameObject("Fill Light (cool, no shadows)").AddComponent<Light>();
            fill.type = LightType.Point;
            fill.color = new Color(0.75f, 0.82f, 1f);
            fill.intensity = 1.6f;
            fill.range = 9f;
            fill.shadows = LightShadows.None;
            fill.transform.position = new Vector3(-2.2f, 2.2f, -2.6f);
            var rim = new GameObject("Rim Light (warm back)").AddComponent<Light>();
            rim.type = LightType.Point;
            rim.color = new Color(1f, 0.78f, 0.55f);
            rim.intensity = 2.2f;
            rim.range = 6f;
            rim.shadows = LightShadows.None;
            rim.transform.position = new Vector3(1.4f, 2.4f, 2.4f);

            // Post-processing
            var volumeGo = new GameObject("Global Volume");
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = BuildVolumeProfile();

            // Environment
            var env = new GameObject("Environment").transform;
            Prim("WoodFloor", PrimitiveType.Plane, env, floorMat, new Vector3(0, 0, 0), Vector3.zero, new Vector3(1.2f, 1, 1.2f));
            Prim("BackWall", PrimitiveType.Cube, env, wallMat, new Vector3(0, 2.2f, 4.2f), Vector3.zero, new Vector3(12f, 4.4f, 0.2f));
            Prim("SideWall", PrimitiveType.Cube, env, wallMat, new Vector3(4.6f, 2.2f, 0f), Vector3.zero, new Vector3(0.2f, 4.4f, 12f));
            Prim("SideWallL", PrimitiveType.Cube, env, wallMat, new Vector3(-4.6f, 2.2f, 0f), Vector3.zero, new Vector3(0.2f, 4.4f, 12f));
            Prim("Window", PrimitiveType.Cube, env, windowMat, new Vector3(-1.2f, 2.3f, 4.08f), Vector3.zero, new Vector3(1.6f, 1.1f, 0.05f));
            Prim("Rug", PrimitiveType.Cylinder, env, rugMat, new Vector3(0.05f, 0.004f, 0f), Vector3.zero, new Vector3(2.7f, 0.004f, 2.2f));
            Prim("HayBale_1", PrimitiveType.Cube, env, hayMat, new Vector3(-2.6f, 0.35f, 3.1f), new Vector3(0, 12, 0), new Vector3(1.3f, 0.7f, 0.8f));
            Prim("HayBale_2", PrimitiveType.Cube, env, hayMat, new Vector3(-2.3f, 1.02f, 3.2f), new Vector3(0, -5, 0), new Vector3(1.2f, 0.65f, 0.75f));
            Prim("HayBale_3", PrimitiveType.Cube, env, hayMat, new Vector3(3.4f, 0.35f, 2.6f), new Vector3(0, 70, 0), new Vector3(1.3f, 0.7f, 0.8f));
            Prim("Bucket", PrimitiveType.Cylinder, env, hoof, new Vector3(2.2f, 0.22f, 2.9f), Vector3.zero, new Vector3(0.45f, 0.22f, 0.45f));
            Prim("Plant", PrimitiveType.Sphere, env, leafMat, new Vector3(3.6f, 0.6f, 3.6f), Vector3.zero, new Vector3(1.1f, 1.2f, 1.1f));
            foreach (Transform t in env) GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);

            // Sheep
            var sheepGo = new GameObject("SheepRoot (sheep_001)");
            var rigBuilder = sheepGo.AddComponent<SheepRigBuilder>();
            SetRefs(rigBuilder, ("_skinMaterial", skin), ("_earInnerMaterial", earInner), ("_hoofMaterial", hoof), ("_eyeWhiteMaterial", eyeWhite),
                ("_eyeDarkMaterial", eyeDark), ("_noseMaterial", nose), ("_woolSoftMaterial", woolSoft));
            var reactions = sheepGo.AddComponent<SheepReactionAnimator>();
            var pose = sheepGo.AddComponent<SheepPoseController>();
            var woolGo = new GameObject("WoolBody");
            woolGo.transform.SetParent(sheepGo.transform, false);
            var woolVisual = woolGo.AddComponent<WoolVisualRenderer>();
            SetRefs(woolVisual, ("_woolShellMaterial", woolShell), ("_undercoatMaterial", undercoat));

            // Trimmer
            var trimmerGo = new GameObject("Trimmer");
            var trimmer = trimmerGo.AddComponent<TrimmerViewController>();
            SetRefs(trimmer, ("_bodyMaterial", trimBody), ("_accentMaterial", trimAccent), ("_metalMaterial", trimMetal), ("_gripMaterial", trimGrip));

            // Systems
            var systems = new GameObject("ShearingSystems");
            var clumpsGo = new GameObject("WoolClumps (runtime pool)");
            clumpsGo.transform.SetParent(systems.transform, false);
            var simulator = clumpsGo.AddComponent<SoftClumpSimulator>();
            var floorGo = new GameObject("FloorWool (piles)");
            floorGo.transform.SetParent(systems.transform, false);
            var floor = floorGo.AddComponent<FloorWoolAccumulator>();
            SetRefs(floor, ("_pileMaterial", clump));
            var fluffGo = new GameObject("FluffVfx");
            fluffGo.transform.SetParent(systems.transform, false);
            var fluff = fluffGo.AddComponent<WoolFluffVfx>();
            SetRefs(fluff, ("_particleMaterial", fluffMat));
            var audioGo = new GameObject("Audio");
            audioGo.transform.SetParent(systems.transform, false);
            var audioDirector = audioGo.AddComponent<ShearingAudioDirector>();
            var hapticsGo = new GameObject("Haptics");
            hapticsGo.transform.SetParent(systems.transform, false);
            var hapticsController = hapticsGo.AddComponent<ShearingHapticsController>();
            var hapticsPanel = hapticsGo.AddComponent<HapticsDebugPanel>();
            SetRefs(hapticsPanel, ("_haptics", hapticsController));
            var inputGo = new GameObject("Input");
            inputGo.transform.SetParent(systems.transform, false);
            var input = inputGo.AddComponent<ShearingInputAdapter>();
            var uiGo = new GameObject("UI");
            uiGo.transform.SetParent(systems.transform, false);
            var hud = uiGo.AddComponent<ShearingHUD>();
            var debugHud = uiGo.AddComponent<ShearingDebugHUD>();

            var es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();

var sessionGo = new GameObject("ShearingSession");
            sessionGo.transform.SetParent(systems.transform, false);
            var session = sessionGo.AddComponent<ShearingSessionController>();
            SetRefs(session,
                ("_feel", feel), ("_chunkPhysics", chunk), ("_hapticsConfig", haptics), ("_audio", audio), ("_quality", quality),
                ("_camera", cam), ("_sheep", rigBuilder), ("_woolVisual", woolVisual), ("_trimmer", trimmer), ("_input", input),
                ("_orbit", orbit), ("_clumps", simulator), ("_floor", floor), ("_fluff", fluff), ("_audioDirector", audioDirector),
                ("_haptics", hapticsController), ("_reactions", reactions), ("_hud", hud), ("_debugHud", debugHud),
                ("_clumpMaterial", clump), ("_pose", pose),
                ("_defaultState", LoadOrCreate<DefaultGameState>(Root + "/Config/Save/DefaultGameState.asset")));
            SetRefs(orbit, ("_environment", env));
            var devSave = uiGo.AddComponent<DevSaveTools>();
            SetRefs(devSave, ("_session", session));
            var econGo = new GameObject("Economy");
            econGo.transform.SetParent(systems.transform, false);
            SetRefs(econGo.AddComponent<ShearAndGrow.Economy.EconomyBootstrap>(),
                ("_balance", AssetDatabase.LoadAssetAtPath<ShearAndGrow.Economy.GameBalanceData>("Assets/ShearAndGrow/Core/Config/GameBalanceData.asset")));

            int styleCount = System.Enum.GetValues(typeof(WoolLook)).Length;
            var woolStyles = new WoolStylePreset[styleCount];
            bool allStyles = true;
            for (int i = 0; i < styleCount; i++)
            {
                woolStyles[i] = AssetDatabase.LoadAssetAtPath<WoolStylePreset>(Root + "/Config/WoolStyles/" + ((WoolLook)i) + ".asset");
                allStyles &= woolStyles[i] != null;
            }
            if (allStyles)
            {
                var styleGo = new GameObject("Wool Style Selector");
                var switcher = styleGo.AddComponent<WoolStyleSwitcher>();
                SetRefs(switcher, ("_wool", woolVisual), ("_session", session), ("_clumpMaterial", clump), ("_decorMaterial", woolSoft));
                var styleSO = new SerializedObject(switcher);
                var styleArray = styleSO.FindProperty("_presets"); styleArray.arraySize = styleCount;
                for (int i = 0; i < styleCount; i++) styleArray.GetArrayElementAtIndex(i).objectReferenceValue = woolStyles[i];
                styleSO.ApplyModifiedPropertiesWithoutUndo();
            }

            var check = new SerializedObject(session).GetIterator();
            check.NextVisible(true);
            while (check.NextVisible(false))
                if (check.propertyType == SerializedPropertyType.ObjectReference && check.objectReferenceValue == null)
                    Debug.LogError("[Shearing] Session reference not wired: " + check.name);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuild(ScenePath);
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            AssetDatabase.SaveAssets();
            Debug.Log("[Shearing] Built " + ScenePath);
        }

        // ------------------------------------------------------------------ helpers

        static void EnsureFolders()
        {
            string[] folders =
            {
                "Scenes", "Config/ShearingFeel", "Config/ChunkPhysics", "Config/Haptics", "Config/Audio", "Config/GraphicsQuality",
                "Config/Save", "Materials/Wool","Materials/Undercoat", "Materials/Clips", "Materials/Floors",
            };
            foreach (var f in folders) Directory.CreateDirectory(Path.Combine(Root, f));
            AssetDatabase.Refresh();
        }

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a != null) return a;
            a = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(a, path);
            return a;
        }

        static Material Mat(string name, string shader, string folder)
        {
            string path = folder + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var s = Shader.Find(shader);
            if (m == null)
            {
                m = new Material(s);
                AssetDatabase.CreateAsset(m, path);
            }
            else m.shader = s;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Soft(string name, Color c, string folder, float fuzz = 0f, float curl = 30f, float fuzzScale = 40f, float rim = 0.35f,
            float spec = 0.1f, float smooth = 0.2f, float metal = 0f, float wrap = 0.45f, Color? shade = null)
        {
            var m = Mat(name, "ShearAndGrow/SoftCharacter", folder);
            m.SetColor("_BaseColor", c);
            m.SetFloat("_FuzzStrength", fuzz);
            m.SetFloat("_CurlScale", curl);
            m.SetFloat("_FuzzScale", fuzzScale);
            m.SetFloat("_RimStrength", rim);
            m.SetFloat("_SpecStrength", spec);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            m.SetFloat("_Wrap", wrap);
            m.SetColor("_ShadeColor", shade ?? new Color(0.72f, 0.6f, 0.56f));
            return m;
        }

        /// <summary>Cartoon puff wool (reference look): used by dropped clumps, crown and tail.</summary>
        static Material PuffMat(string name, string folder)
        {
            var m = Mat(name, "ShearAndGrow/WoolShell", folder);
            m.SetColor("_WoolRootColor", new Color(0.80f, 0.74f, 0.66f));
            m.SetColor("_WoolTipColor", new Color(1f, 0.985f, 0.955f));
            m.SetFloat("_PuffWrap", 0.75f);
            m.SetFloat("_PuffFuzz", 0.12f);
            m.SetFloat("_PuffFuzzScale", 5f);
            m.SetFloat("_PuffRim", 0.45f);
            m.SetFloat("_PuffAmbient", 1.45f);
            m.SetColor("_PuffScatterColor", new Color(1f, 0.82f, 0.66f));
            m.enableInstancing = false;
            return m;
        }

        static Material Lit(string name, Texture2D tex, Color c, float smooth, string folder, Vector2 tiling)
        {
            var m = Mat(name, "Universal Render Pipeline/Lit", folder);
            m.SetColor("_BaseColor", c);
            if (tex != null)
            {
                m.SetTexture("_BaseMap", tex);
                m.SetTextureScale("_BaseMap", tiling);
            }
            m.SetFloat("_Smoothness", smooth);
            return m;
        }

        static Material ParticleMat(string name, Texture2D tex, string folder)
        {
            var m = Mat(name, "Universal Render Pipeline/Particles/Unlit", folder);
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", Color.white);
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

        static VolumeProfile BuildVolumeProfile()
        {
            string path = Root + "/Config/GraphicsQuality/ShearingVolumeProfile.asset";
            var existing = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (existing != null) return existing;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.Neutral);
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.35f);
            bloom.threshold.Override(1.05f);
            bloom.scatter.Override(0.6f);
            var dof = profile.Add<DepthOfField>(true);
            dof.mode.Override(DepthOfFieldMode.Gaussian);
            dof.gaussianStart.Override(4.6f);
            dof.gaussianEnd.Override(10f);
            dof.gaussianMaxRadius.Override(1.2f);
            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0.22f);
            vig.smoothness.Override(0.5f);
            var ca = profile.Add<ColorAdjustments>(true);
            ca.postExposure.Override(0.15f);
            ca.contrast.Override(6f);
            ca.saturation.Override(4f);
            ca.colorFilter.Override(new Color(1f, 0.97f, 0.93f));
            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        static GameObject Prim(string name, PrimitiveType type, Transform parent, Material mat, Vector3 pos, Vector3 euler, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localEulerAngles = euler;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col); // stage visuals don't need physics; clumps use the pile heightfield
            return go;
        }

        static void SetRefs(Object target, params (string field, Object value)[] refs)
        {
            var so = new SerializedObject(target);
            foreach (var (field, value) in refs)
            {
                var p = so.FindProperty(field);
                if (p == null) { Debug.LogError($"[Shearing] Field {field} not found on {target.GetType().Name}"); continue; }
                p.objectReferenceValue = value;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void AddSceneToBuild(string path)
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == path);
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        static Texture2D SaveTexture(Texture2D tex, string path, bool repeat)
        {
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            imp.alphaIsTransparency = !repeat;
            imp.mipmapEnabled = true;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Texture2D GenerateWoodTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            var px = new Color[size * size];
            int planks = 6;
            var rng = new System.Random(5);
            float[] plankTone = new float[planks];
            float[] plankOffset = new float[planks];
            for (int i = 0; i < planks; i++) { plankTone[i] = 0.85f + (float)rng.NextDouble() * 0.2f; plankOffset[i] = (float)rng.NextDouble() * 10f; }
            Color light = new Color(0.78f, 0.56f, 0.36f), dark = new Color(0.55f, 0.36f, 0.22f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    int p = Mathf.Min(planks - 1, (int)(u * planks));
                    float local = u * planks - p;
                    float grain = Mathf.PerlinNoise(local * 3f + plankOffset[p], v * 40f) * 0.6f + Mathf.PerlinNoise(local * 20f, v * 4f + plankOffset[p]) * 0.4f;
                    float rings = Mathf.Sin((v * 18f + grain * 3f + plankOffset[p]) * Mathf.PI) * 0.5f + 0.5f;
                    Color c = Color.Lerp(dark, light, Mathf.Lerp(grain, rings, 0.35f)) * plankTone[p];
                    float seam = Mathf.SmoothStep(0f, 0.025f, local) * Mathf.SmoothStep(0f, 0.025f, 1f - local);
                    float endSeam = Mathf.Abs(Mathf.Repeat(v + plankOffset[p] * 0.1f, 0.5f) - 0.25f) < 0.003f ? 0.6f : 1f;
                    c *= Mathf.Lerp(0.45f, 1f, seam) * endSeam;
                    c.a = 1f;
                    px[y * size + x] = c;
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        static Texture2D GenerateWallTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            var px = new Color[size * size];
            Color a = new Color(0.62f, 0.42f, 0.3f), b = new Color(0.5f, 0.32f, 0.22f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    float boards = u * 8f;
                    float local = boards - Mathf.Floor(boards);
                    float n = Mathf.PerlinNoise(Mathf.Floor(boards) * 3.1f + local, v * 25f);
                    Color c = Color.Lerp(b, a, n);
                    c *= Mathf.Lerp(0.55f, 1f, Mathf.SmoothStep(0f, 0.04f, local) * Mathf.SmoothStep(0f, 0.04f, 1f - local));
                    c.a = 1f;
                    px[y * size + x] = c;
                }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        static Texture2D GenerateSoftDot(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float wisps = 0.75f + 0.25f * Mathf.Sin(Mathf.Atan2(dy, dx) * 5f);
                    float a = Mathf.Clamp01(1f - d / wisps);
                    px[y * size + x] = new Color(1f, 1f, 1f, a * a);
                }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }
    }
}
