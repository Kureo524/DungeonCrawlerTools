using System;
using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using static TheDungeon.Editor.Progression.ProgressionFields;

namespace TheDungeon.Editor.Progression
{
    /// <summary>
    /// Simulation des niveaux : le personnage à un niveau choisi (stats, actions, effort en ennemis), l'histogramme de l'effort,
    /// et la liste des niveaux, dépliables pour modifier leurs valeurs et leurs récompenses. Les calculs viennent de <see cref="ProgressionSimulation"/>.
    /// </summary>
    internal sealed class ProgressionSimulationView : ProgressionView
    {
        readonly ProgressionRewardsView rewards;
        readonly Dictionary<int, VisualElement> levelRows = new();
        Label simBig;
        SliderInt simSlider;
        VisualElement simBars;
        VisualElement simBlocks;

        public ProgressionSimulationView(ProgressionContext context) : base(context) => rewards = new ProgressionRewardsView(context);

        public override void Build(VisualElement parent, PlayerProgression draft)
        {
            var section = ForgeUI.Section("Simulation", parent,
                "Le personnage tel qu'il est à un niveau : ses stats, ses actions avec leurs chiffres réels (compétences comprises), et l'effort pour y arriver. " +
                "Les points d'attributs ne sont pas investis : c'est le joueur qui les répartit.");
            section.Add(BuildToolbar(draft));

            section.Add(ForgeUI.Label("Ennemis à vaincre pour atteindre chaque niveau depuis le précédent. Cliquez sur une colonne pour simuler ce niveau.", "forge-hint"));
            simBars = new VisualElement();
            simBars.AddToClassList("forge-bars");
            section.Add(simBars);
            simBlocks = new VisualElement();
            simBlocks.AddToClassList("forge-sim-blocks");
            section.Add(simBlocks);
            AddRefresher(RefreshSimulation);

            var levels = ForgeUI.Section("Niveau par niveau", parent,
                "Les pastilles montrent ce qui change : une couleur par stat modifiée (cerclée de rouge en cas de problème), puis les récompenses. " +
                "Dépliez un niveau pour modifier ses valeurs et ses récompenses.");
            levels.Add(ProgressionRewardsView.Legend(true));
            levelRows.Clear();
            for (var l = 1; l <= draft.MaxLevel; l++)
                levels.Add(LevelRow(draft, l));
        }

        /// <summary>Niveau simulé (flèches, grand chiffre, curseur) et XP moyenne rapportée par un ennemi.</summary>
        VisualElement BuildToolbar(PlayerProgression draft)
        {
            var top = new VisualElement();
            top.AddToClassList("forge-sim-top");
            var levelBox = new VisualElement();
            levelBox.AddToClassList("forge-sim-level");
            simBig = ForgeUI.Label(State.SimLevel.ToString(), "forge-big");
            levelBox.Add(ForgeUI.Button("‹", "Niveau précédent.", () => SetSimLevel(State.SimLevel - 1)));
            levelBox.Add(simBig);
            levelBox.Add(ForgeUI.Button("›", "Niveau suivant.", () => SetSimLevel(State.SimLevel + 1)));
            top.Add(levelBox);
            simSlider = new SliderInt(1, Mathf.Max(2, draft.MaxLevel)) { value = State.SimLevel, tooltip = "Niveau simulé." };
            simSlider.RegisterValueChangedCallback(e => SetSimLevel(e.newValue));
            top.Add(simSlider);
            var xp = new FloatField("XP moyenne par ennemi")
            {
                isDelayed = true,
                value = State.XpPerEnemy,
                tooltip = "Sert à estimer le nombre d'ennemis à vaincre. Par défaut : moyenne des ennemis ordinaires du projet au premier étage."
            };
            xp.RegisterValueChangedCallback(e =>
            {
                State.XpPerEnemy = Mathf.Max(1f, e.newValue);
                RefreshSimulation();
            });
            top.Add(xp);
            top.Add(ForgeUI.Button("Moyenne des ennemis", "Reprend l'XP moyenne des ennemis ordinaires du projet (premier étage).", () =>
            {
                State.XpPerEnemy = ProgressionSimulation.AverageEnemyXp();
                xp.SetValueWithoutNotify(State.XpPerEnemy);
                RefreshSimulation();
            }));
            return top;
        }

        void SetSimLevel(int level)
        {
            State.SimLevel = Mathf.Clamp(level, 1, Draft.MaxLevel);
            simSlider?.SetValueWithoutNotify(State.SimLevel);
            RefreshSimulation();
        }

        void RefreshSimulation()
        {
            if (simBlocks == null || simBlocks.panel == null)
                return;
            var p = Draft;
            var sim = ProgressionSimulation.At(p, State.SimLevel, State.XpPerEnemy);
            var l = sim.Level;
            simBig.text = l.ToString();
            foreach (var (level, row) in levelRows)
                row.EnableInClassList("forge-acc--current", level == l);

            FillBars(p, l);
            simBlocks.Clear();
            simBlocks.Add(StatsBlock(p, l));
            simBlocks.Add(ActionsBlock(sim));
            simBlocks.Add(EffortBlock(p, sim));
            RefreshLevelRows(p);
        }

        /// <summary>Histogramme : une colonne par niveau, les ennemis à vaincre pour l'atteindre depuis le niveau précédent.</summary>
        void FillBars(PlayerProgression p, int selected)
        {
            simBars.Clear();
            var counts = ProgressionSimulation.EnemiesPerLevel(p, State.XpPerEnemy);
            if (counts == null)
            {
                simBars.Add(ForgeUI.Label("Ajoutez une stat avec le rôle « XP pour le niveau suivant ».", "forge-hint"));
                return;
            }
            var xp = p.Find(StatRole.XpToNext);
            var top = Mathf.Max(1, counts.Max());
            var labelEvery = Mathf.Max(1, Mathf.CeilToInt(counts.Count / 20f));
            for (var k = 0; k < counts.Count; k++)
            {
                var level = k + 1;
                var bar = new VisualElement
                {
                    tooltip = level == 1
                        ? "Niveau 1 : point de départ."
                        : $"Niveau {level - 1} → {level} : {counts[k]} ennemis ({Mathf.RoundToInt(xp.GetValue(level - 1))} XP)"
                };
                bar.AddToClassList("forge-bar");
                bar.EnableInClassList("forge-bar--selected", level == selected);
                var fill = new VisualElement { pickingMode = PickingMode.Ignore };
                fill.AddToClassList("forge-bar__fill");
                fill.style.height = level == 1 ? 3f : Length.Percent(Mathf.Max(3f, 85f * counts[k] / top));
                bar.Add(fill);
                var label = ForgeUI.Label(level % labelEvery == 0 || level == 1 || level == p.MaxLevel ? level.ToString() : string.Empty, "forge-bar__label");
                label.pickingMode = PickingMode.Ignore;
                bar.Add(label);
                bar.RegisterCallback<ClickEvent>(_ => SetSimLevel(level));
                simBars.Add(bar);
            }
        }

        /// <summary>Stats au niveau simulé : valeur, écart avec le niveau précédent, position entre min et max.</summary>
        static VisualElement StatsBlock(PlayerProgression p, int l)
        {
            var stats = SimBlock($"Informations du personnage · niveau {l}");
            foreach (var stat in p.Stats)
            {
                var value = stat.GetValue(l);
                var item = new VisualElement();
                item.AddToClassList("forge-sim-stat");
                var line = new VisualElement();
                line.AddToClassList("forge-sim-stat__row");
                line.Add(Dot(stat.color));
                line.Add(new Label(stat.displayName));
                line.Add(ForgeUI.Label(stat.Format(value), "forge-sim-stat__value"));
                var deltaLabel = ForgeUI.Label(string.Empty, "forge-delta");
                ShowDelta(deltaLabel, stat, l);
                line.Add(deltaLabel);
                item.Add(line);
                var meter = new VisualElement();
                meter.AddToClassList("forge-mini-meter");
                var fill = new VisualElement();
                fill.AddToClassList("forge-mini-meter__fill");
                fill.style.width = Length.Percent(100f * Mathf.Clamp01(Mathf.InverseLerp(stat.min, stat.max, value)));
                fill.style.backgroundColor = stat.color;
                meter.Add(fill);
                item.Add(meter);
                stats.Add(item);
            }
            return stats;
        }

        /// <summary>Écart avec le niveau précédent, coloré selon son sens (« départ » au niveau 1).</summary>
        static void ShowDelta(Label label, ProgressionStat stat, int level)
        {
            var delta = ProgressionSimulation.Delta(stat, level);
            label.text = level > 1 ? ProgressionSimulation.Signed(stat, delta) : "départ";
            label.EnableInClassList("forge-delta--up", delta > 0f);
            label.EnableInClassList("forge-delta--down", delta < 0f);
        }

        /// <summary>Actions : chiffres réels au niveau simulé, ou niveau de déblocage.</summary>
        static VisualElement ActionsBlock(ProgressionSimulation sim)
        {
            var actions = SimBlock("Actions disponibles");
            foreach (var action in sim.Actions)
            {
                var item = new VisualElement();
                item.AddToClassList("forge-sim-action");
                item.EnableInClassList("forge-sim-action--locked", !action.Available);
                item.Add(Dot(action.Mechanic.color));
                var text = new VisualElement();
                text.style.flexShrink = 1;
                var title = new VisualElement();
                title.AddToClassList("forge-row");
                title.Add(ForgeUI.Label($"<b>{action.Mechanic.displayName}</b>" + (action.Rank > 1 ? $"  rang {action.Rank}" : string.Empty)));
                title.Add(ForgeUI.Label(ProgressionEditing.InputLabel(action.Mechanic.input), "forge-key"));
                text.Add(title);
                var detail = action.Available
                    ? string.Join(" · ", new[] { action.Summary }.Concat(action.Details))
                    : action.UnlockLevel > 0 ? $"Débloquée au niveau {action.UnlockLevel}" : "Jamais débloquée";
                var detailLabel = ForgeUI.Label(detail, "forge-hint");
                detailLabel.style.whiteSpace = WhiteSpace.Normal;
                text.Add(detailLabel);
                item.Add(text);
                actions.Add(item);
            }
            if (sim.Actions.Count == 0)
                actions.Add(ForgeUI.Label("Aucune mécanique : tir et esquive de base.", "forge-hint"));
            return actions;
        }

        /// <summary>Effort et récompenses : XP cumulée, ennemis vaincus, points gagnés, compétences et nouveautés du niveau.</summary>
        static VisualElement EffortBlock(PlayerProgression p, ProgressionSimulation sim)
        {
            var l = sim.Level;
            var effort = SimBlock("Évaluation");
            var facts = new VisualElement();
            facts.AddToClassList("forge-facts");
            void Fact(string value, string caption)
            {
                var fact = new VisualElement();
                fact.AddToClassList("forge-fact");
                fact.Add(ForgeUI.Label(value, "forge-fact__value"));
                fact.Add(ForgeUI.Label(caption, "forge-fact__label"));
                facts.Add(fact);
            }
            Fact(sim.HasXp ? sim.XpCumulative.ToString() : "■■", $"XP cumulée pour atteindre le niveau {l}");
            Fact(sim.HasXp ? sim.EnemiesTotal.ToString() : "■■", "ennemis vaincus au total");
            Fact(l < p.MaxLevel && sim.HasXp ? sim.EnemiesToNext.ToString() : "—", l < p.MaxLevel ? $"ennemis pour passer au niveau {l + 1}" : "niveau maximum atteint");
            Fact(sim.AttributePoints.ToString(), "point(s) d'attributs gagnés");
            effort.Add(facts);
            var skills = sim.Skills.Where(s => s.rank > 0).Select(s => $"{s.skill.displayName} rang {s.rank}").ToList();
            if (skills.Count > 0)
                effort.Add(ForgeUI.Label("Compétences : " + string.Join(", ", skills), "forge-hint"));
            var here = ProgressionEditing.RewardSummary(p, l);
            var news = ForgeUI.Label(string.IsNullOrEmpty(here) ? $"[Rien de nouveau au niveau {l}.]" : $"[Niveau {l} : {here}.]", "forge-mono");
            news.style.whiteSpace = WhiteSpace.Normal;
            effort.Add(news);
            return effort;
        }

        /// <summary>Lignes « niveau par niveau » : pastilles, résumés et écarts des niveaux dépliés.</summary>
        void RefreshLevelRows(PlayerProgression p)
        {
            foreach (var (level, row) in levelRows)
            {
                var pills = row.Q(className: "forge-pills");
                pills.Clear();
                FillLevelPills(pills, p, level);
                row.Q<Label>(className: "forge-acc__meta").text = ProgressionSimulation.Summary(p, level);
                row.Query<Label>(className: "forge-delta").ForEach(label =>
                {
                    if (label.userData is not ValueTuple<int, int> key || key.Item1 >= p.Stats.Count)
                        return;
                    var (s, lv) = key;
                    ShowDelta(label, p.Stats[s], lv);
                });
            }
        }

        static VisualElement SimBlock(string title)
        {
            var block = new VisualElement();
            block.AddToClassList("forge-sim-block");
            var label = ForgeUI.Label(ForgeText.Window(title), "forge-system-title");
            label.style.marginBottom = 8;
            block.Add(label);
            return block;
        }

        static void FillLevelPills(VisualElement pills, PlayerProgression p, int level)
        {
            var problems = ProgressionSimulation.Problems(p, level).ToHashSet();
            var shown = ProgressionSimulation.Changed(p, level).Union(problems).ToList();
            foreach (var stat in shown)
                pills.Add(ProgressionRewardsView.Pill(problems.Contains(stat) ? "forge-pill--bad" : null,
                    level > 1 ? $"{stat.displayName} {ProgressionSimulation.Signed(stat, stat.GetValue(level) - stat.GetValue(level - 1))}" : stat.displayName, stat.color));
            var levelRewards = p.Rewards.Where(r => r.level == level).ToList();
            if (shown.Count > 0 && levelRewards.Count > 0)
            {
                var separator = new VisualElement();
                separator.AddToClassList("forge-pills__sep");
                pills.Add(separator);
            }
            foreach (var reward in levelRewards)
                pills.Add(ProgressionRewardsView.RewardPill(p, reward));
        }

        VisualElement LevelRow(PlayerProgression draft, int level)
        {
            var open = level == State.OpenLevel;
            var acc = new VisualElement();
            acc.AddToClassList("forge-acc");
            acc.EnableInClassList("forge-acc--open", open);
            var head = new VisualElement { tooltip = open ? "Replier ce niveau." : "Modifier les valeurs et les récompenses de ce niveau." };
            head.AddToClassList("forge-acc__head");
            head.RegisterCallback<ClickEvent>(_ =>
            {
                State.OpenLevel = open ? -1 : level;
                State.SimLevel = level;
                Rebuild();
            });
            head.Add(ForgeUI.Label(level.ToString(), "forge-lvl__number"));
            var pills = new VisualElement { pickingMode = PickingMode.Ignore };
            pills.AddToClassList("forge-pills");
            head.Add(pills);
            head.Add(ForgeUI.Label(string.Empty, "forge-acc__meta"));
            head.Add(ForgeUI.Label(open ? "▼" : "›", "forge-acc__chevron"));
            acc.Add(head);
            if (open)
                acc.Add(LevelBody(draft, level));
            levelRows[level] = acc;
            return acc;
        }

        /// <summary>Niveau déplié : valeur de chaque stat (modifiable) et récompenses du niveau.</summary>
        VisualElement LevelBody(PlayerProgression draft, int level)
        {
            var body = new VisualElement();
            body.AddToClassList("forge-acc__body");
            var grid = new VisualElement();
            grid.AddToClassList("forge-lvl-grid");
            var stats = Serialized.FindProperty("stats");
            for (var s = 0; s < draft.Stats.Count; s++)
            {
                var stat = draft.Stats[s];
                var values = stats.GetArrayElementAtIndex(s).FindPropertyRelative("values");
                if (level > values.arraySize)
                    continue;
                var cell = new VisualElement();
                cell.AddToClassList("forge-lvl-field");
                var label = new VisualElement();
                label.AddToClassList("forge-row");
                label.style.alignItems = Align.Center;
                label.Add(Dot(stat.color));
                label.Add(new Label(stat.displayName));
                cell.Add(label);
                var line = new VisualElement();
                line.AddToClassList("forge-lvl-field__row");
                line.Add(ValueField(values.GetArrayElementAtIndex(level - 1), s));
                var delta = ForgeUI.Label(string.Empty, "forge-delta");
                delta.userData = (s, level);
                line.Add(delta);
                cell.Add(line);
                cell.Add(ForgeUI.Label($"entre {stat.Format(stat.min)} et {stat.Format(stat.max)}", "forge-hint"));
                grid.Add(cell);
            }
            body.Add(grid);
            var levelRewards = new VisualElement();
            levelRewards.AddToClassList("forge-lvl-rewards");
            levelRewards.Add(ForgeUI.Label($"Récompenses du niveau {level}", "forge-hint"));
            rewards.AddLevelRewards(levelRewards, draft, level);
            body.Add(levelRewards);
            return body;
        }

        /// <summary>Valeur d'une stat à un niveau : toute saisie hors des bornes de la stat est ramenée dans les bornes.</summary>
        FloatField ValueField(SerializedProperty property, int statIndex)
        {
            var field = new FloatField();
            field.BindProperty(property);
            field.RegisterValueChangedCallback(evt =>
            {
                var stat = Draft.Stats[statIndex];
                if (evt.newValue >= stat.min && evt.newValue <= stat.max)
                    return;
                field.value = Mathf.Clamp(evt.newValue, stat.min, stat.max);
                ForgeLists.Notify($"Valeur ramenée entre {stat.min} et {stat.max}.");
            });
            return field;
        }
    }
}
