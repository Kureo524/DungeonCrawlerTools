using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.ContentDatabase
{
    /// <summary>Tableau des niveaux du joueur, avec génération à partir d'une formule et validation avant enregistrement.</summary>
    public sealed class ProgressionTab
    {
        readonly ForgeToolWindow window;

        // Paramètres du générateur (valeurs de départ raisonnables, modifiables dans l'onglet).
        int baseHealth = 50;
        float healthGrowth = 0.12f;
        int baseDamage = 5;
        float damageGrowth = 0.10f;
        int baseXp = 100;
        float xpGrowth = 1.25f;

        VisualElement root;
        ScrollView content;
        ValidationFooter footer;
        VisualElement table;
        IssuesPanel issuesPanel;
        DraftSession<PlayerProgression> session;
        SerializedObject serializedObject;
        IVisualElementScheduledItem pendingRefresh;

        public ProgressionTab(ForgeToolWindow window)
        {
            this.window = window;
        }

        ForgeDesignLimits Limits => ForgeDesignLimits.Instance;

        public VisualElement Build()
        {
            root = new VisualElement();
            root.AddToClassList("forge-details-pane");
            content = new ScrollView();
            content.AddToClassList("forge-details");
            content.style.flexGrow = 1;
            root.Add(content);
            footer = new ValidationFooter(Revert, () => Commit());
            root.Add(footer);
            root.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                session?.Dispose();
                session = null;
                window.SetUnsaved(this, false);
            });

            var existing = AssetFactory.LoadAll<PlayerProgression>().FirstOrDefault();
            if (existing != null)
                Open(DraftSession<PlayerProgression>.Edit(existing));
            else
                ShowEmpty();
            return root;
        }

        void ShowEmpty()
        {
            content.Clear();
            footer.style.display = DisplayStyle.None;
            content.Add(ForgeUI.EmptyState("La progression du joueur n'existe pas encore."));
            var create = ForgeUI.Button($"Préparer la progression ({Limits.maxLevel} niveaux)",
                "Prépare la progression avec la formule par défaut. Elle sera créée après validation.", () =>
                {
                    Open(DraftSession<PlayerProgression>.CreateNew("Player"));
                    Generate();
                }, "forge-button--primary");
            create.style.alignSelf = Align.Center;
            content.Add(create);
        }

        void Open(DraftSession<PlayerProgression> newSession)
        {
            session?.Dispose();
            session = newSession;
            serializedObject = new SerializedObject(session.Draft);
            footer.style.display = DisplayStyle.Flex;
            Rebuild();
        }

        void Rebuild()
        {
            content.Clear();
            serializedObject.Update();
            content.Add(BuildGenerator());

            var tableSection = ForgeUI.Section($"Niveaux 1 à {Limits.maxLevel}", content);
            var levels = serializedObject.FindProperty("levels");
            if (levels.arraySize != Limits.maxLevel)
            {
                tableSection.Add(new HelpBox($"La progression contient {levels.arraySize} niveaux au lieu de {Limits.maxLevel}.", HelpBoxMessageType.Warning));
                tableSection.Add(ForgeUI.Button($"Ajuster à {Limits.maxLevel} niveaux", "Ajoute ou retire des lignes en fin de tableau.", () =>
                {
                    levels.arraySize = Limits.maxLevel;
                    serializedObject.ApplyModifiedProperties();
                    Rebuild();
                }));
            }
            table = new VisualElement();
            tableSection.Add(table);
            BuildTable(levels);

            var issuesSection = ForgeUI.Section("Problèmes détectés", content);
            issuesSection.name = "issues";
            issuesPanel = new IssuesPanel();
            issuesSection.Add(issuesPanel);

            content.TrackSerializedObjectValue(serializedObject, _ =>
            {
                pendingRefresh?.Pause();
                pendingRefresh = root.schedule.Execute(Refresh).StartingIn(250);
            });
            Refresh();
        }

        VisualElement BuildGenerator()
        {
            var foldout = new Foldout { text = "Générer depuis une formule", value = session.IsNew, tooltip = "Remplit tout le tableau en un clic à partir de quelques paramètres." };
            foldout.AddToClassList("forge-section");

            var health = new IntegerField("PV au niveau 1") { value = baseHealth, tooltip = "Points de vie du joueur au niveau 1." };
            health.RegisterValueChangedCallback(e => baseHealth = Limits.playerHealth.Clamp(e.newValue));
            var healthRate = new Slider("Croissance PV / niveau", 0f, 0.5f) { value = healthGrowth, showInputField = true, tooltip = "0.12 = +12 % de PV à chaque niveau." };
            healthRate.RegisterValueChangedCallback(e => healthGrowth = e.newValue);
            var damage = new IntegerField("Dégâts au niveau 1") { value = baseDamage, tooltip = "Dégâts du joueur au niveau 1." };
            damage.RegisterValueChangedCallback(e => baseDamage = Limits.playerDamage.Clamp(e.newValue));
            var damageRate = new Slider("Croissance dégâts / niveau", 0f, 0.5f) { value = damageGrowth, showInputField = true, tooltip = "0.10 = +10 % de dégâts à chaque niveau." };
            damageRate.RegisterValueChangedCallback(e => damageGrowth = e.newValue);
            var xp = new IntegerField("XP du niveau 1 au 2") { value = baseXp, tooltip = "XP nécessaire pour passer du niveau 1 au niveau 2." };
            xp.RegisterValueChangedCallback(e => baseXp = Limits.xpRequired.Clamp(e.newValue));
            var xpRate = new Slider("Multiplicateur XP / niveau", 1f, 2f) { value = xpGrowth, showInputField = true, tooltip = "1.25 = chaque niveau demande 25 % d'XP de plus que le précédent." };
            xpRate.RegisterValueChangedCallback(e => xpGrowth = e.newValue);

            foreach (var field in new VisualElement[] { health, healthRate, damage, damageRate, xp, xpRate })
                foldout.Add(field);

            foldout.Add(ForgeUI.Button("Générer les niveaux",
                "Remplace PV, dégâts et XP de tous les niveaux (les déblocages sont conservés). Rien n'est enregistré avant « Valider ».",
                Generate, "forge-button--primary"));
            return foldout;
        }

        void Generate()
        {
            var levels = serializedObject.FindProperty("levels");
            levels.arraySize = Limits.maxLevel;
            for (var i = 0; i < levels.arraySize; i++)
            {
                var level = levels.GetArrayElementAtIndex(i);
                level.FindPropertyRelative("maxHealth").intValue = Limits.playerHealth.Clamp(Mathf.RoundToInt(baseHealth * Mathf.Pow(1f + healthGrowth, i)));
                level.FindPropertyRelative("damage").intValue = Limits.playerDamage.Clamp(Mathf.RoundToInt(baseDamage * Mathf.Pow(1f + damageGrowth, i)));
                level.FindPropertyRelative("xpRequired").intValue = Limits.xpRequired.Clamp(Mathf.RoundToInt(baseXp * Mathf.Pow(xpGrowth, i)));
            }
            serializedObject.ApplyModifiedProperties();
            Rebuild();
        }

        void BuildTable(SerializedProperty levels)
        {
            var header = new VisualElement();
            header.AddToClassList("forge-table-row");
            foreach (var title in new[] { "Niveau", "PV max", "Dégâts", "XP requise" })
                header.Add(ForgeUI.Label(title, "forge-table-header"));
            var unlockTitle = ForgeUI.Label("Déblocage", "forge-table-header");
            unlockTitle.AddToClassList("forge-table-wide");
            header.Add(unlockTitle);
            table.Add(header);

            for (var i = 0; i < levels.arraySize; i++)
            {
                var level = levels.GetArrayElementAtIndex(i);
                var row = new VisualElement { name = $"level-{i}" };
                row.AddToClassList("forge-table-row");
                row.Add(ForgeUI.Label((i + 1).ToString()));
                row.Add(ClampedField(level.FindPropertyRelative("maxHealth"), Limits.playerHealth, "Points de vie maximum à ce niveau."));
                row.Add(ClampedField(level.FindPropertyRelative("damage"), Limits.playerDamage, "Dégâts de base à ce niveau."));
                row.Add(ClampedField(level.FindPropertyRelative("xpRequired"), Limits.xpRequired, "XP pour passer au niveau suivant."));
                var unlock = new TextField { tooltip = "Capacité ou contenu débloqué à ce niveau (facultatif)." };
                unlock.AddToClassList("forge-table-wide");
                unlock.BindProperty(level.FindPropertyRelative("unlock"));
                row.Add(unlock);
                table.Add(row);
            }
        }

        /// <summary>Champ numérique qui ramène toute saisie hors limites dans les bornes autorisées.</summary>
        static IntegerField ClampedField(SerializedProperty property, IntRange range, string tooltip)
        {
            var field = new IntegerField { tooltip = $"{tooltip}\nValeurs autorisées : {range}." };
            field.BindProperty(property);
            field.RegisterValueChangedCallback(evt =>
            {
                if (range.Contains(evt.newValue))
                    return;
                field.value = range.Clamp(evt.newValue);
                ForgeLists.Notify($"Valeur ramenée entre {range.min} et {range.max}.");
            });
            return field;
        }

        void Refresh()
        {
            if (session == null)
                return;
            var levels = session.Draft.Levels;
            for (var i = 0; i < levels.Count; i++)
            {
                var row = table.Q($"level-{i}");
                if (row == null)
                    continue;
                var decreasing = i > 0 && (levels[i].maxHealth < levels[i - 1].maxHealth || levels[i].damage < levels[i - 1].damage ||
                                           levels[i].xpRequired <= levels[i - 1].xpRequired);
                row.style.backgroundColor = decreasing ? new Color(0.86f, 0.27f, 0.27f, 0.2f) : StyleKeyword.Null;
                row.tooltip = decreasing ? "Une valeur baisse par rapport au niveau précédent." : null;
            }
            var issues = session.Validate();
            issuesPanel.Show(issues);
            footer.Show(issues, session.IsNew, session.IsDirty);
            window.SetUnsaved(this, session.IsDirty, Commit);
        }

        bool Commit()
        {
            var errors = session.TryCommit();
            if (errors.Count > 0)
            {
                ForgeLists.Notify($"Non enregistré : {errors.Count} erreur(s) à corriger.");
                Refresh();
                content.ScrollTo(content.Q("issues"));
                return false;
            }
            ForgeLists.Notify("Progression enregistrée.");
            Refresh();
            return true;
        }

        void Revert()
        {
            if (session.IsNew)
            {
                session.Dispose();
                session = null;
                window.SetUnsaved(this, false);
                ShowEmpty();
                return;
            }
            session.Revert();
            serializedObject = new SerializedObject(session.Draft);
            Rebuild();
        }
    }
}
