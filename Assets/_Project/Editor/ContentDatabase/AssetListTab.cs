using System.Collections.Generic;
using System.Linq;
using TheDungeon.Editor.Common;
using TheDungeon.Editor.Validation;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.ContentDatabase
{
    /// <summary>
    /// Onglet « liste à gauche, fiche à droite » commun aux objets, ennemis et tables de butin.
    /// Gère la recherche, la création, la duplication, la suppression et l'affichage des problèmes.
    /// </summary>
    public abstract class AssetListTab<T> where T : ScriptableObject
    {
        protected ForgeDesignLimits Limits => ForgeDesignLimits.Instance;

        readonly List<T> filtered = new();
        readonly HashSet<Object> assetsWithErrors = new();
        readonly HashSet<Object> assetsWithWarnings = new();

        VisualElement root;
        ListView list;
        ToolbarSearchField search;
        ScrollView details;
        IssuesPanel issuesPanel;
        Label detailsTitle;
        Image detailsPreview;
        VisualElement detailsBadges;
        IVisualElementScheduledItem pendingRefresh;
        T selected;

        /// <summary>Nom proposé à la création, ex. « Nouvel objet ».</summary>
        protected abstract string NewAssetName { get; }

        /// <summary>Nom du type au singulier, ex. « objet ».</summary>
        protected abstract string Noun { get; }

        protected abstract void BuildDetails(VisualElement container, SerializedObject serializedObject, T asset);

        protected virtual string GetLabel(T asset) => asset.name;
        protected virtual Sprite GetPreview(T asset) => null;
        protected virtual Color GetColor(T asset) => new(0.5f, 0.5f, 0.5f);
        protected virtual IEnumerable<VisualElement> GetBadges(T asset) => Enumerable.Empty<VisualElement>();

        /// <summary>Filtres supplémentaires affichés sous la recherche.</summary>
        protected virtual VisualElement BuildFilters() => null;
        protected virtual bool PassesFilters(T asset) => true;

        protected T Selected => selected;

        public VisualElement Build()
        {
            root = new TwoPaneSplitView(0, 240, TwoPaneSplitViewOrientation.Horizontal) { viewDataKey = $"forge-split-{typeof(T).Name}" };
            root.Add(BuildListPane());

            details = new ScrollView();
            details.AddToClassList("forge-details");
            root.Add(details);

            root.RegisterCallback<AttachToPanelEvent>(_ => EditorApplication.projectChanged += Reload);
            root.RegisterCallback<DetachFromPanelEvent>(_ => EditorApplication.projectChanged -= Reload);

            Reload();
            return root;
        }

        VisualElement BuildListPane()
        {
            var pane = new VisualElement();
            pane.AddToClassList("forge-list-pane");

            var toolbar = new VisualElement();
            toolbar.AddToClassList("forge-toolbar");
            toolbar.Add(ForgeUI.Button("+ Nouveau", $"Crée un {Noun}, déjà nommé et rangé au bon endroit.", CreateNew, "forge-button--primary"));
            toolbar.Add(ForgeUI.Button("Dupliquer", $"Copie le {Noun} sélectionné pour en faire une variante.", DuplicateSelected));
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
                fixedItemHeight = 24,
                selectionType = SelectionType.Single,
                makeItem = MakeListItem,
                bindItem = BindListItem
            };
            list.style.flexGrow = 1;
            list.selectionChanged += selection => Select(selection.OfType<T>().FirstOrDefault());
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
            row.tooltip = $"{asset.name}" + (hasError ? "\nContient des erreurs." : hasWarning ? "\nContient des avertissements." : string.Empty);
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

            if (selected == null || !filtered.Contains(selected))
                Select(filtered.FirstOrDefault());
            else
                list.SetSelectionWithoutNotify(new[] { filtered.IndexOf(selected) });
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

        void Select(T asset)
        {
            selected = asset;
            details.Clear();
            if (asset == null)
            {
                details.Add(ForgeUI.EmptyState($"Aucun {Noun} sélectionné.\nCliquez sur « + Nouveau » pour en créer un."));
                return;
            }

            var index = filtered.IndexOf(asset);
            if (index >= 0)
                list.SetSelectionWithoutNotify(new[] { index });

            details.Add(BuildHeader(asset));

            var serializedObject = new SerializedObject(asset);
            var body = new VisualElement();
            BuildDetails(body, serializedObject, asset);
            details.Add(body);

            var issuesSection = ForgeUI.Section("Problèmes détectés", details);
            issuesPanel = new IssuesPanel();
            issuesPanel.Fixed += Reload;
            issuesSection.Add(issuesPanel);

            body.Bind(serializedObject);
            body.TrackSerializedObjectValue(serializedObject, _ => ScheduleRefresh());
            RefreshSelected();
        }

        VisualElement BuildHeader(T asset)
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
            var ping = ForgeUI.Button("Voir dans le projet", "Montre le fichier dans la fenêtre Project.", () => EditorGUIUtility.PingObject(asset));
            ping.style.alignSelf = Align.FlexStart;
            titles.Add(ping);
            header.Add(titles);
            return header;
        }

        /// <summary>Regroupe les modifications rapides (frappe au clavier) avant de revalider.</summary>
        protected void ScheduleRefresh()
        {
            pendingRefresh?.Pause();
            pendingRefresh = root.schedule.Execute(() =>
            {
                RefreshIssueMarkers();
                list.RefreshItems();
                RefreshSelected();
            }).StartingIn(300);
        }

        void RefreshSelected()
        {
            if (selected == null)
                return;
            detailsTitle.text = $"{GetLabel(selected)}  <color=#888888><size=11>{selected.name}</size></color>";
            detailsPreview.sprite = GetPreview(selected);
            detailsBadges.Clear();
            foreach (var badge in GetBadges(selected))
                detailsBadges.Add(badge);
            issuesPanel.Show(ValidationRunner.RunFor(selected));
            OnRefresh(selected);
        }

        /// <summary>Mise à jour des éléments calculés (aperçus, probabilités…) après une modification.</summary>
        protected virtual void OnRefresh(T asset)
        {
        }

        /// <summary>Renomme le fichier selon la convention, au tour suivant (après application des valeurs).</summary>
        protected void ScheduleRename(Object asset)
        {
            root.schedule.Execute(() =>
            {
                var error = AssetFactory.ApplyNamingConvention(asset);
                if (error != null)
                    ForgeLists.Notify(error);
            });
        }

        void CreateNew()
        {
            var asset = AssetFactory.Create<T>(NewAssetName);
            OnCreated(asset);
            selected = asset;
            search.value = string.Empty;
            Reload();
        }

        /// <summary>Valeurs de départ d'un asset fraîchement créé.</summary>
        protected virtual void OnCreated(T asset)
        {
        }

        void DuplicateSelected()
        {
            if (selected == null)
                return;
            var copy = AssetFactory.Duplicate(selected);
            if (copy == null)
                return;
            selected = copy;
            Reload();
        }

        void DeleteSelected()
        {
            if (selected == null || !AssetFactory.DeleteWithConfirmation(selected))
                return;
            selected = null;
            Reload();
        }
    }
}
