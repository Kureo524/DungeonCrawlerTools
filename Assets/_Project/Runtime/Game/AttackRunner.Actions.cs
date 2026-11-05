using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using UnityEngine;

namespace TheDungeon.Game
{
    /// <summary>Actions des étapes : déplacement, zones, projectiles, ruée, téléportation, invocation, renforcement, absorption et coups portés.</summary>
    public sealed partial class AttackRunner
    {
        void Move(AttackStep step, Vector2 aim)
        {
            var direction = step.moveDuring switch
            {
                StepMove.TowardTarget => ((Vector2)actor.TargetPoint - (Vector2)actor.Transform.position).normalized,
                StepMove.AwayFromTarget => -((Vector2)actor.TargetPoint - (Vector2)actor.Transform.position).normalized,
                StepMove.Strafe => new Vector2(-aim.y, aim.x),
                StepMove.Forward => aim,
                _ => Vector2.zero
            };
            // Vers la cible : on s'arrête à son contact.
            if (step.moveDuring == StepMove.TowardTarget && Vector2.Distance(actor.TargetPoint, actor.Transform.position) < 0.9f)
                direction = Vector2.zero;
            actor.SetVelocity(direction * step.moveSpeed * SpeedMultiplier);
        }

        /// <summary>Zone de frappe : instantanée, ou qui dure (et peut avancer, grandir, pivoter) en blessant à intervalles réguliers.</summary>
        IEnumerator Zone(AttackStep step, Vector2 origin, Vector2 aim, int damage, float duration, HitCounter counter)
        {
            var marker = ZoneMarker.Show(step, origin, Start(step, aim), Mathf.Max(0.25f, duration), true);
            var nextTick = 0f;
            for (var t = 0f; ; t += Playing ? Time.deltaTime : 0f)
            {
                if (t >= nextTick)
                {
                    foreach (var victim in Victims())
                        if (EnemyLogic.InZone(step, origin, aim, victim.Position, HitTolerance, t) && Hit(victim, damage, (victim.Position - origin).normalized, step))
                            counter.Count++;
                    nextTick += Mathf.Max(0.1f, step.tickInterval);
                }
                if (marker != null && duration > 0f)
                {
                    var current = EnemyLogic.Rotate(Start(step, aim), step.sweepAngle * Mathf.Clamp01(t / duration));
                    marker.Place(origin + aim.normalized * step.travelSpeed * t, step.sweepAngle != 0f ? current : aim, 1f + step.growth * t / Mathf.Max(0.1f, step.radius));
                }
                if (t >= duration)
                    yield break;
                yield return null;
            }
        }

        void Projectiles(AttackStep step, Vector2 aim, int damage, HitCounter counter)
        {
            var count = Mathf.Max(1, step.projectiles);
            var center = Mathf.Atan2(aim.y, aim.x) * Mathf.Rad2Deg;
            var color = actor.Faction == Faction.Player ? new Color(1f, 0.95f, 0.7f) : new Color(1f, 0.4f, 0.35f);
            for (var i = 0; i < count; i++)
            {
                var angle = step.pattern switch
                {
                    ProjectilePattern.Ring => 360f * i / count,
                    ProjectilePattern.Spread => count == 1 ? center : center - step.spreadAngle / 2f + step.spreadAngle * i / (count - 1),
                    _ => count == 1 ? center : center - 4f * (count - 1) + 8f * i
                } * Mathf.Deg2Rad;
                var projectile = Projectile.Spawn(actor.Transform.position, new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)), Mathf.Max(1f, step.projectileSpeed),
                    Mathf.Max(1, damage), actor.Faction, color, 1f, actor.OnHitEffects, false, () => counter.Count++);
                projectile.Configure(step.homing, step.status, step.statusPower, step.statusDuration, actor.Faction == Faction.Enemy ? () => (Vector2)actor.TargetPoint : null);
            }
        }

        IEnumerator Dash(AttackStep step, Vector2 aim, int damage, HitCounter counter)
        {
            var direction = step.dashDirection switch
            {
                DashDirection.AwayFromPlayer => -aim,
                DashDirection.Sideways => new Vector2(-aim.y, aim.x) * (random.Next(2) == 0 ? 1f : -1f),
                _ => aim
            };
            var speed = Mathf.Max(1f, step.dashSpeed) * SpeedMultiplier;
            var travelled = 0f;
            var touched = new HashSet<IStatusTarget>();
            if (step.airborne)
                stepInvulnerable = true;
            var scale = actor.Transform.localScale;
            while (travelled < step.dashDistance)
            {
                if (Playing)
                {
                    actor.SetVelocity(direction * speed);
                    travelled += speed * Time.deltaTime;
                    if (step.airborne)
                        actor.Transform.localScale = scale * (1f + 0.25f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(travelled / step.dashDistance)));
                    else
                    {
                        // Au sol, la ruée blesse au contact ; vers la cible, elle s'arrête à son contact.
                        var stop = false;
                        foreach (var victim in Victims())
                        {
                            if (Vector2.Distance(victim.Position, actor.Transform.position) > 0.45f * scale.x + 0.45f || !touched.Add(victim))
                                continue;
                            if (damage > 0 && Hit(victim, damage, direction, step))
                                counter.Count++;
                            stop |= step.dashDirection == DashDirection.TowardPlayer && actor.Faction == Faction.Enemy;
                        }
                        if (stop)
                            break;
                    }
                }
                yield return null;
            }
            actor.Transform.localScale = scale;
            stepInvulnerable = false;
            actor.SetVelocity(Vector2.zero);
        }

        void Teleport(AttackStep step, Vector2 aim, Vector2 target)
        {
            var self = (Vector2)actor.Transform.position;
            var distance = Mathf.Max(1f, step.anchorDistance);
            var destination = step.teleportTo switch
            {
                TeleportTarget.BehindTarget => target + aim * Mathf.Min(distance, 2f),
                TeleportTarget.NearTarget => target - aim * Mathf.Min(distance, 2f),
                TeleportTarget.AwayFromTarget => target - aim * distance * 2f,
                _ => self + Random.insideUnitCircle.normalized * distance
            };
            var room = actor.Room;
            if (room != null)
            {
                var bounds = room.Bounds;
                destination = new Vector2(Mathf.Clamp(destination.x, bounds.xMin + 1.5f, bounds.xMax - 1.5f), Mathf.Clamp(destination.y, bounds.yMin + 1.5f, bounds.yMax - 1.5f));
            }
            actor.Transform.position = destination;
            actor.Flash(Color.white);
        }

        void Summon(AttackStep step)
        {
            var room = actor.Room;
            if (room == null || actor.Faction != Faction.Enemy)
                return;
            var enemy = actor as EnemyController;
            for (var i = 0; i < Mathf.Max(1, step.summons); i++)
                enemy?.Summon(step.summon, (Vector2)actor.Transform.position + Random.insideUnitCircle.normalized * Mathf.Max(1f, step.anchorDistance * 0.5f));
        }

        /// <summary>Applique un renforcement ; <paramref name="stacks"/> : nombre d'alliés absorbés (Absorption).</summary>
        void ApplyBuff(AttackStep step, int stacks)
        {
            if (stacks <= 0)
                return;
            var power = step.buffPower * stacks;
            if (step.buff == BuffType.Heal)
            {
                actor.Heal(Mathf.Max(1, Mathf.RoundToInt(actor.MaxHp * power / 100f)));
                return;
            }
            buffs[step.buff] = (power, Time.time + step.buffDuration);
            actor.Flash(new Color(0.7f, 0.5f, 1f));
            GameFx.Float(actor.Transform.position + Vector3.up * 0.6f, step.buff switch
            {
                BuffType.Damage => $"+{power:0} % dégâts",
                BuffType.Speed => $"+{power:0} % vitesse",
                BuffType.Armor => $"armure {power:0} %",
                _ => "invulnérable"
            }, new Color(0.75f, 0.6f, 1f), 0.8f);
        }

        /// <summary>Absorbe les alliés dans le rayon (0 = toute la salle) : ils disparaissent, et chacun renforce l'attaquant.</summary>
        int Absorb(AttackStep step)
        {
            var room = actor.Room;
            if (room == null)
                return 0;
            var absorbed = room.Alive.Where(e => e != null && !ReferenceEquals(e, actor) && e.IsAlive &&
                                                 (step.radius <= 0f || Vector2.Distance(e.transform.position, actor.Transform.position) <= step.radius)).ToList();
            foreach (var ally in absorbed)
                ally.Absorb(actor.Transform.position);
            ApplyBuff(step, absorbed.Count);
            return absorbed.Count;
        }

        /// <summary>Cibles possibles : le joueur pour un ennemi, les ennemis de la salle pour le joueur.</summary>
        IEnumerable<IStatusTarget> Victims()
        {
            var game = actor.Game;
            if (actor.Faction == Faction.Enemy)
            {
                if (game.Player != null)
                    yield return game.Player;
                yield break;
            }
            var room = game.CurrentRoom;
            if (room == null)
                yield break;
            foreach (var enemy in room.Alive.ToList())
                if (enemy != null && enemy.IsAlive)
                    yield return enemy;
        }

        /// <summary>Inflige les dégâts et l'effet de l'étape. Renvoie true si le coup a vraiment touché.</summary>
        bool Hit(IStatusTarget victim, int damage, Vector2 direction, AttackStep step)
        {
            var landed = victim switch
            {
                PlayerController player => damage <= 0 || player.TryHit(Mathf.Max(1, damage), direction),
                EnemyController enemy => damage <= 0 || enemy.TryHit(Mathf.Max(1, damage), direction, actor.OnHitEffects),
                _ => false
            };
            if (landed && step.status != StatusEffect.None)
                victim.ApplyStatus(step.status, step.statusPower, step.statusDuration, direction);
            return landed && damage > 0;
        }
    }
}
