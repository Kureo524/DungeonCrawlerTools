using System;
using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using static TheDungeon.Editor.ContentDatabase.ArenaSituation;

namespace TheDungeon.Editor.ContentDatabase
{
    /// <summary>Dessin de l'arène : sol, portées, mur, ligne de vue, déplacement choisi et enchaînement simulé de l'attaque.</summary>
    public sealed partial class BehaviourArena
    {
        void Draw(MeshGenerationContext context)
        {
            var data = enemy();
            if (data == null || canvas.contentRect.width < 10f)
                return;
            var painter = context.painter2D;
            var scale = Scale;
            var origin = Origin;

            DrawBoard(painter, origin, scale);
            var enemyAt = ToPixels(EnemyPosition);
            DrawRanges(painter, data, enemyAt, scale);
            DrawWall(painter, scale);
            var playerAt = ToPixels(situation.Player);
            // Ligne de vue.
            painter.strokeColor = situation.PlayerInSight ? Sight : Blocked;
            painter.lineWidth = 1.2f;
            painter.BeginPath();
            painter.MoveTo(enemyAt);
            painter.LineTo(playerAt);
            painter.Stroke();

            DrawAction(painter, data, enemyAt, playerAt, scale);
        }

        /// <summary>Sol et grille d'une case.</summary>
        void DrawBoard(Painter2D painter, Vector2 origin, float scale)
        {
            painter.fillColor = Floor;
            painter.BeginPath();
            painter.MoveTo(origin);
            painter.LineTo(origin + new Vector2(Columns * scale, 0f));
            painter.LineTo(origin + new Vector2(Columns, Rows) * scale);
            painter.LineTo(origin + new Vector2(0f, Rows * scale));
            painter.ClosePath();
            painter.Fill();

            painter.strokeColor = Grid;
            painter.lineWidth = 1f;
            painter.BeginPath();
            for (var x = 1; x < Columns; x++)
            {
                painter.MoveTo(origin + new Vector2(x * scale, 0f));
                painter.LineTo(origin + new Vector2(x * scale, Rows * scale));
            }
            for (var y = 1; y < Rows; y++)
            {
                painter.MoveTo(origin + new Vector2(0f, y * scale));
                painter.LineTo(origin + new Vector2(Columns * scale, y * scale));
            }
            painter.Stroke();
        }

        /// <summary>Seuils de distance de la règle sélectionnée et portée de l'attaque sélectionnée, autour de l'ennemi.</summary>
        void DrawRanges(Painter2D painter, EnemyData data, Vector2 enemyAt, float scale)
        {
            // Seuils de distance de la règle sélectionnée : cercles autour de l'ennemi.
            if (SelectedRule >= 0 && SelectedRule < data.Patterns.Count)
            {
                painter.strokeColor = Threshold;
                painter.lineWidth = 1.5f;
                foreach (var condition in data.Patterns[SelectedRule].conditions
                             .Where(c => c.type is EnemyConditionType.PlayerCloserThan or EnemyConditionType.PlayerFartherThan))
                {
                    painter.BeginPath();
                    painter.Arc(enemyAt, condition.value * scale, Angle.Degrees(0f), Angle.Degrees(360f));
                    painter.Stroke();
                }
            }

            // Portée de déclenchement de l'attaque sélectionnée : anneau pointillé entre la distance minimale et maximale.
            if (SelectedAttack >= 0 && SelectedAttack < evaluation.Resolved.Count && evaluation.Resolved[SelectedAttack] is { } selectedAttack)
            {
                painter.strokeColor = new Color(1f, 0.45f, 0.38f, 0.45f);
                painter.lineWidth = 1.5f;
                foreach (var radius in new[] { selectedAttack.Settings.minRange, selectedAttack.Settings.maxRange }.Where(r => r > 0f))
                    DashedCircle(painter, enemyAt, radius * scale);
            }
        }

        void DrawWall(Painter2D painter, float scale)
        {
            var wall = new Rect(ToPixels(situation.WallCorner), WallSize * scale);
            painter.fillColor = Wall;
            painter.BeginPath();
            painter.MoveTo(wall.min);
            painter.LineTo(new Vector2(wall.xMax, wall.yMin));
            painter.LineTo(wall.max);
            painter.LineTo(new Vector2(wall.xMin, wall.yMax));
            painter.ClosePath();
            painter.Fill();
        }

        /// <summary>Déplacement (flèche bleue) et attaque (rouge) de ce que l'ennemi ferait ici.</summary>
        void DrawAction(Painter2D painter, EnemyData data, Vector2 enemyAt, Vector2 playerAt, float scale)
        {
            PatternMovement movement;
            if (active >= 0)
                movement = data.Patterns[active].movement;
            else
                movement = data.Behaviour switch
                {
                    EnemyBehaviour.Ranged or EnemyBehaviour.Summoner => PatternMovement.KeepDistance,
                    EnemyBehaviour.Charger => PatternMovement.Charge,
                    EnemyBehaviour.Stationary => PatternMovement.Hold,
                    _ => PatternMovement.Chase
                };

            // Attaque lancée ici : l'enchaînement simulé (l'ennemi ne se déplace pas pendant une attaque).
            // Sinon : son déplacement, et en aperçu l'attaque sélectionnée telle qu'elle porterait d'ici.
            if (evaluation.Outcomes.Count > 0)
            {
                DrawOutcomes(painter, scale);
                if (evaluation.ShownPossible)
                    return;
            }
            ProjectilePattern? basePattern = data.Attacks.Count > 0 ? null : data.Behaviour switch
            {
                EnemyBehaviour.Ranged => ProjectilePattern.Aimed,
                EnemyBehaviour.Stationary => ProjectilePattern.Ring,
                _ => null
            };

            var toPlayer = (playerAt - enemyAt).normalized;
            var side = new Vector2(-toPlayer.y, toPlayer.x);
            var distance = situation.PlayerDistance;
            var step = 1.6f * scale;
            painter.lineCap = LineCap.Round;
            painter.strokeColor = ActionColor;
            painter.lineWidth = 2.5f;
            switch (movement)
            {
                case PatternMovement.Chase:
                    BehaviourGlyphs.Arrow(painter, enemyAt + toPlayer * scale * 0.7f, enemyAt + toPlayer * (scale * 0.7f + step));
                    break;
                case PatternMovement.Flee:
                    BehaviourGlyphs.Arrow(painter, enemyAt - toPlayer * scale * 0.7f, enemyAt - toPlayer * (scale * 0.7f + step));
                    break;
                case PatternMovement.Circle:
                    BehaviourGlyphs.Arrow(painter, enemyAt + side * scale * 0.7f, enemyAt + side * (scale * 0.7f + step));
                    break;
                case PatternMovement.KeepDistance:
                    var direction = distance < 4f ? -toPlayer : distance > 6.5f ? toPlayer : side;
                    BehaviourGlyphs.Arrow(painter, enemyAt + direction * scale * 0.7f, enemyAt + direction * (scale * 0.7f + step));
                    break;
                case PatternMovement.Charge:
                    painter.lineWidth = 4f;
                    BehaviourGlyphs.Arrow(painter, enemyAt + toPlayer * scale * 0.7f, enemyAt + toPlayer * (scale * 0.7f + step * 2f));
                    break;
            }

            if (basePattern == null)
                return;
            painter.fillColor = ShotColor;
            if (basePattern == ProjectilePattern.Aimed)
                Fan(painter, enemyAt, toPlayer, 1, 0f, Mathf.Max(3f * scale, distance * scale));
            else
                for (var i = 0; i < 6; i++)
                {
                    var angle = i * Mathf.PI * 2f / 6;
                    Trail(painter, enemyAt, new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)), scale, 3f * scale);
                }
        }

        /// <summary>Étapes simulées : zones (rouge si elles touchent), projectiles, ruées ; les étapes sautées ne sont pas dessinées.</summary>
        void DrawOutcomes(Painter2D painter, float scale)
        {
            var steps = evaluation.Resolved[evaluation.ShownAttack].Steps;
            var outcomes = evaluation.Outcomes;
            for (var i = 0; i < outcomes.Count; i++)
            {
                var outcome = outcomes[i];
                if (!outcome.Played)
                    continue;
                var step = steps[i];
                var enemyAt = ToPixels(outcome.Enemy);
                var color = outcome.Hit ? ShotColor : new Color(0.75f, 0.75f, 0.85f, 1f);
                painter.strokeColor = color;
                painter.fillColor = new Color(color.r, color.g, color.b, outcome.Hit ? 0.22f : 0.1f);
                painter.lineWidth = 1.5f;
                switch (step.action)
                {
                    case StepAction.Strike:
                        foreach (var origin in outcome.Origins ?? new List<Vector2>())
                        {
                            if (step.anchor == ZoneAnchor.Thrown)
                            {
                                painter.BeginPath();
                                painter.MoveTo(enemyAt);
                                painter.QuadraticCurveTo((enemyAt + ToPixels(origin)) / 2f + Vector2.down * scale * 1.5f, ToPixels(origin));
                                painter.Stroke();
                            }
                            StepPreview.ZonePath(painter, step, ToPixels(origin), outcome.Aim, scale);
                        }
                        break;
                    case StepAction.Teleport:
                        painter.strokeColor = new Color(0.75f, 0.6f, 1f, 1f);
                        var arrival = ToPixels(outcome.End);
                        BehaviourGlyphs.Arrow(painter, enemyAt, arrival);
                        painter.BeginPath();
                        painter.Arc(arrival, scale * 0.45f, Angle.Degrees(0f), Angle.Degrees(360f));
                        painter.Stroke();
                        break;
                    case StepAction.Projectiles:
                        painter.fillColor = color;
                        var count = Mathf.Max(1, step.projectiles);
                        var reach = Mathf.Max(3f, situation.PlayerDistance) * scale;
                        if (step.pattern == ProjectilePattern.Ring)
                            for (var p = 0; p < count; p++)
                            {
                                var angle = p * Mathf.PI * 2f / count;
                                Trail(painter, enemyAt, new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)), scale, reach);
                            }
                        else
                            Fan(painter, enemyAt, outcome.Aim, count, step.pattern == ProjectilePattern.Spread ? step.spreadAngle : 8f * (count - 1), reach);
                        break;
                    case StepAction.Dash:
                        painter.lineWidth = 3.5f;
                        var end = ToPixels(outcome.End);
                        if ((end - enemyAt).sqrMagnitude > 1f)
                            BehaviourGlyphs.Arrow(painter, enemyAt, end);
                        // Silhouette de l'ennemi à l'arrivée.
                        painter.BeginPath();
                        painter.Arc(end, scale * 0.45f, Angle.Degrees(0f), Angle.Degrees(360f));
                        painter.Stroke();
                        break;
                }
            }
        }

        static void DashedCircle(Painter2D painter, Vector2 center, float radius)
        {
            const int Dashes = 36;
            for (var i = 0; i < Dashes; i += 1)
            {
                painter.BeginPath();
                painter.Arc(center, radius, Angle.Degrees(i * 360f / Dashes), Angle.Degrees((i + 0.5f) * 360f / Dashes));
                painter.Stroke();
            }
        }

        void Fan(Painter2D painter, Vector2 from, Vector2 toward, int count, float spread, float length)
        {
            var center = Mathf.Atan2(toward.y, toward.x) * Mathf.Rad2Deg;
            for (var i = 0; i < count; i++)
            {
                var angle = (count == 1 ? center : center - spread / 2f + spread * i / (count - 1)) * Mathf.Deg2Rad;
                Trail(painter, from, new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)), Scale, length);
            }
        }

        /// <summary>Projectiles espacés le long d'une direction, de plus en plus gros vers l'extérieur.</summary>
        static void Trail(Painter2D painter, Vector2 from, Vector2 direction, float scale, float length)
        {
            const int Dots = 4;
            for (var i = 0; i < Dots; i++)
            {
                var t = (i + 1f) / Dots;
                painter.BeginPath();
                painter.Arc(from + direction * Mathf.Lerp(scale * 0.9f, length, t), scale * Mathf.Lerp(0.07f, 0.15f, t), Angle.Degrees(0f), Angle.Degrees(360f));
                painter.Fill();
            }
        }
    }
}
