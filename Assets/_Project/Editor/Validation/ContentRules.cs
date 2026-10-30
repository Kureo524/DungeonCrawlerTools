using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using TheDungeon.Editor.Progression;
using UnityEngine;

namespace TheDungeon.Editor.Validation
{
    public sealed class NamingRule : ValidationRule
    {
        public override string Name => "Convention de nommage";
        public override string Description => "Chaque asset s'appelle PRÉFIXE_NomEnPascalCase (ITM_, ENM_, BOSS_, LT_, BIO_, RM_, FLR_, PRG_) et son nom est unique.";

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

    public sealed class ItemRule : ValidationRule
    {
        public override string Name => "Objets";
        public override string Description => "Nom, icône, prix, bonus, effets et passifs cohérents avec la rareté et le type de l'objet.";

        public override IEnumerable<ValidationIssue> Run(ValidationContext context)
        {
            var limits = context.Limits;
            // Objets dont la rareté a été supprimée, comptés par rareté : chaque objet concerné dit combien sont à modifier.
            var orphans = context.Items.Where(item => !limits.HasRarity(item.RarityId))
                .GroupBy(item => item.RarityId).ToDictionary(group => group.Key, group => group.Count());

            foreach (var item in context.Items)
            {
                if (item.Icon == null)
                    yield return Warning("L'objet n'a pas d'icône : il sera invisible dans l'inventaire.", item);
                if (string.IsNullOrWhiteSpace(item.Description))
                    yield return Warning("L'objet n'a pas de description.", item);

                var rarity = limits.GetRarity(item.RarityId);
                if (orphans.TryGetValue(item.RarityId, out var orphanCount))
                {
                    yield return Error($"La rareté de cet objet (n°{item.RarityId}) a été supprimée : choisissez-en une autre. " +
                                       $"{orphanCount} objet(s) à modifier pour cette rareté.", item);
                }
                else
                {
                    if (!rarity.price.Contains(item.Price))
                        yield return Error($"Prix de {item.Price} or hors limites pour un objet {rarity.DisplayName} ({rarity.price}).", item);
                    foreach (var stat in item.Stats.Where(stat => stat.value > rarity.maxStatValue))
                        yield return Error($"Bonus {stat.stat} de +{stat.value} trop élevé pour un objet {rarity.DisplayName} (max +{rarity.maxStatValue}).", item);
                }

                foreach (var stat in item.Stats.Where(stat => stat.value == 0))
                    yield return Warning($"Le bonus {stat.stat} vaut 0 : il ne sert à rien.", item);
                foreach (var duplicate in item.Stats.GroupBy(stat => stat.stat).Where(group => group.Count() > 1))
                    yield return Warning($"La statistique {duplicate.Key} apparaît {duplicate.Count()} fois : regroupez ces bonus en une seule ligne.", item);
                if (item.Stats.Count > limits.maxStatsPerItem)
                    yield return Error($"Trop de bonus de stats ({item.Stats.Count}, max {limits.maxStatsPerItem}).", item);

                var active = item.ActiveEffects.Count();
                if (active > limits.maxEffectsPerItem)
                    yield return Error($"Trop d'effets actifs ({active}, max {limits.maxEffectsPerItem}).", item);
                var passives = item.PassiveEffects.Count();
                if (passives > 0 && !EffectInfo.CanHavePassives(item.Type))
                    yield return Warning($"Seules les armes ont des passifs : les {passives} passif(s) de cet objet n'ont aucun effet en jeu.", item);
                else if (passives > rarity.passiveSlots && !orphans.ContainsKey(item.RarityId))
                    yield return Error($"Trop de passifs ({passives}) : une arme {rarity.DisplayName} en a au plus {rarity.passiveSlots}.", item);

                foreach (var effect in item.Effects.Where(effect => !EffectInfo.IsAllowed(item.Type, effect)))
                    yield return Warning($"L'effet {EffectInfo.Label(effect.type)} ne fait rien en {(effect.trigger == EffectTrigger.Passive ? "passif" : "actif")} " +
                                         $"sur un objet de type {item.Type} : choisissez un autre effet.", item);
                foreach (var duplicate in item.PassiveEffects.GroupBy(effect => effect.type).Where(group => group.Count() > 1))
                    yield return Warning($"Le passif {EffectInfo.Label(duplicate.Key)} apparaît {duplicate.Count()} fois : ses valeurs s'additionnent, une seule ligne suffit.", item);
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
        static readonly StatRole[] RequiredRoles = { StatRole.MaxHealth, StatRole.Damage, StatRole.XpToNext };

        public override string Name => "Progression du joueur";
        public override string Description => "Nombre de niveaux, stats nommées et bornées, rôles utilisés par le jeu, évolution des courbes, " +
                                              "attributs, compétences et récompenses par niveau.";

        public override IEnumerable<ValidationIssue> Run(ValidationContext context)
        {
            var limits = context.Limits;
            if (context.Progressions.Count == 0)
                yield return new ValidationIssue(Severity.Error, "Aucune progression du joueur n'existe. Créez-la dans Forge Studio > Progression.", null);

            foreach (var progression in context.Progressions)
            {
                foreach (var issue in Check(progression, limits))
                    yield return issue;
            }
        }

        IEnumerable<ValidationIssue> Check(PlayerProgression progression, ForgeDesignLimits limits)
        {
            var levels = progression.MaxLevel;
            var stats = progression.Stats;
            if (string.IsNullOrWhiteSpace(progression.DisplayName))
                yield return Error("La progression n'a pas de nom.", progression);
            if (levels < 2 || levels > limits.maxLevelCap)
                yield return Error($"La progression contient {levels} niveaux : il en faut entre 2 et {limits.maxLevelCap}.", progression);

            if (stats.Count == 0)
                yield return Error("La progression n'a aucune stat.", progression);
            if (stats.Count > limits.maxProgressionStats)
                yield return Error($"Trop de stats ({stats.Count}, maximum {limits.maxProgressionStats}).", progression);

            foreach (var role in RequiredRoles.Where(r => stats.All(s => s.role != r)))
                yield return Error($"Aucune stat n'a le rôle « {ProgressionEditing.RoleLabel(role)} » : le jeu en a besoin.", progression);
            foreach (var group in stats.Where(s => s.role != StatRole.None).GroupBy(s => s.role).Where(g => g.Count() > 1))
                yield return Error($"Plusieurs stats ont le rôle « {ProgressionEditing.RoleLabel(group.Key)} » : {string.Join(", ", group.Select(s => s.displayName))}.", progression);
            foreach (var group in stats.Where(s => !string.IsNullOrWhiteSpace(s.displayName)).GroupBy(s => s.displayName.Trim()).Where(g => g.Count() > 1))
                yield return Error($"Deux stats portent le même nom : « {group.Key} ».", progression);
            if (stats.FirstOrDefault(s => s.role == StatRole.MaxHealth) is { visibility: StatVisibility.Hidden } health)
                yield return Warning($"« {health.displayName} » est masquée : le joueur ne verra pas sa vie.", progression);

            foreach (var issue in CheckAttributes(progression, limits).Concat(CheckSkills(progression, limits)).Concat(CheckMechanics(progression))
                         .Concat(CheckRewards(progression)))
                yield return issue;

            foreach (var stat in stats)
            {
                var name = string.IsNullOrWhiteSpace(stat.displayName) ? "(sans nom)" : $"« {stat.displayName} »";
                if (string.IsNullOrWhiteSpace(stat.displayName))
                    yield return Error("Une stat n'a pas de nom.", progression);
                if (stat.values.Count != levels)
                    yield return Error($"{name} a {stat.values.Count} valeurs pour {levels} niveaux.", progression);
                if (stat.min >= stat.max)
                    yield return Error($"{name} : le minimum ({stat.min}) doit être inférieur au maximum ({stat.max}).", progression);

                var bounds = ProgressionEditing.Bounds(stat.role, limits);
                if (bounds.HasValue && (stat.min < bounds.Value.min || stat.max > bounds.Value.max))
                    yield return Error($"{name} : les bornes doivent rester entre {bounds.Value.min} et {bounds.Value.max} " +
                                       $"(limites de l'équipe technique pour « {ProgressionEditing.RoleLabel(stat.role)} »).", progression);

                var outside = stat.values.Select((v, i) => (v, i)).Where(p => p.v < stat.min || p.v > stat.max).Select(p => p.i + 1).ToList();
                if (outside.Count > 0)
                    yield return Error($"{name} sort de ses bornes ({stat.min} à {stat.max}) au(x) niveau(x) {string.Join(", ", outside.Take(5))}{(outside.Count > 5 ? "…" : "")}.", progression);

                foreach (var index in ProgressionEditing.GrowthViolations(stat).Take(3))
                    yield return Error(stat.growth == StatGrowth.AlwaysIncreases
                        ? $"{name} n'augmente pas au niveau {index + 1} ({stat.Format(stat.values[index - 1])} → {stat.Format(stat.values[index])})."
                        : $"{name} baisse au niveau {index + 1} ({stat.Format(stat.values[index - 1])} → {stat.Format(stat.values[index])}).", progression);

                var bonus = ProgressionEditing.MaxAttributeBonus(progression, stat);
                if (bonus > 0f && stat.values.Count > 0 && stat.values.Max() + bonus > stat.max)
                    yield return Warning($"Avec tous ses points d'attributs, le joueur peut porter {name} à {stat.Format(stat.values.Max() + bonus)} " +
                                         $"(maximum prévu : {stat.Format(stat.max)}).", progression);
            }
        }

        IEnumerable<ValidationIssue> CheckAttributes(PlayerProgression progression, ForgeDesignLimits limits)
        {
            var attributes = progression.Attributes;
            if (attributes.Count > limits.maxAttributes)
                yield return Error($"Trop d'attributs ({attributes.Count}, maximum {limits.maxAttributes}).", progression);
            foreach (var group in attributes.Where(a => !string.IsNullOrWhiteSpace(a.displayName)).GroupBy(a => a.displayName.Trim()).Where(g => g.Count() > 1))
                yield return Error($"Deux attributs portent le même nom : « {group.Key} ».", progression);

            foreach (var attribute in attributes)
            {
                var name = string.IsNullOrWhiteSpace(attribute.displayName) ? "Un attribut sans nom" : $"L'attribut « {attribute.displayName} »";
                if (string.IsNullOrWhiteSpace(attribute.displayName))
                    yield return Error("Un attribut n'a pas de nom.", progression);
                if (attribute.maxPoints < 1)
                    yield return Error($"{name} doit pouvoir recevoir au moins 1 point.", progression);
                if (attribute.bonuses.Count == 0)
                    yield return Warning($"{name} n'améliore aucune stat : y investir ne servirait à rien.", progression);
                foreach (var bonus in attribute.bonuses)
                {
                    var stat = progression.FindStat(bonus.statId);
                    if (stat == null)
                        yield return Error($"{name} améliore une stat qui n'existe plus.", progression);
                    else if (Mathf.Approximately(bonus.perPoint, 0f))
                        yield return Warning($"{name} : le bonus sur « {stat.displayName} » vaut 0 par point.", progression);
                    if (bonus.softCap < 0 || bonus.afterSoftCap < 0f || bonus.afterSoftCap > 1f)
                        yield return Error($"{name} : le seuil doit être positif et la part conservée au-delà comprise entre 0 et 1.", progression);
                }
            }

            var earned = ProgressionEditing.TotalAttributePoints(progression);
            var capacity = ProgressionEditing.AttributeCapacity(progression);
            if (earned > 0 && attributes.Count == 0)
                yield return Error($"Les récompenses donnent {earned} point(s) d'attributs, mais la progression n'a aucun attribut où les dépenser.", progression);
            else if (earned > capacity)
                yield return Warning($"Le joueur gagne {earned} points d'attributs mais ne peut en dépenser que {capacity} (somme des maximums).", progression);
        }

        IEnumerable<ValidationIssue> CheckSkills(PlayerProgression progression, ForgeDesignLimits limits)
        {
            var skills = progression.Skills;
            if (skills.Count > limits.maxSkills)
                yield return Error($"Trop de compétences ({skills.Count}, maximum {limits.maxSkills}).", progression);
            foreach (var group in skills.Where(s => !string.IsNullOrWhiteSpace(s.displayName)).GroupBy(s => s.displayName.Trim()).Where(g => g.Count() > 1))
                yield return Error($"Deux compétences portent le même nom : « {group.Key} ».", progression);

            foreach (var skill in skills)
            {
                var name = $"La compétence « {skill.displayName} »";
                if (string.IsNullOrWhiteSpace(skill.displayName))
                    yield return Error("Une compétence n'a pas de nom.", progression);
                if (skill.ranks.Count == 0)
                    yield return Error($"{name} n'a aucun rang.", progression);
                if (skill.effect == SkillEffect.None && string.IsNullOrWhiteSpace(skill.description))
                    yield return Warning($"{name} n'a ni effet ni description : le joueur ne saura pas à quoi elle sert.", progression);
                if (ProgressionEditing.RankAt(progression, skill, progression.MaxLevel) == 0)
                    yield return Warning($"{name} n'est débloquée par aucune récompense de niveau.", progression);
            }
        }

        IEnumerable<ValidationIssue> CheckMechanics(PlayerProgression progression)
        {
            var mechanics = progression.Mechanics;
            foreach (var group in mechanics.GroupBy(m => m.input).Where(g => g.Count() > 1))
                yield return Error($"Plusieurs mécaniques utilisent la touche « {ProgressionEditing.InputLabel(group.Key)} » : " +
                                   $"{string.Join(", ", group.Select(m => m.displayName))}.", progression);
            foreach (var group in mechanics.Where(m => !string.IsNullOrWhiteSpace(m.displayName)).GroupBy(m => m.displayName.Trim()).Where(g => g.Count() > 1))
                yield return Error($"Deux mécaniques portent le même nom : « {group.Key} ».", progression);
            if (mechanics.Count > 0 && mechanics.All(m => m.action is not (MechanicAction.Projectile or MechanicAction.Nova)))
                yield return Warning("Aucune mécanique d'attaque (projectile ou onde) : le joueur ne pourra pas se battre.", progression);

            foreach (var mechanic in mechanics)
            {
                var name = $"La mécanique « {mechanic.displayName} »";
                if (string.IsNullOrWhiteSpace(mechanic.displayName))
                    yield return Error("Une mécanique n'a pas de nom.", progression);
                if (mechanic.cooldown < 0f)
                    yield return Error($"{name} a une recharge négative.", progression);
                var (power, count, duration) = ProgressionEditing.ActionFields(mechanic.action);
                if (power != null && mechanic.power <= 0f)
                    yield return Error($"{name} : « {power} » doit être positif.", progression);
                if (count != null && mechanic.count < 1)
                    yield return Error($"{name} : « {count} » doit valoir au moins 1.", progression);
                if (duration != null && mechanic.duration <= 0f)
                    yield return Error($"{name} : « {duration} » doit être positive.", progression);
                if (ProgressionEditing.RankAt(progression, mechanic, progression.MaxLevel) == 0)
                    yield return Warning($"{name} n'est ni débloquée dès le début, ni débloquée par une récompense de niveau.", progression);

                if (string.IsNullOrEmpty(mechanic.costStatId))
                {
                    if (mechanic.cost > 0f)
                        yield return Warning($"{name} a un coût mais ne consomme aucune stat.", progression);
                    continue;
                }
                var stat = progression.FindStat(mechanic.costStatId);
                if (stat == null)
                    yield return Error($"{name} consomme une stat qui n'existe plus.", progression);
                else if (!stat.resource && stat.role != StatRole.MaxHealth)
                    yield return Error($"{name} consomme « {stat.displayName} », qui n'est pas une jauge : cochez « Jauge consommable » sur cette stat.", progression);
                else if (stat.resource && stat.values.Count > 0 && mechanic.cost > stat.values.Max())
                    yield return Error($"{name} coûte {mechanic.cost} « {stat.displayName} », plus que la jauge n'en contient jamais ({stat.Format(stat.values.Max())}).", progression);
                else if (stat.resource && stat.regenPerSecond <= 0f)
                    yield return Warning($"« {stat.displayName} » ne se recharge pas : une fois vide, {mechanic.displayName} devient inutilisable.", progression);
            }
        }

        /// <summary>Rejoue les récompenses dans l'ordre des niveaux, avec les règles du jeu, pour repérer celles qui ne feraient rien.</summary>
        IEnumerable<ValidationIssue> CheckRewards(PlayerProgression progression)
        {
            var ranks = new Dictionary<string, int>();
            foreach (var mechanic in progression.Mechanics.Where(m => m.unlockedAtStart))
                ranks[mechanic.id] = 1;
            foreach (var reward in progression.Rewards.OrderBy(r => r.level))
            {
                var where = $"Récompense du niveau {reward.level}";
                if (reward.level < 1 || reward.level > progression.MaxLevel)
                    yield return Error($"{where} : le niveau doit être compris entre 1 et {progression.MaxLevel}.", progression);

                switch (reward.type)
                {
                    case RewardType.AttributePoints when reward.amount <= 0:
                        yield return Error($"{where} : le nombre de points doit être positif.", progression);
                        break;
                    case RewardType.Note when string.IsNullOrWhiteSpace(reward.note):
                        yield return Warning($"{where} : la note est vide.", progression);
                        break;
                    case RewardType.UnlockSkill or RewardType.UpgradeSkill:
                        var skill = progression.FindSkill(reward.skillId);
                        if (skill == null)
                        {
                            yield return Error($"{where} : aucune compétence choisie (ou elle a été supprimée).", progression);
                            break;
                        }
                        ranks.TryGetValue(skill.id, out var rank);
                        if (reward.type == RewardType.UnlockSkill && rank > 0)
                            yield return Warning($"{where} : « {skill.displayName} » est déjà débloquée.", progression);
                        else if (reward.type == RewardType.UpgradeSkill && rank == 0)
                            yield return Error($"{where} : « {skill.displayName} » est améliorée avant d'être débloquée.", progression);
                        else if (rank >= skill.MaxRank)
                            yield return Warning($"{where} : « {skill.displayName} » est déjà à son rang maximum ({skill.MaxRank}). Ajoutez un rang ou retirez la récompense.", progression);
                        else
                            ranks[skill.id] = rank + 1;
                        break;
                    case RewardType.UnlockMechanic or RewardType.UpgradeMechanic:
                        var mechanic = progression.FindMechanic(reward.mechanicId);
                        if (mechanic == null)
                        {
                            yield return Error($"{where} : aucune mécanique choisie (ou elle a été supprimée).", progression);
                            break;
                        }
                        ranks.TryGetValue(mechanic.id, out var level);
                        if (reward.type == RewardType.UnlockMechanic && level > 0)
                            yield return Warning($"{where} : « {mechanic.displayName} » est déjà débloquée" +
                                                 (mechanic.unlockedAtStart ? " (dès le début)." : "."), progression);
                        else if (reward.type == RewardType.UpgradeMechanic && level == 0)
                            yield return Error($"{where} : « {mechanic.displayName} » est améliorée avant d'être débloquée.", progression);
                        else if (level >= mechanic.MaxRank)
                            yield return Warning($"{where} : « {mechanic.displayName} » n'a plus d'amélioration (rang {mechanic.MaxRank}). Ajoutez une amélioration ou retirez la récompense.", progression);
                        else
                            ranks[mechanic.id] = level + 1;
                        break;

                }
            }
        }

    }
}
