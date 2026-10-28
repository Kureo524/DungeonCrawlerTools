using System;
using UnityEditor;
using UnityEngine;

namespace TheDungeon.Editor.Common.Charts
{
    /// <summary>
    /// Couleurs des graphiques. Palette catégorielle validée (daltonisme, contraste) en version claire et sombre,
    /// choisie selon le thème de l'éditeur. Les couleurs de statut sont réservées aux états, jamais aux séries.
    /// </summary>
    public static class ChartColors
    {
        static readonly Color[] Light = Parse("#2a78d6", "#eb6834", "#1baf7a", "#eda100", "#e87ba4", "#008300", "#6250d6", "#e34948");
        static readonly Color[] Dark = Parse("#3987e5", "#d95926", "#199e70", "#c98500", "#d55181", "#008300", "#9085e9", "#e66767");

        public const int SeriesCount = 8;

        public static readonly Color Good = Hex("#0ca30c");
        public static readonly Color Warning = Hex("#fab219");
        public static readonly Color Critical = Hex("#d03b3b");

        static bool IsDark => EditorGUIUtility.isProSkin;

        /// <summary>Couleur de la n-ième série, dans un ordre fixe. Au-delà de 8 séries, regrouper en « Autres ».</summary>
        public static Color Series(int index)
        {
            if (index < 0 || index >= SeriesCount)
                throw new ArgumentOutOfRangeException(nameof(index), "Au-delà de 8 séries, regroupez les suivantes dans « Autres ».");
            return (IsDark ? Dark : Light)[index];
        }

        /// <summary>Gris neutre pour « Rien », « Autres » et les séries de comparaison.</summary>
        public static Color Neutral => IsDark ? new Color(0.55f, 0.55f, 0.53f) : new Color(0.6f, 0.6f, 0.58f);

        public static Color Surface => IsDark ? new Color(0.22f, 0.22f, 0.22f) : new Color(0.78f, 0.78f, 0.78f);
        public static Color Grid => IsDark ? new Color(1f, 1f, 1f, 0.07f) : new Color(0f, 0f, 0f, 0.09f);
        public static Color Axis => IsDark ? new Color(1f, 1f, 1f, 0.22f) : new Color(0f, 0f, 0f, 0.25f);
        public static Color Track => IsDark ? new Color(1f, 1f, 1f, 0.08f) : new Color(0f, 0f, 0f, 0.1f);

        public static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var color) ? color : Color.magenta;

        static Color[] Parse(params string[] hexes) => Array.ConvertAll(hexes, Hex);
    }
}
