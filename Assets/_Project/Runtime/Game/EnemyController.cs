using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using UnityEngine;

namespace TheDungeon.Game
{
    /// <summary>
    /// Ennemi : patterns conditionnels et comportement par défaut définis dans l'outil Ennemis,
    /// stats calculées pour l'étage, butin tiré de sa table.
    /// </summary>
    public sealed partial class EnemyController : MonoBehaviour, IDamageable, IEnemySituation, IAttackActor, IStatusTarget
    {
        const float SpawnDelay = 0.7f;
        const float ContactCooldown = 0.8f;
        // Les conditions sont réévaluées quelques fois par seconde : assez pour réagir, sans lancer de rayon à chaque image.
        const float PatternCheckInterval = 0.25f;

        DungeonGame game;
        RoomInstance room;
        Rigidbody2D body;
        SpriteRenderer sprite;
        SpriteFlash flash;
        int floor;
        float spawnTimer = SpawnDelay;
        float attackTimer;
        float contactTimer;
        float specialTimer;
        float chargeTimer;
        float stunTimer;
        float dotPerSecond;
        float dotRemaining;
        float dotCarry;
        Vector2 chargeDirection;
        float chargeCooldown;
        int summoned;
        float fightTime;
        float patternCheckTimer;
        int activePattern = -1;
        bool playerInSight = true;
        float playerDistance;
        AttackRunner runner;
        float slowTimer;
        float slowPercent;
        float spawnTick;
        readonly List<EnemyController> phaseSpawns = new();

        /// <summary>Phase actuelle (0 = départ), changée par une étape d'attaque « Changer de phase ».</summary>
        public int Phase { get; private set; }
        EnemyPhase CurrentPhase => Data.Phase(Phase);

        public EnemyData Data { get; private set; }
        public int Hp { get; private set; }
        public int MaxHp { get; private set; }
        public float LastHit { get; private set; } = -10f;
        public Faction Faction => Faction.Enemy;
        public bool IsAlive => Hp > 0;
        public bool IsBoss => Data.IsBoss;

        int Damage => Mathf.RoundToInt(Data.GetDamage(floor) * (CurrentPhase?.damagePercent ?? 100) / 100f * (runner != null ? runner.DamageMultiplier : 1f));
        int Defense => Data.GetDefense(floor);
        float Speed => Mathf.Max(0.5f, Data.MoveSpeed) * (CurrentPhase?.speedPercent ?? 100) / 100f * (runner != null ? runner.SpeedMultiplier : 1f) *
                       (slowTimer > 0f ? 1f - Mathf.Clamp(slowPercent, 0f, 90f) / 100f : 1f);

        public static EnemyController Spawn(DungeonGame game, EnemyData data, int floor, RoomInstance room, Vector3 position)
        {
            var go = new GameObject(data.DisplayName);
            go.transform.position = position;
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = data.Sprite != null ? data.Sprite : GameFx.Circle;
            renderer.sortingOrder = 5;
            var body = go.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.freezeRotation = true;
            body.linearDamping = 4f;
            if (data.Behaviour == EnemyBehaviour.Stationary)
                body.bodyType = RigidbodyType2D.Kinematic;
            go.AddComponent<CircleCollider2D>().radius = 0.35f;
            go.AddComponent<SpriteFlash>();
            if (data.IsBoss)
                go.transform.localScale = Vector3.one * 2f;

            var enemy = go.AddComponent<EnemyController>();
            enemy.game = game;
            enemy.Data = data;
            enemy.floor = floor;
            enemy.room = room;
            enemy.MaxHp = enemy.Hp = Mathf.Max(1, data.GetHealth(floor));
            enemy.attackTimer = Random.Range(0.5f, 1.5f);
            enemy.specialTimer = 3f;
            enemy.runner = AttackRunner.For(enemy, go);
            room.Alive.Add(enemy);
            return enemy;
        }

        void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            sprite = GetComponent<SpriteRenderer>();
            flash = GetComponent<SpriteFlash>();
        }

        void Update()
        {
            if (!game.IsPlaying || !IsAlive)
            {
                if (body.bodyType == RigidbodyType2D.Dynamic)
                    body.linearVelocity = Vector2.zero;
                return;
            }

            // Apparition : l'ennemi grandit et n'attaque pas tout de suite, pour laisser le temps de réagir.
            if (spawnTimer > 0f)
            {
                spawnTimer -= Time.deltaTime;
                var scale = (IsBoss ? 2f : 1f) * Mathf.Clamp01(1f - spawnTimer / SpawnDelay);
                transform.localScale = Vector3.one * Mathf.Max(0.1f, scale);
                return;
            }

            TickDamageOverTime();
            // Une attaque lancée n'est pas interrompue par l'étourdissement : elle va jusqu'au bout.
            if (stunTimer > 0f && !Attacking)
            {
                stunTimer -= Time.deltaTime;
                Move(Vector2.zero);
                return;
            }

            var player = game.Player;
            if (player == null)
                return;
            var toPlayer = (Vector2)(player.transform.position - transform.position);
            var distance = toPlayer.magnitude;
            var direction = distance > 0.01f ? toPlayer / distance : Vector2.zero;
            sprite.flipX = direction.x < 0f;

            attackTimer -= Time.deltaTime;
            contactTimer -= Time.deltaTime;
            specialTimer -= Time.deltaTime;
            chargeCooldown -= Time.deltaTime;
            slowTimer -= Time.deltaTime;
            fightTime += Time.deltaTime;
            playerDistance = distance;
            UpdatePhase(player);

            UpdatePattern(player.transform.position);
            // Attaques définies dans l'outil : pendant une attaque, l'ennemi ne se déplace pas.
            var customAttacks = Data.Attacks.Count > 0;
            var attacking = customAttacks && UpdateAttacks();
            if (attacking)
                sprite.flipX = direction.x < 0f;
            else if (activePattern >= 0)
                RunPattern(Data.Patterns[activePattern], direction, distance);
            else
                RunDefaultBehaviour(direction, distance, !customAttacks);

            var reach = 0.45f * transform.localScale.x + 0.35f;
            // Pas de dégâts au contact pendant un saut (l'ennemi passe par-dessus le joueur).
            if (distance < reach && contactTimer <= 0f && !runner.Invulnerable)
            {
                player.TakeHit(Damage, direction, null, false);
                contactTimer = ContactCooldown;
            }
        }

        /// <summary>Effets de la phase en cours : aura autour de l'ennemi, renforts à intervalle régulier.</summary>
        void UpdatePhase(PlayerController player)
        {
            var phase = CurrentPhase;
            if (phase == null)
                return;
            if (phase.auraRadius > 0f && Vector2.Distance(player.transform.position, transform.position) <= phase.auraRadius)
                player.ApplyStatus(phase.auraStatus, phase.auraPower, 0.5f, ((Vector2)(player.transform.position - transform.position)).normalized);
            if (phase.spawn == null)
                return;
            phaseSpawns.RemoveAll(e => e == null || !e.IsAlive);
            spawnTick -= Time.deltaTime;
            if (spawnTick > 0f || phaseSpawns.Count >= phase.spawnMax)
                return;
            spawnTick = Mathf.Max(0.5f, phase.spawnInterval);
            var spawned = Summon(phase.spawn, (Vector2)transform.position + Random.insideUnitCircle.normalized * 2.5f, force: true);
            if (spawned != null)
                phaseSpawns.Add(spawned);
        }

        public void SetPhase(int number)
        {
            if (number == Phase)
                return;
            Phase = number;
            spawnTick = 1f;
            var phase = CurrentPhase;
            flash.Flash(new Color(0.8f, 0.4f, 1f), 0.4f);
            game.CameraFollow.Shake(0.4f);
            if (phase != null && !string.IsNullOrWhiteSpace(phase.name))
                GameFx.Float(transform.position + Vector3.up * 1.2f, $"[{phase.name}]", new Color(0.85f, 0.6f, 1f), 1.4f);
        }

        /// <summary>Choisit le pattern à jouer : le premier dont les conditions sont remplies (voir l'outil Ennemis).</summary>
        void UpdatePattern(Vector3 playerPosition)
        {
            if (Data.Patterns.Count == 0 && Data.Attacks.Count == 0)
                return;
            patternCheckTimer -= Time.deltaTime;
            if (patternCheckTimer > 0f)
                return;
            patternCheckTimer = PatternCheckInterval;
            if (EnemyLogic.NeedsSight(Data))
                playerInSight = HasLineOfSight(playerPosition);
            var next = EnemyLogic.ActivePattern(Data.Patterns, this);
            if (next == activePattern)
                return;
            activePattern = next;
            // Changement de phase d'un boss : annoncé au joueur.
            if (IsBoss && next >= 0 && !string.IsNullOrWhiteSpace(Data.Patterns[next].name))
                GameFx.Float(transform.position + Vector3.up, $"[{Data.Patterns[next].name}]", new Color(1f, 0.55f, 0.3f), 1.2f);
        }

        /// <summary>Aucun mur entre l'ennemi et le joueur.</summary>
        bool HasLineOfSight(Vector3 playerPosition)
        {
            foreach (var hit in Physics2D.LinecastAll(transform.position, playerPosition))
                if (hit.collider is UnityEngine.Tilemaps.TilemapCollider2D or CompositeCollider2D)
                    return false;
            return true;
        }

        void RunPattern(EnemyPattern pattern, Vector2 direction, float distance)
        {
            var speed = Speed * Mathf.Max(0f, pattern.speedPercent) / 100f;
            switch (pattern.movement)
            {
                case PatternMovement.Chase:
                    Move(direction * speed);
                    break;
                case PatternMovement.KeepDistance:
                    Move(distance < 4f ? -direction * speed : distance > 6.5f ? direction * speed : Strafe(direction) * speed * 0.6f);
                    break;
                case PatternMovement.Circle:
                    Move(Strafe(direction) * speed);
                    break;
                case PatternMovement.Flee:
                    Move(-direction * speed);
                    break;
                case PatternMovement.Hold:
                    Move(Vector2.zero);
                    break;
                case PatternMovement.Charge:
                    UpdateCharge(direction, speed);
                    break;
            }

        }

        // --- IEnemySituation : ce que les conditions des patterns consultent ---
        public float PlayerDistance => playerDistance;
        public bool PlayerInSight => playerInSight;
        public float PlayerHealth => game.State.MaxHp > 0 ? (float)game.State.Hp / game.State.MaxHp : 1f;
        public float OwnHealth => MaxHp > 0 ? (float)Hp / MaxHp : 1f;
        public int PlayerLevel => game.State.Level;
        public int MechanicLevel(string mechanicId) => game.State.MechanicLevel(mechanicId);
        public bool HasFlag(string flag) => game.State.HasFlag(flag);
        public int Floor => floor;
        public BiomeData Biome => room.Data.Biome;
        public RoomType RoomType => room.Data.RoomType;
        public int Allies => room.Alive.Count(enemy => enemy != this && enemy != null && enemy.IsAlive);
        public float FightTime => fightTime;
        public Vector2 Position => transform.position;

        /// <summary>Effet infligé par une attaque du joueur (ou une aura, pour un ennemi touché par erreur).</summary>
        public void ApplyStatus(StatusEffect status, float power, float duration, Vector2 direction)
        {
            switch (status)
            {
                case StatusEffect.Poison or StatusEffect.Burn or StatusEffect.Rot:
                    dotPerSecond = Mathf.Max(dotPerSecond, power);
                    dotRemaining = Mathf.Max(dotRemaining, duration);
                    break;
                case StatusEffect.Frost:
                    slowTimer = Mathf.Max(slowTimer, duration);
                    slowPercent = Mathf.Max(slowPercent, power);
                    break;
                case StatusEffect.Bleed:
                    ApplyDamage(Mathf.Max(1, Mathf.RoundToInt(MaxHp * power / 100f)), new Color(0.85f, 0.1f, 0.15f), false);
                    break;
                case StatusEffect.Stun or StatusEffect.Launch:
                    if (!IsBoss && !runner.SuperArmor)
                        stunTimer = Mathf.Max(stunTimer, duration);
                    break;
                case StatusEffect.Knockback:
                    if (!IsBoss && !runner.SuperArmor && body.bodyType == RigidbodyType2D.Dynamic)
                        body.AddForce(direction * power, ForceMode2D.Impulse);
                    break;
            }
        }

        void TickDamageOverTime()
        {
            if (dotRemaining <= 0f)
                return;
            dotRemaining -= Time.deltaTime;
            dotCarry += dotPerSecond * Time.deltaTime;
            var whole = Mathf.FloorToInt(dotCarry);
            if (whole <= 0)
                return;
            dotCarry -= whole;
            ApplyDamage(whole, new Color(0.6f, 1f, 0.4f), false);
        }

        public void TakeHit(int damage, Vector2 direction, IReadOnlyList<ItemEffect> effects, bool critical) => TryHit(damage, direction, effects, critical);

        /// <summary>Blesse l'ennemi, sauf s'il est invulnérable (saut, téléportation…). Renvoie true si le coup a vraiment touché.</summary>
        public bool TryHit(int damage, Vector2 direction, IReadOnlyList<ItemEffect> effects, bool critical = false)
        {
            if (!IsAlive || spawnTimer > 0f || runner.Invulnerable)
                return false;
            runner.NotifyDamaged();
            // Garde de l'étape en cours, armure (renforcement) et résistance de la phase.
            var scaled = damage * runner.DamageTakenMultiplier * (CurrentPhase?.damageTakenPercent ?? 100) / 100f;
            var dealt = Combat.Mitigate(Mathf.Max(1, Mathf.RoundToInt(scaled)), Defense);
            if (body.bodyType == RigidbodyType2D.Dynamic && !IsBoss && !runner.SuperArmor)
                body.AddForce(direction * 2.5f, ForceMode2D.Impulse);

            // Effets de l'arme du joueur (définis dans l'outil Objets).
            if (effects != null)
            {
                foreach (var effect in effects)
                {
                    switch (effect.type)
                    {
                        case EffectType.Poison:
                        case EffectType.Burn:
                            dotPerSecond = Mathf.Max(dotPerSecond, effect.value);
                            dotRemaining = Mathf.Max(dotRemaining, Mathf.Max(1f, effect.duration));
                            break;
                        case EffectType.Stun:
                            if (!IsBoss)
                                stunTimer = Mathf.Max(stunTimer, Mathf.Max(0.3f, effect.duration));
                            break;
                        case EffectType.LifeSteal:
                            game.State.Heal(Mathf.Max(1, Mathf.RoundToInt(dealt * effect.value / 100f)));
                            break;
                    }
                }
            }
            ApplyDamage(dealt, critical ? new Color(1f, 0.9f, 0.2f) : Color.white, critical);
            return true;
        }

        void ApplyDamage(int amount, Color color, bool critical)
        {
            Hp -= amount;
            LastHit = Time.time;
            flash.Flash(Color.white, 0.08f);
            GameFx.Float(transform.position + Vector3.up * 0.3f * transform.localScale.x, critical ? $"{amount}!" : amount.ToString(), color,
                critical ? 1.4f : 1f);
            if (Hp <= 0)
                Die();
        }

        void Die()
        {
            room.Alive.Remove(this);
            game.OnEnemyKilled(this, room);
            Destroy(gameObject);
        }
    }
}
