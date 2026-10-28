using System;
using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Dialogue;
using UnityEngine;

namespace TheDungeon.Game
{
    /// <summary>
    /// État de la partie : niveau et XP (progression du GDD), PV, or, équipement, inventaire, marqueurs d'histoire.
    /// Implémente IDialogueState : les dialogues lisent et modifient directement la partie.
    /// </summary>
    public sealed class GameState : IDialogueState
    {
        public const float BaseMoveSpeed = 5f;

        readonly PlayerProgression progression;
        readonly HashSet<string> flags = new();

        public int Level { get; private set; } = 1;
        public int Xp { get; private set; }
        public int Hp { get; private set; }
        public int Gold { get; private set; }
        public int Floor { get; set; } = 1;
        public int Kills { get; set; }
        public ItemData Weapon { get; private set; }
        public ItemData Armor { get; private set; }
        public ItemData Accessory { get; private set; }
        public List<ItemData> Inventory { get; } = new();

        /// <summary>Soin progressif en cours (consommables « Régénération »).</summary>
        public float RegenPerSecond { get; private set; }
        float regenRemaining;
        float regenCarry;

        public event Action<string, Color> Message;
        public event Action LeveledUp;

        public GameState(PlayerProgression progression)
        {
            this.progression = progression;
            Hp = MaxHp;
        }

        LevelEntry Entry => progression != null && progression.Levels.Count > 0
            ? progression.GetLevel(Level)
            : new LevelEntry { maxHealth = 50 + Level * 10, damage = 5 + Level, xpRequired = 100 * Level };

        int MaxLevel => progression != null && progression.Levels.Count > 0 ? progression.MaxLevel : 20;

        public IEnumerable<ItemData> Equipped => new[] { Weapon, Armor, Accessory }.Where(item => item != null);

        int Bonus(StatType stat) => Equipped.SelectMany(item => item.Stats).Where(s => s.stat == stat).Sum(s => s.value);

        public int MaxHp => Mathf.Max(1, Entry.maxHealth + Bonus(StatType.Health));
        public int Attack => Mathf.Max(1, Entry.damage + Bonus(StatType.Damage));
        public int Defense => Mathf.Max(0, Bonus(StatType.Defense));
        public float MoveSpeed => Mathf.Max(2f, BaseMoveSpeed + 0.15f * Bonus(StatType.Speed));
        public int XpToNext => Level >= MaxLevel ? 0 : Mathf.Max(1, Entry.xpRequired);
        public bool IsDead => Hp <= 0;

        /// <summary>Dégâts subis après défense (au moins 1). Renvoie les dégâts réellement infligés.</summary>
        public int TakeDamage(int raw)
        {
            var dealt = Combat.Mitigate(raw, Defense);
            Hp = Mathf.Max(0, Hp - dealt);
            return dealt;
        }

        public void Heal(int amount)
        {
            if (amount <= 0)
                return;
            Hp = Mathf.Min(MaxHp, Hp + amount);
        }

        public void AddXp(int amount)
        {
            Xp += amount;
            while (XpToNext > 0 && Xp >= XpToNext)
            {
                Xp -= XpToNext;
                Level++;
                Hp = MaxHp;
                Message?.Invoke($"Niveau {Level} !", new Color(1f, 0.85f, 0.3f));
                LeveledUp?.Invoke();
            }
            if (XpToNext == 0)
                Xp = 0;
        }

        public void AddGold(int amount)
        {
            Gold = Mathf.Max(0, Gold + amount);
        }

        public bool Spend(int amount)
        {
            if (Gold < amount)
                return false;
            Gold -= amount;
            return true;
        }

        public void Tick(float deltaTime)
        {
            if (regenRemaining <= 0f)
            {
                RegenPerSecond = 0f;
                return;
            }
            regenRemaining -= deltaTime;
            regenCarry += RegenPerSecond * deltaTime;
            var whole = Mathf.FloorToInt(regenCarry);
            regenCarry -= whole;
            Heal(whole);
        }

        /// <summary>Ramasse un objet : équipé automatiquement s'il est meilleur que l'actuel, sinon rangé.</summary>
        public void AddItem(ItemData item)
        {
            if (item == null)
                return;
            var slot = Slot(item.Type);
            if (slot.HasValue && IsBetter(item, Current(slot.Value)))
            {
                var previous = Current(slot.Value);
                Equip(item);
                if (previous != null)
                    Inventory.Add(previous);
                Message?.Invoke($"Équipé : {item.DisplayName}", Color.white);
                return;
            }
            Inventory.Add(item);
            Message?.Invoke($"Ramassé : {item.DisplayName}", Color.white);
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
            foreach (var effect in item.Effects)
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
            if (!item.Effects.Any(e => e.type is EffectType.Heal or EffectType.Regeneration))
                Heal(Mathf.Max(5, MaxHp / 5));
            Message?.Invoke($"Utilisé : {item.DisplayName}", new Color(0.5f, 1f, 0.6f));
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
        public static bool IsBetter(ItemData candidate, ItemData current)
        {
            if (current == null)
                return true;
            if (candidate.Rarity != current.Rarity)
                return candidate.Rarity > current.Rarity;
            return candidate.Stats.Sum(s => s.value) > current.Stats.Sum(s => s.value);
        }

        // --- IDialogueState ---
        public int PlayerLevel => Level;
        public bool HasFlag(string flag) => flags.Contains(flag);

        public void SetFlag(string flag, bool value)
        {
            if (string.IsNullOrEmpty(flag))
                return;
            if (value) flags.Add(flag); else flags.Remove(flag);
        }

        void IDialogueState.AddGold(int amount)
        {
            AddGold(amount);
            Message?.Invoke(amount >= 0 ? $"+{amount} or" : $"{amount} or", new Color(1f, 0.85f, 0.3f));
        }

        void IDialogueState.AddXp(int amount)
        {
            Message?.Invoke($"+{amount} XP", new Color(0.6f, 0.8f, 1f));
            AddXp(amount);
        }

        void IDialogueState.Heal(int amount)
        {
            Heal(amount);
            Message?.Invoke($"+{amount} PV", new Color(0.5f, 1f, 0.6f));
        }
    }

    /// <summary>Règles de combat communes au joueur et aux ennemis.</summary>
    public static class Combat
    {
        /// <summary>Dégâts après défense : jamais moins de 1, pour qu'aucun coup ne soit totalement inutile.</summary>
        public static int Mitigate(int raw, int defense) => Mathf.Max(1, raw - defense);
    }
}
