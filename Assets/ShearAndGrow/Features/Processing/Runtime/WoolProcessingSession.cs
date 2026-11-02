using System.Collections;
using ShearAndGrow.Economy;
using ShearAndGrow.Shearing;
using UnityEngine;

namespace ShearAndGrow.Processing
{
    /// <summary>
    /// Runs one processing mini-game (Wash or Dry, from <see cref="ProcessingConfig"/>):
    /// reserves the batch → finger/mouse → heightfield raycast → interpolated brush stamps on the CPU mask →
    /// unique-area progress → visual/audio/haptic/particle feedback → at the threshold the batch is converted ONCE
    /// via <see cref="WoolEconomy.CompleteBatch"/>. Leaving early cancels the reservation (wool stays).
    /// The economy is never touched by visuals; visuals never decide progress.
    /// </summary>
    public sealed class WoolProcessingSession : MonoBehaviour
    {
        [Header("Config")]
        [SerializeField] private ProcessingConfig _config;
        [SerializeField] private GraphicsQualityConfig _quality;

        [Header("Scene")]
        [SerializeField] private Camera _camera;
        [SerializeField] private WoolVisualStateController _heap;
        [SerializeField] private ProcessingToolView _tool;
        [SerializeField] private ToolPointerInput _input;
        [SerializeField] private ProcessingVfx _vfx;
        [SerializeField] private ProcessingAudioDirector _audio;
        [SerializeField] private ProcessingHapticsDriver _haptics;
        [SerializeField] private ProcessingHUD _hud;
        [SerializeField] private WashDebris _debris;
        [Tooltip("Camera pitch for the fixed 3/4 top view.")]
        [SerializeField] private float _cameraPitch = 58f;
        [Tooltip("Extra horizontal margin around the heap when fitting the camera (1 = heap touches the screen edges).")]
        [SerializeField] private float _frameMargin = 1.06f;

        WoolProcessingBatch _batch;
        bool _toolDown, _hadHit, _contactPlayed, _completing, _completed, _leaving;
        Vector3 _lastLocal;
        float _workAccum;
        float _nextAccentAt;
        float _workEma;
        float _hintProgressGate = 0.03f;
        string HintKey => "ShearAndGrow.Hint." + _config.Stage;

        public float Progress => _heap != null && _heap.Mask != null ? _heap.Mask.Progress : 0f;
        public bool IsCompleted => _completed;
        public WoolProcessingBatch Batch => _batch;
        public ProcessingConfig Config => _config;
        public int StampCount { get; private set; }
        /// <summary>Share of the heap area the fixed camera can reach (progress is measured over this part only).</summary>
        public float ReachableFraction { get; private set; }

        static int QualityIndex(GraphicsQualityConfig q) =>
            q == null ? 1 : Mathf.Clamp(Application.isMobilePlatform ? q.MobilePreset : q.EditorPreset, 0, q.Presets.Length - 1);

        void Awake()
        {
            int tier = QualityIndex(_quality);
            int puffs = _config.PuffsPerTier[Mathf.Clamp(tier, 0, _config.PuffsPerTier.Length - 1)];
            int segU = tier >= 2 ? 12 : tier == 1 ? 10 : 8, segV = tier >= 2 ? 8 : tier == 1 ? 7 : 6;
            if (_quality != null) Application.targetFrameRate = _quality.Active.TargetFrameRate;
            _heap.Build(_config, _config.Stage, puffs, segU, segV);
            FitCamera();
            ReachableFraction = _heap.Mask.RestrictToVisible(_heap.transform.InverseTransformPoint(_camera.transform.position));

            Vector3 restPos = _heap.transform.position + _camera.transform.right * (_config.HeapHalfSize.x * 0.95f) +
                              Vector3.up * (_config.HeapHeight + 0.18f) - _camera.transform.forward * 0.12f;
            _tool.Build(_config.Stage, _camera, restPos);
            _vfx.Initialise(_config.Stage, HeapEdgePoints(), _quality != null ? _quality.Active.FluffMultiplier : 1f);
            _audio.Initialise(_config);
            _haptics.Initialise(_config.Haptics);
            if (_debris != null)
            {
                if (_config.Stage == ProcessingStage.Wash) _debris.Build(_heap.transform, _heap.Field, _heap.Mask, _config.Seed + 7);
                else _debris.gameObject.SetActive(false);
            }

            _hud.Build(_config.Title, _config.ProgressLabel, _config.HintText, _config.Stage == ProcessingStage.Wash ? UiKit.Water : UiKit.Air);
            _hud.BackPressed += OnBack;
            _hud.LeaveConfirmed += Leave;
            _hud.MenuPressed += () => GoTo(SceneNavigator.Hub);
            _hud.ContinuePressed += OnContinue;

            _input.Pressed += OnPressed;
            _input.Moved += OnMoved;
            _input.Released += OnReleased;
        }

        void Start()
        {
            if (!WoolEconomy.IsReady) { Debug.LogError("[Processing] Economy not initialised (EconomyBootstrap missing)."); return; }
            if (WoolEconomy.Instance.TryBeginBatch(_config.Input, out _batch))
            {
                string unit = _config.Input == WoolType.Dirty ? "Dirty" : "Wet";
                _hud.SetAmount(_batch.Amount + " " + unit + " Wool");
                _hud.ShowHint(PlayerPrefs.GetInt(HintKey, 0) == 0);
                _input.SetEnabled(true);
            }
            else
            {
                _hud.ShowEmpty(_config.EmptyText);
                _input.SetEnabled(false);
            }
            _nextAccentAt = _config.AccentStep;
            StartCoroutine(_hud.Fade(0f, 0.35f));
        }

        void FitCamera()
        {
            Vector3 c = _heap.transform.position + Vector3.up * (_config.HeapHeight * 0.35f);
            float vFov = _camera.fieldOfView * Mathf.Deg2Rad;
            float hFov = 2f * Mathf.Atan(Mathf.Tan(vFov * 0.5f) * Mathf.Max(0.3f, _camera.aspect));
            float half = Mathf.Max(_config.HeapHalfSize.x, _config.HeapHalfSize.y * _camera.aspect) * _frameMargin;
            float dist = half / Mathf.Tan(hFov * 0.5f);
            Quaternion rot = Quaternion.Euler(_cameraPitch, 0f, 0f);
            // shift the view slightly down so the heap sits in the lower-middle (HUD lives at the top)
            _camera.transform.SetPositionAndRotation(c - rot * Vector3.forward * dist + Vector3.forward * (dist * 0.06f), rot);
        }

        Vector3[] HeapEdgePoints()
        {
            var f = _heap.Field;
            var pts = new System.Collections.Generic.List<Vector3>();
            for (int k = 0; k < 48; k++)
            {
                float a = k / 48f * Mathf.PI * 2f;
                Vector3 dir = new Vector3(Mathf.Cos(a) * f.HalfSize.x, 0f, Mathf.Sin(a) * f.HalfSize.y);
                // walk inward until wool starts: drips fall from the rim of the heap
                for (float t = 1f; t > 0.3f; t -= 0.04f)
                {
                    Vector3 p = dir * t;
                    if (f.Sample(p.x, p.z) > 0.01f) { pts.Add(_heap.transform.TransformPoint(new Vector3(p.x, 0.008f, p.z))); break; }
                }
            }
            return pts.ToArray();
        }

        // ------------------------------------------------------------------ input

        void OnPressed(Vector2 screen)
        {
            if (_completing || _batch == null) return;
            _toolDown = true;
            _hadHit = false;
            _contactPlayed = false;
            _audio.SetTool(true);
            // Dry: the dryer "switches on" with a soft click; Wash: contact pulse only when water reaches the wool.
            if (_config.Stage == ProcessingStage.Dry) { _haptics.ToolStarted(); _audio.PlayContact(); _contactPlayed = true; }
            ProcessPointer(screen, true);
        }

        void OnMoved(Vector2 screen) { if (_toolDown) ProcessPointer(screen, false); }

        void OnReleased()
        {
            if (!_toolDown) return;
            _toolDown = false;
            _hadHit = false;
            _audio.SetTool(false);
            _haptics.ToolStopped();
            _vfx.StopEmitting();
            _tool.SetState(false, Vector3.zero);
        }

        void ProcessPointer(Vector2 screen, bool first)
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            Ray ray = _camera.ScreenPointToRay(screen);
            bool hit = _heap.Raycast(ray, out Vector3 worldHit, out Vector3 local);
            Vector3 aim;
            if (hit) aim = worldHit;
            else
            {
                // stream lands on the table/grate: visible, but no processing (no progress, no texture haptics)
                var table = new Plane(Vector3.up, _heap.transform.position);
                aim = table.Raycast(ray, out float d) ? ray.GetPoint(d) : ray.GetPoint(1.2f);
            }
            _tool.SetState(true, aim);

            float work = 0f, newlyDone = 0f;
            if (hit)
            {
                var mask = _heap.Mask;
                float r = _config.BrushRadius;
                if (_hadHit)
                {
                    Vector3 delta = local - _lastLocal;
                    float dist = new Vector2(delta.x, delta.z).magnitude;
                    float spacing = r * _config.StampSpacing;
                    if (dist >= spacing)
                    {
                        // sweeping: evenly spaced stamps along the path (no gaps on fast swipes)
                        int n = Mathf.Min(64, Mathf.FloorToInt(dist / spacing));
                        float timeDose = _config.DosePerSecond * dt / n;
                        for (int i = 1; i <= n; i++)
                        {
                            Vector3 p = Vector3.Lerp(_lastLocal, local, i / (float)n);
                            work += mask.Stamp(p.x, p.z, r, Mathf.Max(_config.MinSweepDose, timeDose), ref newlyDone);
                            StampCount++;
                        }
                        _lastLocal = local;
                    }
                    else
                    {
                        // holding still / slow: time-based dose on the spot
                        work += mask.Stamp(local.x, local.z, r, _config.DosePerSecond * dt, ref newlyDone);
                        StampCount++;
                    }
                }
                else
                {
                    work += mask.Stamp(local.x, local.z, r, _config.DosePerSecond * dt, ref newlyDone);
                    StampCount++;
                    _lastLocal = local;
                }
                _hadHit = true;
            }
            else _hadHit = false;

            _workAccum += work;
            // feedback only for REAL processing of unfinished wool
            if (hit && work > 1e-6f && !_contactPlayed)
            {
                _contactPlayed = true;
                _haptics.ToolStarted();
                _audio.PlayContact();
            }
            Vector3 normal = hit ? _heap.transform.TransformDirection(_heap.Field.Normal(local.x, local.z)) : Vector3.up;
            float intensity = Mathf.Clamp01(work / dt / Mathf.Max(1e-5f, _config.FeedbackRateForMax));
            if (_config.Stage == ProcessingStage.Wash) _vfx.Spray(aim, normal, _tool.StreamDirection, Mathf.Max(0.35f, intensity), dt, hit);
            else if (hit) _vfx.Blow(aim, normal, _tool.StreamDirection, Mathf.Max(0.15f, intensity), dt);
        }

        // ------------------------------------------------------------------ frame

        void Update()
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            float rate = _workAccum / dt;
            _workAccum = 0f;
            _workEma = Mathf.Lerp(_workEma, rate, 1f - Mathf.Exp(-14f * dt));
            float work01 = _toolDown ? Mathf.Clamp01(_workEma / Mathf.Max(1e-5f, _config.FeedbackRateForMax)) : 0f;
            _audio.SetWork(work01);
            if (_toolDown && rate > 1e-5f) _haptics.Work(Mathf.Lerp(0.25f, 1f, work01));

            // air pushes the wool around the dryer; water presses it slightly
            bool wash = _config.Stage == ProcessingStage.Wash;
            _heap.SetTool(_toolDown && !_completing, _tool.IsOn ? AimWorld() : _heap.transform.position + Vector3.up * 10f,
                _tool.StreamDirection, _config.BrushRadius, wash ? 0f : 0.22f, wash ? 0.12f : 0f);
            _heap.Upload();

            if (wash) _vfx.Drips(Progress, dt);
            if (_batch == null || _completed) return;
            float p = Progress;
            _hud.SetProgress(p, _config.CompletionThreshold);
            if (p > _hintProgressGate && PlayerPrefs.GetInt(HintKey, 0) == 0) { PlayerPrefs.SetInt(HintKey, 1); PlayerPrefs.Save(); _hud.ShowHint(false); }
            if (!_completing && p >= _nextAccentAt && p < _config.CompletionThreshold)
            {
                _nextAccentAt += _config.AccentStep;
                _audio.PlayAccent();
                _haptics.Accent();
                _vfx.Sparkle(_tool.IsOn ? AimWorld() : _heap.transform.position, 5, 0.04f);
            }
            if (!_completing && p >= _config.CompletionThreshold) StartCoroutine(CompleteRoutine());
        }

        Vector3 _lastAim;
        Vector3 AimWorld()
        {
            if (_input.IsActive)
            {
                Ray ray = _camera.ScreenPointToRay(_input.Position);
                if (_heap.Raycast(ray, out Vector3 w, out _)) _lastAim = w;
            }
            return _lastAim;
        }

        // ------------------------------------------------------------------ completion

        IEnumerator CompleteRoutine()
        {
            _completing = true;
            _input.SetEnabled(false);
            OnReleased();
            _haptics.StopAll();
            _audio.StopNow();
            _hud.SetProgress(1f, 1f);
            // remaining wet/dirty patches settle smoothly (shows the fully clean / fully fluffy result)
            float t = 0f;
            while (t < 0.7f)
            {
                t += Time.deltaTime;
                _heap.Mask.Settle(Time.deltaTime * 1.6f);
                _heap.Upload();
                yield return null;
            }
            _heap.Mask.Settle(1f);
            _heap.Upload();
            _audio.PlayFinish();
            _haptics.Completed();
            _vfx.Sparkle(_heap.transform.position + Vector3.up * _config.HeapHeight * 0.8f, 26, _config.HeapHalfSize.x * 0.8f);

            // the economy transfer happens exactly once (idempotent service + local guard)
            bool ok = !_completed && WoolEconomy.Instance.CompleteBatch(_batch);
            _completed = true;
            yield return new WaitForSeconds(0.35f);
            if (ok)
            {
                var output = _batch.Output;
                string title = _config.Stage == ProcessingStage.Wash ? "Squeaky clean!" : "Soft & fluffy!";
                string body = "+" + _batch.Amount + " " + (output == WoolType.Wet ? "Wet" : "Clean") + " Wool";
                string next = output == WoolType.Wet ? "Dry now ▸" : "Sell ▸";
                _hud.ShowResult(title, body, next);
            }
            else _hud.ShowResult("Done", "", "");
        }

        void OnContinue()
        {
            if (_batch == null) return;
            GoTo(_batch.Output == WoolType.Wet ? SceneNavigator.Dry : SceneNavigator.Hub);
        }

        // ------------------------------------------------------------------ leaving

        void OnBack()
        {
            if (_leaving) return;
            if (_completed || _batch == null || Progress < 0.01f) { Leave(); return; }
            _input.Cancel();
            _hud.ShowConfirm();
        }

        void Leave()
        {
            if (_leaving) return;
            if (_batch != null && _batch.IsOpen) WoolEconomy.Instance.CancelBatch(_batch); // wool stays with the player
            GoTo(SceneNavigator.Hub);
        }

        void GoTo(string scene)
        {
            if (_leaving) return;
            _leaving = true;
            _input.SetEnabled(false);
            _audio.StopNow();
            _haptics.StopAll();
            StartCoroutine(GoRoutine(scene));
        }

        IEnumerator GoRoutine(string scene)
        {
            yield return _hud.Fade(1f, 0.25f);
            if (!SceneNavigator.Go(scene)) { _leaving = false; yield return _hud.Fade(0f, 0.2f); _input.SetEnabled(!_completed && _batch != null); }
        }

        void OnApplicationPause(bool paused) { if (paused) OnReleased(); }
        void OnApplicationFocus(bool focus) { if (!focus) OnReleased(); }

        void OnDestroy()
        {
            // scene unloaded without completing: release the reservation (nothing was converted)
            if (_batch != null && _batch.IsOpen && WoolEconomy.IsReady) WoolEconomy.Instance.CancelBatch(_batch);
        }

        // ------------------------------------------------------------------ QA hooks

        /// <summary>QA: a finger stroke between two screen points through the real input path.</summary>
        public void QA_Stroke(Vector2 from, Vector2 to, int frames)
        {
            OnPressed(from);
            for (int i = 1; i <= frames; i++) OnMoved(Vector2.Lerp(from, to, i / (float)frames));
            OnReleased();
        }

        public void QA_Press(Vector2 p) => OnPressed(p);
        public void QA_Move(Vector2 p) => OnMoved(p);
        public void QA_Release() => OnReleased();
        public void QA_Back() => OnBack();
        public Camera Camera => _camera;
        public WoolVisualStateController Heap => _heap;
        public ProcessingHapticsDriver Haptics => _haptics;
        public ProcessingAudioDirector Audio => _audio;
        public ProcessingVfx Vfx => _vfx;
        public WashDebris Debris => _debris;
    }
}
