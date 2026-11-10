using System;
using UnityEngine;

namespace ShearAndGrow.Shearing
{
    /// <summary>Semantic event produced only when NEW unique wool area was removed (spec §12.2).</summary>
    public struct CutDeltaEvent
    {
        public string SheepId;
        /// <summary>Stamp centre in sheep-local (body object) space.</summary>
        public Vector3 SurfacePointLocal;
        public Vector3 SurfacePointWorld;
        public Vector3 NormalWorld;
        public Vector3 TangentWorld;
        /// <summary>Newly removed area as a fraction of the total shearable area (0..1).</summary>
        public float NewAreaNormalized;
        /// <summary>Remaining local wool density around the stamp before the cut (0..1).</summary>
        public float LocalDensity;
        public int StrokeId;
    }

    public enum ClumpEventType { BeganHanging, Detached, Landed, Settled }

    public struct ClumpEvent
    {
        public ClumpEventType Type;
        public Vector3 Position;
        /// <summary>Approximate relative size, 1 = average clump.</summary>
        public float Size;
    }

    /// <summary>
    /// Instance-owned event hub that decouples the authoritative core from feedback layers
    /// (visual mask, clumps, audio, haptics, progress). Owned by the session controller.
    /// </summary>
    public sealed class ShearingEventHub
    {
        public event Action<CutDeltaEvent> CutDelta;
        /// <summary>Contact with the shearing surface began (true) or ended (false). Fires on release, cancel, focus loss.</summary>
        public event Action<bool> ContactChanged;
        /// <summary>Trimmer is currently over bare (already sheared) area while in contact.</summary>
        public event Action<bool> BareContactChanged;
        public event Action<ClumpEvent> Clump;
        public event Action<float> ProgressChanged;
        public event Action Completed;
        public event Action SessionClosing;
        public event Action SessionClosed;
        public event Action SessionStarted;

        public void RaiseCutDelta(in CutDeltaEvent e) => CutDelta?.Invoke(e);
        public void RaiseContactChanged(bool inContact) => ContactChanged?.Invoke(inContact);
        public void RaiseBareContactChanged(bool bare) => BareContactChanged?.Invoke(bare);
        public void RaiseClump(in ClumpEvent e) => Clump?.Invoke(e);
        public void RaiseProgress(float p) => ProgressChanged?.Invoke(p);
        public void RaiseCompleted() => Completed?.Invoke();
        public void RaiseSessionClosing() => SessionClosing?.Invoke();
        public void RaiseSessionClosed() => SessionClosed?.Invoke();
        public void RaiseSessionStarted() => SessionStarted?.Invoke();
    }
}
