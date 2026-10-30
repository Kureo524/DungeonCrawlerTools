using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace TheDungeon.Data
{
    /// <summary>Ce que le jeu fait d'une stat. « Aucun » : stat purement informative, affichée au joueur si on le souhaite.</summary>
    public enum StatRole
    {
        None,
        MaxHealth,
        Damage,
        Defense,
        Speed,
        XpToNext
    }

    /// <summary>Où le joueur voit la stat.</summary>
    public enum StatVisibility
    {
        Hidden,
        Hud,
        CharacterSheet
    }

    /// <summary>Comment la valeur est présentée au joueur.</summary>
    public enum StatFormat
    {
        Number,
        Bar,
        Percent
    }

    /// <summary>Contrainte d'évolution vérifiée par le Validator.</summary>
    public enum StatGrowth
    {
        Free,
        NeverDecreases,
        AlwaysIncreases
    }

    /// <summary>Effet en jeu d'une compétence. La valeur dépend du rang atteint.</summary>
    public enum SkillEffect
    {
        None,
        ExtraProjectiles,
        CriticalChance,
        FireRate,
        DashCooldown,
        Regeneration
    }

    /// <summary>Ce qu'apporte une récompense de niveau. Les nouvelles valeurs vont à la fin (les assets enregistrent le numéro).</summary>
    public enum RewardType
    {
        AttributePoints,
        UnlockSkill,
        UpgradeSkill,
        Note,
        UnlockMechanic,
        UpgradeMechanic
    }

    /// <summary>Ce que fait une mécanique du personnage quand le joueur l'utilise.</summary>
    public enum MechanicAction
    {
        Projectile,
        Nova,
        Dash,
        Heal,
        Shield,
        SpeedBoost,
        WeaponSkill
    }

    /// <summary>Touche qui déclenche une mécanique (clavier / manette).</summary>
    public enum MechanicInput
    {
        Primary,
        Secondary,
        Dash,
        Ability1,
        Ability2,
        Ability3
    }

    /// <summary>Valeurs d'une mécanique à un rang donné.</summary>
    public struct MechanicValues
    {
        public float Power;
        public float Cost;
        public float Cooldown;
        public int Count;
        public float Duration;
    }

    /// <summary>Ce qu'une amélioration ajoute (ou retire, si négatif) aux valeurs de la mécanique.</summary>
    [Serializable]
    public class MechanicUpgrade
    {
        public float power;
        public float cost;
        public float cooldown;
        public int count;
        public float duration;
    }

    /// <summary>
    /// Mécanique du personnage (tir, esquive, soin…) : une touche, une action, une stat consommée à chaque utilisation
    /// et un temps de recharge. Débloquée dès le début ou par une récompense de niveau ; chaque amélioration fait passer au rang suivant.
    /// </summary>
    [Serializable]
    public class CharacterMechanic
    {
        public string id = PlayerProgression.NewId();
        public string displayName = "Mécanique";
        [Tooltip("Explication montrée au joueur.")]
        public string description = string.Empty;
        public Color color = Color.white;
        public MechanicAction action;
        public MechanicInput input;
        [Tooltip("Disponible dès le début de la partie. Sinon, une récompense de niveau la débloque.")]
        public bool unlockedAtStart = true;
        [Tooltip("Stat consommée à chaque utilisation (une jauge, ou les PV). Vide : gratuite.")]
        public string costStatId = string.Empty;
        [Tooltip("Quantité de la stat consommée à chaque utilisation.")]
        public float cost;
        [Tooltip("Secondes avant de pouvoir la réutiliser.")]
        public float cooldown = 0.5f;
        [Tooltip("Projectile / onde : dégâts en % de l'attaque. Ruée : distance en cases. Soin : PV rendus. Sprint : % de vitesse en plus.")]
        public float power = 100f;
        [Tooltip("Projectile : nombre de projectiles par tir. Onde : nombre de projectiles tout autour.")]
        public int count = 1;
        [Tooltip("Ruée : durée du mouvement. Bouclier : durée d'invincibilité. Sprint : durée du bonus. En secondes.")]
        public float duration = 0.2f;
        [Tooltip("Améliorations successives : la première fait passer au rang 2.")]
        public List<MechanicUpgrade> upgrades = new();

        public int MaxRank => upgrades.Count + 1;

        /// <summary>Valeurs au rang donné : valeurs de base plus toutes les améliorations obtenues.</summary>
        public MechanicValues At(int rank)
        {
            var values = new MechanicValues { Power = power, Cost = cost, Cooldown = cooldown, Count = count, Duration = duration };
            for (var i = 0; i < Mathf.Min(rank - 1, upgrades.Count); i++)
            {
                values.Power += upgrades[i].power;
                values.Cost += upgrades[i].cost;
                values.Cooldown += upgrades[i].cooldown;
                values.Count += upgrades[i].count;
                values.Duration += upgrades[i].duration;
            }
            values.Cost = Mathf.Max(0f, values.Cost);
            values.Cooldown = Mathf.Max(0f, values.Cooldown);
            values.Count = Mathf.Max(1, values.Count);
            values.Duration = Mathf.Max(0f, values.Duration);
            return values;
        }
    }

    /// <summary>Une stat du joueur et sa valeur à chaque niveau (une courbe).</summary>
    [Serializable]
    public class ProgressionStat
    {
        [Tooltip("Identifiant stable, utilisé par les bonus d'attributs (ne change pas quand on renomme la stat).")]
        public string id = PlayerProgression.NewId();
        [Tooltip("Nom affiché au joueur et dans les outils.")]
        public string displayName = "Stat";
        [Tooltip("Ce que le jeu fait de cette stat.")]
        public StatRole role;
        public Color color = Color.white;
        public StatVisibility visibility = StatVisibility.CharacterSheet;
        public StatFormat format = StatFormat.Number;
        [Tooltip("Nombre de décimales des valeurs (0 = entiers).")]
        public int decimals;
        public float min;
        public float max = 100f;
        public StatGrowth growth = StatGrowth.NeverDecreases;
        [Tooltip("Jauge (endurance, mana…) : les mécaniques la consomment, elle se recharge, et la valeur du niveau est son maximum.")]
        public bool resource;
        [Tooltip("Jauge : quantité rendue par seconde.")]
        public float regenPerSecond;
        [Tooltip("Une valeur par niveau, la première correspond au niveau 1.")]
        public List<float> values = new();

        public float GetValue(int level) => values.Count == 0 ? 0f : values[Mathf.Clamp(level - 1, 0, values.Count - 1)];

        /// <summary>Valeur telle que le joueur la lit : « 12 », « 1.5 » ou « 12 % ».</summary>
        public string Format(float value)
        {
            var text = value.ToString(format == StatFormat.Percent ? "0.#" : "F" + Mathf.Clamp(decimals, 0, 3), CultureInfo.InvariantCulture);
            return format == StatFormat.Percent ? text + " %" : text;
        }
    }

    /// <summary>
    /// Effet d'un point d'attribut sur une stat. Au-delà du seuil (soft cap), chaque point rapporte moins :
    /// répartir ses points reste intéressant, comme dans un action-RPG à la Elden Ring.
    /// </summary>
    [Serializable]
    public class AttributeBonus
    {
        [Tooltip("Identifiant de la stat améliorée.")]
        public string statId;
        [Tooltip("Gain par point investi, jusqu'au seuil.")]
        public float perPoint = 1f;
        [Tooltip("Nombre de points à partir duquel le gain diminue (0 = pas de seuil).")]
        public int softCap;
        [Tooltip("Part du gain conservée au-delà du seuil (0,5 = moitié).")]
        public float afterSoftCap = 0.5f;

        /// <summary>Bonus total apporté par ce nombre de points.</summary>
        public float Total(int points)
        {
            if (softCap <= 0 || points <= softCap)
                return perPoint * Mathf.Max(0, points);
            return perPoint * (softCap + afterSoftCap * (points - softCap));
        }
    }

    /// <summary>Attribut dans lequel le joueur investit ses points (Vigueur, Force…) : chaque point améliore une ou plusieurs stats.</summary>
    [Serializable]
    public class ProgressionAttribute
    {
        public string id = PlayerProgression.NewId();
        public string displayName = "Attribut";
        [Tooltip("Explication montrée au joueur quand il répartit ses points.")]
        public string description = string.Empty;
        public Color color = Color.white;
        [Tooltip("Nombre maximum de points investis dans cet attribut.")]
        public int maxPoints = 10;
        public List<AttributeBonus> bonuses = new();

        /// <summary>Bonus apporté à une stat par ce nombre de points.</summary>
        public float BonusFor(string statId, int points)
        {
            var total = 0f;
            foreach (var bonus in bonuses)
                if (bonus.statId == statId)
                    total += bonus.Total(points);
            return total;
        }
    }

    /// <summary>Compétence débloquée puis améliorée par les récompenses de niveau. Une valeur par rang.</summary>
    [Serializable]
    public class ProgressionSkill
    {
        public string id = PlayerProgression.NewId();
        public string displayName = "Compétence";
        [Tooltip("Explication montrée au joueur.")]
        public string description = string.Empty;
        public Color color = Color.white;
        public SkillEffect effect;
        [Tooltip("Valeur de l'effet à chaque rang : la première correspond au rang 1 (déblocage).")]
        public List<float> ranks = new() { 1f };

        public int MaxRank => ranks.Count;

        public float Value(int rank) => rank <= 0 || ranks.Count == 0 ? 0f : ranks[Mathf.Clamp(rank - 1, 0, ranks.Count - 1)];

        /// <summary>Effet à ce rang, tel que le joueur le lit (« +1 projectile », « +5 % de coups critiques »…).</summary>
        public string EffectText(int rank)
        {
            var value = Value(rank).ToString("0.#", CultureInfo.InvariantCulture);
            return effect switch
            {
                SkillEffect.ExtraProjectiles => $"+{value} projectile(s) par tir",
                SkillEffect.CriticalChance => $"+{value} % de coups critiques",
                SkillEffect.FireRate => $"+{value} % de cadence de tir",
                SkillEffect.DashCooldown => $"-{value} % de recharge de l'esquive",
                SkillEffect.Regeneration => $"{value} PV par seconde",
                _ => description
            };
        }
    }

    /// <summary>Récompense obtenue en atteignant un niveau : points d'attributs, compétence débloquée ou améliorée, ou simple note.</summary>
    [Serializable]
    public class LevelReward
    {
        [Tooltip("Niveau auquel la récompense est obtenue (1 = dès le début de la partie).")]
        public int level = 2;
        public RewardType type;
        [Tooltip("Nombre de points d'attributs donnés.")]
        public int amount = 1;
        [Tooltip("Compétence débloquée ou améliorée.")]
        public string skillId = string.Empty;
        [Tooltip("Mécanique débloquée ou améliorée.")]
        public string mechanicId = string.Empty;
        [Tooltip("Texte libre (contenu débloqué, événement…).")]
        public string note = string.Empty;
    }

    /// <summary>Ancien format (une ligne par niveau, stats fixes). Lu uniquement pour convertir les anciennes progressions.</summary>
    [Serializable]
    public struct LevelEntry
    {
        public int maxHealth;
        public int damage;
        public int xpRequired;
        public string unlock;
    }

    [CreateAssetMenu(menuName = "The Dungeon/Player Progression", fileName = "PRG_Player")]
    public class PlayerProgression : ScriptableObject, IDisplayNamed, ISerializationCallbackReceiver
    {
        [SerializeField, Tooltip("Nom de la progression (ex : « Standard », « Difficile »).")]
        string displayName = "Progression";
        [SerializeField, Tooltip("Nombre de niveaux.")]
        int levelCount;
        [SerializeField]
        List<ProgressionStat> stats = new();
        [SerializeField, Tooltip("Attributs dans lesquels le joueur répartit ses points.")]
        List<ProgressionAttribute> attributes = new();
        [SerializeField]
        List<ProgressionSkill> skills = new();
        [SerializeField, Tooltip("Actions du personnage : tir, esquive, soin…")]
        List<CharacterMechanic> mechanics = new();
        [SerializeField, Tooltip("Récompenses obtenues en atteignant un niveau.")]
        List<LevelReward> rewards = new();
        [SerializeField, HideInInspector]
        List<string> unlocks = new();
        [SerializeField, HideInInspector]
        List<LevelEntry> levels = new();

        public string DisplayName => displayName;
        public int MaxLevel => levelCount;
        public IReadOnlyList<ProgressionStat> Stats => stats;
        public IReadOnlyList<ProgressionAttribute> Attributes => attributes;
        public IReadOnlyList<ProgressionSkill> Skills => skills;
        public IReadOnlyList<CharacterMechanic> Mechanics => mechanics;
        public IReadOnlyList<LevelReward> Rewards => rewards;

        public static string NewId() => Guid.NewGuid().ToString("N").Substring(0, 8);

        public ProgressionStat FindStat(string id) => stats.Find(s => s.id == id);
        public ProgressionSkill FindSkill(string id) => skills.Find(s => s.id == id);
        public CharacterMechanic FindMechanic(string id) => mechanics.Find(m => m.id == id);


        /// <summary>Récompenses obtenues en atteignant ce niveau.</summary>
        public IEnumerable<LevelReward> RewardsAt(int level)
        {
            foreach (var reward in rewards)
                if (reward.level == level)
                    yield return reward;
        }

        /// <summary>Première stat qui joue ce rôle, ou null.</summary>
        public ProgressionStat Find(StatRole role)
        {
            foreach (var stat in stats)
                if (stat.role == role)
                    return stat;
            return null;
        }

        public void OnBeforeSerialize()
        {
        }

        /// <summary>Convertit les anciens formats : tableau de niveaux en courbes (PV, dégâts, XP), déblocages texte en récompenses.</summary>
        public void OnAfterDeserialize()
        {
            MigrateUnlocks();
            if (stats != null)
                foreach (var stat in stats)
                    if (string.IsNullOrEmpty(stat.id))
                        stat.id = NewId();
            if (levels == null || levels.Count == 0 || (stats != null && stats.Count > 0))
                return;
            levelCount = levels.Count;
            stats = new List<ProgressionStat>
            {
                Legacy("PV max", StatRole.MaxHealth, new Color(0.86f, 0.25f, 0.28f), StatVisibility.Hud, StatFormat.Bar, 2000f, e => e.maxHealth),
                Legacy("Dégâts", StatRole.Damage, new Color(0.95f, 0.6f, 0.25f), StatVisibility.Hud, StatFormat.Number, 300f, e => e.damage),
                Legacy("XP requise", StatRole.XpToNext, new Color(0.3f, 0.6f, 1f), StatVisibility.Hud, StatFormat.Bar, 100000f, e => e.xpRequired)
            };
            stats[2].growth = StatGrowth.AlwaysIncreases;
            unlocks = levels.ConvertAll(e => e.unlock ?? string.Empty);
            levels.Clear();
            MigrateUnlocks();
        }

        void MigrateUnlocks()
        {
            if (unlocks == null || unlocks.Count == 0)
                return;
            rewards ??= new List<LevelReward>();
            for (var i = 0; i < unlocks.Count; i++)
                if (!string.IsNullOrWhiteSpace(unlocks[i]))
                    rewards.Add(new LevelReward { level = i + 1, type = RewardType.Note, note = unlocks[i] });
            unlocks.Clear();
        }

        ProgressionStat Legacy(string name, StatRole role, Color color, StatVisibility visibility, StatFormat format, float max, Func<LevelEntry, int> read) => new()
        {
            displayName = name,
            role = role,
            color = color,
            visibility = visibility,
            format = format,
            min = 0f,
            max = max,
            values = levels.ConvertAll(e => (float)read(e))
        };
    }
}
