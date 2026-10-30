using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using UnityEngine;

namespace TheDungeon.Game
{
    /// <summary>
    /// Joueur : déplacement, interaction et mécaniques de la progression (tir, esquive, soin…), chacune sur sa touche,
    /// avec son coût et sa recharge. Ses stats viennent de GameState.
    /// </summary>
    public sealed class PlayerController : MonoBehaviour, IDamageable, IStatusTarget, IAttackActor
    {
        const float ProjectileSpeed = 13f;
        const float HurtInvulnerability = 0.6f;
        const float CriticalChance = 0.1f;
        const float Spread = 12f;

        DungeonGame game;
        Rigidbody2D body;
        SpriteRenderer sprite;
        SpriteFlash flash;
        readonly Dictionary<CharacterMechanic, float> cooldowns = new();
        float dashTimer;
        float dashSpeed;
        float shieldTimer;
        float boostTimer;
        float boostPercent;
        float invulnerable;
        Vector2 dashDirection;
        Vector2 aim = Vector2.right;
        AttackRunner runner;
        float attackCooldown;
        float stunTimer;
        float knockTimer;
        float slowTimer;
        float slowPercent;
        float dotPerSecond;
        float dotRemaining;
        float dotCarry;
        Color dotColor;
        MechanicInput heldInput;

        public Faction Faction => Faction.Player;
        public bool IsAlive => !game.State.IsDead;
        public bool IsDashing => dashTimer > 0f;
        public bool IsShielded => shieldTimer > 0f;

        float CurrentCriticalChance => Mathf.Clamp01(CriticalChance +
            (game.State.SkillValue(SkillEffect.CriticalChance) + game.State.Passive(EffectType.CriticalChance)) / 100f);

        /// <summary>Recharge réelle : les compétences « cadence de tir » et « recharge de l'esquive » la raccourcissent.</summary>
        float Cooldown(CharacterMechanic mechanic)
        {
            var cooldown = game.State.ValuesOf(mechanic).Cooldown;
            return mechanic.action switch
            {
                MechanicAction.Projectile or MechanicAction.Nova => cooldown / (1f + Mathf.Max(0f, game.State.SkillValue(SkillEffect.FireRate)) / 100f),
                MechanicAction.Dash => cooldown * (1f - Mathf.Clamp(game.State.SkillValue(SkillEffect.DashCooldown) / 100f, 0f, 0.8f)),
                _ => cooldown
            };
        }

        /// <summary>0 juste après utilisation, 1 quand la mécanique est de nouveau prête.</summary>
        public float Readiness(CharacterMechanic mechanic)
        {
            if (!cooldowns.TryGetValue(mechanic, out var left) || left <= 0f)
                return 1f;
            return 1f - Mathf.Clamp01(left / Mathf.Max(0.01f, Cooldown(mechanic)));
        }

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
            player.runner = AttackRunner.For(player, go);
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

            foreach (var mechanic in cooldowns.Keys.ToList())
                cooldowns[mechanic] -= Time.deltaTime;
            invulnerable -= Time.deltaTime;
            shieldTimer -= Time.deltaTime;
            boostTimer -= Time.deltaTime;
            attackCooldown -= Time.deltaTime;
            slowTimer -= Time.deltaTime;
            knockTimer -= Time.deltaTime;
            TickDamageOverTime();

            var screen = (Vector2)game.Camera.WorldToScreenPoint(transform.position);
            aim = GameInput.AimFrom(screen, aim);
            sprite.flipX = aim.x < 0f;

            // Étourdi ou projeté : ni déplacement ni action.
            if (stunTimer > 0f || knockTimer > 0f)
            {
                stunTimer -= Time.deltaTime;
                if (knockTimer <= 0f)
                    body.linearVelocity = Vector2.zero;
                return;
            }

            var move = GameInput.Move;
            // Une attaque d'arme en cours décide du déplacement (et va jusqu'au bout).
            if (!runner.Running)
                foreach (var mechanic in game.State.Mechanics)
                    if (game.State.IsUnlocked(mechanic) && GameInput.Triggered(mechanic.input))
                        TryUse(mechanic, move);
            if (runner.Running)
                return;

            if (dashTimer > 0f)
            {
                dashTimer -= Time.deltaTime;
                body.linearVelocity = dashDirection * dashSpeed;
                sprite.color = new Color(1f, 1f, 1f, 0.55f);
            }
            else
            {
                var boost = boostTimer > 0f ? 1f + boostPercent / 100f : 1f;
                var slow = slowTimer > 0f ? 1f - Mathf.Clamp(slowPercent, 0f, 80f) / 100f : 1f;
                body.linearVelocity = move * game.State.MoveSpeed * boost * slow * runner.SpeedMultiplier;
                sprite.color = IsShielded ? new Color(0.6f, 0.9f, 1f) : Color.white;
            }

            if (GameInput.UsePotion && !game.State.Use(game.State.FirstConsumable))
                game.Toast("Aucun consommable (F)", new Color(1f, 0.6f, 0.5f));
            if (GameInput.Interact)
                game.Nearest(transform.position)?.Interact(game);
        }

        /// <summary>Utilise une mécanique si elle est rechargée et si le joueur peut en payer le coût.</summary>
        void TryUse(CharacterMechanic mechanic, Vector2 move)
        {
            if (cooldowns.TryGetValue(mechanic, out var left) && left > 0f)
                return;
            // Attaque et compétence de l'arme (outil Attaques) : elles ont aussi leur propre recharge.
            var weapon = game.State.Weapon;
            var weaponAttack = mechanic.action switch
            {
                MechanicAction.Projectile => weapon != null ? weapon.WeaponAttack : null,
                MechanicAction.WeaponSkill => weapon != null ? weapon.WeaponSkill : null,
                _ => null
            };
            if (mechanic.action == MechanicAction.WeaponSkill && weaponAttack == null)
            {
                if (GameInput.JustPressed(mechanic.input))
                    GameFx.Float(transform.position, weapon == null ? "Aucune arme" : "Cette arme n'a pas de compétence", new Color(0.7f, 0.7f, 0.75f), 0.8f);
                return;
            }
            if (weaponAttack != null && attackCooldown > 0f)
                return;
            if (!game.State.TrySpend(mechanic))
            {
                if (GameInput.JustPressed(mechanic.input))
                    GameFx.Float(transform.position, $"{game.State.CostStat(mechanic)?.displayName ?? "Ressource"} insuffisant(e)", new Color(0.7f, 0.7f, 0.75f), 0.8f);
                return;
            }
            cooldowns[mechanic] = Cooldown(mechanic);
            if (weaponAttack != null)
            {
                heldInput = mechanic.input;
                var resolved = new EnemyAttack { attack = weaponAttack }.Resolve();
                runner.Run(resolved, null, () => attackCooldown = Mathf.Max(0f, resolved.Settings.cooldown));
                return;
            }
            var values = game.State.ValuesOf(mechanic);
            switch (mechanic.action)
            {
                case MechanicAction.Projectile:
                    // Projectiles supplémentaires (compétence) : en éventail, 12° d'écart.
                    var count = values.Count + Mathf.Clamp(Mathf.RoundToInt(game.State.SkillValue(SkillEffect.ExtraProjectiles)), 0, 8);
                    for (var i = 0; i < count; i++)
                        Shoot(Quaternion.Euler(0f, 0f, (i - (count - 1) / 2f) * Spread) * aim, values.Power);
                    break;
                case MechanicAction.Nova:
                    for (var i = 0; i < values.Count; i++)
                        Shoot(Quaternion.Euler(0f, 0f, 360f * i / values.Count) * aim, values.Power);
                    game.CameraFollow.Shake(0.1f);
                    break;
                case MechanicAction.Dash:
                    dashDirection = move.sqrMagnitude > 0.01f ? move.normalized : aim;
                    dashTimer = Mathf.Max(0.05f, values.Duration);
                    dashSpeed = values.Power / dashTimer;
                    break;
                case MechanicAction.Heal:
                    var healed = Mathf.RoundToInt(values.Power);
                    game.State.Heal(healed);
                    flash.Flash(new Color(0.5f, 1f, 0.6f), 0.2f);
                    GameFx.Float(transform.position, $"+{healed} PV", new Color(0.5f, 1f, 0.6f));
                    break;
                case MechanicAction.Shield:
                    shieldTimer = Mathf.Max(shieldTimer, values.Duration);
                    break;
                case MechanicAction.SpeedBoost:
                    boostTimer = values.Duration;
                    boostPercent = values.Power;
                    break;
            }
        }

        #region Effets subis (attaques de l'outil, auras de phase)

        public Vector2 Position => transform.position;

        public void ApplyStatus(StatusEffect status, float power, float duration, Vector2 direction)
        {
            if (game.State.IsDead)
                return;
            switch (status)
            {
                case StatusEffect.Poison or StatusEffect.Burn or StatusEffect.Rot:
                    dotPerSecond = Mathf.Max(dotPerSecond, power);
                    dotRemaining = Mathf.Max(dotRemaining, duration);
                    dotColor = status switch
                    {
                        StatusEffect.Burn => new Color(1f, 0.55f, 0.2f),
                        StatusEffect.Rot => new Color(0.9f, 0.3f, 0.35f),
                        _ => new Color(0.55f, 0.9f, 0.35f)
                    };
                    break;
                case StatusEffect.Frost:
                    slowTimer = Mathf.Max(slowTimer, duration);
                    slowPercent = Mathf.Max(slowPercent, power);
                    flash.Flash(new Color(0.6f, 0.85f, 1f), 0.2f);
                    break;
                case StatusEffect.Bleed:
                    var bled = Mathf.Max(1, Mathf.RoundToInt(game.State.MaxHp * power / 100f));
                    game.State.TakeDamage(bled);
                    GameFx.Float(transform.position, $"-{bled} saignement", new Color(0.85f, 0.1f, 0.15f));
                    break;
                case StatusEffect.Stun or StatusEffect.Launch:
                    if (runner.SuperArmor)
                        break;
                    stunTimer = Mathf.Max(stunTimer, duration);
                    if (status == StatusEffect.Launch)
                        GameFx.Float(transform.position, "projeté", Color.white, 0.8f);
                    break;
                case StatusEffect.Knockback:
                    if (runner.SuperArmor)
                        break;
                    knockTimer = 0.25f;
                    body.linearVelocity = direction.normalized * power;
                    break;
            }
            if (game.State.IsDead)
                game.OnPlayerDied();
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
            game.State.TakeDamage(whole);
            flash.Flash(dotColor, 0.1f);
            if (game.State.IsDead)
                game.OnPlayerDied();
        }

        #endregion

        #region Attaques d'arme (outil Attaques)

        DungeonGame IAttackActor.Game => game;
        Transform IAttackActor.Transform => transform;
        int IAttackActor.BaseDamage => game.State.Attack;
        int IAttackActor.MaxHp => game.State.MaxHp;
        Vector2 IAttackActor.Aim => aim;
        RoomInstance IAttackActor.Room => game.CurrentRoom;
        bool IAttackActor.Holding => IsHeld(heldInput);
        IReadOnlyList<ItemEffect> IAttackActor.OnHitEffects => game.State.Weapon != null ? game.State.Weapon.ActiveEffects.ToList() : null;
        IEnemySituation IAttackActor.Situation => new WeaponSituation(this);
        void IAttackActor.SetVelocity(Vector2 velocity) => body.linearVelocity = velocity;
        void IAttackActor.Flash(Color color) => flash.Flash(color, 0.05f);
        void IAttackActor.Heal(int amount) => game.State.Heal(amount);
        void IAttackActor.SelfDamage(int amount) => game.State.TakeDamage(Mathf.Min(amount, game.State.Hp - 1));
        void IAttackActor.SetPhase(int phase) { }

        static bool IsHeld(MechanicInput input) => input is MechanicInput.Primary or MechanicInput.Secondary && GameInput.Triggered(input);

        /// <summary>Ennemi visé : le plus proche dans un cône de 60° devant la visée (12 cases au plus).</summary>
        EnemyController AimedEnemy()
        {
            var room = game.CurrentRoom;
            return room?.Alive.Where(e => e != null && e.IsAlive)
                .Where(e => Vector2.Distance(e.transform.position, transform.position) < 12f &&
                            Vector2.Angle(aim, e.transform.position - transform.position) < 30f)
                .OrderBy(e => Vector2.Distance(e.transform.position, transform.position))
                .FirstOrDefault();
        }

        Vector2 IAttackActor.TargetPoint
        {
            get
            {
                var target = AimedEnemy();
                return target != null ? target.transform.position : (Vector2)transform.position + aim * 4f;
            }
        }

        /// <summary>
        /// Conditions des étapes vues par une arme : « le joueur » est la cible visée (distance, en vue, PV),
        /// « l'ennemi » est le porteur de l'arme (PV).
        /// </summary>
        sealed class WeaponSituation : IEnemySituation
        {
            readonly PlayerController player;
            readonly EnemyController target;

            public WeaponSituation(PlayerController player)
            {
                this.player = player;
                target = player.AimedEnemy();
            }

            public float PlayerDistance => target != null ? Vector2.Distance(target.transform.position, player.transform.position) : 99f;
            public bool PlayerInSight => target != null;
            public float PlayerHealth => target != null ? (float)target.Hp / Mathf.Max(1, target.MaxHp) : 1f;
            public float OwnHealth => (float)player.game.State.Hp / Mathf.Max(1, player.game.State.MaxHp);
            public int PlayerLevel => player.game.State.Level;
            public int MechanicLevel(string mechanicId) => player.game.State.MechanicLevel(mechanicId);
            public bool HasFlag(string flag) => player.game.State.HasFlag(flag);
            public int Floor => player.game.State.Floor;
            public BiomeData Biome => player.game.CurrentRoom?.Data.Biome;
            public RoomType RoomType => player.game.CurrentRoom?.Data.RoomType ?? RoomType.Combat;
            public int Allies => player.game.CurrentRoom?.Alive.Count(e => e != null && e.IsAlive) ?? 0;
            public float FightTime => 0f;
        }

        #endregion

        /// <summary>Un projectile : dégâts = attaque × puissance (%), doublés sur un coup critique.</summary>
        void Shoot(Vector2 direction, float power)
        {
            var critical = Random.value < CurrentCriticalChance;
            var damage = Mathf.Max(1, Mathf.RoundToInt(game.State.Attack * power / 100f));
            if (critical)
                damage *= 2;
            var weapon = game.State.Weapon;
            var color = weapon != null ? game.Database.GetRarityColor(weapon.RarityId) : new Color(1f, 0.95f, 0.7f);
            Projectile.Spawn(transform.position + (Vector3)(direction * 0.4f), direction, ProjectileSpeed, damage, Faction.Player, color,
                1f, weapon != null ? weapon.ActiveEffects.ToList() : null, critical);
        }

        public void TakeHit(int damage, Vector2 direction, IReadOnlyList<ItemEffect> effects, bool critical) => TryHit(damage, direction);

        /// <summary>Blesse le joueur, sauf s'il esquive, est invulnérable ou protégé. Renvoie true si le coup a vraiment touché.</summary>
        public bool TryHit(int damage, Vector2 direction)
        {
            if (invulnerable > 0f || IsDashing || IsShielded || runner.Invulnerable || game.State.IsDead || !game.IsPlaying)
                return false;
            runner.NotifyDamaged();
            var dealt = game.State.TakeDamage(Mathf.Max(1, Mathf.RoundToInt(damage * runner.DamageTakenMultiplier)));
            invulnerable = HurtInvulnerability;
            flash.Flash(new Color(1f, 0.3f, 0.3f), 0.15f);
            body.AddForce(direction * 3f, ForceMode2D.Impulse);
            game.CameraFollow.Shake(0.25f);
            GameFx.Float(transform.position, $"-{dealt}", new Color(1f, 0.35f, 0.35f));
            if (game.State.IsDead)
                game.OnPlayerDied();
            return true;
        }
    }

    /// <summary>
    /// Ennemi : patterns conditionnels et comportement par défaut définis dans l'outil Ennemis,
    /// stats calculées pour l'étage, butin tiré de sa table.
    /// </summary>
    public sealed class EnemyController : MonoBehaviour, IDamageable, IEnemySituation, IAttackActor, IStatusTarget
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

        #region Attaques définies dans l'outil

        ResolvedAttack[] resolvedAttacks;
        float[] cooldowns;
        readonly System.Random attackRandom = new();

        /// <summary>Une attaque est en cours : elle va jusqu'au bout, quoi que fasse le joueur (elle peut rater).</summary>
        bool Attacking => runner.Running;

        /// <summary>Lance la première attaque prête, à portée et dont les conditions sont remplies. Renvoie true si l'ennemi attaque.</summary>
        bool UpdateAttacks()
        {
            if (Attacking)
                return true;
            var attacks = Data.Attacks;
            // Réglages effectifs (attaque + remplacements de l'ennemi), calculés une fois.
            resolvedAttacks ??= attacks.Select(a => a.Resolve()).ToArray();
            cooldowns ??= new float[attacks.Count];
            for (var i = 0; i < cooldowns.Length; i++)
                cooldowns[i] -= Time.deltaTime;
            // Parmi les attaques possibles, tirage au hasard selon leurs poids.
            var next = EnemyLogic.ChooseAttack(attacks, resolvedAttacks, this, i => cooldowns[i] <= 0f, attackRandom);
            if (next < 0)
                return false;
            runner.Run(resolvedAttacks[next], IsBoss ? attacks[next].Name : null,
                () => cooldowns[next] = Mathf.Max(0.1f, resolvedAttacks[next].Settings.cooldown));
            return true;
        }

        // --- IAttackActor : ce que les étapes d'attaque utilisent ---
        DungeonGame IAttackActor.Game => game;
        Transform IAttackActor.Transform => transform;
        int IAttackActor.BaseDamage => Damage;
        Vector2 IAttackActor.Aim
        {
            get
            {
                var player = game.Player;
                if (player == null)
                    return Vector2.right;
                var offset = (Vector2)(player.transform.position - transform.position);
                return offset.sqrMagnitude > 0.0001f ? offset.normalized : Vector2.right;
            }
        }
        Vector2 IAttackActor.TargetPoint => game.Player != null ? (Vector2)game.Player.transform.position : (Vector2)transform.position;
        IEnemySituation IAttackActor.Situation => this;
        RoomInstance IAttackActor.Room => room;
        bool IAttackActor.Holding => false;
        IReadOnlyList<ItemEffect> IAttackActor.OnHitEffects => null;
        void IAttackActor.SetVelocity(Vector2 velocity) => body.linearVelocity = velocity;
        void IAttackActor.Flash(Color color) => flash.Flash(color, 0.05f);

        void IAttackActor.Heal(int amount)
        {
            Hp = Mathf.Min(MaxHp, Hp + amount);
            GameFx.Float(transform.position, $"+{amount}", new Color(0.5f, 1f, 0.6f));
        }

        void IAttackActor.SelfDamage(int amount) => ApplyDamage(Mathf.Min(amount, Hp - 1), new Color(0.8f, 0.3f, 0.3f), false);

        /// <summary>Absorbé par un allié (étape « Absorption ») : disparaît sans butin, aspiré vers lui.</summary>
        public void Absorb(Vector3 toward)
        {
            Hp = 0;
            room.Alive.Remove(this);
            GameFx.Float(transform.position, "absorbé", new Color(0.75f, 0.6f, 1f), 0.7f);
            body.linearVelocity = (toward - transform.position) * 3f;
            Destroy(gameObject, 0.15f);
        }

        /// <summary>
        /// Fait apparaître un renfort : l'ennemi donné, ou un ennemi ordinaire du biome. 3 au plus par invocateur,
        /// sauf les renforts de phase (<paramref name="force"/>), limités par la phase elle-même.
        /// </summary>
        public EnemyController Summon(EnemyData data, Vector2 position, bool force = false)
        {
            if (!force && summoned >= 3)
                return null;
            data ??= room.Data.Biome?.Enemies
                .Where(e => e.enemy != null && !e.enemy.IsBoss && e.enemy.Behaviour != EnemyBehaviour.Summoner)
                .OrderBy(e => e.enemy.GetHealth(floor))
                .Select(e => e.enemy)
                .FirstOrDefault();
            if (data == null)
                return null;
            if (!force)
                summoned++;
            if (!room.ContainsInner(position))
                position = transform.position;
            return Spawn(game, data, floor, room, position);
        }

        #endregion

        /// <param name="attack">Faux quand l'ennemi a ses propres attaques : le comportement de base ne gère que le déplacement.</param>
        void RunDefaultBehaviour(Vector2 direction, float distance, bool attack)
        {
            switch (Data.Behaviour)
            {
                case EnemyBehaviour.Melee:
                    Move(direction * Speed);
                    break;
                case EnemyBehaviour.Ranged:
                    Move(distance < 4f ? -direction * Speed : distance > 6.5f ? direction * Speed : Strafe(direction) * Speed * 0.6f);
                    if (attack && attackTimer <= 0f)
                        Shoot(direction, 1.6f);
                    break;
                case EnemyBehaviour.Charger:
                    UpdateCharge(direction, Speed);
                    break;
                case EnemyBehaviour.Summoner:
                    Move(distance < 5f ? -direction * Speed : Strafe(direction) * Speed * 0.5f);
                    if (attack && attackTimer <= 0f)
                        Shoot(direction, 2.2f, 0.7f);
                    if (attack && specialTimer <= 0f)
                        Summon();
                    break;
                case EnemyBehaviour.Stationary:
                    if (attack && attackTimer <= 0f)
                    {
                        Burst(IsBoss ? 12 : 6, 5f);
                        attackTimer = 2.4f;
                    }
                    break;
            }

            // Les boss ajoutent une salve en cercle, quel que soit leur comportement par défaut.
            if (attack && IsBoss && Data.Behaviour != EnemyBehaviour.Stationary && specialTimer <= 0f)
            {
                Burst(14, 5.5f);
                specialTimer = 3.5f;
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

        void Burst(int count, float speed, int damage = -1, System.Action onHit = null)
        {
            var offset = Random.Range(0f, 360f);
            for (var i = 0; i < count; i++)
            {
                var angle = (offset + 360f * i / count) * Mathf.Deg2Rad;
                Projectile.Spawn(transform.position, new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)), speed, damage > 0 ? damage : Damage, Faction.Enemy,
                    new Color(1f, 0.55f, 0.2f), IsBoss ? 1.3f : 1f, onHit: onHit);
            }
        }

        /// <summary>Projectiles en éventail, centrés sur la direction du joueur (<paramref name="spread"/> = ouverture totale en degrés).</summary>
        void Fan(Vector2 direction, int count, float spread, int damage, float speed, System.Action onHit = null)
        {
            var center = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            for (var i = 0; i < count; i++)
            {
                var angle = (count == 1 ? center : center - spread / 2f + spread * i / (count - 1)) * Mathf.Deg2Rad;
                Projectile.Spawn(transform.position, new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)), speed, damage, Faction.Enemy,
                    new Color(1f, 0.4f, 0.35f), IsBoss ? 1.5f : 1f, onHit: onHit);
            }
        }

        /// <summary>Charge : avance lentement, s'arrête et clignote (annonce), puis fonce en ligne droite.</summary>
        void UpdateCharge(Vector2 direction, float speed)
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
                    Move(chargeDirection * speed * 4f);
                }
                return;
            }
            Move(direction * speed * 0.6f);
            if (chargeCooldown <= 0f)
            {
                chargeTimer = 1.1f;
                chargeCooldown = 3f;
            }
        }

        /// <summary>Invoque un ennemi ordinaire du biome de la salle (3 au plus).</summary>
        void Summon()
        {
            specialTimer = 5f;
            Summon(null, (Vector2)transform.position + Random.insideUnitCircle.normalized);
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
