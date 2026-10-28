using System;
using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using TheDungeon.Editor.Common.Charts;
using TheDungeon.Editor.Validation;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Studio
{
    /// <summary>Vue d'ensemble : avancement par rapport au GDD, santé des données et répartition du contenu.</summary>
    public sealed class DashboardView
    {
        const int MaxListedIssues = 12;

        readonly IToolHost host;
        readonly Dictionary<string, ProgressRing> rings = new();

        VisualElement gddRules;
        Label errorsValue;
        Label warningsValue;
        Label validValue;
        VisualElement issuesByRule;
        IssuesPanel issuesList;
        Label issuesMore;
        DonutChart itemsByRarity;
        DonutChart roomsByType;
        VisualElement roomsByBiome;
        LineChart playerHealth;

        public DashboardView(IToolHost host)
        {
            this.host = host;
        }

        public VisualElement Build()
        {
            var scroll = new ScrollView();
            scroll.AddToClassList("forge-dashboard");

            scroll.Add(ForgeUI.Label("Avancement de la production", "forge-dashboard__heading"));
            var progress = new VisualElement();
            progress.AddToClassList("forge-card-grid");
            AddRing(progress, "items", "Objets", ForgeStudioWindow.Items);
            AddRing(progress, "enemies", "Ennemis", ForgeStudioWindow.Enemies);
            AddRing(progress, "bosses", "Boss", ForgeStudioWindow.Enemies);
            AddRing(progress, "loot", "Tables de butin", ForgeStudioWindow.LootTables);
            AddRing(progress, "rooms", "Salles", ForgeStudioWindow.Rooms);
            AddRing(progress, "floors", "Étages", ForgeStudioWindow.Floors);
            AddRing(progress, "levels", "Niveaux du joueur", ForgeStudioWindow.Progression);
            scroll.Add(progress);

            scroll.Add(ForgeUI.Label("Règles de cohérence du GDD", "forge-dashboard__heading"));
            gddRules = new VisualElement();
            gddRules.AddToClassList("forge-card-grid");
            scroll.Add(gddRules);

            scroll.Add(ForgeUI.Label("Santé des données", "forge-dashboard__heading"));
            var health = new VisualElement();
            health.AddToClassList("forge-card-grid");
            errorsValue = StatTile(health, "Erreurs", "console.erroricon", "Bloquent l'enregistrement et le build.", "forge-stat--critical");
            warningsValue = StatTile(health, "Avertissements", "console.warnicon", "À vérifier, mais ne bloquent pas.", "forge-stat--warning");
            validValue = StatTile(health, "Données valides", "TestPassed", "Part des assets sans aucune erreur.", "forge-stat--good");
            scroll.Add(health);

            var issuesRow = new VisualElement();
            issuesRow.AddToClassList("forge-card-grid");
            var byRuleCard = Card(issuesRow, "Problèmes par règle", "forge-card--wide");
            issuesByRule = new VisualElement();
            byRuleCard.Add(issuesByRule);
            var listCard = Card(issuesRow, "À corriger en priorité", "forge-card--wide");
            listCard.Add(ForgeUI.Label("Cliquez sur un problème pour ouvrir l'élément concerné.", "forge-hint"));
            issuesList = new IssuesPanel(showContext: true);
            issuesList.Clicked += issue => host.Reveal(issue.Context);
            issuesList.Fixed += Refresh;
            listCard.Add(issuesList);
            issuesMore = ForgeUI.Label(string.Empty, "forge-hint");
            listCard.Add(issuesMore);
            scroll.Add(issuesRow);

            scroll.Add(ForgeUI.Label("Répartition du contenu", "forge-dashboard__heading"));
            var charts = new VisualElement();
            charts.AddToClassList("forge-card-grid");
            itemsByRarity = new DonutChart("Objets par rareté");
            Card(charts, null).Add(itemsByRarity);
            roomsByType = new DonutChart("Salles par type");
            Card(charts, null).Add(roomsByType);
            var biomeCard = Card(charts, "Salles par biome");
            roomsByBiome = new VisualElement();
            biomeCard.Add(roomsByBiome);
            playerHealth = new LineChart("Progression du joueur : PV max", 130f);
            var healthCard = Card(charts, null, "forge-card--wide");
            healthCard.Add(playerHealth);
            scroll.Add(charts);

            Refresh();
            return scroll;
        }

        void AddRing(VisualElement parent, string key, string title, string section)
        {
            var ring = new ProgressRing(title, () => host.ShowSection(section));
            rings[key] = ring;
            parent.Add(ring);
        }

        static Label StatTile(VisualElement parent, string title, string icon, string tooltip, string className)
        {
            var tile = new VisualElement { tooltip = tooltip };
            tile.AddToClassList("forge-card");
            tile.AddToClassList("forge-stat");
            tile.AddToClassList(className);
            var header = new VisualElement();
            header.AddToClassList("forge-row");
            var image = new Image { image = UnityEditor.EditorGUIUtility.IconContent(icon).image };
            image.AddToClassList("forge-stat__icon");
            header.Add(image);
            header.Add(ForgeUI.Label(title, "forge-stat__title"));
            tile.Add(header);
            var value = ForgeUI.Label("0", "forge-stat__value");
            tile.Add(value);
            parent.Add(tile);
            return value;
        }

        static VisualElement Card(VisualElement parent, string title, string extraClass = null)
        {
            var card = new VisualElement();
            card.AddToClassList("forge-card");
            if (extraClass != null)
                card.AddToClassList(extraClass);
            if (title != null)
                card.Add(ForgeUI.Label(title, "forge-chart__title"));
            parent.Add(card);
            return card;
        }

        /// <summary>Les 4 règles de cohérence du GDD, chacune vérifiée directement sur les données.</summary>
        void RefreshGddRules(ValidationContext context, List<ValidationIssue> issues)
        {
            gddRules.Clear();
            var missingEnemies = context.Rooms.Sum(r => r.Enemies.Count(e => e.enemy == null));
            var missingItems = context.LootTables.Sum(t => t.Entries.Count(e => e.item == null));
            GddRule("Ennemis et objets cités existent",
                missingEnemies + missingItems == 0 ? "Toutes les références sont valides." : $"{missingEnemies} ennemi(s) et {missingItems} objet(s) manquants.",
                missingEnemies + missingItems == 0, ForgeStudioWindow.Rooms);

            var floorIssues = issues.Where(i => i.RuleName == "Étages" && i.Severity == Severity.Error).ToList();
            var doorIssues = floorIssues.Count(i => i.Message.Contains("porte", StringComparison.OrdinalIgnoreCase));
            var accessIssues = floorIssues.Count(i => i.Message.Contains("atteignable") || i.Message.Contains("entrée"));
            GddRule("Portes compatibles, salles atteignables",
                context.Floors.Count == 0 ? "Aucun étage pour l'instant." :
                doorIssues + accessIssues == 0 ? "Toutes les salles sont reliées et accessibles." : $"{doorIssues} porte(s) incompatible(s), {accessIssues} problème(s) d'accès.",
                context.Floors.Count > 0 && doorIssues + accessIssues == 0, ForgeStudioWindow.Floors);

            var wrongBoss = context.Floors.Count(f => FloorGraph.BossCount(FloorGraph.Map(f.Rooms).Values) != 1);
            GddRule("Exactement un boss par étage",
                context.Floors.Count == 0 ? "Aucun étage pour l'instant." :
                wrongBoss == 0 ? $"Les {context.Floors.Count} étage(s) ont un boss." : $"{wrongBoss} étage(s) sans boss ou avec plusieurs.",
                context.Floors.Count > 0 && wrongBoss == 0, ForgeStudioWindow.Floors);

            var naming = issues.Count(i => i.RuleName == "Convention de nommage");
            GddRule("Convention de nommage",
                naming == 0 ? "Tous les assets respectent PRÉFIXE_Nom." : $"{naming} asset(s) mal nommé(s) (correction automatique disponible).",
                naming == 0, ForgeStudioWindow.Home);
        }

        void GddRule(string title, string detail, bool ok, string section)
        {
            var card = new VisualElement { tooltip = "Cliquez pour ouvrir l'outil concerné." };
            card.AddToClassList("forge-card");
            card.AddToClassList("forge-card--clickable");
            card.AddToClassList("forge-stat");
            card.AddToClassList(ok ? "forge-stat--good" : "forge-stat--critical");
            card.Add(ForgeUI.Label($"<b>{(ok ? "✔" : "✖")}  {title}</b>", "forge-stat__title"));
            card.Add(ForgeUI.Label(detail, "forge-hint"));
            card.RegisterCallback<ClickEvent>(_ => host.ShowSection(section));
            gddRules.Add(card);
        }

        public void Refresh()
        {
            if (errorsValue == null)
                return;
            var context = ValidationContext.FromProject();
            var issues = ValidationRunner.Run(context);
            var invalid = issues.Where(i => i.Severity == Severity.Error && i.Context != null).Select(i => i.Context).ToHashSet();
            var limits = context.Limits;

            int Errors<T>(IEnumerable<T> assets) where T : UnityEngine.Object => assets.Count(invalid.Contains);
            var enemies = context.Enemies.Where(e => !e.IsBoss).ToList();
            var bosses = context.Enemies.Where(e => e.IsBoss).ToList();
            rings["items"].SetValue(context.Items.Count, limits.targetItems, Errors(context.Items));
            rings["enemies"].SetValue(enemies.Count, limits.targetEnemies, Errors(enemies));
            rings["bosses"].SetValue(bosses.Count, limits.targetBosses, Errors(bosses));
            rings["loot"].SetValue(context.LootTables.Count, limits.targetLootTables, Errors(context.LootTables));
            rings["rooms"].SetValue(context.Rooms.Count, limits.targetRooms, Errors(context.Rooms));
            rings["floors"].SetValue(context.Floors.Count, limits.floorCount, Errors(context.Floors));
            RefreshGddRules(context, issues);
            var progression = context.Progressions.FirstOrDefault();
            rings["levels"].SetValue(progression != null ? progression.Levels.Count : 0, limits.maxLevel, Errors(context.Progressions));

            var errorCount = issues.Count(i => i.Severity == Severity.Error);
            var assetCount = context.AllAssets().Count();
            errorsValue.text = errorCount.ToString();
            warningsValue.text = (issues.Count - errorCount).ToString();
            validValue.text = assetCount == 0 ? "—" : $"{(assetCount - invalid.Count) / (float)assetCount:P0}";

            BarList.Fill(issuesByRule, issues
                    .GroupBy(i => i.RuleName)
                    .Select(g => (rule: g.Key, errors: g.Count(i => i.Severity == Severity.Error), total: g.Count()))
                    .OrderByDescending(g => g.errors).ThenByDescending(g => g.total)
                    .Select(g => new BarList.Bar(g.rule, g.total, g.errors > 0 ? $"{g.errors} err. · {g.total - g.errors} av." : $"{g.total} av.",
                        g.errors > 0 ? ChartColors.Critical : ChartColors.Warning)),
                "Aucun problème : bravo !");

            issuesList.Show(issues.Where(i => i.Context != null).Take(MaxListedIssues).ToList());
            var hidden = issues.Count(i => i.Context != null) - MaxListedIssues;
            issuesMore.text = hidden > 0 ? $"… et {hidden} autre(s)." : string.Empty;

            itemsByRarity.SetData(Enum.GetValues(typeof(Rarity)).Cast<Rarity>()
                .Select(r => new DonutChart.Segment(r.ToString(), context.Items.Count(i => i.Rarity == r), limits.GetRarity(r).color)), "objets");

            roomsByType.SetData(Enum.GetValues(typeof(RoomType)).Cast<RoomType>()
                .Select((t, index) => new DonutChart.Segment(t.ToString(), context.Rooms.Count(r => r.RoomType == t), ChartColors.Series(index))), "salles");

            BarList.Fill(roomsByBiome, context.Biomes
                    .Select(b => new BarList.Bar(b.DisplayName, context.Rooms.Count(r => r.Biome == b), context.Rooms.Count(r => r.Biome == b).ToString(),
                        b.Color, b.FloorSprites.FirstOrDefault(s => s != null)))
                    .OrderByDescending(bar => bar.Value),
                "Aucun biome pour l'instant.");

            if (progression != null && progression.Levels.Count > 0)
                playerHealth.SetData("Niveau", Enumerable.Range(1, progression.Levels.Count).Select(l => l.ToString()).ToArray(),
                    new[] { new LineChart.Series { Name = "PV max", Color = ChartColors.Series(0), Values = progression.Levels.Select(l => (float)l.maxHealth).ToArray() } });
            else
                playerHealth.SetData("Niveau", Array.Empty<string>(), Array.Empty<LineChart.Series>());
        }
    }
}
