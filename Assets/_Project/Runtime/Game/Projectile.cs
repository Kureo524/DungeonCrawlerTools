using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using UnityEngine;

namespace TheDungeon.Game
{
    public enum Faction
    {
        Player,
        Enemy
    }

    /// <summary>Tout ce qui peut être touché par un projectile ou une attaque.</summary>
    public interface IDamageable
    {
        Faction Faction { get; }
        bool IsAlive { get; }
        void TakeHit(int damage, Vector2 direction, IReadOnlyList<ItemEffect> effects, bool critical);
    }

    /// <summary>
    /// Projectile : avance en ligne droite et teste les collisions par lancer de cercle à chaque image
    /// (les trous, sur le calque « Water », sont survolés ; murs et décors bloquants l'arrêtent).
    /// </summary>
    public sealed class Projectile : MonoBehaviour
    {
        const float Radius = 0.14f;
        static readonly int Mask = 1 << 0;

        Faction faction;
        Vector2 velocity;
        int damage;
        float lifetime = 3f;
        bool critical;
        IReadOnlyList<ItemEffect> effects;
        /// <summary>Appelé quand le projectile blesse vraiment sa cible (pas d'esquive, d'invulnérabilité ni de bouclier).</summary>
        System.Action onHit;
        float homing;
        System.Func<Vector2> homingTarget;
        StatusEffect status;
        float statusPower;
        float statusDuration;
        static ContactFilter2D filter = new() { useLayerMask = true, layerMask = Mask, useTriggers = false };
        readonly List<RaycastHit2D> hits = new();

        public static Projectile Spawn(Vector3 position, Vector2 direction, float speed, int damage, Faction faction, Color color,
            float scale = 1f, IReadOnlyList<ItemEffect> effects = null, bool critical = false, System.Action onHit = null)
        {
            var go = new GameObject(faction == Faction.Player ? "Tir" : "Tir ennemi");
            go.transform.position = position;
            go.transform.localScale = Vector3.one * 0.45f * scale;
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = GameFx.Circle;
            renderer.color = color;
            renderer.sortingOrder = 8;
            var projectile = go.AddComponent<Projectile>();
            projectile.faction = faction;
            projectile.velocity = direction.normalized * speed;
            projectile.damage = damage;
            projectile.effects = effects ?? new List<ItemEffect>();
            projectile.critical = critical;
            projectile.onHit = onHit;
            return projectile;
        }

        /// <summary>Options des attaques de l'outil : tête chercheuse (degrés par seconde vers la cible) et effet infligé à l'impact.</summary>
        public void Configure(float homingDegrees, StatusEffect effect, float power, float duration, System.Func<Vector2> target)
        {
            homing = homingDegrees;
            homingTarget = target;
            status = effect;
            statusPower = power;
            statusDuration = duration;
        }

        void Update()
        {
            if (homing > 0f)
            {
                // Vers la cible (le joueur pour un ennemi, l'ennemi le plus proche pour le joueur).
                Vector2? target = homingTarget?.Invoke() ?? Nearest();
                if (target.HasValue)
                {
                    var wanted = Vector2.SignedAngle(velocity, target.Value - (Vector2)transform.position);
                    var turn = Mathf.Clamp(wanted, -homing * Time.deltaTime, homing * Time.deltaTime);
                    velocity = Quaternion.Euler(0f, 0f, turn) * velocity;
                }
            }
            var step = velocity * Time.deltaTime;
            Physics2D.CircleCast(transform.position, Radius, velocity.normalized, filter, hits, step.magnitude);
            foreach (var hit in hits.OrderBy(h => h.distance))
            {
                var target = hit.collider.GetComponentInParent<IDamageable>();
                if (target != null)
                {
                    if (target.Faction == faction || !target.IsAlive)
                        continue;
                    var landed = target switch
                    {
                        PlayerController player => player.TryHit(damage, velocity.normalized),
                        EnemyController enemy => enemy.TryHit(damage, velocity.normalized, effects, critical),
                        _ => true
                    };
                    if (landed)
                    {
                        onHit?.Invoke();
                        if (status != StatusEffect.None && target is IStatusTarget statusTarget)
                            statusTarget.ApplyStatus(status, statusPower, statusDuration, velocity.normalized);
                    }
                }
                Destroy(gameObject);
                return;
            }

            transform.position += (Vector3)step;
            lifetime -= Time.deltaTime;
            if (lifetime <= 0f)
                Destroy(gameObject);
        }

        static DungeonGame cachedGame;

        Vector2? Nearest()
        {
            if (cachedGame == null)
                cachedGame = FindAnyObjectByType<DungeonGame>();
            var game = cachedGame;
            var room = game != null ? game.CurrentRoom : null;
            if (room == null || faction != Faction.Player)
                return null;
            var nearest = room.Alive.Where(e => e != null && e.IsAlive).OrderBy(e => Vector2.Distance(e.transform.position, transform.position)).FirstOrDefault();
            return nearest != null ? nearest.transform.position : null;
        }
    }
}
