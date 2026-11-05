using UnityEngine;

namespace TheDungeon.Game
{
    /// <summary>
    /// Apparence commune du HUD (IMGUI) : mise à l'échelle sur une hauteur de 720, couleurs, styles de texte
    /// et primitives de dessin (aplats, contours, barres, sprites).
    /// </summary>
    internal sealed class HudSkin
    {
        public const float ReferenceHeight = 720f;

        public static readonly Color Panel = new(0.06f, 0.05f, 0.09f, 0.82f);
        public static readonly Color Gold = new(1f, 0.85f, 0.35f);
        public static readonly Color Health = new(0.86f, 0.25f, 0.28f);
        public static readonly Color Muted = new(0.75f, 0.72f, 0.68f);

        float scale;

        /// <summary>Largeur de l'écran dans le repère du HUD (la hauteur vaut toujours <see cref="ReferenceHeight"/>).</summary>
        public float Width { get; private set; }

        public GUIStyle Text { get; private set; }
        public GUIStyle Small { get; private set; }
        public GUIStyle Centered { get; private set; }
        public GUIStyle Title { get; private set; }
        public GUIStyle Big { get; private set; }
        public GUIStyle Choice { get; private set; }

        /// <summary>À appeler au début de chaque OnGUI : met l'interface à l'échelle de l'écran et crée les styles au premier passage.</summary>
        public void Begin()
        {
            scale = Screen.height / ReferenceHeight;
            Width = Screen.width / scale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            EnsureStyles();
        }

        void EnsureStyles()
        {
            if (Text != null)
                return;
            Text = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true, richText = true };
            Text.normal.textColor = new Color(0.95f, 0.93f, 0.9f);
            Small = new GUIStyle(Text) { fontSize = 12 };
            Centered = new GUIStyle(Text) { alignment = TextAnchor.MiddleCenter };
            Title = new GUIStyle(Centered) { fontSize = 44, fontStyle = FontStyle.Bold };
            Big = new GUIStyle(Centered) { fontSize = 22, fontStyle = FontStyle.Bold };
            Choice = new GUIStyle(GUI.skin.button) { fontSize = 15, alignment = TextAnchor.MiddleLeft, wordWrap = true, richText = true, padding = new RectOffset(12, 12, 6, 6) };
            Choice.normal.textColor = new Color(0.95f, 0.93f, 0.9f);
            Choice.hover.textColor = Gold;
        }

        /// <summary>Position d'un point du monde dans le repère du HUD.</summary>
        public Vector2 ToGui(Camera camera, Vector3 world)
        {
            var screen = camera.WorldToScreenPoint(world);
            return new Vector2(screen.x / scale, (Screen.height - screen.y) / scale);
        }

        public static void Fill(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        public static void Outline(Rect rect, Color color, float thickness)
        {
            Fill(new Rect(rect.x, rect.y, rect.width, thickness), color);
            Fill(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            Fill(new Rect(rect.x, rect.y, thickness, rect.height), color);
            Fill(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }

        public void Bar(Rect rect, float ratio, Color color, string label)
        {
            Fill(rect, new Color(0f, 0f, 0f, 0.55f));
            Fill(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(ratio), rect.height), color);
            if (!string.IsNullOrEmpty(label))
                GUI.Label(rect, label, new GUIStyle(Centered) { fontSize = Mathf.Min(13, Mathf.RoundToInt(rect.height - 4)) });
        }

        public static void DrawSprite(Rect rect, Sprite sprite)
        {
            if (sprite == null)
                return;
            var texture = sprite.texture;
            var source = sprite.textureRect;
            var uv = new Rect(source.x / texture.width, source.y / texture.height, source.width / texture.width, source.height / texture.height);
            GUI.DrawTextureWithTexCoords(rect, texture, uv);
        }
    }
}
