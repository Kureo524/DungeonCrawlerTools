using System.Collections.Generic;
using TheDungeon.Data;
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
}
