using System;
using System.Linq;
using TheDungeon.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Common
{
    /// <summary>Barres horizontales des probabilités d'une table de butin (avec bonus de biome éventuel).</summary>
    public static class ProbabilityBars
    {
        static readonly Color NothingColor = new(0.45f, 0.45f, 0.45f);

        public static void Fill(VisualElement container, LootTable table, Func<Rarity, float> rarityMultiplier, ForgeDesignLimits limits)
        {
            container.Clear();
            if (table == null)
                return;

            var rows = table.Entries
                .Select(entry => (label: entry.item != null ? entry.item.DisplayName : "(objet manquant)",
                    probability: table.GetProbability(entry, rarityMultiplier),
                    color: entry.item != null ? limits.GetRarity(entry.item.Rarity).color : Color.red))
                .Append(("Rien", table.Entries.Count == 0 ? 1f : 1f - table.DropChance, NothingColor))
                .OrderByDescending(row => row.Item2)
                .ToList();

            var max = Mathf.Max(0.0001f, rows.Max(row => row.Item2));
            foreach (var (label, probability, color) in rows)
                container.Add(Bar(label, probability, probability / max, color));
        }

        static VisualElement Bar(string label, float probability, float fill, Color color)
        {
            var row = new VisualElement { tooltip = $"{label} : {probability:P1} de chance à chaque tirage." };
            row.AddToClassList("forge-bar-row");
            var name = ForgeUI.Label(label, "forge-bar-row__label");
            var track = new VisualElement();
            track.AddToClassList("forge-bar-row__track");
            var bar = new VisualElement();
            bar.AddToClassList("forge-bar-row__fill");
            bar.style.width = Length.Percent(fill * 100f);
            bar.style.backgroundColor = color;
            track.Add(bar);
            row.Add(name);
            row.Add(track);
            row.Add(ForgeUI.Label($"{probability:P1}", "forge-bar-row__value"));
            return row;
        }
    }
}
