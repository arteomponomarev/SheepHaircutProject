using System.Collections;
using UnityEngine;

namespace ShearAndGrow.Shearing
{
    /// <summary>
    /// Owns the TRANSIENT shearing session (spec §11/§12): wires input → surface mapper → mask authority → CutDelta events
    /// (visual mask, clumps, audio, haptics, progress), handles Back/Save and Finish, commits only NEW unique cut area
    /// once to the PERSISTENT sheep state, and clears floor clumps only after the frame is hidden.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class ShearingSessionController : MonoBehaviour
    {
        [Header("Config")]
        [SerializeField] private ShearingFeelConfig _feel;
        [SerializeField] private ChunkPhysicsConfig _chunkPhysics;
        [SerializeField] private HapticsConfig _hapticsConfig;
        [Tooltip("Start state for brand-new players (read-only data shipped in the build).")]
        [SerializeField] private DefaultGameState _defaultState;
        [Tooltip("Wool regrowth settings (shared with the farm).")]
        [SerializeField] private WoolGrowthConfig _growth;
        [Tooltip("Trimmer upgrades (Trimmer Workshop). Owned level comes from the economy save.")]
        [SerializeField] private ToolUpgradeConfig _tools;

        /// <summary>Real brush radius of this session (base × trimmer upgrade). Used for cutting, visuals and clumps.</summary>
        float _brushRadius;
        public float BrushRadius => _brushRadius;
        public int TrimmerLevel { get; private set; } = 1;
        [Tooltip("Belly-roll pose controller on the sheep root (added at runtime if missing).")]
        [SerializeField] private SheepPoseController _pose;
        [SerializeField] private ShearingAudioConfig _audio;
        [SerializeField] private GraphicsQualityConfig _quality;

        [Header("Scene references")]
        [SerializeField] private Camera _camera;
        [SerializeField] private SheepRigBuilder _sheep;
        [SerializeField] private WoolVisualRenderer _woolVisual;
        [SerializeField] private TrimmerViewController _trimmer;
        [SerializeField] private ShearingInputAdapter _input;
        [SerializeField] private ShearingCameraOrbit _orbit;
        [SerializeField] private SoftClumpSimulator _clumps;
        [SerializeField] private FloorWoolAccumulator _floor;
        [SerializeField] private WoolFluffVfx _fluff;
        [SerializeField] private ShearingAudioDirector _audioDirector;
        [SerializeField] private ShearingHapticsController _haptics;
        [SerializeField] private SheepReactionAnimator _reactions;
        [SerializeField] private ShearingHUD _hud;
        [SerializeField] private ShearingDebugHUD _debugHud;

        [Header("Materials")]
        [SerializeField] private Material _clumpMaterial;

        [Header("Economy hook (not balanced here)")]
        [Tooltip("Yield credited for 100% unique cut area. Economy is out of scope; field exists for integration.")]
        [SerializeField] private float _yieldPerFullShear = 1f;
        [SerializeField] private float _finishCelebrationSeconds = 1.8f;
        [SerializeField] private float _fadeSeconds = 0.35f;

        public const string DebugPrefKey = "ShearAndGrow.DebugHud";

        ShearingEventHub _hub;
        ShearMaskAuthority _mask;
        WoolClumpAttachmentGraph _graph;
        ShearingSurfaceMapper _mapper;
        SheepWoolState _state;
        ShearingSurfaceMapper.StampHandler _stampHandler;

        bool _inContact;
        bool _bareContact;
        bool _hintVisible = true;
        bool _closing;
        bool _finishing;
        bool _thresholdReached;
        float _frameNewArea;
        Vector3 _frameTangentWorld;
        Vector3 _lastStampWorld;
        Vector3 _lastNormalWorld;

        // ---- diagnostics (debug HUD / QA)
        public ShearingEventHub Hub => _hub;
        public ShearMaskAuthority Mask => _mask;
        public SheepWoolState State => _state;
        public SoftClumpSimulator Clumps => _clumps;
        public FloorWoolAccumulator Floor => _floor;
        public ShearingHapticsController Haptics => _haptics;
        public ShearingAudioDirector AudioDirector => _audioDirector;
        public float LastFrameNewArea { get; private set; }
        public float Progress => _mask != null ? _mask.CutFraction : 0f;
        public bool InContact => _inContact;
        public bool IsClosing => _closing;
        public int CutEventCount { get; private set; }
        public int SessionIndex { get; private set; }
        public string LastRestoreInfo { get; private set; } = "";
        public ShearingSurfaceMapper Mapper => _mapper;
        public ShearingFeelConfig Feel => _feel;
        public Camera Camera => _camera;

        void Awake()
        {
            _hub = new ShearingEventHub();
            var preset = _quality.Active;
            Application.targetFrameRate = preset.TargetFrameRate;

            // Which sheep to shear is chosen on the farm; the scene default is used when started directly.
            _sheep.OverrideIdentity(ShearAndGrow.Economy.LaunchContext.ConsumeSheepId());
            _sheep.Build();
            int w = Mathf.Max(64, _feel.MaskWidth);
            int h = w / 2;
            _woolVisual.Build(_sheep.Surface, _feel, preset, w, h);
            _mask = new ShearMaskAuthority(_sheep.Surface, w, h);

            var variants = ClumpMeshLibrary.Build(7, _feel.SessionSeed);
            _graph = new WoolClumpAttachmentGraph(_sheep.Surface, _mask, _chunkPhysics, variants.Length, _feel.SessionSeed ^ _sheep.SheepId.GetHashCode());
            _clumps.Initialise(_chunkPhysics, variants, _clumpMaterial, _floor, _woolVisual.Body, _sheep.Surface, _feel.WoolThickness, _graph, _hub, _feel.SessionSeed, preset.MaxHeroActive);

            _mapper = new ShearingSurfaceMapper(_camera, _woolVisual.ShearCollider, _woolVisual.Body, _sheep.Surface, _feel);
            _stampHandler = OnStamp;

            // trimmer upgrade (Trimmer Workshop): widens the REAL cut (stamp radius, spacing, visual ring, clumps).
            // Payment stays per removed wool volume, so a wider head is faster but never pays more for the same wool.
            TrimmerLevel = _tools != null && ShearAndGrow.Economy.WoolEconomy.IsReady ? ShearAndGrow.Economy.WoolEconomy.Instance.ToolLevel(_tools.ToolId) : 1;
            _brushRadius = _tools != null ? _tools.EffectiveRadius(_feel.BrushRadius, TrimmerLevel) : _feel.BrushRadius;
            _mapper.SetBrushRadius(_brushRadius);

            _trimmer.Initialise(_camera);
            _trimmer.SetHeadWidth(_brushRadius / Mathf.Max(1e-4f, _feel.BrushRadius));
            _fluff.Initialise(preset.FluffMultiplier);

            _haptics.Initialise(_hapticsConfig, _hub);
            _audioDirector.Initialise(_audio, _hub, _camera.transform, _feel.SessionSeed);
            _reactions.Initialise(_sheep, _trimmer.CutterTip, _camera.transform, _hub, _feel.SessionSeed);

            // Belly roll: the pose controller rotates the sheep root around the body centre.
            if (_pose == null) _pose = _sheep.GetComponent<SheepPoseController>();
            if (_pose == null) _pose = _sheep.gameObject.AddComponent<SheepPoseController>();
            _pose.Initialise(_sheep.transform, _sheep.transform.InverseTransformPoint(_sheep.BodyPivot.position));
            _orbit.Initialise(_pose);

            bool devTools = Debug.isDebugBuild; // Editor + Development builds only
            bool debugOn = devTools && PlayerPrefs.GetInt(DebugPrefKey, 1) == 1;
            _hud.Build(_feel.HintText, _haptics.Enabled, _audioDirector.SoundEnabled, debugOn, devTools);
            _hud.BackPressed += OnBackPressed;
            _hud.FinishPressed += OnFinishPressed;
            _hud.SaveHairstylePressed += OnSaveHairstylePressed;
            _hud.ResetViewPressed += () => _orbit.ResetView();
            _hud.BellyPressed += () => { if (!_inContact) _orbit.ToggleBellyView(); };
            _hud.RegrowPressed += OnRegrowPressed;
            _hud.MenuPressed += OnMenuPressed;
            _hud.ReenterPressed += OnReenterPressed;
            _hud.HapticsToggled += _haptics.SetEnabled;
            _hud.SoundToggled += _audioDirector.SetSoundEnabled;
            _hud.DebugToggled += on => { PlayerPrefs.SetInt(DebugPrefKey, on ? 1 : 0); _debugHud.SetVisible(on); };
            _debugHud.Initialise(this);
            _debugHud.SetVisible(debugOn);

            _input.HitsShearable = HitsShearable;
            _input.ShearMove += OnShearMove;
            _input.ShearEnd += OnShearEnd;
            _input.OrbitDelta += d => { if (!_inContact) _orbit.AddOrbit(d); };
            _input.ZoomDelta += r => { if (!_inContact) _orbit.AddZoom(r); };
            _input.ResetViewRequested += () => _orbit.ResetView();

            StartSession();
        }

        bool HitsShearable(Vector2 screen) => !_closing && _mapper.Raycast(screen, out _);

        // ------------------------------------------------------------------ session lifecycle

        void StartSession()
        {
            SessionIndex++;
            LoadOrCreateState();
            _graph.RefreshAllSupport();
            _woolVisual.UploadMask(_mask);
            _thresholdReached = _mask.CutFraction >= _feel.CompletionThreshold;
            _finishing = false;
            _closing = false;
            _hintVisible = _mask.CutFraction <= 0.0001f;
            _hud.ShowHint(_hintVisible);
            _hud.SetProgress(_mask.CutFraction, _feel.CompletionThreshold);
            _hud.SetButtonsInteractable(true);
            _hud.SetFinishAvailable(true);   // finishing never requires a % – any amount can be the end of a session
            _hud.SetRegrowAvailable(false);  // instant regrow is a dev tool now (natural regrowth exists)
            _hud.SetPreserved(_state.hairstylePreserved);
            _pendingRemoved = 0f;
            _input.SetInputEnabled(true);
            _hub.RaiseSessionStarted();
        }

        void LoadOrCreateState()
        {
            string id = _sheep.SheepId;
            int epoch = _defaultState != null ? _defaultState.SaveEpoch : 0;
            if (SheepWoolStateStore.TryLoad(id, epoch, out var loaded))
            {
                if (loaded.woolSurfaceVersion != _feel.WoolSurfaceVersion || loaded.maskWidth != _mask.Width || loaded.maskHeight != _mask.Height)
                {
                    // never silently apply an incompatible pattern to a new topology
                    Debug.LogWarning($"[Shearing] Saved wool state for '{id}' has surface v{loaded.woolSurfaceVersion} {loaded.maskWidth}x{loaded.maskHeight}, " +
                                     $"current is v{_feel.WoolSurfaceVersion} {_mask.Width}x{_mask.Height}. Not applied; starting with full wool.");
                    LastRestoreInfo = "Incompatible save ignored";
                    _state = NewState(id);
                    _mask.ResetToFull();
                    return;
                }
                if (_mask.Decode(loaded.cutMask))
                {
                    _state = loaded;
                    if (string.IsNullOrEmpty(_state.cycleId)) _state.cycleId = System.Guid.NewGuid().ToString("N"); // pre-economy save
                    // wool kept growing while the sheep was away (paused for preserved hairstyles)
                    int steps = SheepWoolLifecycle.CatchUp(_state, _mask, _growth, GrowthNow);
                    LastRestoreInfo = $"Restored, wool {(_mask.WoolAmount * 100f):0.0}% (commit #{loaded.commitCount}, +{steps} growth steps" +
                                      (_state.hairstylePreserved ? ", hairstyle preserved)" : ")");
                    return;
                }
                Debug.LogWarning($"[Shearing] Corrupt mask for '{id}', starting fresh.");
            }
            _state = NewState(id);
            _mask.ResetToFull();
            LastRestoreInfo = "New sheep state (" + SheepWoolStateStore.LastLoadInfo + ")";
        }

        /// <summary>Player save state for a sheep the player has never touched: built ONLY from the shipped default state.</summary>
        SheepWoolState NewState(string id)
        {
            var s = new SheepWoolState
            {
                sheepId = id,
                cycleId = System.Guid.NewGuid().ToString("N"),
                sheepTypeId = _sheep.SheepTypeId,
                woolSurfaceVersion = _feel.WoolSurfaceVersion,
                maskWidth = _mask.Width,
                maskHeight = _mask.Height,
                maskEncoding = 2,
                growthClockStamp = GrowthNow,
            };
            SheepWoolLifecycle.StampMode(s, _growth);
            SheepWoolStateStore.Stamp(s, _defaultState != null ? _defaultState.SaveEpoch : 0);
            return s;
        }

        double GrowthNow => _growth != null ? _growth.Now : ShearAndGrow.Economy.WoolGrowthClock.GameSeconds;

        /// <summary>Wool VOLUME removed since the last commit (paid on the next commit, exactly once).</summary>
        float _pendingRemoved;

        /// <summary>
        /// Saves the current fleece (per-texel lengths) and pays the wool VOLUME removed since the last commit.
        /// Idempotent: a second call adds 0 removed volume and the ledger pays 0.
        /// </summary>
        public void CommitState()
        {
            if (_state == null) return;
            float fraction = _mask.CutFraction;
            if (_pendingRemoved > 0f)
            {
                _state.lifetimeRemovedWool += _pendingRemoved;
                _state.creditedYield += _pendingRemoved * _yieldPerFullShear;
                _pendingRemoved = 0f;
            }
            _state.creditedCutArea = fraction; // current missing wool (legacy/readout field)
            if (fraction >= _feel.CompletionThreshold) _state.completionFlags |= SheepCompletionFlags.ReachedThreshold;
            _state.woolSurfaceVersion = _feel.WoolSurfaceVersion;
            _state.maskWidth = _mask.Width;
            _state.maskHeight = _mask.Height;
            _state.maskEncoding = 2;
            _state.cutMask = _mask.Encode();
            _state.woolAmountAtSave = _mask.WoolAmount;
            // growth pauses while the sheep is on the shearing table: restart its clock from now
            SheepWoolLifecycle.Restamp(_state, GrowthNow);
            _state.commitCount++;
            _state.lastCommitUtc = System.DateTime.UtcNow.ToString("o");
            if (string.IsNullOrEmpty(_state.cycleId)) _state.cycleId = System.Guid.NewGuid().ToString("N");
            SheepWoolStateStore.Save(_state);

            // Dirty Wool for removed, never-paid wool. The ledger (one atomic economy file) pays
            // floor(lifetimeRemoved × WoolPerFullSheep) − already given, so re-commits, re-entries, restarts, preserve
            // toggles and a crash between the two saves can neither lose nor duplicate wool; regrown wool pays exactly
            // the regrown volume.
            if (ShearAndGrow.Economy.WoolEconomy.IsReady)
            {
                int units = ShearAndGrow.Economy.WoolEconomy.Instance.CreditRemovedWool(_state.sheepId, _state.cycleId, _state.lifetimeRemovedWool);
                LastCreditedWool = units;
                SessionWoolEarned += units;
            }
        }

        /// <summary>Dirty Wool added by the most recent commit.</summary>
        public int LastCreditedWool { get; private set; }
        /// <summary>Dirty Wool earned since this scene was opened.</summary>
        public int SessionWoolEarned { get; private set; }
        /// <summary>Dirty Wool paid so far for the current wool cycle of this sheep.</summary>
        public int CycleWoolEarned => ShearAndGrow.Economy.WoolEconomy.IsReady && _state != null
            ? ShearAndGrow.Economy.WoolEconomy.Instance.GivenUnits(_state.sheepId, _state.cycleId) : 0;

        void OnBackPressed()
        {
            if (_closing) return;
            _goToHub = ShearAndGrow.Economy.SceneNavigator.IsInBuild(ShearAndGrow.Economy.SceneNavigator.Hub);
            StartCoroutine(CloseSession(reenter: true, showComplete: false, toast: "Saved"));
        }

        bool _goToHub;

        /// <summary>Completion panel "Menu": session is already closed and committed.</summary>
        void OnMenuPressed()
        {
            if (!ShearAndGrow.Economy.SceneNavigator.Go(ShearAndGrow.Economy.SceneNavigator.Hub)) OnReenterPressed();
        }

        /// <summary>
        /// "Finish shearing": deliberately ends this production session at ANY amount, with the celebration and a
        /// summary. Pays exactly like Back (same idempotent commit), so the same area is never paid twice.
        /// </summary>
        void OnFinishPressed()
        {
            if (_closing || _finishing) return;
            _finishing = true;
            _hud.SetFinishAvailable(false);
            _state.completionFlags |= SheepCompletionFlags.FinishSettled;
            ForceEndContact();
            _hub.RaiseCompleted(); // one animation, one cue, one haptic
            _orbit.ResetView();
            StartCoroutine(FinishRoutine());
        }

        /// <summary>
        /// "Save hairstyle": keeps the current shape, pauses regrowth (HairstylePreserved), pays new wool and returns
        /// to the farm. Re-styling a preserved sheep later keeps the lock.
        /// </summary>
        void OnSaveHairstylePressed()
        {
            if (_closing || _finishing) return;
            ForceEndContact();
            SheepWoolLifecycle.SetPreserved(_state, null, _growth, GrowthNow, true); // mask is current (no growth on the table)
            _hud.SetPreserved(true);
            _goToHub = ShearAndGrow.Economy.SceneNavigator.IsInBuild(ShearAndGrow.Economy.SceneNavigator.Hub);
            StartCoroutine(CloseSession(reenter: true, showComplete: false, toast: "Hairstyle saved"));
        }

        IEnumerator FinishRoutine()
        {
            _input.SetInputEnabled(false);
            yield return new WaitForSeconds(_finishCelebrationSeconds);
            yield return CloseSession(reenter: false, showComplete: true, toast: null);
        }

        IEnumerator CloseSession(bool reenter, bool showComplete, string toast)
        {
            _closing = true;
            ForceEndContact();
            _input.SetInputEnabled(false);
            _hud.SetButtonsInteractable(false);
            _hub.RaiseSessionClosing();       // stop audio loops & haptics immediately
            CommitState();                    // durable save before the transition
            yield return _hud.Fade(1f, _fadeSeconds);
            // frame hidden → runtime floor clumps may now be cleared
            _clumps.ClearAfterSessionClosed();
            _fluff.Clear();
            _hub.RaiseSessionClosed();
            if (showComplete)
            {
                _hud.SetCompleteWool(SessionWoolEarned, _mask.WoolAmount);
                _hud.ShowCompletePanel(true);
                yield return _hud.Fade(0f, _fadeSeconds);
                yield break;
            }
            if (_goToHub)
            {
                // Hub shows the "+N Dirty Wool" result; the screen stays faded while the next scene loads.
                _goToHub = false;
                ShearAndGrow.Economy.SceneNavigator.Go(ShearAndGrow.Economy.SceneNavigator.Hub);
                yield break;
            }
            if (reenter)
            {
                StartSession(); // re-enter: restores the saved pattern from disk
                yield return _hud.Fade(0f, _fadeSeconds);
                if (!string.IsNullOrEmpty(toast)) _hud.ShowToast(toast);
            }
        }

        void OnReenterPressed()
        {
            _hud.ShowCompletePanel(false);
            StartSession();
        }

        /// <summary>
        /// Regrows the wool of a finished sheep: the sheep starts again with full wool and a fresh save (new shearing
        /// cycle, rewards can be earned again for the new wool). Available from the completion panel and, after
        /// re-entering a finished sheep, from the "Regrow" button.
        /// </summary>
        void OnRegrowPressed()
        {
            // Instant regrow would bypass natural regrowth and print free wool → development builds only.
            if (_regrowing || !Debug.isDebugBuild) return;
            if (_hud.IsCompletePanelVisible)
            {
                // Session is already closed and faded; floor is clean.
                _hud.ShowCompletePanel(false);
                SheepWoolStateStore.Delete(_sheep.SheepId);
                _orbit.ResetView(true);
                StartSession();
                return;
            }
            if (_closing) return;
            StartCoroutine(RegrowRoutine());
        }

        bool _regrowing;

        IEnumerator RegrowRoutine()
        {
            _regrowing = true;
            _closing = true;
            ForceEndContact();
            _input.SetInputEnabled(false);
            _hud.SetButtonsInteractable(false);
            _hub.RaiseSessionClosing(); // stop audio loops & haptics
            yield return _hud.Fade(1f, _fadeSeconds);
            _clumps.ClearAfterSessionClosed(); // hidden by the fade: no popping
            _fluff.Clear();
            _hub.RaiseSessionClosed();
            SheepWoolStateStore.Delete(_sheep.SheepId);
            _orbit.ResetView(true);
            StartSession();
            yield return _hud.Fade(0f, _fadeSeconds);
            _hud.ShowToast("Шерсть отросла!");
            _regrowing = false;
        }

        /// <summary>Debug/QA: regrow wool immediately. No effect in release builds.</summary>
        public void DebugRegrow()
        {
            if (_closing || !Debug.isDebugBuild) return;
            ForceEndContact();
            _clumps.ClearAfterSessionClosed();
            SheepWoolStateStore.Delete(_sheep.SheepId);
            _orbit.ResetView(true);
            StartSession();
        }

        /// <summary>
        /// Development-only "Reset Save": deletes ALL sheep saves of this install and restarts from the default
        /// game state, exactly like a first launch on a clean device. No effect in release builds.
        /// </summary>
        public int DevResetSave()
        {
            if (!Debug.isDebugBuild || _closing) return 0;
            ForceEndContact();
            _clumps.ClearAfterSessionClosed();
            _fluff.Clear();
            int removed = SheepWoolStateStore.DeleteAll();
            if (ShearAndGrow.Economy.WoolEconomy.IsReady) ShearAndGrow.Economy.WoolEconomy.Instance.DevReset(); // coins + wool too
            _hud.ShowCompletePanel(false);
            _orbit.ResetView(true);
            StartSession();
            Debug.Log("[Shearing] DEV reset save: removed " + removed + " file(s) from " + SheepWoolStateStore.Directory);
            return removed;
        }

        void OnApplicationPause(bool paused)
        {
            if (!paused) return;
            ForceEndContact();
            if (!_closing) CommitState(); // lifecycle checkpoint (idempotent)
        }

        void OnApplicationFocus(bool focus)
        {
            if (!focus) ForceEndContact();
        }

        void OnApplicationQuit()
        {
            if (!_closing) CommitState();
        }

        // ------------------------------------------------------------------ stroke pipeline

        void OnShearMove(Vector2 screen)
        {
            if (_closing || _finishing) return;
            if (!_mapper.Raycast(screen, out SurfaceHit hit))
            {
                // finger left the model: end the segment immediately, never bridge through empty space
                _mapper.EndSegment();
                SetContact(false);
                return;
            }
            SetContact(true);

            _frameTangentWorld = Vector3.zero;
            _mapper.Feed(hit, _stampHandler);
            float newArea = _frameNewArea;
            LastFrameNewArea = newArea;
            _frameNewArea = 0f;

            Vector3 tangentWorld = _woolVisual.Body.TransformDirection(_mapper.LastTangentLocal);
            // trimmer rides on the remaining wool (sits in the fresh trench after cutting)
            float occ = _mask.SampleUV(hit.U, hit.V);
            float woolH = _feel.WoolThickness * _sheep.Surface.WoolHeightMultiplier(hit.U, hit.V) * occ;
            Vector3 contact = _woolVisual.Body.TransformPoint(hit.LocalPoint + hit.LocalNormal * (woolH + 0.01f));
            _trimmer.SetContact(true, contact, hit.WorldNormal, tangentWorld, newArea > 0f);
            _woolVisual.SetTrimmer(true, contact, tangentWorld, _brushRadius);
            _clumps.ApplyStrokeImpulse(contact, tangentWorld, _brushRadius);

            bool bare = newArea <= 0f && occ <= 0f;
            if (bare != _bareContact) { _bareContact = bare; _hub.RaiseBareContactChanged(bare); }

            if (newArea > 0f)
            {
                if (_hintVisible) { _hintVisible = false; _hud.HideHint(); } // first real gap
                float p = _mask.CutFraction;
                _hub.RaiseProgress(p);
                _hud.SetProgress(p, _feel.CompletionThreshold);
                if (!_thresholdReached && p >= _feel.CompletionThreshold)
                    _thresholdReached = true; // sparkle only (HUD); no forced exit, hairstyle can still be saved
            }
        }

        void OnStamp(Vector3 local, Vector3 normalLocal, Vector3 tangentLocal, int strokeId)
        {
            float area = _mask.Stamp(local, _brushRadius, out float density);
            var transitions = _graph.OnStamp(local, _brushRadius, _chunkPhysics.HangingThreshold);
            Transform body = _woolVisual.Body;
            Vector3 tangentWorld = body.TransformDirection(tangentLocal);
            for (int i = 0; i < transitions.Count; i++) _clumps.SpawnHanging(transitions[i], tangentWorld);
            if (area <= 0f) return; // bare area: no events, no feedback
            _pendingRemoved += area; // removed wool volume (partly regrown wool yields only its length)
            _frameNewArea += area;
            CutEventCount++;
            Vector3 world = body.TransformPoint(local + normalLocal * _feel.WoolThickness * 0.6f);
            Vector3 normalWorld = body.TransformDirection(normalLocal).normalized;
            _hub.RaiseCutDelta(new CutDeltaEvent
            {
                SheepId = _state.sheepId,
                SurfacePointLocal = local,
                SurfacePointWorld = world,
                NormalWorld = normalWorld,
                TangentWorld = tangentWorld,
                NewAreaNormalized = area,
                LocalDensity = density,
                StrokeId = strokeId,
            });
            _fluff.Emit(world, normalWorld, tangentWorld, area);
        }

        void OnShearEnd()
        {
            _mapper.EndSegment();
            SetContact(false);
        }

        void ForceEndContact()
        {
            _input.CancelAll();
            _mapper?.EndSegment();
            SetContact(false);
        }

        void SetContact(bool contact)
        {
            if (contact == _inContact) return;
            _inContact = contact;
            _trimmer.SetMotor(contact);
            if (!contact)
            {
                // stop cutting within this frame
                _trimmer.SetContact(false, Vector3.zero, Vector3.up, Vector3.zero, false);
                _woolVisual.SetTrimmer(false, Vector3.zero, Vector3.zero, _brushRadius);
                LastFrameNewArea = 0f;
                if (_bareContact) { _bareContact = false; _hub.RaiseBareContactChanged(false); }
            }
            _hub.RaiseContactChanged(contact);
        }

        void LateUpdate()
        {
            if (_mask == null) return;
            _woolVisual.UploadMask(_mask);
            if (!_inContact) _woolVisual.SetTrimmer(false, Vector3.zero, Vector3.zero, _brushRadius);
            if (_pose != null && _pose.IsBellyMode != _bellyLabelState)
            {
                _bellyLabelState = _pose.IsBellyMode;
                _hud.SetBellyMode(_bellyLabelState);
            }
        }

        bool _bellyLabelState;

        // ------------------------------------------------------------------ QA hooks (used by self tests / play mode checks)

        /// <summary>Simulates a stroke between two (u,v) points on the body in object space, bypassing the screen. QA only.</summary>
        public float QA_StrokeUV(Vector2 fromUV, Vector2 toUV, int samples)
        {
            float before = _mask.CutFraction;
            SetContact(true);
            _mapper.EndSegment();
            for (int i = 0; i <= samples; i++)
            {
                Vector2 uv = Vector2.Lerp(fromUV, toUV, i / (float)samples);
                var hit = new SurfaceHit
                {
                    U = uv.x, V = uv.y,
                    LocalPoint = _sheep.Surface.GetPoint(uv.x, uv.y),
                    LocalNormal = _sheep.Surface.GetNormal(uv.x, uv.y),
                };
                hit.WorldPoint = _woolVisual.Body.TransformPoint(hit.LocalPoint);
                hit.WorldNormal = _woolVisual.Body.TransformDirection(hit.LocalNormal);
                _mapper.Feed(hit, _stampHandler);
                _frameNewArea = 0f;
            }
            _mapper.EndSegment();
            SetContact(false);
            _hud.SetProgress(_mask.CutFraction, _feel.CompletionThreshold);
            if (!_thresholdReached && _mask.CutFraction >= _feel.CompletionThreshold) _thresholdReached = true;
            return _mask.CutFraction - before;
        }

        /// <summary>
        /// QA: a finger stroke between two SCREEN positions through the real input path (camera raycast → collider →
        /// surface mapper → mask). Returns the unique area removed.
        /// </summary>
        public float QA_StrokeScreen(Vector2 from, Vector2 to, int samples)
        {
            float before = _mask.CutFraction;
            for (int i = 0; i <= samples; i++) OnShearMove(Vector2.Lerp(from, to, i / (float)samples));
            OnShearEnd();
            return _mask.CutFraction - before;
        }

        public void QA_PressBack() => OnBackPressed();
        public void QA_PressFinish() => OnFinishPressed();
        public void QA_PressSaveHairstyle() => OnSaveHairstylePressed();
        public void QA_PressMenu() => OnMenuPressed();
        public WoolGrowthConfig GrowthConfig => _growth;

        public SheepPoseController Pose => _pose;
        public ShearingCameraOrbit Orbit => _orbit;
    }
}
