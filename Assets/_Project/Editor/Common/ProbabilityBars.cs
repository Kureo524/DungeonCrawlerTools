using System;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common.Charts;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Common
{
    /// <summary>Probabilités d'une table de butin (avec bonus de biome éventuel) : barres par objet et anneau par rareté.</summary>
    public static class ProbabilityBars
    {
        public static void Fill(VisualElement container, LootTable table, Func<int, float> rarityMultiplier, ForgeDesignLimits limits)
        {
            if (table == null)
            {
                container.Clear();
                return;
            }
            var nothing = table.Entries.Count == 0 ? 1f : 1f - table.DropChance;
            var bars = table.Entries
                .Select(entry => new BarList.Bar(
                    entry.item != null ? entry.item.DisplayName : "(objet manquant)",
                    table.GetProbability(entry, rarityMultiplier),
                    $"{table.GetProbability(entry, rarityMultiplier):P1}",
                    entry.item != null ? limits.GetRarity(entry.item.RarityId).color : ChartColors.Critical,
                    entry.item != null ? entry.item.Icon : null,
                    entry.item != null
                        ? $"{entry.item.DisplayName} ({limits.GetRarity(entry.item.RarityId).DisplayName}) : {table.GetProbability(entry, rarityMultiplier):P1} de chance à chaque tirage."
                        : "Objet manquant ou supprimé."))
                .Append(new BarList.Bar("Rien", nothing, $"{nothing:P1}", ChartColors.Neutral, null, $"Aucun objet : {nothing:P1}"))
                .OrderByDescending(bar => bar.Value);
            BarList.Fill(container, bars);
        }

        /// <summary>Probabilité totale par rareté (plus « Rien »), pour un anneau de répartition.</summary>
        public static void FillByRarity(DonutChart donut, LootTable table, Func<int, float> rarityMultiplier, ForgeDesignLimits limits)
        {
            if (table == null)
                return;
            var segments = limits.rarities
                .Select(rarity => new DonutChart.Segment(rarity.DisplayName,
                    table.Entries.Where(e => e.item != null && e.item.RarityId == rarity.id).Sum(e => table.GetProbability(e, rarityMultiplier)),
                    rarity.color))
                .Append(new DonutChart.Segment("Rien", table.Entries.Count == 0 ? 1f : 1f - table.DropChance, ChartColors.Neutral));
            donut.SetData(segments, "par tirage", v => $"{v:P0}", $"{table.DropChance:P0}");
        }
    }
}
