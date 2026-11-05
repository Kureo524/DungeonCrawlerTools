using System;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using TheDungeon.Editor.Common.Charts;
using UnityEngine;
using UnityEngine.UIElements;
using static TheDungeon.Editor.Progression.ProgressionFields;

namespace TheDungeon.Editor.Progression
{
    /// <summary>Aperçu : une tuile par catégorie (identité, courbes, mécaniques, simulation, attributs, récompenses), chacune menant à ses détails.</summary>
    internal sealed class ProgressionOverviewView : ProgressionView
    {
        readonly Func<PlayerProgression> original;
        readonly Action activate;
        readonly Action exportSvg;

        /// <param name="original">Progression enregistrée (null tant que le brouillon n'a pas été validé).</param>
        /// <param name="activate">Fait jouer la progression enregistrée par le jeu.</param>
        /// <param name="exportSvg">Exporte la progression en SVG.</param>
        public ProgressionOverviewView(ProgressionContext context, Func<PlayerProgression> original, Action activate, Action exportSvg) : base(context)
        {
            this.original = original;
            this.activate = activate;
            this.exportSvg = exportSvg;
        }

        public override void Build(VisualElement parent, PlayerProgression draft)
        {
            var tiles = new VisualElement();
            tiles.AddToClassList("forge-tiles");
            parent.Add(tiles);
            tiles.Add(IdentityTile(draft));
            tiles.Add(StatsTile(draft));
            tiles.Add(MechanicsTile(draft));
            tiles.Add(SimulationTile());
            tiles.Add(GrowthTile());

            // Récompenses : la frise des niveaux.
            var rewards = Tile("Récompenses", ProgressionCategory.Rewards, "forge-tile--full");
            rewards.Add(new ProgressionRewardsView(Context).Timeline(draft, true));
            rewards.Add(ProgressionRewardsView.Legend());
            tiles.Add(rewards);
        }

        VisualElement Tile(string title, ProgressionCategory? details, string modifier = null)
        {
            var tile = new VisualElement();
            tile.AddToClassList("forge-section");
            tile.AddToClassList("forge-tile");
            if (modifier != null)
                tile.AddToClassList(modifier);
            var header = new VisualElement();
            header.AddToClassList("forge-tile__header");
            header.Add(ForgeUI.Label(ForgeText.WindowRich(title), "forge-tile__title"));
            if (details.HasValue)
            {
                var target = details.Value;
                header.Add(ForgeUI.Button("Détails", $"Ouvre « {ProgressionCategories.Label(target)} ».", () => ShowView(target)));
            }
            tile.Add(header);
            return tile;
        }

        /// <summary>Progression : nom, nombre de niveaux, progression jouée, export.</summary>
        VisualElement IdentityTile(PlayerProgression draft)
        {
            var identity = Tile("Progression", null);
            identity.Add(ForgeUI.Text("Nom", "displayName", "Nom de la progression (ex : « Standard », « Difficile »). Le fichier sera nommé PRG_<Nom>."));
            var levels = new IntegerField("Nombre de niveaux")
            {
                isDelayed = true,
                value = draft.MaxLevel,
                tooltip = $"Niveau maximum du joueur (2 à {Limits.maxLevelCap}). Ajouter des niveaux prolonge les courbes ; en retirer coupe les courbes et supprime les récompenses des niveaux retirés."
            };
            levels.RegisterValueChangedCallback(e => ChangeLevelCount(levels, e.newValue));
            identity.Add(levels);
            var actions = new VisualElement();
            actions.AddToClassList("forge-row");
            var saved = original();
            var activateButton = ForgeUI.Button("Utiliser dans le jeu", "Le jeu suivra cette progression (une seule progression est jouée à la fois).", activate,
                "forge-button--primary");
            activateButton.SetEnabled(saved != null && !ProgressionTab.IsActive(saved));
            if (saved == null)
                activateButton.tooltip = "Validez d'abord la progression pour pouvoir la faire jouer.";
            else if (ProgressionTab.IsActive(saved))
                activateButton.text = "Utilisée par le jeu";
            actions.Add(activateButton);
            actions.Add(ForgeUI.Button("Exporter en SVG",
                "Enregistre les courbes dans une image SVG : lisible partout, retouchable dans un logiciel de dessin et réimportable.", exportSvg));
            identity.Add(actions);
            return identity;
        }

        /// <summary>Stats et courbes : une mini-courbe par stat.</summary>
        VisualElement StatsTile(PlayerProgression draft)
        {
            var stats = Tile("Stats et courbes", ProgressionCategory.Stats, "forge-tile--wide");
            var sparks = new VisualElement();
            sparks.AddToClassList("forge-sparks");
            for (var i = 0; i < draft.Stats.Count; i++)
                sparks.Add(SparkCard(draft, i));
            stats.Add(sparks);
            return stats;
        }

        VisualElement SparkCard(PlayerProgression draft, int index)
        {
            var card = new VisualElement { tooltip = "Modifier cette courbe." };
            card.AddToClassList("forge-spark-card");
            var head = new VisualElement();
            head.AddToClassList("forge-mini-row");
            head.style.paddingLeft = 0;
            var dot = Dot(draft.Stats[index].color);
            head.Add(dot);
            var name = ForgeUI.Label(draft.Stats[index].displayName);
            head.Add(name);
            card.Add(head);
            var spark = new Sparkline();
            card.Add(spark);
            var range = ForgeUI.Label(string.Empty, "forge-hint");
            range.AddToClassList("forge-mono");
            card.Add(range);
            card.RegisterCallback<ClickEvent>(_ =>
            {
                State.SelectedStat = index;
                ShowView(ProgressionCategory.Stats);
            });
            AddRefresher(() =>
            {
                if (index >= Draft.Stats.Count)
                    return;
                var stat = Draft.Stats[index];
                dot.style.backgroundColor = stat.color;
                name.text = stat.displayName;
                spark.SetData(stat.values, stat.color);
                range.text = stat.values.Count > 0 ? $"{stat.Format(stat.values[0])} → {stat.Format(stat.values[^1])}" : "—";
            });
            return card;
        }

        /// <summary>Mécaniques : touche et nom.</summary>
        VisualElement MechanicsTile(PlayerProgression draft)
        {
            var mechanics = Tile("Mécaniques", ProgressionCategory.Mechanics);
            for (var i = 0; i < draft.Mechanics.Count; i++)
            {
                var index = i;
                var mechanic = draft.Mechanics[i];
                var row = MiniRow(mechanic.color, mechanic.displayName, ProgressionLabels.InputLabel(mechanic.input));
                row.RegisterCallback<ClickEvent>(_ =>
                {
                    State.OpenMechanic = index;
                    ShowView(ProgressionCategory.Mechanics);
                });
                mechanics.Add(row);
            }
            if (draft.Mechanics.Count == 0)
                mechanics.Add(ForgeUI.Label("Aucune mécanique : le joueur garde le tir et l'esquive de base.", "forge-hint"));
            return mechanics;
        }

        /// <summary>Simulation : le personnage au dernier niveau.</summary>
        VisualElement SimulationTile()
        {
            var simulation = Tile("Simulation", ProgressionCategory.Simulation);
            var simBody = new VisualElement();
            simulation.Add(simBody);
            AddRefresher(() =>
            {
                simBody.Clear();
                var p = Draft;
                simBody.Add(ForgeUI.Label(ForgeText.Window($"Personnage au niveau {p.MaxLevel}"), "forge-system-title"));
                foreach (var stat in p.Stats.Where(s => s.visibility != StatVisibility.Hidden && s.role != StatRole.XpToNext).Take(4))
                    simBody.Add(MiniRow(stat.color, stat.displayName, stat.Format(stat.GetValue(p.MaxLevel)), false));
                var sim = ProgressionSimulation.At(p, p.MaxLevel, State.XpPerEnemy);
                simBody.Add(ForgeUI.Label(sim.HasXp
                    ? $"Environ <b>{sim.EnemiesTotal}</b> ennemis à vaincre pour l'atteindre ({State.XpPerEnemy:0.#} XP par ennemi)."
                    : "Aucune stat d'XP : l'effort n'est pas calculable.", "forge-hint"));
            });
            return simulation;
        }

        /// <summary>Attributs et compétences : résumé.</summary>
        VisualElement GrowthTile()
        {
            var growth = Tile("Attributs et compétences", ProgressionCategory.Growth);
            var growthBody = new VisualElement();
            growth.Add(growthBody);
            AddRefresher(() =>
            {
                var p = Draft;
                growthBody.Clear();
                growthBody.Add(MiniRow(ChartColors.Good, "Attributs", p.Attributes.Count > 0 ? string.Join(", ", p.Attributes.Select(a => a.displayName)) : "aucun", false));
                growthBody.Add(MiniRow(ChartColors.Good, "Points d'attributs",
                    $"{ProgressionEditing.TotalAttributePoints(p)} gagnés / {ProgressionEditing.AttributeCapacity(p)} dépensables", false));
                growthBody.Add(MiniRow(new Color(0.54f, 0.66f, 0.88f), "Compétences", p.Skills.Count > 0 ? string.Join(", ", p.Skills.Select(s => s.displayName)) : "aucune", false));
            });
            return growth;
        }

        static VisualElement MiniRow(Color color, string text, string tag, bool keyStyle = true)
        {
            var row = new VisualElement();
            row.AddToClassList("forge-mini-row");
            row.Add(Dot(color));
            row.Add(ForgeUI.Label(text, "forge-mini-row__text"));
            var right = ForgeUI.Label(tag, keyStyle ? "forge-key" : "forge-mono");
            right.AddToClassList("forge-mini-row__value");
            row.Add(right);
            return row;
        }

        /// <summary>Change le nombre de niveaux, après confirmation si des récompenses vont disparaître.</summary>
        void ChangeLevelCount(IntegerField field, int requested)
        {
            var count = Mathf.Clamp(requested, 2, Limits.maxLevelCap);
            if (count == Draft.MaxLevel)
            {
                field.SetValueWithoutNotify(count);
                return;
            }
            var lost = Draft.Rewards.Count(r => r.level > count);
            if (lost > 0 && !ForgeDialogs.Confirm("Retirer des niveaux",
                    $"{lost} récompense(s) des niveaux {count + 1} à {Draft.MaxLevel} seront supprimées (Ctrl+Z pour annuler).", "Continuer", "Annuler"))
            {
                field.SetValueWithoutNotify(Draft.MaxLevel);
                return;
            }
            ProgressionEditing.SetLevelCount(Serialized, count);
            ForgeLists.Notify($"La progression compte maintenant {count} niveaux.");
            Rebuild();
        }
    }
}
