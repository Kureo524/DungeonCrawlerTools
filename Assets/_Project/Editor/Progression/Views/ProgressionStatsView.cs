using System;
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
    /// <summary>Stats et courbes : liste des stats, courbe modifiable à la souris, réglages de la stat et générateur par fonction.</summary>
    internal sealed class ProgressionStatsView : ProgressionView
    {
        const float CurveHeight = 300f;

        CurveEditor curve;
        ProgressionGeneratorView generator;
        int undoGroup;

        public ProgressionStatsView(ProgressionContext context) : base(context)
        {
        }

        public override void Build(VisualElement parent, PlayerProgression draft)
        {
            var split = new VisualElement();
            split.AddToClassList("forge-split");
            parent.Add(split);
            if (!State.Expanded)
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
            if (State.Expanded)
                return;

            var settings = new Foldout { text = "Réglages de la stat", value = State.SettingsOpen };
            settings.AddToClassList("forge-section");
            settings.AddToClassList("forge-fold");
            settings.RegisterValueChangedCallback(e =>
            {
                if (e.target == settings)
                    State.SettingsOpen = e.newValue;
            });
            settings.Add(StatCard(Serialized.FindProperty("stats").GetArrayElementAtIndex(State.SelectedStat), State.SelectedStat, draft.Stats[State.SelectedStat]));
            main.Add(settings);
            generator = new ProgressionGeneratorView(Context, RefreshCurve);
            generator.Build(main, draft);
        }

        public override void Refresh()
        {
            if (generator == null)
            {
                RefreshCurve();
                return;
            }
            generator.SyncWithSelectedStat();
            generator.UpdatePreview(); // Rafraîchit aussi la courbe.
        }

        VisualElement BuildStatList(PlayerProgression draft)
        {
            var list = new VisualElement();
            list.AddToClassList("forge-section");
            list.AddToClassList("forge-stat-list");
            list.Add(ForgeUI.Label(ForgeText.WindowRich("Stats"), "forge-section-title"));
            list.Add(ForgeUI.Label("Choisissez une stat : sa courbe et ses réglages s'affichent à côté.", "forge-section-description"));
            for (var i = 0; i < draft.Stats.Count; i++)
                list.Add(StatRow(draft, i));
            list.Add(AddStatButton(draft));
            return list;
        }

        VisualElement StatRow(PlayerProgression draft, int index)
        {
            var row = new VisualElement { tooltip = "Modifier cette stat." };
            row.AddToClassList("forge-srow");
            row.EnableInClassList("forge-srow--selected", index == State.SelectedStat);
            var dot = Dot(draft.Stats[index].color);
            row.Add(dot);
            var text = new VisualElement { pickingMode = PickingMode.Ignore };
            text.AddToClassList("forge-srow__text");
            var name = ForgeUI.Label(draft.Stats[index].displayName, "forge-srow__name");
            var meta = ForgeUI.Label(string.Empty, "forge-srow__meta");
            text.Add(name);
            text.Add(meta);
            row.Add(text);
            var spark = new Sparkline();
            row.Add(spark);
            row.RegisterCallback<ClickEvent>(_ =>
            {
                State.SelectedStat = index;
                Rebuild();
            });
            AddRefresher(() =>
            {
                if (index >= Draft.Stats.Count)
                    return;
                var stat = Draft.Stats[index];
                dot.style.backgroundColor = stat.color;
                name.text = stat.displayName;
                meta.text = stat.resource ? $"Jauge, +{stat.regenPerSecond:0.#}/s" : ProgressionLabels.RoleLabel(stat.role);
                spark.SetData(stat.values, stat.color);
            });
            return row;
        }

        Button AddStatButton(PlayerProgression draft)
        {
            var add = ForgeUI.Button("+ Ajouter une stat", $"Ajoute une stat libre (maximum {Limits.maxProgressionStats}).", () =>
            {
                State.SelectedStat = ProgressionEditing.AddStat(Serialized, new ProgressionStat
                {
                    displayName = UniqueName(Draft.Stats.Select(s => s.displayName), "Nouvelle stat"),
                    color = NextColor(Draft.Stats.Count),
                    min = 0f,
                    max = 100f
                }, "Linéaire", 0f, 10f);
                State.SettingsOpen = true;
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
            section.Add(CurveHeader(draft));

            var expanded = State.Expanded;
            curve = new CurveEditor(expanded ? Mathf.Max(460f, (Context.Host.panel?.visualTree.layout.height ?? 0f) - 330f) : CurveHeight);
            curve.EditStarted += () =>
            {
                Undo.IncrementCurrentGroup();
                undoGroup = Undo.GetCurrentGroup();
            };
            curve.Edited += values =>
            {
                if (State.SelectedStat < Draft.Stats.Count)
                    ProgressionEditing.SetValues(Serialized, State.SelectedStat, values);
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

        /// <summary>Pastille, nom et plage de la stat, import d'une forme SVG et bouton pour agrandir la courbe.</summary>
        VisualElement CurveHeader(PlayerProgression draft)
        {
            var head = new VisualElement();
            head.AddToClassList("forge-curve-head");
            var dot = Dot(draft.Stats[State.SelectedStat].color);
            head.Add(dot);
            var title = ForgeUI.Label(draft.Stats[State.SelectedStat].displayName, "forge-section-title");
            head.Add(title);
            var range = ForgeUI.Label(string.Empty, "forge-curve-head__range");
            range.AddToClassList("forge-mono");
            head.Add(range);
            head.Add(ForgeUI.Button("Importer une courbe",
                "Remplace la courbe par la forme d'une courbe SVG (dessinée dans Inkscape, Figma…), entre ses valeurs actuelles la plus basse et la plus haute.",
                ImportShape));
            var expanded = State.Expanded;
            head.Add(ForgeUI.Button(expanded ? "Réduire" : "Agrandir",
                expanded ? "Revient à la liste des stats et aux réglages." : "Agrandit la courbe pour la retoucher plus finement.", () =>
                {
                    State.Expanded = !State.Expanded;
                    Rebuild();
                }));
            AddRefresher(() =>
            {
                if (State.SelectedStat >= Draft.Stats.Count)
                    return;
                var stat = Draft.Stats[State.SelectedStat];
                dot.style.backgroundColor = stat.color;
                title.text = stat.displayName;
                range.text = stat.values.Count > 0
                    ? $"[{stat.Format(stat.values[0])} → {stat.Format(stat.values[^1])}, bornes {stat.Format(stat.min)} à {stat.Format(stat.max)}]"
                    : string.Empty;
            });
            return head;
        }

        void RefreshCurve()
        {
            if (curve == null)
                return;
            if (State.SelectedStat >= Draft.Stats.Count)
            {
                curve.SetData(string.Empty, Color.gray, Array.Empty<float>(), (0f, 1f), 0, null, null, null);
                return;
            }
            var stat = Draft.Stats[State.SelectedStat];
            curve.SetData(stat.displayName, stat.color, stat.values, (stat.min, stat.max), stat.decimals, stat.Format,
                ProgressionEditing.GrowthViolations(stat), generator != null && generator.IsOpen ? generator.PreviewValues : null);
        }

        void ImportShape()
        {
            if (ProgressionImportExport.ImportShape(Serialized, Draft, State.SelectedStat))
                Context.Refresh();
        }

        VisualElement StatCard(SerializedProperty property, int index, ProgressionStat stat)
        {
            var card = new VisualElement();
            card.AddToClassList("forge-stat-card");
            card.Add(StatHeader(property, index));
            card.Add(StatDisplay(property, stat));
            card.Add(StatBounds(property, stat));
            card.Add(StatGauge(property, stat));
            return card;
        }

        /// <summary>Couleur, nom, rôle, ordre dans la liste et suppression.</summary>
        VisualElement StatHeader(SerializedProperty property, int index)
        {
            var header = Row();
            var color = new ColorField { showAlpha = false, tooltip = "Couleur de la courbe et de la barre en jeu." };
            color.AddToClassList("forge-stat-card__color");
            color.BindProperty(property.FindPropertyRelative("color"));
            header.Add(color);
            var name = new TextField { tooltip = "Nom affiché au joueur et dans les outils." };
            name.AddToClassList("forge-stat-card__name");
            name.BindProperty(property.FindPropertyRelative("displayName"));
            header.Add(name);
            header.Add(EnumDropdown<StatRole>("Rôle", property.FindPropertyRelative("role"), ProgressionLabels.RoleLabel,
                "Ce que le jeu fait de la stat. Les rôles ont des bornes fixées par l'équipe technique.", role => OnRoleChanged(index, role)));
            header.Add(SmallButton("▲", "Monter la stat dans la liste.", () => Move(index, -1), index > 0));
            header.Add(SmallButton("▼", "Descendre la stat dans la liste.", () => Move(index, 1), index < Draft.Stats.Count - 1));
            header.Add(SmallButton("Supprimer", "Supprime la stat et sa courbe (Ctrl+Z pour annuler).", () => RemoveStat(index), true, "forge-button--danger"));
            return header;
        }

        /// <summary>Affichage au joueur : visibilité, présentation et décimales.</summary>
        VisualElement StatDisplay(SerializedProperty property, ProgressionStat stat)
        {
            var display = Row();
            var format = EnumDropdown<StatFormat>("Présentation", property.FindPropertyRelative("format"), ProgressionLabels.FormatLabel,
                "Nombre (12), barre qui se remplit, ou pourcentage (12 %).");
            format.SetEnabled(stat.visibility != StatVisibility.Hidden);
            display.Add(EnumDropdown<StatVisibility>("Affichage joueur", property.FindPropertyRelative("visibility"), ProgressionLabels.VisibilityLabel,
                "Masquée, toujours à l'écran (HUD) ou seulement dans la fiche du personnage (inventaire).",
                visibility => format.SetEnabled(visibility != StatVisibility.Hidden)));
            display.Add(format);
            var decimals = new SliderInt("Décimales", 0, 3) { showInputField = true, tooltip = "0 = valeurs entières. Les points glissés sont arrondis en conséquence." };
            decimals.BindProperty(property.FindPropertyRelative("decimals"));
            display.Add(decimals);
            return display;
        }

        /// <summary>Bornes de la stat (rappel des limites de son rôle) et sens d'évolution attendu.</summary>
        VisualElement StatBounds(SerializedProperty property, ProgressionStat stat)
        {
            var bounds = Row();
            var limits = ProgressionEditing.Bounds(stat.role, Limits);
            var boundsTip = limits.HasValue ? $"\nLimites de l'équipe technique pour ce rôle : {limits.Value.min} à {limits.Value.max}." : string.Empty;
            var min = new FloatField("Min") { tooltip = "Valeur minimale à tous les niveaux. C'est aussi le bas de l'axe de la courbe." + boundsTip };
            min.BindProperty(property.FindPropertyRelative("min"));
            var max = new FloatField("Max") { tooltip = "Valeur maximale à tous les niveaux. C'est aussi le haut de l'axe de la courbe et le repère d'une barre." + boundsTip };
            max.BindProperty(property.FindPropertyRelative("max"));
            bounds.Add(min);
            bounds.Add(max);
            bounds.Add(EnumDropdown<StatGrowth>("Évolution", property.FindPropertyRelative("growth"), ProgressionLabels.GrowthLabel,
                "Contrôle du Validator : une valeur qui baisse (ou stagne) est signalée en rouge."));
            return bounds;
        }

        /// <summary>Jauge consommable (endurance, mana…) et sa recharge.</summary>
        static VisualElement StatGauge(SerializedProperty property, ProgressionStat stat)
        {
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
            return gauge;
        }

        /// <summary>Un rôle impose ses bornes : min, max et valeurs sont ramenés dans les limites de l'équipe technique.</summary>
        void OnRoleChanged(int index, StatRole role)
        {
            var bounds = ProgressionEditing.Bounds(role, Limits);
            if (!bounds.HasValue)
                return;
            Serialized.Update();
            var stat = Serialized.FindProperty("stats").GetArrayElementAtIndex(index);
            var min = stat.FindPropertyRelative("min");
            var max = stat.FindPropertyRelative("max");
            min.floatValue = Mathf.Clamp(min.floatValue, bounds.Value.min, bounds.Value.max);
            max.floatValue = Mathf.Clamp(max.floatValue, bounds.Value.min, bounds.Value.max);
            if (max.floatValue <= min.floatValue)
                (min.floatValue, max.floatValue) = (bounds.Value.min, bounds.Value.max);
            var values = stat.FindPropertyRelative("values");
            for (var i = 0; i < values.arraySize; i++)
                values.GetArrayElementAtIndex(i).floatValue = Mathf.Clamp(values.GetArrayElementAtIndex(i).floatValue, min.floatValue, max.floatValue);
            Serialized.ApplyModifiedProperties();
            Rebuild();
        }

        void Move(int index, int offset)
        {
            ProgressionEditing.MoveStat(Serialized, index, offset);
            if (State.SelectedStat == index)
                State.SelectedStat += offset;
            Rebuild();
        }

        void RemoveStat(int index)
        {
            var stat = Draft.Stats[index];
            if (stat.role != StatRole.None && !ForgeDialogs.Confirm("Supprimer une stat utilisée par le jeu",
                    $"« {stat.displayName} » joue le rôle « {ProgressionLabels.RoleLabel(stat.role)} ». Sans elle, la progression ne pourra pas être validée " +
                    "tant qu'une autre stat ne reprend pas ce rôle.", "Supprimer", "Annuler"))
                return;
            ProgressionEditing.RemoveStat(Serialized, index);
            if (State.SelectedStat >= index)
                State.SelectedStat = Mathf.Max(0, State.SelectedStat - 1);
            Rebuild();
        }
    }
}
