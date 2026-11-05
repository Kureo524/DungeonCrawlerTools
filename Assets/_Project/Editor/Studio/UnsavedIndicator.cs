using System;
using System.Linq;
using TheDungeon.Editor.Common;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Studio
{
    /// <summary>
    /// Indicateur des modifications non validées, en bas de la barre latérale. Clic : affiche la suivante.
    /// Appui long : liste des modifications et « Tout valider » ; relâcher sur une ligne la choisit.
    /// </summary>
    internal sealed class UnsavedIndicator
    {
        const long LongPressMs = 400;

        readonly UnsavedChanges changes;
        readonly VisualElement root;
        readonly Func<string, string> sectionTitle;
        readonly Action<(object owner, string section)> show;
        readonly Action commitAll;
        int lastShown = -1;
        VisualElement menu;
        IVisualElementScheduledItem longPress;

        /// <param name="root">Racine de la fenêtre, où s'ouvre la liste.</param>
        /// <param name="show">Affiche une modification (sa section, puis l'élément).</param>
        public UnsavedIndicator(UnsavedChanges changes, VisualElement root, Func<string, string> sectionTitle,
            Action<(object owner, string section)> show, Action commitAll)
        {
            this.changes = changes;
            this.root = root;
            this.sectionTitle = sectionTitle;
            this.show = show;
            this.commitAll = commitAll;
            Label = new Label { enableRichText = true };
            Label.AddToClassList("forge-sidebar__status");
            Label.AddToClassList("forge-sidebar__unsaved");
            Label.AddToClassList("forge-mono");
            RegisterPointer(Label);
            Refresh();
        }

        public Label Label { get; }

        public void Refresh()
        {
            var count = changes.Count;
            Label.style.display = count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            var blocked = changes.BlockedCount > 0;
            Label.EnableInClassList("forge-sidebar__unsaved--blocked", blocked);
            var what = $"{count} élément{(count > 1 ? "s" : "")} non validé{(count > 1 ? "s" : "")}";
            // L'action sur sa propre ligne : la barre latérale est trop étroite pour tout tenir sur une seule.
            Label.text = $"[<color={(blocked ? "#e0606a" : "#a597c0")}>●</color> {what}]\n<color=#e8eefa><b>Voir ›</b></color>  (maintenir : liste)";
            Label.tooltip = "Clic : affiche la modification (la suivante à chaque clic).\n" +
                            "Maintenir : liste des modifications et « Tout valider » ; relâchez sur une ligne pour la choisir." +
                            (blocked ? "\nCertaines modifications contiennent des erreurs : « Tout valider » est bloqué." : string.Empty);
        }

        /// <summary>Clic : aller à une modification. Appui long : liste ; relâcher sur une ligne la choisit.</summary>
        void RegisterPointer(VisualElement indicator)
        {
            indicator.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                    return;
                indicator.CapturePointer(evt.pointerId);
                longPress?.Pause();
                longPress = indicator.schedule.Execute(OpenMenu).StartingIn(LongPressMs);
            });
            indicator.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (menu != null)
                    HighlightMenuRow(evt.position);
            });
            indicator.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!indicator.HasPointerCapture(evt.pointerId))
                    return;
                indicator.ReleasePointer(evt.pointerId);
                longPress?.Pause();
                if (menu == null)
                {
                    ShowNext();
                    return;
                }
                var row = MenuRowAt(evt.position);
                CloseMenu();
                (row?.userData as Action)?.Invoke();
            });
            indicator.RegisterCallback<PointerCaptureOutEvent>(_ =>
            {
                longPress?.Pause();
                CloseMenu();
            });
        }

        void ShowNext()
        {
            if (changes.Entries.Count == 0)
                return;
            lastShown = (lastShown + 1) % changes.Entries.Count;
            show(changes.Entries[lastShown]);
        }

        /// <summary>Panneau au-dessus de l'indicateur : une ligne par modification, puis « Tout valider ».</summary>
        void OpenMenu()
        {
            CloseMenu();
            if (changes.Entries.Count == 0)
                return;
            menu = new VisualElement { pickingMode = PickingMode.Ignore };
            menu.AddToClassList("forge-unsaved-menu");
            menu.Add(ForgeUI.Label(ForgeText.Window("Modifications non validées"), "forge-unsaved-menu__title"));
            foreach (var entry in changes.Entries)
            {
                var label = (entry.owner as IUnsavedSource)?.UnsavedLabel ?? entry.section;
                var section = sectionTitle(entry.section);
                var dot = changes.IsBlocked(entry.owner) ? "#e0606a" : "#a597c0";
                var captured = entry;
                menu.Add(MenuRow($"<color={dot}>●</color> {label}  <color=#8a8a9c>{section}</color>", () => show(captured)));
            }
            var blocked = changes.BlockedCount > 0;
            var commitRow = MenuRow(blocked ? "<color=#e0606a>Tout valider : erreurs à corriger</color>" : "<b>Tout valider ›</b>", commitAll);
            commitRow.AddToClassList("forge-unsaved-menu__commit");
            menu.Add(commitRow);

            var anchor = root.WorldToLocal(Label.worldBound);
            menu.style.position = Position.Absolute;
            menu.style.left = anchor.x;
            menu.style.bottom = root.layout.height - anchor.yMin + 6;
            menu.style.minWidth = Mathf.Max(anchor.width, 280);
            // Opaque : le panneau passe au-dessus du bouton « Jouer » et de l'état de validation.
            menu.style.backgroundColor = new Color(0.10f, 0.11f, 0.16f, 1f);
            root.Add(menu);
        }

        static VisualElement MenuRow(string text, Action action)
        {
            var row = new VisualElement { userData = action, pickingMode = PickingMode.Ignore };
            row.AddToClassList("forge-unsaved-menu__row");
            var label = ForgeUI.Label(text);
            label.pickingMode = PickingMode.Ignore;
            row.Add(label);
            return row;
        }

        VisualElement MenuRowAt(Vector2 position) =>
            menu?.Query(className: "forge-unsaved-menu__row").Where(row => row.worldBound.Contains(position)).First();

        void HighlightMenuRow(Vector2 position)
        {
            var hovered = MenuRowAt(position);
            menu.Query(className: "forge-unsaved-menu__row").ForEach(row => row.EnableInClassList("forge-unsaved-menu__row--hover", row == hovered));
        }

        void CloseMenu()
        {
            menu?.RemoveFromHierarchy();
            menu = null;
        }
    }
}
