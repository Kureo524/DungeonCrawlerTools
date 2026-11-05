using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using UnityEngine;

namespace TheDungeon.Game
{
    /// <summary>Ce qui peut être touché et subir un effet (poison, étourdissement, projection…).</summary>
    public interface IStatusTarget
    {
        Vector2 Position { get; }
        void ApplyStatus(StatusEffect status, float power, float duration, Vector2 direction);
    }

    /// <summary>Celui qui lance une attaque de l'outil Attaques : un ennemi, ou le joueur avec son arme.</summary>
    public interface IAttackActor
    {
        DungeonGame Game { get; }
        Faction Faction { get; }
        Transform Transform { get; }
        /// <summary>Dégâts de base, avant les pourcentages de l'attaque et de l'étape.</summary>
        int BaseDamage { get; }
        int MaxHp { get; }
        /// <summary>Direction visée : vers le joueur pour un ennemi, la visée du joueur pour son arme.</summary>
        Vector2 Aim { get; }
        /// <summary>Point visé : le joueur pour un ennemi ; pour le joueur, l'ennemi le plus proche dans la visée, sinon un point devant lui.</summary>
        Vector2 TargetPoint { get; }
        IEnemySituation Situation { get; }
        RoomInstance Room { get; }
        /// <summary>Le joueur maintient la touche (attaque chargée) ; toujours faux pour un ennemi.</summary>
        bool Holding { get; }
        void SetVelocity(Vector2 velocity);
        void Flash(Color color);
        void Heal(int amount);
        void SelfDamage(int amount);
        void SetPhase(int phase);
        /// <summary>Effets de l'arme transmis aux cibles (joueur) ; null pour un ennemi.</summary>
        IReadOnlyList<ItemEffect> OnHitEffects { get; }
    }

    /// <summary>
    /// Joue une attaque de l'outil Attaques, étape par étape. Une attaque lancée va jusqu'au bout (elle peut rater) ;
    /// chaque étape décide au moment où elle commence si elle est jouée. Gère aussi les renforcements en cours
    /// (dégâts, vitesse, armure, invulnérabilité) et la garde de l'étape en cours.
    /// </summary>
    public sealed class AttackRunner : MonoBehaviour
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

    /// <summary>Boule lancée en cloche jusqu'au point d'impact d'une zone, pendant la préparation.</summary>
    public sealed class ThrownOrb : MonoBehaviour
    {
        Vector2 from;
        Vector2 to;
        float duration;
        float age;

        public static void Launch(Vector2 from, Vector2 to, float duration)
        {
            var go = new GameObject("Projectile lancé");
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = GameFx.Circle;
            renderer.color = new Color(0.9f, 0.45f, 0.35f);
            renderer.sortingOrder = 9;
            var orb = go.AddComponent<ThrownOrb>();
            orb.from = from;
            orb.to = to;
            orb.duration = Mathf.Max(0.05f, duration);
            orb.Update();
        }

        void Update()
        {
            age += Time.deltaTime;
            var t = Mathf.Clamp01(age / duration);
            // Arc : la boule monte puis redescend, et grossit au sommet.
            var height = Mathf.Sin(Mathf.PI * t) * Mathf.Max(1f, Vector2.Distance(from, to) * 0.4f);
            transform.position = Vector2.Lerp(from, to, t) + Vector2.up * height;
            transform.localScale = Vector3.one * (0.5f + 0.3f * Mathf.Sin(Mathf.PI * t));
            if (t >= 1f)
                Destroy(gameObject);
        }
    }
}
