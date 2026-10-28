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
    public abstract class AssetListTab<T> where T : ScriptableObject
    {
        protected ForgeDesignLimits Limits => ForgeDesignLimits.Instance;
        protected ForgeToolWindow Window { get; }

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

        protected AssetListTab(ForgeToolWindow window)
        {
            Window = window;
        }

        /// <summary>Nom proposé à la création, ex. « Nouvel objet ».</summary>
        protected abstract string NewAssetName { get; }

        /// <summary>Nom du type au singulier, ex. « objet ».</summary>
        protected abstract string Noun { get; }

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

        public VisualElement Build()
        {
            root = new TwoPaneSplitView(0, ListWidth, TwoPaneSplitViewOrientation.Horizontal) { viewDataKey = $"forge-split-{typeof(T).Name}" };
            root.Add(BuildListPane());

            var right = new VisualElement();
            right.AddToClassList("forge-details-pane");
            details = new ScrollView();
            details.AddToClassList("forge-details");
            details.style.flexGrow = 1;
            right.Add(details);
            footer = new ValidationFooter(Revert, () => Commit());
            right.Add(footer);
            root.Add(right);

            root.RegisterCallback<AttachToPanelEvent>(_ => EditorApplication.projectChanged += OnProjectChanged);
            root.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                EditorApplication.projectChanged -= OnProjectChanged;
                session?.Dispose();
                session = null;
                Window.SetUnsaved(this, false);
            });

            Reload();
            return root;
        }

        VisualElement BuildListPane()
        {
            var pane = new VisualElement();
            pane.AddToClassList("forge-list-pane");

            var toolbar = new VisualElement();
            toolbar.AddToClassList("forge-toolbar");
            toolbar.Add(ForgeUI.Button("+ Nouveau", $"Prépare un nouveau {Noun}. Il ne sera créé qu'après validation.", CreateNew, "forge-button--primary"));
            toolbar.Add(ForgeUI.Button("Dupliquer", $"Prépare une copie du {Noun} sélectionné, à valider.", DuplicateSelected));
            toolbar.Add(ForgeUI.Button("Supprimer", $"Supprime le {Noun} sélectionné (avec confirmation).", DeleteSelected, "forge-button--danger"));
            pane.Add(toolbar);

            search = new ToolbarSearchField { tooltip = "Filtre la liste par nom." };
            search.style.width = StyleKeyword.Auto;
            search.RegisterValueChangedCallback(_ => ApplyFilter());
            pane.Add(search);

            var filters = BuildFilters();
            if (filters != null)
                pane.Add(filters);

            list = new ListView
            {
                itemsSource = filtered,
                fixedItemHeight = 26,
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
            var row = new VisualElement();
            row.AddToClassList("forge-list-item");
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
            return row;
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
            warning.text = hasError || hasWarning ? "!" : string.Empty;
            warning.style.color = hasError ? new Color(0.86f, 0.27f, 0.27f) : new Color(0.94f, 0.67f, 0.24f);
            row.tooltip = asset.name + (hasError ? "\nContient des erreurs." : hasWarning ? "\nContient des avertissements." : string.Empty);
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
            var choice = EditorUtility.DisplayDialogComplex("Modifications non validées",
                $"« {GetLabel(session.Draft)} » contient des modifications non validées.",
                "Valider", "Rester ici", "Abandonner les modifications");
            return choice switch
            {
                0 => Commit(),
                2 => true,
                _ => false
            };
        }

        void ShowEmpty()
        {
            details.Clear();
            details.Add(ForgeUI.EmptyState($"Aucun {Noun} pour l'instant.\nCliquez sur « + Nouveau » pour en préparer un."));
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
            detailsBadges.Clear();
            foreach (var badge in GetBadges(draft))
                detailsBadges.Add(badge);
            issuesPanel.Show(issues);
            footer.Show(issues, session.IsNew, session.IsDirty);
            Window.SetUnsaved(this, session.IsDirty, Commit);
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
                EditorUtility.DisplayDialog($"Impossible de créer un {Noun}", reason, "OK");
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
