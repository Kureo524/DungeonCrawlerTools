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
        static ContactFilter2D filter = new() { useLayerMask = true, layerMask = Mask, useTriggers = false };
        readonly List<RaycastHit2D> hits = new();

        public static Projectile Spawn(Vector3 position, Vector2 direction, float speed, int damage, Faction faction, Color color,
            float scale = 1f, IReadOnlyList<ItemEffect> effects = null, bool critical = false)
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
            return projectile;
        }

        void Update()
        {
            var step = velocity * Time.deltaTime;
            Physics2D.CircleCast(transform.position, Radius, velocity.normalized, filter, hits, step.magnitude);
            foreach (var hit in hits.OrderBy(h => h.distance))
            {
                var target = hit.collider.GetComponentInParent<IDamageable>();
                if (target != null)
                {
                    if (target.Faction == faction || !target.IsAlive)
                        continue;
                    target.TakeHit(damage, velocity.normalized, effects, critical);
                }
                Destroy(gameObject);
                return;
            }

            transform.position += (Vector3)step;
            lifetime -= Time.deltaTime;
            if (lifetime <= 0f)
                Destroy(gameObject);
        }
    }
}
