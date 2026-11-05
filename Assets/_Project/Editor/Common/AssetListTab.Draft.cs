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
    /// Copie de travail de l'onglet : ouverture (avec confirmation si des modifications sont en attente), fiche, validation,
    /// annulation, création, duplication et suppression.
    /// </summary>
    public abstract partial class AssetListTab<T> where T : ScriptableObject
    {
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
