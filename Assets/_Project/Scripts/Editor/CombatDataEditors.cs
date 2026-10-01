using System.Collections.Generic;
using Game.Combat;
using Game.Simulation;
using UnityEditor;

namespace Game.EditorTools
{
    /// <summary> Скилл: только поля, которые читает симуляция для выбранного типа (<see cref="SkillData.UsesField"/>), и ошибки данных. </summary>
    [CustomEditor(typeof(SkillData))]
    public class SkillDataEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var kind = (SkillKind)serializedObject.FindProperty(nameof(SkillData.Kind)).intValue;
            var it = serializedObject.GetIterator();
            for (bool enter = true; it.NextVisible(enter); enter = false)
            {
                if (it.propertyPath == "m_Script") continue;
                if (!SkillData.UsesField(kind, it.name)) continue;
                EditorGUILayout.PropertyField(it, true);
            }
            serializedObject.ApplyModifiedProperties();
            Problems.Draw(((SkillData)target).Validate());
        }
    }

    [CustomEditor(typeof(FighterDefinition))]
    public class FighterDefinitionEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            Problems.Draw(((FighterDefinition)target).Validate());
            base.OnInspectorGUI();
        }
    }

    [CustomEditor(typeof(FighterRoster))]
    public class FighterRosterEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            Problems.Draw(((FighterRoster)target).Validate());
            base.OnInspectorGUI();
        }
    }

    internal static class Problems
    {
        public static void Draw(List<string> problems)
        {
            if (problems.Count == 0) return;
            EditorGUILayout.HelpBox(string.Join("\n", problems), MessageType.Warning);
            EditorGUILayout.Space();
        }
    }
}
