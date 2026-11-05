using System;
using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.ContentDatabase
{
    /// <summary>Petite vue de dessus d'une étape : l'ennemi, la direction visée (le joueur à droite) et la zone ou la trajectoire, à l'échelle.</summary>
    public sealed class StepPreview : VisualElement
    {
        static readonly Color Red = new(1f, 0.45f, 0.38f, 1f);

        readonly Func<AttackStep> step;

        public StepPreview(Func<AttackStep> step)
        {
            this.step = step;
            AddToClassList("forge-step-preview");
            tooltip = "Vue de dessus, à l'échelle : l'attaquant (bleu) et la cible (blanc) à 3,5 cases. Une case = un carreau. Pointillés : la zone à la fin de sa durée.";
            generateVisualContent += Draw;
            schedule.Execute(MarkDirtyRepaint).Every(300);
        }

        /// <summary>Passage des cases de la scène aux pixels de la vue (l'axe vertical est inversé).</summary>
        readonly struct Frame
        {
            readonly Vector2 offset;
            public readonly float Scale;

            public Frame(Vector2 offset, float scale)
            {
                this.offset = offset;
                Scale = scale;
            }

            public Vector2 Px(Vector2 cases) => offset + new Vector2(cases.x, -cases.y) * Scale;
        }

        void Draw(MeshGenerationContext context)
        {
            var current = step();
            var rect = contentRect;
            if (current == null || rect.width < 10f)
                return;
            var painter = context.painter2D;

            // Scène en cases : l'attaquant en (0, 0), la cible à 3,5 cases à droite.
            var target = new Vector2(3.5f, 0f);
            var origins = current.action == StepAction.Strike ? EnemyLogic.ZoneOrigins(current, Vector2.zero, Vector2.right, target) : null;
            var points = SceneExtent(current, target, origins);
            var min = new Vector2(points.Min(p => p.x), points.Min(p => p.y)) - Vector2.one * 0.8f;
            var max = new Vector2(points.Max(p => p.x), points.Max(p => p.y)) + Vector2.one * 0.8f;
            var scale = Mathf.Min(rect.width / (max.x - min.x), rect.height / (max.y - min.y));
            var frame = new Frame(rect.center - (min + max) / 2f * scale, scale);

            DrawGrid(painter, frame, min, max);
            painter.strokeColor = Red;
            painter.fillColor = new Color(Red.r, Red.g, Red.b, 0.22f);
            painter.lineWidth = 1.5f;
            switch (current.action)
            {
                case StepAction.Strike:
                    foreach (var origin in origins)
                        DrawZone(painter, frame, current, origin);
                    break;
                case StepAction.Projectiles:
                    DrawProjectiles(painter, frame, current);
                    break;
                case StepAction.Dash:
                    painter.lineWidth = 3f;
                    BehaviourGlyphs.Arrow(painter, frame.Px(Vector2.zero), frame.Px(DashVector(current) * current.dashDistance));
                    break;
            }
            DrawActors(painter, frame, target);
        }

        /// <summary>Points que la vue doit contenir : les deux personnages, et la zone (à sa taille maximale) ou la trajectoire de l'étape.</summary>
        static List<Vector2> SceneExtent(AttackStep current, Vector2 target, List<Vector2> origins)
        {
            var points = new List<Vector2> { Vector2.zero, target };
            switch (current.action)
            {
                case StepAction.Strike:
                    var duration = Mathf.Max(0f, current.activeDuration);
                    var reach = current.radius + Mathf.Max(0f, current.growth) * duration;
                    foreach (var origin in origins)
                    {
                        points.Add(origin + new Vector2(reach, reach));
                        points.Add(origin - new Vector2(reach, reach));
                        points.Add(origin + Vector2.right * (current.travelSpeed * duration + reach));
                    }
                    break;
                case StepAction.Projectiles:
                    points.Add(new Vector2(6f, 3f));
                    points.Add(new Vector2(-1f, -3f));
                    if (current.pattern == ProjectilePattern.Ring)
                        points.Add(new Vector2(-6f, 0f));
                    break;
                case StepAction.Dash:
                    points.Add(DashVector(current) * current.dashDistance);
                    break;
            }
            return points;
        }

        static Vector2 DashVector(AttackStep current) => current.dashDirection switch
        {
            DashDirection.AwayFromPlayer => Vector2.left,
            DashDirection.Sideways => Vector2.down,
            _ => Vector2.right
        };

        /// <summary>Grille d'une case.</summary>
        static void DrawGrid(Painter2D painter, Frame frame, Vector2 min, Vector2 max)
        {
            painter.strokeColor = new Color(1f, 1f, 1f, 0.05f);
            painter.lineWidth = 1f;
            painter.BeginPath();
            for (var x = Mathf.Floor(min.x); x <= max.x; x++)
            {
                painter.MoveTo(frame.Px(new Vector2(x, min.y)));
                painter.LineTo(frame.Px(new Vector2(x, max.y)));
            }
            for (var y = Mathf.Floor(min.y); y <= max.y; y++)
            {
                painter.MoveTo(frame.Px(new Vector2(min.x, y)));
                painter.LineTo(frame.Px(new Vector2(max.x, y)));
            }
            painter.Stroke();
        }

        /// <summary>Une zone de frappe : trajectoire du lancer, zone, position finale si elle se déplace ou grandit, et balayage.</summary>
        static void DrawZone(Painter2D painter, Frame frame, AttackStep current, Vector2 origin)
        {
            var self = frame.Px(Vector2.zero);
            var scale = frame.Scale;
            if (current.anchor == ZoneAnchor.Thrown)
            {
                // Trajectoire du lancer, en cloche.
                painter.strokeColor = new Color(Red.r, Red.g, Red.b, 0.5f);
                painter.BeginPath();
                painter.MoveTo(self);
                painter.QuadraticCurveTo((self + frame.Px(origin)) / 2f + Vector2.down * scale * 1.5f, frame.Px(origin));
                painter.Stroke();
                painter.strokeColor = Red;
            }
            ZonePath(painter, current, frame.Px(origin), Vector2.right, scale);
            // Zone qui dure : sa position et sa taille à la fin, en pointillés.
            if (current.activeDuration > 0f && (current.travelSpeed != 0f || current.growth != 0f))
            {
                var end = frame.Px(origin + Vector2.right * current.travelSpeed * current.activeDuration);
                var radius = (current.radius + current.growth * current.activeDuration) * scale;
                painter.strokeColor = new Color(Red.r, Red.g, Red.b, 0.45f);
                for (var d = 0; d < 24; d++)
                {
                    painter.BeginPath();
                    painter.Arc(end, Mathf.Max(1f, radius), Angle.Degrees(d * 15f), Angle.Degrees(d * 15f + 7f));
                    painter.Stroke();
                }
                if (current.travelSpeed != 0f)
                    BehaviourGlyphs.Arrow(painter, frame.Px(origin), end);
                painter.strokeColor = Red;
            }
            if (current.sweepAngle != 0f && current.activeDuration > 0f)
            {
                painter.BeginPath();
                painter.Arc(frame.Px(origin), current.radius * scale + 6f, Angle.Degrees(-current.sweepAngle / 2f), Angle.Degrees(current.sweepAngle / 2f));
                painter.Stroke();
            }
        }

        /// <summary>Une flèche par projectile, selon la forme de la salve (droite, éventail ou cercle).</summary>
        static void DrawProjectiles(Painter2D painter, Frame frame, AttackStep current)
        {
            var self = frame.Px(Vector2.zero);
            var count = Mathf.Max(1, current.projectiles);
            for (var i = 0; i < count; i++)
            {
                var angle = current.pattern switch
                {
                    ProjectilePattern.Ring => 360f * i / count,
                    ProjectilePattern.Spread => count == 1 ? 0f : -current.spreadAngle / 2f + current.spreadAngle * i / (count - 1),
                    _ => count == 1 ? 0f : -4f * (count - 1) + 8f * i
                } * Mathf.Deg2Rad;
                BehaviourGlyphs.Arrow(painter, self, self + new Vector2(Mathf.Cos(angle), -Mathf.Sin(angle)) * 5f * frame.Scale);
            }
        }

        /// <summary>La cible (cercle blanc) et l'attaquant (cercle plein bleu).</summary>
        static void DrawActors(Painter2D painter, Frame frame, Vector2 target)
        {
            painter.strokeColor = new Color(1f, 1f, 1f, 0.7f);
            painter.lineWidth = 1.5f;
            painter.BeginPath();
            painter.Arc(frame.Px(target), frame.Scale * 0.3f, Angle.Degrees(0f), Angle.Degrees(360f));
            painter.Stroke();
            painter.fillColor = new Color(0.66f, 0.77f, 0.94f, 1f);
            painter.BeginPath();
            painter.Arc(frame.Px(Vector2.zero), frame.Scale * 0.3f, Angle.Degrees(0f), Angle.Degrees(360f));
            painter.Fill();
        }

        /// <summary>Contour rempli de la zone d'une étape (aussi utilisé par l'arène de test).</summary>
        public static void ZonePath(Painter2D painter, AttackStep step, Vector2 origin, Vector2 aim, float scale)
        {
            var heading = Mathf.Atan2(aim.y, aim.x) * Mathf.Rad2Deg;
            painter.BeginPath();
            switch (step.shape)
            {
                case ZoneShape.Circle:
                    painter.Arc(origin, step.radius * scale, Angle.Degrees(0f), Angle.Degrees(360f));
                    painter.Fill();
                    painter.Stroke();
                    break;
                case ZoneShape.Ring:
                    painter.Arc(origin, step.radius * scale, Angle.Degrees(0f), Angle.Degrees(360f));
                    painter.Arc(origin, Mathf.Max(0f, step.innerRadius) * scale, Angle.Degrees(360f), Angle.Degrees(0f), ArcDirection.CounterClockwise);
                    painter.Fill(FillRule.OddEven);
                    painter.BeginPath();
                    painter.Arc(origin, step.radius * scale, Angle.Degrees(0f), Angle.Degrees(360f));
                    painter.Stroke();
                    painter.BeginPath();
                    painter.Arc(origin, Mathf.Max(0f, step.innerRadius) * scale, Angle.Degrees(0f), Angle.Degrees(360f));
                    painter.Stroke();
                    break;
                case ZoneShape.Cone:
                    painter.MoveTo(origin);
                    painter.Arc(origin, step.radius * scale, Angle.Degrees(heading - step.angle / 2f), Angle.Degrees(heading + step.angle / 2f));
                    painter.ClosePath();
                    painter.Fill();
                    painter.Stroke();
                    break;
                case ZoneShape.Line:
                    var forward = aim.normalized * step.radius * scale;
                    var side = new Vector2(-aim.y, aim.x).normalized * step.width * scale / 2f;
                    painter.MoveTo(origin - side);
                    painter.LineTo(origin + forward - side);
                    painter.LineTo(origin + forward + side);
                    painter.LineTo(origin + side);
                    painter.ClosePath();
                    painter.Fill();
                    painter.Stroke();
                    break;
            }
        }
    }
}
