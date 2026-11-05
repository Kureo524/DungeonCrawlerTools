using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using UnityEngine;

namespace TheDungeon.Editor.Progression
{
    /// <summary>
    /// Le personnage à un niveau donné, calculé avec les règles du jeu (GameState, PlayerController) :
    /// effort en XP, points gagnés, compétences et chiffres réels de chaque mécanique.
    /// Les points d'attributs ne sont pas investis : c'est le joueur qui les répartit.
    /// </summary>
    public sealed class ProgressionSimulation
    {
        public sealed class ActionResult
        {
            public CharacterMechanic Mechanic;
            public int Rank;
            /// <summary>Niveau où la mécanique est obtenue, ou 0 si elle ne l'est jamais.</summary>
            public int UnlockLevel;
            public string Summary;
            public readonly List<string> Details = new();
            public bool Available => Rank > 0;
        }

        public int Level { get; private set; }
        public bool HasXp { get; private set; }
        public int XpCumulative { get; private set; }
        public int XpToNext { get; private set; }
        public int EnemiesTotal { get; private set; }
        public int EnemiesToNext { get; private set; }
        public int AttributePoints { get; private set; }
        public List<ActionResult> Actions { get; } = new();
        public List<(ProgressionSkill skill, int rank)> Skills { get; } = new();

        static string N(float value, string format = "0.#") => value.ToString(format, CultureInfo.InvariantCulture);

        public static ProgressionSimulation At(PlayerProgression progression, int level, float xpPerEnemy)
        {
            var result = new ProgressionSimulation { Level = Mathf.Clamp(level, 1, Mathf.Max(1, progression.MaxLevel)) };
            var l = result.Level;
            result.ComputeEffort(progression, Mathf.Max(1f, xpPerEnemy));
            result.AttributePoints = progression.Rewards.Where(r => r.type == RewardType.AttributePoints && r.level <= l).Sum(r => r.amount);
            foreach (var skill in progression.Skills)
                result.Skills.Add((skill, ProgressionEditing.RankAt(progression, skill, l)));

            var character = new Character(progression, l, result.Skills);
            foreach (var mechanic in progression.Mechanics)
                result.Actions.Add(Simulate(progression, mechanic, l, character));
            return result;
        }

        /// <summary>XP cumulée pour atteindre le niveau, XP vers le suivant, et ennemis à vaincre pour l'une et l'autre.</summary>
        void ComputeEffort(PlayerProgression progression, float perEnemy)
        {
            var xp = progression.Find(StatRole.XpToNext);
            HasXp = xp != null && xp.values.Count > 0;
            if (!HasXp)
                return;
            XpCumulative = Mathf.RoundToInt(Enumerable.Range(1, Level - 1).Sum(k => xp.GetValue(k)));
            XpToNext = Level < progression.MaxLevel ? Mathf.RoundToInt(xp.GetValue(Level)) : 0;
            EnemiesTotal = Mathf.CeilToInt(XpCumulative / perEnemy);
            EnemiesToNext = Mathf.CeilToInt(XpToNext / perEnemy);
        }

        /// <summary>Ce dont les chiffres des actions dépendent au niveau simulé : stats et effets des compétences.</summary>
        sealed class Character
        {
            public readonly float Attack;
            public readonly float Health;
            public readonly ProgressionStat Speed;
            public readonly float FireRate;
            public readonly float DashReduction;
            public readonly int ExtraProjectiles;

            public Character(PlayerProgression progression, int level, List<(ProgressionSkill skill, int rank)> skills)
            {
                float SkillValue(SkillEffect effect) => skills.Where(s => s.skill.effect == effect).Sum(s => s.skill.Value(s.rank));
                Attack = progression.Find(StatRole.Damage)?.GetValue(level) ?? 0f;
                Health = progression.Find(StatRole.MaxHealth)?.GetValue(level) ?? 0f;
                Speed = progression.Find(StatRole.Speed);
                FireRate = 1f + Mathf.Max(0f, SkillValue(SkillEffect.FireRate)) / 100f;
                DashReduction = 1f - Mathf.Clamp(SkillValue(SkillEffect.DashCooldown) / 100f, 0f, 0.8f);
                ExtraProjectiles = Mathf.Clamp(Mathf.RoundToInt(SkillValue(SkillEffect.ExtraProjectiles)), 0, 8);
            }
        }

        static ActionResult Simulate(PlayerProgression progression, CharacterMechanic mechanic, int level, Character character)
        {
            var action = new ActionResult
            {
                Mechanic = mechanic,
                Rank = ProgressionEditing.RankAt(progression, mechanic, level),
                UnlockLevel = Enumerable.Range(1, progression.MaxLevel).FirstOrDefault(k => ProgressionEditing.RankAt(progression, mechanic, k) > 0)
            };
            var v = mechanic.At(Mathf.Max(1, action.Rank));
            Describe(action, mechanic, v, level, character);
            AddCostDetails(action, progression.FindStat(mechanic.costStatId), v, level);
            return action;
        }

        /// <summary>Résumé et détails propres à l'action : dégâts, distance, soin, invincibilité, vitesse.</summary>
        static void Describe(ActionResult action, CharacterMechanic mechanic, MechanicValues v, int level, Character character)
        {
            switch (mechanic.action)
            {
                case MechanicAction.Projectile or MechanicAction.Nova:
                {
                    var count = v.Count + (mechanic.action == MechanicAction.Projectile ? character.ExtraProjectiles : 0);
                    var hit = Mathf.Max(1, Mathf.RoundToInt(character.Attack * v.Power / 100f));
                    var cooldown = v.Cooldown / character.FireRate;
                    action.Summary = $"{hit * count} dégâts par utilisation";
                    action.Details.Add($"{count} × {hit}");
                    if (cooldown > 0f)
                        action.Details.Add($"≈ {Mathf.RoundToInt(hit * count / cooldown)} dégâts/s");
                    action.Details.Add($"recharge {N(cooldown, "0.##")} s");
                    break;
                }
                case MechanicAction.Dash:
                    action.Summary = $"ruée de {N(v.Power)} cases";
                    action.Details.Add($"{N(v.Duration, "0.##")} s d'invincibilité");
                    action.Details.Add($"recharge {N(v.Cooldown * character.DashReduction, "0.##")} s");
                    break;
                case MechanicAction.Heal:
                    action.Summary = $"+{N(v.Power, "0")} PV";
                    if (character.Health > 0f)
                        action.Details.Add($"{N(100f * v.Power / character.Health, "0")} % des PV max");
                    action.Details.Add($"recharge {N(v.Cooldown, "0.##")} s");
                    break;
                case MechanicAction.Shield:
                    action.Summary = $"{N(v.Duration, "0.##")} s d'invincibilité";
                    action.Details.Add($"recharge {N(v.Cooldown, "0.##")} s");
                    break;
                case MechanicAction.WeaponSkill:
                    action.Summary = "compétence de l'arme équipée";
                    action.Details.Add($"recharge {N(v.Cooldown, "0.##")} s");
                    break;
                default:
                    action.Summary = $"+{N(v.Power, "0")} % de vitesse";
                    action.Details.Add($"pendant {N(v.Duration, "0.##")} s");
                    if (character.Speed != null)
                        action.Details.Add($"{N(character.Speed.GetValue(level) * (1f + v.Power / 100f))} au lieu de {N(character.Speed.GetValue(level))}");
                    break;
            }
        }

        /// <summary>Coût : nombre d'utilisations avec la jauge pleine et temps pour la remplir, ou quantité dépensée.</summary>
        static void AddCostDetails(ActionResult action, ProgressionStat cost, MechanicValues v, int level)
        {
            if (cost != null && v.Cost > 0f)
            {
                if (cost.resource)
                {
                    var gauge = cost.GetValue(level);
                    var uses = Mathf.FloorToInt(gauge / v.Cost);
                    action.Details.Add($"{uses} utilisation{(uses > 1 ? "s" : "")} avec {cost.displayName} pleine");
                    if (cost.regenPerSecond > 0f)
                        action.Details.Add($"{cost.displayName} pleine en {N(gauge / cost.regenPerSecond)} s");
                }
                else
                    action.Details.Add($"coûte {N(v.Cost)} {cost.displayName}");
            }
        }

        /// <summary>XP moyenne rapportée par un ennemi ordinaire au premier étage, ou 25 s'il n'y a pas encore d'ennemi.</summary>
        public static float AverageEnemyXp()
        {
            var enemies = AssetFactory.LoadAll<EnemyData>().Where(e => !e.IsBoss).ToList();
            return enemies.Count > 0 ? (float)enemies.Average(e => e.GetXpReward(1)) : 25f;
        }

        /// <summary>
        /// Ennemis à vaincre pour atteindre chaque niveau depuis le précédent (index 0 : niveau 1, point de départ, donc 0),
        /// ou null si la progression n'a pas de stat d'XP ou qu'un seul niveau.
        /// </summary>
        public static List<int> EnemiesPerLevel(PlayerProgression progression, float xpPerEnemy)
        {
            var xp = progression.Find(StatRole.XpToNext);
            if (xp == null || progression.MaxLevel <= 1)
                return null;
            var perEnemy = Mathf.Max(1f, xpPerEnemy);
            return Enumerable.Range(1, progression.MaxLevel).Select(level => level == 1 ? 0 : Mathf.CeilToInt(xp.GetValue(level - 1) / perEnemy)).ToList();
        }

        /// <summary>Écart de la stat avec le niveau précédent (0 au niveau 1).</summary>
        public static float Delta(ProgressionStat stat, int level) => level > 1 ? stat.GetValue(level) - stat.GetValue(level - 1) : 0f;

        /// <summary>Écart signé, au format de la stat : « +18 », « −2 », « ±0 ».</summary>
        public static string Signed(ProgressionStat stat, float delta) =>
            (delta > 0f ? "+" : delta < 0f ? "−" : "±") + stat.Format(Mathf.Abs(delta));

        /// <summary>Stats dont la valeur change en atteignant ce niveau (aucune au niveau 1).</summary>
        public static IEnumerable<ProgressionStat> Changed(PlayerProgression progression, int level) =>
            level <= 1 ? Enumerable.Empty<ProgressionStat>()
                : progression.Stats.Where(s => s.values.Count >= level && !Mathf.Approximately(s.values[level - 1], s.values[level - 2]));

        /// <summary>Stats qui posent problème à ce niveau : hors bornes, ou qui baissent (ou stagnent) alors que c'est interdit.</summary>
        public static IEnumerable<ProgressionStat> Problems(PlayerProgression progression, int level) =>
            progression.Stats.Where(s => s.values.Count >= level &&
                                         (s.values[level - 1] < s.min || s.values[level - 1] > s.max || ProgressionEditing.GrowthViolations(s).Contains(level - 1)));

        /// <summary>Résumé d'un niveau : « PV max +18, ATQ +1 · +1 point d'attribut ».</summary>
        public static string Summary(PlayerProgression progression, int level)
        {
            var changes = string.Join(", ", Changed(progression, level).Select(s => $"{s.displayName} {Signed(s, s.values[level - 1] - s.values[level - 2])}"));
            var rewards = ProgressionEditing.RewardSummary(progression, level);
            if (level == 1)
                changes = "Valeurs de départ";
            var text = string.Join("  ·  ", new[] { changes, rewards }.Where(t => !string.IsNullOrEmpty(t)));
            return string.IsNullOrEmpty(text) ? "Aucun changement" : text;
        }
    }
}
