using System;
using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using TheDungeon.Editor.Common.Charts;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Biomes
{
    /// <summary>Presets de biome : visuels, ennemis, décors, coffres et bonus de butin, réutilisés par l'éditeur de salles.</summary>
    public sealed class BiomesTab : AssetListTab<BiomeData>
    {
        VisualElement tilePreview;
        DropdownField lootPreviewTable;
        VisualElement lootPreview;
        DonutChart lootByRarity;
        DonutChart spawnShare;

        public BiomesTab(IToolHost window) : base(window)
        {
        }

        protected override string NewAssetName => "Nouveau biome";
        protected override string Noun => "biome";

        protected override Sprite GetPreview(BiomeData biome) => biome.FloorSprites.FirstOrDefault(sprite => sprite != null);
        protected override Color GetColor(BiomeData biome) => biome.Color;

        protected override IEnumerable<VisualElement> GetBadges(BiomeData biome)
        {
            yield return ForgeUI.Badge(biome.MinFloor == biome.MaxFloor ? $"Étage {biome.MinFloor}" : $"Étages {biome.MinFloor}-{biome.MaxFloor}",
                new Color(0.6f, 0.75f, 0.95f), "Étages où le biome peut apparaître.");
            yield return ForgeUI.Badge($"{biome.Enemies.Count} ennemi(s)", new Color(0.85f, 0.55f, 0.55f), "Ennemis proposés.");
            yield return ForgeUI.Badge($"{biome.Props.Count} décor(s)", new Color(0.6f, 0.8f, 0.6f), "Décors proposés.");
            yield return ForgeUI.Badge($"{biome.ChestLootTables.Count} table(s)", new Color(0.95f, 0.8f, 0.3f), "Tables de butin des coffres.");
        }

        protected override void InitializeNew(SerializedObject draft)
        {
            draft.FindProperty("maxFloor").intValue = Limits.floorCount;
        }

        protected override void BuildDetails(VisualElement container, SerializedObject serializedObject, BiomeData biome)
        {
            var identity = ForgeUI.Section("Identité", container);
            identity.Add(ForgeUI.Text("Nom", "displayName", "Nom du biome. Le fichier sera nommé BIO_<Nom> automatiquement."));
            identity.Add(new ColorField("Couleur") { showAlpha = false, bindingPath = "color", tooltip = "Couleur du biome dans les listes et sur le plan des étages." });
            var floors = new IntRange(1, Limits.floorCount);
            identity.Add(ForgeUI.Slider("Étage minimum", "minFloor", floors, "Premier étage où le biome peut apparaître."));
            identity.Add(ForgeUI.Slider("Étage maximum", "maxFloor", floors, "Dernier étage où le biome peut apparaître."));

            var visuals = ForgeUI.Section("Visuels", container);
            visuals.Add(new SpriteField("Mur", serializedObject.FindProperty("wallSprite"), "Biomes", "Sprite des murs de toutes les salles du biome."));
            visuals.Add(new SpriteField("Trou", serializedObject.FindProperty("pitSprite"), "Biomes", "Sprite des trous (eau, lave, vide…)."));
            visuals.Add(ForgeLists.Array(serializedObject, "floorSprites", "Variantes de sol", "Les variantes sont mélangées automatiquement dans les salles.",
                Limits.maxBiomeFloorVariants,
                (element, _) => new SpriteField(null, element, "Biomes", "Variante de sol.")));
            visuals.Add(ForgeUI.Label("Aperçu", "forge-section-title"));
            tilePreview = new VisualElement();
            tilePreview.AddToClassList("forge-tile-preview");
            visuals.Add(tilePreview);

            container.Add(ForgeLists.Array(serializedObject, "enemies", "Ennemis du biome",
                "Seuls ces ennemis pourront être placés dans les salles du biome. Ajoutez au moins un boss pour pouvoir créer une salle de boss.",
                Limits.maxBiomeEnemies,
                (element, _) =>
                {
                    var enemy = new AssetPicker<EnemyData>("", element.FindPropertyRelative("enemy"), "Ennemi autorisé dans ce biome.");
                    enemy.style.width = 280;
                    var weight = ForgeUI.Slider("Poids", null, Limits.biomeSpawnWeight, "Fréquence d'apparition relative de l'ennemi dans ce biome.");
                    weight.BindProperty(element.FindPropertyRelative("spawnWeight"));
                    weight.style.flexGrow = 1;
                    return ForgeLists.Row(enemy, weight);
                },
                element =>
                {
                    element.FindPropertyRelative("enemy").objectReferenceValue = null;
                    element.FindPropertyRelative("spawnWeight").intValue = 10;
                    return true;
                }));

            var spawnSection = ForgeUI.Section("Répartition des apparitions", container);
            spawnShare = new DonutChart("Part de chaque ennemi dans les apparitions");
            spawnSection.Add(spawnShare);

            container.Add(ForgeLists.Array(serializedObject, "props", "Décors du biome", "Décors disponibles dans la palette de l'éditeur de salles.",
                Limits.maxBiomeProps,
                (element, _) =>
                {
                    var row = new VisualElement();
                    row.Add(new SpriteField(null, element.FindPropertyRelative("sprite"), "Biomes", "Visuel du décor."));
                    var name = new TextField("Nom") { tooltip = "Nom affiché dans la palette." };
                    name.BindProperty(element.FindPropertyRelative("name"));
                    var blocks = new Toggle("Bloque le passage") { tooltip = "Coché : le joueur et les ennemis ne peuvent pas traverser ce décor." };
                    blocks.BindProperty(element.FindPropertyRelative("blocksMovement"));
                    row.Add(ForgeLists.Row(name, blocks));
                    return row;
                },
                element =>
                {
                    element.FindPropertyRelative("sprite").objectReferenceValue = null;
                    element.FindPropertyRelative("name").stringValue = string.Empty;
                    element.FindPropertyRelative("blocksMovement").boolValue = true;
                    return true;
                }));

            container.Add(ForgeLists.Array(serializedObject, "chestLootTables", "Tables de butin des coffres",
                "Tables que les coffres des salles de ce biome peuvent utiliser.", Limits.maxBiomeLootTables,
                (element, _) => new AssetPicker<LootTable>("", element, "Table de butin utilisable par les coffres."),
                element =>
                {
                    element.objectReferenceValue = null;
                    return true;
                }));

            container.Add(ForgeLists.Array(serializedObject, "rarityBoosts", "Bonus de butin par rareté",
                "Multiplie le poids des objets d'une rareté pour tous les tirages de butin du biome.", Enum.GetValues(typeof(Rarity)).Length,
                (element, _) =>
                {
                    var rarity = new EnumField(Rarity.Common) { tooltip = "Rareté concernée." };
                    rarity.BindProperty(element.FindPropertyRelative("rarity"));
                    rarity.style.width = 110;
                    var multiplier = ForgeUI.Slider("×", null, Limits.biomeRarityBoost, "2 = deux fois plus fréquent, 0.5 = deux fois moins, 0 = jamais.");
                    multiplier.BindProperty(element.FindPropertyRelative("multiplier"));
                    multiplier.style.flexGrow = 1;
                    return ForgeLists.Row(rarity, multiplier);
                },
                element => InitializeBoost(serializedObject, element)));

            var lootSection = ForgeUI.Section("Aperçu du butin des coffres (bonus appliqués)", container);
            lootPreviewTable = new DropdownField("Table") { tooltip = "Table de butin à prévisualiser avec les bonus de ce biome." };
            lootPreviewTable.RegisterValueChangedCallback(_ => RefreshLootPreview());
            lootSection.Add(lootPreviewTable);
            var lootRow = new VisualElement();
            lootRow.AddToClassList("forge-chart-row");
            lootByRarity = new DonutChart("Par rareté, bonus inclus");
            lootRow.Add(lootByRarity);
            lootPreview = new VisualElement();
            lootPreview.AddToClassList("forge-chart-row__grow");
            lootRow.Add(lootPreview);
            lootSection.Add(lootRow);
        }

        static bool InitializeBoost(SerializedObject serializedObject, SerializedProperty element)
        {
            var boosts = serializedObject.FindProperty("rarityBoosts");
            var used = new HashSet<int>();
            for (var i = 0; i < boosts.arraySize - 1; i++)
                used.Add(boosts.GetArrayElementAtIndex(i).FindPropertyRelative("rarity").enumValueIndex);
            var free = Enumerable.Range(0, Enum.GetValues(typeof(Rarity)).Length).Where(index => !used.Contains(index)).DefaultIfEmpty(-1).First();
            if (free < 0)
                return false;
            element.FindPropertyRelative("rarity").enumValueIndex = free;
            element.FindPropertyRelative("multiplier").floatValue = 1.5f;
            return true;
        }

        protected override void OnRefresh(BiomeData biome)
        {
            RefreshTilePreview(biome);
            // Ordre fixe des couleurs : celui de la liste du biome. Au-delà de 5, regroupé dans « Autres ».
            spawnShare.SetData(biome.Enemies.Where(e => e.enemy != null && e.spawnWeight > 0)
                .Select((e, index) => new DonutChart.Segment(e.enemy.DisplayName + (e.enemy.IsBoss ? " (boss)" : string.Empty), e.spawnWeight,
                    index < 5 ? ChartColors.Series(index) : ChartColors.Neutral)), "poids total");
            var tables = biome.ChestLootTables.Where(table => table != null).Select(table => table.name).ToList();
            lootPreviewTable.choices = tables;
            if (!tables.Contains(lootPreviewTable.value))
                lootPreviewTable.SetValueWithoutNotify(tables.FirstOrDefault());
            RefreshLootPreview();
        }

        /// <summary>Petite salle d'exemple dessinée avec les visuels du biome.</summary>
        void RefreshTilePreview(BiomeData biome)
        {
            tilePreview.Clear();
            const int width = 9;
            const int height = 5;
            for (var y = height - 1; y >= 0; y--)
            {
                var row = new VisualElement();
                row.AddToClassList("forge-row");
                for (var x = 0; x < width; x++)
                {
                    var tile = x == 0 || y == 0 || x == width - 1 || y == height - 1 ? TileType.Wall
                        : x == 6 && y == 2 ? TileType.Pit : TileType.Floor;
                    var cell = new VisualElement { tooltip = tile.ToString() };
                    cell.AddToClassList("forge-tile-preview__cell");
                    var sprite = biome.GetTileSprite(tile, x, y);
                    if (sprite != null)
                        cell.style.backgroundImage = new StyleBackground(sprite);
                    else
                        cell.AddToClassList("forge-tile-preview__cell--missing");
                    row.Add(cell);
                }
                tilePreview.Add(row);
            }
        }

        void RefreshLootPreview()
        {
            var biome = Draft;
            if (biome == null)
                return;
            var table = biome.ChestLootTables.FirstOrDefault(t => t != null && t.name == lootPreviewTable.value);
            if (table == null)
            {
                lootPreview.Clear();
                lootPreview.Add(ForgeUI.Label("Ajoutez une table de butin pour voir l'aperçu.", "forge-hint"));
                lootByRarity.SetData(Enumerable.Empty<DonutChart.Segment>(), "par tirage");
                return;
            }
            ProbabilityBars.Fill(lootPreview, table, biome.GetRarityMultiplier, Limits);
            ProbabilityBars.FillByRarity(lootByRarity, table, biome.GetRarityMultiplier, Limits);
        }
    }
}
