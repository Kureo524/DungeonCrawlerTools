using System.Collections.Generic;
using System.Linq;

namespace TheDungeon.Data
{
    /// <summary>
    /// Ce que fait chaque effet d'objet, et où il a un sens : partagé par l'outil Objets (listes proposées, libellés),
    /// le Validator et le jeu. Une arme porte des effets actifs (à l'impact) et des passifs (permanents, à la Nightreign).
    /// </summary>
    public static class EffectInfo
    {
        static readonly EffectType[] WeaponActive = { EffectType.Poison, EffectType.Burn, EffectType.Stun, EffectType.LifeSteal };

        static readonly EffectType[] WeaponPassive =
        {
            EffectType.AttackBoost, EffectType.CriticalChance, EffectType.LastStand, EffectType.KillHeal,
            EffectType.Regeneration, EffectType.DamageReduction, EffectType.MoveSpeed
        };

        static readonly EffectType[] ConsumableActive = { EffectType.Heal, EffectType.Regeneration };

        static readonly EffectType[] LegacyActive =
            { EffectType.Heal, EffectType.Poison, EffectType.Burn, EffectType.Regeneration, EffectType.LifeSteal, EffectType.Stun };

        /// <summary>Part des PV sous laquelle « Dernier souffle » s'active.</summary>
        public const float LastStandThreshold = 0.3f;

        /// <summary>Réduction des dégâts reçus maximale, quel que soit le cumul.</summary>
        public const float MaxDamageReduction = 80f;

        /// <summary>Seules les armes ont des passifs.</summary>
        public static bool CanHavePassives(ItemType type) => type == ItemType.Weapon;

        /// <summary>Effets proposés pour ce type d'objet et ce déclenchement.</summary>
        public static IReadOnlyList<EffectType> Allowed(ItemType type, EffectTrigger trigger) => (type, trigger) switch
        {
            (ItemType.Weapon, EffectTrigger.Active) => WeaponActive,
            (ItemType.Weapon, EffectTrigger.Passive) => WeaponPassive,
            (ItemType.Consumable, EffectTrigger.Active) => ConsumableActive,
            (_, EffectTrigger.Active) => LegacyActive,
            _ => System.Array.Empty<EffectType>()
        };

        public static bool IsAllowed(ItemType type, ItemEffect effect) => Allowed(type, effect.trigger).Contains(effect.type);

        public static string Label(EffectType type) => type switch
        {
            EffectType.Heal => "Soin",
            EffectType.Poison => "Poison",
            EffectType.Burn => "Brûlure",
            EffectType.Regeneration => "Régénération",
            EffectType.LifeSteal => "Vol de vie",
            EffectType.Stun => "Étourdissement",
            EffectType.AttackBoost => "Attaque accrue",
            EffectType.CriticalChance => "Coups critiques accrus",
            EffectType.LastStand => "Dernier souffle",
            EffectType.KillHeal => "Festin",
            EffectType.DamageReduction => "Peau de fer",
            EffectType.MoveSpeed => "Pas léger",
            _ => type.ToString()
        };

        /// <summary>Ce que fait l'effet, pour l'infobulle de l'outil.</summary>
        public static string Description(EffectType type, EffectTrigger trigger) => type switch
        {
            EffectType.Heal => "Rend des PV d'un coup.",
            EffectType.Poison => "L'ennemi touché perd des PV chaque seconde.",
            EffectType.Burn => "L'ennemi touché brûle et perd des PV chaque seconde.",
            EffectType.Regeneration when trigger == EffectTrigger.Passive => "Rend des PV chaque seconde tant que l'arme est équipée.",
            EffectType.Regeneration => "Rend des PV petit à petit pendant la durée.",
            EffectType.LifeSteal => "Rend une part des dégâts infligés en PV.",
            EffectType.Stun => "Immobilise l'ennemi touché (sauf les boss).",
            EffectType.AttackBoost => "Augmente les dégâts du joueur.",
            EffectType.CriticalChance => "Augmente la chance de coup critique (dégâts doublés).",
            EffectType.LastStand => $"Augmente les dégâts quand il reste moins de {LastStandThreshold:P0} des PV.",
            EffectType.KillHeal => "Rend des PV à chaque ennemi vaincu.",
            EffectType.DamageReduction => $"Réduit les dégâts reçus (au plus {MaxDamageReduction:0} %).",
            EffectType.MoveSpeed => "Augmente la vitesse de déplacement.",
            _ => string.Empty
        };

        /// <summary>Unité de la valeur, ex. « % des dégâts ».</summary>
        public static string Unit(EffectType type, EffectTrigger trigger) => type switch
        {
            EffectType.Heal => "PV",
            EffectType.Poison or EffectType.Burn => "dégâts/s",
            EffectType.Regeneration => trigger == EffectTrigger.Passive ? "PV/s" : "PV au total",
            EffectType.LifeSteal => "% des dégâts",
            EffectType.AttackBoost or EffectType.LastStand => "% de dégâts",
            EffectType.CriticalChance => "% de chance",
            EffectType.KillHeal => "PV par ennemi",
            EffectType.DamageReduction => "% de dégâts reçus en moins",
            EffectType.MoveSpeed => "% de vitesse",
            _ => string.Empty
        };

        /// <summary>L'étourdissement n'a qu'une durée.</summary>
        public static bool UsesValue(EffectType type) => type != EffectType.Stun;

        /// <summary>Les passifs sont permanents : seuls les effets actifs qui s'étalent dans le temps ont une durée.</summary>
        public static bool UsesDuration(EffectType type, EffectTrigger trigger) =>
            trigger == EffectTrigger.Active && type is EffectType.Poison or EffectType.Burn or EffectType.Stun or EffectType.Regeneration;

        /// <summary>Résumé lisible : « Vol de vie 12 % des dégâts », « Poison 5 dégâts/s · 3 s ».</summary>
        public static string Summary(ItemEffect effect)
        {
            var text = Label(effect.type);
            if (UsesValue(effect.type))
                text += $" {effect.value:0.#} {Unit(effect.type, effect.trigger)}";
            if (UsesDuration(effect.type, effect.trigger))
                text += $" · {effect.duration:0.#} s";
            return text;
        }
    }
}
