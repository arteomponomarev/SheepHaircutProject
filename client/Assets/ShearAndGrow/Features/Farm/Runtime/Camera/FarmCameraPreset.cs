using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShearAndGrow.Farm
{
    public enum FarmCameraProjection { Orthographic = 0, Perspective = 1 }

    /// <summary>
    /// One farm camera configuration (angle + projection + zoom limits). Zoom itself is the player's state and is
    /// expressed as "visible half height at the focus point" (orthographic size, or the equivalent for perspective),
    /// so switching projection keeps roughly the same visible area.
    /// </summary>
    [Serializable]
    public sealed class FarmCameraPreset
    {
        public string Id = "custom";
        public string DisplayName = "Custom";
        [Tooltip("Rotation around the vertical axis relative to the farm grid (0 = looking along +Z, building fronts face −Z).")]
        [Range(-180f, 180f)] public float Yaw;
        [Tooltip("Downward angle from the horizon.")]
        [Range(20f, 85f)] public float Pitch = 48f;
        public FarmCameraProjection Projection = FarmCameraProjection.Orthographic;
        [Tooltip("Vertical field of view for Perspective (moderate values keep the isometric look).")]
        [Range(10f, 60f)] public float FieldOfView = 25f;
        [Tooltip("Camera distance from the focus point for Orthographic (only affects clipping, not the visible size).")]
        [Min(10f)] public float OrthoDistance = 40f;
        [Tooltip("Zoom limits (visible half height at the focus, metres).")]
        [Min(1f)] public float MinSize = 3.2f;
        [Min(2f)] public float MaxSize = 20f;

        public FarmCameraPreset Clone() => (FarmCameraPreset)MemberwiseClone();

        /// <summary>Plain-text description (Developer Panel "Copy Camera Settings").</summary>
        public string ToText(float currentSize, Vector3 focus) =>
            string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "ShearAndGrow FarmCamera\npreset={0}\nyaw={1:0.##}\npitch={2:0.##}\nprojection={3}\nfov={4:0.##}\northoDistance={5:0.##}\nminSize={6:0.##}\nmaxSize={7:0.##}\nzoomSize={8:0.##}\nfocus=({9:0.##}, {10:0.##})",
                Id, Yaw, Pitch, Projection, FieldOfView, OrthoDistance, MinSize, MaxSize, currentSize, focus.x, focus.z);
    }
    }
