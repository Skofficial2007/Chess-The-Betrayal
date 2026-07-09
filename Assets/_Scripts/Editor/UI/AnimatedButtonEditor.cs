using UnityEditor;
using UnityEditor.UI;
using ChessTheBetrayal.UI.Controls;

namespace ChessTheBetrayal.EditorTools.UI
{
    /// <summary>
    /// Button ships with its own built-in custom editor (UnityEditor.UI.ButtonEditor) that draws
    /// Interactable/Transition/Navigation/OnClick field-by-field instead of calling
    /// DrawDefaultInspector() — which means it never surfaces a subclass's own [SerializeField]
    /// fields for free. Without this editor, AnimatedButton's Play Click Animation/Scale
    /// Target/Punch Scale/etc. fields are silently invisible in the Inspector even though they
    /// compile and serialize correctly.
    ///
    /// Subclassing ButtonEditor (rather than plain Editor + DrawDefaultInspector()) keeps every
    /// bit of Button's existing Inspector behavior — including its Visualize navigation button and
    /// OnClick() event list — and only appends the animation fields beneath it.
    /// </summary>
    [CustomEditor(typeof(AnimatedButton), true)]
    [CanEditMultipleObjects]
    public class AnimatedButtonEditor : ButtonEditor
    {
        private SerializedProperty _playClickAnimation;
        private SerializedProperty _scaleTarget;
        private SerializedProperty _punchScale;
        private SerializedProperty _animationDuration;
        private SerializedProperty _punchDownEase;
        private SerializedProperty _punchBackEase;

        protected override void OnEnable()
        {
            base.OnEnable();

            _playClickAnimation = serializedObject.FindProperty("_playClickAnimation");
            _scaleTarget = serializedObject.FindProperty("_scaleTarget");
            _punchScale = serializedObject.FindProperty("_punchScale");
            _animationDuration = serializedObject.FindProperty("_animationDuration");
            _punchDownEase = serializedObject.FindProperty("_punchDownEase");
            _punchBackEase = serializedObject.FindProperty("_punchBackEase");
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            serializedObject.Update();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Click Animation", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_playClickAnimation);

            using (new EditorGUI.DisabledScope(!_playClickAnimation.boolValue))
            {
                EditorGUILayout.PropertyField(_scaleTarget);
                EditorGUILayout.PropertyField(_punchScale);
                EditorGUILayout.PropertyField(_animationDuration);
                EditorGUILayout.PropertyField(_punchDownEase);
                EditorGUILayout.PropertyField(_punchBackEase);
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
