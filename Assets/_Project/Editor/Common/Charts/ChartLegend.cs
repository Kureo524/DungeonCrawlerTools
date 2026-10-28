using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Common.Charts
{
    /// <summary>Légende : pastille de couleur + libellé (le texte garde la couleur du thème, jamais celle de la série).</summary>
    public static class ChartLegend
    {
        public static VisualElement Build(IEnumerable<(string label, Color color)> entries)
        {
            var legend = new VisualElement();
            legend.AddToClassList("forge-chart__legend");
            foreach (var (label, color) in entries)
            {
                var item = new VisualElement();
                item.AddToClassList("forge-chart__legend-item");
                var swatch = new VisualElement();
                swatch.AddToClassList("forge-chart__swatch");
                swatch.style.backgroundColor = color;
                item.Add(swatch);
                item.Add(new Label(label));
                legend.Add(item);
            }
            return legend;
        }
    }
}
