using System;
using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Dialogue;
using UnityEngine;

namespace TheDungeon.Game
{
    /// <summary>Inventaire et équipement : ramasser, équiper, utiliser un consommable, comparer deux objets.</summary>
    public sealed partial class GameState
    {
        /// <summary>Ramasse un objet : équipé automatiquement s'il est meilleur que l'actuel, sinon rangé.</summary>
        public void AddItem(ItemData item)
        {
            if (item == null)
                return;
            var slot = Slot(item.Type);
            if (slot.HasValue && IsBetter(item, Current(slot.Value), RarityRank))
            {
                var previous = Current(slot.Value);
                Equip(item);
                if (previous != null)
                    Inventory.Add(previous);
                Message?.Invoke($"'{item.DisplayName}' équipé.", Color.white);
                return;
            }
            Inventory.Add(item);
            Message?.Invoke($"'{item.DisplayName}' ramassé.", Color.white);
        }

        public void RemoveItem(ItemData item)
        {
            if (!Inventory.Remove(item))
            {
                if (Weapon == item) Weapon = null;
                else if (Armor == item) Armor = null;
                else if (Accessory == item) Accessory = null;
            }
            Hp = Mathf.Min(Hp, MaxHp);
        }

        public bool HasItem(ItemData item) => item != null && (Inventory.Contains(item) || Equipped.Contains(item));

        public void Equip(ItemData item)
        {
            var slot = Slot(item.Type);
            if (!slot.HasValue)
                return;
            Inventory.Remove(item);
            var previous = Current(slot.Value);
            switch (slot.Value)
            {
                case ItemType.Weapon: Weapon = item; break;
                case ItemType.Armor: Armor = item; break;
                default: Accessory = item; break;
            }
            if (previous != null && previous != item)
                Inventory.Add(previous);
            Hp = Mathf.Min(Hp, MaxHp);
        }

        /// <summary>Utilise un consommable : applique ses effets de soin. Renvoie false si l'objet n'est pas utilisable.</summary>
        public bool Use(ItemData item)
        {
            if (item == null || item.Type != ItemType.Consumable || !Inventory.Contains(item))
                return false;
            Inventory.Remove(item);
            foreach (var effect in item.ActiveEffects)
            {
                if (effect.type == EffectType.Heal)
                    Heal(Mathf.RoundToInt(effect.value));
                else if (effect.type == EffectType.Regeneration)
                {
                    RegenPerSecond = effect.duration > 0f ? effect.value / effect.duration : effect.value;
                    regenRemaining = Mathf.Max(1f, effect.duration);
                }
            }
            // Un consommable sans effet de soin rend au moins un peu de vie (nourriture, potion…).
            if (!item.ActiveEffects.Any(e => e.type is EffectType.Heal or EffectType.Regeneration))
                Heal(Mathf.Max(5, MaxHp / 5));
            Message?.Invoke($"'{item.DisplayName}' utilisé.", new Color(0.5f, 1f, 0.6f));
            return true;
        }

        public ItemData FirstConsumable => Inventory.FirstOrDefault(i => i.Type == ItemType.Consumable);

        static ItemType? Slot(ItemType type) => type switch
        {
            ItemType.Weapon => ItemType.Weapon,
            ItemType.Armor => ItemType.Armor,
            ItemType.Accessory => ItemType.Accessory,
            _ => null
        };

        ItemData Current(ItemType slot) => slot switch
        {
            ItemType.Weapon => Weapon,
            ItemType.Armor => Armor,
            _ => Accessory
        };

        /// <summary>Meilleur = rareté supérieure, ou même rareté et plus de bonus au total.</summary>
        /// <param name="rarityRank">Rang de chaque rareté ; par défaut, l'identifiant (les raretés de départ sont rangées ainsi).</param>
        public static bool IsBetter(ItemData candidate, ItemData current, Func<int, int> rarityRank = null)
        {
            if (current == null)
                return true;
            rarityRank ??= id => id;
            var candidateRank = rarityRank(candidate.RarityId);
            var currentRank = rarityRank(current.RarityId);
            if (candidateRank != currentRank)
                return candidateRank > currentRank;
            return candidate.Stats.Sum(s => s.value) > current.Stats.Sum(s => s.value);
        }
    }
}
