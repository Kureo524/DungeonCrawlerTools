using System.Linq;
using UnityEngine;

namespace TheDungeon.Editor.Common
{
    /// <summary>
    /// Fond de Forge Studio : un ciel de constellations éclairé par trois lumières froides (bleu, lavande, vert),
    /// visible à travers les panneaux en verre. Texture calculée une seule fois, aucun fichier image.
    /// </summary>
    public static class ForgeSky
    {
        const string TextureName = "ForgeSky";
        const int Width = 1280;
        const int Height = 800;

        static Texture2D texture;

        public static Texture2D Texture
        {
            get
            {
                if (texture != null)
                    return texture;
                // Après un rechargement du code, la texture déjà calculée existe encore : on la reprend au lieu d'en refaire une.
                texture = Resources.FindObjectsOfTypeAll<Texture2D>().FirstOrDefault(t => t.name == TextureName) ?? Generate();
                return texture;
            }
        }

        static Texture2D Generate()
        {
            var pixels = new Color[Width * Height];
            var background = new Color(0.047f, 0.047f, 0.071f);
            var size = Mathf.Max(Width, Height);
            // Lumières : position (0..1, origine en haut à gauche), rayon relatif, couleur, intensité au centre.
            var lights = new[]
            {
                (x: 0.12f, y: 0.18f, radius: 0.42f, color: new Color(0.541f, 0.659f, 0.878f), strength: 0.31f),
                (x: 0.88f, y: 0.12f, radius: 0.36f, color: new Color(0.647f, 0.592f, 0.753f), strength: 0.25f),
                (x: 0.60f, y: 1.00f, radius: 0.42f, color: new Color(0.620f, 0.741f, 0.522f), strength: 0.19f)
            };

            for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
            {
                var color = background;
                foreach (var light in lights)
                {
                    var distance = Vector2.Distance(new Vector2(x, y), new Vector2(light.x * Width, light.y * Height)) / (light.radius * size);
                    if (distance < 1f)
                        color = Color.Lerp(color, light.color, light.strength * (1f - distance));
                }
                pixels[(Height - 1 - y) * Width + x] = color;
            }

            // Étoiles et constellations, toujours les mêmes (graine fixe).
            var random = new System.Random(42);
            var stars = Enumerable.Range(0, Width * Height / 2400)
                .Select(_ => (x: (float)random.NextDouble() * Width, y: (float)random.NextDouble() * Height, size: (float)random.NextDouble() * 1.3f + 0.3f))
                .ToList();
            var bright = stars.Where(s => s.size > 1.35f).Take(14).ToList();
            for (var i = 1; i < bright.Count; i++)
                if (Vector2.Distance(new Vector2(bright[i].x, bright[i].y), new Vector2(bright[i - 1].x, bright[i - 1].y)) < Width * 0.25f)
                    Line(pixels, bright[i - 1].x, bright[i - 1].y, bright[i].x, bright[i].y, 0.16f);
            foreach (var star in stars)
                Dot(pixels, star.x, star.y, star.size, 0.2f + 0.45f * star.size / 1.6f);

            var result = new Texture2D(Width, Height, TextureFormat.RGBA32, false)
            {
                name = TextureName,
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            result.SetPixels(pixels);
            result.Apply(false, true);
            return result;
        }

        static readonly Color StarColor = new(0.91f, 0.933f, 0.98f);

        static void Blend(Color[] pixels, int x, int y, float alpha)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height || alpha <= 0f)
                return;
            var index = (Height - 1 - y) * Width + x;
            pixels[index] = Color.Lerp(pixels[index], StarColor, Mathf.Clamp01(alpha));
        }

        static void Dot(Color[] pixels, float cx, float cy, float radius, float alpha)
        {
            for (var y = Mathf.FloorToInt(cy - radius - 1); y <= Mathf.CeilToInt(cy + radius + 1); y++)
            for (var x = Mathf.FloorToInt(cx - radius - 1); x <= Mathf.CeilToInt(cx + radius + 1); x++)
            {
                var coverage = Mathf.Clamp01(radius + 0.5f - Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy)));
                Blend(pixels, x, y, alpha * coverage);
            }
        }

        static void Line(Color[] pixels, float x0, float y0, float x1, float y1, float alpha)
        {
            var steps = Mathf.CeilToInt(Vector2.Distance(new Vector2(x0, y0), new Vector2(x1, y1)));
            for (var i = 0; i <= steps; i++)
            {
                var t = steps == 0 ? 0f : i / (float)steps;
                Blend(pixels, Mathf.RoundToInt(Mathf.Lerp(x0, x1, t)), Mathf.RoundToInt(Mathf.Lerp(y0, y1, t)), alpha);
            }
        }
    }
}
