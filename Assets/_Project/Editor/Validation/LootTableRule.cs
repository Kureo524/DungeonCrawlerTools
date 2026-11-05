using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;

namespace TheDungeon.Editor.Validation
{
    public sealed class LootTableRule : ValidationRule
    {
        public override string Name => "Tables de butin";
        public override string Description => "Chaque objet cité existe, les poids sont positifs et les étages cohérents.";

        public override IEnumerable<ValidationIssue> Run(ValidationContext context)
        {
            var limits = context.Limits;
            foreach (var table in context.LootTables)
            {
                if (table.Entries.Count == 0)
                    yield return Error("La table de butin est vide.", table);
                if (table.Entries.Count > limits.maxLootEntries)
                    yield return Error($"Trop d'objets dans la table ({table.Entries.Count}, max {limits.maxLootEntries}).", table);

                for (var i = 0; i < table.Entries.Count; i++)
                {
                    var entry = table.Entries[i];
                    if (entry.item == null)
                        yield return Error($"La ligne {i + 1} ne cite aucun objet (objet manquant ou supprimé).", table);
                    if (entry.weight <= 0)
                        yield return Error($"La ligne {i + 1} a un poids de {entry.weight} : l'objet ne tombera jamais.", table);
                }
                if (table.Entries.Where(e => e.item != null).GroupBy(e => e.item).Any(group => group.Count() > 1))
                    yield return Warning("Un même objet apparaît sur plusieurs lignes : additionnez plutôt les poids.", table);
                if (table.MinFloor > table.MaxFloor)
                    yield return Error($"L'étage minimum ({table.MinFloor}) est supérieur à l'étage maximum ({table.MaxFloor}).", table);
                if (table.MinFloor < 1 || table.MaxFloor > limits.floorCount)
                    yield return Error($"Les étages doivent être compris entre 1 et {limits.floorCount}.", table);
            }
        }
    }
}
