using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Common.Charts
{
    /// <summary>Barres horizontales triées, avec libellé, valeur et icône facultative ; les barres s'allongent à l'affichage.</summary>
    public static class BarList
    {
        public readonly struct Bar
        {
            public readonly string Label;
            public readonly float Value;
            public readonly string ValueText;
            public readonly Color Color;
            public readonly Sprite Icon;
            public readonly string Tooltip;

            public Bar(string label, float value, string valueText, Color color, Sprite icon = null, string tooltip = null)
            {
                Label = label;
                Value = value;
                ValueText = valueText;
                Color = color;
                Icon = icon;
                Tooltip = tooltip;
            }
        }

        /// <param name="scaleMax">Valeur correspondant à une barre pleine ; par défaut la plus grande valeur de la liste.</param>
        public static void Fill(VisualElement container, IEnumerable<Bar> bars, string emptyMessage = "Aucune donnée.", float? scaleMax = null)
        {
            // Longueurs précédentes, pour que les barres glissent vers leur nouvelle valeur au lieu de repartir de zéro.
            var previous = container.userData as Dictionary<string, float> ?? new Dictionary<string, float>();
            var current = new Dictionary<string, float>();
            container.userData = current;
            container.Clear();
            var list = bars.ToList();
            if (list.Count == 0)
            {
                container.Add(ForgeUI.Label(emptyMessage, "forge-hint"));
                return;
            }

            var max = Mathf.Max(0.0001f, scaleMax ?? list.Max(bar => bar.Value));
            var fills = new List<(VisualElement fill, float percent)>();
            foreach (var bar in list)
            {
                var row = new VisualElement { tooltip = bar.Tooltip ?? $"{bar.Label} : {bar.ValueText}" };
                row.AddToClassList("forge-bar-row");

                var icon = new Image { sprite = bar.Icon, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                icon.AddToClassList("forge-bar-row__icon");
                icon.style.display = bar.Icon != null ? DisplayStyle.Flex : DisplayStyle.None;
                row.Add(icon);
                row.Add(ForgeUI.Label(bar.Label, "forge-bar-row__label"));

                var track = new VisualElement();
                track.AddToClassList("forge-bar-row__track");
                var fill = new VisualElement();
                fill.AddToClassList("forge-bar-row__fill");
                fill.style.backgroundColor = bar.Color;
                fill.style.width = Length.Percent(previous.TryGetValue(bar.Label, out var before) ? before : 0f);
                track.Add(fill);
                row.Add(track);
                row.Add(ForgeUI.Label(bar.ValueText, "forge-bar-row__value"));
                container.Add(row);
                var percent = Mathf.Clamp(bar.Value / max * 100f, 1f, 100f);
                current[bar.Label] = percent;
                fills.Add((fill, percent));
            }

            // Largeur finale appliquée au tour suivant : la transition CSS anime l'allongement.
            container.schedule.Execute(() =>
            {
                foreach (var (fill, percent) in fills)
                    fill.style.width = Length.Percent(percent);
            }).StartingIn(30);
        }
    }
}
