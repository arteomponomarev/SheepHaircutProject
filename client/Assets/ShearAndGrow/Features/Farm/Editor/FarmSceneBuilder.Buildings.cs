using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShearAndGrow.Farm.Editor
{
    /// <summary>
    /// Building feature assets: visual prefabs (procedural placeholders split into construction stage groups),
    /// decoration prefabs (fence / path with neighbour connectors), the builder NPC, the construction site, ghost /
    /// grid materials, building definitions, the catalog, the construction config and catalog icons.
    /// All of it is placeholder art that final models or 2.5D sprite prefabs replace 1:1 (same components).
    /// </summary>
    public static partial class FarmSceneBuilder
    {
        const string PrefabDir = Root + "/Prefabs";
        const string IconDir = Root + "/Art/Icons";

        // ------------------------------------------------------------------ materials

        static Material Transparent(string name, Color c, Texture2D tex = null, string shader = "Universal Render Pipeline/Unlit")
        {
            string path = Root + "/Materials/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(m, path); }
            m.shader = Shader.Find(shader);
            if (tex != null) m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetOverrideTag("RenderType", "Transparent");
            EditorUtility.SetDirty(m);
            return m;
        }

        static void CreateBuildingMaterials()
        {
            Soft("SkinTone", new Color(1f, 0.82f, 0.68f));
            Soft("Overalls", new Color(0.32f, 0.52f, 0.82f));
            Soft("Shirt", new Color(0.95f, 0.55f, 0.35f));
            Soft("HatYellow", new Color(1f, 0.82f, 0.25f), 0.35f, 0.25f, 0.5f);
            Soft("Metal", new Color(0.55f, 0.58f, 0.62f), 0.3f, 0.5f, 0.6f);
            Soft("Dirt", new Color(0.66f, 0.52f, 0.36f), 0.05f, 0.02f, 0.1f);
            Soft("Rope", new Color(0.95f, 0.75f, 0.35f));
        }

        static Texture2D SoftDotTexture()
        {
            string path = Root + "/Materials/T_SoftDot.png";
            const int n = 64;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = new Vector2(x + 0.5f - n * 0.5f, y + 0.5f - n * 0.5f).magnitude / (n * 0.5f);
                    float a = Mathf.Clamp01(1f - d); a = a * a * (3f - 2f * a);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
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

        // ------------------------------------------------------------------ helpers

        static Model Offset(Model m, Vector3 o)
        {
            var r = new Model();
            foreach (var p in m.Parts) r.Parts.Add(new Part { Mesh = p.Mesh, Mat = p.Mat, M = Matrix4x4.Translate(o) * p.M });
            return r;
        }

        static Transform Group(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        static GameObject SavePrefab(GameObject go, string path)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        static void SetPrivate(Object o, string field, object value)
        {
            var so = new SerializedObject(o);
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogWarning("[Farm] Field not found: " + field); return; }
            switch (value)
            {
                case Object obj: p.objectReferenceValue = obj; break;
                case bool b: p.boolValue = b; break;
                case float f: p.floatValue = f; break;
                case int i: p.intValue = i; break;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Creates a building prefab: View with stage groups, hit area(s), label anchor, view + interaction components.</summary>
        static GameObject BuildingPrefab<T>(string id, string label, Model[] stages, (Vector3 c, Vector3 s)[] hits, Vector3 labelPos, int layer, bool showLabel = true, Model level2 = null)
            where T : FarmInteractable
        {
            var root = new GameObject(label);
            var view = Group(root.transform, "View");
            // level variants: Level1 holds the construction stage groups; Level2 is the upgraded look (same footprint)
            var l1 = level2 != null ? Group(view, "Level1") : view;
            string[] names = { "Stage_Foundation", "Stage_Walls", "Stage_Roof", "Stage_Details" };
            var groups = new Transform[4];
            for (int i = 0; i < 4; i++)
            {
                groups[i] = Group(l1, names[i]);
                if (stages[i] != null && stages[i].Parts.Count > 0) Realise(stages[i], groups[i], id + "_" + i);
            }
            GameObject[] levels = null;
            if (level2 != null)
            {
                var l2 = Group(view, "Level2");
                Realise(level2, l2, id + "_L2");
                l2.gameObject.SetActive(false);
                levels = new[] { l1.gameObject, l2.gameObject };
            }
            Collider first = null;
            foreach (var h in hits)
            {
                var hit = Hit(root.transform, h.c, h.s, layer);
                if (first == null) first = hit.GetComponent<Collider>();
            }
            var anchor = Group(root.transform, "LabelAnchor");
            anchor.localPosition = labelPos;
            var bv = root.AddComponent<BuildingView>();
            bv.Configure(view, anchor, first, groups[0], groups[1], groups[2], groups[3]);
            if (levels != null) bv.ConfigureLevels(levels);
            var it = root.AddComponent<T>();
            it.Configure(id, label, view, anchor);
            if (!showLabel) SetPrivate(it, "_showLabel", false);
            return SavePrefab(root, PrefabDir + "/Buildings/" + id + ".prefab");
        }

        // ------------------------------------------------------------------ staged building models (local: footprint centre)

        static Model[] HouseStages()
        {
            var f = new Model(); var w = new Model(); var r = new Model(); var d = new Model();
            f.Box("Stone", new Vector3(0, 0.1f, 0), new Vector3(3.4f, 0.2f, 2.8f));
            f.Box("Wood", new Vector3(0, 0.22f, -1.55f), new Vector3(1.2f, 0.12f, 0.6f));
            w.Box("Plaster", new Vector3(0, 1.2f, 0), new Vector3(3.0f, 2.0f, 2.4f));
            w.Box("Plaster", new Vector3(0, 2.4f, 0), new Vector3(2.96f, 0.9f, 0.9f), new Vector3(45f, 0, 0));
            r.Box("RoofRed", new Vector3(0, 2.62f, -0.66f), new Vector3(3.5f, 0.18f, 1.62f), new Vector3(-36f, 0, 0));
            r.Box("RoofRed", new Vector3(0, 2.62f, 0.66f), new Vector3(3.5f, 0.18f, 1.62f), new Vector3(36f, 0, 0));
            r.Box("Stone", new Vector3(0.95f, 3.05f, 0.45f), new Vector3(0.38f, 0.9f, 0.38f));
            d.Box("Door", new Vector3(0, 0.75f, -1.21f), new Vector3(0.7f, 1.1f, 0.06f));
            d.Box("Trim", new Vector3(0, 1.34f, -1.22f), new Vector3(0.84f, 0.1f, 0.06f));
            foreach (float x in new[] { -0.95f, 0.95f })
            {
                d.Box("Window", new Vector3(x, 1.25f, -1.21f), new Vector3(0.6f, 0.55f, 0.05f));
                d.Box("Trim", new Vector3(x, 0.95f, -1.24f), new Vector3(0.72f, 0.08f, 0.1f));
                d.Box("FlowerPink", new Vector3(x, 0.9f, -1.33f), new Vector3(0.6f, 0.14f, 0.16f));
            }
            return new[] { f, w, r, d };
        }

        /// <summary>Barn body at +z, its sheep pen at −z (one 5×8 footprint; the pen moves with the barn).</summary>
        static Model[] BarnStages(Vector3 barnOffset, Vector3 penOffset)
        {
            var f = new Model(); var w = new Model(); var r = new Model(); var d = new Model();
            f.Box("Stone", new Vector3(0, 0.07f, 0), new Vector3(3.4f, 0.14f, 3.0f));
            w.Box("BarnRed", new Vector3(0, 1.3f, 0), new Vector3(3.2f, 2.6f, 2.8f));
            w.Box("BarnRed", new Vector3(0, 2.75f, 0), new Vector3(3.16f, 1.0f, 1.0f), new Vector3(45f, 0, 0));
            r.Box("RoofDark", new Vector3(0, 2.9f, -0.75f), new Vector3(3.5f, 0.2f, 1.85f), new Vector3(-32f, 0, 0));
            r.Box("RoofDark", new Vector3(0, 2.9f, 0.75f), new Vector3(3.5f, 0.2f, 1.85f), new Vector3(32f, 0, 0));
            d.Box("Door", new Vector3(0, 0.95f, -1.41f), new Vector3(1.5f, 1.9f, 0.06f));
            d.Box("Trim", new Vector3(0, 0.95f, -1.45f), new Vector3(1.62f, 0.1f, 0.06f));
            d.Box("Trim", new Vector3(0, 0.95f, -1.45f), new Vector3(0.1f, 1.95f, 0.06f));
            d.Box("Trim", new Vector3(0, 0.95f, -1.46f), new Vector3(2.3f, 0.09f, 0.05f), new Vector3(0, 0, 48f));
            d.Box("Trim", new Vector3(0, 0.95f, -1.46f), new Vector3(2.3f, 0.09f, 0.05f), new Vector3(0, 0, -48f));
            d.Box("Window", new Vector3(0, 2.35f, -1.41f), new Vector3(0.6f, 0.5f, 0.05f));
            d.Box("Hay", new Vector3(1.85f, 0.35f, -0.9f), new Vector3(0.7f, 0.7f, 1.0f));
            d.Box("Hay", new Vector3(1.85f, 1.0f, -0.7f), new Vector3(0.6f, 0.6f, 0.8f), new Vector3(0, 12f, 0));
            var pen = new Model();
            pen.Box("PenDirt", new Vector3(0, 0.012f, 0), new Vector3(4.8f, 0.03f, 3.5f));
            var fence = PenFence(4.6f, 3.4f);
            var stages = new[] { Offset(f, barnOffset), Offset(w, barnOffset), Offset(r, barnOffset), Offset(d, barnOffset) };
            foreach (var p in Offset(pen, penOffset).Parts) stages[0].Parts.Add(p);
            foreach (var p in Offset(fence, penOffset).Parts) stages[3].Parts.Add(p);
            return stages;
        }

        static Model[] ShedStages()
        {
            var f = new Model(); var w = new Model(); var r = new Model(); var d = new Model();
            f.Box("Stone", new Vector3(0, 0.08f, 0), new Vector3(3.4f, 0.16f, 2.6f));
            foreach (var p in new[] { new Vector3(-1.5f, 0, -1.1f), new Vector3(1.5f, 0, -1.1f), new Vector3(-1.5f, 0, 1.1f), new Vector3(1.5f, 0, 1.1f) })
                w.Box("Wood", p + new Vector3(0, 1.15f, 0), new Vector3(0.18f, 2.3f, 0.18f));
            w.Box("Wood", new Vector3(0, 1.4f, 1.12f), new Vector3(3.1f, 2.5f, 0.12f));
            r.Box("RoofBlue", new Vector3(0, 2.5f, 0), new Vector3(3.7f, 0.18f, 3.0f), new Vector3(-12f, 0, 0));
            r.Box("Trim", new Vector3(0, 2.33f, -1.42f), new Vector3(3.7f, 0.12f, 0.08f), new Vector3(-12f, 0, 0));
            d.Cyl("WoodDark", new Vector3(-0.75f, 0.16f, -0.1f), 0.62f, 0.7f, 0.55f, 16);
            d.Cyl("Water", new Vector3(-0.75f, 0.68f, -0.1f), 0.6f, 0.6f, 0.03f, 16);
            d.Box("Wood", new Vector3(0.9f, 0.55f, 0f), new Vector3(1.1f, 0.08f, 0.9f));
            foreach (var x in new[] { 0.4f, 1.4f }) foreach (var z in new[] { -0.4f, 0.4f })
                d.Box("Wood", new Vector3(x, 0.3f, z), new Vector3(0.07f, 0.5f, 0.07f));
            d.Box("Trim", new Vector3(0, 2.0f, -1.2f), new Vector3(1.4f, 0.4f, 0.06f));
            d.Box("Water", new Vector3(0, 2.0f, -1.24f), new Vector3(0.3f, 0.3f, 0.04f), new Vector3(0, 0, 45f));
            return new[] { f, w, r, d };
        }

        // ------------------------------------------------------------------ Stage 5 models

        /// <summary>Farmer House Lv 2: bigger body, dormer, porch roof, more windows, tall chimney (same 4×4 footprint).</summary>
        static Model HouseLevel2()
        {
            var m = new Model();
            m.Box("Stone", new Vector3(0, 0.1f, 0), new Vector3(3.8f, 0.2f, 3.3f));
            m.Box("Plaster", new Vector3(0, 1.35f, 0.1f), new Vector3(3.4f, 2.3f, 2.7f));
            m.Box("Wood", new Vector3(0, 0.26f, -1.7f), new Vector3(1.6f, 0.14f, 0.7f));
            m.Box("RoofBlue", new Vector3(0, 2.95f, -0.62f), new Vector3(3.9f, 0.2f, 1.85f), new Vector3(-36f, 0, 0));
            m.Box("RoofBlue", new Vector3(0, 2.95f, 0.82f), new Vector3(3.9f, 0.2f, 1.85f), new Vector3(36f, 0, 0));
            m.Box("Plaster", new Vector3(0, 2.7f, 0.1f), new Vector3(3.36f, 1.0f, 1.0f), new Vector3(45f, 0, 0));
            m.Box("Plaster", new Vector3(-0.8f, 3.0f, -0.55f), new Vector3(0.8f, 0.7f, 0.7f));                 // dormer
            m.Box("RoofBlue", new Vector3(-0.8f, 3.45f, -0.55f), new Vector3(1.0f, 0.12f, 0.9f), new Vector3(-12f, 0, 0));
            m.Box("Window", new Vector3(-0.8f, 3.0f, -0.92f), new Vector3(0.42f, 0.4f, 0.05f));
            m.Box("Stone", new Vector3(1.1f, 3.5f, 0.6f), new Vector3(0.42f, 1.3f, 0.42f));                    // chimney
            m.Box("Trim", new Vector3(1.1f, 4.18f, 0.6f), new Vector3(0.5f, 0.08f, 0.5f));
            m.Box("Door", new Vector3(0, 0.85f, -1.26f), new Vector3(0.8f, 1.25f, 0.06f));
            m.Box("Trim", new Vector3(0, 1.55f, -1.27f), new Vector3(0.95f, 0.1f, 0.06f));
            foreach (float x in new[] { -0.75f, 0.75f }) m.Box("WoodDark", new Vector3(x, 0.95f, -1.75f), new Vector3(0.1f, 1.5f, 0.1f)); // porch posts
            m.Box("RoofRed", new Vector3(0, 1.78f, -1.55f), new Vector3(1.9f, 0.1f, 0.8f), new Vector3(-14f, 0, 0));
            foreach (float x in new[] { -1.2f, 1.2f })
            {
                m.Box("Window", new Vector3(x, 1.3f, -1.26f), new Vector3(0.62f, 0.62f, 0.05f));
                m.Box("Trim", new Vector3(x, 1.3f, -1.28f), new Vector3(0.08f, 0.66f, 0.04f));
                m.Box("Trim", new Vector3(x, 0.96f, -1.29f), new Vector3(0.74f, 0.08f, 0.1f));
                m.Box(x < 0 ? "FlowerPink" : "FlowerYellow", new Vector3(x, 0.9f, -1.38f), new Vector3(0.62f, 0.16f, 0.18f));
                m.Box("Window", new Vector3(1.71f, 1.4f, x * 0.5f + 0.1f), new Vector3(0.05f, 0.55f, 0.5f));
            }
            m.Ball("Leaves", new Vector3(-1.75f, 0.35f, -1.3f), new Vector3(0.35f, 0.32f, 0.35f), 10, 7);
            m.Ball("Leaves", new Vector3(1.75f, 0.35f, -1.3f), new Vector3(0.35f, 0.32f, 0.35f), 10, 7);
            return m;
        }

        /// <summary>Barn Lv 2: L1 barn + hayloft door, weathervane, silo, second trough and hay in a wider pen.</summary>
        static Model BarnLevel2(Vector3 barnOffset, Vector3 penOffset)
        {
            var m = new Model();
            foreach (var st in BarnStages(barnOffset, penOffset)) foreach (var p in st.Parts) m.Parts.Add(p);
            var extra = new Model();
            extra.Box("Door", new Vector3(0, 2.75f, -1.42f), new Vector3(0.7f, 0.6f, 0.05f));            // hayloft
            extra.Box("Trim", new Vector3(0, 2.75f, -1.45f), new Vector3(0.8f, 0.07f, 0.04f));
            extra.Cyl("Metal", new Vector3(0, 3.45f, 0), 0.03f, 0.03f, 0.6f, 6);                         // weathervane
            extra.Box("Metal", new Vector3(0, 3.95f, 0), new Vector3(0.5f, 0.05f, 0.05f));
            extra.Box("HatYellow", new Vector3(0.22f, 3.95f, 0), new Vector3(0.12f, 0.16f, 0.03f));
            extra.Cyl("Stone", new Vector3(2.0f, 0f, 0.6f), 0.45f, 0.45f, 3.0f, 14);                        // silo
            extra.Ball("RoofDark", new Vector3(2.0f, 3.0f, 0.6f), new Vector3(0.48f, 0.35f, 0.48f), 12, 7);
            foreach (var p in Offset(extra, barnOffset).Parts) m.Parts.Add(p);
            var pen = new Model();
            pen.Box("Wood", new Vector3(1.6f, 0.25f, 1.1f), new Vector3(0.9f, 0.3f, 0.4f));
            pen.Box("Hay", new Vector3(1.6f, 0.4f, 1.1f), new Vector3(0.75f, 0.05f, 0.28f));
            pen.Box("Hay", new Vector3(-1.7f, 0.25f, 1.15f), new Vector3(0.6f, 0.5f, 0.5f), new Vector3(0, 18f, 0));
            foreach (var p in Offset(pen, penOffset).Parts) m.Parts.Add(p);
            return m;
        }

        static Model[] WorkshopStages()
        {
            var f = new Model(); var w = new Model(); var r = new Model(); var d = new Model();
            f.Box("Stone", new Vector3(0, 0.08f, 0), new Vector3(2.8f, 0.16f, 2.6f));
            w.Box("Wood", new Vector3(0, 1.1f, 0.2f), new Vector3(2.5f, 1.9f, 2.0f));
            w.Box("WoodDark", new Vector3(0, 1.1f, 0.2f), new Vector3(2.56f, 0.1f, 2.06f));
            r.Box("RoofBlue", new Vector3(0, 2.25f, -0.35f), new Vector3(2.9f, 0.16f, 1.4f), new Vector3(-24f, 0, 0));
            r.Box("RoofBlue", new Vector3(0, 2.25f, 0.75f), new Vector3(2.9f, 0.16f, 1.4f), new Vector3(24f, 0, 0));
            r.Box("Wood", new Vector3(0, 2.15f, 0.2f), new Vector3(2.46f, 0.6f, 0.6f), new Vector3(45f, 0, 0));
            d.Box("Door", new Vector3(-0.5f, 0.8f, -0.81f), new Vector3(0.9f, 1.3f, 0.06f));
            d.Box("Window", new Vector3(0.65f, 1.2f, -0.81f), new Vector3(0.6f, 0.5f, 0.05f));
            d.Box("Trim", new Vector3(0, 2.0f, -0.95f), new Vector3(1.6f, 0.45f, 0.06f));                      // sign
            d.Box("Metal", new Vector3(-0.2f, 2.0f, -0.99f), new Vector3(0.7f, 0.12f, 0.04f));                 // trimmer on the sign
            d.Box("Overalls", new Vector3(0.28f, 2.0f, -0.99f), new Vector3(0.35f, 0.2f, 0.04f));
            d.Cyl("Metal", new Vector3(0.62f, 2.0f, -0.98f), 0.15f, 0.15f, 0.05f, 10, new Vector3(90f, 0, 0)); // gear
            d.Box("Wood", new Vector3(0.9f, 0.45f, -1.05f), new Vector3(0.9f, 0.08f, 0.45f));                  // workbench
            foreach (var x in new[] { 0.55f, 1.25f }) d.Box("WoodDark", new Vector3(x, 0.22f, -1.05f), new Vector3(0.07f, 0.44f, 0.4f));
            d.Box("Metal", new Vector3(0.85f, 0.53f, -1.05f), new Vector3(0.25f, 0.08f, 0.12f));
            return new[] { f, w, r, d };
        }

        static Model[] FeedingStationStages()
        {
            var f = new Model(); var w = new Model(); var r = new Model(); var d = new Model();
            f.Box("Dirt", new Vector3(0, 0.03f, 0), new Vector3(2.8f, 0.05f, 1.8f));
            foreach (var p in new[] { new Vector3(-1.2f, 0, -0.7f), new Vector3(1.2f, 0, -0.7f), new Vector3(-1.2f, 0, 0.7f), new Vector3(1.2f, 0, 0.7f) })
                w.Box("Wood", p + new Vector3(0, 0.8f, 0), new Vector3(0.12f, 1.6f, 0.12f));
            w.Box("Wood", new Vector3(0, 0.9f, 0.3f), new Vector3(2.2f, 0.9f, 0.08f), new Vector3(-18f, 0, 0));    // hay rack
            r.Box("RoofRed", new Vector3(0, 1.75f, -0.1f), new Vector3(2.8f, 0.12f, 2.0f), new Vector3(-10f, 0, 0));
            d.Box("Hay", new Vector3(0, 1.0f, 0.4f), new Vector3(2.0f, 0.6f, 0.4f), new Vector3(-18f, 0, 0));
            foreach (var x in new[] { -0.65f, 0.65f })
            {
                d.Box("Wood", new Vector3(x, 0.22f, -0.45f), new Vector3(0.9f, 0.28f, 0.4f));
                d.Box("Hay", new Vector3(x, 0.38f, -0.45f), new Vector3(0.78f, 0.06f, 0.3f));
            }
            d.Box("Water", new Vector3(1.0f, 0.2f, 0.75f), new Vector3(0.5f, 0.04f, 0.3f));
            return new[] { f, w, r, d };
        }

        static GameObject SheepPrefab(int layer)
        {
            var go = new GameObject("FarmSheep");
            var view = go.AddComponent<FarmSheepView>();
            var so = new SerializedObject(view);
            so.FindProperty("_skin").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(ShearRoot + "/Materials/Undercoat/M_SheepSkin.mat");
            so.FindProperty("_wool").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(ShearRoot + "/Materials/Wool/M_WoolDecor.mat");
            so.FindProperty("_dark").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(ShearRoot + "/Materials/Undercoat/M_Hoof.mat");
            so.FindProperty("_undercoat").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(ShearRoot + "/Materials/Undercoat/M_Undercoat.mat");
            so.ApplyModifiedPropertiesWithoutUndo();
            Hit(go.transform, new Vector3(0, 0.5f, 0), new Vector3(1.0f, 1.0f, 0.9f), layer);
            var anchor = Group(go.transform, "LabelAnchor");
            anchor.localPosition = new Vector3(0, 1.25f, 0);
            var it = go.AddComponent<SheepInteraction>();
            it.Configure("sheep", "Sheep", go.transform, anchor);
            return SavePrefab(go, PrefabDir + "/FarmSheep.prefab");
        }

        static Model[] TraderStages()
        {
            var all = TraderModel();
            return new[] { new Model(), all, new Model(), new Model() };
        }

        // ------------------------------------------------------------------ decorations

        static GameObject DecorPrefab(string id, string label, Model model, Vector3 hitSize, int layer)
        {
            var root = new GameObject(label);
            var view = Group(root.transform, "View");
            Realise(model, view, id);
            var hit = Hit(root.transform, new Vector3(0, hitSize.y * 0.5f, 0), hitSize, layer);
            var anchor = Group(root.transform, "LabelAnchor");
            anchor.localPosition = new Vector3(0, hitSize.y + 0.3f, 0);
            var bv = root.AddComponent<BuildingView>();
            bv.Configure(view, anchor, hit.GetComponent<Collider>(), null, null, null, null);
            var it = root.AddComponent<DecorationInteraction>();
            it.Configure(id, label, view, anchor);
            SetPrivate(it, "_showLabel", false);
            return SavePrefab(root, PrefabDir + "/Decor/" + id + ".prefab");
        }

        /// <summary>Segment decor: centre piece + N/E/S/W connector pieces toggled by neighbours.</summary>
        static GameObject ConnectablePrefab(string id, string label, Model centre, Model north, Model single, Vector3 hitSize, int layer)
        {
            var root = new GameObject(label);
            var view = Group(root.transform, "View");
            Realise(centre, Group(view, "Centre"), id + "_c");
            var conn = new GameObject[4];
            string[] n = { "N", "E", "S", "W" };
            for (int i = 0; i < 4; i++)
            {
                var g = Group(view, n[i]);
                g.localRotation = Quaternion.Euler(0f, 90f * i, 0f); // north piece rotated to E/S/W
                Realise(north, g, id + "_n" + i);
                conn[i] = g.gameObject;
                g.gameObject.SetActive(false);
            }
            GameObject singleGo = null;
            if (single != null) { var g = Group(view, "Single"); Realise(single, g, id + "_s"); singleGo = g.gameObject; }
            var hit = Hit(root.transform, new Vector3(0, hitSize.y * 0.5f, 0), hitSize, layer);
            var anchor = Group(root.transform, "LabelAnchor");
            anchor.localPosition = new Vector3(0, hitSize.y + 0.3f, 0);
            var cv = root.AddComponent<ConnectableDecorView>();
            cv.Configure(view, anchor, hit.GetComponent<Collider>(), null, null, null, null);
            cv.ConfigureConnectors(conn[0], conn[1], conn[2], conn[3], singleGo);
            var it = root.AddComponent<DecorationInteraction>();
            it.Configure(id, label, view, anchor);
            SetPrivate(it, "_showLabel", false);
            return SavePrefab(root, PrefabDir + "/Decor/" + id + ".prefab");
        }

        static Model DecorTree()
        {
            var m = new Model();
            m.Cyl("Trunk", Vector3.zero, 0.12f, 0.09f, 0.7f, 8);
            m.Ball("Leaves", new Vector3(0, 0.95f, 0), new Vector3(0.48f, 0.46f, 0.48f), 12, 8);
            m.Ball("LeavesLight", new Vector3(0.12f, 1.22f, -0.1f), new Vector3(0.32f, 0.3f, 0.32f), 10, 7);
            return m;
        }

        static Model DecorFlowerBed()
        {
            var m = new Model();
            m.Box("Wood", new Vector3(0, 0.08f, 0), new Vector3(0.9f, 0.16f, 0.9f));
            m.Box("Dirt", new Vector3(0, 0.165f, 0), new Vector3(0.78f, 0.02f, 0.78f));
            var rng = new System.Random(11);
            for (int i = 0; i < 9; i++)
            {
                var p = new Vector3(((i % 3) - 1) * 0.24f + (float)(rng.NextDouble() - 0.5) * 0.06f, 0.24f, ((i / 3) - 1) * 0.24f);
                m.Ball(i % 2 == 0 ? "FlowerPink" : "FlowerYellow", p, new Vector3(0.08f, 0.07f, 0.08f), 6, 4);
                m.Ball("Leaves", p - new Vector3(0, 0.05f, 0), new Vector3(0.07f, 0.04f, 0.07f), 6, 4);
            }
            return m;
        }

        static Model DecorStone()
        {
            var m = new Model();
            m.Ball("Stone", new Vector3(0, 0.18f, 0), new Vector3(0.36f, 0.24f, 0.3f), 10, 7);
            m.Ball("Stone", new Vector3(0.22f, 0.1f, -0.18f), new Vector3(0.18f, 0.12f, 0.16f), 8, 6);
            m.Ball("Leaves", new Vector3(-0.2f, 0.04f, -0.18f), new Vector3(0.14f, 0.06f, 0.12f), 8, 5);
            return m;
        }

        static Model DecorSheepStatue()
        {
            var m = new Model();
            m.Box("Stone", new Vector3(0, 0.15f, 0), new Vector3(1.5f, 0.3f, 1.1f));
            m.Box("Trim", new Vector3(0, 0.32f, 0), new Vector3(1.4f, 0.04f, 1.0f));
            var rng = new System.Random(5);
            for (int i = 0; i < 9; i++)
            {
                var p = new Vector3(-0.3f + (i % 3) * 0.3f, 0.75f + (i / 3) * 0.12f, -0.15f + (float)rng.NextDouble() * 0.3f);
                m.Ball("Plaster", p, new Vector3(0.24f, 0.22f, 0.24f), 10, 7);
            }
            m.Ball("RoofDark", new Vector3(0.55f, 0.92f, 0), new Vector3(0.17f, 0.15f, 0.14f), 10, 7);
            foreach (var x in new[] { -0.3f, 0.3f }) foreach (var z in new[] { -0.15f, 0.15f })
                m.Cyl("RoofDark", new Vector3(x, 0.34f, z), 0.05f, 0.05f, 0.32f, 6);
            return m;
        }

        static void FenceModels(out Model centre, out Model north, out Model single)
        {
            centre = new Model().Box("Trim", new Vector3(0, 0.35f, 0), new Vector3(0.13f, 0.7f, 0.13f));
            north = new Model();
            foreach (float y in new[] { 0.28f, 0.56f }) north.Box("Trim", new Vector3(0, y, 0.25f), new Vector3(0.05f, 0.07f, 0.5f));
            single = new Model();
            foreach (float y in new[] { 0.28f, 0.56f }) single.Box("Trim", new Vector3(0, y, 0), new Vector3(0.5f, 0.07f, 0.05f));
        }

        static void PathModels(out Model centre, out Model north)
        {
            centre = new Model().Box("Path", new Vector3(0, 0.02f, 0), new Vector3(0.66f, 0.03f, 0.66f));
            north = new Model().Box("Path", new Vector3(0, 0.021f, 0.42f), new Vector3(0.66f, 0.03f, 0.18f));
        }

        // ------------------------------------------------------------------ builder NPC & construction site

        static GameObject BuilderPrefab()
        {
            var root = new GameObject("Builder");
            var model = Group(root.transform, "Model");
            var body = Group(model, "Body");
            var torso = new Model();
            torso.Ball("Overalls", new Vector3(0, 0.62f, 0), new Vector3(0.21f, 0.24f, 0.17f), 12, 9);
            torso.Ball("Shirt", new Vector3(0, 0.78f, 0), new Vector3(0.2f, 0.12f, 0.16f), 12, 8);
            torso.Ball("SkinTone", new Vector3(0, 0.99f, 0), new Vector3(0.17f, 0.17f, 0.16f), 12, 9);
            torso.Ball("RoofDark", new Vector3(-0.06f, 1.01f, 0.145f), new Vector3(0.022f, 0.028f, 0.02f), 6, 4);
            torso.Ball("RoofDark", new Vector3(0.06f, 1.01f, 0.145f), new Vector3(0.022f, 0.028f, 0.02f), 6, 4);
            torso.Ball("HatYellow", new Vector3(0, 1.1f, 0), new Vector3(0.19f, 0.11f, 0.19f), 12, 8);
            torso.Cyl("HatYellow", new Vector3(0, 1.06f, 0.02f), 0.23f, 0.23f, 0.03f, 14);
            Realise(torso, body, "builder_body");
            Transform Limb(Transform parent, string name, Vector3 pivot, Model mesh, string meshName)
            {
                var t = Group(parent, name);
                t.localPosition = pivot;
                Realise(mesh, t, meshName);
                return t;
            }
            var arm = new Model();
            arm.Cyl("Shirt", new Vector3(0, -0.26f, 0), 0.05f, 0.055f, 0.26f, 8);
            arm.Ball("SkinTone", new Vector3(0, -0.29f, 0), new Vector3(0.055f, 0.055f, 0.055f), 8, 6);
            var armL = Limb(body, "ArmL", new Vector3(-0.23f, 0.8f, 0f), arm, "builder_armL");
            var armHammer = new Model();
            foreach (var p in arm.Parts) armHammer.Parts.Add(p);
            armHammer.Box("Wood", new Vector3(0, -0.3f, 0.14f), new Vector3(0.035f, 0.035f, 0.32f));
            armHammer.Box("Metal", new Vector3(0, -0.3f, 0.3f), new Vector3(0.07f, 0.13f, 0.07f));
            var armR = Limb(body, "ArmR", new Vector3(0.23f, 0.8f, 0f), armHammer, "builder_armR");
            var leg = new Model();
            leg.Cyl("Overalls", new Vector3(0, -0.4f, 0), 0.065f, 0.07f, 0.36f, 8);
            leg.Box("WoodDark", new Vector3(0, -0.4f, 0.03f), new Vector3(0.12f, 0.08f, 0.18f));
            var legL = Limb(model, "LegL", new Vector3(-0.09f, 0.42f, 0f), leg, "builder_legL");
            var legR = Limb(model, "LegR", new Vector3(0.09f, 0.42f, 0f), leg, "builder_legR");
            var ctl = root.AddComponent<BuilderNpcController>();
            ctl.Configure(model, body, armL, armR, legL, legR);
            return SavePrefab(root, PrefabDir + "/Builder.prefab");
        }

        static ParticleSystem MakeParticles(Transform parent, string name, Material mat, Color color, float size, float life, float speed, int max, float gravity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false; main.loop = false; main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.7f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.5f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
            main.startColor = color;
            main.maxParticles = max;
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 0f;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.15f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.3f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            return ps;
        }

        static GameObject SitePrefab(Material particleMat)
        {
            var root = new GameObject("ConstructionSite");
            var pad = Group(root.transform, "Pad");
            Realise(new Model().Box("Dirt", new Vector3(0, 0.02f, 0), new Vector3(1f, 0.03f, 1f)), pad, "site_pad", false);
            var poles = new Transform[4];
            for (int i = 0; i < 4; i++)
            {
                poles[i] = Group(root.transform, "Pole" + i);
                var pm = new Model();
                pm.Cyl("Wood", Vector3.zero, 0.06f, 0.06f, 2.3f, 6);
                pm.Box("Rope", new Vector3(0, 0.6f, 0), new Vector3(0.14f, 0.05f, 0.14f));
                Realise(pm, poles[i], "site_pole" + i);
            }
            var scaffold = Group(root.transform, "Scaffold");
            var sm = new Model();
            foreach (float y in new[] { 1.1f, 2.0f })
            {
                sm.Box("Wood", new Vector3(0, y, -0.5f), new Vector3(1f, 0.06f, 0.04f));
                sm.Box("Wood", new Vector3(0, y, 0.5f), new Vector3(1f, 0.06f, 0.04f));
                sm.Box("Wood", new Vector3(-0.5f, y, 0), new Vector3(0.04f, 0.06f, 1f));
                sm.Box("Wood", new Vector3(0.5f, y, 0), new Vector3(0.04f, 0.06f, 1f));
            }
            sm.Box("WoodDark", new Vector3(0, 1.13f, -0.5f), new Vector3(1f, 0.03f, 0.08f)); // walkway plank (front)
            Realise(sm, scaffold, "site_scaffold");
            var planks = Group(root.transform, "Planks");
            var pl = new Model();
            for (int i = 0; i < 4; i++) pl.Box(i % 2 == 0 ? "Wood" : "WoodDark", new Vector3(0, 0.05f + i * 0.08f, (i % 2) * 0.05f), new Vector3(0.3f, 0.07f, 1.2f), new Vector3(0, i * 6f, 0));
            Realise(pl, planks, "site_planks");
            var dust = MakeParticles(root.transform, "Dust", particleMat, new Color(0.9f, 0.82f, 0.68f, 0.8f), 0.35f, 0.8f, 0.8f, 12, -0.05f);
            var sparkle = MakeParticles(root.transform, "Sparkle", particleMat, new Color(1f, 0.92f, 0.5f, 1f), 0.22f, 1.1f, 2.2f, 40, 0.3f);
            var sh = sparkle.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(3f, 1.5f, 3f);
            var site = root.AddComponent<ConstructionSiteView>();
            site.Configure(pad, poles, scaffold, planks, dust, sparkle);
            return SavePrefab(root, PrefabDir + "/ConstructionSite.prefab");
        }

        // ------------------------------------------------------------------ definitions

        static FarmBuildingDefinition Define(string id, string name, string desc, FarmBuildingKind kind, BuildingCategory cat, BuildingInteraction interaction,
            Vector2Int footprint, int price, float seconds, int maxInstances, bool movable, bool storable, bool purchasable, ConnectMode connect, GameObject prefab)
        {
            var d = Def(id, name, desc, kind, footprint);
            d.Category = cat; d.Interaction = interaction; d.Price = price; d.ConstructionSeconds = seconds;
            d.MaxInstances = maxInstances; d.Movable = movable; d.Storable = storable; d.Purchasable = purchasable;
            d.ShowInCatalog = purchasable; d.Connect = connect; d.VisualPrefab = prefab; d.RequiredFarmerHouseLevel = 1;
            EditorUtility.SetDirty(d);
            return d;
        }

        // ------------------------------------------------------------------ icons

        static void RenderIcons(IEnumerable<FarmBuildingDefinition> defs)
        {
            Directory.CreateDirectory(IconDir);
            var camGo = new GameObject("IconCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 60f; cam.enabled = false;
            var rt = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            var origin = new Vector3(400f, 0f, 400f);
            foreach (var d in defs)
            {
                if (d == null || d.VisualPrefab == null) continue;
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(d.VisualPrefab);
                inst.transform.position = origin;
                var bounds = new Bounds(origin, Vector3.one);
                bool first = true;
                foreach (var r in inst.GetComponentsInChildren<Renderer>())
                {
                    if (!r.gameObject.activeInHierarchy) continue;
                    if (first) { bounds = r.bounds; first = false; } else bounds.Encapsulate(r.bounds);
                }
                var rot = Quaternion.Euler(32f, 40f, 0f);
                cam.transform.rotation = rot;
                cam.transform.position = bounds.center - rot * Vector3.forward * 30f;
                cam.orthographicSize = Mathf.Max(bounds.extents.magnitude * 0.82f, 0.5f);
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(256, 256, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                string path = IconDir + "/Icon_" + d.Id + ".png";
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                Object.DestroyImmediate(inst);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var imp = (TextureImporter)AssetImporter.GetAtPath(path);
                imp.textureType = TextureImporterType.Sprite; imp.alphaIsTransparency = true; imp.mipmapEnabled = false;
                imp.SaveAndReimport();
                d.Icon = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                EditorUtility.SetDirty(d);
            }
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGo);
        }
    }
}