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
        readonly Dictionary<string, int> invested = new();
        readonly Dictionary<string, int> skillRanks = new();
        readonly Dictionary<string, int> mechanicRanks = new();
        // Niveau actuel des jauges (stats « jauge ») ; absent = pleine.
        readonly Dictionary<string, float> resources = new();

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

        /// <summary>Points d'attributs gagnés et pas encore répartis.</summary>
        public int AttributePoints { get; private set; }

        /// <summary>Soin progressif en cours (consommables « Régénération »).</summary>
        public float RegenPerSecond { get; private set; }
        float regenRemaining;
        float regenCarry;
        float passiveRegenCarry;

        public event Action<string, Color> Message;
        public event Action LeveledUp;

        public GameState(PlayerProgression progression)
        {
            this.progression = progression;
            foreach (var mechanic in Mechanics.Where(m => m.unlockedAtStart))
                mechanicRanks[mechanic.id] = 1;
            ApplyRewards(1);
            Hp = MaxHp;
        }

        int MaxLevel => progression != null && progression.MaxLevel > 0 ? progression.MaxLevel : 20;

        /// <summary>Valeur de base d'un rôle au niveau actuel, ou la valeur de secours si la progression n'a pas cette stat.</summary>
        float Base(StatRole role, float fallback)
        {
            var stat = progression != null ? progression.Find(role) : null;
            return stat != null && stat.values.Count > 0 ? stat.GetValue(Level) + AttributeBonus(stat) : fallback;
        }

        /// <summary>Bonus des points investis dans les attributs pour cette stat.</summary>
        float AttributeBonus(ProgressionStat stat) => progression == null ? 0f : progression.Attributes.Sum(a => a.BonusFor(stat.id, Points(a)));

        public IEnumerable<ItemData> Equipped => new[] { Weapon, Armor, Accessory }.Where(item => item != null);

        int Bonus(StatType stat) => Equipped.SelectMany(item => item.Stats).Where(s => s.stat == stat).Sum(s => s.value);

        /// <summary>Total des passifs de l'arme équipée pour ce type d'effet.</summary>
        public float Passive(EffectType type) => Weapon != null ? Weapon.PassiveTotal(type) : 0f;

        /// <summary>Rang d'une rareté (0 = la plus commune), fourni par la base de jeu ; par défaut, l'identifiant.</summary>
        public Func<int, int> RarityRank { get; set; }

        public int MaxHp => Mathf.Max(1, Mathf.RoundToInt(Base(StatRole.MaxHealth, 50 + Level * 10)) + Bonus(StatType.Health));

        public int Attack
        {
            get
            {
                var boost = Passive(EffectType.AttackBoost);
                if (Hp > 0 && Hp < MaxHp * EffectInfo.LastStandThreshold)
                    boost += Passive(EffectType.LastStand);
                var raw = Base(StatRole.Damage, 5 + Level) + Bonus(StatType.Damage);
                return Mathf.Max(1, Mathf.RoundToInt(raw * (1f + boost / 100f)));
            }
        }

        public int Defense => Mathf.Max(0, Mathf.RoundToInt(Base(StatRole.Defense, 0f)) + Bonus(StatType.Defense));

        public float MoveSpeed => Mathf.Max(2f, (Base(StatRole.Speed, BaseMoveSpeed) + 0.15f * Bonus(StatType.Speed)) * (1f + Passive(EffectType.MoveSpeed) / 100f));
        public int XpToNext => Level >= MaxLevel ? 0 : Mathf.Max(1, Mathf.RoundToInt(Base(StatRole.XpToNext, 100 * Level)));

        /// <summary>Stats montrées au joueur (celles de la progression, ou PV / dégâts / défense / XP à défaut).</summary>
        public IEnumerable<ProgressionStat> Stats => progression != null && progression.Stats.Count > 0 ? progression.Stats : FallbackStats;

        static readonly ProgressionStat[] FallbackStats =
        {
            new() { displayName = "PV", role = StatRole.MaxHealth, visibility = StatVisibility.Hud, format = StatFormat.Bar, color = new Color(0.86f, 0.25f, 0.28f) },
            new() { displayName = "XP", role = StatRole.XpToNext, visibility = StatVisibility.Hud, format = StatFormat.Bar, color = new Color(0.3f, 0.6f, 1f) },
            new() { displayName = "ATQ", role = StatRole.Damage, visibility = StatVisibility.Hud },
            new() { displayName = "DÉF", role = StatRole.Defense, visibility = StatVisibility.Hud }
        };

        /// <summary>Valeur effective d'une stat : base de la progression + bonus d'équipement pour les stats qui ont un rôle.</summary>
        public float Value(ProgressionStat stat) => stat.role switch
        {
            StatRole.MaxHealth => MaxHp,
            StatRole.Damage => Attack,
            StatRole.Defense => Defense,
            StatRole.Speed => MoveSpeed,
            StatRole.XpToNext => XpToNext,
            _ => stat.GetValue(Level) + AttributeBonus(stat)
        };

        #region Attributs et compétences

        public IReadOnlyList<ProgressionAttribute> Attributes => progression != null ? progression.Attributes : Array.Empty<ProgressionAttribute>();
        public IEnumerable<ProgressionSkill> UnlockedSkills => progression != null ? progression.Skills.Where(s => SkillRank(s) > 0) : Enumerable.Empty<ProgressionSkill>();

        public int Points(ProgressionAttribute attribute) => invested.TryGetValue(attribute.id, out var points) ? points : 0;

        public bool CanInvest(ProgressionAttribute attribute) => AttributePoints > 0 && Points(attribute) < attribute.maxPoints;

        /// <summary>Investit un point : les PV max gagnés sont aussi rendus au joueur.</summary>
        public bool Invest(ProgressionAttribute attribute)
        {
            if (!CanInvest(attribute))
                return false;
            var maxHp = MaxHp;
            AttributePoints--;
            invested[attribute.id] = Points(attribute) + 1;
            Hp = Mathf.Min(MaxHp, Hp + Mathf.Max(0, MaxHp - maxHp));
            return true;
        }

        public int SkillRank(ProgressionSkill skill) => skillRanks.TryGetValue(skill.id, out var rank) ? rank : 0;

        /// <summary>Somme des effets de ce type, au rang atteint de chaque compétence.</summary>
        public float SkillValue(SkillEffect effect) =>
            progression == null ? 0f : progression.Skills.Where(s => s.effect == effect).Sum(s => s.Value(SkillRank(s)));

        /// <summary>Récompenses du niveau atteint : points à répartir, compétences débloquées ou améliorées, notes.</summary>
        void ApplyRewards(int level)
        {
            if (progression == null)
                return;
            foreach (var reward in progression.RewardsAt(level))
            {
                switch (reward.type)
                {
                    case RewardType.AttributePoints when reward.amount > 0:
                        AttributePoints += reward.amount;
                        Message?.Invoke($"{reward.amount} point(s) d'attribut obtenu(s). Répartissez-les au prochain feu de camp.", new Color(1f, 0.85f, 0.3f));
                        break;
                    case RewardType.UnlockSkill or RewardType.UpgradeSkill:
                        var skill = progression.FindSkill(reward.skillId);
                        if (skill == null)
                            break;
                        var rank = SkillRank(skill);
                        // Débloquer : seulement si la compétence ne l'est pas encore. Améliorer : seulement une compétence déjà débloquée.
                        if ((reward.type == RewardType.UnlockSkill) != (rank == 0) || rank >= skill.MaxRank)
                            break;

                        skillRanks[skill.id] = rank + 1;
                        Message?.Invoke(rank == 0 ? $"Compétence '{skill.displayName}' débloquée." : $"La compétence '{skill.displayName}' passe au rang {rank + 1}.", skill.color);
                        break;
                    case RewardType.Note when !string.IsNullOrWhiteSpace(reward.note):
                        Message?.Invoke(reward.note, Color.white);
                        break;
                    case RewardType.UnlockMechanic or RewardType.UpgradeMechanic:
                        var mechanic = progression.FindMechanic(reward.mechanicId);
                        if (mechanic == null)
                            break;
                        var current = MechanicRank(mechanic);
                        if ((reward.type == RewardType.UnlockMechanic) != (current == 0) || current >= mechanic.MaxRank)
                            break;
                        mechanicRanks[mechanic.id] = current + 1;
                        Message?.Invoke(current == 0 ? $"Nouvelle action '{mechanic.displayName}' ({InputName(mechanic.input)})." : $"L'action '{mechanic.displayName}' passe au rang {current + 1}.",
                            mechanic.color);
                        break;
                }
            }
        }

        #endregion

        #region Mécaniques et jauges

        /// <summary>Actions du joueur : celles de la progression, ou tir + esquive à défaut.</summary>
        public IReadOnlyList<CharacterMechanic> Mechanics => progression != null && progression.Mechanics.Count > 0 ? progression.Mechanics : FallbackMechanics;

        static readonly CharacterMechanic[] FallbackMechanics =
        {
            new() { displayName = "Tir", action = MechanicAction.Projectile, input = MechanicInput.Primary, cooldown = 0.22f, power = 100f, count = 1 },
            new() { displayName = "Esquive", action = MechanicAction.Dash, input = MechanicInput.Dash, cooldown = 0.7f, power = 3f, duration = 0.18f }
        };

        public int MechanicRank(CharacterMechanic mechanic) => mechanicRanks.TryGetValue(mechanic.id, out var rank) ? rank : 0;

        public bool IsUnlocked(CharacterMechanic mechanic) => MechanicRank(mechanic) > 0;

        public MechanicValues ValuesOf(CharacterMechanic mechanic) => mechanic.At(Mathf.Max(1, MechanicRank(mechanic)));

        public ProgressionStat CostStat(CharacterMechanic mechanic) => progression != null ? progression.FindStat(mechanic.costStatId) : null;

        /// <summary>Niveau actuel d'une stat : PV actuels, jauge actuelle, ou valeur effective pour les autres stats.</summary>
        public float Current(ProgressionStat stat)
        {
            if (stat.role == StatRole.MaxHealth)
                return Hp;
            if (!stat.resource)
                return Value(stat);
            return resources.TryGetValue(stat.id, out var current) ? Mathf.Min(current, Value(stat)) : Value(stat);
        }

        /// <summary>Assez de la stat consommée ? Payer en PV ne peut jamais tuer le joueur.</summary>
        public bool CanAfford(CharacterMechanic mechanic)
        {
            var stat = CostStat(mechanic);
            var cost = ValuesOf(mechanic).Cost;
            if (stat == null || cost <= 0f)
                return true;
            return stat.role == StatRole.MaxHealth ? Hp > cost : Current(stat) >= cost;
        }

        /// <summary>Paie le coût d'une utilisation. Renvoie false (sans rien payer) si la stat ne suffit pas.</summary>
        public bool TrySpend(CharacterMechanic mechanic)
        {
            if (!CanAfford(mechanic))
                return false;
            var stat = CostStat(mechanic);
            var cost = ValuesOf(mechanic).Cost;
            if (stat == null || cost <= 0f)
                return true;
            if (stat.role == StatRole.MaxHealth)
                Hp = Mathf.Max(1, Hp - Mathf.CeilToInt(cost));
            else
                resources[stat.id] = Current(stat) - cost;
            return true;
        }

        public static string InputName(MechanicInput input) => input switch
        {
            MechanicInput.Primary => "clic gauche",
            MechanicInput.Secondary => "clic droit",
            MechanicInput.Dash => "Espace",
            MechanicInput.Ability1 => "touche 1",
            MechanicInput.Ability2 => "touche 2",
            _ => "touche 3"
        };

        #endregion

        public bool IsDead => Hp <= 0;

        /// <summary>Dégâts subis après défense (au moins 1). Renvoie les dégâts réellement infligés.</summary>
        public int TakeDamage(int raw)
        {
            var reduction = Mathf.Clamp(Passive(EffectType.DamageReduction), 0f, EffectInfo.MaxDamageReduction);
            raw = Mathf.Max(1, Mathf.RoundToInt(raw * (1f - reduction / 100f)));
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
                ApplyRewards(Level);
                Hp = MaxHp;
                resources.Clear();
                Message?.Invoke($"Niveau {Level} atteint.", new Color(1f, 0.85f, 0.3f));
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
            // Les jauges se rechargent.
            foreach (var stat in Stats.Where(s => s.resource && s.regenPerSecond > 0f))
                resources[stat.id] = Mathf.Min(Value(stat), Current(stat) + stat.regenPerSecond * deltaTime);

            // Régénération permanente des compétences et des passifs de l'arme.
            var passive = SkillValue(SkillEffect.Regeneration) + Passive(EffectType.Regeneration);
            if (passive > 0f && Hp < MaxHp)
            {
                passiveRegenCarry += passive * deltaTime;
                var healed = Mathf.FloorToInt(passiveRegenCarry);
                passiveRegenCarry -= healed;
                Heal(healed);
            }

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

        // --- IDialogueState ---
        public int PlayerLevel => Level;
        public bool HasFlag(string flag) => flags.Contains(flag);

        public int MechanicLevel(string mechanicId) => mechanicRanks.TryGetValue(mechanicId ?? string.Empty, out var rank) ? rank : 0;

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
