using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TheDungeon.Data;
using UnityEngine;

namespace TheDungeon.Editor.Common
{
    /// <summary>
    /// Convention de nommage des assets : PRÉFIXE_NomEnPascalCase.
    /// ITM_ objets, ENM_ ennemis, BOSS_ boss, ATK_ attaques, LT_ tables de butin, BIO_ biomes, RM_ salles, FLR_ étages, PRG_ progression, NPC_ personnages.
    /// </summary>
    public static class NamingConvention
    {
        static readonly Regex ValidBody = new("^[A-Z0-9][A-Za-z0-9]*(_[A-Za-z0-9]+)*$");
        static readonly Regex AnyPrefix = new("^(ITM|ENM|BOSS|ATK|LT|BIO|RM|FLR|PRG|NPC)_");

        public static string GetPrefix(Object asset) => asset switch
        {
            ItemData => "ITM_",
            EnemyData enemy => enemy.IsBoss ? "BOSS_" : "ENM_",
            AttackData => "ATK_",
            LootTable => "LT_",
            BiomeData => "BIO_",
            NpcData => "NPC_",
            RoomData => "RM_",
            FloorData => "FLR_",
            PlayerProgression => "PRG_",
            _ => string.Empty
        };

        public static bool IsValid(Object asset)
        {
            var prefix = GetPrefix(asset);
            return asset.name.StartsWith(prefix) && ValidBody.IsMatch(asset.name.Substring(prefix.Length));
        }

        /// <summary>Nom attendu : préfixe correct + nom affiché (ou nom actuel) converti en PascalCase.</summary>
        public static string GetExpectedName(Object asset)
        {
            if (asset is FloorData floor)
                return $"FLR_{floor.FloorNumber}";
            var source = asset is IDisplayNamed named && !string.IsNullOrWhiteSpace(named.DisplayName)
                ? named.DisplayName
                : AnyPrefix.Replace(asset.name, string.Empty);
            var body = ToPascalCase(source);
            return GetPrefix(asset) + (body.Length > 0 ? body : "Unnamed");
        }

        /// <summary>« Épée longue +1 » devient « EpeeLongue1 ».</summary>
        public static string ToPascalCase(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var builder = new StringBuilder();
            var upperNext = true;
            foreach (var c in RemoveAccents(text))
            {
                if (char.IsLetterOrDigit(c) && c < 128)
                {
                    builder.Append(upperNext ? char.ToUpperInvariant(c) : c);
                    upperNext = false;
                }
                else if (c == '_')
                {
                    builder.Append('_');
                    upperNext = true;
                }
                else
                {
                    upperNext = true;
                }
            }
            return builder.ToString().Trim('_');
        }

        static string RemoveAccents(string text)
        {
            var builder = new StringBuilder();
            foreach (var c in text.Normalize(NormalizationForm.FormD))
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    builder.Append(c);
            return builder.ToString();
        }
    }
}
