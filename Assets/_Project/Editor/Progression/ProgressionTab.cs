using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using TheDungeon.Editor.Common.Charts;
using TheDungeon.Editor.Play;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Progression
{
    /// <summary>
    /// Progressions du joueur, rangées en catégories : aperçu, stats et courbes (modifiables à la souris, générées par fonction),
    /// mécaniques, attributs et compétences, récompenses par niveau, et simulation du personnage niveau par niveau.
    /// Import / export SVG et choix de la progression jouée.
    /// </summary>
    public sealed class ProgressionTab : AssetListTab<PlayerProgression>
    {
        enum View
        {
            Overview,
            Stats,
            Mechanics,
            Growth,
            Rewards,
            Simulation
        }

        static readonly (View view, string label)[] Views =
        {
            (View.Overview, "Aperçu"), (View.Stats, "Stats et courbes"), (View.Mechanics, "Mécaniques"),
            (View.Growth, "Attributs et compétences"), (View.Rewards, "Récompenses"), (View.Simulation, "Simulation des niveaux")
        };

        const string ViewKey = "forge.progression.view";
        const string CustomFunction = "Personnalisée";
        const float CurveHeight = 300f;

        static readonly Color InactiveColor = new(0.55f, 0.55f, 0.6f);

        SerializedObject serializedObject;
        VisualElement host;
        VisualElement viewHost;
        VisualElement segDrop;
        readonly Dictionary<View, Button> segButtons = new();
        readonly Dictionary<View, Label> segCounts = new();
        // Mises à jour légères après chaque modification (noms, résumés, simulation), sans reconstruire la catégorie.
        readonly List<Action> refreshers = new();
        string builtShape;
        View view = (View)SessionState.GetInt(ViewKey, 0);

        int selectedStat;
        int openMechanic = -1;
        int rewardLevel = -1;
        int simLevel = 1;
        int openLevel = -1;
        float xpPerEnemy = -1f;
        bool settingsOpen = true;
        bool generatorOpen;
        bool expanded;
        int undoGroup;

        CurveEditor curve;

        // Générateur : fonction, bornes et aperçu.
        Foldout generator;
        DropdownField presetField;
        TextField formulaField;
        FloatField startField;
        FloatField endField;
        Label generatorStatus;
        Button applyButton;
        Button deletePresetButton;
        float[] previewValues;
        int generatorStat = -1;

        public ProgressionTab(IToolHost window) : base(window)
        {
        }

        protected override string NewAssetName => "Nouvelle progression";
        protected override string Noun => "progression";
        protected override bool FeminineNoun => true;

        static bool IsActive(PlayerProgression progression) => progression != null && progression == GameLauncher.ActiveProgression;

        protected override Color GetColor(PlayerProgression progression) => IsActive(progression) ? ChartColors.Good : InactiveColor;

        protected override IEnumerable<VisualElement> GetBadges(PlayerProgression progression)
        {
            yield return Original != null && IsActive(Original)
                ? ForgeUI.Badge("Utilisée par le jeu", ChartColors.Good, "C'est cette progression que le joueur suit en jeu.")
                : ForgeUI.Badge("Non utilisée en jeu", InactiveColor, "Cliquez sur « Utiliser dans le jeu » (Aperçu) pour la faire jouer.");
            yield return ForgeUI.Badge($"{progression.Stats.Count} stats · {progression.MaxLevel} niveaux", new Color(0.7f, 0.7f, 0.7f));
        }

        /// <summary>Sous la recherche : import d'une progression exportée en SVG.</summary>
        protected override VisualElement BuildFilters()
        {
            var import = ForgeUI.Button("Importer un SVG…",
                "Crée une progression à partir d'un fichier SVG exporté par Forge Studio (éventuellement retouché dans Inkscape ou Figma).", ImportProgression);
            import.style.marginTop = 4;
            import.style.marginBottom = 12;
            return import;
        }

        protected override void InitializeNew(SerializedObject draft) => ProgressionEditing.Reset(draft, Limits);

        protected override void BuildDetails(VisualElement container, SerializedObject serialized, PlayerProgression draft)
        {
            serializedObject = serialized;
            host = new VisualElement();
            host.RegisterCallback<AttachToPanelEvent>(_ => Undo.undoRedoPerformed += OnUndoRedo);
            host.RegisterCallback<DetachFromPanelEvent>(_ => Undo.undoRedoPerformed -= OnUndoRedo);
            container.Add(host);
            generatorStat = -1;
            openLevel = -1;
            host.Add(BuildSeg());
            viewHost = new VisualElement();
            host.Add(viewHost);
            BuildView();
        }

        /// <summary>Reconstruit la catégorie affichée après un changement de structure (élément ajouté, supprimé, catégorie changée…).</summary>
        void Rebuild()
        {
            serializedObject.Update();
            viewHost.Clear();
            BuildView();
            viewHost.Bind(serializedObject);
            OnRefresh(Draft);
        }

        void OnUndoRedo()
        {
            if (serializedObject == null || serializedObject.targetObject == null)
                return;
            serializedObject.Update();
            if (Shape(Draft) != builtShape)
                Rebuild();
            else
                OnRefresh(Draft);
        }

        /// <summary>Structure de la fiche : si elle change (élément ajouté, supprimé, type de récompense…), la catégorie est reconstruite.</summary>
        static string Shape(PlayerProgression p) =>
            $"{p.Stats.Count}/{p.MaxLevel}/{string.Join(",", p.Attributes.Select(a => a.bonuses.Count))}/" +
            $"{string.Join(",", p.Skills.Select(s => s.ranks.Count))}/{string.Join(",", p.Rewards.Select(r => $"{(int)r.type}.{r.level}"))}/" +
            $"{string.Join(",", p.Mechanics.Select(m => $"{(int)m.action}.{m.upgrades.Count}"))}";

        void BuildView()
        {
            var draft = Draft;
            builtShape = Shape(draft);
            selectedStat = Mathf.Clamp(selectedStat, 0, Mathf.Max(0, draft.Stats.Count - 1));
            simLevel = Mathf.Clamp(simLevel, 1, Mathf.Max(1, draft.MaxLevel));
            refreshers.Clear();
            curve = null;
            generator = null;
            switch (view)
            {
                case View.Stats: BuildStatsView(draft); break;
                case View.Mechanics: BuildMechanicsView(draft); break;
                case View.Growth:
                    BuildAttributes(draft);
                    BuildSkills(draft);
                    break;
                case View.Rewards: BuildRewardsView(draft); break;
                case View.Simulation: BuildSimulationView(draft); break;
                default: BuildOverview(draft); break;
            }
            UpdateSeg();
        }

        #region Catégories

        VisualElement BuildSeg()
        {
            segButtons.Clear();
            segCounts.Clear();
            var seg = new VisualElement();
            seg.AddToClassList("forge-seg");
            segDrop = new VisualElement { pickingMode = PickingMode.Ignore };
            segDrop.AddToClassList("forge-seg__drop");
            seg.Add(segDrop);
            foreach (var (target, label) in Views)
            {
                var button = new Button(() => ShowView(target)) { tooltip = $"Afficher « {label} »." };
                button.AddToClassList("forge-seg__item");
                button.Add(new Label(label) { pickingMode = PickingMode.Ignore });
                var count = new Label { pickingMode = PickingMode.Ignore };
                count.AddToClassList("forge-seg__count");
                button.Add(count);
                button.RegisterCallback<GeometryChangedEvent>(_ =>
                {
                    if (target == view)
                        PlaceDrop();
                });
                segButtons[target] = button;
                segCounts[target] = count;
                seg.Add(button);
            }
            return seg;
        }

        void ShowView(View target)
        {
            view = target;
            SessionState.SetInt(ViewKey, (int)target);
            Rebuild();
            // Les barres de la catégorie ouverte grandissent, comme à l'ouverture d'un panneau.
            viewHost.schedule.Execute(() => ChartAnimation.ReplayAll(viewHost)).StartingIn(20);
        }


        void UpdateSeg()
        {
            var draft = Draft;
            if (draft == null || segButtons.Count == 0)
                return;
            foreach (var (target, button) in segButtons)
                button.EnableInClassList("forge-seg__item--selected", target == view);
            segCounts[View.Stats].text = draft.Stats.Count.ToString();
            segCounts[View.Mechanics].text = draft.Mechanics.Count.ToString();
            segCounts[View.Growth].text = (draft.Attributes.Count + draft.Skills.Count).ToString();
            segCounts[View.Rewards].text = draft.Rewards.Count.ToString();
            PlaceDrop();
        }

        /// <summary>La goutte de verre glisse sous la catégorie choisie (transition USS sur sa position et sa largeur).</summary>
        void PlaceDrop()
        {
            if (segDrop?.parent == null || !segButtons.TryGetValue(view, out var button) || float.IsNaN(button.worldBound.width))
                return;
            // Position à l'écran du bouton, ramenée dans le repère de la goutte (bord intérieur du contrôle segmenté).
            var seg = segDrop.parent;
            segDrop.style.left = button.worldBound.x - seg.worldBound.x - seg.resolvedStyle.borderLeftWidth;
            segDrop.style.top = button.worldBound.y - seg.worldBound.y - seg.resolvedStyle.borderTopWidth;
            segDrop.style.bottom = StyleKeyword.Auto;
            segDrop.style.width = button.worldBound.width;
            segDrop.style.height = button.worldBound.height;

        }

        #endregion

        #region Aperçu

        VisualElement Tile(string title, View? details, string modifier = null)
        {
            var tile = new VisualElement();
            tile.AddToClassList("forge-section");
            tile.AddToClassList("forge-tile");
            if (modifier != null)
                tile.AddToClassList(modifier);
            var header = new VisualElement();
            header.AddToClassList("forge-tile__header");
            header.Add(ForgeUI.Label(title, "forge-tile__title"));
            if (details.HasValue)
            {
                var target = details.Value;
                header.Add(ForgeUI.Button("Détails", $"Ouvre « {Views.First(v => v.view == target).label} ».", () => ShowView(target)));
            }
            tile.Add(header);
            return tile;
        }

        void BuildOverview(PlayerProgression draft)
        {
            var tiles = new VisualElement();
            tiles.AddToClassList("forge-tiles");
            viewHost.Add(tiles);

            // Progression : nom, nombre de niveaux, progression jouée, export.
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
            var activate = ForgeUI.Button("Utiliser dans le jeu", "Le jeu suivra cette progression (une seule progression est jouée à la fois).", () =>
            {
                GameLauncher.ActiveProgression = Original;
                ForgeLists.Notify($"« {Original.DisplayName} » est maintenant utilisée par le jeu.");
                Reload();
                Rebuild();
                RefreshDetails();
            }, "forge-button--primary");
            activate.SetEnabled(Original != null && !IsActive(Original));
            if (Original == null)
                activate.tooltip = "Validez d'abord la progression pour pouvoir la faire jouer.";
            else if (IsActive(Original))
                activate.text = "Utilisée par le jeu";
            actions.Add(activate);
            actions.Add(ForgeUI.Button("Exporter en SVG…",
                "Enregistre les courbes dans une image SVG : lisible partout, retouchable dans un logiciel de dessin et réimportable.", ExportSvg));
            identity.Add(actions);
            tiles.Add(identity);

            // Stats et courbes : une mini-courbe par stat.
            var stats = Tile("Stats et courbes", View.Stats, "forge-tile--wide");
            var sparks = new VisualElement();
            sparks.AddToClassList("forge-sparks");
            for (var i = 0; i < draft.Stats.Count; i++)
            {
                var index = i;
                var card = new VisualElement { tooltip = "Modifier cette courbe." };
                card.AddToClassList("forge-spark-card");
                var head = new VisualElement();
                head.AddToClassList("forge-mini-row");
                head.style.paddingLeft = 0;
                var dot = Dot(draft.Stats[i].color);
                head.Add(dot);
                var name = ForgeUI.Label(draft.Stats[i].displayName);
                head.Add(name);
                card.Add(head);
                var spark = new Sparkline();
                card.Add(spark);
                var range = ForgeUI.Label(string.Empty, "forge-hint");
                range.AddToClassList("forge-mono");
                card.Add(range);
                card.RegisterCallback<ClickEvent>(_ =>
                {
                    selectedStat = index;
                    ShowView(View.Stats);
                });
                refreshers.Add(() =>
                {
                    if (index >= Draft.Stats.Count)
                        return;
                    var stat = Draft.Stats[index];
                    dot.style.backgroundColor = stat.color;
                    name.text = stat.displayName;
                    spark.SetData(stat.values, stat.color);
                    range.text = stat.values.Count > 0 ? $"{stat.Format(stat.values[0])} → {stat.Format(stat.values[^1])}" : "—";
                });
                sparks.Add(card);
            }
            stats.Add(sparks);
            tiles.Add(stats);

            // Mécaniques : touche et nom.
            var mechanics = Tile("Mécaniques", View.Mechanics);
            for (var i = 0; i < draft.Mechanics.Count; i++)
            {
                var index = i;
                var mechanic = draft.Mechanics[i];
                var row = MiniRow(mechanic.color, mechanic.displayName, ProgressionEditing.InputLabel(mechanic.input));
                row.RegisterCallback<ClickEvent>(_ =>
                {
                    openMechanic = index;
                    ShowView(View.Mechanics);
                });
                mechanics.Add(row);
            }
            if (draft.Mechanics.Count == 0)
                mechanics.Add(ForgeUI.Label("Aucune mécanique : le joueur garde le tir et l'esquive de base.", "forge-hint"));
            tiles.Add(mechanics);

            // Simulation : le personnage au dernier niveau.
            var simulation = Tile("Simulation", View.Simulation);
            var simBody = new VisualElement();
            simulation.Add(simBody);
            refreshers.Add(() =>
            {
                simBody.Clear();
                var p = Draft;
                simBody.Add(ForgeUI.Label(ForgeText.Window($"Personnage au niveau {p.MaxLevel}"), "forge-system-title"));
                foreach (var stat in p.Stats.Where(s => s.visibility != StatVisibility.Hidden && s.role != StatRole.XpToNext).Take(4))
                    simBody.Add(MiniRow(stat.color, stat.displayName, stat.Format(stat.GetValue(p.MaxLevel)), false));
                var sim = ProgressionSimulation.At(p, p.MaxLevel, XpPerEnemy);
                simBody.Add(ForgeUI.Label(sim.HasXp
                    ? $"Environ <b>{sim.EnemiesTotal}</b> ennemis à vaincre pour l'atteindre ({XpPerEnemy:0.#} XP par ennemi)."
                    : "Aucune stat d'XP : l'effort n'est pas calculable.", "forge-hint"));
            });
            tiles.Add(simulation);

            // Attributs et compétences : résumé.
            var growth = Tile("Attributs et compétences", View.Growth);
            var growthBody = new VisualElement();
            growth.Add(growthBody);
            refreshers.Add(() =>
            {
                var p = Draft;
                growthBody.Clear();
                growthBody.Add(MiniRow(ChartColors.Good, "Attributs", p.Attributes.Count > 0 ? string.Join(", ", p.Attributes.Select(a => a.displayName)) : "aucun", false));
                growthBody.Add(MiniRow(ChartColors.Good, "Points d'attributs",
                    $"{ProgressionEditing.TotalAttributePoints(p)} gagnés / {ProgressionEditing.AttributeCapacity(p)} dépensables", false));
                growthBody.Add(MiniRow(new Color(0.54f, 0.66f, 0.88f), "Compétences", p.Skills.Count > 0 ? string.Join(", ", p.Skills.Select(s => s.displayName)) : "aucune", false));
            });

            tiles.Add(growth);

            // Récompenses : la frise des niveaux.
            var rewards = Tile("Récompenses", View.Rewards, "forge-tile--full");
            rewards.Add(Timeline(draft, true));
            rewards.Add(Legend());
            tiles.Add(rewards);
        }

        VisualElement MiniRow(Color color, string text, string tag, bool keyStyle = true)
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

        static VisualElement Dot(Color color)
        {
            var dot = new VisualElement { pickingMode = PickingMode.Ignore };
            dot.AddToClassList("forge-dot");
            dot.style.backgroundColor = color;
            return dot;
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
            ProgressionEditing.SetLevelCount(serializedObject, count);
            ForgeLists.Notify($"La progression compte maintenant {count} niveaux.");
            Rebuild();
        }

        #endregion

        #region Stats et courbes

        void BuildStatsView(PlayerProgression draft)
        {
            var split = new VisualElement();
            split.AddToClassList("forge-split");
            viewHost.Add(split);
            if (!expanded)
                split.Add(BuildStatList(draft));

            var main = new VisualElement();
            main.AddToClassList("forge-split__main");
            split.Add(main);
            if (draft.Stats.Count == 0)
            {
                var empty = ForgeUI.Section("Aucune stat", main, "Ajoutez une stat pour dessiner sa courbe.");
                empty.Add(AddStatButton(draft));
                return;
            }

            BuildCurve(main, draft);
            if (expanded)
                return;

            var settings = new Foldout { text = "Réglages de la stat", value = settingsOpen };
            settings.AddToClassList("forge-section");
            settings.AddToClassList("forge-fold");
            settings.RegisterValueChangedCallback(e =>
            {
                if (e.target == settings)
                    settingsOpen = e.newValue;
            });
            settings.Add(StatCard(serializedObject.FindProperty("stats").GetArrayElementAtIndex(selectedStat), selectedStat, draft.Stats[selectedStat]));
            main.Add(settings);
            BuildGenerator(main);
        }

        VisualElement BuildStatList(PlayerProgression draft)
        {
            var list = new VisualElement();
            list.AddToClassList("forge-section");
            list.AddToClassList("forge-stat-list");
            list.Add(ForgeUI.Label("Stats", "forge-section-title"));
            list.Add(ForgeUI.Label("Choisissez une stat : sa courbe et ses réglages s'affichent à côté.", "forge-section-description"));
            for (var i = 0; i < draft.Stats.Count; i++)
            {
                var index = i;
                var row = new VisualElement { tooltip = "Modifier cette stat." };
                row.AddToClassList("forge-srow");
                row.EnableInClassList("forge-srow--selected", i == selectedStat);
                var dot = Dot(draft.Stats[i].color);
                row.Add(dot);
                var text = new VisualElement { pickingMode = PickingMode.Ignore };
                text.AddToClassList("forge-srow__text");
                var name = ForgeUI.Label(draft.Stats[i].displayName, "forge-srow__name");
                var meta = ForgeUI.Label(string.Empty, "forge-srow__meta");
                text.Add(name);
                text.Add(meta);
                row.Add(text);
                var spark = new Sparkline();
                row.Add(spark);
                row.RegisterCallback<ClickEvent>(_ =>
                {
                    selectedStat = index;
                    Rebuild();
                });
                refreshers.Add(() =>
                {
                    if (index >= Draft.Stats.Count)
                        return;
                    var stat = Draft.Stats[index];
                    dot.style.backgroundColor = stat.color;
                    name.text = stat.displayName;
                    meta.text = stat.resource ? $"Jauge, +{stat.regenPerSecond:0.#}/s" : ProgressionEditing.RoleLabel(stat.role);
                    spark.SetData(stat.values, stat.color);
                });
                list.Add(row);
            }
            list.Add(AddStatButton(draft));
            return list;
        }

        Button AddStatButton(PlayerProgression draft)
        {
            var add = ForgeUI.Button("+ Ajouter une stat", $"Ajoute une stat libre (maximum {Limits.maxProgressionStats}).", () =>
            {
                selectedStat = ProgressionEditing.AddStat(serializedObject, new ProgressionStat
                {
                    displayName = UniqueName(Draft.Stats.Select(s => s.displayName), "Nouvelle stat"),
                    color = NextColor(Draft.Stats.Count),
                    min = 0f,
                    max = 100f
                }, "Linéaire", 0f, 10f);
                settingsOpen = true;
                Rebuild();
            });
            add.SetEnabled(draft.Stats.Count < Limits.maxProgressionStats);
            add.style.alignSelf = Align.FlexStart;
            add.style.marginTop = 6;
            return add;
        }

        void BuildCurve(VisualElement parent, PlayerProgression draft)
        {
            var section = new VisualElement();
            section.AddToClassList("forge-section");
            var head = new VisualElement();
            head.AddToClassList("forge-curve-head");
            var dot = Dot(draft.Stats[selectedStat].color);
            head.Add(dot);
            var title = ForgeUI.Label(draft.Stats[selectedStat].displayName, "forge-section-title");
            head.Add(title);
            var range = ForgeUI.Label(string.Empty, "forge-curve-head__range");
            range.AddToClassList("forge-mono");
            head.Add(range);
            head.Add(ForgeUI.Button("Importer une courbe…",
                "Remplace la courbe par la forme d'une courbe SVG (dessinée dans Inkscape, Figma…), entre ses valeurs actuelles la plus basse et la plus haute.",
                ImportShape));
            head.Add(ForgeUI.Button(expanded ? "Réduire" : "Agrandir",
                expanded ? "Revient à la liste des stats et aux réglages." : "Agrandit la courbe pour la retoucher plus finement.", () =>
                {
                    expanded = !expanded;
                    Rebuild();
                }));
            section.Add(head);
            refreshers.Add(() =>
            {
                if (selectedStat >= Draft.Stats.Count)
                    return;
                var stat = Draft.Stats[selectedStat];
                dot.style.backgroundColor = stat.color;
                title.text = stat.displayName;
                range.text = stat.values.Count > 0
                    ? $"[{stat.Format(stat.values[0])} → {stat.Format(stat.values[^1])}, bornes {stat.Format(stat.min)} à {stat.Format(stat.max)}]"
                    : string.Empty;
            });

            curve = new CurveEditor(expanded ? Mathf.Max(460f, (host.panel?.visualTree.layout.height ?? 0f) - 330f) : CurveHeight);
            curve.EditStarted += () =>
            {
                Undo.IncrementCurrentGroup();
                undoGroup = Undo.GetCurrentGroup();
            };
            curve.Edited += values =>
            {
                if (selectedStat < Draft.Stats.Count)
                    ProgressionEditing.SetValues(serializedObject, selectedStat, values);
            };
            curve.EditFinished += () =>
            {
                Undo.SetCurrentGroupName("Modifier la courbe");
                Undo.CollapseUndoOperations(undoGroup);
            };
            section.Add(curve);
            section.Add(ForgeUI.Label("Glisser un point : changer sa valeur  ·  Maj + glisser : les niveaux voisins suivent  ·  " +
                                      "Ctrl + glisser : dessiner à main levée  ·  Ctrl+Z : annuler", "forge-hint"));
            parent.Add(section);
        }

        VisualElement StatCard(SerializedProperty property, int index, ProgressionStat stat)
        {
            var card = new VisualElement();
            card.AddToClassList("forge-stat-card");

            var header = Row();
            var color = new ColorField { showAlpha = false, tooltip = "Couleur de la courbe et de la barre en jeu." };
            color.AddToClassList("forge-stat-card__color");
            color.BindProperty(property.FindPropertyRelative("color"));
            header.Add(color);
            var name = new TextField { tooltip = "Nom affiché au joueur et dans les outils." };
            name.AddToClassList("forge-stat-card__name");
            name.BindProperty(property.FindPropertyRelative("displayName"));
            header.Add(name);
            header.Add(EnumDropdown<StatRole>("Rôle", property.FindPropertyRelative("role"), ProgressionEditing.RoleLabel,
                "Ce que le jeu fait de la stat. Les rôles ont des bornes fixées par l'équipe technique.", role => OnRoleChanged(index, role)));
            header.Add(SmallButton("▲", "Monter la stat dans la liste.", () => Move(index, -1), index > 0));
            header.Add(SmallButton("▼", "Descendre la stat dans la liste.", () => Move(index, 1), index < Draft.Stats.Count - 1));
            header.Add(SmallButton("Supprimer", "Supprime la stat et sa courbe (Ctrl+Z pour annuler).", () => RemoveStat(index), true, "forge-button--danger"));
            card.Add(header);

            var display = Row();
            var format = EnumDropdown<StatFormat>("Présentation", property.FindPropertyRelative("format"), ProgressionEditing.FormatLabel,
                "Nombre (12), barre qui se remplit, ou pourcentage (12 %).");
            format.SetEnabled(stat.visibility != StatVisibility.Hidden);
            display.Add(EnumDropdown<StatVisibility>("Affichage joueur", property.FindPropertyRelative("visibility"), ProgressionEditing.VisibilityLabel,
                "Masquée, toujours à l'écran (HUD) ou seulement dans la fiche du personnage (inventaire).",
                visibility => format.SetEnabled(visibility != StatVisibility.Hidden)));
            display.Add(format);
            var decimals = new SliderInt("Décimales", 0, 3) { showInputField = true, tooltip = "0 = valeurs entières. Les points glissés sont arrondis en conséquence." };
            decimals.BindProperty(property.FindPropertyRelative("decimals"));
            display.Add(decimals);
            card.Add(display);

            var bounds = Row();
            var limits = ProgressionEditing.Bounds(stat.role, Limits);
            var boundsTip = limits.HasValue ? $"\nLimites de l'équipe technique pour ce rôle : {limits.Value.min} à {limits.Value.max}." : string.Empty;
            var min = new FloatField("Min") { tooltip = "Valeur minimale à tous les niveaux. C'est aussi le bas de l'axe de la courbe." + boundsTip };
            min.BindProperty(property.FindPropertyRelative("min"));
            var max = new FloatField("Max") { tooltip = "Valeur maximale à tous les niveaux. C'est aussi le haut de l'axe de la courbe et le repère d'une barre." + boundsTip };
            max.BindProperty(property.FindPropertyRelative("max"));
            bounds.Add(min);
            bounds.Add(max);
            bounds.Add(EnumDropdown<StatGrowth>("Évolution", property.FindPropertyRelative("growth"), ProgressionEditing.GrowthLabel,
                "Contrôle du Validator : une valeur qui baisse (ou stagne) est signalée en rouge."));
            card.Add(bounds);

            var gauge = Row();
            var regen = Bound(new FloatField("Recharge par seconde") { tooltip = "Quantité rendue chaque seconde à la jauge." }, property.FindPropertyRelative("regenPerSecond"));
            regen.SetEnabled(stat.resource);
            var resource = Bound(new Toggle("Jauge consommable")
            {
                tooltip = "Endurance, mana… : les mécaniques du personnage la consomment et elle se recharge. La valeur du niveau devient le maximum de la jauge."
            }, property.FindPropertyRelative("resource"));
            resource.RegisterValueChangedCallback(e => regen.SetEnabled(e.newValue));
            gauge.Add(resource);
            gauge.Add(regen);
            card.Add(gauge);
            return card;
        }

        static VisualElement Row()
        {
            var row = new VisualElement();
            row.AddToClassList("forge-stat-card__row");
            return row;
        }

        static Button SmallButton(string text, string tooltip, Action click, bool enabled, string className = null)
        {
            var button = ForgeUI.Button(text, tooltip, click, className);
            button.AddToClassList("forge-stat-card__button");
            button.SetEnabled(enabled);
            return button;
        }

        /// <summary>Liste déroulante en français pour un enum sérialisé (les valeurs de l'enum se suivent à partir de 0).</summary>
        DropdownField EnumDropdown<TEnum>(string label, SerializedProperty property, Func<TEnum, string> labeler, string tooltip,
            Action<TEnum> changed = null) where TEnum : Enum
        {
            var values = Enum.GetValues(typeof(TEnum)).Cast<TEnum>().ToList();
            var choices = values.Select(labeler).ToList();
            var field = new DropdownField(label, choices, Mathf.Clamp(property.enumValueIndex, 0, choices.Count - 1)) { tooltip = tooltip };
            var path = property.propertyPath;
            field.RegisterValueChangedCallback(e =>
            {
                var index = choices.IndexOf(e.newValue);
                if (index < 0)
                    return;
                serializedObject.Update();
                serializedObject.FindProperty(path).enumValueIndex = index;
                serializedObject.ApplyModifiedProperties();
                changed?.Invoke(values[index]);
            });
            return field;
        }

        /// <summary>Un rôle impose ses bornes : min, max et valeurs sont ramenés dans les limites de l'équipe technique.</summary>
        void OnRoleChanged(int index, StatRole role)
        {
            var bounds = ProgressionEditing.Bounds(role, Limits);
            if (!bounds.HasValue)
                return;
            serializedObject.Update();
            var stat = serializedObject.FindProperty("stats").GetArrayElementAtIndex(index);
            var min = stat.FindPropertyRelative("min");
            var max = stat.FindPropertyRelative("max");
            min.floatValue = Mathf.Clamp(min.floatValue, bounds.Value.min, bounds.Value.max);
            max.floatValue = Mathf.Clamp(max.floatValue, bounds.Value.min, bounds.Value.max);
            if (max.floatValue <= min.floatValue)
                (min.floatValue, max.floatValue) = (bounds.Value.min, bounds.Value.max);
            var values = stat.FindPropertyRelative("values");
            for (var i = 0; i < values.arraySize; i++)
                values.GetArrayElementAtIndex(i).floatValue = Mathf.Clamp(values.GetArrayElementAtIndex(i).floatValue, min.floatValue, max.floatValue);
            serializedObject.ApplyModifiedProperties();
            Rebuild();
        }

        void Move(int index, int offset)
        {
            ProgressionEditing.MoveStat(serializedObject, index, offset);
            if (selectedStat == index)
                selectedStat += offset;
            Rebuild();
        }

        void RemoveStat(int index)
        {
            var stat = Draft.Stats[index];
            if (stat.role != StatRole.None && !ForgeDialogs.Confirm("Supprimer une stat utilisée par le jeu",
                    $"« {stat.displayName} » joue le rôle « {ProgressionEditing.RoleLabel(stat.role)} ». Sans elle, la progression ne pourra pas être validée " +
                    "tant qu'une autre stat ne reprend pas ce rôle.", "Supprimer", "Annuler"))
                return;
            ProgressionEditing.RemoveStat(serializedObject, index);
            if (selectedStat >= index)
                selectedStat = Mathf.Max(0, selectedStat - 1);
            Rebuild();
        }

        #endregion

        #region Générateur

        void BuildGenerator(VisualElement parent)
        {
            generator = new Foldout
            {
                text = "Générer la courbe avec une fonction",
                value = generatorOpen,
                tooltip = "Remplit toute la courbe de la stat sélectionnée à partir d'une fonction (preset ou formule personnelle). Un aperçu en pointillés s'affiche avant d'appliquer."
            };
            generator.AddToClassList("forge-section");
            generator.AddToClassList("forge-fold");
            generator.RegisterValueChangedCallback(e =>
            {
                if (e.target != generator)
                    return;
                generatorOpen = e.newValue;
                UpdatePreview();
            });
            parent.Add(generator);

            presetField = new DropdownField("Fonction", PresetChoices(), 0) { tooltip = "Presets intégrés, presets enregistrés par l'équipe, ou formule personnalisée." };
            presetField.RegisterValueChangedCallback(e =>
            {
                var preset = FormulaPresets.All.FirstOrDefault(p => p.name == e.newValue);
                if (preset != null)
                    formulaField.SetValueWithoutNotify(preset.expression);
                UpdatePreview();
            });
            generator.Add(presetField);

            formulaField = new TextField("Formule") { tooltip = Formula.Help };
            formulaField.AddToClassList("forge-formula");
            formulaField.AddToClassList("forge-mono");
            formulaField.RegisterValueChangedCallback(e =>
            {
                var match = FormulaPresets.All.FirstOrDefault(p => p.expression == e.newValue);
                presetField.SetValueWithoutNotify(match?.name ?? CustomFunction);
                UpdatePreview();
            });
            generator.Add(formulaField);

            startField = new FloatField("Valeur au niveau 1 (start)") { tooltip = "Variable « start » de la formule." };
            startField.RegisterValueChangedCallback(_ => UpdatePreview());
            endField = new FloatField($"Valeur au niveau {Draft.MaxLevel} (end)") { tooltip = "Variable « end » de la formule." };
            endField.RegisterValueChangedCallback(_ => UpdatePreview());
            generator.Add(startField);
            generator.Add(endField);

            generatorStatus = ForgeUI.Label(string.Empty, "forge-hint");
            generatorStatus.AddToClassList("forge-mono");
            generator.Add(generatorStatus);
            generator.Add(ForgeUI.Label(Formula.Help, "forge-hint"));

            var actions = new VisualElement();
            actions.AddToClassList("forge-row");
            applyButton = ForgeUI.Button("Appliquer", "Remplace toutes les valeurs de la stat par l'aperçu (Ctrl+Z pour annuler).", ApplyGenerator, "forge-button--primary");
            actions.Add(applyButton);
            var presetName = new TextField { tooltip = "Nom du preset à enregistrer." };
            presetName.style.flexGrow = 1;
            presetName.textEdition.placeholder = "Nom du preset";
            actions.Add(presetName);
            actions.Add(ForgeUI.Button("Enregistrer comme preset", "Ajoute la formule à la liste des fonctions, pour toute l'équipe (Settings/ProgressionPresets.asset).",
                () => SavePreset(presetName)));
            deletePresetButton = ForgeUI.Button("Supprimer le preset", "Retire ce preset d'équipe de la liste (les presets intégrés ne peuvent pas être supprimés).", DeletePreset,
                "forge-button--danger");
            actions.Add(deletePresetButton);
            generator.Add(actions);

            generatorStat = -1;
            presetField.value = "Exponentielle";
        }

        static List<string> PresetChoices() => FormulaPresets.All.Select(p => p.name).Append(CustomFunction).ToList();

        /// <summary>Quand on change de stat, start et end reprennent ses valeurs actuelles au premier et au dernier niveau.</summary>
        void SyncGeneratorStat()
        {
            if (generator == null || generatorStat == selectedStat || selectedStat >= Draft.Stats.Count)
                return;
            generatorStat = selectedStat;
            var values = Draft.Stats[selectedStat].values;
            startField.SetValueWithoutNotify(values.Count > 0 ? values[0] : 0f);
            endField.SetValueWithoutNotify(values.Count > 0 ? values[^1] : 0f);
        }

        void UpdatePreview()
        {
            previewValues = null;
            if (generator == null)
            {
                RefreshCurve();
                return;
            }
            var hasStat = selectedStat < Draft.Stats.Count;
            applyButton.text = hasStat ? $"Appliquer à « {Draft.Stats[selectedStat].displayName} »" : "Appliquer";
            deletePresetButton.style.display = presetField.value != CustomFunction && !FormulaPresets.IsBuiltIn(presetField.value) ? DisplayStyle.Flex : DisplayStyle.None;

            if (!hasStat)
                SetStatus("Ajoutez d'abord une stat.", true);
            else if (!Formula.TryParse(formulaField.value, out var formula, out var error))
                SetStatus(error, true);
            else
            {
                try
                {
                    var stat = Draft.Stats[selectedStat];
                    previewValues = ProgressionEditing.Generate(formula, Draft.MaxLevel, startField.value, endField.value, stat, out var clamped);
                    SetStatus(clamped > 0
                        ? $"[Aperçu en pointillés. {clamped} niveau(x) ramené(s) dans les bornes de la stat ({stat.min} à {stat.max}).]"
                        : "[Aperçu en pointillés sur la courbe.]", false);
                }
                catch (FormulaException exception)
                {
                    SetStatus(exception.Message, true);
                }
            }
            applyButton.SetEnabled(previewValues != null);
            RefreshCurve();
        }

        void SetStatus(string message, bool isError)
        {
            generatorStatus.text = isError ? $"<color=#e0606a>[{message}]</color>" : message;
        }

        void ApplyGenerator()
        {
            if (previewValues == null)
                return;
            ProgressionEditing.SetValues(serializedObject, selectedStat, previewValues);
            ForgeLists.Notify($"Courbe « {Draft.Stats[selectedStat].displayName} » générée.");
            OnRefresh(Draft);
        }

        void SavePreset(TextField nameField)
        {
            var name = nameField.value?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                ForgeLists.Notify("Donnez un nom au preset.");
                return;
            }
            if (FormulaPresets.IsBuiltIn(name) || name == CustomFunction)
            {
                ForgeLists.Notify("Ce nom est réservé à un preset intégré.");
                return;
            }
            if (!Formula.TryParse(formulaField.value, out _, out var error))
            {
                ForgeLists.Notify($"Formule invalide : {error}");
                return;
            }
            if (FormulaPresets.All.Any(p => p.name == name) &&
                !ForgeDialogs.Confirm("Remplacer le preset", $"Le preset « {name} » existe déjà. Le remplacer ?", "Remplacer", "Annuler"))
                return;
            FormulaPresets.Save(name, formulaField.value);
            presetField.choices = PresetChoices();
            presetField.SetValueWithoutNotify(name);
            nameField.SetValueWithoutNotify(string.Empty);
            ForgeLists.Notify($"Preset « {name} » enregistré.");
            UpdatePreview();
        }

        void DeletePreset()
        {
            var name = presetField.value;
            if (FormulaPresets.IsBuiltIn(name) || !ForgeDialogs.Confirm("Supprimer le preset", $"Supprimer le preset « {name} » pour toute l'équipe ?", "Supprimer", "Annuler"))
                return;
            FormulaPresets.Delete(name);
            presetField.choices = PresetChoices();
            presetField.SetValueWithoutNotify(CustomFunction);
            UpdatePreview();
        }

        #endregion

        #region Mécaniques

        void BuildMechanicsView(PlayerProgression draft)
        {
            var section = ForgeUI.Section("Mécaniques du personnage", viewHost,
                "Les actions du joueur : attaques, mouvements, soin, bouclier. Chacune a sa touche, peut consommer une stat (une jauge comme l'énergie, ou les PV) " +
                "et a un temps de recharge. Cliquez sur une ligne pour afficher tous ses réglages.");
            if (draft.Mechanics.Count == 0)
                section.Add(new HelpBox("Aucune mécanique : le joueur garde le tir (clic gauche) et l'esquive (Espace) de base.", HelpBoxMessageType.Info));
            var mechanics = serializedObject.FindProperty("mechanics");
            for (var i = 0; i < mechanics.arraySize; i++)
            {
                var index = i;
                var open = i == openMechanic;
                var acc = Accordion(open, draft.Mechanics[i].color, draft.Mechanics[i].displayName, () =>
                {
                    openMechanic = open ? -1 : index;
                    Rebuild();
                }, out var name, out var meta, out var dot);
                var key = ForgeUI.Label(ProgressionEditing.InputLabel(draft.Mechanics[i].input), "forge-key");
                acc.Q(className: "forge-acc__head").Insert(3, key);
                refreshers.Add(() =>
                {
                    if (index >= Draft.Mechanics.Count)
                        return;
                    var mechanic = Draft.Mechanics[index];
                    name.text = mechanic.displayName;
                    dot.style.backgroundColor = mechanic.color;
                    key.text = ProgressionEditing.InputLabel(mechanic.input);
                    var unlock = Enumerable.Range(1, Draft.MaxLevel).FirstOrDefault(l => ProgressionEditing.RankAt(Draft, mechanic, l) > 0);
                    meta.text = ProgressionEditing.Describe(Draft, mechanic, 1) + (unlock == 0 ? " · jamais débloquée" : unlock == 1 ? string.Empty : $" · niveau {unlock}");
                });
                if (open)
                    acc.Q(className: "forge-acc__body").Add(MechanicCard(mechanics.GetArrayElementAtIndex(i), i));
                section.Add(acc);
            }

            var add = ForgeUI.Button("+ Ajouter une mécanique", "Une mécanique par touche (6 au maximum).", () =>
            {
                var free = Enum.GetValues(typeof(MechanicInput)).Cast<MechanicInput>().FirstOrDefault(input => Draft.Mechanics.All(m => m.input != input));
                openMechanic = ProgressionEditing.AddMechanic(serializedObject, ProgressionEditing.Mechanic(UniqueName(Draft.Mechanics.Select(m => m.displayName), "Nouvelle mécanique"),
                    MechanicAction.Projectile, free, NextColor(Draft.Mechanics.Count + 7)));
                Rebuild();
            });
            add.SetEnabled(draft.Mechanics.Count < Enum.GetValues(typeof(MechanicInput)).Length);
            add.style.alignSelf = Align.FlexStart;
            section.Add(add);
        }

        /// <summary>Ligne dépliable : pastille, nom, résumé, chevron ; le contenu détaillé va dans « forge-acc__body ».</summary>
        static VisualElement Accordion(bool open, Color color, string title, Action toggle, out Label name, out Label meta, out VisualElement dot)
        {
            var acc = new VisualElement();
            acc.AddToClassList("forge-acc");
            acc.EnableInClassList("forge-acc--open", open);
            var head = new VisualElement { tooltip = open ? "Replier." : "Afficher tous les réglages." };
            head.AddToClassList("forge-acc__head");
            head.RegisterCallback<ClickEvent>(_ => toggle());
            dot = Dot(color);
            head.Add(dot);
            name = ForgeUI.Label(title, "forge-acc__name");
            head.Add(name);
            meta = ForgeUI.Label(string.Empty, "forge-acc__meta");
            head.Add(meta);
            var chevron = ForgeUI.Label(open ? "▼" : "›", "forge-acc__chevron");
            head.Add(chevron);
            acc.Add(head);
            var body = new VisualElement();
            body.AddToClassList("forge-acc__body");
            body.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            acc.Add(body);
            return acc;
        }

        VisualElement MechanicCard(SerializedProperty property, int index)
        {
            var mechanic = Draft.Mechanics[index];
            var card = Card(property, "Nom affiché au joueur dans la barre d'actions.", out var header);
            header.Add(EnumDropdown<MechanicAction>("Action", property.FindPropertyRelative("action"), ProgressionEditing.ActionLabel,
                "Ce que fait la mécanique. Changer d'action remet ses réglages par défaut.", action =>
                {
                    serializedObject.Update();
                    var copy = (CharacterMechanic)serializedObject.FindProperty(property.propertyPath).boxedValue;
                    ProgressionEditing.ApplyActionDefaults(copy, action);
                    serializedObject.FindProperty(property.propertyPath).boxedValue = copy;
                    serializedObject.ApplyModifiedProperties();
                    Rebuild();
                }));
            header.Add(EnumDropdown<MechanicInput>("Touche", property.FindPropertyRelative("input"), ProgressionEditing.InputLabel,
                "Touche qui la déclenche. Les clics peuvent être maintenus (tir continu)."));
            header.Add(SmallButton("Supprimer", "Supprime la mécanique et ses récompenses (Ctrl+Z pour annuler).", () =>
            {
                var used = Draft.Rewards.Count(r => r.mechanicId == Draft.Mechanics[index].id && r.type is RewardType.UnlockMechanic or RewardType.UpgradeMechanic);
                if (used > 0 && !ForgeDialogs.Confirm("Supprimer une mécanique", $"« {Draft.Mechanics[index].displayName} » est utilisée par {used} récompense(s) de niveau, " +
                        "qui seront supprimées aussi.", "Supprimer", "Annuler"))
                    return;
                ProgressionEditing.RemoveMechanic(serializedObject, index);
                openMechanic = -1;
                Rebuild();
            }, true, "forge-button--danger"));
            card.Add(Description(property, "Explication montrée au joueur."));

            var cost = Row();
            cost.Add(IdDropdown("Consomme", property.FindPropertyRelative("costStatId"),
                () => Draft.Stats.Select(s => (s.id, s.displayName)).ToList(), "Stat dépensée à chaque utilisation : une jauge (énergie, mana…) ou les PV.", "(gratuite)"));
            cost.Add(Bound(new FloatField("Par utilisation") { tooltip = "Quantité consommée à chaque utilisation." }, property.FindPropertyRelative("cost")));
            cost.Add(Bound(new FloatField("Recharge (s)") { tooltip = "Secondes avant de pouvoir la réutiliser." }, property.FindPropertyRelative("cooldown")));
            card.Add(cost);

            var (powerLabel, countLabel, durationLabel) = ProgressionEditing.ActionFields(mechanic.action);
            var settings = Row();
            if (powerLabel != null)
                settings.Add(Bound(new FloatField(powerLabel), property.FindPropertyRelative("power")));
            if (countLabel != null)
                settings.Add(Bound(new IntegerField(countLabel), property.FindPropertyRelative("count")));
            if (durationLabel != null)
                settings.Add(Bound(new FloatField(durationLabel), property.FindPropertyRelative("duration")));
            card.Add(settings);

            var unlock = Row();
            unlock.Add(Bound(new Toggle("Débloquée dès le début")
            {
                tooltip = "Sinon, ajoutez une récompense « Débloquer une mécanique » au niveau voulu (catégorie Récompenses)."
            }, property.FindPropertyRelative("unlockedAtStart")));
            card.Add(unlock);

            // Améliorations : ce que chaque rang ajoute (ou retire) aux réglages.
            var upgrades = property.FindPropertyRelative("upgrades");
            for (var u = 0; u < upgrades.arraySize; u++)
            {
                var upgradeIndex = u;
                var upgrade = upgrades.GetArrayElementAtIndex(u);
                var row = Row();
                row.AddToClassList("forge-stat-card__sub");
                row.Add(ForgeUI.Label($"Rang {u + 2}", "forge-stat-card__rank-title"));
                if (powerLabel != null)
                    row.Add(Bound(new FloatField($"{powerLabel} +") { tooltip = "Ajouté à la valeur du rang précédent (négatif : retiré)." }, upgrade.FindPropertyRelative("power")));
                if (countLabel != null)
                    row.Add(Bound(new IntegerField($"{countLabel} +"), upgrade.FindPropertyRelative("count")));
                if (durationLabel != null)
                    row.Add(Bound(new FloatField($"{durationLabel} +"), upgrade.FindPropertyRelative("duration")));
                row.Add(Bound(new FloatField("Coût +") { tooltip = "Négatif pour la rendre moins chère." }, upgrade.FindPropertyRelative("cost")));
                row.Add(Bound(new FloatField("Recharge +") { tooltip = "Négatif pour la recharger plus vite." }, upgrade.FindPropertyRelative("cooldown")));
                row.Add(SmallButton("×", "Retirer cette amélioration (et les rangs suivants changent de numéro).", () =>
                {
                    ProgressionEditing.RemoveAt(serializedObject, upgrades.propertyPath, upgradeIndex);
                    Rebuild();
                }, true));
                card.Add(row);
            }

            var footer = Row();
            footer.Add(SmallButton("+ Amélioration", "Ajoute un rang. Il s'obtient avec une récompense « Améliorer une mécanique ».", () =>
            {
                ProgressionEditing.Append(serializedObject, upgrades.propertyPath, new MechanicUpgrade());
                Rebuild();
            }, upgrades.arraySize < 9));
            card.Add(footer);

            var preview = ForgeUI.Label(string.Empty, "forge-hint");
            preview.AddToClassList("forge-mono");
            card.Add(preview);
            refreshers.Add(() =>
            {
                if (index >= Draft.Mechanics.Count)
                    return;
                var current = Draft.Mechanics[index];
                var lines = Enumerable.Range(1, current.MaxRank).Select(rank =>
                {
                    var level = Enumerable.Range(1, Draft.MaxLevel).FirstOrDefault(l => ProgressionEditing.RankAt(Draft, current, l) >= rank);
                    var when = level == 0 ? "jamais obtenu" : level == 1 && rank == 1 && current.unlockedAtStart ? "dès le début" : $"niveau {level}";
                    return $"[Rang {rank}, {when}] {ProgressionEditing.Describe(Draft, current, rank)}";
                });
                preview.text = string.Join("\n", lines);
            });
            return card;
        }

        #endregion

        #region Attributs et compétences

        void BuildAttributes(PlayerProgression draft)
        {
            var section = ForgeUI.Section("Attributs (points à répartir)", viewHost,
                "Le joueur gagne des points avec les récompenses de niveau et les répartit lui-même en se reposant à un feu de camp, comme dans Elden Ring. " +
                "Chaque point améliore une ou plusieurs stats ; au-delà du seuil, un point rapporte moins.");
            var summary = ForgeUI.Label(string.Empty, "forge-hint");
            refreshers.Add(() =>
            {
                var earned = ProgressionEditing.TotalAttributePoints(Draft);
                var capacity = ProgressionEditing.AttributeCapacity(Draft);
                summary.text = $"{earned} point(s) gagné(s) sur toute la partie · {capacity} point(s) dépensable(s) au maximum (somme des maximums)." +
                               (earned > capacity ? "  <color=#e0606a>Le joueur gagnera des points qu'il ne pourra pas dépenser.</color>" : string.Empty);
            });
            section.Add(summary);

            var attributes = serializedObject.FindProperty("attributes");
            for (var i = 0; i < attributes.arraySize; i++)
                section.Add(AttributeCard(attributes.GetArrayElementAtIndex(i), i));

            var add = ForgeUI.Button("+ Ajouter un attribut", $"Maximum {Limits.maxAttributes} (la fiche du jeu n'en affiche pas plus).", () =>
            {
                var attribute = new ProgressionAttribute { displayName = UniqueName(Draft.Attributes.Select(a => a.displayName), "Nouvel attribut"), color = NextColor(Draft.Attributes.Count + 3) };
                if (Draft.Stats.Count > 0)
                    attribute.bonuses.Add(new AttributeBonus { statId = Draft.Stats[0].id, perPoint = 1f });
                ProgressionEditing.AddAttribute(serializedObject, attribute);
                Rebuild();
            });
            add.SetEnabled(draft.Attributes.Count < Limits.maxAttributes);
            add.style.alignSelf = Align.FlexStart;
            section.Add(add);
        }

        VisualElement AttributeCard(SerializedProperty property, int index)
        {
            var card = Card(property, "Nom affiché au joueur dans sa fiche (Vigueur, Force…).", out var header);
            var maxPoints = new IntegerField("Points max") { tooltip = "Nombre maximum de points investis dans cet attribut." };
            maxPoints.BindProperty(property.FindPropertyRelative("maxPoints"));
            maxPoints.RegisterValueChangedCallback(e =>
            {
                if (e.newValue < 1)
                    maxPoints.value = 1;
            });
            header.Add(maxPoints);
            header.Add(SmallButton("Supprimer", "Supprime l'attribut (Ctrl+Z pour annuler).", () =>
            {
                ProgressionEditing.RemoveAt(serializedObject, "attributes", index);
                Rebuild();
            }, true, "forge-button--danger"));
            card.Add(Description(property, "Explication montrée au joueur quand il répartit ses points."));

            var bonuses = property.FindPropertyRelative("bonuses");
            for (var b = 0; b < bonuses.arraySize; b++)
            {
                var bonusIndex = b;
                var bonus = bonuses.GetArrayElementAtIndex(b);
                var row = Row();
                row.AddToClassList("forge-stat-card__sub");
                row.Add(IdDropdown("Stat", bonus.FindPropertyRelative("statId"), () => Draft.Stats.Select(s => (s.id, s.displayName)).ToList(),
                    "Stat améliorée par chaque point."));
                row.Add(Bound(new FloatField("Par point") { tooltip = "Gain par point investi, jusqu'au seuil." }, bonus.FindPropertyRelative("perPoint")));
                row.Add(Bound(new IntegerField("Seuil") { tooltip = "À partir de ce nombre de points, le gain diminue (0 = jamais)." }, bonus.FindPropertyRelative("softCap")));
                row.Add(Bound(new Slider("Après le seuil", 0f, 1f) { showInputField = true, tooltip = "Part du gain conservée au-delà du seuil (0,5 = moitié)." },
                    bonus.FindPropertyRelative("afterSoftCap")));
                row.Add(SmallButton("×", "Retirer ce bonus.", () =>
                {
                    serializedObject.Update();
                    serializedObject.FindProperty(bonuses.propertyPath).DeleteArrayElementAtIndex(bonusIndex);
                    serializedObject.ApplyModifiedProperties();
                    Rebuild();
                }, true));
                card.Add(row);
            }

            var footer = Row();
            footer.Add(SmallButton("+ Bonus sur une stat", "Chaque point de l'attribut améliorera aussi cette stat.", () =>
            {
                ProgressionEditing.Append(serializedObject, bonuses.propertyPath,
                    new AttributeBonus { statId = Draft.Stats.Count > 0 ? Draft.Stats[0].id : string.Empty, perPoint = 1f });
                Rebuild();
            }, Draft.Stats.Count > 0));
            var preview = ForgeUI.Label(string.Empty, "forge-hint");
            preview.style.flexGrow = 1;
            footer.Add(preview);
            refreshers.Add(() =>
            {
                if (index >= Draft.Attributes.Count)
                    return;
                var current = Draft.Attributes[index];
                var half = Mathf.Max(1, current.maxPoints / 2);
                preview.text = $"À {half} pts : {Gains(current, half)}   ·   À {current.maxPoints} pts (max) : {Gains(current, current.maxPoints)}";
            });
            card.Add(footer);
            return card;
        }

        string Gains(ProgressionAttribute attribute, int points)
        {
            var gains = attribute.bonuses.Select(b => (b, stat: Draft.FindStat(b.statId))).Where(p => p.stat != null)
                .Select(p => $"{p.stat.displayName} +{p.stat.Format(p.b.Total(points))}").ToList();
            return gains.Count > 0 ? string.Join(", ", gains) : "aucun effet";
        }

        void BuildSkills(PlayerProgression draft)
        {
            var section = ForgeUI.Section("Compétences", viewHost,
                "Débloquées puis améliorées par les récompenses de niveau. Une valeur par rang : le rang 1 est obtenu au déblocage, " +
                "chaque amélioration fait passer au rang suivant.");
            var skills = serializedObject.FindProperty("skills");
            for (var i = 0; i < skills.arraySize; i++)
                section.Add(SkillCard(skills.GetArrayElementAtIndex(i), i));

            var add = ForgeUI.Button("+ Ajouter une compétence", $"Maximum {Limits.maxSkills}.", () =>
            {
                ProgressionEditing.AddSkill(serializedObject, ProgressionEditing.Skill(UniqueName(Draft.Skills.Select(s => s.displayName), "Nouvelle compétence"),
                    SkillEffect.None, NextColor(Draft.Skills.Count + 5), 1f));
                Rebuild();
            });
            add.SetEnabled(draft.Skills.Count < Limits.maxSkills);
            add.style.alignSelf = Align.FlexStart;
            section.Add(add);
        }

        VisualElement SkillCard(SerializedProperty property, int index)
        {
            var card = Card(property, "Nom affiché au joueur.", out var header);
            header.Add(EnumDropdown<SkillEffect>("Effet", property.FindPropertyRelative("effect"), ProgressionEditing.SkillEffectLabel,
                "Ce que fait la compétence en jeu. « Aucun » : la description seule est montrée au joueur."));
            header.Add(SmallButton("Supprimer", "Supprime la compétence et ses récompenses (Ctrl+Z pour annuler).", () =>
            {
                var skill = Draft.Skills[index];
                var used = Draft.Rewards.Count(r => r.skillId == skill.id && r.type is RewardType.UnlockSkill or RewardType.UpgradeSkill);
                if (used > 0 && !ForgeDialogs.Confirm("Supprimer une compétence", $"« {skill.displayName} » est utilisée par {used} récompense(s) de niveau, " +
                        "qui seront supprimées aussi.", "Supprimer", "Annuler"))
                    return;
                ProgressionEditing.RemoveSkill(serializedObject, index);
                Rebuild();
            }, true, "forge-button--danger"));
            card.Add(Description(property, "Explication montrée au joueur."));

            var ranks = property.FindPropertyRelative("ranks");
            var row = Row();
            for (var r = 0; r < ranks.arraySize; r++)
            {
                var field = Bound(new FloatField($"Rang {r + 1}") { tooltip = r == 0 ? "Valeur au déblocage." : $"Valeur après {r} amélioration(s)." },
                    ranks.GetArrayElementAtIndex(r));
                field.AddToClassList("forge-stat-card__rank");
                row.Add(field);
            }
            row.Add(SmallButton("+ rang", "Ajoute un rang (une amélioration possible de plus).", () =>
            {
                serializedObject.Update();
                var list = serializedObject.FindProperty(ranks.propertyPath);
                var last = list.arraySize > 0 ? list.GetArrayElementAtIndex(list.arraySize - 1).floatValue : 1f;
                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).floatValue = last;
                serializedObject.ApplyModifiedProperties();
                Rebuild();
            }, ranks.arraySize < 10));
            row.Add(SmallButton("- rang", "Retire le dernier rang.", () =>
            {
                serializedObject.Update();
                serializedObject.FindProperty(ranks.propertyPath).arraySize--;
                serializedObject.ApplyModifiedProperties();
                Rebuild();
            }, ranks.arraySize > 1));
            card.Add(row);

            var preview = ForgeUI.Label(string.Empty, "forge-hint");
            card.Add(preview);
            refreshers.Add(() =>
            {
                if (index >= Draft.Skills.Count)
                    return;
                var skill = Draft.Skills[index];
                var obtained = Enumerable.Range(1, skill.MaxRank)
                    .Select(rank => (rank, level: Enumerable.Range(1, Draft.MaxLevel).FirstOrDefault(l => ProgressionEditing.RankAt(Draft, skill, l) >= rank)))
                    .Select(p => p.level > 0 ? $"rang {p.rank} au niveau {p.level} ({skill.EffectText(p.rank)})" : $"rang {p.rank} jamais obtenu");
                preview.text = string.Join("  ·  ", obtained);
            });
            return card;
        }

        #endregion

        #region Récompenses

        void BuildRewardsView(PlayerProgression draft)
        {
            if (rewardLevel < 1)
                rewardLevel = draft.Rewards.Count > 0 ? draft.Rewards.Min(r => r.level) : 1;
            rewardLevel = Mathf.Clamp(rewardLevel, 1, draft.MaxLevel);

            var timeline = ForgeUI.Section("Récompenses par niveau", viewHost,
                "Choisissez un niveau sur la frise pour voir et modifier ce qu'il rapporte : points d'attributs, compétence ou mécanique débloquée ou améliorée, note.");
            timeline.Add(Timeline(draft, false));
            timeline.Add(Legend());

            var level = ForgeUI.Section($"Niveau {rewardLevel}", viewHost);
            AddLevelRewards(level, draft, rewardLevel);

            var quick = new Foldout { text = "Donner des points d'attributs à intervalle régulier", value = false };
            quick.AddToClassList("forge-section");
            quick.AddToClassList("forge-fold");
            var row = Row();
            var amount = new IntegerField("Points") { value = 1, tooltip = "Points d'attributs donnés à chaque fois." };
            var every = new IntegerField("Tous les … niveaux") { value = 1, tooltip = "Écart entre deux niveaux qui donnent des points." };
            var from = new IntegerField("À partir du niveau") { value = 2, tooltip = "Premier niveau qui donne des points." };
            row.Add(amount);
            row.Add(every);
            row.Add(from);
            row.Add(SmallButton("Répartir", "Remplace toutes les récompenses « Points d'attributs » par ce rythme (Ctrl+Z pour annuler).", () =>
            {
                ProgressionEditing.SetAttributePoints(serializedObject, Mathf.Max(0, amount.value), Mathf.Max(1, from.value), Mathf.Max(1, every.value));
                Rebuild();
            }, true, "forge-button--primary"));
            quick.Add(row);
            viewHost.Add(quick);
        }

        /// <summary>Récompenses d'un niveau, modifiables, et boutons pour en ajouter.</summary>
        void AddLevelRewards(VisualElement parent, PlayerProgression draft, int level)
        {
            var rewards = serializedObject.FindProperty("rewards");
            var any = false;
            for (var i = 0; i < draft.Rewards.Count; i++)
            {
                if (draft.Rewards[i].level != level)
                    continue;
                parent.Add(RewardRow(rewards.GetArrayElementAtIndex(i), i, draft.Rewards[i]));
                any = true;
            }
            if (!any)
                parent.Add(ForgeUI.Label("Rien à ce niveau.", "forge-hint"));

            var add = new VisualElement();
            add.AddToClassList("forge-row");
            void AddButton(string text, RewardType type, bool enabled = true)
            {
                var button = ForgeUI.Button(text, $"Ajoute une récompense au niveau {level}.", () =>
                {
                    ProgressionEditing.AddReward(serializedObject, new LevelReward { level = level, type = type, amount = 1 });
                    ProgressionEditing.SortRewards(serializedObject);
                    Rebuild();
                });
                button.SetEnabled(enabled);
                add.Add(button);
            }
            AddButton("+ Points d'attributs", RewardType.AttributePoints);
            AddButton("+ Compétence", RewardType.UnlockSkill, draft.Skills.Count > 0);
            AddButton("+ Rang de compétence", RewardType.UpgradeSkill, draft.Skills.Count > 0);
            AddButton("+ Mécanique", RewardType.UnlockMechanic, draft.Mechanics.Count > 0);
            AddButton("+ Rang de mécanique", RewardType.UpgradeMechanic, draft.Mechanics.Count > 0);
            AddButton("+ Note", RewardType.Note);
            parent.Add(add);
        }

        VisualElement RewardRow(SerializedProperty property, int index, LevelReward reward)
        {
            var row = Row();
            row.AddToClassList("forge-reward-row");
            var level = Bound(new IntegerField("Niveau") { isDelayed = true, tooltip = $"Niveau auquel la récompense est obtenue (1 à {Draft.MaxLevel}). La changer la déplace sur la frise." },
                property.FindPropertyRelative("level"));
            level.AddToClassList("forge-reward-row__level");
            level.RegisterValueChangedCallback(e =>
            {
                var clamped = Mathf.Clamp(e.newValue, 1, Draft.MaxLevel);
                if (clamped != e.newValue)
                {
                    level.value = clamped;
                    return;
                }
                // La récompense change de niveau : on la suit (frise et simulation).
                rewardLevel = clamped;
                if (view == View.Simulation)
                    openLevel = simLevel = clamped;
                level.schedule.Execute(() =>
                {
                    ProgressionEditing.SortRewards(serializedObject);
                    Rebuild();
                });
            });
            row.Add(level);
            row.Add(EnumDropdown<RewardType>("Type", property.FindPropertyRelative("type"), ProgressionEditing.RewardTypeLabel,
                "Points d'attributs, compétence ou mécanique débloquée ou améliorée, ou note.", _ => Rebuild()));
            switch (reward.type)
            {
                case RewardType.AttributePoints:
                    row.Add(Bound(new IntegerField("Points") { tooltip = "Points d'attributs donnés." }, property.FindPropertyRelative("amount")));
                    break;
                case RewardType.UnlockSkill or RewardType.UpgradeSkill:
                    row.Add(IdDropdown("Compétence", property.FindPropertyRelative("skillId"), () => Draft.Skills.Select(s => (s.id, s.displayName)).ToList(),
                        reward.type == RewardType.UnlockSkill ? "Compétence débloquée (rang 1)." : "Compétence qui passe au rang suivant (elle doit déjà être débloquée)."));
                    break;
                case RewardType.UnlockMechanic or RewardType.UpgradeMechanic:
                    row.Add(IdDropdown("Mécanique", property.FindPropertyRelative("mechanicId"), () => Draft.Mechanics.Select(m => (m.id, m.displayName)).ToList(),
                        reward.type == RewardType.UnlockMechanic ? "Mécanique débloquée (rang 1)." : "Mécanique qui passe au rang suivant (elle doit déjà être débloquée)."));
                    break;
                default:
                    row.Add(Bound(new TextField("Note") { tooltip = "Texte affiché au joueur en atteignant le niveau (contenu débloqué, événement…)." },
                        property.FindPropertyRelative("note")));
                    break;
            }
            row.Add(SmallButton("×", "Supprimer la récompense (Ctrl+Z pour annuler).", () =>
            {
                ProgressionEditing.RemoveAt(serializedObject, "rewards", index);
                Rebuild();
            }, true));
            return row;
        }

        /// <summary>Frise des niveaux : une case par niveau avec les pastilles de ses récompenses. Cliquer ouvre le niveau.</summary>
        VisualElement Timeline(PlayerProgression draft, bool compact)
        {
            var scroll = new ScrollView(ScrollViewMode.Horizontal);
            var row = new VisualElement();
            row.AddToClassList("forge-timeline");
            scroll.Add(row);
            for (var l = 1; l <= draft.MaxLevel; l++)
            {
                var level = l;
                var cell = new VisualElement { tooltip = ForgeText.System($"Niveau {l} : {ProgressionEditing.RewardSummary(draft, l)}") };
                cell.AddToClassList("forge-tl");
                cell.EnableInClassList("forge-tl--selected", !compact && l == rewardLevel);
                cell.Add(ForgeUI.Label(l.ToString(), "forge-tl__number"));
                var dots = new VisualElement { pickingMode = PickingMode.Ignore };
                dots.AddToClassList("forge-tl__dots");
                foreach (var reward in draft.Rewards.Where(r => r.level == l))
                    dots.Add(RewardPill(draft, reward));
                cell.Add(dots);
                cell.RegisterCallback<ClickEvent>(_ =>
                {
                    rewardLevel = level;
                    if (view == View.Rewards)
                        Rebuild();
                    else
                        ShowView(View.Rewards);
                });
                row.Add(cell);
            }
            return scroll;
        }

        static VisualElement Pill(string modifier, string tooltip, Color? color = null)
        {
            var pill = new VisualElement { tooltip = tooltip };
            pill.AddToClassList("forge-pill");
            if (modifier != null)
                pill.AddToClassList(modifier);
            if (color.HasValue)
                pill.style.backgroundColor = color.Value;
            return pill;
        }

        static VisualElement RewardPill(PlayerProgression draft, LevelReward reward) => reward.type switch
        {
            RewardType.AttributePoints => Pill("forge-pill--points", $"+{reward.amount} point(s) d'attribut"),
            RewardType.UnlockSkill or RewardType.UpgradeSkill => Pill("forge-pill--skill", ProgressionEditing.RewardTypeLabel(reward.type) + " : " + (draft.FindSkill(reward.skillId)?.displayName ?? "■■■")),
            RewardType.UnlockMechanic or RewardType.UpgradeMechanic => Pill("forge-pill--mechanic", ProgressionEditing.RewardTypeLabel(reward.type) + " : " + (draft.FindMechanic(reward.mechanicId)?.displayName ?? "■■■")),
            _ => Pill("forge-pill--note", string.IsNullOrWhiteSpace(reward.note) ? "Note vide" : reward.note)
        };

        static VisualElement Legend(bool withStats = false)
        {
            var legend = new VisualElement();
            legend.AddToClassList("forge-legend");
            void Item(VisualElement pill, string text)
            {
                var item = new VisualElement();
                item.Add(pill);
                item.Add(new Label(text));
                legend.Add(item);
            }
            if (withStats)
            {
                Item(Pill(null, null, new Color(0.64f, 0.65f, 0.71f)), "Stat modifiée (sa couleur)");
                Item(Pill("forge-pill--bad", null, new Color(0.64f, 0.65f, 0.71f)), "Problème");
            }
            Item(Pill("forge-pill--points", null), "Points d'attributs");
            Item(Pill("forge-pill--skill", null), "Compétence");
            Item(Pill("forge-pill--mechanic", null), "Mécanique");
            Item(Pill("forge-pill--note", null), "Note");
            return legend;
        }

        #endregion

        #region Simulation des niveaux

        float XpPerEnemy
        {
            get
            {
                if (xpPerEnemy <= 0f)
                    xpPerEnemy = ProgressionSimulation.AverageEnemyXp();
                return xpPerEnemy;
            }
        }

        void BuildSimulationView(PlayerProgression draft)
        {
            var section = ForgeUI.Section("Simulation", viewHost,
                "Le personnage tel qu'il est à un niveau : ses stats, ses actions avec leurs chiffres réels (compétences comprises), et l'effort pour y arriver. " +
                "Les points d'attributs ne sont pas investis : c'est le joueur qui les répartit.");

            var top = new VisualElement();
            top.AddToClassList("forge-sim-top");
            var levelBox = new VisualElement();
            levelBox.AddToClassList("forge-sim-level");
            var big = ForgeUI.Label(simLevel.ToString(), "forge-big");
            levelBox.Add(ForgeUI.Button("‹", "Niveau précédent.", () => SetSimLevel(simLevel - 1)));
            levelBox.Add(big);
            levelBox.Add(ForgeUI.Button("›", "Niveau suivant.", () => SetSimLevel(simLevel + 1)));
            top.Add(levelBox);
            var slider = new SliderInt(1, Mathf.Max(2, draft.MaxLevel)) { value = simLevel, tooltip = "Niveau simulé." };
            slider.RegisterValueChangedCallback(e => SetSimLevel(e.newValue));
            top.Add(slider);
            var xp = new FloatField("XP moyenne par ennemi")
            {
                isDelayed = true,
                value = XpPerEnemy,
                tooltip = "Sert à estimer le nombre d'ennemis à vaincre. Par défaut : moyenne des ennemis ordinaires du projet au premier étage."
            };
            xp.RegisterValueChangedCallback(e =>
            {
                xpPerEnemy = Mathf.Max(1f, e.newValue);
                RefreshSimulation();
            });
            top.Add(xp);
            top.Add(ForgeUI.Button("Moyenne des ennemis", "Reprend l'XP moyenne des ennemis ordinaires du projet (premier étage).", () =>
            {
                xpPerEnemy = ProgressionSimulation.AverageEnemyXp();
                xp.SetValueWithoutNotify(xpPerEnemy);
                RefreshSimulation();
            }));
            section.Add(top);

            section.Add(ForgeUI.Label("Ennemis à vaincre pour atteindre chaque niveau depuis le précédent. Cliquez sur une colonne pour simuler ce niveau.", "forge-hint"));
            var bars = new VisualElement();
            bars.AddToClassList("forge-bars");
            section.Add(bars);
            var blocks = new VisualElement();
            blocks.AddToClassList("forge-sim-blocks");
            section.Add(blocks);

            simBig = big;
            simSlider = slider;
            simBars = bars;
            simBlocks = blocks;
            refreshers.Add(RefreshSimulation);

            var levels = ForgeUI.Section("Niveau par niveau", viewHost,
                "Les pastilles montrent ce qui change : une couleur par stat modifiée (cerclée de rouge en cas de problème), puis les récompenses. " +
                "Dépliez un niveau pour modifier ses valeurs et ses récompenses.");
            levels.Add(Legend(true));
            levelRows.Clear();
            for (var l = 1; l <= draft.MaxLevel; l++)
                levels.Add(LevelRow(draft, l));
        }

        Label simBig;
        SliderInt simSlider;
        VisualElement simBars;
        VisualElement simBlocks;
        readonly Dictionary<int, VisualElement> levelRows = new();

        void SetSimLevel(int level)
        {
            simLevel = Mathf.Clamp(level, 1, Draft.MaxLevel);
            simSlider?.SetValueWithoutNotify(simLevel);
            RefreshSimulation();
        }

        void RefreshSimulation()
        {
            if (simBlocks == null || simBlocks.panel == null)
                return;
            var p = Draft;
            var sim = ProgressionSimulation.At(p, simLevel, XpPerEnemy);
            var l = sim.Level;
            simBig.text = l.ToString();
            foreach (var (level, row) in levelRows)
                row.EnableInClassList("forge-acc--current", level == l);

            // Histogramme : une colonne par niveau, les ennemis à vaincre pour l'atteindre depuis le niveau précédent.
            simBars.Clear();
            var xp = p.Find(StatRole.XpToNext);
            if (xp != null && p.MaxLevel > 1)
            {
                var perEnemy = Mathf.Max(1f, XpPerEnemy);
                // Le niveau 1 est le point de départ : rien à vaincre pour l'atteindre.
                var counts = Enumerable.Range(1, p.MaxLevel).Select(level => level == 1 ? 0 : Mathf.CeilToInt(xp.GetValue(level - 1) / perEnemy)).ToList();
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
                    bar.EnableInClassList("forge-bar--selected", level == l);
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
            else
                simBars.Add(ForgeUI.Label("Ajoutez une stat avec le rôle « XP pour le niveau suivant ».", "forge-hint"));

            simBlocks.Clear();
            // Stats au niveau simulé : valeur, écart avec le niveau précédent, position entre min et max.
            var stats = SimBlock($"Informations du personnage · niveau {l}");
            foreach (var stat in p.Stats)
            {
                var value = stat.GetValue(l);
                var delta = l > 1 ? value - stat.GetValue(l - 1) : 0f;
                var item = new VisualElement();
                item.AddToClassList("forge-sim-stat");
                var line = new VisualElement();
                line.AddToClassList("forge-sim-stat__row");
                line.Add(Dot(stat.color));
                line.Add(new Label(stat.displayName));
                line.Add(ForgeUI.Label(stat.Format(value), "forge-sim-stat__value"));
                var deltaLabel = ForgeUI.Label(l > 1 ? ProgressionSimulation.Signed(stat, delta) : "départ", "forge-delta");
                deltaLabel.EnableInClassList("forge-delta--up", delta > 0f);
                deltaLabel.EnableInClassList("forge-delta--down", delta < 0f);
                line.Add(deltaLabel);
                item.Add(line);
                var meter = new VisualElement();
                meter.AddToClassList("forge-meter");
                var fill = new VisualElement();
                fill.AddToClassList("forge-meter__fill");
                fill.style.width = Length.Percent(100f * Mathf.Clamp01(Mathf.InverseLerp(stat.min, stat.max, value)));
                fill.style.backgroundColor = stat.color;
                meter.Add(fill);
                item.Add(meter);
                stats.Add(item);
            }
            simBlocks.Add(stats);

            // Actions : chiffres réels au niveau simulé, ou niveau de déblocage.
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
            simBlocks.Add(actions);

            // Effort et récompenses.
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
            simBlocks.Add(effort);

            // Lignes « niveau par niveau » : pastilles et résumés.
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
                    var stat = p.Stats[s];

                    var delta = lv > 1 ? stat.GetValue(lv) - stat.GetValue(lv - 1) : 0f;
                    label.text = lv > 1 ? ProgressionSimulation.Signed(stat, delta) : "départ";
                    label.EnableInClassList("forge-delta--up", delta > 0f);
                    label.EnableInClassList("forge-delta--down", delta < 0f);
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
                pills.Add(Pill(problems.Contains(stat) ? "forge-pill--bad" : null,
                    level > 1 ? $"{stat.displayName} {ProgressionSimulation.Signed(stat, stat.GetValue(level) - stat.GetValue(level - 1))}" : stat.displayName, stat.color));
            var rewards = p.Rewards.Where(r => r.level == level).ToList();
            if (shown.Count > 0 && rewards.Count > 0)
            {
                var separator = new VisualElement();
                separator.AddToClassList("forge-pills__sep");
                pills.Add(separator);
            }
            foreach (var reward in rewards)
                pills.Add(RewardPill(p, reward));
        }

        VisualElement LevelRow(PlayerProgression draft, int level)
        {
            var open = level == openLevel;
            var acc = new VisualElement();
            acc.AddToClassList("forge-acc");
            acc.EnableInClassList("forge-acc--open", open);
            var head = new VisualElement { tooltip = open ? "Replier ce niveau." : "Modifier les valeurs et les récompenses de ce niveau." };
            head.AddToClassList("forge-acc__head");
            head.RegisterCallback<ClickEvent>(_ =>
            {
                openLevel = open ? -1 : level;
                simLevel = level;
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
            {
                var body = new VisualElement();
                body.AddToClassList("forge-acc__body");
                var grid = new VisualElement();
                grid.AddToClassList("forge-lvl-grid");
                var stats = serializedObject.FindProperty("stats");
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
                var rewards = new VisualElement();
                rewards.AddToClassList("forge-lvl-rewards");
                rewards.Add(ForgeUI.Label($"Récompenses du niveau {level}", "forge-hint"));
                AddLevelRewards(rewards, draft, level);
                body.Add(rewards);
                acc.Add(body);
            }
            levelRows[level] = acc;
            return acc;
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

        #endregion

        #region Champs communs

        /// <summary>Carte avec couleur et nom (attribut, compétence, mécanique) ; les boutons sont ajoutés à <paramref name="header"/>.</summary>
        static VisualElement Card(SerializedProperty property, string nameTooltip, out VisualElement header)
        {
            var card = new VisualElement();
            card.AddToClassList("forge-stat-card");
            header = Row();
            var colorField = new ColorField { showAlpha = false, tooltip = "Couleur dans la fiche du joueur." };
            colorField.AddToClassList("forge-stat-card__color");
            header.Add(Bound(colorField, property.FindPropertyRelative("color")));
            var name = new TextField { tooltip = nameTooltip };
            name.AddToClassList("forge-stat-card__name");
            header.Add(Bound(name, property.FindPropertyRelative("displayName")));
            card.Add(header);
            return card;
        }

        static VisualElement Description(SerializedProperty property, string tooltip)
        {
            var row = Row();
            var field = new TextField("Description") { tooltip = tooltip };
            field.BindProperty(property.FindPropertyRelative("description"));
            row.Add(field);
            return row;
        }

        static T Bound<T>(T field, SerializedProperty property) where T : IBindable
        {
            field.BindProperty(property);
            return field;
        }

        /// <summary>Choix d'une stat ou d'une compétence par son nom ; on enregistre son identifiant (stable même si on la renomme).</summary>
        /// <param name="emptyLabel">Choix « aucun » (identifiant vide), ou null s'il faut forcément choisir.</param>
        DropdownField IdDropdown(string label, SerializedProperty property, Func<List<(string id, string name)>> options, string tooltip, string emptyLabel = null)
        {
            var path = property.propertyPath;
            var field = new DropdownField(label) { tooltip = tooltip };
            List<(string id, string name)> current = null;
            void Sync()
            {
                current = options();
                if (emptyLabel != null)
                    current.Insert(0, (string.Empty, emptyLabel));
                field.choices = current.Select(o => o.name).ToList();
                var selected = current.FindIndex(o => o.id == serializedObject.FindProperty(path)?.stringValue);
                field.SetValueWithoutNotify(selected >= 0 ? current[selected].name : "(à choisir)");
            }
            Sync();
            refreshers.Add(Sync);
            field.RegisterValueChangedCallback(e =>
            {
                var index = field.choices.IndexOf(e.newValue);
                if (index < 0 || index >= current.Count)
                    return;
                serializedObject.Update();
                serializedObject.FindProperty(path).stringValue = current[index].id;
                serializedObject.ApplyModifiedProperties();
            });
            return field;
        }

        /// <summary>Couleur d'un nouvel élément : la palette des graphiques (8 couleurs) reprend au début une fois épuisée.</summary>
        static Color NextColor(int index) => ChartColors.Series(index % ChartColors.SeriesCount);

        static string UniqueName(IEnumerable<string> existing, string name)
        {
            var names = existing.ToHashSet();
            var candidate = name;
            for (var i = 2; names.Contains(candidate); i++)
                candidate = $"{name} {i}";
            return candidate;
        }

        #endregion

        protected override void OnRefresh(PlayerProgression draft)
        {
            if (host == null || viewHost == null || Shape(draft) != builtShape)
                return;
            selectedStat = Mathf.Clamp(selectedStat, 0, Mathf.Max(0, draft.Stats.Count - 1));
            UpdateSeg();
            SyncGeneratorStat();
            UpdatePreview(); // Rafraîchit aussi la courbe.
            foreach (var refresh in refreshers.ToList())
                refresh();
        }

        void RefreshCurve()
        {
            if (curve == null)
                return;
            if (selectedStat >= Draft.Stats.Count)
            {
                curve.SetData(string.Empty, Color.gray, Array.Empty<float>(), (0f, 1f), 0, null, null, null);
                return;
            }
            var stat = Draft.Stats[selectedStat];
            curve.SetData(stat.displayName, stat.color, stat.values, (stat.min, stat.max), stat.decimals, stat.Format,
                ProgressionEditing.GrowthViolations(stat), generator != null && generator.value ? previewValues : null);
        }

        #region Import / export

        void ExportSvg()
        {
            var path = EditorUtility.SaveFilePanel("Exporter la progression en SVG", string.Empty, $"{Draft.name}.svg", "svg");
            if (string.IsNullOrEmpty(path))
                return;
            File.WriteAllText(path, ProgressionSvg.Export(Draft));
            ForgeLists.Notify($"Exportée : {Path.GetFileName(path)}");
        }

        void ImportProgression()
        {
            var path = EditorUtility.OpenFilePanel("Importer une progression (SVG)", string.Empty, "svg");
            if (string.IsNullOrEmpty(path))
                return;
            var imported = ScriptableObject.CreateInstance<PlayerProgression>();
            try
            {
                var redrawn = ProgressionSvg.Import(File.ReadAllText(path), imported);
                OpenDraft(() => DraftSession<PlayerProgression>.CreateFrom(imported));
                ForgeLists.Notify(redrawn.Count > 0
                    ? $"Importée. Courbes retouchées reprises : {string.Join(", ", redrawn)}. Validez pour la créer."
                    : "Importée. Validez pour la créer.");
            }
            catch (Exception exception) when (exception is FormatException or ArgumentException or IOException)
            {
                ForgeDialogs.Inform("Import impossible", exception.Message);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(imported);
            }
        }

        void ImportShape()
        {
            if (selectedStat >= Draft.Stats.Count)
                return;
            var path = EditorUtility.OpenFilePanel("Importer une courbe (SVG)", string.Empty, "svg");
            if (string.IsNullOrEmpty(path))
                return;
            try
            {
                var shape = ProgressionSvg.ImportShape(File.ReadAllText(path), Draft.MaxLevel);
                var stat = Draft.Stats[selectedStat];
                var low = stat.values.DefaultIfEmpty(stat.min).Min();
                var high = stat.values.DefaultIfEmpty(stat.max).Max();
                if (high - low < 0.0001f)
                    (low, high) = (stat.min, stat.max);
                var values = shape.Select(t => Mathf.Clamp(ProgressionEditing.Round(Mathf.Lerp(low, high, t), stat.decimals), stat.min, stat.max)).ToList();
                ProgressionEditing.SetValues(serializedObject, selectedStat, values);
                ForgeLists.Notify($"Courbe importée entre {stat.Format(low)} et {stat.Format(high)} (Ctrl+Z pour annuler).");
                OnRefresh(Draft);
            }
            catch (Exception exception) when (exception is FormatException or IOException)
            {
                ForgeDialogs.Inform("Import impossible", exception.Message);
            }
        }

        #endregion
    }
}
