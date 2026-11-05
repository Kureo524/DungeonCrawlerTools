using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using UnityEditor;
using UnityEngine;

namespace TheDungeon.Editor.ContentDatabase
{
    /// <summary>Modifications du comportement d'un ennemi sérialisé (règles, attaques, phases), sans interface : chaque ajout renvoie l'indice créé, ou -1 au maximum.</summary>
    public static class EnemyBehaviourEditing
    {
        public static int AddRule(SerializedObject enemy, RulePresets.Preset preset, int max)
        {
            enemy.Update();
            var patterns = enemy.FindProperty("patterns");
            if (patterns.arraySize >= max)
                return -1;
            patterns.arraySize++;
            RulePresets.Apply(preset, patterns.GetArrayElementAtIndex(patterns.arraySize - 1));
            enemy.ApplyModifiedProperties();
            return patterns.arraySize - 1;
        }

        /// <summary>Donne une attaque existante à l'ennemi : sans condition, sans valeur propre, poids 100.</summary>
        public static int AddAttack(SerializedObject enemy, AttackData attack, int max)
        {
            enemy.Update();
            var attacks = enemy.FindProperty("attacks");
            if (attacks.arraySize >= max)
                return -1;
            attacks.arraySize++;
            var slot = attacks.GetArrayElementAtIndex(attacks.arraySize - 1);
            slot.FindPropertyRelative("attack").objectReferenceValue = attack;
            slot.FindPropertyRelative("conditions").arraySize = 0;
            slot.FindPropertyRelative("overridden").intValue = 0;
            slot.FindPropertyRelative("weight").intValue = 100;
            enemy.ApplyModifiedProperties();
            return attacks.arraySize - 1;
        }

        /// <summary>Nouvelle phase : un peu plus forte et rapide, sans aura ni renforts.</summary>
        public static int AddPhase(SerializedObject enemy, int max)
        {
            enemy.Update();
            var phases = enemy.FindProperty("phases");
            if (phases.arraySize >= max)
                return -1;
            phases.arraySize++;
            var phase = phases.GetArrayElementAtIndex(phases.arraySize - 1);
            phase.FindPropertyRelative("name").stringValue = $"Phase {phases.arraySize}";
            phase.FindPropertyRelative("damagePercent").intValue = 120;
            phase.FindPropertyRelative("speedPercent").intValue = 110;
            phase.FindPropertyRelative("damageTakenPercent").intValue = 100;
            phase.FindPropertyRelative("auraRadius").floatValue = 0f;
            phase.FindPropertyRelative("auraStatus").enumValueIndex = (int)StatusEffect.Rot;
            phase.FindPropertyRelative("auraPower").floatValue = 3f;
            phase.FindPropertyRelative("spawn").objectReferenceValue = null;
            phase.FindPropertyRelative("spawnInterval").floatValue = 6f;
            phase.FindPropertyRelative("spawnMax").intValue = 3;
            enemy.ApplyModifiedProperties();
            return phases.arraySize - 1;
        }

        /// <summary>L'attaque d'un modèle : celle qui porte déjà son nom si elle existe (<paramref name="reused"/>), sinon un nouveau fichier ATK_.</summary>
        public static AttackData AttackFromPreset(RulePresets.AttackPreset preset, out bool reused)
        {
            var existing = AssetFactory.LoadAll<AttackData>().FirstOrDefault(a => a.DisplayName == preset.Name);
            reused = existing != null;
            if (reused)
                return existing;
            var created = ScriptableObject.CreateInstance<AttackData>();
            var serialized = new SerializedObject(created);
            RulePresets.Apply(preset, serialized);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            created.name = NamingConvention.GetExpectedName(created);
            return AssetFactory.SaveNew(created);
        }
    }
}
