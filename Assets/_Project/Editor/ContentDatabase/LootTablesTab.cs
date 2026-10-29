using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using TheDungeon.Editor.Common.Charts;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.ContentDatabase
{
    public sealed class LootTablesTab : AssetListTab<LootTable>
    {
        VisualElement probabilities;
        DonutChart byRarity;

        public LootTablesTab(IToolHost window) : base(window)
        {
        }

        protected override string NewAssetName => "Nouvelle table";
        protected override string Noun => "table de butin";
        protected override bool FeminineNoun => true;

        protected override Color GetColor(LootTable table) => new(0.95f, 0.8f, 0.3f);

        protected override IEnumerable<VisualElement> GetBadges(LootTable table)
        {
            yield return ForgeUI.Badge($"{table.Entries.Count} objet(s)", new Color(0.7f, 0.7f, 0.7f), "Nombre de lignes de la table.");
            yield return ForgeUI.Badge(table.MinFloor == table.MaxFloor ? $"Étage {table.MinFloor}" : $"Étages {table.MinFloor}-{table.MaxFloor}",
                new Color(0.6f, 0.75f, 0.95f), "Étages où la table peut apparaître.");
        }

        protected override void InitializeNew(SerializedObject draft)
        {
            draft.FindProperty("maxFloor").intValue = Limits.floorCount;
        }

        protected override void BuildDetails(VisualElement container, SerializedObject serializedObject, LootTable table)
        {
            var identity = ForgeUI.Section("Identité", container);
            identity.Add(ForgeUI.Text("Nom", "displayName", "Nom de la table. Le fichier sera nommé LT_<Nom> automatiquement."));

            var rules = ForgeUI.Section("Règles de tirage", container);
            rules.Add(ForgeUI.Slider("Chance de butin", "dropChance", new FloatRange(0f, 1f), "Probabilité qu'un tirage donne un objet. 0.5 = une fois sur deux."));
            rules.Add(ForgeUI.Slider("Tirages", "rolls", Limits.lootRolls, "Nombre de tirages effectués à chaque fois."));
            var floors = new IntRange(1, Limits.floorCount);
            rules.Add(ForgeUI.Slider("Étage minimum", "minFloor", floors, "Premier étage où la table est utilisée."));
            rules.Add(ForgeUI.Slider("Étage maximum", "maxFloor", floors, "Dernier étage où la table est utilisée."));

            container.Add(ForgeLists.Array(serializedObject, "entries", "Objets", "Objets pouvant tomber et leur poids.",
                Limits.maxLootEntries,
                (element, _) =>
                {
                    var item = new AssetPicker<ItemData>("", element.FindPropertyRelative("item"), "Objet pouvant tomber.");
                    item.style.width = 260;
                    var weight = ForgeUI.Slider("Poids", null, Limits.lootWeight, "Plus le poids est élevé, plus l'objet tombe souvent.");
                    weight.BindProperty(element.FindPropertyRelative("weight"));
                    weight.style.flexGrow = 1;
                    return ForgeLists.Row(item, weight);
                },
                element =>
                {
                    element.FindPropertyRelative("item").objectReferenceValue = null;
                    element.FindPropertyRelative("weight").intValue = Limits.GetRarity(Rarity.Common).defaultLootWeight;
                    return true;
                }));

            var shortcuts = ForgeUI.Section("Raccourcis", container);
            var rarity = new EnumField("Rareté", Rarity.Common) { tooltip = "Rareté des objets à ajouter." };
            shortcuts.Add(ForgeLists.Row(rarity,
                ForgeUI.Button("Ajouter tous les objets valides de cette rareté",
                    "Ajoute les objets valides de la rareté choisie absents de la table, avec le poids par défaut de leur rareté.",
                    () => AddAllOfRarity(serializedObject, (Rarity)rarity.value))));
            shortcuts.Add(ForgeUI.Button("Appliquer les poids par défaut des raretés",
                "Remplace chaque poids par celui défini pour la rareté de l'objet (Forge Studio > Limites (dev)).",
                () => ApplyDefaultWeights(serializedObject)));

            var preview = ForgeUI.Section("Probabilités par tirage", container);
            var row = new VisualElement();
            row.AddToClassList("forge-chart-row");
            byRarity = new DonutChart("Chance d'obtenir un objet, par rareté");
            row.Add(byRarity);
            probabilities = new VisualElement();
            probabilities.AddToClassList("forge-chart-row__grow");
            row.Add(probabilities);
            preview.Add(row);
        }

        void AddAllOfRarity(SerializedObject serializedObject, Rarity rarity)
        {
            var entries = serializedObject.FindProperty("entries");
            var present = new HashSet<Object>();
            for (var i = 0; i < entries.arraySize; i++)
                present.Add(entries.GetArrayElementAtIndex(i).FindPropertyRelative("item").objectReferenceValue);

            var added = 0;
            foreach (var item in AssetFactory.LoadAll<ItemData>().Where(item => item.Rarity == rarity && !present.Contains(item) && ValidationCache.IsValid(item)))
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
                ForgeLists.Notify($"Aucun nouvel objet {rarity} valide à ajouter.");
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
            ProbabilityBars.Fill(probabilities, table, null, Limits);
            ProbabilityBars.FillByRarity(byRarity, table, null, Limits);
        }
    }
}
