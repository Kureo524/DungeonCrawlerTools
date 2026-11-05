using System.Collections.Generic;
using System.Linq;
using TheDungeon.Editor.Validation;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Common
{
    /// <summary>
    /// Onglet « liste à gauche, fiche à droite » commun aux outils de contenu.
    /// Les modifications portent sur une copie de travail : rien n'est créé ni modifié avant « Valider ».
    /// </summary>
    public abstract partial class AssetListTab<T> : IAssetNavigable, IUnsavedSource where T : ScriptableObject
    {
        protected ForgeDesignLimits Limits => ForgeDesignLimits.Instance;
        protected IToolHost Window { get; }

        readonly List<T> filtered = new();
        readonly HashSet<Object> assetsWithErrors = new();
        readonly HashSet<Object> assetsWithWarnings = new();

        VisualElement root;
        ListView list;
        ToolbarSearchField search;
        ScrollView details;
        ValidationFooter footer;
        IssuesPanel issuesPanel;
        Label detailsTitle;
        Image detailsPreview;
        VisualElement detailsBadges;
        IVisualElementScheduledItem pendingRefresh;
        DraftSession<T> session;

        protected AssetListTab(IToolHost window)
        {
            Window = window;
        }

        /// <summary>Nom proposé à la création, ex. « Nouvel objet ».</summary>
        protected abstract string NewAssetName { get; }

        /// <summary>Nom du type au singulier, ex. « objet ».</summary>
        protected abstract string Noun { get; }

        /// <summary>Accorde les messages (« une nouvelle salle », « cette table »…).</summary>
        protected virtual bool FeminineNoun => false;

        string A => FeminineNoun ? "une" : "un";
        string This => FeminineNoun ? "cette" : "ce";

        protected abstract void BuildDetails(VisualElement container, SerializedObject serializedObject, T draft);

        protected virtual string GetLabel(T asset) =>
            asset is TheDungeon.Data.IDisplayNamed named && !string.IsNullOrWhiteSpace(named.DisplayName) ? named.DisplayName : asset.name;

        protected virtual Sprite GetPreview(T asset) => null;
        protected virtual Color GetColor(T asset) => new(0.5f, 0.5f, 0.5f);
        protected virtual IEnumerable<VisualElement> GetBadges(T asset) => Enumerable.Empty<VisualElement>();

        /// <summary>Filtres supplémentaires affichés sous la recherche.</summary>
        protected virtual VisualElement BuildFilters() => null;
        protected virtual bool PassesFilters(T asset) => true;

        /// <summary>Empêche la création quand un prérequis manque (ex : pas de biome pour une salle).</summary>
        protected virtual bool CanCreate(out string reason)
        {
            reason = null;
            return true;
        }

        /// <summary>Valeurs de départ d'une copie de travail neuve.</summary>
        protected virtual void InitializeNew(SerializedObject draft)
        {
        }

        /// <summary>Mise à jour des éléments calculés (aperçus, probabilités…) après une modification.</summary>
        protected virtual void OnRefresh(T draft)
        {
        }

        /// <summary>Largeur de la liste de gauche.</summary>
        protected virtual float ListWidth => 240;

        protected T Draft => session?.Draft;

        /// <summary>Racine de l'onglet (pour les éléments flottants, comme la copie d'une carte pendant un glisser-déposer).</summary>
        protected VisualElement Root => root;

        /// <summary>Asset enregistré correspondant à la copie de travail, ou null s'il n'est pas encore créé.</summary>
        protected T Original => session?.Original;

        /// <summary>Avant un test en jeu : seule la version enregistrée est jouée. Renvoie false si l'utilisateur annule.</summary>
        protected bool ConfirmPlaySavedVersion()
        {
            if (session == null || session.IsNew)
            {
                ForgeLists.Notify($"Validez d'abord {This} {Noun} pour pouvoir le tester.");
                return false;
            }
            return !session.IsDirty || ForgeDialogs.Confirm("Modifications non validées",
                "Le test utilise la version enregistrée : les modifications en cours n'en feront pas partie.", "Tester quand même", "Annuler");
        }

        public virtual VisualElement Build()
        {
            root = new TwoPaneSplitView(0, ListWidth, TwoPaneSplitViewOrientation.Horizontal) { viewDataKey = $"forge-split-{typeof(T).Name}" };
            root.Add(BuildListPane());

            var right = new VisualElement();
            right.AddToClassList("forge-details-pane");
            // Défilement vertical seulement : la fiche s'adapte à la largeur au lieu de déborder.
            details = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            details.AddToClassList("forge-details");
            details.style.flexGrow = 1;
            right.Add(details);
            footer = new ValidationFooter(Revert, () => Commit());
            right.Add(footer);
            root.Add(right);

            // Abonnements liés à la vie de la fenêtre, pas au détachement de l'interface (qui survient aussi à l'ancrage).
            EditorApplication.projectChanged += OnProjectChanged;
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
            Window.Closing += () =>
            {
                EditorApplication.projectChanged -= OnProjectChanged;
                Undo.undoRedoPerformed -= OnUndoRedoPerformed;
                CloseSession();
            };

            Reload();
            return root;
        }

        VisualElement BuildListPane()
        {
            var pane = new VisualElement();
            pane.AddToClassList("forge-list-pane");

            var toolbar = new VisualElement();
            toolbar.AddToClassList("forge-toolbar");
            toolbar.Add(ForgeUI.Button("+ Nouveau", FeminineNoun ? $"Prépare une nouvelle {Noun}. Elle ne sera créée qu'après validation." : $"Prépare un nouveau {Noun}. Il ne sera créé qu'après validation.", CreateNew, "forge-button--primary"));
            toolbar.Add(ForgeUI.Button("Dupliquer", FeminineNoun ? $"Prépare une copie de la {Noun} sélectionnée, à valider." : $"Prépare une copie du {Noun} sélectionné, à valider.", DuplicateSelected));
            toolbar.Add(ForgeUI.Button("Supprimer", FeminineNoun ? $"Supprime la {Noun} sélectionnée (avec confirmation)." : $"Supprime le {Noun} sélectionné (avec confirmation).", DeleteSelected, "forge-button--danger"));
            pane.Add(toolbar);

            search = ForgeUI.SearchField(new ToolbarSearchField { tooltip = "Filtre la liste par nom." });
            search.style.width = StyleKeyword.Auto;
            search.RegisterValueChangedCallback(_ => ApplyFilter());
            pane.Add(search);

            var filters = BuildFilters();
            if (filters != null)
            {
                // Les filtres sont séparés de la liste qu'ils filtrent.
                filters.AddToClassList("forge-list-filters");
                pane.Add(filters);
            }

            list = new ListView
            {
                itemsSource = filtered,
                fixedItemHeight = 32,
                selectionType = SelectionType.Single,
                makeItem = MakeListItem,
                bindItem = BindListItem,
                // Liste vide : le message est déjà dans la fiche (« Aucun … pour l'instant »), pas de « List is empty ».
                makeNoneElement = () => new VisualElement()
            };
            list.style.flexGrow = 1;
            list.selectionChanged += selection =>
            {
                var asset = selection.OfType<T>().FirstOrDefault();
                if (asset != null && asset != session?.Original)
                    TryOpen(() => DraftSession<T>.Edit(asset));
            };
            pane.Add(list);
            return pane;
        }

        static VisualElement MakeListItem()
        {
            // La ligne occupe toute la hauteur réservée ; son contenu (et son surlignage) est un peu plus fin,
            // ce qui laisse un espace entre deux éléments de la liste.
            var item = new VisualElement();
            item.AddToClassList("forge-list-item__slot");
            var row = new VisualElement();
            row.AddToClassList("forge-list-item");
            item.Add(row);
            var strip = new VisualElement { name = "strip" };
            strip.AddToClassList("forge-list-item__strip");
            var icon = new Image { name = "icon", scaleMode = ScaleMode.ScaleToFit };
            icon.AddToClassList("forge-list-item__icon");
            var label = new Label { name = "label" };
            label.AddToClassList("forge-list-item__label");
            var warning = new Label { name = "warning" };
            warning.AddToClassList("forge-list-item__warning");
            row.Add(strip);
            row.Add(icon);
            row.Add(label);
            row.Add(warning);
            return item;
        }

        void BindListItem(VisualElement row, int index)
        {
            var asset = filtered[index];
            row.Q("strip").style.backgroundColor = GetColor(asset);
            var preview = GetPreview(asset);
            var icon = row.Q<Image>("icon");
            icon.sprite = preview;
            icon.style.display = preview != null ? DisplayStyle.Flex : DisplayStyle.None;
            row.Q<Label>("label").text = GetLabel(asset);

            var warning = row.Q<Label>("warning");
            var hasError = assetsWithErrors.Contains(asset);
            var hasWarning = assetsWithWarnings.Contains(asset);
            warning.EnableInClassList("forge-list-item__warning--error", hasError);
            warning.EnableInClassList("forge-list-item__warning--warning", !hasError && hasWarning);
            row.tooltip = asset.name + (hasError ? "\nContient des erreurs." : hasWarning ? "\nContient des avertissements." : string.Empty);
        }

        /// <summary>Affiche un asset précis (depuis le tableau de bord ou un autre outil).</summary>
        public virtual void Reveal(Object asset)
        {
            if (asset is not T typed || typed == session?.Original)
                return;
            if (search != null && !string.IsNullOrEmpty(search.value))
                search.value = string.Empty;
            TryOpen(() => DraftSession<T>.Edit(typed));
        }

        void OnProjectChanged()
        {
            // L'asset en cours d'édition a été supprimé depuis l'extérieur.
            if (session != null && !session.IsNew && session.Original == null)
                CloseSession();
            Reload();
        }

        /// <summary>Recharge la liste depuis le projet (création, suppression, renommage…).</summary>
        protected void Reload()
        {
            RefreshIssueMarkers();
            ApplyFilter();
        }

        protected void ApplyFilter()
        {
            var text = search?.value?.Trim() ?? string.Empty;
            filtered.Clear();
            filtered.AddRange(AssetFactory.LoadAll<T>().Where(asset =>
                PassesFilters(asset) &&
                (text.Length == 0 || asset.name.Contains(text, System.StringComparison.OrdinalIgnoreCase) ||
                 GetLabel(asset).Contains(text, System.StringComparison.OrdinalIgnoreCase))));
            list.Rebuild();

            if (session == null)
            {
                var first = filtered.FirstOrDefault();
                if (first != null)
                    TryOpen(() => DraftSession<T>.Edit(first));
                else
                    ShowEmpty();
            }
            SyncListSelection();
        }

        void SyncListSelection()
        {
            var index = session?.Original != null ? filtered.IndexOf(session.Original) : -1;
            if (index >= 0)
                list.SetSelectionWithoutNotify(new[] { index });
            else
                list.ClearSelection();
        }

        void RefreshIssueMarkers()
        {
            assetsWithErrors.Clear();
            assetsWithWarnings.Clear();
            foreach (var issue in ValidationRunner.Run(ValidationContext.FromProject()))
            {
                if (issue.Context == null)
                    continue;
                (issue.Severity == Severity.Error ? assetsWithErrors : assetsWithWarnings).Add(issue.Context);
            }
        }
    }
}
