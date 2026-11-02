using System.Collections.Generic;
using UnityEngine;

namespace ShearAndGrow.Shearing
{
    public enum SoftClumpPhase : byte { Pooled, Hanging, Falling, Settled }

    /// <summary>One pooled, deformable clump object. Vertices are written in world space (identity transform).</summary>
    public sealed class SoftClumpInstance
    {
        public GameObject GameObject;
        public MeshRenderer Renderer;
        public Mesh Mesh;
        public ClumpVariant Variant;
        public ClumpNode Node;
        public SoftClumpPhase Phase;
        public readonly Vector3[] P = new Vector3[ClumpMeshLibrary.ParticleCount];
        public readonly Vector3[] Prev = new Vector3[ClumpMeshLibrary.ParticleCount];
        public readonly float[] RestDist = new float[ClumpMeshLibrary.ParticleCount * ClumpMeshLibrary.ParticleCount];
        public Vector3[] Verts;
        public Vector3[] Normals;
        public float Scale;
        public float Timer;
        public float StillTime;
        public float AirTime;
        public float LandTime;
        public bool Grounded;
        public bool Landed;
        public Vector3 AnchorLocal;
        public float RopeLength;
        public Vector3 SpinAxis;
        public float SupportCheckTimer;
        public Bounds WorldBounds;
        public Quaternion RestFrameInv;
    }

    /// <summary>
    /// Hybrid soft-body solver (spec §6.2): 7 control particles per clump with pairwise distance constraints and
    /// soft shape matching, verlet integration at a fixed step, air drag + tumble, heightfield floor & ellipsoid sheep
    /// collisions, zero restitution. HERO quality for the first N active clumps, cheaper STANDARD for the rest.
    /// </summary>
    public sealed class SoftClumpSimulator : MonoBehaviour
    {
        ChunkPhysicsConfig _cfg;
        ClumpVariant[] _variants;
        Material _material;
        FloorWoolAccumulator _floor;
        Transform _body;
        SheepBodySurface _surface;
        float _woolThickness;
        WoolClumpAttachmentGraph _graph;
        ShearingEventHub _hub;
        System.Random _rng;
        int _maxHero;

        readonly Stack<SoftClumpInstance> _pool = new Stack<SoftClumpInstance>(256);
        readonly List<SoftClumpInstance> _active = new List<SoftClumpInstance>(64);
        float _accumulator;
        Vector3 _ellipsoidRadii;

        public int ActiveCount => _active.Count;
        public int HangingCount { get; private set; }
        public int FallingCount { get; private set; }
        public int PoolAvailable => _pool.Count;
        public int TotalDetached { get; private set; }

        public void Initialise(ChunkPhysicsConfig cfg, ClumpVariant[] variants, Material material, FloorWoolAccumulator floor,
            Transform body, SheepBodySurface surface, float woolThickness, WoolClumpAttachmentGraph graph, ShearingEventHub hub, int seed, int maxHero)
        {
            _cfg = cfg;
            _variants = variants;
            _material = material;
            _floor = floor;
            _body = body;
            _surface = surface;
            _woolThickness = woolThickness;
            _graph = graph;
            _hub = hub;
            _rng = new System.Random(seed);
            _maxHero = maxHero;
            _ellipsoidRadii = new Vector3(surface.RadiusX, surface.RadiusY, surface.RadiusZ) + Vector3.one * woolThickness * 0.25f;
            floor.Initialise(cfg, material, ReturnToPool);

            // Pre-allocate everything: no Instantiate/Destroy per stamp (spec §13.1).
            for (int i = 0; i < cfg.PoolSize; i++) _pool.Push(CreateInstance(i));
        }

        public void SetMaxHero(int maxHero) => _maxHero = Mathf.Max(1, maxHero);

        SoftClumpInstance CreateInstance(int i)
        {
            var go = new GameObject("WoolClump_" + i);
            go.transform.SetParent(transform, false);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            var mesh = new Mesh { name = "WoolClumpDyn_" + i };
            mesh.MarkDynamic();
            mf.sharedMesh = mesh;
            go.SetActive(false);
            return new SoftClumpInstance { GameObject = go, Renderer = mr, Mesh = mesh, Phase = SoftClumpPhase.Pooled };
        }

        void ReturnToPool(SoftClumpInstance c)
        {
            c.Phase = SoftClumpPhase.Pooled;
            c.Node = null;
            c.GameObject.SetActive(false);
            _pool.Push(c);
        }

        /// <summary>Begins the Hanging phase for a node whose support just dropped (location = its own wool region).</summary>
        public bool SpawnHanging(ClumpNode node, Vector3 strokeTangentWorld)
        {
            if (_pool.Count == 0)
            {
                // Should not happen thanks to floor merging; release the node silently rather than spawning from nowhere.
                _graph.MarkReleased(node);
                return false;
            }
            var c = _pool.Pop();
            var variant = _variants[node.VariantIndex % _variants.Length];
            if (c.Variant != variant)
            {
                c.Variant = variant;
                c.Mesh.Clear();
                c.Verts = new Vector3[variant.RestVertices.Length];
                c.Normals = new Vector3[variant.RestVertices.Length];
                c.Mesh.vertices = variant.RestVertices;
                c.Mesh.normals = variant.RestNormals;
                c.Mesh.colors = variant.RestMesh.colors;
                c.Mesh.uv = variant.RestMesh.uv;
                c.Mesh.uv2 = variant.RestMesh.uv2; // (-1, seed): baked cartoon puff shading
                c.Mesh.triangles = variant.RestMesh.triangles;
            }
            c.Node = node;
            c.Scale = node.Scale * (_body.lossyScale.x);
            c.Timer = 0f;
            c.StillTime = 0f;
            c.AirTime = 0f;
            c.LandTime = 0f;
            c.Grounded = false;
            c.Landed = false;
            c.SupportCheckTimer = 0f;

            Vector3 centreWorld = _body.TransformPoint(node.CentreLocal + node.NormalLocal * _woolThickness * 0.55f);
            Vector3 normalWorld = _body.TransformDirection(node.NormalLocal).normalized;
            Vector3 fwd = Vector3.ProjectOnPlane(strokeTangentWorld.sqrMagnitude > 1e-6f ? strokeTangentWorld : Vector3.forward, normalWorld);
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.ProjectOnPlane(Vector3.right, normalWorld);
            float yaw = (float)_rng.NextDouble() * 360f;
            Quaternion rot = Quaternion.LookRotation(fwd.normalized, normalWorld) * Quaternion.Euler(0f, yaw, 0f);

            for (int i = 0; i < ClumpMeshLibrary.ParticleCount; i++)
            {
                c.P[i] = centreWorld + rot * (variant.RestParticles[i] * c.Scale);
                c.Prev[i] = c.P[i];
            }
            for (int i = 0; i < ClumpMeshLibrary.ParticleCount; i++)
                for (int j = 0; j < ClumpMeshLibrary.ParticleCount; j++)
                    c.RestDist[i * ClumpMeshLibrary.ParticleCount + j] = (variant.RestParticles[i] - variant.RestParticles[j]).magnitude * c.Scale;
            c.RestFrameInv = Quaternion.Inverse(FrameFrom(variant.RestParticles[0], variant.RestParticles[variant.PinParticle], variant.RestParticles[3]));

            Vector3 upLocal = _body.InverseTransformDirection(Vector3.up).normalized;
            if (!_graph.TryFindAnchor(node, upLocal, out c.AnchorLocal))
                c.AnchorLocal = node.CentreLocal;
            c.AnchorLocal += node.NormalLocal * _woolThickness * 0.5f;
            Vector3 anchorWorld = _body.TransformPoint(c.AnchorLocal);
            c.RopeLength = (anchorWorld - c.P[variant.PinParticle]).magnitude + _cfg.MaxHangStretch * (node.IsHero ? 1.4f : 1f) * c.Scale * 4f;
            c.SpinAxis = Random.onUnitSphere;
            c.Phase = SoftClumpPhase.Hanging;
            c.GameObject.SetActive(true);
            _active.Add(c);
            UpdateMesh(c);
            _hub?.RaiseClump(new ClumpEvent { Type = ClumpEventType.BeganHanging, Position = centreWorld, Size = node.IsHero ? 1.6f : 1f });
            return true;
        }

        void Update()
        {
            if (_cfg == null) return;
            _accumulator += Mathf.Min(Time.deltaTime, _cfg.FixedStep * _cfg.MaxSubsteps);
            int steps = 0;
            while (_accumulator >= _cfg.FixedStep && steps < _cfg.MaxSubsteps)
            {
                Step(_cfg.FixedStep);
                _accumulator -= _cfg.FixedStep;
                steps++;
            }
            if (steps == _cfg.MaxSubsteps) _accumulator = 0f;

            int hanging = 0, falling = 0;
            for (int i = 0; i < _active.Count; i++)
            {
                var c = _active[i];
                if (c.Phase == SoftClumpPhase.Hanging) hanging++; else if (c.Phase == SoftClumpPhase.Falling) falling++;
                UpdateMesh(c);
            }
            HangingCount = hanging;
            FallingCount = falling;
        }

        void Step(float dt)
        {
            int heroUsed = 0;
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var c = _active[i];
                bool hero = heroUsed < _maxHero;
                if (hero) heroUsed++;
                StepClump(c, dt, hero);
                if (c.Phase == SoftClumpPhase.Settled)
                {
                    _active.RemoveAt(i);
                    UpdateMesh(c);
                    _floor.RegisterSettled(c);
                    _hub?.RaiseClump(new ClumpEvent { Type = ClumpEventType.Settled, Position = c.WorldBounds.center, Size = c.Node != null && c.Node.IsHero ? 1.6f : 1f });
                }
            }
        }

        void StepClump(SoftClumpInstance c, float dt, bool hero)
        {
            var v = c.Variant;
            int n = ClumpMeshLibrary.ParticleCount;
            c.Timer += dt;

            if (c.Phase == SoftClumpPhase.Hanging)
            {
                c.SupportCheckTimer -= dt;
                bool detach = c.Timer >= c.Node.HangDuration;
                if (!detach && c.SupportCheckTimer <= 0f)
                {
                    c.SupportCheckTimer = 0.05f;
                    float support = _graph.CurrentSupport(c.Node);
                    Vector3 upLocal = _body.InverseTransformDirection(Vector3.up).normalized;
                    bool anchored = _graph.TryFindAnchor(c.Node, upLocal, out Vector3 newAnchor);
                    if (anchored) c.AnchorLocal = newAnchor + c.Node.NormalLocal * _woolThickness * 0.5f;
                    if ((support < _cfg.DetachThreshold || !anchored) && c.Timer >= _cfg.MinHang) detach = true;
                }
                if (detach) Detach(c, dt);
            }

            float drag = Mathf.Clamp01(_cfg.AirDrag * dt);
            // after touching the floor/pile wool loses energy quickly: short squash, then rest (no rolling balls)
            if (c.Landed) drag = Mathf.Max(drag, Mathf.Clamp01(_cfg.LandedDamping * dt));
            Vector3 gravity = Vector3.down * _cfg.Gravity * dt * dt;
            Vector3 centre = Vector3.zero;
            for (int i = 0; i < n; i++) centre += c.P[i];
            centre /= n;

            // Verlet integrate with drag, gravity and a gentle tumble (lazy, floaty fall – not a stone).
            for (int i = 0; i < n; i++)
            {
                Vector3 vel = (c.P[i] - c.Prev[i]) * (1f - drag);
                Vector3 acc = gravity;
                if (c.Phase == SoftClumpPhase.Falling && !c.Grounded)
                {
                    Vector3 r = c.P[i] - centre;
                    acc += Vector3.Cross(c.SpinAxis, r) * (_cfg.TumbleStrength * dt * dt * 6f);
                    // air resistance stronger for big clumps: slower terminal velocity
                    acc -= vel * (0.6f * (c.Node != null && c.Node.IsHero ? 1.3f : 1f)) * dt;
                }
                c.Prev[i] = c.P[i];
                c.P[i] += vel + acc;
            }

            int iterations = hero ? _cfg.HeroIterations : _cfg.StandardIterations;
            float shapeK = c.Phase == SoftClumpPhase.Hanging ? _cfg.HangingStiffness : (hero ? _cfg.ShapeStiffness : Mathf.Max(_cfg.ShapeStiffness, 0.75f));
            float distK = c.Phase == SoftClumpPhase.Hanging ? 0.25f : 0.5f;
            Vector3 anchorWorld = _body.TransformPoint(c.AnchorLocal);

            for (int it = 0; it < iterations; it++)
            {
                // pairwise distance (stretch/bend) constraints
                for (int a = 0; a < n; a++)
                    for (int b = a + 1; b < n; b++)
                    {
                        Vector3 d = c.P[b] - c.P[a];
                        float len = d.magnitude;
                        if (len < 1e-6f) continue;
                        float rest = c.RestDist[a * n + b];
                        Vector3 corr = d * ((len - rest) / len * 0.5f * distK);
                        c.P[a] += corr;
                        c.P[b] -= corr;
                    }

                // soft shape matching to keep volume / silhouette
                Quaternion frame = FrameFrom(c.P[0], c.P[v.PinParticle], c.P[3]) * c.RestFrameInv;
                Vector3 cm = Vector3.zero;
                for (int i = 0; i < n; i++) cm += c.P[i];
                cm /= n;
                Vector3 restCm = Vector3.zero;
                for (int i = 0; i < n; i++) restCm += v.RestParticles[i];
                restCm /= n;
                for (int i = 0; i < n; i++)
                {
                    Vector3 goal = cm + frame * ((v.RestParticles[i] - restCm) * c.Scale);
                    c.P[i] += (goal - c.P[i]) * shapeK;
                }

                // hanging rope: pin particle can stretch up to RopeLength from the remaining wool anchor
                if (c.Phase == SoftClumpPhase.Hanging)
                {
                    int pin = v.PinParticle;
                    Vector3 toPin = c.P[pin] - anchorWorld;
                    float len = toPin.magnitude;
                    if (len > c.RopeLength) c.P[pin] = anchorWorld + toPin * (c.RopeLength / len);
                }

                Collide(c);
            }

            // sleep detection
            if (c.Phase == SoftClumpPhase.Falling)
            {
                c.AirTime += dt;
                if (c.Landed) c.LandTime += dt;
                // centre-of-mass speed: internal shape jiggle must not keep a resting clump awake
                Vector3 cmNow = Vector3.zero, cmPrev = Vector3.zero;
                for (int i = 0; i < n; i++) { cmNow += c.P[i]; cmPrev += c.Prev[i]; }
                float speed = ((cmNow - cmPrev) / n).magnitude / dt;
                if (c.Grounded && speed < _cfg.SleepVelocity) c.StillTime += dt; else c.StillTime = 0f;
                if (c.StillTime >= _cfg.SleepTime || c.LandTime >= _cfg.MaxSettleTime || c.AirTime > _cfg.MaxAirTime)
                {
                    c.Phase = SoftClumpPhase.Settled;
                }
            }
        }

        void Detach(SoftClumpInstance c, float dt)
        {
            c.Phase = SoftClumpPhase.Falling;
            c.Timer = 0f;
            _graph.MarkReleased(c.Node);
            Vector3 normalWorld = _body.TransformDirection(c.Node.NormalLocal).normalized;
            Vector3 tangent = Vector3.ProjectOnPlane(Random.insideUnitSphere, normalWorld).normalized;
            Vector3 vel = normalWorld * _cfg.DetachImpulse + tangent * _cfg.TangentImpulse * 0.35f;
            for (int i = 0; i < ClumpMeshLibrary.ParticleCount; i++) c.Prev[i] = c.P[i] - vel * dt;
            c.SpinAxis = Vector3.Cross(normalWorld, Vector3.up).normalized + Random.insideUnitSphere * 0.4f;
            TotalDetached++;
            _hub?.RaiseClump(new ClumpEvent { Type = ClumpEventType.Detached, Position = c.P[0], Size = c.Node.IsHero ? 1.6f : 1f });
        }

        /// <summary>Adds the stroke tangent momentum to hanging clumps near the trimmer (spec: tangent impulse, not a cork).</summary>
        public void ApplyStrokeImpulse(Vector3 worldPoint, Vector3 tangentWorld, float radius)
        {
            float dt = _cfg.FixedStep;
            for (int i = 0; i < _active.Count; i++)
            {
                var c = _active[i];
                if (c.Phase != SoftClumpPhase.Hanging) continue;
                if ((c.P[0] - worldPoint).sqrMagnitude > radius * radius * 9f) continue;
                Vector3 dv = tangentWorld * (_cfg.TangentImpulse * dt * 0.15f);
                for (int p = 0; p < ClumpMeshLibrary.ParticleCount; p++) c.Prev[p] -= dv;
            }
        }

        void Collide(SoftClumpInstance c)
        {
            int n = ClumpMeshLibrary.ParticleCount;
            bool grounded = false;
            float particleRadius = c.Scale * 0.16f;
            for (int i = 0; i < n; i++)
            {
                Vector3 p = c.P[i];
                // floor + growing pile (heightfield): zero restitution, high friction -> squash then settle
                float ground = _cfg.FloorY + _floor.SampleHeight(p.x, p.z) + particleRadius;
                if (p.y < ground)
                {
                    p.y = ground;
                    Vector3 prev = c.Prev[i];
                    prev.y = Mathf.Max(prev.y, p.y); // kill downward velocity, no bounce
                    prev.x = Mathf.Lerp(prev.x, p.x, _cfg.GroundFriction);
                    prev.z = Mathf.Lerp(prev.z, p.z, _cfg.GroundFriction);
                    c.Prev[i] = prev;
                    grounded = true;
                }
                // keep inside the stage
                p.x = Mathf.Clamp(p.x, -_cfg.FloorHalfExtents.x, _cfg.FloorHalfExtents.x);
                p.z = Mathf.Clamp(p.z, -_cfg.FloorHalfExtents.y, _cfg.FloorHalfExtents.y);

                // sheep body (ellipsoid around undercoat + part of remaining wool)
                if (c.Phase == SoftClumpPhase.Falling)
                {
                    Vector3 local = _body.InverseTransformPoint(p);
                    float f = (local.x * local.x) / (_ellipsoidRadii.x * _ellipsoidRadii.x)
                            + (local.y * local.y) / (_ellipsoidRadii.y * _ellipsoidRadii.y)
                            + (local.z * local.z) / (_ellipsoidRadii.z * _ellipsoidRadii.z);
                    if (f < 1f && f > 1e-5f)
                    {
                        local /= Mathf.Sqrt(f);
                        Vector3 world = _body.TransformPoint(local);
                        c.Prev[i] = Vector3.Lerp(c.Prev[i], world, 0.5f);
                        p = world;
                    }
                }
                c.P[i] = p;
            }
            if (grounded && !c.Landed && c.Phase == SoftClumpPhase.Falling)
            {
                c.Landed = true;
                _hub?.RaiseClump(new ClumpEvent { Type = ClumpEventType.Landed, Position = c.P[0], Size = c.Node != null && c.Node.IsHero ? 1.6f : 1f });
            }
            c.Grounded = grounded;
        }

        static Quaternion FrameFrom(Vector3 origin, Vector3 upPoint, Vector3 sidePoint)
        {
            Vector3 up = upPoint - origin;
            Vector3 side = sidePoint - origin;
            if (up.sqrMagnitude < 1e-10f) up = Vector3.up;
            Vector3 fwd = Vector3.Cross(side, up);
            if (fwd.sqrMagnitude < 1e-10f) fwd = Vector3.forward;
            return Quaternion.LookRotation(fwd.normalized, up.normalized);
        }

        void UpdateMesh(SoftClumpInstance c)
        {
            var v = c.Variant;
            Quaternion frame = FrameFrom(c.P[0], c.P[v.PinParticle], c.P[3]) * c.RestFrameInv;
            var rv= v.RestVertices;
            var rn = v.RestNormals;
            var rp = v.RestParticles;
            var wi = v.WeightIndex;
            var wv = v.WeightValue;
            float s = c.Scale;
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 max = -min;
            for (int k = 0; k < rv.Length; k++)
            {
                int b = k * 3;
                int i0 = wi[b], i1 = wi[b + 1], i2 = wi[b + 2];
                Vector3 p = wv[b] * (c.P[i0] + frame * ((rv[k] - rp[i0]) * s))
                          + wv[b + 1] * (c.P[i1] + frame * ((rv[k] - rp[i1]) * s))
                          + wv[b + 2] * (c.P[i2] + frame * ((rv[k] - rp[i2]) * s));
                c.Verts[k] = p;
                c.Normals[k] = frame * rn[k];
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            c.Mesh.SetVertices(c.Verts);
            c.Mesh.SetNormals(c.Normals);
            var bounds = new Bounds((min + max) * 0.5f, max - min);
            c.Mesh.bounds = bounds;
            c.WorldBounds = bounds;
        }

        /// <summary>Session closed: everything still in flight goes back to the pool (frame is hidden at this point).</summary>
        public void ClearAfterSessionClosed()
        {
            for (int i = 0; i < _active.Count; i++) ReturnToPool(_active[i]);
            _active.Clear();
            _floor.ClearFloorAfterSessionClosed();
            _accumulator = 0f;
        }
    }
}
