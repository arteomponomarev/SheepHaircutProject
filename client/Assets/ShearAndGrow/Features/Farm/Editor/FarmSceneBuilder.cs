using System.Collections.Generic;
using System.IO;
using ShearAndGrow.Economy;
using ShearAndGrow.Processing;
using ShearAndGrow.Shearing;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ShearAndGrow.Farm.Editor
{
    /// <summary>
    /// Builds the Farm Hub MVP: building definitions + FarmLayout (data), materials, the procedural cosy farm
    /// (buildings combined per material → few draw calls; decor static-batched), hit areas on the FarmInteractable
    /// layer, one directional light, a farm volume profile without DoF, all runtime systems, and the build order
    /// (FarmHub first). Re-running updates assets in place.
    /// </summary>
    public static partial class FarmSceneBuilder
    {
        const string Root = "Assets/ShearAndGrow/Features/Farm";
        const string ShearRoot = "Assets/ShearAndGrow/Features/Shearing";
        const string ProcRoot = "Assets/ShearAndGrow/Features/Processing";
        const string ScenePath = Root + "/Scenes/" + SceneNavigator.Home + ".unity";

        [MenuItem("Shear & Grow/Farm/Build Farm Hub Scene")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildNoPrompt();
        }

        // ------------------------------------------------------------------ materials

        static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();

        static Material Soft(string name, Color c, float rim = 0.25f, float spec = 0.08f, float smooth = 0.25f, float wrap = 0.5f)
        {
            string path = Root + "/Materials/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var sh = Shader.Find("ShearAndGrow/SoftCharacter");
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            m.shader = sh;
            m.SetColor("_BaseColor", c);
            m.SetColor("_ShadeColor", Color.Lerp(c, new Color(0.42f, 0.32f, 0.36f), 0.42f));
            m.SetColor("_RimColor", new Color(1f, 0.9f, 0.75f));
            m.SetFloat("_RimStrength", rim);
            m.SetFloat("_SpecStrength", spec);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Wrap", wrap);
            m.SetFloat("_FuzzStrength", 0f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            Mats[name] = m;
            return m;
        }

        static Material M(string name) => Mats[name];

        static void CreateMaterials()
        {
            Mats.Clear();
            Soft("Grass", new Color(0.56f, 0.76f, 0.38f), 0.05f, 0.02f, 0.1f, 0.6f);
            Soft("Meadow", new Color(0.47f, 0.66f, 0.33f), 0.0f, 0.0f, 0.1f, 0.6f);
            Soft("Path", new Color(0.86f, 0.74f, 0.55f), 0.05f, 0.02f, 0.1f);
            Soft("PenDirt", new Color(0.72f, 0.82f, 0.45f), 0.05f, 0.02f, 0.1f);
            Soft("Plaster", new Color(0.98f, 0.92f, 0.80f));
            Soft("RoofRed", new Color(0.86f, 0.36f, 0.30f), 0.3f, 0.15f, 0.35f);
            Soft("RoofBlue", new Color(0.36f, 0.58f, 0.80f), 0.3f, 0.15f, 0.35f);
            Soft("RoofDark", new Color(0.45f, 0.32f, 0.28f), 0.3f, 0.12f, 0.3f);
            Soft("BarnRed", new Color(0.78f, 0.30f, 0.24f));
            Soft("Trim", new Color(1f, 0.97f, 0.9f));
            Soft("Wood", new Color(0.74f, 0.52f, 0.33f));
            Soft("WoodDark", new Color(0.52f, 0.35f, 0.22f));
            Soft("Window", new Color(0.55f, 0.78f, 0.92f), 0.4f, 0.5f, 0.7f);
            Soft("Door", new Color(0.55f, 0.36f, 0.24f));
            Soft("Stone", new Color(0.72f, 0.70f, 0.68f));
            Soft("Leaves", new Color(0.38f, 0.66f, 0.34f), 0.2f, 0.03f, 0.15f, 0.65f);
            Soft("LeavesLight", new Color(0.52f, 0.76f, 0.38f), 0.2f, 0.03f, 0.15f, 0.65f);
            Soft("Trunk", new Color(0.55f, 0.38f, 0.26f));
            Soft("FlowerPink", new Color(0.98f, 0.62f, 0.72f));
            Soft("FlowerYellow", new Color(1f, 0.86f, 0.36f));
            Soft("Awning1", new Color(0.98f, 0.55f, 0.35f));
            Soft("Awning2", new Color(1f, 0.95f, 0.85f));
            Soft("Water", new Color(0.50f, 0.78f, 0.95f), 0.5f, 0.8f, 0.8f);
            Soft("Hay", new Color(0.96f, 0.82f, 0.45f));
            Soft("Coin", new Color(1f, 0.82f, 0.3f), 0.4f, 0.6f, 0.7f);
        }

        // ------------------------------------------------------------------ mesh composition

        sealed class Part { public Mesh Mesh; public Matrix4x4 M; public string Mat; }

        sealed class Model
        {
            public readonly List<Part> Parts = new List<Part>();
            public Model Add(Mesh mesh, string mat, Vector3 pos, Vector3 euler, Vector3 scale)
            { Parts.Add(new Part { Mesh = mesh, M = Matrix4x4.TRS(pos, Quaternion.Euler(euler), scale), Mat = mat }); return this; }
            public Model Box(string mat, Vector3 pos, Vector3 size, Vector3 euler = default) => Add(Cube, mat, pos, euler, size);
            public Model Cyl(string mat, Vector3 pos, float r0, float r1, float h, int seg = 12, Vector3 euler = default)
                => Add(ProceduralMeshFactory.Cylinder("cyl", r0, r1, h, seg), mat, pos, euler, Vector3.one);
            public Model Ball(string mat, Vector3 pos, Vector3 radii, int u = 14, int v = 10)
                => Add(ProceduralMeshFactory.Ellipsoid("ball", radii, u, v), mat, pos, Vector3.zero, Vector3.one);
        }

        static Mesh _cube;
        static Mesh Cube => _cube != null ? _cube : (_cube = ProceduralMeshFactory.Box("UnitBox", Vector3.one));

        /// <summary>Combines a model into one mesh per material under <paramref name="parent"/> and saves meshes as assets.</summary>
        static void Realise(Model model, Transform parent, string name, bool castShadows = true)
        {
            var groups = new Dictionary<string, List<(Mesh, Matrix4x4)>>();
            foreach (var p in model.Parts)
            {
                if (!groups.TryGetValue(p.Mat, out var list)) groups[p.Mat] = list = new List<(Mesh, Matrix4x4)>();
                list.Add((p.Mesh, p.M));
            }
            foreach (var kv in groups)
            {
                var mesh = ProceduralMeshFactory.Combine(name + "_" + kv.Key, kv.Value);
                mesh.RecalculateNormals(); // flat-ish but consistent shading after combine
                string path = Root + "/Meshes/" + name + "_" + kv.Key + ".asset";
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(mesh, path);
                var go = new GameObject(kv.Key, typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(parent, false);
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.GetComponent<MeshRenderer>();
                r.sharedMaterial = M(kv.Key);
                r.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            }
        }

        // ------------------------------------------------------------------ building models (local space, front faces -Z = toward camera)

        static Model HouseModel()
        {
            var m = new Model();
            m.Box("Stone", new Vector3(0, 0.1f, 0), new Vector3(3.4f, 0.2f, 2.8f));
            m.Box("Plaster", new Vector3(0, 1.2f, 0), new Vector3(3.0f, 2.0f, 2.4f));
            // gable roof: two slabs + gable triangles approximated by a rotated box
            m.Box("RoofRed", new Vector3(0, 2.62f, -0.66f), new Vector3(3.5f, 0.18f, 1.62f), new Vector3(-36f, 0, 0));
            m.Box("RoofRed", new Vector3(0, 2.62f, 0.66f), new Vector3(3.5f, 0.18f, 1.62f), new Vector3(36f, 0, 0));
            m.Box("Plaster", new Vector3(0, 2.4f, 0), new Vector3(2.96f, 0.9f, 0.9f), new Vector3(45f, 0, 0));
            m.Box("Stone", new Vector3(0.95f, 3.05f, 0.45f), new Vector3(0.38f, 0.9f, 0.38f));
            m.Box("Door", new Vector3(0, 0.75f, -1.21f), new Vector3(0.7f, 1.1f, 0.06f));
            m.Box("Trim", new Vector3(0, 1.34f, -1.22f), new Vector3(0.84f, 0.1f, 0.06f));
            foreach (float x in new[] { -0.95f, 0.95f })
            {
                m.Box("Window", new Vector3(x, 1.25f, -1.21f), new Vector3(0.6f, 0.55f, 0.05f));
                m.Box("Trim", new Vector3(x, 0.95f, -1.24f), new Vector3(0.72f, 0.08f, 0.1f));
                m.Box("FlowerPink", new Vector3(x, 0.9f, -1.33f), new Vector3(0.6f, 0.14f, 0.16f));
            }
            m.Box("Wood", new Vector3(0, 0.22f, -1.55f), new Vector3(1.2f, 0.12f, 0.6f)); // porch step
            return m;
        }

        static Model BarnModel()
        {
            var m = new Model();
            m.Box("BarnRed", new Vector3(0, 1.3f, 0), new Vector3(3.2f, 2.6f, 2.8f));
            m.Box("RoofDark", new Vector3(0, 2.9f, -0.75f), new Vector3(3.5f, 0.2f, 1.85f), new Vector3(-32f, 0, 0));
            m.Box("RoofDark", new Vector3(0, 2.9f, 0.75f), new Vector3(3.5f, 0.2f, 1.85f), new Vector3(32f, 0, 0));
            m.Box("BarnRed", new Vector3(0, 2.75f, 0), new Vector3(3.16f, 1.0f, 1.0f), new Vector3(45f, 0, 0));
            m.Box("Door", new Vector3(0, 0.95f, -1.41f), new Vector3(1.5f, 1.9f, 0.06f));
            m.Box("Trim", new Vector3(0, 0.95f, -1.45f), new Vector3(1.62f, 0.1f, 0.06f));
            m.Box("Trim", new Vector3(0, 0.95f, -1.45f), new Vector3(0.1f, 1.95f, 0.06f));
            m.Box("Trim", new Vector3(0, 0.95f, -1.46f), new Vector3(2.3f, 0.09f, 0.05f), new Vector3(0, 0, 48f));
            m.Box("Trim", new Vector3(0, 0.95f, -1.46f), new Vector3(2.3f, 0.09f, 0.05f), new Vector3(0, 0, -48f));
            m.Box("Window", new Vector3(0, 2.35f, -1.41f), new Vector3(0.6f, 0.5f, 0.05f));
            m.Box("Hay", new Vector3(1.85f, 0.35f, -0.9f), new Vector3(0.7f, 0.7f, 1.0f));
            m.Box("Hay", new Vector3(1.85f, 1.0f, -0.7f), new Vector3(0.6f, 0.6f, 0.8f), new Vector3(0, 12f, 0));
            return m;
        }

        static Model PenFence(float w, float d)
        {
            var m = new Model();
            float hw = w * 0.5f, hd = d * 0.5f;
            for (float x = -hw; x <= hw + 0.01f; x += w / 6f)
            {
                m.Box("Trim", new Vector3(x, 0.35f, -hd), new Vector3(0.12f, 0.7f, 0.12f));
                if (Mathf.Abs(x) > 0.6f) m.Box("Trim", new Vector3(x, 0.35f, hd), new Vector3(0.12f, 0.7f, 0.12f));
            }
            for (float z = -hd; z <= hd + 0.01f; z += d / 4f)
            {
                m.Box("Trim", new Vector3(-hw, 0.35f, z), new Vector3(0.12f, 0.7f, 0.12f));
                m.Box("Trim", new Vector3(hw, 0.35f, z), new Vector3(0.12f, 0.7f, 0.12f));
            }
            foreach (float y in new[] { 0.28f, 0.56f })
            {
                m.Box("Trim", new Vector3(0, y, -hd), new Vector3(w, 0.07f, 0.05f));
                m.Box("Trim", new Vector3(-hw, y, 0), new Vector3(0.05f, 0.07f, d));
                m.Box("Trim", new Vector3(hw, y, 0), new Vector3(0.05f, 0.07f, d));
                m.Box("Trim", new Vector3(-hw * 0.6f - 0.3f, y, hd), new Vector3(w * 0.4f, 0.07f, 0.05f));
                m.Box("Trim", new Vector3(hw * 0.6f + 0.3f, y, hd), new Vector3(w * 0.4f, 0.07f, 0.05f));
            }
            m.Box("Wood", new Vector3(-hw + 0.6f, 0.25f, -hd + 0.5f), new Vector3(0.9f, 0.3f, 0.4f)); // feeding trough
            m.Box("Water", new Vector3(-hw + 0.6f, 0.38f, -hd + 0.5f), new Vector3(0.75f, 0.04f, 0.28f));
            return m;
        }

        static Model ShedModel()
        {
            var m = new Model();
            m.Box("Stone", new Vector3(0, 0.08f, 0), new Vector3(3.4f, 0.16f, 2.6f));
            foreach (var p in new[] { new Vector3(-1.5f, 0, -1.1f), new Vector3(1.5f, 0, -1.1f), new Vector3(-1.5f, 0, 1.1f), new Vector3(1.5f, 0, 1.1f) })
                m.Box("Wood", p + new Vector3(0, 1.15f, 0), new Vector3(0.18f, 2.3f, 0.18f));
            m.Box("Wood", new Vector3(0, 1.4f, 1.12f), new Vector3(3.1f, 2.5f, 0.12f)); // back wall
            m.Box("RoofBlue", new Vector3(0, 2.5f, 0), new Vector3(3.7f, 0.18f, 3.0f), new Vector3(-12f, 0, 0));
            m.Box("Trim", new Vector3(0, 2.33f, -1.42f), new Vector3(3.7f, 0.12f, 0.08f), new Vector3(-12f, 0, 0));
            // washing tub with water
            m.Cyl("WoodDark", new Vector3(-0.75f, 0.16f, -0.1f), 0.62f, 0.7f, 0.55f, 16);
            m.Cyl("Water", new Vector3(-0.75f, 0.68f, -0.1f), 0.6f, 0.6f, 0.03f, 16);
            // drying rack
            m.Box("Wood", new Vector3(0.9f, 0.55f, 0f), new Vector3(1.1f, 0.08f, 0.9f));
            foreach (var x in new[] { 0.4f, 1.4f }) foreach (var z in new[] { -0.4f, 0.4f })
                m.Box("Wood", new Vector3(x, 0.3f, z), new Vector3(0.07f, 0.5f, 0.07f));
            // sign
            m.Box("Trim", new Vector3(0, 2.0f, -1.2f), new Vector3(1.4f, 0.4f, 0.06f));
            m.Box("Water", new Vector3(0, 2.0f, -1.24f), new Vector3(0.3f, 0.3f, 0.04f), new Vector3(0, 0, 45f));
            return m;
        }

        static Model TraderModel()
        {
            var m = new Model();
            m.Box("Wood", new Vector3(0, 0.55f, 0), new Vector3(2.2f, 1.1f, 0.9f));
            m.Box("Trim", new Vector3(0, 1.12f, 0), new Vector3(2.4f, 0.08f, 1.0f));
            foreach (var x in new[] { -1.05f, 1.05f })
            {
                m.Box("WoodDark", new Vector3(x, 1.25f, -0.4f), new Vector3(0.12f, 2.5f, 0.12f));
                m.Box("WoodDark", new Vector3(x, 1.25f, 0.4f), new Vector3(0.12f, 2.5f, 0.12f));
            }
            for (int i = 0; i < 6; i++)
                m.Box(i % 2 == 0 ? "Awning1" : "Awning2", new Vector3(-1.1f + i * 0.44f + 0.22f, 2.42f, -0.15f), new Vector3(0.44f, 0.1f, 1.5f), new Vector3(-16f, 0, 0));
            m.Box("Awning1", new Vector3(0, 2.15f, -0.92f), new Vector3(2.64f, 0.3f, 0.05f), new Vector3(-16f, 0, 0));
            m.Box("Wood", new Vector3(1.6f, 0.3f, -0.4f), new Vector3(0.6f, 0.6f, 0.6f));
            m.Box("Wood", new Vector3(1.55f, 0.85f, -0.35f), new Vector3(0.5f, 0.5f, 0.5f), new Vector3(0, 15f, 0));
            m.Cyl("Coin", new Vector3(0, 2.75f, -0.62f), 0.3f, 0.3f, 0.08f, 16, new Vector3(-74f, 0, 0));
            m.Box("Hay", new Vector3(-1.55f, 0.25f, -0.35f), new Vector3(0.5f, 0.5f, 0.7f));
            return m;
        }

        static Model TreeModel(float h, int seed)
        {
            var rng = new System.Random(seed);
            var m = new Model();
            m.Cyl("Trunk", Vector3.zero, 0.18f, 0.13f, h * 0.45f, 8);
            m.Ball("Leaves", new Vector3(0, h * 0.62f, 0), new Vector3(0.9f, 0.85f, 0.9f) * (h / 2.4f));
            m.Ball("LeavesLight", new Vector3(0.35f * (float)rng.NextDouble(), h * 0.85f, -0.2f), new Vector3(0.6f, 0.55f, 0.6f) * (h / 2.4f));
            return m;
        }

        // ------------------------------------------------------------------ scene

        static GameObject Hit(Transform parent, Vector3 centre, Vector3 size, int layer)
        {
            var go = new GameObject("HitArea");
            go.layer = layer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = centre;
            var bc = go.AddComponent<BoxCollider>();
            bc.size = size;
            bc.isTrigger = true; // tap target only, never simulated
            return go;
        }

        static T Interactable<T>(string id, string label, Model model, Vector3 pos, float yaw, Vector3 hitCentre, Vector3 hitSize, float labelHeight, Transform parent, int layer) where T : FarmInteractable
        {
            var root = new GameObject(label);
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            var view = new GameObject("View").transform;
            view.SetParent(root.transform, false);
            Realise(model, view, id);
            Hit(root.transform, hitCentre, hitSize, layer);
            var anchor = new GameObject("LabelAnchor").transform;
            anchor.SetParent(root.transform, false);
            anchor.localPosition = new Vector3(0, labelHeight, 0);
            var it = root.AddComponent<T>();
            it.Configure(id, label, view, anchor);
            return it;
        }

        static int EnsureLayer(string name)
        {
            int existing = LayerMask.NameToLayer(name);
            if (existing >= 0) return existing;
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            for (int i = 8; i < 32; i++)
            {
                var p = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(p.stringValue)) { p.stringValue = name; tagManager.ApplyModifiedProperties(); return i; }
            }
            Debug.LogError("[Farm] No free layer for " + name);
            return 0;
        }

        static FarmBuildingDefinition Def(string id, string name, string desc, FarmBuildingKind kind, Vector2Int footprint)
        {
            string path = Root + "/Config/Buildings/" + id + ".asset";
            var d = AssetDatabase.LoadAssetAtPath<FarmBuildingDefinition>(path);
            if (d == null) { d = ScriptableObject.CreateInstance<FarmBuildingDefinition>(); AssetDatabase.CreateAsset(d, path); }
            d.Id = id; d.DisplayName = name; d.Description = desc; d.Kind = kind; d.Footprint = footprint; d.StartLevel = 1; d.MaxLevel = kind == FarmBuildingKind.House ? 5 : 1;
            EditorUtility.SetDirty(d);
            return d;
        }

        static VolumeProfile FarmVolume()
        {
            string path = Root + "/Config/FarmVolumeProfile.asset";
            var src = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ShearRoot + "/Config/GraphicsQuality/ShearingVolumeProfile.asset");
            var p = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (p == null) { p = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(p, path); }
            foreach (var c in p.components.ToArray()) { p.components.Remove(c); Object.DestroyImmediate(c, true); }
            var tone = p.Add<Tonemapping>(true); tone.mode.Override(TonemappingMode.Neutral);
            var col = p.Add<ColorAdjustments>(true); col.saturation.Override(12f); col.postExposure.Override(0.1f); col.contrast.Override(4f);
            var vig = p.Add<Vignette>(true); vig.intensity.Override(0.18f); vig.smoothness.Override(0.6f);
            var bloom = p.Add<Bloom>(true); bloom.intensity.Override(0.15f); bloom.threshold.Override(1.1f);
            foreach (var c in p.components) { c.name = c.GetType().Name; AssetDatabase.AddObjectToAsset(c, p); }
            EditorUtility.SetDirty(p);
            return p;
        }

        static Texture2D RingTexture()
        {
            string path = Root + "/Materials/T_SelectRing.png";
            const int n = 128;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = new Vector2(x + 0.5f - n * 0.5f, y + 0.5f - n * 0.5f).magnitude / (n * 0.5f);
                    float ring = Mathf.Clamp01(1f - Mathf.Abs(d - 0.86f) / 0.1f);
                    float fill = d < 0.86f ? 0.12f * d : 0f;
                    t.SetPixel(x, y, new Color(1f, 0.9f, 0.55f, Mathf.Max(ring * ring, fill)));
                }
            t.Apply();
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.alphaIsTransparency = true; imp.mipmapEnabled = false; imp.wrapMode = TextureWrapMode.Clamp;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        public static void BuildNoPrompt()
        {
            foreach (var d in new[] { "/Config", "/Config/Buildings", "/Materials", "/Meshes", "/Scenes", "/Prefabs", "/Prefabs/Buildings", "/Prefabs/Decor", "/Art", "/Art/Icons" }) Directory.CreateDirectory(Root + d);
            AssetDatabase.Refresh();
            int layer = EnsureLayer(FarmInteractable.LayerName);
            CreateMaterials();
            CreateBuildingMaterials();
            var softDot = SoftDotTexture();
            var ghostMat = Transparent("M_Ghost", new Color(0.6f, 1f, 0.65f, 0.6f));
            var gridMat = Transparent("M_BuildGrid", Color.white);
            var footMat = Transparent("M_Footprint", new Color(0.45f, 1f, 0.55f, 0.5f));
            var particleMat = Transparent("M_SiteParticles", Color.white, softDot, "Universal Render Pipeline/Particles/Unlit");

            // ---- visual prefabs (pivot = footprint centre; stage groups for the construction animation)
            // Barn footprint 5×8 covers the barn (+z) and its sheep pen (−z): pen moves with the barn.
            var barnBody = new Vector3(0f, 0f, 2.4f);
            var penLocal = new Vector3(0f, 0f, -2.0f);
            var housePrefab = BuildingPrefab<HouseInteraction>("farmhouse", "Farmer House", HouseStages(), new[] { (new Vector3(0, 1.6f, 0), new Vector3(3.8f, 3.4f, 3.4f)) }, new Vector3(0, 4.3f, 0), layer, true, HouseLevel2());
            var barnPrefab = BuildingPrefab<BarnInteraction>("barn", "Barn & Sheep Pen", BarnStages(barnBody, penLocal),
                new[] { (barnBody + new Vector3(0, 1.5f, 0), new Vector3(3.6f, 3.2f, 3.2f)), (penLocal + new Vector3(0, 0.05f, 0), new Vector3(4.6f, 0.1f, 3.4f)) }, barnBody + new Vector3(0, 4.3f, 0), layer, true, BarnLevel2(barnBody, penLocal));
            var workshopPrefab = BuildingPrefab<TrimmerWorkshopInteraction>("trimmer_workshop", "Trimmer Workshop", WorkshopStages(), new[] { (new Vector3(0, 1.2f, 0), new Vector3(2.8f, 2.6f, 2.6f)) }, new Vector3(0, 3.0f, 0), layer);
            var stationPrefab = BuildingPrefab<FeedingStationInteraction>("feeding_station", "Feeding Station", FeedingStationStages(), new[] { (new Vector3(0, 0.9f, 0), new Vector3(2.8f, 1.9f, 1.9f)) }, new Vector3(0, 2.3f, 0), layer);
            var sheepPrefab = SheepPrefab(layer);
            var shedPrefab = BuildingPrefab<ProcessingShedInteraction>("woolshed", "Wool Processing", ShedStages(), new[] { (new Vector3(0, 1.3f, 0), new Vector3(3.6f, 2.8f, 2.8f)) }, new Vector3(0, 3.3f, 0), layer);
            var traderPrefab = BuildingPrefab<TraderInteraction>("trader", "Trader Stall", TraderStages(), new[] { (new Vector3(0, 1.3f, 0), new Vector3(3f, 2.8f, 1.8f)) }, new Vector3(0, 3.2f, 0), layer);
            var treePrefab = DecorPrefab("decor_tree", "Small Tree", DecorTree(), new Vector3(0.9f, 1.5f, 0.9f), layer);
            var bedPrefab = DecorPrefab("decor_flowerbed", "Flower Bed", DecorFlowerBed(), new Vector3(0.95f, 0.4f, 0.95f), layer);
            var stonePrefab = DecorPrefab("decor_stone", "Decorative Stone", DecorStone(), new Vector3(0.9f, 0.5f, 0.9f), layer);
            var statuePrefab = DecorPrefab("decor_sheepstatue", "Sheep Statue", DecorSheepStatue(), new Vector3(1.8f, 1.3f, 1.8f), layer);
            FenceModels(out var fC, out var fN, out var fS);
            var fencePrefab = ConnectablePrefab("decor_fence", "Wooden Fence", fC, fN, fS, new Vector3(0.9f, 0.8f, 0.9f), layer);
            PathModels(out var pC, out var pN);
            var pathPrefab = ConnectablePrefab("decor_path", "Path Tile", pC, pN, null, new Vector3(0.95f, 0.1f, 0.95f), layer);
            var builderPrefab = BuilderPrefab();
            var sitePrefab = SitePrefab(particleMat);

            // ---- data (definitions = the configurable catalog; prices / times / sizes are balance values)
            var house = Define("farmhouse", "Farmer House", "The heart of your farm. Upgrades come later.", FarmBuildingKind.House, BuildingCategory.Building, BuildingInteraction.FarmerHouse, new Vector2Int(4, 4), 0, 0f, 1, true, false, false, ConnectMode.None, housePrefab);
            house.MaxLevel = 8;
            var barn = Define("barn", "Barn & Sheep Pen", "Home of your sheep. The pen moves with the barn.", FarmBuildingKind.Barn, BuildingCategory.Building, BuildingInteraction.Barn, new Vector2Int(5, 8), 0, 0f, 1, true, false, false, ConnectMode.None, barnPrefab);
            var shed = Define("woolshed", "Wool Processing", "Wash and dry your wool here – clean wool sells for much more.", FarmBuildingKind.ProcessingShed, BuildingCategory.Building, BuildingInteraction.WoolProcessing, new Vector2Int(4, 3), 80, 8f, 1, true, false, true, ConnectMode.None, shedPrefab);
            var trader = Define("trader", "Trader Stall", "Sells your wool for coins. Stays by the road.", FarmBuildingKind.Trader, BuildingCategory.Building, BuildingInteraction.Trader, new Vector2Int(3, 2), 0, 0f, 1, false, false, false, ConnectMode.None, traderPrefab);
            var dTree = Define("decor_tree", "Small Tree", "A young tree for some shade.", FarmBuildingKind.Decoration, BuildingCategory.Decoration, BuildingInteraction.None, Vector2Int.one, 5, 0f, 0, true, true, true, ConnectMode.None, treePrefab);
            var dBed = Define("decor_flowerbed", "Flower Bed", "Colourful flowers in a wooden box.", FarmBuildingKind.Decoration, BuildingCategory.Decoration, BuildingInteraction.None, Vector2Int.one, 8, 0f, 0, true, true, true, ConnectMode.None, bedPrefab);
            var dStone = Define("decor_stone", "Decorative Stone", "Mossy garden stones.", FarmBuildingKind.Decoration, BuildingCategory.Decoration, BuildingInteraction.None, Vector2Int.one, 4, 0f, 0, true, true, true, ConnectMode.None, stonePrefab);
            var dFence = Define("decor_fence", "Wooden Fence", "Fence segment – neighbours connect automatically.", FarmBuildingKind.Decoration, BuildingCategory.Decoration, BuildingInteraction.None, Vector2Int.one, 2, 0f, 0, true, true, true, ConnectMode.Fence, fencePrefab);
            var dPath = Define("decor_path", "Path Tile", "Sandy path tile – neighbours connect automatically.", FarmBuildingKind.Decoration, BuildingCategory.Decoration, BuildingInteraction.None, Vector2Int.one, 1, 0f, 0, true, true, true, ConnectMode.Path, pathPrefab);
            var dStatue = Define("decor_sheepstatue", "Sheep Statue", "A proud stone sheep for your farm.", FarmBuildingKind.Decoration, BuildingCategory.Decoration, BuildingInteraction.None, new Vector2Int(2, 2), 25, 0f, 0, true, true, true, ConnectMode.None, statuePrefab);
            house.Description = "The heart of your farm. Its level unlocks new buildings and upgrades.";
            barn.MaxLevel = 2;
            barn.UpgradeLevels = new[] { new BuildingLevel { Level = 2, RequiredFarmerHouseLevel = 2, Cost = 250, Seconds = 15f, Benefit = "Room for 4 sheep" } };
            EditorUtility.SetDirty(house); EditorUtility.SetDirty(barn);
            var workshop = Define("trimmer_workshop", "Trimmer Workshop", "Upgrade your trimmer for a wider cutting head.", FarmBuildingKind.ProcessingShed, BuildingCategory.Building, BuildingInteraction.TrimmerWorkshop, new Vector2Int(3, 3), 180, 12f, 1, true, false, true, ConnectMode.None, workshopPrefab);
            workshop.RequiredFarmerHouseLevel = 2; EditorUtility.SetDirty(workshop);
            var station = Define("feeding_station", "Feeding Station", "Treat a sheep to make its wool grow back faster for a while.", FarmBuildingKind.ProcessingShed, BuildingCategory.Building, BuildingInteraction.FeedingStation, new Vector2Int(3, 2), 120, 10f, 1, true, false, true, ConnectMode.None, stationPrefab);
            station.RequiredFarmerHouseLevel = 2; EditorUtility.SetDirty(station);
            var allDefs = new[] { house, barn, shed, trader, workshop, station, dTree, dBed, dStone, dFence, dPath, dStatue };
            var progression = AssetDatabase.LoadAssetAtPath<FarmProgressionConfig>(Root + "/Config/FarmProgressionConfig.asset");
            if (progression == null) { progression = ScriptableObject.CreateInstance<FarmProgressionConfig>(); AssetDatabase.CreateAsset(progression, Root + "/Config/FarmProgressionConfig.asset"); }
            var toolsPath = ShearRoot + "/Config/Tools/ToolUpgradeConfig.asset";
            Directory.CreateDirectory(ShearRoot + "/Config/Tools");
            var tools = AssetDatabase.LoadAssetAtPath<ShearAndGrow.Shearing.ToolUpgradeConfig>(toolsPath);
            if (tools == null) { tools = ScriptableObject.CreateInstance<ShearAndGrow.Shearing.ToolUpgradeConfig>(); AssetDatabase.CreateAsset(tools, toolsPath); }

            var catalog = AssetDatabase.LoadAssetAtPath<BuildingCatalog>(Root + "/Config/BuildingCatalog.asset");
            if (catalog == null) { catalog = ScriptableObject.CreateInstance<BuildingCatalog>(); AssetDatabase.CreateAsset(catalog, Root + "/Config/BuildingCatalog.asset"); }
            catalog.Items = new[] { shed, workshop, station, house, barn, trader, dTree, dBed, dStone, dFence, dPath, dStatue };
            EditorUtility.SetDirty(catalog);
            var cConfig = AssetDatabase.LoadAssetAtPath<ConstructionConfig>(Root + "/Config/ConstructionConfig.asset");
            if (cConfig == null) { cConfig = ScriptableObject.CreateInstance<ConstructionConfig>(); AssetDatabase.CreateAsset(cConfig, Root + "/Config/ConstructionConfig.asset"); }
            EditorUtility.SetDirty(cConfig);

            var layout = AssetDatabase.LoadAssetAtPath<FarmLayout>(Root + "/Config/FarmLayout.asset");
            if (layout == null) { layout = ScriptableObject.CreateInstance<FarmLayout>(); AssetDatabase.CreateAsset(layout, Root + "/Config/FarmLayout.asset"); }
            layout.LayoutVersion = 2;
            layout.CellSize = 1f;
            layout.SizeCells = new Vector2Int(16, 24);
            layout.Definitions = allDefs;
            // starting farm of a NEW player (cells = footprint min corner); the processing building is NOT included
            layout.StartingBuildings = new List<StartingBuilding>
            {
                new StartingBuilding { InstanceId = "start_house", TypeId = house.Id, Cell = new Vector2Int(2, 16) },
                new StartingBuilding { InstanceId = "start_barn", TypeId = barn.Id, Cell = new Vector2Int(9, 12) },
                new StartingBuilding { InstanceId = "start_trader", TypeId = trader.Id, Cell = new Vector2Int(10, 5) },
            };
            // never buildable: the main road (truck / trader access) and the trader's lane
            layout.ReservedRects = new List<RectInt> { new RectInt(7, 0, 2, 20), new RectInt(9, 5, 1, 2) };
            layout.GateCell = new Vector2Int(7, -1);
            layout.BarnPenLocal = new Rect(penLocal.x - 2.0f, penLocal.z - 1.35f, 4.0f, 2.7f);
            var barnCentre = layout.FootprintCenterWorld(new Vector2Int(9, 12), barn.Footprint);
            var penCentre = barnCentre + penLocal;
            layout.Sheep = new List<FarmSheepSpawn>
            {
                new FarmSheepSpawn { SheepId = "sheep_001", DisplayName = "Daisy", PenOffset = new Vector2(-0.8f, 0f) },
                new FarmSheepSpawn { SheepId = "sheep_002", DisplayName = "Clover", PenOffset = new Vector2(0.8f, 0.3f) },
            };
            EditorUtility.SetDirty(layout);
            var volume = FarmVolume();
            var ring = RingTexture();
            AssetDatabase.SaveAssets();

            // ---- scene
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // re-resolve after NewScene (unloaded asset references become fake nulls)
            layout = AssetDatabase.LoadAssetAtPath<FarmLayout>(Root + "/Config/FarmLayout.asset");
            volume = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Root + "/Config/FarmVolumeProfile.asset");
            ring = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Materials/T_SelectRing.png");
            foreach (var key in new List<string>(Mats.Keys)) Mats[key] = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/" + key + ".mat");
            catalog = AssetDatabase.LoadAssetAtPath<BuildingCatalog>(Root + "/Config/BuildingCatalog.asset");
            cConfig = AssetDatabase.LoadAssetAtPath<ConstructionConfig>(Root + "/Config/ConstructionConfig.asset");
            ghostMat = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/M_Ghost.mat");
            gridMat = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/M_BuildGrid.mat");
            footMat = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/M_Footprint.mat");
            builderPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/Builder.prefab");
            sitePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/ConstructionSite.prefab");
            sheepPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/FarmSheep.prefab");
            progression = AssetDatabase.LoadAssetAtPath<FarmProgressionConfig>(Root + "/Config/FarmProgressionConfig.asset");
            tools = AssetDatabase.LoadAssetAtPath<ShearAndGrow.Shearing.ToolUpgradeConfig>(toolsPath);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.86f, 0.88f, 0.92f);
            RenderSettings.ambientEquatorColor = new Color(0.72f, 0.70f, 0.60f);
            RenderSettings.ambientGroundColor = new Color(0.42f, 0.40f, 0.30f);
            RenderSettings.fog = false;

            var camGo = new GameObject("Farm Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true; cam.nearClipPlane = 1f; cam.farClipPlane = 90f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.47f, 0.66f, 0.33f);
            camGo.AddComponent<AudioListener>();
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var camCtl = camGo.AddComponent<FarmCameraController>();

            var sun = new GameObject("Sun (soft shadows)").AddComponent<Light>();
            sun.type = LightType.Directional; sun.color = new Color(1f, 0.93f, 0.8f); sun.intensity = 1.25f;
            sun.shadows = LightShadows.Soft; sun.shadowStrength = 0.55f; sun.shadowBias = 0.05f; sun.shadowNormalBias = 0.4f;
            sun.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
            var vol = new GameObject("Global Volume").AddComponent<Volume>();
            vol.isGlobal = true; vol.sharedProfile = volume;

            // ground: meadow backdrop + farm lawn + paths (static)
            var env = new GameObject("Environment").transform;
            var groundModel = new Model()
                .Box("Meadow", new Vector3(0, -0.06f, 0), new Vector3(80f, 0.1f, 80f));
            Realise(groundModel, env, "Meadow", false);
            // lawn = the buildable grid (16×24 cells). Only the reserved road and trader lane are drawn on it;
            // everything else on the lawn is placed by the player.
            var lawn = new Model().Box("Grass", new Vector3(0, -0.02f, 0), new Vector3(16f, 0.06f, 24f));
            lawn.Box("Path", new Vector3(0f, 0.015f, -2f), new Vector3(1.8f, 0.03f, 20f));// road: cells x 7–8, y 0–19
            lawn.Box("Path", new Vector3(1.5f, 0.016f, -6f), new Vector3(1.2f, 0.03f, 1.6f));      // trader lane: cell x 9, y 5–6
            lawn.Box("Path", new Vector3(0f, 0.014f, -12.8f), new Vector3(1.8f, 0.03f, 1.6f));     // road leaves the farm (gate)
            Realise(lawn, env, "Lawn", false);
            // decor (trees, bushes, flowers, hay, mailbox, well) – static
            var decor = new Model();
            // all landscape decor stays OUTSIDE the 16×24 buildable lawn so it never conflicts with the grid
            var trees = new[] { new Vector3(-9.4f, 0, 10.4f), new Vector3(-1.0f, 0, 13.4f), new Vector3(9.3f, 0, 10.6f), new Vector3(-9.6f, 0, 3.6f),
                                new Vector3(9.5f, 0, -1.8f), new Vector3(-9.3f, 0, -9.6f), new Vector3(9.4f, 0, -10.2f), new Vector3(-3.6f, 0, -13.6f),
                                new Vector3(-10.5f, 0, -1f), new Vector3(10.6f, 0, 4f), new Vector3(-11.8f, 0, 7f), new Vector3(10.9f, 0, -6f), new Vector3(3.5f, 0, 13.6f), new Vector3(-6f, 0, 13.4f), new Vector3(7.5f, 0, 13.8f) };
            for (int i = 0; i < trees.Length; i++)
            {
                var t = TreeModel(2.2f + (i % 3) * 0.4f, i + 1);
                foreach (var p in t.Parts) decor.Parts.Add(new Part { Mesh = p.Mesh, Mat = p.Mat, M = Matrix4x4.Translate(trees[i]) * p.M });
            }
            var rng = new System.Random(77);
            for (int i = 0; i < 40; i++)
            {
                // ring 0.4–3 m outside the lawn
                var p = new Vector3((float)rng.NextDouble() * 22f - 11f, 0, (float)rng.NextDouble() * 30f - 15f);
                if (Mathf.Abs(p.x) < 8.4f && Mathf.Abs(p.z) < 12.4f) continue;
                if (Mathf.Abs(p.x) < 1.4f && p.z < 0f) continue; // keep the road entrance clear
                if (i % 3 == 0) decor.Ball("LeavesLight", p + new Vector3(0, 0.25f, 0), new Vector3(0.45f, 0.35f, 0.45f), 10, 6);
                else decor.Ball(i % 2 == 0 ? "FlowerPink" : "FlowerYellow", p + new Vector3(0, 0.08f, 0), new Vector3(0.1f, 0.08f, 0.1f), 6, 4);
            }
            decor.Box("Wood", new Vector3(1.5f, 0.55f, -12.9f), new Vector3(0.1f, 1.1f, 0.1f)); // mailbox at the gate
            decor.Box("RoofRed", new Vector3(1.5f, 1.15f, -12.9f), new Vector3(0.5f, 0.3f, 0.35f));
            decor.Cyl("Stone", new Vector3(-9.4f, 0f, 6.2f), 0.5f, 0.5f, 0.6f, 14);                       // well
            decor.Cyl("Water", new Vector3(-9.4f, 0.61f, 6.2f), 0.42f, 0.42f, 0.02f, 14);
            decor.Box("Hay", new Vector3(9.2f, 0.3f, 7.0f), new Vector3(0.9f, 0.6f, 0.6f), new Vector3(0, 20f, 0));
            decor.Box("Hay", new Vector3(-9.0f, 0.3f, -4.0f), new Vector3(0.8f, 0.6f, 0.6f), new Vector3(0, -15f, 0));
            // farm border fence along the front edge, just outside the lawn
            for (float x = -7.8f; x <= 7.81f; x += 1.2f)
            {
                if (Mathf.Abs(x) < 1.3f) continue;
                decor.Box("Trim", new Vector3(x, 0.3f, -12.3f), new Vector3(0.1f, 0.6f, 0.1f));
            }
            decor.Box("Trim", new Vector3(-4.55f, 0.4f, -12.3f), new Vector3(6.5f, 0.06f, 0.05f));
            decor.Box("Trim", new Vector3(4.55f, 0.4f, -12.3f), new Vector3(6.5f, 0.06f, 0.05f));
            Realise(decor, env, "Decor", true);
            foreach (Transform c in env) GameObjectUtility.SetStaticEditorFlags(c.gameObject, StaticEditorFlags.BatchingStatic);

            // ---- interactables (not static: they pulse)
            // buildings & decorations are NOT baked: they are spawned at runtime from the saved farm (grid data)
            var world = new GameObject("FarmWorld").transform;
            var buildingsRoot = new GameObject("Buildings").transform;
            buildingsRoot.SetParent(world, false);

            var skin = AssetDatabase.LoadAssetAtPath<Material>(ShearRoot + "/Materials/Undercoat/M_SheepSkin.mat");
            var woolMat = AssetDatabase.LoadAssetAtPath<Material>(ShearRoot + "/Materials/Wool/M_WoolDecor.mat");
            var dark = AssetDatabase.LoadAssetAtPath<Material>(ShearRoot + "/Materials/Undercoat/M_Hoof.mat");
            int seed = 1;
            // Stage 5: sheep are spawned at runtime from the saved flock (FarmSheep.prefab) – nothing baked here
            foreach (var s in new List<FarmSheepSpawn>())
            {
                var go = new GameObject("Sheep " + s.DisplayName);
                go.transform.SetParent(world, false);
                go.transform.position = penCentre + new Vector3(s.PenOffset.x, 0, s.PenOffset.y);
                var view = go.AddComponent<FarmSheepView>();
                var so = new SerializedObject(view);
                so.FindProperty("_skin").objectReferenceValue = skin;
                so.FindProperty("_wool").objectReferenceValue = woolMat;
                so.FindProperty("_dark").objectReferenceValue = dark;
                so.FindProperty("_undercoat").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(ShearRoot + "/Materials/Undercoat/M_Undercoat.mat");
                so.FindProperty("_pen").rectValue = new Rect(penCentre.x - 2.0f, penCentre.z - 1.35f, 4.0f, 2.7f);
                so.FindProperty("_seed").intValue = seed++;
                so.ApplyModifiedPropertiesWithoutUndo();
                Hit(go.transform, new Vector3(0, 0.5f, 0), new Vector3(1.0f, 1.0f, 0.9f), layer);
                var anchor = new GameObject("LabelAnchor").transform;
                anchor.SetParent(go.transform, false);
                anchor.localPosition = new Vector3(0, 1.25f, 0);
                var it = go.AddComponent<SheepInteraction>();
                it.Configure(s.SheepId, s.DisplayName, go.transform, anchor);
                it.ConfigureSheep(s.SheepId, view);
            }

            // ---- selection ring (one quad, transparent, no shadows)
            var ringMatPath = Root + "/Materials/M_SelectRing.mat";
            var ringMat = AssetDatabase.LoadAssetAtPath<Material>(ringMatPath);
            if (ringMat == null) { ringMat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")); AssetDatabase.CreateAsset(ringMat, ringMatPath); }
            ringMat.SetTexture("_BaseMap", ring);
            ringMat.SetColor("_BaseColor", Color.white);
            ringMat.SetFloat("_Surface", 1f); ringMat.SetFloat("_Blend", 0f);
            ringMat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); ringMat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            ringMat.SetFloat("_ZWrite", 0f); ringMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            ringMat.renderQueue = (int)RenderQueue.Transparent; ringMat.SetOverrideTag("RenderType", "Transparent");
            EditorUtility.SetDirty(ringMat);
            var ringGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            ringGo.name = "SelectionRing";
            Object.DestroyImmediate(ringGo.GetComponent<Collider>());
            var rr = ringGo.GetComponent<MeshRenderer>();
            rr.sharedMaterial = ringMat; rr.shadowCastingMode = ShadowCastingMode.Off; rr.receiveShadows = false;
            ringGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            // ---- systems
            var systems = new GameObject("FarmSystems").transform;
            GameObject Sys(string n) { var g = new GameObject(n); g.transform.SetParent(systems, false); return g; }
            var econ = Sys("Economy");
            var bootstrap = econ.AddComponent<EconomyBootstrap>();
            var bso = new SerializedObject(bootstrap);
            bso.FindProperty("_balance").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameBalanceData>("Assets/ShearAndGrow/Core/Config/GameBalanceData.asset");
            bso.ApplyModifiedPropertiesWithoutUndo();
            var es = Sys("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            var input = Sys("Input").AddComponent<FarmInputRouter>();
            var sel = Sys("Selection").AddComponent<FarmSelectionFeedback>();
            ringGo.transform.SetParent(sel.transform, true);
            var ui = Sys("UI");
            var hud = ui.AddComponent<FarmHUD>();
            var panels = ui.AddComponent<FarmPanels>();
            var labels = ui.AddComponent<FarmWorldLabels>();
            var sell = ui.AddComponent<SellPanel>();
            var hap = Sys("Haptics").AddComponent<ProcessingHapticsDriver>();
            var hub = Sys("FarmHub").AddComponent<FarmHubController>();
            var buildUI = ui.AddComponent<BuildUI>();
            var buildingSys = Sys("Buildings");
            var director = buildingSys.AddComponent<FarmBuildingDirector>();
            var placement = buildingSys.AddComponent<PlacementController>();
            placement.Configure(ghostMat);
            var overlay = new GameObject("GridOverlay").AddComponent<GridOverlay>();
            overlay.transform.SetParent(buildingSys.transform, false);
            overlay.Configure(gridMat, footMat);
            var siteGo = (GameObject)PrefabUtility.InstantiatePrefab(sitePrefab);
            siteGo.transform.SetParent(world, false);
            var builderGo = (GameObject)PrefabUtility.InstantiatePrefab(builderPrefab);
            builderGo.transform.SetParent(world, false);
            director.Configure(catalog, cConfig, buildingsRoot, overlay, placement, siteGo.GetComponent<ConstructionSiteView>(), builderGo.GetComponent<BuilderNpcController>());

            void Set(Object o, string f, Object v) { var so = new SerializedObject(o); so.FindProperty(f).objectReferenceValue = v; so.ApplyModifiedPropertiesWithoutUndo(); }
            Set(input, "_camera", camCtl);
            Set(sel, "_ring", ringGo.transform);
            var hso = new SerializedObject(hub);
            hso.FindProperty("_layout").objectReferenceValue = layout;
            hso.FindProperty("_defaultState").objectReferenceValue = AssetDatabase.LoadAssetAtPath<DefaultGameState>(ShearRoot + "/Config/Save/DefaultGameState.asset");
            hso.FindProperty("_growth").objectReferenceValue = AssetDatabase.LoadAssetAtPath<WoolGrowthConfig>(ShearRoot + "/Config/Growth/WoolGrowthConfig.asset");
            hso.FindProperty("_feel").objectReferenceValue = AssetDatabase.LoadAssetAtPath<ShearingFeelConfig>(ShearRoot + "/Config/ShearingFeel/ShearingFeelConfig.asset");
            hso.FindProperty("_camera").objectReferenceValue = camCtl;
            hso.FindProperty("_input").objectReferenceValue = input;
            hso.FindProperty("_selection").objectReferenceValue = sel;
            hso.FindProperty("_hud").objectReferenceValue = hud;
            hso.FindProperty("_panels").objectReferenceValue = panels;
            hso.FindProperty("_labels").objectReferenceValue = labels;
            hso.FindProperty("_sell").objectReferenceValue = sell;
            hso.FindProperty("_haptics").objectReferenceValue = hap;
            hso.FindProperty("_hapticsConfig").objectReferenceValue = AssetDatabase.LoadAssetAtPath<HapticsConfig>(ShearRoot + "/Config/Haptics/HapticsConfig.asset");
            hso.FindProperty("_interactablesRoot").objectReferenceValue = world;
            hso.FindProperty("_interactableMask").intValue = 1 << layer;
            hso.FindProperty("_quality").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GraphicsQualityConfig>(ShearRoot + "/Config/GraphicsQuality/GraphicsQualityConfig.asset");
            hso.FindProperty("_director").objectReferenceValue = director;
            hso.FindProperty("_buildUI").objectReferenceValue = buildUI;
            hso.FindProperty("_sheepPrefab").objectReferenceValue = sheepPrefab;
            hso.FindProperty("_tools").objectReferenceValue = tools;
            director.SetProgression(progression);
            hso.ApplyModifiedPropertiesWithoutUndo();

            // catalog icons (one-off ortho renders of the visual prefabs, lit by the scene's sun)
            var iconDefs = new List<FarmBuildingDefinition>();
            foreach (var d in catalog.Items) if (d != null) iconDefs.Add(d);
            RenderIcons(iconDefs);

            camCtl.Initialise(layout.WorldBounds, new Bounds(new Vector3(0, 1.5f, 0f), new Vector3(15f, 3f, 22f)));
            EditorSceneManager.SaveScene(scene, ScenePath);
            SetBuildOrder();
            AssetDatabase.SaveAssets();
            Debug.Log("[Farm] Built " + ScenePath + " (layer " + layer + "), build order: FarmHub, Shearing, WoolWash, WoolDry.");
        }

        static void SetBuildOrder()
        {
            var wanted = new List<string>
            {
                ScenePath,
                ShearRoot + "/Scenes/Shearing_Feature_Test.unity",
                ProcRoot + "/Scenes/" + SceneNavigator.Wash + ".unity",
                ProcRoot + "/Scenes/" + SceneNavigator.Dry + ".unity",
            };
            string devMenu = ProcRoot + "/Scenes/" + SceneNavigator.DevMenu + ".unity";
            var list = new List<EditorBuildSettingsScene>();
            foreach (var p in wanted) list.Add(new EditorBuildSettingsScene(p, true));
            foreach (var s in EditorBuildSettings.scenes)
                if (!wanted.Contains(s.path) && s.path != devMenu) list.Add(s);
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
