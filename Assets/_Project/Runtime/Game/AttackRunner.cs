using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using UnityEngine;

namespace TheDungeon.Game
{
    /// <summary>
    /// Joue une attaque de l'outil Attaques, étape par étape. Une attaque lancée va jusqu'au bout (elle peut rater) ;
    /// chaque étape décide au moment où elle commence si elle est jouée. Gère aussi les renforcements en cours
    /// (dégâts, vitesse, armure, invulnérabilité) et la garde de l'étape en cours.
    /// </summary>
    public sealed partial class AttackRunner : MonoBehaviour
    {
        const float HitTolerance = 0.3f;

        IAttackActor actor;
        Coroutine routine;
        readonly System.Random random = new();
        readonly Dictionary<BuffType, (float power, float until)> buffs = new();
        int damagedCount;

        public bool Running => routine != null;
        /// <summary>Réduction des dégâts de l'étape en cours (garde), en %.</summary>
        public float Guard { get; private set; }
        public bool SuperArmor { get; private set; }
        bool stepInvulnerable;

        public bool Invulnerable => stepInvulnerable || Active(BuffType.Invulnerable);
        public float DamageMultiplier => 1f + Power(BuffType.Damage) / 100f;
        public float SpeedMultiplier => 1f + Power(BuffType.Speed) / 100f;
        /// <summary>Part des dégâts reçus qui passe (garde de l'étape et renforcement « armure »).</summary>
        public float DamageTakenMultiplier => Mathf.Clamp01(1f - Guard / 100f) * Mathf.Clamp01(1f - Power(BuffType.Armor) / 100f);

        public static AttackRunner For(IAttackActor actor, GameObject host)
        {
            var runner = host.GetComponent<AttackRunner>() ?? host.AddComponent<AttackRunner>();
            runner.actor = actor;
            return runner;
        }

        bool Active(BuffType type) => buffs.TryGetValue(type, out var buff) && buff.until > Time.time;
        float Power(BuffType type) => Active(type) ? buffs[type].power : 0f;

        /// <summary>À appeler quand l'attaquant prend un coup : sert aux étapes « si l'attaquant a été touché » (contre, parade).</summary>
        public void NotifyDamaged() => damagedCount++;

        public void Run(ResolvedAttack attack, string announce, System.Action finished)
        {
            if (Running || attack == null)
                return;
            if (!string.IsNullOrWhiteSpace(announce))
                GameFx.Float(actor.Transform.position + Vector3.up, $"[{announce}]", new Color(1f, 0.55f, 0.3f), 1f);
            routine = StartCoroutine(Play(attack, finished));
        }

        bool Playing => actor.Game.IsPlaying;

        IEnumerator Wait(float seconds, System.Action<float> each = null)
        {
            for (var t = 0f; t < seconds; t += Playing ? Time.deltaTime : 0f)
            {
                each?.Invoke(t);
                yield return null;
            }
        }

        IEnumerator Play(ResolvedAttack attack, System.Action finished)
        {
            var previousHit = false;
            var wasHit = false;
            var hits = 0;
            var played = 0;
            var tempo = attack.Tempo;
            foreach (var step in attack.Steps)
            {
                if (!EnemyLogic.StepAllowed(step, played == 0, previousHit, hits, actor.Situation, wasHit))
                {
                    if (step.fallback == StepFallback.EndAttack)
                        break;
                    continue;
                }
                played++;
                var damagedBefore = damagedCount;
                var counter = new HitCounter();
                Guard = step.guardPercent;
                SuperArmor = step.superArmor;
                if (step.healthCost > 0f)
                    actor.SelfDamage(Mathf.Max(1, Mathf.RoundToInt(actor.MaxHp * step.healthCost / 100f)));
                for (var r = 0; r < Mathf.Max(1, step.repeat); r++)
                    yield return PlayOnce(step, attack, tempo, counter);
                previousHit = counter.Count > 0;
                hits += counter.Count;
                wasHit = damagedCount > damagedBefore;
            }
            Guard = 0f;
            SuperArmor = false;
            stepInvulnerable = false;
            actor.SetVelocity(Vector2.zero);
            routine = null;
            finished?.Invoke();
        }

        sealed class HitCounter
        {
            public int Count;
        }

        /// <summary>État d'une étape en cours : visée, cible, zones annoncées et dégâts (augmentés par une charge).</summary>
        sealed class StepState
        {
            public Vector2 Aim;
            public Vector2 Target;
            public List<Vector2> Origins;
            public List<ZoneMarker> Markers;
            public int Damage;
        }

        IEnumerator PlayOnce(AttackStep step, ResolvedAttack attack, float tempo, HitCounter counter)
        {
            // Préparation : annonce (clignote, zones au sol, projectile lancé en l'air), déplacement éventuel.
            var windup = Mathf.Max(0f, step.windup) * tempo;
            var state = Prepare(step, attack, windup);
            yield return Wait(windup, _ => Windup(step, state));

            // Attaque chargée (arme du joueur) : maintenir prolonge la préparation et augmente les dégâts.
            if (step.chargeable && actor.Holding)
            {
                var charged = 0f;
                while (actor.Holding && charged < step.chargeTime)
                {
                    if (Playing)
                        charged += Time.deltaTime;
                    actor.Flash(Color.Lerp(new Color(1f, 0.8f, 0.3f), Color.white, charged / Mathf.Max(0.01f, step.chargeTime)));
                    yield return null;
                }
                state.Damage = Mathf.RoundToInt(state.Damage * (1f + step.chargeBonus / 100f * Mathf.Clamp01(charged / Mathf.Max(0.01f, step.chargeTime))));
                state.Aim = actor.Aim;
            }

            var aim = state.Aim;
            switch (step.action)
            {
                case StepAction.Strike:
                    var duration = Mathf.Max(0f, step.activeDuration);
                    // Chaque zone vit sa vie (une flaque reste pendant que l'attaque continue), sauf si l'étape est canalisée.
                    foreach (var origin in state.Origins)
                        StartCoroutine(Zone(step, origin, aim, state.Damage, duration, counter));
                    if (step.channel && duration > 0f)
                        yield return Wait(duration, _ => Move(step, aim));
                    break;
                case StepAction.Dash:
                    yield return Dash(step, aim, state.Damage, counter);
                    break;
                default:
                    ActInstantly(step, state, counter);
                    break;
            }

            // Récupération : immobile. Les projectiles et zones qui touchent pendant ce temps comptent pour cette étape.
            yield return Wait(Mathf.Max(0f, step.recovery) * tempo, _ => actor.SetVelocity(Vector2.zero));
        }

        /// <summary>Début de la préparation : visée, dégâts, zones annoncées au sol et projectile lancé en l'air.</summary>
        StepState Prepare(AttackStep step, ResolvedAttack attack, float windup)
        {
            var self = (Vector2)actor.Transform.position;
            var state = new StepState
            {
                Aim = actor.Aim,
                Target = actor.TargetPoint,
                Damage = Mathf.RoundToInt(actor.BaseDamage * step.damagePercent / 100f * attack.Settings.damagePercent / 100f * DamageMultiplier)
            };
            state.Origins = step.action == StepAction.Strike ? EnemyLogic.ZoneOrigins(step, self, state.Aim, state.Target, random) : null;
            state.Markers = state.Origins?.Select(o => windup > 0f ? ZoneMarker.Show(step, o, Start(step, state.Aim), windup, false) : null).ToList();
            if (step.action == StepAction.Strike && step.anchor == ZoneAnchor.Thrown && windup > 0f)
                foreach (var origin in state.Origins)
                    ThrownOrb.Launch(self, origin, windup);
            if (step.action == StepAction.Teleport)
                stepInvulnerable = true;
            return state;
        }

        /// <summary>Chaque image de la préparation : déplacement, clignotement, et visée qui suit la cible si l'étape le demande.</summary>
        void Windup(AttackStep step, StepState state)
        {
            Move(step, state.Aim);
            actor.Flash(step.action == StepAction.Teleport ? new Color(1f, 1f, 1f, 0.3f) : new Color(1f, 0.8f, 0.3f));
            if (!step.trackDuringWindup)
                return;
            state.Aim = actor.Aim;
            state.Target = actor.TargetPoint;
            if (step.action != StepAction.Strike || step.anchor is ZoneAnchor.Thrown or ZoneAnchor.RandomNearSelf or ZoneAnchor.RandomNearTarget)
                return;
            state.Origins = EnemyLogic.ZoneOrigins(step, actor.Transform.position, state.Aim, state.Target, random);
            for (var i = 0; i < state.Markers.Count; i++)
                state.Markers[i]?.Place(state.Origins[i], Start(step, state.Aim));
        }

        /// <summary>Actions sans durée : projectiles, téléportation, invocation, renforcement, absorption, changement de phase.</summary>
        void ActInstantly(AttackStep step, StepState state, HitCounter counter)
        {
            switch (step.action)
            {
                case StepAction.Projectiles:
                    Projectiles(step, state.Aim, state.Damage, counter);
                    break;
                case StepAction.Teleport:
                    Teleport(step, state.Aim, state.Target);
                    stepInvulnerable = false;
                    break;
                case StepAction.Summon:
                    Summon(step);
                    break;
                case StepAction.Buff:
                    ApplyBuff(step, 1);
                    break;
                case StepAction.Absorb:
                    counter.Count += Absorb(step);
                    break;
                case StepAction.Phase:
                    actor.SetPhase(step.phase);
                    break;
            }
        }

        /// <summary>Orientation de départ de la zone : un balayage commence d'un côté de la visée.</summary>
        static Vector2 Start(AttackStep step, Vector2 aim) =>
            step.sweepAngle != 0f && step.activeDuration > 0f ? EnemyLogic.Rotate(aim, -step.sweepAngle / 2f) : aim;
    }
}
