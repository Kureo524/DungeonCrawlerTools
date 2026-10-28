using System;
using System.Collections.Generic;
using System.Linq;
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

        /// <summary>
        /// Ouvre (ou ramène au premier plan) la fenêtre avec une taille confortable.
        /// La fenêtre reste dockable : on peut la glisser à côté de l'Inspector ou de la Scene.
        /// </summary>
        protected static T Open<T>(string title, float width = 1100, float height = 720) where T : ForgeToolWindow
        {
            var window = GetWindow<T>(title);
            window.minSize = new Vector2(Mathf.Min(width, 760), Mathf.Min(height, 520));
            if (window.position.width < width * 0.8f || window.position.height < height * 0.8f)
            {
                var main = EditorGUIUtility.GetMainWindowPosition();
                window.position = new Rect(main.center.x - width / 2, main.center.y - height / 2, width, height);
            }
            return window;
        }

        readonly Dictionary<object, Func<bool>> pendingCommits = new();

        /// <summary>
        /// Signale à Unity des modifications non validées : à la fermeture, Unity propose de les valider.
        /// Chaque onglet (owner) déclare son propre état.
        /// </summary>
        /// <param name="commit">Tente la validation ; renvoie false si les données contiennent des erreurs.</param>
        public void SetUnsaved(object owner, bool unsaved, Func<bool> commit = null)
        {
            if (unsaved)
                pendingCommits[owner] = commit;
            else
                pendingCommits.Remove(owner);
            hasUnsavedChanges = pendingCommits.Count > 0;
            saveChangesMessage = "Des modifications n'ont pas été validées. Voulez-vous les valider avant de fermer ?";
        }

        public override void SaveChanges()
        {
            var allSaved = true;
            foreach (var commit in pendingCommits.Values.ToList())
                allSaved &= commit == null || commit();
            if (allSaved)
            {
                pendingCommits.Clear();
                base.SaveChanges();
                return;
            }
            // Données invalides : la fermeture est annulée pour laisser corriger les erreurs.
            EditorUtility.DisplayDialog("Validation impossible",
                "Les données contiennent des erreurs et n'ont pas été enregistrées. Corrigez-les ou choisissez « Discard ».", "OK");
        }

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

        /// <summary>Les onglets déjà ouverts restent en mémoire (masqués) pour conserver leur travail en cours.</summary>
        protected void SelectTab(int index)
        {
            selectedTab = index;
            var content = tabs[index].Content;
            if (content.parent != tabContent)
                tabContent.Add(content);
            foreach (var child in tabContent.Children())
                child.style.display = child == content ? DisplayStyle.Flex : DisplayStyle.None;
            for (var i = 0; i < tabButtons.Count; i++)
                tabButtons[i].EnableInClassList("forge-tab--active", i == index);
        }
    }
}
