using System;
using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using TheDungeon.Editor.Dialogues;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.ContentDatabase
{
    /// <summary>
    /// Conditions en pastilles (« joueur à moins de [3] cases ✕ ») et menu « + Condition » classé par ce qui est observé.
    /// Partagé par les règles de déplacement, les attaques d'un ennemi et les étapes d'attaque.
    /// </summary>
    public sealed class ConditionChips
    {
        readonly SerializedObject serializedObject;
        readonly Action rebuild;

        static ForgeDesignLimits Limits => ForgeDesignLimits.Instance;

        public ConditionChips(SerializedObject serializedObject, Action rebuild)
        {
            this.serializedObject = serializedObject;
            this.rebuild = rebuild;
        }

        /// <summary>Pastilles des conditions de l'élément <paramref name="ownerPath"/> (qui a une liste « conditions »), puis « + Condition ».</summary>
        public VisualElement Build(string ownerPath, string emptyText)
        {
            var chips = new VisualElement();
            chips.AddToClassList("forge-chips");
            var conditions = serializedObject.FindProperty(ownerPath).FindPropertyRelative("conditions");
            for (var i = 0; i < conditions.arraySize; i++)
                chips.Add(ConditionChip(ownerPath, i));
            if (conditions.arraySize == 0 && !string.IsNullOrEmpty(emptyText))
            {
                var always = ForgeUI.Label(emptyText, "forge-chip");
                always.AddToClassList("forge-chip--empty");
                chips.Add(always);
            }
            var addCondition = ForgeUI.Button("+ Condition", "Ajoute une condition : position ou état du joueur, état de l'ennemi, lieu.", null, "forge-chip-add");
            addCondition.clicked += () => ShowConditionMenu(addCondition, ownerPath);
            addCondition.SetEnabled(conditions.arraySize < Limits.maxPatternConditions);
            chips.Add(addCondition);
            return chips;
        }

        /// <summary>Pastille d'une condition : sa phrase, avec le champ à régler au milieu (« joueur à moins de [3] cases »).</summary>
        VisualElement ConditionChip(string patternPath, int index)
        {
            var conditionsPath = patternPath + ".conditions";
            var condition = serializedObject.FindProperty(conditionsPath).GetArrayElementAtIndex(index);
            var type = (EnemyConditionType)condition.FindPropertyRelative("type").enumValueIndex;
            var (before, after) = EnemyPatternText.Chip(type);
            var chip = new VisualElement { tooltip = EnemyPatternText.Label(type) };
            chip.AddToClassList("forge-chip");
            chip.Add(ForgeUI.Label(before, "forge-chip__text"));

            var value = condition.FindPropertyRelative("value");
            switch (type)
            {
                case EnemyConditionType.MechanicAtLeast:
                    chip.Add(MechanicPicker(condition));
                    break;
                case EnemyConditionType.FlagSet:
                    var flag = new TextField { isDelayed = true, tooltip = "Marqueur d'histoire (un mot-clé sans espace, ex : quete_acceptee)." };
                    flag.AddToClassList("forge-chip__field");
                    flag.AddToClassList("forge-chip__field--wide");
                    flag.BindProperty(condition.FindPropertyRelative("flag"));
                    flag.RegisterValueChangedCallback(evt =>
                    {
                        var clean = DialogueRulesEditor.Sanitize(evt.newValue);
                        if (clean != evt.newValue)
                            flag.value = clean;
                    });
                    chip.Add(flag);
                    break;
                case EnemyConditionType.FloorIn:
                    chip.Add(new FloorMaskField(null, value, Limits.floorCount, "Étages où la condition est remplie."));
                    break;
                case EnemyConditionType.BiomeIs:
                    chip.Add(BiomePicker(condition.FindPropertyRelative("biome")));
                    break;
                case EnemyConditionType.RoomTypeIs:
                    var roomType = new EnumField(RoomType.Combat) { tooltip = "Type de la salle où se trouve l'ennemi." };
                    roomType.AddToClassList("forge-chip__field");
                    roomType.AddToClassList("forge-chip__field--wide");
                    roomType.BindProperty(condition.FindPropertyRelative("roomType"));
                    chip.Add(roomType);
                    break;
                default:
                    if (EnemyPatternText.Range(type, Limits) is { } range)
                        chip.Add(NumberField(value, range));
                    break;
            }
            if (!string.IsNullOrEmpty(after))
                chip.Add(ForgeUI.Label(after, "forge-chip__text"));

            var remove = ForgeUI.CloseButton("Retirer cette condition.", () =>
            {
                serializedObject.Update();
                serializedObject.FindProperty(conditionsPath).DeleteArrayElementAtIndex(index);
                serializedObject.ApplyModifiedProperties();
                rebuild();
            });
            remove.AddToClassList("forge-chip__remove");
            chip.Add(remove);
            return chip;
        }

        static IntegerField NumberField(SerializedProperty property, IntRange range)
        {
            var field = new IntegerField { tooltip = $"Valeurs autorisées : {range}." };
            field.AddToClassList("forge-chip__field");
            field.BindProperty(property);
            field.RegisterValueChangedCallback(evt =>
            {
                if (range.Contains(evt.newValue))
                    return;
                field.value = range.Clamp(evt.newValue);
                ForgeLists.Notify($"Valeur ramenée entre {range.min} et {range.max}.");
            });
            return field;
        }

        /// <summary>Mécanique de la progression utilisée en jeu, puis son rang minimum.</summary>
        VisualElement MechanicPicker(SerializedProperty condition)
        {
            var mechanics = TheDungeon.Editor.Play.GameLauncher.ActiveProgression?.Mechanics.ToList() ?? new List<CharacterMechanic>();
            if (mechanics.Count == 0)
                return ForgeUI.Label("(aucune mécanique dans la Progression)", "forge-chip__text");
            var id = condition.FindPropertyRelative("flag");
            var names = mechanics.Select(m => m.displayName).ToList();
            var index = Mathf.Max(0, mechanics.FindIndex(m => m.id == id.stringValue));
            var picker = new DropdownField(names, index) { tooltip = "Mécanique du personnage (outil Progression > Mécaniques)." };
            picker.AddToClassList("forge-chip__field");
            picker.AddToClassList("forge-chip__field--wide");
            var path = id.propertyPath;
            picker.RegisterValueChangedCallback(evt =>
            {
                serializedObject.Update();
                serializedObject.FindProperty(path).stringValue = mechanics[Mathf.Max(0, names.IndexOf(evt.newValue))].id;
                serializedObject.ApplyModifiedProperties();
            });
            var row = new VisualElement();
            row.AddToClassList("forge-chip__group");
            row.Add(picker);
            row.Add(ForgeUI.Label("rang ≥", "forge-chip__text"));
            row.Add(NumberField(condition.FindPropertyRelative("value"), new IntRange(1, Mathf.Max(1, mechanics[index].MaxRank))));
            return row;
        }

        VisualElement BiomePicker(SerializedProperty biome)
        {
            var biomes = AssetFactory.LoadAll<BiomeData>();
            if (biomes.Count == 0)
                return ForgeUI.Label("(aucun biome)", "forge-chip__text");
            var names = biomes.Select(b => ForgeText.NameOrRedacted(b.DisplayName)).ToList();
            var picker = new DropdownField(names, Mathf.Max(0, biomes.IndexOf(biome.objectReferenceValue as BiomeData)))
                { tooltip = "Biome de la salle où se trouve l'ennemi." };
            picker.AddToClassList("forge-chip__field");
            picker.AddToClassList("forge-chip__field--wide");
            var path = biome.propertyPath;
            picker.RegisterValueChangedCallback(evt =>
            {
                serializedObject.Update();
                serializedObject.FindProperty(path).objectReferenceValue = biomes[Mathf.Max(0, names.IndexOf(evt.newValue))];
                serializedObject.ApplyModifiedProperties();
            });
            return picker;
        }

        /// <summary>Menu « + Condition », classé par ce qui est observé : le joueur, l'ennemi, le lieu.</summary>
        void ShowConditionMenu(VisualElement anchor, string patternPath)
        {
            var menu = ForgeDropdowns.Menu();
            var groups = ((EnemyConditionType[])Enum.GetValues(typeof(EnemyConditionType))).GroupBy(EnemyPatternText.MenuGroup);
            var first = true;
            foreach (var group in groups.OrderBy(g => g.Key == "Le joueur" ? 0 : g.Key == "L'ennemi" ? 1 : 2))
            {
                if (!first)
                    menu.AddSeparator(string.Empty);
                first = false;
                menu.AddDisabledItem(group.Key, false);
                foreach (var type in group)
                {
                    var chosen = type;
                    menu.AddItem($"   {EnemyPatternText.MenuLabel(type)}", false, () => AddCondition(patternPath, chosen));
                }
            }
            menu.DropDown(anchor.worldBound, anchor, false);
        }

        void AddCondition(string patternPath, EnemyConditionType type)
        {
            serializedObject.Update();
            var conditions = serializedObject.FindProperty(patternPath + ".conditions");
            conditions.arraySize++;
            var condition = conditions.GetArrayElementAtIndex(conditions.arraySize - 1);
            condition.FindPropertyRelative("type").enumValueIndex = (int)type;
            condition.FindPropertyRelative("value").intValue = EnemyPatternText.DefaultValue(type);
            // Valeurs de départ utilisables tout de suite : première mécanique, premier biome, salle de boss.
            condition.FindPropertyRelative("flag").stringValue = type == EnemyConditionType.MechanicAtLeast
                ? TheDungeon.Editor.Play.GameLauncher.ActiveProgression?.Mechanics.FirstOrDefault()?.id ?? string.Empty
                : string.Empty;
            condition.FindPropertyRelative("biome").objectReferenceValue = type == EnemyConditionType.BiomeIs ? AssetFactory.LoadAll<BiomeData>().FirstOrDefault() : null;
            condition.FindPropertyRelative("roomType").enumValueIndex = (int)RoomType.Boss;
            serializedObject.ApplyModifiedProperties();
            rebuild();
        }

    }
}
