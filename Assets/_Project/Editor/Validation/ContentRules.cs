using System.Collections.Generic;
using System.Linq;
using TheDungeon.Editor.Common;

namespace TheDungeon.Editor.Validation
{
    public sealed class NamingRule : ValidationRule
    {
        public override string Name => "Convention de nommage";
        public override string Description => "Chaque asset s'appelle PRÉFIXE_NomEnPascalCase (ITM_, ENM_, BOSS_, LT_, RM_, FLR_, PRG_).";

        public override IEnumerable<ValidationIssue> Run(ValidationContext context)
        {
            foreach (var asset in context.AllAssets())
            {
                if (NamingConvention.IsValid(asset) && asset.name == NamingConvention.GetExpectedName(asset))
                    continue;
                var expected = NamingConvention.GetExpectedName(asset);
                yield return Warning($"Le nom « {asset.name} » ne respecte pas la convention (attendu : « {expected} »).",
                    asset, () => AssetFactory.ApplyNamingConvention(asset), "Renommer");
            }
        }
    }

    public sealed class ItemRule : ValidationRule
    {
        public override string Name => "Objets";
        public override string Description => "Nom, icône, prix et bonus cohérents avec la rareté de l'objet.";

        public override IEnumerable<ValidationIssue> Run(ValidationContext context)
        {
            var limits = context.Limits;
            foreach (var item in context.Items)
            {
                if (string.IsNullOrWhiteSpace(item.DisplayName))
                    yield return Error("L'objet n'a pas de nom affiché.", item);
                if (item.Icon == null)
                    yield return Warning("L'objet n'a pas d'icône : il sera invisible dans l'inventaire.", item);
                if (string.IsNullOrWhiteSpace(item.Description))
                    yield return Warning("L'objet n'a pas de description.", item);

                var rarity = limits.GetRarity(item.Rarity);
                if (!rarity.price.Contains(item.Price))
                    yield return Error($"Prix de {item.Price} or hors limites pour un objet {item.Rarity} ({rarity.price}).", item);

                foreach (var stat in item.Stats)
                {
                    if (stat.value > rarity.maxStatValue)
                        yield return Error($"Bonus {stat.stat} de +{stat.value} trop élevé pour un objet {item.Rarity} (max +{rarity.maxStatValue}).", item);
                    if (stat.value == 0)
                        yield return Warning($"Le bonus {stat.stat} vaut 0 : il ne sert à rien.", item);
                }
                if (item.Stats.GroupBy(stat => stat.stat).Any(group => group.Count() > 1))
                    yield return Warning("La même statistique apparaît plusieurs fois : regroupez les bonus.", item);
                if (item.Stats.Count > limits.maxStatsPerItem)
                    yield return Error($"Trop de bonus de stats ({item.Stats.Count}, max {limits.maxStatsPerItem}).", item);
                if (item.Effects.Count > limits.maxEffectsPerItem)
                    yield return Error($"Trop d'effets ({item.Effects.Count}, max {limits.maxEffectsPerItem}).", item);
            }

            foreach (var group in context.Items.Where(i => !string.IsNullOrWhiteSpace(i.DisplayName)).GroupBy(i => i.DisplayName.Trim().ToLowerInvariant()))
            {
                if (group.Count() < 2)
                    continue;
                foreach (var item in group)
                    yield return Warning($"Plusieurs objets s'appellent « {item.DisplayName} ».", item);
            }
        }
    }

    public sealed class EnemyRule : ValidationRule
    {
        public override string Name => "Ennemis";
        public override string Description => "Stats dans les limites, nom et table de butin renseignés.";

        public override IEnumerable<ValidationIssue> Run(ValidationContext context)
        {
            var limits = context.Limits;
            foreach (var enemy in context.Enemies)
            {
                if (string.IsNullOrWhiteSpace(enemy.DisplayName))
                    yield return Error("L'ennemi n'a pas de nom affiché.", enemy);
                if (enemy.Sprite == null)
                    yield return Warning("L'ennemi n'a pas de visuel.", enemy);
                if (enemy.LootTable == null)
                    yield return Warning("L'ennemi n'a pas de table de butin : il ne lâchera rien.", enemy);
                if (!limits.enemyHealth.Contains(enemy.GetHealth(1)))
                    yield return Error($"PV de base ({enemy.GetHealth(1)}) hors limites ({limits.enemyHealth}).", enemy);
                if (!limits.enemyDamage.Contains(enemy.GetDamage(1)))
                    yield return Error($"Dégâts de base ({enemy.GetDamage(1)}) hors limites ({limits.enemyDamage}).", enemy);
                if (!limits.enemyFloorScaling.Contains(enemy.FloorScaling))
                    yield return Error($"Croissance par étage ({enemy.FloorScaling:P0}) hors limites ({limits.enemyFloorScaling}).", enemy);
            }
        }
    }

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

    public sealed class ProgressionRule : ValidationRule
    {
        public override string Name => "Progression du joueur";
        public override string Description => "Un niveau par ligne, PV, dégâts et XP toujours croissants.";

        public override IEnumerable<ValidationIssue> Run(ValidationContext context)
        {
            var limits = context.Limits;
            if (context.Progressions.Count == 0)
                yield return new ValidationIssue(Severity.Error, "Aucune progression du joueur n'existe. Créez-la dans Content Database > Progression.", null);
            if (context.Progressions.Count > 1)
                foreach (var extra in context.Progressions.Skip(1))
                    yield return Warning("Il existe plusieurs progressions du joueur : une seule est utilisée par le jeu.", extra);

            foreach (var progression in context.Progressions)
            {
                var levels = progression.Levels;
                if (levels.Count != limits.maxLevel)
                    yield return Error($"La progression contient {levels.Count} niveaux au lieu de {limits.maxLevel}.", progression);

                for (var i = 1; i < levels.Count; i++)
                {
                    var level = i + 1;
                    if (levels[i].maxHealth < levels[i - 1].maxHealth)
                        yield return Error($"Les PV baissent au niveau {level} ({levels[i - 1].maxHealth} → {levels[i].maxHealth}).", progression);
                    if (levels[i].damage < levels[i - 1].damage)
                        yield return Error($"Les dégâts baissent au niveau {level} ({levels[i - 1].damage} → {levels[i].damage}).", progression);
                    if (levels[i].xpRequired <= levels[i - 1].xpRequired)
                        yield return Error($"L'XP requise n'augmente pas au niveau {level}.", progression);
                }
            }
        }
    }
}
