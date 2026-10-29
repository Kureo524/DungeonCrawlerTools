using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Common
{
    /// <summary>
    /// Fond animé de Forge Studio, au-dessus de la texture du ciel et sous les panneaux en verre :
    /// étoiles qui scintillent, une constellation dont les liens s'allument doucement, et de temps en temps une étoile filante.
    /// Rafraîchi 20 fois par seconde, seulement quand la fenêtre est affichée.
    /// </summary>
    public sealed class ForgeSkyAnimation : VisualElement
    {
        const int StarCount = 70;
        const long FrameMs = 50;
        const float ShotDuration = 1.3f;

        static readonly Color StarColor = new(0.91f, 0.93f, 0.98f);
        static readonly Color ConstellationColor = new(0.65f, 0.59f, 0.75f);
        static readonly Color ShotColor = new(0.79f, 0.84f, 0.96f);

        readonly (Vector2 position, float size, float speed, float phase)[] stars = new (Vector2, float, float, float)[StarCount];
        readonly int[] constellation = { 3, 11, 17, 26, 34, 41 };
        readonly System.Random random = new(7);
        IVisualElementScheduledItem ticker;
        float nextShot;
        float shotStart = -10f;
        Vector2 shotFrom;
        Vector2 shotDirection;

        public ForgeSkyAnimation()
        {
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = style.top = style.right = style.bottom = 0;
            for (var i = 0; i < StarCount; i++)
                stars[i] = (new Vector2(Next(), Next()), 0.6f + Next() * 1.2f, 0.6f + Next() * 1.6f, Next() * Mathf.PI * 2f);
            nextShot = Time + 4f;
            generateVisualContent += Draw;
            RegisterCallback<AttachToPanelEvent>(_ => ticker = schedule.Execute(MarkDirtyRepaint).Every(FrameMs));
            RegisterCallback<DetachFromPanelEvent>(_ => ticker?.Pause());
        }

        static float Time => (float)EditorApplication.timeSinceStartup;

        float Next() => (float)random.NextDouble();

        void Draw(MeshGenerationContext context)
        {
            var rect = contentRect;
            if (rect.width < 10f || rect.height < 10f)
                return;
            var painter = context.painter2D;
            var time = Time;

            // Constellation : ses liens s'allument et s'éteignent lentement.
            var glow = 0.08f + 0.12f * (0.5f + 0.5f * Mathf.Sin(time * 0.4f));
            painter.strokeColor = new Color(ConstellationColor.r, ConstellationColor.g, ConstellationColor.b, glow);
            painter.lineWidth = 1f;
            painter.BeginPath();
            for (var i = 0; i < constellation.Length; i++)
            {
                var point = Scale(stars[constellation[i]].position, rect);
                if (i == 0)
                    painter.MoveTo(point);
                else
                    painter.LineTo(point);
            }
            painter.Stroke();

            // Étoiles qui scintillent.
            foreach (var (position, size, speed, phase) in stars)
            {
                var twinkle = 0.5f + 0.5f * Mathf.Sin(time * speed + phase);
                painter.fillColor = new Color(StarColor.r, StarColor.g, StarColor.b, 0.12f + 0.5f * twinkle);
                painter.BeginPath();
                painter.Arc(Scale(position, rect), size * (0.8f + 0.4f * twinkle), Angle.Degrees(0f), Angle.Degrees(360f));
                painter.Fill();
            }

            DrawShootingStar(painter, rect, time);
        }

        /// <summary>Étoile filante : une traînée qui traverse le haut du ciel, toutes les 7 à 14 secondes.</summary>
        void DrawShootingStar(Painter2D painter, Rect rect, float time)
        {
            if (time >= nextShot)
            {
                shotStart = time;
                nextShot = time + 7f + Next() * 7f;
                shotFrom = new Vector2(rect.width * (0.15f + Next() * 0.6f), rect.height * (0.05f + Next() * 0.3f));
                shotDirection = new Vector2(1f, 0.35f + Next() * 0.25f).normalized;
            }
            var progress = (time - shotStart) / ShotDuration;
            if (progress < 0f || progress > 1f)
                return;
            var head = shotFrom + shotDirection * (rect.width * 0.35f * progress);
            var fade = Mathf.Sin(progress * Mathf.PI);
            const int segments = 12;
            const float length = 140f;
            painter.lineCap = LineCap.Round;
            for (var s = 0; s < segments; s++)
            {
                var from = head - shotDirection * (length * s / segments);
                var to = head - shotDirection * (length * (s + 1) / segments);
                painter.strokeColor = new Color(ShotColor.r, ShotColor.g, ShotColor.b, fade * 0.7f * (1f - s / (float)segments));
                painter.lineWidth = 1.8f * (1f - s / (float)segments) + 0.3f;
                painter.BeginPath();
                painter.MoveTo(from);
                painter.LineTo(to);
                painter.Stroke();
            }
        }

        static Vector2 Scale(Vector2 normalized, Rect rect) => new(rect.x + normalized.x * rect.width, rect.y + normalized.y * rect.height);
    }
}
