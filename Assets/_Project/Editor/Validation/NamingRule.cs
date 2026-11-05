using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;

namespace TheDungeon.Editor.Validation
{
    public sealed class NamingRule : ValidationRule
    {
        public override string Name => "Convention de nommage";
        public override string Description => "Chaque asset s'appelle PRÉFIXE_NomEnPascalCase (ITM_, ENM_, BOSS_, ATK_, LT_, BIO_, RM_, FLR_, PRG_, NPC_) et son nom est unique.";

        public override IEnumerable<ValidationIssue> Run(ValidationContext context)
        {
            var all = context.AllAssets().ToList();
            foreach (var asset in all)
            {
                if (asset is TheDungeon.Data.IDisplayNamed named && string.IsNullOrWhiteSpace(named.DisplayName))
                    yield return Error("Le nom est vide.", asset);

                var expectedName = NamingConvention.GetExpectedName(asset);
                if (all.Any(other => other != asset && NamingConvention.GetExpectedName(other) == expectedName))
                    yield return Error($"Un autre élément porte déjà le nom « {expectedName} ». Choisissez un nom différent.", asset);

                if (NamingConvention.IsValid(asset) && asset.name == NamingConvention.GetExpectedName(asset))
                    continue;
                var expected = NamingConvention.GetExpectedName(asset);
                yield return Warning($"Le nom « {asset.name} » ne respecte pas la convention (attendu : « {expected} »).",
                    asset, () => AssetFactory.ApplyNamingConvention(asset), "Renommer");
            }
        }
    }
}
