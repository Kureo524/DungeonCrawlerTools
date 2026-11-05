using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;

namespace TheDungeon.Editor.Validation
{
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
                if (item.WeaponAttack != null && item.WeaponAttack.User != AttackUser.Weapon)
                    yield return Warning($"L'attaque '{item.WeaponAttack.DisplayName}' n'est pas une attaque d'arme (outil Attaques > Pour).", item);
                if (item.WeaponSkill != null && item.WeaponSkill.User != AttackUser.Skill)
                    yield return Warning($"La compétence '{item.WeaponSkill.DisplayName}' n'est pas une compétence d'arme (outil Attaques > Pour).", item);
                if ((item.WeaponAttack != null || item.WeaponSkill != null) && item.Type != ItemType.Weapon)
                    yield return Warning("Seules les armes utilisent leurs attaques : celles de cet objet ne servent pas.", item);
                foreach (var duplicate in item.PassiveEffects.GroupBy(effect => effect.type).Where(group => group.Count() > 1))
                    yield return Warning($"Le passif {EffectInfo.Label(duplicate.Key)} apparaît {duplicate.Count()} fois : ses valeurs s'additionnent, une seule ligne suffit.", item);
            }
        }
    }
}
