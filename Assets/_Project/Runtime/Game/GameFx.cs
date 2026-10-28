using System.Collections.Generic;
using UnityEngine;

namespace TheDungeon.Game
{
    /// <summary>Petits effets visuels : chiffres flottants, flash à l'impact, secousse de caméra.</summary>
    public static class GameFx
    {
        public sealed class FloatingText
        {
            public Vector3 Position;
            public string Text;
            public Color Color;
            public float Age;
            public float Size;
        }

        const float TextLifetime = 0.9f;

        public static readonly List<FloatingText> Texts = new();
        static Sprite circle;

        public static void Float(Vector3 position, string text, Color color, float size = 1f)
        {
            Texts.Add(new FloatingText
            {
                Position = position + new Vector3(Random.Range(-0.2f, 0.2f), 0.4f, 0f),
                Text = text,
                Color = color,
                Size = size
            });
        }

        public static void Tick(float deltaTime)
        {
            for (var i = Texts.Count - 1; i >= 0; i--)
            {
                var text = Texts[i];
                text.Age += deltaTime;
                text.Position += Vector3.up * deltaTime * 1.2f;
                if (text.Age > TextLifetime)
                    Texts.RemoveAt(i);
            }
        }

        public static float Alpha(FloatingText text) => 1f - Mathf.Clamp01((text.Age - TextLifetime * 0.5f) / (TextLifetime * 0.5f));

        public static void Clear() => Texts.Clear();

        /// <summary>Disque blanc généré une fois (projectiles, halos), à teinter.</summary>
        public static Sprite Circle
        {
            get
            {
                if (circle != null)
                    return circle;
                const int size = 32;
                var texture = new Texture2D(size, size) { filterMode = FilterMode.Bilinear };
                var center = (size - 1) / 2f;
                for (var y = 0; y < size; y++)
                    for (var x = 0; x < size; x++)
                    {
                        var distance = new Vector2(x - center, y - center).magnitude / center;
                        texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01((1f - distance) * 4f)));
                    }
                texture.Apply();
                circle = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
                return circle;
            }
        }
    }

    /// <summary>Flash de couleur bref sur un sprite (impact, invulnérabilité).</summary>
    public sealed class SpriteFlash : MonoBehaviour
    {
        SpriteRenderer target;
        Color baseColor = Color.white;
        float remaining;
        Color flashColor;

        void Awake()
        {
            target = GetComponent<SpriteRenderer>();
            if (target != null)
                baseColor = target.color;
        }

        public void Flash(Color color, float duration = 0.1f)
        {
            flashColor = color;
            remaining = duration;
        }

        void Update()
        {
            if (target == null)
                return;
            if (remaining > 0f)
            {
                remaining -= Time.deltaTime;
                target.color = flashColor;
            }
            else
            {
                target.color = baseColor;
            }
        }
    }

    /// <summary>Caméra qui suit le joueur en douceur, avec secousse à l'impact.</summary>
    public sealed class CameraFollow : MonoBehaviour
    {
        public Transform Target;
        Vector3 velocity;
        float shake;

        public void Shake(float amount) => shake = Mathf.Max(shake, amount);

        public void SnapToTarget()
        {
            if (Target != null)
                transform.position = new Vector3(Target.position.x, Target.position.y, -10f);
        }

        void LateUpdate()
        {
            if (Target == null)
                return;
            var goal = new Vector3(Target.position.x, Target.position.y, -10f);
            var position = Vector3.SmoothDamp(transform.position, goal, ref velocity, 0.12f, Mathf.Infinity, Time.unscaledDeltaTime);
            if (shake > 0f)
            {
                position += (Vector3)(Random.insideUnitCircle * shake);
                shake = Mathf.MoveTowards(shake, 0f, Time.unscaledDeltaTime * 1.5f);
            }
            transform.position = position;
        }
    }
}
