using System.Collections.Generic;
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
        static readonly Color InactiveColor = new(0.55f, 0.55f, 0.6f);

        readonly ProgressionContext context;
        VisualElement host;
        VisualElement viewHost;
        VisualElement segDrop;
        readonly Dictionary<ProgressionCategory, Button> segButtons = new();
        readonly Dictionary<ProgressionCategory, Label> segCounts = new();
        ProgressionView currentView;
        string builtShape;

        public ProgressionTab(IToolHost window) : base(window)
        {
            context = new ProgressionContext(() => Draft, Rebuild, ShowView, () => OnRefresh(Draft));
        }

        ProgressionState State => context.State;

        protected override string NewAssetName => "Nouvelle progression";
        protected override string Noun => "progression";
        protected override bool FeminineNoun => true;

        internal static bool IsActive(PlayerProgression progression) => progression != null && progression == GameLauncher.ActiveProgression;

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
                "Crée une progression à partir d'un fichier SVG exporté par Forge Studio (éventuellement retouché dans Inkscape ou Figma).",
                () => ProgressionImportExport.ImportProgression(imported => OpenDraft(() => DraftSession<PlayerProgression>.CreateFrom(imported))));
            import.style.marginTop = 4;
            import.style.marginBottom = 12;
            return import;
        }

        protected override void InitializeNew(SerializedObject draft) => ProgressionDefaults.Reset(draft, Limits);

        protected override void BuildDetails(VisualElement container, SerializedObject serialized, PlayerProgression draft)
        {
            context.Serialized = serialized;
            host = new VisualElement();
            host.RegisterCallback<AttachToPanelEvent>(_ => Undo.undoRedoPerformed += OnUndoRedo);
            host.RegisterCallback<DetachFromPanelEvent>(_ => Undo.undoRedoPerformed -= OnUndoRedo);
            container.Add(host);
            context.Host = host;
            State.OpenLevel = -1;
            host.Add(BuildSeg());
            viewHost = new VisualElement();
            host.Add(viewHost);
            BuildView();
        }

        /// <summary>Reconstruit la catégorie affichée après un changement de structure (élément ajouté, supprimé, catégorie changée…).</summary>
        void Rebuild()
        {
            context.Serialized.Update();
            viewHost.Clear();
            BuildView();
            viewHost.Bind(context.Serialized);
            OnRefresh(Draft);
        }

        void OnUndoRedo()
        {
            var serialized = context.Serialized;
            if (serialized == null || serialized.targetObject == null)
                return;
            serialized.Update();
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
            State.SelectedStat = Mathf.Clamp(State.SelectedStat, 0, Mathf.Max(0, draft.Stats.Count - 1));
            State.SimLevel = Mathf.Clamp(State.SimLevel, 1, Mathf.Max(1, draft.MaxLevel));
            context.ClearRefreshers();
            currentView = CreateView(State.View);
            currentView.Build(viewHost, draft);
            UpdateSeg();
        }

        ProgressionView CreateView(ProgressionCategory category) => category switch
        {
            ProgressionCategory.Stats => new ProgressionStatsView(context),
            ProgressionCategory.Mechanics => new ProgressionMechanicsView(context),
            ProgressionCategory.Growth => new ProgressionAttributesView(context),
            ProgressionCategory.Rewards => new ProgressionRewardsView(context),
            ProgressionCategory.Simulation => new ProgressionSimulationView(context),
            _ => new ProgressionOverviewView(context, () => Original, ActivateInGame, () => ProgressionImportExport.ExportSvg(Draft))
        };

        /// <summary>Le jeu suivra la progression enregistrée (une seule progression est jouée à la fois).</summary>
        void ActivateInGame()
        {
            GameLauncher.ActiveProgression = Original;
            ForgeLists.Notify($"« {Original.DisplayName} » est maintenant utilisée par le jeu.");
            Reload();
            Rebuild();
            RefreshDetails();
        }

        protected override void OnRefresh(PlayerProgression draft)
        {
            if (host == null || viewHost == null || Shape(draft) != builtShape)
                return;
            State.SelectedStat = Mathf.Clamp(State.SelectedStat, 0, Mathf.Max(0, draft.Stats.Count - 1));
            UpdateSeg();
            currentView?.Refresh();
            context.RunRefreshers();
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
            foreach (var (target, label) in ProgressionCategories.All)
            {
                var button = new Button(() => ShowView(target)) { tooltip = $"Afficher « {label} »." };
                button.AddToClassList("forge-seg__item");
                button.Add(new Label(label) { pickingMode = PickingMode.Ignore });
                var count = new Label { pickingMode = PickingMode.Ignore };
                count.AddToClassList("forge-seg__count");
                button.Add(count);
                button.RegisterCallback<GeometryChangedEvent>(_ =>
                {
                    if (target == State.View)
                        PlaceDrop();
                });
                segButtons[target] = button;
                segCounts[target] = count;
                seg.Add(button);
            }
            return seg;
        }

        void ShowView(ProgressionCategory target)
        {
            State.View = target;
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
                button.EnableInClassList("forge-seg__item--selected", target == State.View);
            segCounts[ProgressionCategory.Stats].text = draft.Stats.Count.ToString();
            segCounts[ProgressionCategory.Mechanics].text = draft.Mechanics.Count.ToString();
            segCounts[ProgressionCategory.Growth].text = (draft.Attributes.Count + draft.Skills.Count).ToString();
            segCounts[ProgressionCategory.Rewards].text = draft.Rewards.Count.ToString();
            PlaceDrop();
        }

        /// <summary>La goutte de verre glisse sous la catégorie choisie (transition USS sur sa position et sa largeur).</summary>
        void PlaceDrop()
        {
            if (segDrop?.parent == null || !segButtons.TryGetValue(State.View, out var button) || float.IsNaN(button.worldBound.width))
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
    }
}
