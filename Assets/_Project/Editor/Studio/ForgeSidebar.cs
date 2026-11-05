using System;
using System.Collections.Generic;
using System.Linq;
using TheDungeon.Editor.Common;
using TheDungeon.Editor.Validation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Studio
{
    /// <summary>
    /// Barre latérale de Forge Studio : marque, sections par groupe (nombre d'éléments et pastille d'erreur),
    /// bouton « Jouer », état de validation du projet et indicateur des modifications non validées.
    /// </summary>
    internal sealed class ForgeSidebar
    {
        readonly IReadOnlyList<ForgeSection> sections;
        readonly Action<string> showSection;
        readonly Dictionary<string, VisualElement> navItems = new();
        Label sidebarStatus;

        public ForgeSidebar(IReadOnlyList<ForgeSection> sections, Action<string> showSection, UnsavedIndicator unsaved)
        {
            this.sections = sections;
            this.showSection = showSection;
            Build(unsaved);
        }

        public VisualElement Root { get; private set; }

        /// <summary>Met en avant la section affichée.</summary>
        public void Select(string id)
        {
            foreach (var (navId, item) in navItems)
                item.EnableInClassList("forge-nav--active", navId == id);
        }

        void Build(UnsavedIndicator unsaved)
        {
            var sidebar = new VisualElement();
            Root = sidebar;
            sidebar.AddToClassList("forge-sidebar");

            var brand = new VisualElement();
            brand.AddToClassList("forge-sidebar__brand");
            var mark = ForgeUI.Label("F", "forge-sidebar__mark");
            brand.Add(mark);
            var names = new VisualElement();
            names.Add(ForgeUI.Label("Forge Studio", "forge-sidebar__logo"));
            names.Add(ForgeUI.Label("The Dungeon\nOutils de production", "forge-sidebar__tagline"));
            brand.Add(names);
            sidebar.Add(brand);

            var scroll = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            scroll.style.flexGrow = 1;
            foreach (var group in sections.GroupBy(s => s.Group))
            {
                scroll.Add(ForgeUI.Label(group.Key.ToUpperInvariant(), "forge-sidebar__group"));
                foreach (var section in group)
                    scroll.Add(BuildNavItem(section));
            }
            sidebar.Add(scroll);

            var play = ForgeUI.Button("▶   Jouer", "Lance le jeu avec toutes les données actuelles (Tools > Jouer).",
                TheDungeon.Editor.Play.GameLauncher.PlayFromStart, "forge-play-button");
            sidebar.Add(play);

            sidebarStatus = new Label { enableRichText = true, tooltip = "État de validation de tout le projet. Cliquez pour ouvrir le tableau de bord." };
            sidebarStatus.AddToClassList("forge-sidebar__status");
            sidebarStatus.AddToClassList("forge-mono");
            sidebarStatus.RegisterCallback<ClickEvent>(_ => showSection(ForgeStudioWindow.Home));
            sidebar.Add(sidebarStatus);

            // Sous l'état de validation : les modifications en attente, et un clic pour toutes les valider.
            sidebar.Add(unsaved.Label);
        }

        VisualElement BuildNavItem(ForgeSection section)
        {
            var item = new VisualElement { tooltip = section.Subtitle };
            item.AddToClassList("forge-nav");
            var indicator = new VisualElement { pickingMode = PickingMode.Ignore };
            indicator.AddToClassList("forge-nav__indicator");
            item.Add(indicator);
            var icon = new Image { sprite = LoadIcon(section.IconSprite), scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            icon.AddToClassList("forge-nav__icon");
            item.Add(icon);
            var label = new Label(section.Title) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("forge-nav__label");
            item.Add(label);
            var dot = new VisualElement { name = "dot", pickingMode = PickingMode.Ignore };
            dot.AddToClassList("forge-nav__dot");
            item.Add(dot);
            var count = new Label { name = "count", pickingMode = PickingMode.Ignore };
            count.AddToClassList("forge-nav__count");
            item.Add(count);
            item.RegisterCallback<ClickEvent>(_ => showSection(section.Id));
            navItems[section.Id] = item;
            return item;
        }

        /// <summary>Compteurs et pastilles d'erreur de la barre latérale.</summary>
        public void Refresh()
        {
            var issues = ValidationRunner.Run(ValidationContext.FromProject());
            var invalid = issues.Where(i => i.Severity == Severity.Error && i.Context != null).Select(i => i.Context).ToHashSet();
            var all = AssetFactory.LoadAll<ScriptableObject>();
            foreach (var section in sections.Where(s => s.AssetType != null))
            {
                var assets = all.Where(section.AssetType.IsInstanceOfType).ToList();
                var item = navItems[section.Id];
                item.Q<Label>("count").text = assets.Count.ToString();
                var errors = assets.Count(invalid.Contains);
                var dot = item.Q("dot");
                dot.style.display = errors > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                dot.tooltip = errors > 0 ? $"{errors} élément(s) en erreur" : null;
            }
            var errorCount = issues.Count(i => i.Severity == Severity.Error);
            var warningCount = issues.Count - errorCount;
            sidebarStatus.text = errorCount == 0 && warningCount == 0
                ? "[<color=#9ebd85>●</color> Données valides]"
                : $"[<color=#e0606a>●</color> {errorCount} erreur{(errorCount > 1 ? "s" : "")} · <color=#e7b52f>●</color> {warningCount} alerte{(warningCount > 1 ? "s" : "")}]";
        }

        public static Sprite LoadIcon(string name)
        {
            var guid = AssetDatabase.FindAssets($"{name} t:Sprite", new[] { DungeonPaths.Art }).FirstOrDefault();
            return guid != null ? AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(guid)) : null;
        }
    }
}
