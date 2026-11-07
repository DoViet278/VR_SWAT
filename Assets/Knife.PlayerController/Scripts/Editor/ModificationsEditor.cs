using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace KnifePlayerController
{
    public class ModificationsEditor : EditorWindow
    {
        [MenuItem("Window/Knife/Modifications Editor")]
        public static void Open()
        {
            ModificationsEditor editorWindow = GetWindow<ModificationsEditor>();

            editorWindow.Show();
        }

        Vector2 scrollPosition;
        ModificationSlot[] slots;

        private void OnGUI()
        {
            scrollPosition = GUILayout.BeginScrollView(scrollPosition);
            GUILayout.BeginVertical();

            slots = GameObject.FindObjectsOfType<ModificationSlot>();

            foreach (ModificationSlot s in slots)
            {
                drawSlotGUI(s);
                GUILayout.Space(50f);
            }

            GUILayout.EndVertical();
            GUILayout.EndScrollView();
        }

        ModificationSlot findModificatorWithId(Modificator mod, int id)
        {
            foreach (ModificationSlot slot in slots)
            {
                foreach (Modificator m in slot.PossibleModifications)
                {
                    if (m.ModId == id && mod != m)
                    {
                        return slot;
                    }
                }
            }

            return null;
        }

        private void OnDestroy()
        {
            foreach (ModificationSlot slot in slots)
            {
                slot.Visual = false;
            }
        }

        void drawSlotGUI(ModificationSlot slot)
        {
            SerializedObject serializedObject = new SerializedObject(slot);
            SerializedProperty modsProperty = serializedObject.FindProperty("PossibleModifications");
            GUILayout.Label(slot.name);
            slot.Visual = GUILayout.Toggle(slot.Visual, "Edit that slot");
            EditorGUI.BeginChangeCheck();
            for (int i = 0; i < slot.PossibleModifications.Length; i++)
            {
                ModificationSlot otherSlot = findModificatorWithId(slot.PossibleModifications[i], slot.PossibleModifications[i].ModId);

                if (otherSlot != null)
                {
                    GUILayout.Label("Other modificator has equals modificator id. Slot GameObject Name: " + otherSlot.name + " ModId: " + slot.PossibleModifications[i].ModId);
                }
            }
            if (slot.Visual)
            {
                EditorGUILayout.PropertyField(modsProperty, modsProperty.isExpanded);
            }

            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
            }
        }
    }
}