using UnityEditor;
using UnityEngine;

namespace ShearAndGrow.Editor
{
    /// <summary>Editor-only input diagnostics; no prototype HUD or gameplay dependencies.</summary>
    [CustomEditor(typeof(ShearingInputAdapter))]
    public sealed class ShearingInputAdapterEditor : UnityEditor.Editor
    {
        private ShearingInputAdapter adapter;
        private int begins, heldFrames, releases, cancels;

        private void OnEnable()
        {
            adapter = (ShearingInputAdapter)target;
            adapter.PressBegan += OnBegin;
            adapter.Held += OnHeld;
            adapter.Released += OnRelease;
            adapter.Canceled += OnCancel;
        }

        private void OnDisable()
        {
            if (adapter == null) return;
            adapter.PressBegan -= OnBegin;
            adapter.Held -= OnHeld;
            adapter.Released -= OnRelease;
            adapter.Canceled -= OnCancel;
        }

        private void OnBegin(Vector2 position) => begins++;
        private void OnHeld(Vector2 position) => heldFrames++;
        private void OnRelease(Vector2 position) => releases++;
        private void OnCancel() => cancels++;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (!Application.isPlaying) return;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Input diagnostics (while this Inspector is open)");
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.Vector2Field("Pointer position", adapter.PointerPosition);
                EditorGUILayout.Toggle("Held", adapter.IsHeld);
                EditorGUILayout.IntField("Press begins", begins);
                EditorGUILayout.IntField("Held frames", heldFrames);
                EditorGUILayout.IntField("Releases", releases);
                EditorGUILayout.IntField("Cancels", cancels);
            }
        }

        public override bool RequiresConstantRepaint() => Application.isPlaying;
    }
}
