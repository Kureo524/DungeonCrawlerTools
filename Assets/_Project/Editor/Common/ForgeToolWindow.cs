using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Common
{
    /// <summary>Un onglet d'outil, construit à la première ouverture.</summary>
    public sealed class ForgeTab
    {
        public readonly string Label;
        public readonly string Tooltip;
        readonly Func<VisualElement> build;
        VisualElement content;

        public ForgeTab(string label, string tooltip, Func<VisualElement> build)
        {
            Label = label;
            Tooltip = tooltip;
            this.build = build;
        }

        public VisualElement Content => content ??= build();
    }

    /// <summary>
    /// Base commune des fenêtres d'outils : en-tête, barre d'onglets, onglet Help automatique et feuille de style partagée.
    /// Les fenêtres sont dockables comme Game ou Inspector, et l'onglet ouvert est conservé entre deux sessions.
    /// </summary>
    public abstract class ForgeToolWindow : EditorWindow
    {
        [SerializeField] int selectedTab;

        readonly List<ForgeTab> tabs = new();
        readonly List<Button> tabButtons = new();
        VisualElement tabContent;

        /// <summary>Phrase courte affichée sous le titre : à quoi sert l'outil.</summary>
        protected abstract string Subtitle { get; }

        /// <summary>Nom du fichier d'aide dans Editor/Help, sans extension.</summary>
        protected abstract string HelpFile { get; }

        protected abstract IEnumerable<ForgeTab> CreateTabs();

        protected ForgeDesignLimits Limits => ForgeDesignLimits.Instance;

        public void CreateGUI()
        {
            var root = rootVisualElement;
            ForgeUI.ApplyStyle(root);
            root.AddToClassList("forge-window");

            var header = new VisualElement();
            header.AddToClassList("forge-header");
            header.Add(ForgeUI.Label(titleContent.text, "forge-title"));
            header.Add(ForgeUI.Label(Subtitle, "forge-subtitle"));
            root.Add(header);

            var tabBar = new VisualElement();
            tabBar.AddToClassList("forge-tabbar");
            root.Add(tabBar);

            tabs.Clear();
            tabButtons.Clear();
            tabs.AddRange(CreateTabs());
            tabs.Add(new ForgeTab("Help", "Mode d'emploi de l'outil, pas à pas.", () => ForgeHelp.Build(HelpFile)));

            for (var i = 0; i < tabs.Count; i++)
            {
                var index = i;
                var button = new Button(() => SelectTab(index)) { text = tabs[i].Label, tooltip = tabs[i].Tooltip };
                button.AddToClassList("forge-tab");
                if (tabs[i].Label == "Help")
                    button.AddToClassList("forge-tab--help");
                tabBar.Add(button);
                tabButtons.Add(button);
            }

            tabContent = new VisualElement();
            tabContent.AddToClassList("forge-tab-content");
            root.Add(tabContent);

            SelectTab(Mathf.Clamp(selectedTab, 0, tabs.Count - 1));
        }

        protected void SelectTab(int index)
        {
            selectedTab = index;
            tabContent.Clear();
            tabContent.Add(tabs[index].Content);
            for (var i = 0; i < tabButtons.Count; i++)
                tabButtons[i].EnableInClassList("forge-tab--active", i == index);
        }
    }
}
