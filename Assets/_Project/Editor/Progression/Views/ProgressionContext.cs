using System;
using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using UnityEditor;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Progression
{
    /// <summary>
    /// Ce que les vues de l'onglet Progression partagent : la fiche éditée, l'état de navigation, les mises à jour légères
    /// et les demandes de reconstruction adressées à l'onglet.
    /// </summary>
    internal sealed class ProgressionContext
    {
        readonly Func<PlayerProgression> draft;
        readonly Action rebuild;
        readonly Action<ProgressionCategory> showView;
        readonly Action refresh;
        // Mises à jour légères après chaque modification (noms, résumés, simulation), sans reconstruire la catégorie.
        readonly List<Action> refreshers = new();

        public ProgressionContext(Func<PlayerProgression> draft, Action rebuild, Action<ProgressionCategory> showView, Action refresh)
        {
            this.draft = draft;
            this.rebuild = rebuild;
            this.showView = showView;
            this.refresh = refresh;
        }

        public PlayerProgression Draft => draft();
        public SerializedObject Serialized { get; set; }
        public ForgeDesignLimits Limits => ForgeDesignLimits.Instance;
        public ProgressionState State { get; } = new();

        /// <summary>Racine de la fiche : sert à connaître la hauteur disponible.</summary>
        public VisualElement Host { get; set; }

        /// <summary>Reconstruit la catégorie affichée après un changement de structure.</summary>
        public void Rebuild() => rebuild();

        public void ShowView(ProgressionCategory category) => showView(category);

        /// <summary>Rafraîchit la catégorie affichée sans la reconstruire.</summary>
        public void Refresh() => refresh();

        public void AddRefresher(Action refresher) => refreshers.Add(refresher);

        public void ClearRefreshers() => refreshers.Clear();

        public void RunRefreshers()
        {
            foreach (var refresher in refreshers.ToList())
                refresher();
        }
    }

    /// <summary>État de navigation de l'onglet, conservé quand une catégorie est reconstruite.</summary>
    internal sealed class ProgressionState
    {
        const string ViewKey = "forge.progression.view";

        ProgressionCategory view = (ProgressionCategory)SessionState.GetInt(ViewKey, 0);
        float xpPerEnemy = -1f;

        public int SelectedStat;
        public int OpenMechanic = -1;
        public int RewardLevel = -1;
        public int SimLevel = 1;
        public int OpenLevel = -1;
        public bool SettingsOpen = true;
        public bool GeneratorOpen;
        public bool Expanded;

        /// <summary>Catégorie affichée, retenue pour la session de l'éditeur.</summary>
        public ProgressionCategory View
        {
            get => view;
            set
            {
                view = value;
                SessionState.SetInt(ViewKey, (int)value);
            }
        }

        /// <summary>XP moyenne d'un ennemi pour la simulation ; par défaut, la moyenne des ennemis du projet.</summary>
        public float XpPerEnemy
        {
            get
            {
                if (xpPerEnemy <= 0f)
                    xpPerEnemy = ProgressionSimulation.AverageEnemyXp();
                return xpPerEnemy;
            }
            set => xpPerEnemy = value;
        }
    }
}
