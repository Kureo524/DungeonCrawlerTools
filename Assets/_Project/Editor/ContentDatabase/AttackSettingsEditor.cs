using System;
using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

using static TheDungeon.Editor.ContentDatabase.AttackFields;

namespace TheDungeon.Editor.ContentDatabase
{
    /// <summary>
    /// Réglages de déclenchement d'une attaque (portée, recharge, dégâts, cadence). Côté ennemi, chaque réglage vient de l'attaque
    /// (grisé) et peut être remplacé pour cet ennemi seulement.
    /// </summary>
    public static class AttackSettingsEditor
    {
        static ForgeDesignLimits Limits => ForgeDesignLimits.Instance;

        /// <summary>Contexte d'un ennemi : quels réglages il remplace, et ceux de l'attaque d'origine.</summary>
        public sealed class Override
        {
            public SerializedProperty Mask;
            public AttackSettings Base;
        }

        sealed class Field
        {
            public AttackField Flag;
            public string Property;
            public string Before;
            public string After;
            public string Tooltip;
            public bool IsInt;
            public float Min;
            public float Max;
        }

        static IEnumerable<Field> SettingsFields()
        {
            var l = Limits;
            yield return new Field { Flag = AttackField.MinRange, Property = "minRange", Before = "joueur entre", Min = l.attackRange.min, Max = l.attackRange.max,
                Tooltip = "Distance minimale au joueur pour lancer l'attaque, en cases (0 = collé)." };
            yield return new Field { Flag = AttackField.MaxRange, Property = "maxRange", Before = "et", After = "cases", Min = l.attackRange.min, Max = l.attackRange.max,
                Tooltip = "Distance maximale au joueur pour lancer l'attaque, en cases. Une fois lancée, l'attaque va au bout même si le joueur s'éloigne." };
            yield return new Field { Flag = AttackField.Cooldown, Property = "cooldown", Before = "recharge", After = "s", Min = l.attackCooldown.min, Max = l.attackCooldown.max,
                Tooltip = "Délai avant de relancer cette attaque, compté à partir de sa fin. Les autres attaques restent possibles pendant ce temps." };
            yield return new Field { Flag = AttackField.Damage, Property = "damagePercent", Before = "dégâts", After = "%", IsInt = true, Min = l.attackDamage.min, Max = l.attackDamage.max,
                Tooltip = "Multiplie les dégâts de toutes les étapes. Pratique pour qu'un boss frappe plus fort avec la même attaque." };
            yield return new Field { Flag = AttackField.Tempo, Property = "tempoPercent", Before = "cadence", After = "%", IsInt = true, Min = l.attackTempo.min, Max = l.attackTempo.max,
                Tooltip = "100 % = durées normales ; 150 % = préparations et récupérations 1,5 fois plus rapides (attaque plus dure à esquiver)." };
        }

        /// <summary>Réglages de déclenchement. <paramref name="overrides"/> : null dans l'outil Attaques, le contexte de l'ennemi sinon.</summary>
        /// <param name="user">Arme ou compétence : pas de distance de lancement (c'est le joueur qui la déclenche).</param>
        public static VisualElement Settings(SerializedProperty settings, Override overrides, Action rebuild, AttackUser user = AttackUser.Enemy)
        {
            var row = new VisualElement();
            row.AddToClassList("forge-chips");
            foreach (var field in SettingsFields().Where(f => user == AttackUser.Enemy || f.Flag is not (AttackField.MinRange or AttackField.MaxRange)))
                row.Add(SettingPill(field, settings, overrides, rebuild));
            return row;
        }

        static VisualElement SettingPill(Field field, SerializedProperty settings, Override overrides, Action rebuild)
        {
            var pill = new VisualElement { tooltip = $"{field.Tooltip}\nValeurs autorisées : {field.Min:0.##} à {field.Max:0.##}." };
            pill.AddToClassList("forge-chip");
            pill.AddToClassList("forge-chip--setting");
            pill.Add(ForgeUI.Label(field.Before, "forge-chip__text"));
            var property = settings.FindPropertyRelative(field.Property);
            var inherited = overrides != null && (overrides.Mask.intValue & (int)field.Flag) == 0;
            if (inherited)
            {
                // Réglage de l'attaque : grisé ; un clic le remplace pour cet ennemi (en partant de la même valeur).
                var baseValue = Value(overrides.Base, field.Property);
                pill.Add(ForgeUI.Label(field.IsInt ? $"{baseValue:0}" : $"{baseValue:0.##}", "forge-chip__inherited"));
                pill.AddToClassList("forge-chip--inherited");
                pill.tooltip = $"Valeur de l'attaque. Cliquez pour la changer pour cet ennemi.\n{field.Tooltip}";
                pill.RegisterCallback<ClickEvent>(_ =>
                {
                    var serializedObject = settings.serializedObject;
                    serializedObject.Update();
                    if (field.IsInt)
                        property.intValue = Mathf.RoundToInt(baseValue);
                    else
                        property.floatValue = baseValue;
                    overrides.Mask.intValue |= (int)field.Flag;
                    serializedObject.ApplyModifiedProperties();
                    rebuild();
                });
            }
            else
                pill.Add(field.IsInt ? IntField(property, field.Min, field.Max) : FloatField(property, field.Min, field.Max));
            if (!string.IsNullOrEmpty(field.After))
                pill.Add(ForgeUI.Label(field.After, "forge-chip__text"));
            if (overrides != null && !inherited)
            {
                pill.AddToClassList("forge-chip--overridden");
                pill.Add(ForgeUI.Button("↺", "Revenir à la valeur de l'attaque.", () =>
                {
                    var serializedObject = settings.serializedObject;
                    serializedObject.Update();
                    overrides.Mask.intValue &= ~(int)field.Flag;
                    serializedObject.ApplyModifiedProperties();
                    rebuild();
                }, "forge-chip__reset"));
            }
            return pill;
        }

        static float Value(AttackSettings settings, string property) => property switch
        {
            "minRange" => settings.minRange,
            "maxRange" => settings.maxRange,
            "cooldown" => settings.cooldown,
            "damagePercent" => settings.damagePercent,
            "tempoPercent" => settings.tempoPercent,
            _ => 0f
        };
    }
}
