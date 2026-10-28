using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using UnityEngine;

namespace TheDungeon.Game
{
    /// <summary>Joueur : déplacement, tir à la souris, esquive, interaction. Ses stats viennent de GameState.</summary>
    public sealed class PlayerController : MonoBehaviour, IDamageable
    {
        const float FireInterval = 0.22f;
        const float ProjectileSpeed = 13f;
        const float DashDuration = 0.18f;
        const float DashSpeed = 17f;
        const float DashCooldown = 0.7f;
        const float HurtInvulnerability = 0.6f;
        const float CriticalChance = 0.1f;

        DungeonGame game;
        Rigidbody2D body;
        SpriteRenderer sprite;
        SpriteFlash flash;
        float fireTimer;
        float dashTimer;
        float dashCooldown;
        float invulnerable;
        Vector2 dashDirection;
        Vector2 aim = Vector2.right;

        public Faction Faction => Faction.Player;
        public bool IsAlive => !game.State.IsDead;
        public bool IsDashing => dashTimer > 0f;
        public float DashReady => 1f - Mathf.Clamp01(dashCooldown / DashCooldown);

        public static PlayerController Spawn(DungeonGame game, Sprite sprite, Vector3 position)
        {
            var go = new GameObject("Joueur");
            go.transform.position = position;
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = 5;
            var body = go.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.freezeRotation = true;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            go.AddComponent<CircleCollider2D>().radius = 0.3f;
            go.AddComponent<SpriteFlash>();
            var player = go.AddComponent<PlayerController>();
            player.game = game;
            return player;
        }

        void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            sprite = GetComponent<SpriteRenderer>();
            flash = GetComponent<SpriteFlash>();
        }

        void Update()
        {
            if (!game.IsPlaying || game.State.IsDead)
            {
                body.linearVelocity = Vector2.zero;
                return;
            }

            fireTimer -= Time.deltaTime;
            dashCooldown -= Time.deltaTime;
            invulnerable -= Time.deltaTime;

            var screen = (Vector2)game.Camera.WorldToScreenPoint(transform.position);
            aim = GameInput.AimFrom(screen, aim);
            sprite.flipX = aim.x < 0f;

            var move = GameInput.Move;
            if (GameInput.Dash && dashCooldown <= 0f)
            {
                dashDirection = move.sqrMagnitude > 0.01f ? move.normalized : aim;
                dashTimer = DashDuration;
                dashCooldown = DashCooldown;
            }

            if (dashTimer > 0f)
            {
                dashTimer -= Time.deltaTime;
                body.linearVelocity = dashDirection * DashSpeed;
                sprite.color = new Color(1f, 1f, 1f, 0.55f);
            }
            else
            {
                body.linearVelocity = move * game.State.MoveSpeed;
                if (sprite.color.a < 1f)
                    sprite.color = Color.white;
            }

            if (GameInput.Fire && fireTimer <= 0f)
                Shoot();
            if (GameInput.UsePotion && !game.State.Use(game.State.FirstConsumable))
                game.Toast("Aucun consommable (F)", new Color(1f, 0.6f, 0.5f));
            if (GameInput.Interact)
                game.Nearest(transform.position)?.Interact(game);
        }

        void Shoot()
        {
            fireTimer = FireInterval;
            var critical = Random.value < CriticalChance;
            var damage = critical ? game.State.Attack * 2 : game.State.Attack;
            var weapon = game.State.Weapon;
            var color = weapon != null ? game.Database.GetRarityColor(weapon.Rarity) : new Color(1f, 0.95f, 0.7f);
            Projectile.Spawn(transform.position + (Vector3)(aim * 0.4f), aim, ProjectileSpeed, damage, Faction.Player, color,
                1f, weapon != null ? weapon.Effects : null, critical);
        }

        public void TakeHit(int damage, Vector2 direction, IReadOnlyList<ItemEffect> effects, bool critical)
        {
            if (invulnerable > 0f || IsDashing || game.State.IsDead || !game.IsPlaying)
                return;
            var dealt = game.State.TakeDamage(damage);
            invulnerable = HurtInvulnerability;
            flash.Flash(new Color(1f, 0.3f, 0.3f), 0.15f);
            body.AddForce(direction * 3f, ForceMode2D.Impulse);
            game.CameraFollow.Shake(0.25f);
            GameFx.Float(transform.position, $"-{dealt}", new Color(1f, 0.35f, 0.35f));
            if (game.State.IsDead)
                game.OnPlayerDied();
        }
    }

    /// <summary>Ennemi : comportement défini dans l'outil Ennemis, stats calculées pour l'étage, butin tiré de sa table.</summary>
    public sealed class EnemyController : MonoBehaviour, IDamageable
    {
        const float SpawnDelay = 0.7f;
        const float ContactCooldown = 0.8f;

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
        int summoned;

        public EnemyData Data { get; private set; }
        public int Hp { get; private set; }
        public int MaxHp { get; private set; }
        public float LastHit { get; private set; } = -10f;
        public Faction Faction => Faction.Enemy;
        public bool IsAlive => Hp > 0;
        public bool IsBoss => Data.IsBoss;

        int Damage => Data.GetDamage(floor);
        int Defense => Data.GetDefense(floor);
        float Speed => Mathf.Max(0.5f, Data.MoveSpeed);

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
            if (stunTimer > 0f)
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

            switch (Data.Behaviour)
            {
                case EnemyBehaviour.Melee:
                    Move(direction * Speed);
                    break;
                case EnemyBehaviour.Ranged:
                    Move(distance < 4f ? -direction * Speed : distance > 6.5f ? direction * Speed : Strafe(direction) * Speed * 0.6f);
                    if (attackTimer <= 0f)
                        Shoot(direction, 1.6f);
                    break;
                case EnemyBehaviour.Charger:
                    UpdateCharge(direction);
                    break;
                case EnemyBehaviour.Summoner:
                    Move(distance < 5f ? -direction * Speed : Strafe(direction) * Speed * 0.5f);
                    if (attackTimer <= 0f)
                        Shoot(direction, 2.2f, 0.7f);
                    if (specialTimer <= 0f)
                        Summon();
                    break;
                case EnemyBehaviour.Stationary:
                    if (attackTimer <= 0f)
                    {
                        Burst(IsBoss ? 12 : 6, 5f);
                        attackTimer = 2.4f;
                    }
                    break;
            }

            // Les boss ajoutent une salve en cercle, quel que soit leur comportement.
            if (IsBoss && Data.Behaviour != EnemyBehaviour.Stationary && specialTimer <= 0f)
            {
                Burst(14, 5.5f);
                specialTimer = 3.5f;
            }

            var reach = 0.45f * transform.localScale.x + 0.35f;
            if (distance < reach && contactTimer <= 0f)
            {
                player.TakeHit(Damage, direction, null, false);
                contactTimer = ContactCooldown;
            }
        }

        void Move(Vector2 velocity)
        {
            if (body.bodyType == RigidbodyType2D.Dynamic)
                body.linearVelocity = velocity;
        }

        Vector2 Strafe(Vector2 direction) => new Vector2(-direction.y, direction.x) * (GetInstanceID() % 2 == 0 ? 1f : -1f);

        void Shoot(Vector2 direction, float interval, float scale = 1f)
        {
            attackTimer = interval * Random.Range(0.85f, 1.15f);
            Projectile.Spawn(transform.position, direction, 6.5f, Damage, Faction.Enemy, new Color(1f, 0.4f, 0.35f), scale * (IsBoss ? 1.5f : 1f));
        }

        void Burst(int count, float speed)
        {
            var offset = Random.Range(0f, 360f);
            for (var i = 0; i < count; i++)
            {
                var angle = (offset + 360f * i / count) * Mathf.Deg2Rad;
                Projectile.Spawn(transform.position, new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)), speed, Damage, Faction.Enemy,
                    new Color(1f, 0.55f, 0.2f), IsBoss ? 1.3f : 1f);
            }
        }

        /// <summary>Charge : avance lentement, s'arrête et clignote (annonce), puis fonce en ligne droite.</summary>
        void UpdateCharge(Vector2 direction)
        {
            if (chargeTimer > 0f)
            {
                chargeTimer -= Time.deltaTime;
                if (chargeTimer > 0.6f)
                {
                    Move(Vector2.zero);
                    flash.Flash(new Color(1f, 0.8f, 0.3f), 0.05f);
                    chargeDirection = direction;
                }
                else
                {
                    Move(chargeDirection * Speed * 4f);
                }
                return;
            }
            Move(direction * Speed * 0.6f);
            if (attackTimer <= 0f)
            {
                chargeTimer = 1.1f;
                attackTimer = 3f;
            }
        }

        /// <summary>Invoque un ennemi ordinaire du biome de la salle (3 au plus).</summary>
        void Summon()
        {
            specialTimer = 5f;
            if (summoned >= 3 || room.Data.Biome == null)
                return;
            var minion = room.Data.Biome.Enemies
                .Where(e => e.enemy != null && !e.enemy.IsBoss && e.enemy.Behaviour != EnemyBehaviour.Summoner)
                .OrderBy(e => e.enemy.GetHealth(floor))
                .Select(e => e.enemy)
                .FirstOrDefault();
            if (minion == null)
                return;
            summoned++;
            Spawn(game, minion, floor, room, transform.position + (Vector3)Random.insideUnitCircle.normalized);
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

        public void TakeHit(int damage, Vector2 direction, IReadOnlyList<ItemEffect> effects, bool critical)
        {
            if (!IsAlive || spawnTimer > 0f)
                return;
            var dealt = Combat.Mitigate(damage, Defense);
            if (body.bodyType == RigidbodyType2D.Dynamic && !IsBoss)
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
