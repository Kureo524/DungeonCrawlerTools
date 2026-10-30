using System;
using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Dialogues
{
    /// <summary>Listes éditables de conditions et de conséquences, avec un champ adapté à chaque type.</summary>
    public static class DialogueRulesEditor
    {
        public static VisualElement Conditions(SerializedObject npc, string path, string title, string tooltip, Func<IEnumerable<string>> knownFlags)
        {
            var limits = ForgeDesignLimits.Instance;
            var types = Enum.GetValues(typeof(ConditionType)).Cast<ConditionType>().ToList();
            return ForgeLists.Array(npc, path, title, tooltip, limits.maxDialogueRules,
                (element, _) => Row(element, types.Select(t => DialogueText.Label(t)).ToList(),
                    index => (DialogueText.UsesValue(types[index]), DialogueText.UsesFlag(types[index]), DialogueText.UsesItem(types[index])),
                    knownFlags, ValueRange(types), index => DialogueText.UsesFloors(types[index]), index => DialogueText.UsesMechanic(types[index])),
                element =>
                {
                    element.FindPropertyRelative("type").enumValueIndex = (int)ConditionType.FlagSet;
                    element.FindPropertyRelative("value").intValue = 1;
                    element.FindPropertyRelative("flag").stringValue = string.Empty;
                    element.FindPropertyRelative("item").objectReferenceValue = null;
                    return true;
                });
        }

        public static VisualElement Consequences(SerializedObject npc, string path, string title, string tooltip, Func<IEnumerable<string>> knownFlags)
        {
            var limits = ForgeDesignLimits.Instance;
            var types = Enum.GetValues(typeof(ConsequenceType)).Cast<ConsequenceType>().ToList();
            return ForgeLists.Array(npc, path, title, tooltip, limits.maxDialogueRules,
                (element, _) => Row(element, types.Select(t => DialogueText.Label(t)).ToList(),
                    index => (DialogueText.UsesValue(types[index]), DialogueText.UsesFlag(types[index]), DialogueText.UsesItem(types[index])),
                    knownFlags, _ => new IntRange(1, 1000)),
                element =>
                {
                    element.FindPropertyRelative("type").enumValueIndex = (int)ConsequenceType.SetFlag;
                    element.FindPropertyRelative("value").intValue = 10;
                    element.FindPropertyRelative("flag").stringValue = string.Empty;
                    element.FindPropertyRelative("item").objectReferenceValue = null;
                    return true;
                });
        }

        static Func<int, IntRange> ValueRange(List<ConditionType> types) => index => types[index] switch
        {
            ConditionType.PlayerLevelAtLeast or ConditionType.PlayerLevelBelow => new IntRange(1, ForgeDesignLimits.Instance.maxLevelCap),
            ConditionType.FloorAtLeast => new IntRange(1, ForgeDesignLimits.Instance.floorCount),
            _ => new IntRange(0, 10000)
        };

        /// <summary>Une ligne : liste déroulante du type, puis le champ correspondant (nombre, marqueur ou objet).</summary>
        static VisualElement Row(SerializedProperty element, List<string> labels, Func<int, (bool value, bool flag, bool item)> uses,
            Func<IEnumerable<string>> knownFlags, Func<int, IntRange> range, Func<int, bool> usesFloors = null, Func<int, bool> usesMechanic = null)
        {
            var row = new VisualElement();
            row.AddToClassList("forge-rule-row");
            var typeProperty = element.FindPropertyRelative("type");
            var type = new DropdownField(labels, Mathf.Clamp(typeProperty.enumValueIndex, 0, labels.Count - 1)) { tooltip = "Type de règle." };
            type.AddToClassList("forge-rule-row__type");
            row.Add(type);
            var valueArea = new VisualElement();
            valueArea.AddToClassList("forge-rule-row__value");
            row.Add(valueArea);

            void BuildValue()
            {
                valueArea.Clear();
                var index = typeProperty.enumValueIndex;
                var (usesValue, usesFlag, usesItem) = uses(index);
                if (usesMechanic != null && usesMechanic(index))
                {
                    valueArea.Add(MechanicField(element));
                }
                else if (usesFloors != null && usesFloors(index))
                {
                    // Étages précis : une pastille par étage, la réponse n'est proposée que sur les étages cochés.
                    var floors = element.FindPropertyRelative("value");
                    if (!FloorSet.Floors(floors.intValue).Any(f => f <= ForgeDesignLimits.Instance.floorCount))
                    {
                        element.serializedObject.Update();
                        floors.intValue = FloorSet.Of(1);
                        element.serializedObject.ApplyModifiedProperties();
                    }
                    valueArea.Add(new FloorMaskField(null, floors, ForgeDesignLimits.Instance.floorCount, "Étages où la condition est remplie."));
                }
                else if (usesValue)
                {
                    var bounds = range(index);
                    var value = new IntegerField { tooltip = $"Valeur ({bounds})." };
                    value.BindProperty(element.FindPropertyRelative("value"));
                    value.RegisterValueChangedCallback(evt =>
                    {
                        if (bounds.Contains(evt.newValue))
                            return;
                        value.value = bounds.Clamp(evt.newValue);
                        ForgeLists.Notify($"Valeur ramenée entre {bounds.min} et {bounds.max}.");
                    });
                    valueArea.Add(value);
                }
                if (usesFlag)
                    valueArea.Add(FlagField(element.FindPropertyRelative("flag"), knownFlags));
                if (usesItem)
                    valueArea.Add(new AssetPicker<ItemData>(string.Empty, element.FindPropertyRelative("item"), "Objet concerné."));
            }

            type.RegisterValueChangedCallback(evt =>
            {
                var index = labels.IndexOf(evt.newValue);
                if (index < 0)
                    return;
                element.serializedObject.Update();
                typeProperty.enumValueIndex = index;
                element.serializedObject.ApplyModifiedProperties();
                BuildValue();
            });
            BuildValue();
            return row;
        }

        /// <summary>Mécanique de la progression utilisée en jeu, et le niveau qu'elle doit avoir au minimum.</summary>
        public static VisualElement MechanicField(SerializedProperty element)
        {
            var row = new VisualElement();
            row.AddToClassList("forge-row");
            row.style.flexGrow = 1;
            var mechanics = TheDungeon.Editor.Play.GameLauncher.ActiveProgression?.Mechanics.ToList() ?? new List<CharacterMechanic>();
            if (mechanics.Count == 0)
            {
                row.Add(ForgeUI.Label("La progression utilisée en jeu n'a aucune mécanique (outil Progression).", "forge-hint"));
                return row;
            }
            var id = element.FindPropertyRelative("flag");
            var level = element.FindPropertyRelative("value");
            var names = mechanics.Select(m => m.displayName).ToList();
            var index = mechanics.FindIndex(m => m.id == id.stringValue);
            if (index < 0)
            {
                // Pas encore de mécanique choisie : la première, débloquée (niveau 1).
                element.serializedObject.Update();
                index = 0;
                id.stringValue = mechanics[0].id;
                level.intValue = Mathf.Clamp(level.intValue, 1, mechanics[0].MaxRank);
                element.serializedObject.ApplyModifiedProperties();
            }
            var picker = new DropdownField(names, index) { tooltip = "Mécanique du personnage (outil Progression > Mécaniques)." };
            picker.style.flexGrow = 1;
            var levelField = new SliderInt(1, Math.Max(1, mechanics[index].MaxRank))
                { showInputField = true, tooltip = "Niveau minimum de la mécanique : 1 = débloquée, puis chaque amélioration." };
            levelField.style.flexGrow = 1;
            levelField.BindProperty(level);
            picker.RegisterValueChangedCallback(evt =>
            {
                var chosen = mechanics[Mathf.Max(0, names.IndexOf(evt.newValue))];
                element.serializedObject.Update();
                id.stringValue = chosen.id;
                level.intValue = Mathf.Clamp(level.intValue, 1, chosen.MaxRank);
                element.serializedObject.ApplyModifiedProperties();
                levelField.highValue = Math.Max(1, chosen.MaxRank);
            });
            row.Add(picker);
            row.Add(levelField);
            return row;
        }

        /// <summary>Nom de marqueur, avec la liste des marqueurs déjà utilisés pour éviter les fautes de frappe.</summary>
        public static VisualElement FlagField(SerializedProperty property, Func<IEnumerable<string>> knownFlags)
        {
            var field = new TextField
            {
                isDelayed = true,
                tooltip = "Marqueur d'histoire : un mot-clé sans espace (ex : quete_forgeron_acceptee). " +
                          "Il est activé par une conséquence et testé par une condition."
            };
            field.BindProperty(property);
            field.RegisterValueChangedCallback(evt =>
            {
                var clean = Sanitize(evt.newValue);
                if (clean != evt.newValue)
                    field.value = clean;
            });
            var pick = ForgeUI.Button("▾", "Choisir parmi les marqueurs existants.", () =>
            {
                var menu = new GenericMenu();
                var flags = knownFlags().Distinct().OrderBy(f => f).ToList();
                if (flags.Count == 0)
                    menu.AddDisabledItem(new GUIContent("Aucun marqueur pour l'instant"));
                foreach (var flag in flags)
                    menu.AddItem(new GUIContent(flag), flag == property.stringValue, () =>
                    {
                        property.serializedObject.Update();
                        property.stringValue = flag;
                        property.serializedObject.ApplyModifiedProperties();
                    });
                menu.ShowAsContext();
            }, "forge-button--icon");
            var row = ForgeLists.Row(field, pick);
            row.style.flexGrow = 1;
            return row;
        }

        /// <summary>Marqueur en minuscules, sans accent ni espace : « Quête acceptée » devient « quete_acceptee ».</summary>
        public static string Sanitize(string flag)
        {
            if (string.IsNullOrEmpty(flag))
                return string.Empty;
            var pascal = NamingConvention.ToPascalCase(flag.Replace('_', ' '));
            var builder = new System.Text.StringBuilder();
            for (var i = 0; i < pascal.Length; i++)
            {
                if (char.IsUpper(pascal[i]) && i > 0)
                    builder.Append('_');
                builder.Append(char.ToLowerInvariant(pascal[i]));
            }
            return builder.ToString();
        }

        /// <summary>Tous les marqueurs utilisés dans les dialogues du projet et dans la conversation en cours.</summary>
        public static IEnumerable<string> FlagsUsedIn(IEnumerable<NpcData> npcs)
        {
            foreach (var npc in npcs)
            {
                foreach (var entry in npc.Entries)
                    foreach (var condition in entry.conditions)
                        if (!string.IsNullOrWhiteSpace(condition.flag)) yield return condition.flag;
                foreach (var node in npc.Nodes)
                {
                    foreach (var consequence in node.consequences)
                        if (!string.IsNullOrWhiteSpace(consequence.flag)) yield return consequence.flag;
                    foreach (var choice in node.choices)
                    {
                        foreach (var condition in choice.conditions)
                            if (!string.IsNullOrWhiteSpace(condition.flag)) yield return condition.flag;
                        foreach (var consequence in choice.consequences)
                            if (!string.IsNullOrWhiteSpace(consequence.flag)) yield return consequence.flag;
                    }
                }
            }
        }
    }
}
