using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using TheDungeon.Editor.Common.Charts;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.ContentDatabase
{
    public sealed class EnemiesTab : AssetListTab<EnemyData>
    {
        static readonly Color BossColor = new(0.85f, 0.25f, 0.25f);
        static readonly Color EnemyColor = new(0.55f, 0.55f, 0.6f);

        const string AllEnemies = "Ennemis et boss";
        const string OnlyEnemies = "Ennemis uniquement";
        const string OnlyBosses = "Boss uniquement";

        DropdownField kindFilter;
        LineChart healthChart;
        LineChart damageChart;
        LineChart xpChart;
        EnemyBehaviourView behaviourView;

        /// <summary>Vue affichée dans la fiche (« Fiche » ou « Comportement »), gardée d'un ennemi à l'autre.</summary>
        static bool showBehaviour;

        public EnemiesTab(IToolHost window) : base(window)
        {
        }

        protected override string NewAssetName => "Nouvel ennemi";
        protected override string Noun => "ennemi";

        protected override Sprite GetPreview(EnemyData enemy) => enemy.Sprite;
        protected override Color GetColor(EnemyData enemy) => enemy.IsBoss ? BossColor : EnemyColor;

        protected override IEnumerable<VisualElement> GetBadges(EnemyData enemy)
        {
            if (enemy.IsBoss)
                yield return ForgeUI.Badge("BOSS", BossColor, "Ennemi de fin d'étage.");
            yield return ForgeUI.Badge(EnemyPatternText.Label(enemy.Behaviour), new Color(0.7f, 0.7f, 0.7f), "Comportement de base (« Sinon »).");
            if (enemy.Patterns.Count > 0)
                yield return ForgeUI.Badge($"{enemy.Patterns.Count} règle(s)", new Color(0.65f, 0.59f, 0.75f), "Règles de comportement (vue « Comportement »).");
            yield return ForgeUI.Badge(enemy.LootTable != null ? enemy.LootTable.name : "Sans butin",
                enemy.LootTable != null ? new Color(0.95f, 0.8f, 0.3f) : new Color(0.6f, 0.6f, 0.6f), "Table de butin.");
        }

        protected override VisualElement BuildFilters()
        {
            kindFilter = new DropdownField(new List<string> { AllEnemies, OnlyEnemies, OnlyBosses }, 0) { tooltip = "Filtre ennemis / boss." };
            kindFilter.RegisterValueChangedCallback(_ => ApplyFilter());
            return kindFilter;
        }

        protected override bool PassesFilters(EnemyData enemy) =>
            kindFilter.value == AllEnemies || (kindFilter.value == OnlyBosses) == enemy.IsBoss;

        protected override void BuildDetails(VisualElement root, SerializedObject serializedObject, EnemyData enemy)
        {
            // Deux vues : la fiche (identité, stats, butin) et le comportement (règles et arène de test).
            var switcher = new VisualElement();
            switcher.AddToClassList("forge-view-switch");
            switcher.style.flexShrink = 0;
            root.Add(switcher);
            var container = new VisualElement();
            root.Add(container);
            behaviourView = new EnemyBehaviourView(serializedObject, () => enemy, Window.Reveal);
            root.Add(behaviourView);
            Button sheetButton = null;
            Button behaviourButton = null;
            void ShowView(bool behaviour)
            {
                showBehaviour = behaviour;
                container.style.display = behaviour ? DisplayStyle.None : DisplayStyle.Flex;
                behaviourView.style.display = behaviour ? DisplayStyle.Flex : DisplayStyle.None;
                sheetButton.EnableInClassList("forge-view-switch__button--active", !behaviour);
                behaviourButton.EnableInClassList("forge-view-switch__button--active", behaviour);
                if (behaviour)
                    behaviourView.Refresh();
            }
            sheetButton = ForgeUI.Button("Fiche", "Identité, statistiques, évolution par étage et butin.", () => ShowView(false), "forge-view-switch__button");
            behaviourButton = ForgeUI.Button("Comportement", "Règles « Quand… alors… » et arène pour les tester.", () => ShowView(true), "forge-view-switch__button");
            switcher.Add(sheetButton);
            switcher.Add(behaviourButton);
            ShowView(showBehaviour);

            var identity = ForgeUI.Section("Identité", container);
            identity.Add(ForgeUI.Text("Nom", "displayName", "Nom affiché au joueur. Le fichier sera nommé ENM_<Nom> (ou BOSS_<Nom>) automatiquement."));
            identity.Add(new SpriteField("Visuel", serializedObject.FindProperty("sprite"), "Enemies", "Visuel de l'ennemi. Cliquez pour ouvrir la galerie."));
            identity.Add(ForgeUI.Toggle("Boss", "isBoss", "Un boss garde la salle de boss d'un étage. Chaque étage doit en avoir exactement un."));

            var stats = ForgeUI.Section("Statistiques à l'étage 1", container);
            stats.Add(ForgeUI.Slider("Points de vie", "baseHealth", Limits.enemyHealth, "PV à l'étage 1."));
            stats.Add(ForgeUI.Slider("Dégâts", "baseDamage", Limits.enemyDamage, "Dégâts par coup à l'étage 1."));
            stats.Add(ForgeUI.Slider("Défense", "baseDefense", Limits.enemyDefense, "Réduction des dégâts reçus."));
            stats.Add(ForgeUI.Slider("Vitesse", "moveSpeed", Limits.enemySpeed, "Cases parcourues par seconde."));
            stats.Add(ForgeUI.Slider("XP donnée", "xpReward", Limits.enemyXp, "XP gagnée par le joueur à l'étage 1."));

            var scaling = ForgeUI.Section("Évolution par étage", container);
            scaling.Add(ForgeUI.Slider("Croissance", "floorScaling", Limits.enemyFloorScaling, "0.25 = +25 % de stats à chaque étage."));
            scaling.Add(ForgeUI.Label("Comparé à la moyenne des autres ennemis du même rang (ennemis ou boss).", "forge-hint"));
            var row = new VisualElement();
            row.AddToClassList("forge-small-multiples");
            healthChart = new LineChart("PV par étage", 120f);
            damageChart = new LineChart("Dégâts par étage", 120f);
            xpChart = new LineChart("XP donnée par étage", 120f);
            row.Add(healthChart);
            row.Add(damageChart);
            row.Add(xpChart);
            scaling.Add(row);

            var loot = ForgeUI.Section("Butin", container);
            loot.Add(new AssetPicker<LootTable>("Table de butin", serializedObject.FindProperty("lootTable"), "Table tirée à la mort de l'ennemi."));
        }

        protected override void OnRefresh(EnemyData enemy)
        {
            behaviourView.Refresh();
            var floors = Enumerable.Range(1, Limits.floorCount).ToArray();
            var labels = floors.Select(f => f.ToString()).ToArray();
            var peers = AssetFactory.LoadAll<EnemyData>().Where(e => e.IsBoss == enemy.IsBoss && e.name != enemy.name).ToList();
            Chart(healthChart, labels, floors, enemy, peers, (e, f) => e.GetHealth(f), "PV");
            Chart(damageChart, labels, floors, enemy, peers, (e, f) => e.GetDamage(f), "Dégâts");
            Chart(xpChart, labels, floors, enemy, peers, (e, f) => e.GetXpReward(f), "XP");
        }

        static void Chart(LineChart chart, string[] labels, int[] floors, EnemyData enemy, List<EnemyData> peers,
            System.Func<EnemyData, int, int> stat, string name)
        {
            var series = new List<LineChart.Series>
            {
                new() { Name = string.IsNullOrWhiteSpace(enemy.DisplayName) ? name : enemy.DisplayName, Color = ChartColors.Series(0),
                    Values = floors.Select(f => (float)stat(enemy, f)).ToArray() }
            };
            if (peers.Count > 0)
                series.Add(new LineChart.Series { Name = "Moyenne", Color = ChartColors.Neutral, Secondary = true,
                    Values = floors.Select(f => (float)peers.Average(p => stat(p, f))).ToArray() });
            chart.SetData("Étage", labels, series);
        }
    }
}
