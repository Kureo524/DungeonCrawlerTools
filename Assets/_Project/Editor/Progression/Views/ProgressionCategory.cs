using System.Linq;

namespace TheDungeon.Editor.Progression
{
    /// <summary>Catégories de l'onglet Progression, dans l'ordre du contrôle segmenté.</summary>
    internal enum ProgressionCategory
    {
        Overview,
        Stats,
        Mechanics,
        Growth,
        Rewards,
        Simulation
    }

    internal static class ProgressionCategories
    {
        public static readonly (ProgressionCategory category, string label)[] All =
        {
            (ProgressionCategory.Overview, "Aperçu"), (ProgressionCategory.Stats, "Stats et courbes"), (ProgressionCategory.Mechanics, "Mécaniques"),
            (ProgressionCategory.Growth, "Attributs et compétences"), (ProgressionCategory.Rewards, "Récompenses"),
            (ProgressionCategory.Simulation, "Simulation des niveaux")
        };

        public static string Label(ProgressionCategory category) => All.First(c => c.category == category).label;
    }
}
