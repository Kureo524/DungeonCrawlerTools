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
            var perEnemy = Mathf.Max(1f, xpPerEnemy);

            var xp = progression.Find(StatRole.XpToNext);
            result.HasXp = xp != null && xp.values.Count > 0;
            if (result.HasXp)
            {
                result.XpCumulative = Mathf.RoundToInt(Enumerable.Range(1, l - 1).Sum(k => xp.GetValue(k)));
                result.XpToNext = l < progression.MaxLevel ? Mathf.RoundToInt(xp.GetValue(l)) : 0;
                result.EnemiesTotal = Mathf.CeilToInt(result.XpCumulative / perEnemy);
                result.EnemiesToNext = Mathf.CeilToInt(result.XpToNext / perEnemy);
            }
            result.AttributePoints = progression.Rewards.Where(r => r.type == RewardType.AttributePoints && r.level <= l).Sum(r => r.amount);

            foreach (var skill in progression.Skills)
                result.Skills.Add((skill, ProgressionEditing.RankAt(progression, skill, l)));
            float SkillValue(SkillEffect effect) => result.Skills.Where(s => s.skill.effect == effect).Sum(s => s.skill.Value(s.rank));

            var attack = progression.Find(StatRole.Damage)?.GetValue(l) ?? 0f;
            var health = progression.Find(StatRole.MaxHealth)?.GetValue(l) ?? 0f;
            var speed = progression.Find(StatRole.Speed);
            var fireRate = 1f + Mathf.Max(0f, SkillValue(SkillEffect.FireRate)) / 100f;
            var dashReduction = 1f - Mathf.Clamp(SkillValue(SkillEffect.DashCooldown) / 100f, 0f, 0.8f);
            var extraProjectiles = Mathf.Clamp(Mathf.RoundToInt(SkillValue(SkillEffect.ExtraProjectiles)), 0, 8);

            foreach (var mechanic in progression.Mechanics)
            {
                var action = new ActionResult
                {
                    Mechanic = mechanic,
                    Rank = ProgressionEditing.RankAt(progression, mechanic, l),
                    UnlockLevel = Enumerable.Range(1, progression.MaxLevel).FirstOrDefault(k => ProgressionEditing.RankAt(progression, mechanic, k) > 0)
                };
                var v = mechanic.At(Mathf.Max(1, action.Rank));
                switch (mechanic.action)
                {
                    case MechanicAction.Projectile or MechanicAction.Nova:
                    {
                        var count = v.Count + (mechanic.action == MechanicAction.Projectile ? extraProjectiles : 0);
                        var hit = Mathf.Max(1, Mathf.RoundToInt(attack * v.Power / 100f));
                        var cooldown = v.Cooldown / fireRate;
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
                        action.Details.Add($"recharge {N(v.Cooldown * dashReduction, "0.##")} s");
                        break;
                    case MechanicAction.Heal:
                        action.Summary = $"+{N(v.Power, "0")} PV";
                        if (health > 0f)
                            action.Details.Add($"{N(100f * v.Power / health, "0")} % des PV max");
                        action.Details.Add($"recharge {N(v.Cooldown, "0.##")} s");
                        break;
                    case MechanicAction.Shield:
                        action.Summary = $"{N(v.Duration, "0.##")} s d'invincibilité";
                        action.Details.Add($"recharge {N(v.Cooldown, "0.##")} s");
                        break;
                    default:
                        action.Summary = $"+{N(v.Power, "0")} % de vitesse";
                        action.Details.Add($"pendant {N(v.Duration, "0.##")} s");
                        if (speed != null)
                            action.Details.Add($"{N(speed.GetValue(l) * (1f + v.Power / 100f))} au lieu de {N(speed.GetValue(l))}");
                        break;
                }

                var cost = progression.FindStat(mechanic.costStatId);
                if (cost != null && v.Cost > 0f)
                {
                    if (cost.resource)
                    {
                        var gauge = cost.GetValue(l);
                        var uses = Mathf.FloorToInt(gauge / v.Cost);
                        action.Details.Add($"{uses} utilisation{(uses > 1 ? "s" : "")} avec {cost.displayName} pleine");
                        if (cost.regenPerSecond > 0f)
                            action.Details.Add($"{cost.displayName} pleine en {N(gauge / cost.regenPerSecond)} s");
                    }
                    else
                        action.Details.Add($"coûte {N(v.Cost)} {cost.displayName}");
                }
                result.Actions.Add(action);
            }
            return result;
        }

        /// <summary>XP moyenne rapportée par un ennemi ordinaire au premier étage, ou 25 s'il n'y a pas encore d'ennemi.</summary>
        public static float AverageEnemyXp()
        {
            var enemies = AssetFactory.LoadAll<EnemyData>().Where(e => !e.IsBoss).ToList();
            return enemies.Count > 0 ? (float)enemies.Average(e => e.GetXpReward(1)) : 25f;
        }

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
