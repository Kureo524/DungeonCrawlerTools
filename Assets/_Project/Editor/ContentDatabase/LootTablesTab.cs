using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.ContentDatabase
{
    public sealed class LootTablesTab : AssetListTab<LootTable>
    {
        static readonly Color NothingColor = new(0.45f, 0.45f, 0.45f);

        VisualElement probabilities;

        protected override string NewAssetName => "Nouvelle table";
        protected override string Noun => "table de butin";

        protected override string GetLabel(LootTable table) => table.name.StartsWith("LT_") ? table.name[3..] : table.name;
        protected override Color GetColor(LootTable table) => new(0.95f, 0.8f, 0.3f);

        protected override IEnumerable<VisualElement> GetBadges(LootTable table)
        {
            yield return ForgeUI.Badge($"{table.Entries.Count} objet(s)", new Color(0.7f, 0.7f, 0.7f), "Nombre de lignes de la table.");
            yield return ForgeUI.Badge(table.MinFloor == table.MaxFloor ? $"Étage {table.MinFloor}" : $"Étages {table.MinFloor}-{table.MaxFloor}",
                new Color(0.6f, 0.75f, 0.95f), "Étages où la table peut apparaître.");
        }

        protected override void BuildDetails(VisualElement container, SerializedObject serializedObject, LootTable table)
        {
            var identity = ForgeUI.Section("Identité", container);
            var nameField = new TextField("Nom")
            {
                value = GetLabel(table),
                isDelayed = true,
                tooltip = "Nom de la table. Le fichier est renommé automatiquement (LT_…) quand vous validez."
            };
            nameField.RegisterValueChangedCallback(evt =>
            {
                var body = NamingConvention.ToPascalCase(evt.newValue);
                if (body.Length == 0)
                {
                    ForgeLists.Notify("Le nom ne peut pas être vide.");
                    nameField.SetValueWithoutNotify(evt.previousValue);
                    return;
                }
                var error = AssetDatabase.RenameAsset(AssetDatabase.GetAssetPath(table), "LT_" + body);
                if (!string.IsNullOrEmpty(error))
                    ForgeLists.Notify(error);
            });
            identity.Add(nameField);

            var rules = ForgeUI.Section("Règles de tirage", container);
            rules.Add(ForgeUI.Slider("Chance de butin", "dropChance", new FloatRange(0f, 1f), "Probabilité qu'un tirage donne un objet. 0.5 = une fois sur deux."));
            rules.Add(ForgeUI.Slider("Tirages", "rolls", Limits.lootRolls, "Nombre de tirages effectués à chaque fois."));
            var floors = new IntRange(1, Limits.floorCount);
            rules.Add(ForgeUI.Slider("Étage minimum", "minFloor", floors, "Premier étage où la table est utilisée."));
            rules.Add(ForgeUI.Slider("Étage maximum", "maxFloor", floors, "Dernier étage où la table est utilisée."));

            var entries = ForgeLists.Bound(serializedObject, "entries", "Objets", "Objets pouvant tomber et leur poids.",
                Limits.maxLootEntries, () =>
                {
                    var item = ForgeUI.Object<ItemData>("", "item", "Objet pouvant tomber. Glissez-le depuis la Content Database ou le Project.");
                    item.style.width = 180;
                    var weight = ForgeUI.Slider("Poids", "weight", Limits.lootWeight, "Plus le poids est élevé, plus l'objet tombe souvent.");
                    weight.style.flexGrow = 1;
                    return ForgeLists.Row(item, weight);
                },
                element => element.FindPropertyRelative("weight").intValue = Limits.GetRarity(Rarity.Common).defaultLootWeight);
            container.Add(entries);

            var shortcuts = ForgeUI.Section("Raccourcis", container);
            var rarity = new EnumField("Rareté", Rarity.Common) { tooltip = "Rareté des objets à ajouter." };
            shortcuts.Add(ForgeLists.Row(rarity,
                ForgeUI.Button("Ajouter tous les objets de cette rareté",
                    "Ajoute les objets de la rareté choisie absents de la table, avec le poids par défaut de leur rareté.",
                    () => AddAllOfRarity(serializedObject, (Rarity)rarity.value))));
            shortcuts.Add(ForgeUI.Button("Appliquer les poids par défaut des raretés",
                "Remplace chaque poids par celui défini pour la rareté de l'objet (Tools > Settings).",
                () => ApplyDefaultWeights(serializedObject)));

            var preview = ForgeUI.Section("Probabilités par tirage", container);
            probabilities = new VisualElement();
            preview.Add(probabilities);
        }

        void AddAllOfRarity(SerializedObject serializedObject, Rarity rarity)
        {
            var entries = serializedObject.FindProperty("entries");
            var present = new HashSet<Object>();
            for (var i = 0; i < entries.arraySize; i++)
                present.Add(entries.GetArrayElementAtIndex(i).FindPropertyRelative("item").objectReferenceValue);

            var added = 0;
            foreach (var item in AssetFactory.LoadAll<ItemData>().Where(item => item.Rarity == rarity && !present.Contains(item)))
            {
                if (entries.arraySize >= Limits.maxLootEntries)
                {
                    ForgeLists.Notify($"Table pleine ({Limits.maxLootEntries} objets maximum).");
                    break;
                }
                entries.arraySize++;
                var element = entries.GetArrayElementAtIndex(entries.arraySize - 1);
                element.FindPropertyRelative("item").objectReferenceValue = item;
                element.FindPropertyRelative("weight").intValue = Limits.GetRarity(rarity).defaultLootWeight;
                added++;
            }
            serializedObject.ApplyModifiedProperties();
            if (added == 0)
                ForgeLists.Notify($"Aucun nouvel objet {rarity} à ajouter.");
        }

        void ApplyDefaultWeights(SerializedObject serializedObject)
        {
            var entries = serializedObject.FindProperty("entries");
            for (var i = 0; i < entries.arraySize; i++)
            {
                var element = entries.GetArrayElementAtIndex(i);
                if (element.FindPropertyRelative("item").objectReferenceValue is ItemData item)
                    element.FindPropertyRelative("weight").intValue = Limits.GetRarity(item.Rarity).defaultLootWeight;
            }
            serializedObject.ApplyModifiedProperties();
        }

        protected override void OnRefresh(LootTable table)
        {
            probabilities.Clear();
            var rows = table.Entries
                .Select(entry => (label: entry.item != null ? entry.item.DisplayName : "(objet manquant)",
                    probability: table.GetProbability(entry),
                    color: entry.item != null ? Limits.GetRarity(entry.item.Rarity).color : Color.red))
                .Append(("Rien", table.TotalWeight == 0 ? 1f : 1f - table.DropChance, NothingColor))
                .OrderByDescending(row => row.Item2)
                .ToList();

            var max = Mathf.Max(0.0001f, rows.Max(row => row.Item2));
            foreach (var (label, probability, color) in rows)
                probabilities.Add(Bar(label, probability, probability / max, color));
        }

        static VisualElement Bar(string label, float probability, float fill, Color color)
        {
            var row = new VisualElement { tooltip = $"{label} : {probability:P1} de chance à chaque tirage." };
            row.AddToClassList("forge-bar-row");
            var name = ForgeUI.Label(label, "forge-bar-row__label");
            var track = new VisualElement();
            track.AddToClassList("forge-bar-row__track");
            var bar = new VisualElement();
            bar.AddToClassList("forge-bar-row__fill");
            bar.style.width = Length.Percent(fill * 100f);
            bar.style.backgroundColor = color;
            track.Add(bar);
            row.Add(name);
            row.Add(track);
            row.Add(ForgeUI.Label($"{probability:P1}", "forge-bar-row__value"));
            return row;
        }
    }
}
