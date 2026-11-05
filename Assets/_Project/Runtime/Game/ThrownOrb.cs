using UnityEngine;

namespace TheDungeon.Game
{
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
