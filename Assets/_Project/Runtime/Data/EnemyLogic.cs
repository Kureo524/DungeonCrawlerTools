using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TheDungeon.Data
{
    /// <summary>Ce qu'un ennemi sait de la situation : fourni par le jeu, et simulé par l'aperçu de l'outil Ennemis.</summary>
    public interface IEnemySituation
    {
        /// <summary>Distance au joueur, en cases.</summary>
        float PlayerDistance { get; }
        /// <summary>Aucun mur entre l'ennemi et le joueur.</summary>
        bool PlayerInSight { get; }
        /// <summary>PV du joueur, de 0 à 1.</summary>
        float PlayerHealth { get; }
        /// <summary>PV de l'ennemi, de 0 à 1.</summary>
        float OwnHealth { get; }
        int PlayerLevel { get; }
        /// <summary>Niveau d'une mécanique du personnage, 0 si elle n'est pas débloquée.</summary>
        int MechanicLevel(string mechanicId);
        bool HasFlag(string flag);
        int Floor { get; }
        BiomeData Biome { get; }
        RoomType RoomType { get; }
        /// <summary>Autres ennemis encore en vie dans la salle.</summary>
        int Allies { get; }
        /// <summary>Secondes depuis le début du combat de l'ennemi.</summary>
        float FightTime { get; }
        /// <summary>Phase actuelle de l'ennemi (0 = départ).</summary>
        int Phase => 0;
    }

    /// <summary>Choix du pattern d'un ennemi, partagé par le jeu, l'aperçu de l'outil et le Validator.</summary>
    public static class EnemyLogic
    {
        public static bool IsMet(EnemyCondition condition, IEnemySituation situation) => condition.type switch
        {
            EnemyConditionType.PlayerCloserThan => situation.PlayerDistance < condition.value,
            EnemyConditionType.PlayerFartherThan => situation.PlayerDistance > condition.value,
            EnemyConditionType.PlayerInSight => situation.PlayerInSight,
            EnemyConditionType.PlayerHidden => !situation.PlayerInSight,
            EnemyConditionType.PlayerHealthBelow => situation.PlayerHealth * 100f < condition.value,
            EnemyConditionType.OwnHealthBelow => situation.OwnHealth * 100f < condition.value,
            EnemyConditionType.PlayerLevelAtLeast => situation.PlayerLevel >= condition.value,
            EnemyConditionType.MechanicAtLeast => !string.IsNullOrEmpty(condition.flag) && situation.MechanicLevel(condition.flag) >= condition.value,
            EnemyConditionType.FlagSet => !string.IsNullOrEmpty(condition.flag) && situation.HasFlag(condition.flag),
            EnemyConditionType.FloorIn => FloorSet.Contains(condition.value, situation.Floor),
            EnemyConditionType.BiomeIs => condition.biome != null && situation.Biome == condition.biome,
            EnemyConditionType.RoomTypeIs => situation.RoomType == condition.roomType,
            EnemyConditionType.AlliesAtMost => situation.Allies <= condition.value,
            EnemyConditionType.FightLongerThan => situation.FightTime > condition.value,
            EnemyConditionType.PhaseIs => situation.Phase == condition.value,
            _ => true
        };

        public static bool AllMet(IEnumerable<EnemyCondition> conditions, IEnemySituation situation) =>
            conditions == null || conditions.All(condition => IsMet(condition, situation));

        /// <summary>Première règle de déplacement dont toutes les conditions sont remplies, ou -1 (comportement de base).</summary>
        public static int ActivePattern(IReadOnlyList<EnemyPattern> patterns, IEnemySituation situation)
        {
            for (var i = 0; i < patterns.Count; i++)
                if (AllMet(patterns[i].conditions, situation))
                    return i;
            return -1;
        }

        public static bool InRange(AttackSettings settings, float distance) => distance >= settings.minRange && distance <= settings.maxRange;

        /// <summary>Attaques utilisables maintenant : rechargées (<paramref name="ready"/>), joueur à portée, conditions remplies, poids non nul.</summary>
        /// <param name="resolved">Attaques effectives (<see cref="EnemyAttack.Resolve"/>), dans le même ordre.</param>
        public static List<int> AvailableAttacks(IReadOnlyList<EnemyAttack> attacks, IReadOnlyList<ResolvedAttack> resolved, IEnemySituation situation,
            Func<int, bool> ready = null)
        {
            var available = new List<int>();
            for (var i = 0; i < attacks.Count; i++)
                if (resolved[i] != null && attacks[i].weight > 0 && (ready == null || ready(i)) && WhyNot(attacks[i], resolved[i], situation) == null)
                    available.Add(i);
            return available;
        }

        /// <summary>Chance de chaque attaque d'être lancée maintenant (0 pour celles qui ne sont pas utilisables), d'après les poids.</summary>
        public static float[] AttackChances(IReadOnlyList<EnemyAttack> attacks, IReadOnlyList<ResolvedAttack> resolved, IEnemySituation situation,
            Func<int, bool> ready = null)
        {
            var chances = new float[attacks.Count];
            var available = AvailableAttacks(attacks, resolved, situation, ready);
            var total = available.Sum(i => attacks[i].weight);
            foreach (var i in available)
                chances[i] = (float)attacks[i].weight / total;
            return chances;
        }

        /// <summary>
        /// Attaque lancée : tirée au hasard parmi les attaques utilisables, proportionnellement à leur poids ; -1 s'il n'y en a aucune.
        /// Sans <paramref name="random"/> (aperçu, tests) : la plus probable (la plus haute dans la liste en cas d'égalité).
        /// </summary>
        public static int ChooseAttack(IReadOnlyList<EnemyAttack> attacks, IReadOnlyList<ResolvedAttack> resolved, IEnemySituation situation,
            Func<int, bool> ready = null, System.Random random = null)
        {
            var available = AvailableAttacks(attacks, resolved, situation, ready);
            if (available.Count == 0)
                return -1;
            if (random == null)
                return available.OrderByDescending(i => attacks[i].weight).First();
            var pick = random.NextDouble() * available.Sum(i => attacks[i].weight);
            foreach (var i in available)
            {
                pick -= attacks[i].weight;
                if (pick < 0)
                    return i;
            }
            return available[^1];
        }

        /// <summary>Pourquoi une attaque n'est pas lancée en ce moment (null si elle peut l'être).</summary>
        public static string WhyNot(EnemyAttack attack, ResolvedAttack resolved, IEnemySituation situation)
        {
            if (resolved == null)
                return "aucune attaque choisie";
            if (attack.weight <= 0)
                return "poids 0";
            if (resolved.Steps.Count == 0)
                return "aucune étape";
            if (!InRange(resolved.Settings, situation.PlayerDistance))
                return situation.PlayerDistance < resolved.Settings.minRange ? "joueur trop près" : "joueur hors de portée";
            if (resolved.UsesProjectiles && !situation.PlayerInSight)
                return "joueur caché par un mur";
            return AllMet(attack.conditions, situation) ? null : "condition non remplie";
        }

        /// <summary>Une étape est-elle jouée ? Évalué au moment où elle commence.</summary>
        /// <param name="previousHit">L'étape jouée juste avant a touché le joueur.</param>
        /// <param name="hits">Coups ayant touché depuis le début de l'attaque.</param>
        /// <param name="wasHit">L'attaquant a été touché pendant l'étape jouée juste avant (contre-attaque, parade).</param>
        public static bool StepAllowed(AttackStep step, bool isFirst, bool previousHit, int hits, IEnemySituation situation, bool wasHit = false)
        {
            var trigger = isFirst ? StepTrigger.Always : step.trigger;
            var ok = trigger switch
            {
                StepTrigger.PreviousHit => previousHit,
                StepTrigger.PreviousMissed => !previousHit,
                StepTrigger.HitsAtLeast => hits >= step.hitCount,
                StepTrigger.WasHit => wasHit,
                _ => true
            };
            return ok && AllMet(step.conditions, situation);
        }

        /// <summary>
        /// Centres des zones d'une étape : sur l'attaquant, sur la cible, en chapelet devant l'attaquant, là où tombe le lancer,
        /// ou au hasard près de l'attaquant ou de la cible. Sans <paramref name="random"/> (aperçu), les zones « au hasard »
        /// sont réparties régulièrement.
        /// </summary>
        public static List<Vector2> ZoneOrigins(AttackStep step, Vector2 self, Vector2 aim, Vector2 target, System.Random random = null)
        {
            var origins = new List<Vector2>();
            var count = Mathf.Max(1, step.zoneCount);
            Vector2 Scatter(int i, float radius)
            {
                if (random == null)
                {
                    if (count == 1)
                        return Vector2.zero;
                    var angle = i * Mathf.PI * 2f / count;
                    return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius * 0.7f;
                }
                var a = (float)random.NextDouble() * Mathf.PI * 2f;
                return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius * Mathf.Sqrt((float)random.NextDouble());
            }
            for (var i = 0; i < count; i++)
            {
                switch (step.anchor)
                {
                    case ZoneAnchor.Target:
                        origins.Add(target + (i == 0 ? Vector2.zero : Scatter(i, step.radius * 2f)));
                        break;
                    case ZoneAnchor.Ahead:
                        origins.Add(self + aim * (step.anchorDistance + i * Mathf.Max(0.5f, step.radius * 1.6f)));
                        break;
                    case ZoneAnchor.Thrown:
                        var landing = self + Vector2.ClampMagnitude(target - self, Mathf.Max(0.5f, step.anchorDistance));
                        origins.Add(landing + (i == 0 ? Vector2.zero : Scatter(i, step.radius * 2f)));
                        break;
                    case ZoneAnchor.RandomNearSelf:
                        origins.Add(self + Scatter(i, step.anchorDistance));
                        break;
                    case ZoneAnchor.RandomNearTarget:
                        origins.Add(target + Scatter(i, step.anchorDistance));
                        break;
                    default:
                        origins.Add(self);
                        break;
                }
            }
            return origins;
        }

        /// <summary>
        /// La cible (en <paramref name="target"/>) est-elle dans la zone de l'étape, <paramref name="time"/> secondes après l'impact ?
        /// Tient compte d'une zone qui avance, grandit ou pivote. <paramref name="aim"/> : direction visée.
        /// </summary>
        /// <param name="tolerance">Rayon de la cible : elle est touchée dès que son bord entre dans la zone.</param>
        public static bool InZone(AttackStep step, Vector2 origin, Vector2 aim, Vector2 target, float tolerance = 0.3f, float time = 0f)
        {
            aim = aim.sqrMagnitude > 0.0001f ? aim.normalized : Vector2.right;
            origin += aim * step.travelSpeed * time;
            if (step.sweepAngle != 0f && step.activeDuration > 0f)
                aim = Rotate(aim, step.sweepAngle * Mathf.Clamp01(time / step.activeDuration) - step.sweepAngle / 2f);
            var grow = step.growth * time;
            var radius = step.radius + grow;
            var offset = target - origin;
            var distance = offset.magnitude;
            switch (step.shape)
            {
                case ZoneShape.Circle:
                    return distance <= radius + tolerance;
                case ZoneShape.Ring:
                    return distance <= radius + tolerance && distance >= step.innerRadius + grow - tolerance;
                case ZoneShape.Cone:
                    if (distance <= tolerance)
                        return true;
                    return distance <= radius + tolerance && Vector2.Angle(aim, offset) <= step.angle / 2f;
                case ZoneShape.Line:
                    var along = Vector2.Dot(offset, aim);
                    var across = Mathf.Abs(Vector2.Dot(offset, new Vector2(-aim.y, aim.x)));
                    return along >= -tolerance && along <= radius + tolerance && across <= step.width / 2f + tolerance;
                default:
                    return false;
            }
        }

        /// <summary>Orientation de départ d'une zone qui pivote : le balayage est centré sur la visée.</summary>
        public static Vector2 Rotate(Vector2 v, float degrees)
        {
            var r = degrees * Mathf.Deg2Rad;
            return new Vector2(v.x * Mathf.Cos(r) - v.y * Mathf.Sin(r), v.x * Mathf.Sin(r) + v.y * Mathf.Cos(r));
        }

        /// <summary>Résultat simulé d'une étape (aperçu de l'outil) : jouée ou non, et si elle toucherait la cible immobile.</summary>
        public struct StepOutcome
        {
            public bool Played;
            public bool Hit;
            public string Reason;
            /// <summary>Position de l'attaquant au début de l'étape, et à la fin (après une ruée ou une téléportation).</summary>
            public Vector2 Enemy;
            public Vector2 End;
            /// <summary>Centres des zones (frappe) ; le premier sert de repère.</summary>
            public List<Vector2> Origins;
            public Vector2 Origin => Origins != null && Origins.Count > 0 ? Origins[0] : End;
            public Vector2 Aim;
        }

        /// <summary>
        /// Déroule une attaque contre une cible immobile (aperçu de l'outil) : conditions de chaque étape, déplacements, zones et projectiles.
        /// Un projectile touche si la cible est en vue ; une frappe, si la cible est dans une de ses zones à un moment de sa durée.
        /// « Si l'attaquant a été touché » est supposé faux.
        /// </summary>
        public static List<StepOutcome> Simulate(ResolvedAttack attack, IEnemySituation situation, Vector2 enemy, Vector2 player, Func<Vector2, Vector2, bool> inSight)
        {
            var outcomes = new List<StepOutcome>();
            var previousHit = false;
            var hits = 0;
            var played = 0;
            var moved = new MovedSituation(situation);
            for (var i = 0; i < attack.Steps.Count; i++)
            {
                var step = attack.Steps[i];
                moved.Distance = Vector2.Distance(enemy, player);
                moved.Sight = inSight(enemy, player);
                var outcome = new StepOutcome { Enemy = enemy, End = enemy, Aim = Direction(enemy, player) };
                if (!StepAllowed(step, played == 0, previousHit, hits, moved))
                {
                    outcome.Reason = step.fallback == StepFallback.EndAttack ? "condition non remplie : l'attaque s'arrête" : "condition non remplie : sautée";
                    outcomes.Add(outcome);
                    if (step.fallback == StepFallback.EndAttack)
                        break;
                    continue;
                }
                outcome.Played = true;
                played++;
                var stepHits = 0;
                for (var r = 0; r < Mathf.Max(1, step.repeat); r++)
                    if (SimulateOnce(step, attack.Tempo, ref enemy, player, inSight, ref outcome))
                        stepHits++;
                outcome.End = enemy;
                outcome.Hit = stepHits > 0;
                previousHit = outcome.Hit;
                hits += stepHits;
                outcome.Reason = !step.CanHit ? "jouée" : outcome.Hit ? (step.repeat > 1 ? $"touche ({stepHits}×)" : "touche") : "rate";
                outcomes.Add(outcome);
            }
            return outcomes;
        }

        /// <summary>Une répétition d'une étape : déplacement pendant la préparation, puis l'action. Renvoie vrai si elle touche la cible.</summary>
        static bool SimulateOnce(AttackStep step, float tempo, ref Vector2 enemy, Vector2 player, Func<Vector2, Vector2, bool> inSight, ref StepOutcome outcome)
        {
            var hit = false;
            var aim = Direction(enemy, player);
            outcome.Aim = aim;
            if (step.moveDuring != StepMove.None && step.action != StepAction.Dash)
            {
                enemy = MoveDuringWindup(step, tempo, enemy, player, aim);
                aim = Direction(enemy, player);
            }
            switch (step.action)
            {
                case StepAction.Strike:
                    outcome.Origins = ZoneOrigins(step, enemy, aim, player);
                    var duration = Mathf.Max(0f, step.activeDuration);
                    var samples = duration > 0f ? 8 : 1;
                    var inZone = false;
                    foreach (var origin in outcome.Origins)
                        for (var t = 0; t < samples && !inZone; t++)
                            inZone = InZone(step, origin, aim, player, 0.3f, duration * t / Mathf.Max(1, samples - 1));
                    if (step.damagePercent > 0 && inZone)
                        hit = true;
                    break;
                case StepAction.Projectiles:
                    // Un cercle clairsemé peut passer à côté : on le compte comme touchant à partir de 8 projectiles.
                    if (step.damagePercent > 0 && inSight(enemy, player) && (step.pattern != ProjectilePattern.Ring || step.projectiles >= 8))
                        hit = true;
                    break;
                case StepAction.Dash:
                    var direction = step.dashDirection switch
                    {
                        DashDirection.AwayFromPlayer => -aim,
                        DashDirection.Sideways => new Vector2(-aim.y, aim.x),
                        _ => aim
                    };
                    var distance = Vector2.Distance(enemy, player);
                    var reach = step.dashDirection == DashDirection.TowardPlayer && !step.airborne ? Mathf.Min(step.dashDistance, Mathf.Max(0f, distance - 0.8f)) : step.dashDistance;
                    if (step.damagePercent > 0 && step.dashDirection == DashDirection.TowardPlayer && step.dashDistance >= distance - 0.8f)
                        hit = true;
                    enemy += direction * reach;
                    break;
                case StepAction.Teleport:
                    enemy = step.teleportTo switch
                    {
                        TeleportTarget.BehindTarget => player + aim * Mathf.Max(1f, step.anchorDistance * 0.5f),
                        TeleportTarget.NearTarget => player - aim * Mathf.Max(1f, step.anchorDistance * 0.5f),
                        TeleportTarget.AwayFromTarget => player - aim * Mathf.Max(2f, step.anchorDistance * 2f),
                        _ => enemy + new Vector2(-aim.y, aim.x) * step.anchorDistance
                    };
                    break;
            }
            return hit;
        }

        /// <summary>Position de l'attaquant après s'être déplacé pendant la préparation (il s'arrête au contact s'il avance vers la cible).</summary>
        static Vector2 MoveDuringWindup(AttackStep step, float tempo, Vector2 enemy, Vector2 player, Vector2 aim)
        {
            var move = step.moveDuring switch
            {
                StepMove.AwayFromTarget => -aim,
                StepMove.Strafe => new Vector2(-aim.y, aim.x),
                _ => aim
            };
            var travel = step.moveSpeed * Mathf.Max(0.1f, step.windup) * tempo;
            if (step.moveDuring == StepMove.TowardTarget)
                travel = Mathf.Min(travel, Mathf.Max(0f, Vector2.Distance(enemy, player) - 0.8f));
            return enemy + move * travel;
        }

        static Vector2 Direction(Vector2 from, Vector2 to) => (to - from).sqrMagnitude > 0.0001f ? (to - from).normalized : Vector2.right;

        /// <summary>Situation vue depuis la position de l'ennemi pendant l'attaque (après une ruée).</summary>
        sealed class MovedSituation : IEnemySituation
        {
            readonly IEnemySituation inner;
            public float Distance;
            public bool Sight;
            public MovedSituation(IEnemySituation inner) => this.inner = inner;
            public float PlayerDistance => Distance;
            public bool PlayerInSight => Sight;
            public float PlayerHealth => inner.PlayerHealth;
            public float OwnHealth => inner.OwnHealth;
            public int PlayerLevel => inner.PlayerLevel;
            public int MechanicLevel(string mechanicId) => inner.MechanicLevel(mechanicId);
            public bool HasFlag(string flag) => inner.HasFlag(flag);
            public int Floor => inner.Floor;
            public BiomeData Biome => inner.Biome;
            public RoomType RoomType => inner.RoomType;
            public int Allies => inner.Allies;
            public float FightTime => inner.FightTime;
            public int Phase => inner.Phase;
        }

        /// <summary>La ligne de vue demande un lancer de rayon : on l'évite quand rien ne s'en sert.</summary>
        public static bool NeedsSight(EnemyData enemy) =>
            enemy.Attacks.Any(a => a.attack != null && a.attack.Steps.Any(step => step.action == StepAction.Projectiles)) ||
            enemy.Patterns.SelectMany(p => p.conditions).Concat(enemy.Attacks.SelectMany(a => a.conditions))
                .Any(c => c.type is EnemyConditionType.PlayerInSight or EnemyConditionType.PlayerHidden);
    }
}
