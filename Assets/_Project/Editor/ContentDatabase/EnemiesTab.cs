using System.Collections.Generic;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using UnityEditor;
using UnityEditor.UIElements;
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
        VisualElement floorPreview;

        protected override string NewAssetName => "Nouvel ennemi";
        protected override string Noun => "ennemi";

        protected override string GetLabel(EnemyData enemy) => string.IsNullOrWhiteSpace(enemy.DisplayName) ? enemy.name : enemy.DisplayName;
        protected override Sprite GetPreview(EnemyData enemy) => enemy.Sprite;
        protected override Color GetColor(EnemyData enemy) => enemy.IsBoss ? BossColor : EnemyColor;

        protected override IEnumerable<VisualElement> GetBadges(EnemyData enemy)
        {
            if (enemy.IsBoss)
                yield return ForgeUI.Badge("BOSS", BossColor, "Ennemi de fin d'étage.");
            yield return ForgeUI.Badge(enemy.Behaviour.ToString(), new Color(0.7f, 0.7f, 0.7f), "Comportement de combat.");
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

        protected override void BuildDetails(VisualElement container, SerializedObject serializedObject, EnemyData enemy)
        {
            var identity = ForgeUI.Section("Identité", container);
            var nameField = ForgeUI.Text("Nom", "displayName", "Nom affiché au joueur. Le fichier est renommé automatiquement (ENM_… ou BOSS_…).");
            nameField.isDelayed = true;
            nameField.RegisterValueChangedCallback(_ => ScheduleRename(enemy));
            identity.Add(nameField);
            identity.Add(ForgeUI.Object<Sprite>("Visuel", "sprite", "Glissez un sprite depuis la fenêtre Project."));
            var bossToggle = ForgeUI.Toggle("Boss", "isBoss", "Un boss garde la fin d'un étage. Chaque étage doit en avoir exactement un.");
            bossToggle.RegisterValueChangedCallback(_ => ScheduleRename(enemy));
            identity.Add(bossToggle);
            identity.Add(ForgeUI.Enum<EnemyBehaviour>("Comportement", "behaviour", "Façon dont l'ennemi combat."));

            var stats = ForgeUI.Section("Statistiques à l'étage 1", container);
            stats.Add(ForgeUI.Slider("Points de vie", "baseHealth", Limits.enemyHealth, "PV à l'étage 1."));
            stats.Add(ForgeUI.Slider("Dégâts", "baseDamage", Limits.enemyDamage, "Dégâts par coup à l'étage 1."));
            stats.Add(ForgeUI.Slider("Défense", "baseDefense", Limits.enemyDefense, "Réduction des dégâts reçus."));
            stats.Add(ForgeUI.Slider("Vitesse", "moveSpeed", Limits.enemySpeed, "Cases parcourues par seconde."));
            stats.Add(ForgeUI.Slider("XP donnée", "xpReward", Limits.enemyXp, "XP gagnée par le joueur à l'étage 1."));

            var scaling = ForgeUI.Section("Évolution par étage", container);
            scaling.Add(ForgeUI.Slider("Croissance", "floorScaling", Limits.enemyFloorScaling, "0.25 = +25 % de stats à chaque étage."));
            floorPreview = new VisualElement();
            scaling.Add(floorPreview);

            var loot = ForgeUI.Section("Butin", container);
            loot.Add(ForgeUI.Object<LootTable>("Table de butin", "lootTable", "Table tirée à la mort de l'ennemi."));
        }

        protected override void OnRefresh(EnemyData enemy)
        {
            floorPreview.Clear();
            floorPreview.Add(TableRow("Étage", "PV", "Dégâts", "Défense", "XP", true));
            for (var floor = 1; floor <= Limits.floorCount; floor++)
                floorPreview.Add(TableRow(floor.ToString(), enemy.GetHealth(floor).ToString(), enemy.GetDamage(floor).ToString(),
                    enemy.GetDefense(floor).ToString(), enemy.GetXpReward(floor).ToString(), false));
        }

        static VisualElement TableRow(string a, string b, string c, string d, string e, bool header)
        {
            var row = new VisualElement();
            row.AddToClassList("forge-table-row");
            foreach (var text in new[] { a, b, c, d, e })
                row.Add(ForgeUI.Label(text, header ? "forge-table-header" : null));
            row.tooltip = header ? "Stats calculées automatiquement à partir de l'étage 1 et de la croissance." : null;
            return row;
        }
    }
}
