using System.Collections.Generic;
using UnityEngine;

namespace ShearAndGrow.Shearing
{
    /// <summary>
    /// PLACEHOLDER ART: builds a stylised sheep (head, big eyes, ears, muzzle, legs, hooves, tail) around the
    /// parametric woolly body. Replace with an authored rigged model when available – the shearing core only
    /// depends on <see cref="WoolVisualRenderer"/> and <see cref="SheepBodySurface"/>.
    /// </summary>
    public sealed class SheepRigBuilder : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] private string _sheepId = "sheep_001";
        [SerializeField] private string _sheepTypeId = "sheep_fluffy_default";

        [Header("Body")]
        [SerializeField] private SheepBodySurface _surface = new SheepBodySurface();
        [SerializeField] private float _bodyHeight = 0.93f;

        [Header("Materials")]
        [SerializeField] private Material _skinMaterial;
        [SerializeField] private Material _earInnerMaterial;
        [SerializeField] private Material _hoofMaterial;
        [SerializeField] private Material _eyeWhiteMaterial;
        [SerializeField] private Material _eyeDarkMaterial;
        [SerializeField] private Material _noseMaterial;
        [SerializeField] private Material _woolSoftMaterial;

        public string SheepId => _sheepId;
        public string SheepTypeId => _sheepTypeId;
        public SheepBodySurface Surface => _surface;

        public Transform BodyPivot { get; private set; }
        public Transform HeadPivot { get; private set; }
        public Transform LeftEar { get; private set; }
        public Transform RightEar { get; private set; }
        public Transform LeftEye { get; private set; }
        public Transform RightEye { get; private set; }
        public Transform Tail { get; private set; }
        public Transform Muzzle { get; private set; }
        public WoolVisualRenderer WoolBody { get; private set; }

        public void Build()
        {
            var rig = new GameObject("Rig").transform;
            rig.SetParent(transform, false);

            BodyPivot = new GameObject("BodyPivot").transform;
            BodyPivot.SetParent(rig, false);
            BodyPivot.localPosition = new Vector3(0f, _bodyHeight, 0f);

            WoolBody = GetComponentInChildren<WoolVisualRenderer>(true);
            if (WoolBody != null)
            {
                WoolBody.transform.SetParent(BodyPivot, false);
                WoolBody.transform.localPosition = Vector3.zero;
                WoolBody.transform.localRotation = Quaternion.identity;
            }

            BuildLegs(rig);
            BuildHead(rig);
            BuildTail(rig);
        }

        GameObject Part(string name, Transform parent, Mesh mesh, Material mat, Vector3 pos, Quaternion rot, bool shadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        void BuildLegs(Transform rig)
        {
            var leg = ProceduralMeshFactory.Cylinder("Leg", 0.068f, 0.075f, 0.62f, 18);
            var hoof = ProceduralMeshFactory.Cylinder("Hoof", 0.082f, 0.072f, 0.075f, 18);
            Vector3[] spots =
            {
                new Vector3(0.4f, 0f, 0.2f), new Vector3(0.4f, 0f, -0.2f),
                new Vector3(-0.42f, 0f, 0.21f), new Vector3(-0.42f, 0f, -0.21f),
            };
            for (int i = 0; i < spots.Length; i++)
            {
                var l = Part("Leg_" + i, rig, leg, _skinMaterial, spots[i] + Vector3.up * 0.06f, Quaternion.identity);
                Part("Hoof_" + i, rig, hoof, _hoofMaterial, spots[i], Quaternion.identity);
                l.transform.localRotation = Quaternion.Euler(0f, 0f, spots[i].x > 0 ? -2f : 2f);
            }
        }

        void BuildHead(Transform rig)
        {
            HeadPivot = new GameObject("HeadPivot").transform;
            HeadPivot.SetParent(rig, false);
            HeadPivot.localPosition = new Vector3(_surface.RadiusX * 0.92f, _bodyHeight + 0.24f, 0f);

            var headMesh = ProceduralMeshFactory.SuperEllipsoid("Head", new Vector3(0.235f, 0.23f, 0.205f), 2.2f, 30, 20);
            Part("Head", HeadPivot, headMesh, _skinMaterial, new Vector3(0.12f, 0f, 0f), Quaternion.Euler(0, 0, -12f));

            var muzzleMesh = ProceduralMeshFactory.Ellipsoid("Muzzle", new Vector3(0.15f, 0.125f, 0.15f));
            Muzzle = Part("Muzzle", HeadPivot, muzzleMesh, _skinMaterial, new Vector3(0.3f, -0.08f, 0f), Quaternion.identity).transform;
            var noseMesh = ProceduralMeshFactory.Ellipsoid("Nose", new Vector3(0.028f, 0.022f, 0.045f));
            Part("Nose", Muzzle, noseMesh, _noseMaterial, new Vector3(0.135f, 0.04f, 0f), Quaternion.Euler(0, 0, -20f), false);
            var mouthMesh = ProceduralMeshFactory.Ellipsoid("Mouth", new Vector3(0.012f, 0.008f, 0.04f));
            Part("Mouth", Muzzle, mouthMesh, _eyeDarkMaterial, new Vector3(0.12f, -0.045f, 0f), Quaternion.Euler(0, 0, -35f), false);

            // Big, expressive eyes
            var eyeWhite = ProceduralMeshFactory.Ellipsoid("EyeWhite", new Vector3(0.05f, 0.068f, 0.06f));
            var pupil = ProceduralMeshFactory.Ellipsoid("Pupil", new Vector3(0.03f, 0.05f, 0.042f));
            var glint = ProceduralMeshFactory.Ellipsoid("Glint", new Vector3(0.012f, 0.014f, 0.012f));
            LeftEye = BuildEye("EyeL", eyeWhite, pupil, glint, new Vector3(0.24f, 0.07f, 0.115f), 1f);
            RightEye = BuildEye("EyeR", eyeWhite, pupil, glint, new Vector3(0.24f, 0.07f, -0.115f), -1f);

            // Ears (pink inner)
            var earOuter = ProceduralMeshFactory.Ellipsoid("Ear", new Vector3(0.15f, 0.035f, 0.07f));
            var earInner = ProceduralMeshFactory.Ellipsoid("EarInner", new Vector3(0.12f, 0.02f, 0.05f));
            LeftEar = BuildEar("EarL", earOuter, earInner, new Vector3(0.04f, 0.12f, 0.19f), 1f);
            RightEar = BuildEar("EarR", earOuter, earInner, new Vector3(0.04f, 0.12f, -0.19f), -1f);

            // Wool tuft on the forehead (decorative, not shearable)
            // Puffy crown of the same cartoon puffs as the coat (reference: topknot between the ears).
            var crown = WoolPuffMeshBuilder.BuildCluster("HeadCrown", 11, 0.1f, 0.05f, 0.22f, new Vector3(1.1f, 0.55f, 1.25f), 4242, true);
            Part("HeadCrown", HeadPivot, crown, _woolSoftMaterial, new Vector3(0.11f, 0.22f, 0f), Quaternion.Euler(0, 0, -18f));
        }

        Transform BuildEye(string name, Mesh white, Mesh pupil, Mesh glint, Vector3 pos, float side)
        {
            var pivot = new GameObject(name).transform;
            pivot.SetParent(HeadPivot, false);
            pivot.localPosition = pos;
            pivot.localRotation = Quaternion.Euler(0f, -side * 28f, 0f);
            Part("White", pivot, white, _eyeWhiteMaterial, Vector3.zero, Quaternion.identity, false);
            Part("Pupil", pivot, pupil, _eyeDarkMaterial, new Vector3(0.028f, -0.004f, 0f), Quaternion.identity, false);
            Part("Glint", pivot, glint, _eyeWhiteMaterial, new Vector3(0.055f, 0.02f, side * 0.008f), Quaternion.identity, false);
            return pivot;
        }

        Transform BuildEar(string name, Mesh outer, Mesh inner, Vector3 pos, float side)
        {
            var pivot = new GameObject(name).transform;
            pivot.SetParent(HeadPivot, false);
            pivot.localPosition = pos;
            pivot.localRotation = Quaternion.Euler(side * -25f, -side * 70f, -20f);
            Part("Outer", pivot, outer, _skinMaterial, new Vector3(0.12f, 0f, 0f), Quaternion.identity);
            Part("Inner", pivot, inner, _earInnerMaterial, new Vector3(0.12f, 0.018f, 0f), Quaternion.identity, false);
            return pivot;
        }

        void BuildTail(Transform rig)
        {
            Tail = new GameObject("TailPivot").transform;
            Tail.SetParent(rig, false);
            Tail.localPosition = new Vector3(-_surface.RadiusX - 0.1f, _bodyHeight + 0.08f, 0f);
            var puff = WoolPuffMeshBuilder.BuildCluster("TailPuff", 5, 0.06f, 0.055f, 0.22f, Vector3.one, 5151, true);
            Part("Tail", Tail, puff, _woolSoftMaterial, new Vector3(-0.04f, 0f, 0f), Quaternion.identity);
        }
    }
}
