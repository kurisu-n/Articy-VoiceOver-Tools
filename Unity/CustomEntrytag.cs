using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

using PixelCrushers.DialogueSystem;

namespace Firecrest.Integrations.DS
{
    // Names each line's entrytag after one of its fields, the Articy Id by default, so the voice-over files
    // named after that id are what AudioWait(entrytag) and AudioWait(entrytaglocal) look for.
    // Only used when the Dialogue Manager's Entrytag Format is Custom.
    // Hooked in edit mode too so the Dialogue Editor's voice-over exports use the same names.
    [ExecuteInEditMode]
    [DefaultExecutionOrder(-100000)]
    public class CustomEntrytag : MonoBehaviour
    {
        /// <summary>
        /// The dialogue entry field whose value becomes the entry's entrytag.
        /// </summary>
        /// <remarks>
        /// The articy:draft importer writes each entry's articy object id into the "Articy Id" field.
        /// An entry without the field gets no entrytag.
        /// </remarks>
        [Tooltip("The dialogue entry field whose value names the line's voice-over file.\n\n" +
                 "\"Articy Id\" is filled in for every line by the articy:draft importer.")]
        public string entrytagFieldName = "Articy Id";

        /// <summary>
        /// Whether this component's lookup is the one the Dialogue System currently uses for custom entrytags.
        /// </summary>
        public bool IsHooked => DialogueDatabase.getCustomEntrytag == (DialogueDatabase.GetCustomEntrytagDelegate)OnGetEntrytag;

        // DS clears its static hook on entering play mode, and objects enable after that, so OnEnable is enough
        // both in play mode and after a script reload in the editor.
        private void OnEnable()
        {
            Setup();
        }

        private void OnDisable()
        {
            DialogueDatabase.getCustomEntrytag -= OnGetEntrytag;
        }

        /// <summary>
        /// Makes this component's lookup the Dialogue System's custom entrytag source.
        /// </summary>
        /// <remarks>
        /// Safe to call repeatedly: the lookup is never registered twice. Runs on its own when the component
        /// is enabled, so a manual call is only needed after something else replaced the hook.
        /// </remarks>
        public void Setup()
        {
            DialogueDatabase.getCustomEntrytag -= OnGetEntrytag;
            DialogueDatabase.getCustomEntrytag += OnGetEntrytag;
        }

        private string OnGetEntrytag(Conversation conversation, DialogueEntry entry)
        {
            return Field.LookupValue(entry.fields, entrytagFieldName);
        }
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(CustomEntrytag))]
    // qualified, a project's own ...DS.Editor namespace would shadow it
    internal class CustomEntrytagEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(CustomEntrytag.entrytagFieldName)));
            serializedObject.ApplyModifiedProperties();

            var custom = (CustomEntrytag)target;
            var controller = FindController();

            if (controller == null)
                EditorGUILayout.HelpBox("No Dialogue Manager in the open scenes. Set its Entrytag Format to Custom for this component to be used.", MessageType.Info);
            else if (controller.displaySettings.cameraSettings.entrytagFormat != EntrytagFormat.Custom)
                EditorGUILayout.HelpBox("The Dialogue Manager's Entrytag Format is not Custom, so this component is not used. " +
                                        "Set it under Display Settings > Camera & Cutscene Settings.", MessageType.Warning);

            if (!custom.isActiveAndEnabled)
                return;

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(custom.IsHooked ? "Hooked into the Dialogue System" : "Not hooked", EditorStyles.miniLabel);
                if (GUILayout.Button("Hook Now", GUILayout.Width(90)))
                    custom.Setup();
            }
        }

        private static DialogueSystemController FindController()
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindFirstObjectByType<DialogueSystemController>();
#else
            return Object.FindObjectOfType<DialogueSystemController>();
#endif
        }
    }
#endif
}
