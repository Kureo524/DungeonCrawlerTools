using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using UnityEngine;

namespace TheDungeon.Game
{
    /// <summary>
    /// Combat de l'ennemi : attaques définies dans l'outil Attaques (tirées selon leur poids et rechargées une à une),
    /// interface avec <see cref="AttackRunner"/>, invocations, et comportement de base quand il n'a pas d'attaque propre.
    /// </summary>
    public sealed partial class EnemyController
    {
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
    }
}
