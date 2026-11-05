using System;
using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Biomes;
using TheDungeon.Editor.Common;
using TheDungeon.Editor.ContentDatabase;
using TheDungeon.Editor.Dialogues;
using TheDungeon.Editor.Floors;
using TheDungeon.Editor.Progression;
using TheDungeon.Editor.RoomEditor;
using TheDungeon.Editor.Validation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace TheDungeon.Editor.Studio
{
    /// <summary>
    /// Fenêtre unique regroupant tous les outils : navigation à gauche, outil au centre, aide repliable à droite.
    /// Dockable comme Inspector ou Game ; la section ouverte et l'aide sont conservées entre deux sessions.
    /// </summary>
    public class ForgeStudioWindow : EditorWindow, IToolHost
    {
        public const string Home = "home";
        public const string Items = "items";
        public const string Enemies = "enemies";
        public const string Attacks = "attacks";
        public const string LootTables = "loot";
        public const string Progression = "progression";
        public const string Biomes = "biomes";
        public const string Rooms = "rooms";
        public const string Floors = "floors";
        public const string Dialogues = "dialogues";
        public const string Settings = "settings";

        [SerializeField] string currentSection = Home;
        [SerializeField] bool helpVisible;

        readonly List<ForgeSection> sections = new();
        readonly UnsavedChanges unsaved = new();

        VisualElement contentHost;
        VisualElement helpDrawer;
        Image headerIcon;
        Label headerTrail;
        Label headerTitle;
        Label headerSubtitle;
        Button helpButton;
        ForgeSidebar sidebar;
        UnsavedIndicator unsavedIndicator;
        // Section où chaque modification a été faite, dans l'ordre d'apparition.
        DashboardView dashboard;
        IVisualElementScheduledItem pendingRefresh;

        public event Action Closing;

        #region Menu Tools

        [MenuItem("Tools/Forge Studio", priority = 0)]
        static void OpenHome() => Open(Home);

        [MenuItem("Tools/Objets", priority = 20)]
        static void OpenItems() => Open(Items);

        [MenuItem("Tools/Ennemis", priority = 21)]
        static void OpenEnemies() => Open(Enemies);

        [MenuItem("Tools/Tables de butin", priority = 22)]
        static void OpenLoot() => Open(LootTables);

        [MenuItem("Tools/Progression du joueur", priority = 23)]
        static void OpenProgression() => Open(Progression);

        [MenuItem("Tools/Dialogues", priority = 24)]
        static void OpenDialogues() => Open(Dialogues);

        [MenuItem("Tools/Biomes", priority = 40)]
        static void OpenBiomes() => Open(Biomes);

        [MenuItem("Tools/Salles", priority = 41)]
        static void OpenRooms() => Open(Rooms);

        [MenuItem("Tools/Étages", priority = 42)]
        static void OpenFloors() => Open(Floors);

        [MenuItem("Tools/Limites (dev)", priority = 100)]
        static void OpenSettings() => Open(Settings);

        #endregion

        /// <summary>Ouvre (ou ramène au premier plan) Forge Studio sur une section, avec une taille confortable.</summary>
        public static ForgeStudioWindow Open(string sectionId)
        {
            var window = GetWindow<ForgeStudioWindow>();
            window.minSize = new Vector2(900, 560);
            const float width = 1400f;
            const float height = 860f;
            if (window.position.width < width * 0.7f || window.position.height < height * 0.7f)
            {
                var main = EditorGUIUtility.GetMainWindowPosition();
                var w = Mathf.Min(width, main.width - 40f);
                var h = Mathf.Min(height, main.height - 40f);
                window.position = new Rect(main.center.x - w / 2, main.center.y - h / 2, w, h);
            }
            window.ShowSection(sectionId);
            return window;
        }

        IEnumerable<ForgeSection> CreateSections()
        {
            yield return new ForgeSection(Home, "Vue d'ensemble", "Tableau de bord",
                "Avancement de la production par rapport au GDD et santé des données.", "Dashboard", "SPR_Orb", null,
                () => (dashboard = new DashboardView(this)).Build());
            yield return Content(Items, "Contenu", "Objets", "Armes, armures, accessoires et consommables.",
                "ContentDatabase", "SPR_LongSword", typeof(ItemData), () => new ItemsTab(this));
            yield return Content(Enemies, "Contenu", "Ennemis", "Ennemis et boss, avec leurs stats à chaque étage.",
                "ContentDatabase", "SPR_Goblin", typeof(EnemyData), () => new EnemiesTab(this));
            yield return Content(Attacks, "Contenu", "Attaques", "Attaques réutilisables : type, portée, rythme. Les ennemis les utilisent et peuvent en ajuster les valeurs.",
                "ContentDatabase", "SPR_BattleAxe", typeof(AttackData), () => new AttacksTab(this));
            yield return Content(LootTables, "Contenu", "Tables de butin", "Ce que lâchent les ennemis et les coffres.",
                "ContentDatabase", "SPR_Chest", typeof(LootTable), () => new LootTablesTab(this));
            yield return Content(Progression, "Contenu", "Progression", "Stats du joueur niveau par niveau : courbes, fonctions, import et export SVG.",
                "Progression", "SPR_PotionRed", typeof(PlayerProgression), () => new ProgressionTab(this));
            yield return Content(Dialogues, "Contenu", "Dialogues", "PNJ et arbres de dialogue, écrits comme une messagerie et jouables en aperçu.",
                "Dialogues", "SPR_ScrollBlue", typeof(NpcData), () => new DialoguesTab(this));
            yield return Content(Biomes, "Monde", "Biomes", "Presets de biome : visuels, ennemis, décors et butin. Les salles s'appuient dessus.",
                "BiomePresets", "SPR_SwampTree", typeof(BiomeData), () => new BiomesTab(this));
            yield return Content(Rooms, "Monde", "Salles", "Salles dessinées sur une grille avec la palette de leur biome.",
                "RoomEditor", "SPR_DoorClosed", typeof(RoomData), () => new RoomsTab(this));
            yield return Content(Floors, "Monde", "Étages", "Plan de chaque étage : salles reliées par leurs portes, entrée et boss.",
                "Floors", "SPR_DoorOpen", typeof(FloorData), () => new FloorsTab(this));
            yield return new ForgeSection(Settings, "Équipe technique", "Limites (dev)",
                "Bornes imposées aux outils et objectifs de production du GDD. Réservé aux développeurs.", "Settings", "SPR_Key", null,
                SettingsView.Build);
        }

        /// <summary>Section « liste + fiche » : l'outil sait aussi afficher un asset précis.</summary>
        ForgeSection Content<T>(string id, string group, string title, string subtitle, string help, string icon, Type type,
            Func<AssetListTab<T>> create) where T : ScriptableObject
        {
            ForgeSection section = null;
            section = new ForgeSection(id, group, title, subtitle, help, icon, type, () =>
            {
                var tab = create();
                section.Navigable = tab;
                return tab.Build();
            });
            return section;
        }

        public void CreateGUI()
        {
            titleContent = new GUIContent("Forge Studio", AssetDatabase.LoadAssetAtPath<Texture2D>($"{DungeonPaths.Root}/Editor/Styles/ForgeLogo.png"));
            var root = rootVisualElement;
            ForgeUI.ApplyStyle(root);
            root.AddToClassList("forge-studio");
            // Ciel de constellations derrière les panneaux en verre, et reflet qui suit le curseur.
            root.style.backgroundImage = ForgeSky.Texture;
            root.Add(new ForgeSkyAnimation());
            GlassLight.Attach(root);

            sections.Clear();
            sections.AddRange(CreateSections());

            unsavedIndicator = new UnsavedIndicator(unsaved, root, id => sections.FirstOrDefault(s => s.Id == id)?.Title ?? id, ShowUnsaved, CommitAllUnsaved);
            sidebar = new ForgeSidebar(sections, ShowSection, unsavedIndicator);
            root.Add(sidebar.Root);

            var main = new VisualElement();
            main.AddToClassList("forge-main");
            main.Add(BuildHeader());
            var body = new VisualElement();
            body.AddToClassList("forge-main__body");
            contentHost = new VisualElement();
            contentHost.AddToClassList("forge-tab-content");
            body.Add(contentHost);
            helpDrawer = new VisualElement();
            helpDrawer.AddToClassList("forge-help-drawer");
            body.Add(helpDrawer);
            main.Add(body);
            root.Add(main);

            EditorApplication.projectChanged -= ScheduleRefresh;
            EditorApplication.projectChanged += ScheduleRefresh;

            ShowSection(currentSection);
            sidebar.Refresh();
        }

        VisualElement BuildHeader()
        {
            var header = new VisualElement();
            header.AddToClassList("forge-main__header");
            headerIcon = new Image { scaleMode = ScaleMode.ScaleToFit };
            headerIcon.AddToClassList("forge-main__icon");
            header.Add(headerIcon);
            var titles = new VisualElement();
            titles.style.flexGrow = 1;
            titles.style.flexShrink = 1;
            headerTrail = ForgeUI.Label(string.Empty, "forge-breadcrumb");
            titles.Add(headerTrail);
            headerTitle = ForgeUI.Label(string.Empty, "forge-title");
            headerSubtitle = ForgeUI.Label(string.Empty, "forge-subtitle");
            titles.Add(headerTitle);
            titles.Add(headerSubtitle);
            header.Add(titles);
            helpButton = ForgeUI.Button("Aide", "Affiche ou masque le mode d'emploi de cet outil.", () =>
            {
                helpVisible = !helpVisible;
                RefreshHelp();
            }, "forge-help-toggle");
            header.Add(helpButton);
            return header;
        }

        /// <summary>Affiche une section. Les sections déjà ouvertes restent en mémoire (masquées) pour garder leur travail en cours.</summary>
        public void ShowSection(string id)
        {
            currentSection = id;
            if (contentHost == null)
                return;
            var section = sections.FirstOrDefault(s => s.Id == id) ?? sections[0];
            currentSection = section.Id;

            var content = section.Content;
            if (content.parent != contentHost)
                contentHost.Add(content);
            foreach (var child in contentHost.Children())
                child.style.display = child == content ? DisplayStyle.Flex : DisplayStyle.None;

            // Fondu d'apparition : la classe est retirée juste après, la transition CSS fait le reste.
            // Les graphiques et les barres rejouent leur animation à chaque ouverture.
            content.AddToClassList("forge-fade");
            content.schedule.Execute(() =>
            {
                content.RemoveFromClassList("forge-fade");
                TheDungeon.Editor.Common.Charts.ChartAnimation.ReplayAll(content);
            }).StartingIn(20);

            sidebar.Select(section.Id);
            headerIcon.sprite = ForgeSidebar.LoadIcon(section.IconSprite);
            headerTrail.text = $"Forge Studio  /  {section.Group}";
            headerTitle.text = section.Title;
            headerSubtitle.text = section.Subtitle;
            RefreshHelp();

            if (section.Id == Home)
                dashboard?.Refresh();
        }

        public void Reveal(Object asset)
        {
            if (asset == null)
                return;
            var section = sections.FirstOrDefault(s => s.AssetType != null && s.AssetType.IsInstanceOfType(asset));
            if (section == null)
            {
                EditorGUIUtility.PingObject(asset);
                return;
            }
            ShowSection(section.Id);
            section.Navigable?.Reveal(asset);
        }

        /// <summary>Élément racine de la section affichée (utilisé par les tests).</summary>
        public VisualElement ActiveContent => sections.FirstOrDefault(s => s.Id == currentSection)?.Content;

        void RefreshHelp()
        {
            var section = sections.FirstOrDefault(s => s.Id == currentSection);
            helpDrawer.Clear();
            helpDrawer.style.display = helpVisible ? DisplayStyle.Flex : DisplayStyle.None;
            helpButton.EnableInClassList("forge-help-toggle--active", helpVisible);
            helpButton.text = helpVisible ? "Fermer l'aide" : "Aide";
            if (helpVisible && section != null)
                helpDrawer.Add(ForgeHelp.Build(section.HelpFile));
        }

        void ScheduleRefresh()
        {
            pendingRefresh?.Pause();
            pendingRefresh = rootVisualElement.schedule.Execute(() =>
            {
                sidebar.Refresh();
                if (currentSection == Home)
                    dashboard?.Refresh();
            }).StartingIn(250);
        }

        #region Modifications non validées

        public void SetUnsaved(object owner, bool unsaved, Func<bool> commit = null, bool blocked = false)
        {
            this.unsaved.Set(owner, unsaved, commit, blocked, currentSection);
            hasUnsavedChanges = this.unsaved.Count > 0;
            saveChangesMessage = "Des modifications n'ont pas été validées. Voulez-vous les valider avant de fermer ?";
            unsavedIndicator?.Refresh();
        }

        /// <summary>Abandonne toutes les modifications non validées (fermeture sans confirmation).</summary>
        public void DiscardAllUnsaved()
        {
            unsaved.Clear();
            hasUnsavedChanges = false;
            unsavedIndicator?.Refresh();
        }

        void ShowUnsaved((object owner, string section) entry)
        {
            ShowSection(entry.section);
            (entry.owner as IUnsavedSource)?.ShowUnsaved();
        }

        /// <summary>Valide toutes les modifications en attente, seulement si aucune ne contient d'erreur.</summary>
        void CommitAllUnsaved()
        {
            if (unsaved.BlockedCount > 0)
            {
                ForgeLists.Notify($"Rien n'a été enregistré : {unsaved.BlockedCount} élément(s) contiennent des erreurs à corriger.");
                return;
            }
            var failed = unsaved.CommitAll();
            ForgeLists.Notify(failed == 0 ? "Toutes les modifications sont validées." : $"{failed} élément(s) n'ont pas pu être validés : erreurs à corriger.");
        }

        public override void SaveChanges()
        {
            if (unsaved.SaveAll())
            {
                base.SaveChanges();
                return;
            }
            // Données invalides : la fermeture est annulée pour laisser corriger les erreurs.
            ForgeDialogs.Inform("Validation impossible",
                "Les données contiennent des erreurs et n'ont pas été enregistrées. Corrigez-les ou choisissez « Discard ».");
        }

        void OnDestroy()
        {
            EditorApplication.projectChanged -= ScheduleRefresh;
            pendingRefresh?.Pause();
            Closing?.Invoke();
            Closing = null;
        }

        #endregion
    }
}
