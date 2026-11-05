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
}
