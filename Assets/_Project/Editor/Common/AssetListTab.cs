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
    public abstract class AssetListTab<T> : IAssetNavigable, IUnsavedSource where T : ScriptableObject
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
                bindItem = BindListItem
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

        /// <summary>Ouvre une nouvelle copie de travail, après confirmation si des modifications sont en attente.</summary>
        void TryOpen(System.Func<DraftSession<T>> open)
        {
            if (!ConfirmLeave())
            {
                SyncListSelection();
                return;
            }
            session?.Dispose();
            session = open();
            ShowDraft();
            SyncListSelection();
        }

        bool ConfirmLeave()
        {
            if (session == null || !session.IsDirty)
                return true;
            // En mode batch, les modifications en attente sont abandonnées (aucune boîte modale).
            var choice = ForgeDialogs.Choose("Modifications non validées",
                $"« {GetLabel(session.Draft)} » contient des modifications non validées.",
                "Valider", "Rester ici", "Abandonner les modifications", batchAnswer: 2);
            return choice switch
            {
                0 => Commit(),
                2 => true,
                _ => false
            };
        }

        /// <summary>Ctrl+Z / Ctrl+Y : la copie de travail a changé sans passer par la fiche, on la remet à jour.</summary>
        void OnUndoRedoPerformed()
        {
            if (session == null || root?.panel == null)
                return;
            AfterUndoRedo();
        }

        /// <summary>Après une annulation : par défaut, la fiche est reconstruite à la même position de défilement.</summary>
        protected virtual void AfterUndoRedo() => KeepingScroll(ShowDraft);

        /// <summary>Exécute une mise à jour de la fiche sans qu'elle remonte en haut.</summary>
        protected void KeepingScroll(System.Action update)
        {
            var offset = details.scrollOffset;
            update();
            details.scrollOffset = offset;
            details.schedule.Execute(() => details.scrollOffset = offset).StartingIn(20);
            details.schedule.Execute(() => details.scrollOffset = offset).StartingIn(120);
        }

        public string UnsavedLabel => session == null
            ? Noun
            : $"{char.ToUpperInvariant(Noun[0])}{Noun[1..]} {ForgeText.Name(GetLabel(session.Draft))}{(session.IsNew ? " (nouveau)" : string.Empty)}";

        /// <summary>La fiche en cours est déjà affichée quand la section s'ouvre ; un onglet à plusieurs vues revient sur la bonne.</summary>
        public virtual void ShowUnsaved()
        {
        }

        /// <summary>Reconstruit la fiche (ses champs dépendent d'une valeur qui vient de changer), sans perdre le défilement.</summary>
        protected void RebuildDetails()
        {
            if (session != null)
                KeepingScroll(ShowDraft);
        }

        /// <summary>Recalcule l'en-tête, les problèmes et les éléments calculés de la fiche.</summary>
        protected void RefreshDetails() => RefreshDraft();

        /// <summary>Ouvre une copie de travail préparée par l'onglet (import…), après confirmation si des modifications sont en attente.</summary>
        protected void OpenDraft(System.Func<DraftSession<T>> open) => TryOpen(open);

        void ShowEmpty()
        {
            details.Clear();
            details.Add(ForgeUI.EmptyState($"{(FeminineNoun ? "Aucune" : "Aucun")} {Noun} pour l'instant.\nCliquez sur « + Nouveau » pour en préparer {A}."));
            footer.style.display = DisplayStyle.None;
            Window.SetUnsaved(this, false);
        }

        void ShowDraft()
        {
            details.Clear();
            footer.style.display = DisplayStyle.Flex;
            var draft = session.Draft;
            details.Add(BuildHeader());

            var serializedObject = new SerializedObject(draft);
            var body = new VisualElement();
            BuildDetails(body, serializedObject, draft);
            details.Add(body);

            var issuesSection = ForgeUI.Section("Problèmes détectés", details);
            issuesSection.name = "issues";
            issuesPanel = new IssuesPanel();
            issuesPanel.Fixed += RefreshDraft;
            issuesSection.Add(issuesPanel);

            body.Bind(serializedObject);
            body.TrackSerializedObjectValue(serializedObject, _ => ScheduleRefresh());
            RefreshDraft();
        }

        VisualElement BuildHeader()
        {
            var header = new VisualElement();
            header.AddToClassList("forge-details-header");
            detailsPreview = new Image { scaleMode = ScaleMode.ScaleToFit };
            detailsPreview.AddToClassList("forge-preview");
            header.Add(detailsPreview);

            var titles = new VisualElement();
            detailsTitle = ForgeUI.Label(string.Empty, "forge-details-title");
            titles.Add(detailsTitle);
            detailsBadges = new VisualElement();
            detailsBadges.AddToClassList("forge-row");
            titles.Add(detailsBadges);
            if (!session.IsNew)
            {
                var original = session.Original;
                var ping = ForgeUI.Button("Voir dans le projet", "Montre le fichier dans la fenêtre Project.", () => EditorGUIUtility.PingObject(original));
                ping.style.alignSelf = Align.FlexStart;
                titles.Add(ping);
            }
            header.Add(titles);
            return header;
        }

        /// <summary>Regroupe les modifications rapides (frappe au clavier) avant de revalider.</summary>
        void ScheduleRefresh()
        {
            pendingRefresh?.Pause();
            pendingRefresh = root.schedule.Execute(RefreshDraft).StartingIn(250);
        }

        void RefreshDraft()
        {
            if (session == null)
                return;
            var draft = session.Draft;
            var issues = session.Validate();
            var fileName = session.IsNew ? $"{draft.name} (non créé)" : session.Original.name;
            detailsTitle.text = $"{GetLabel(draft)}  <color=#888888><size=11>{fileName}</size></color>";
            detailsPreview.sprite = GetPreview(draft);
            detailsPreview.style.display = detailsPreview.sprite != null ? DisplayStyle.Flex : DisplayStyle.None;

            detailsBadges.Clear();
            foreach (var badge in GetBadges(draft))
                detailsBadges.Add(badge);
            issuesPanel.Show(issues);
            footer.Show(issues, session.IsNew, session.IsDirty);
            Window.SetUnsaved(this, session.IsDirty, Commit, issues.Any(issue => issue.Severity == Severity.Error));
            OnRefresh(draft);
        }

        /// <summary>Valide puis enregistre. Renvoie false si des erreurs bloquent l'enregistrement.</summary>
        bool Commit()
        {
            if (session == null)
                return true;
            var errors = session.TryCommit();
            if (errors.Count > 0)
            {
                ForgeLists.Notify($"Non enregistré : {errors.Count} erreur(s) à corriger.");
                RefreshDraft();
                details.ScrollTo(details.Q("issues"));
                return false;
            }
            ForgeLists.Notify(session.IsNew ? "Créé." : "Enregistré.");
            Reload();
            RefreshDraft();
            return true;
        }

        void Revert()
        {
            if (session == null)
                return;
            if (session.IsNew)
            {
                CloseSession();
                ApplyFilter();
                return;
            }
            session.Revert();
            ShowDraft();
        }

        void CloseSession()
        {
            session?.Dispose();
            session = null;
            Window.SetUnsaved(this, false);
        }

        void CreateNew()
        {
            if (!CanCreate(out var reason))
            {
                ForgeDialogs.Inform($"Impossible de créer {A} {Noun}", reason);
                return;
            }
            TryOpen(() =>
            {
                var created = DraftSession<T>.CreateNew(NewAssetName);
                var serialized = new SerializedObject(created.Draft);
                InitializeNew(serialized);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                return created;
            });
        }

        void DuplicateSelected()
        {
            if (session?.Original == null)
                return;
            var source = session.Original;
            TryOpen(() => DraftSession<T>.Duplicate(source));
        }

        void DeleteSelected()
        {
            if (session?.Original == null || !AssetFactory.DeleteWithConfirmation(session.Original))
                return;
            CloseSession();
            Reload();
        }
    }
}
