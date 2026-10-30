using System;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.ContentDatabase
{
    /// <summary>
    /// Pictogrammes des déplacements et des attaques d'ennemis (tracés, nets à toute taille), et tuiles cliquables
    /// qui les présentent : on choisit une action en la voyant plutôt qu'en lisant une liste.
    /// </summary>
    public static class BehaviourGlyphs
    {
        /// <summary>Tuile : pictogramme au-dessus d'un libellé court ; « sélectionnée » quand c'est le choix actuel.</summary>
        public static VisualElement Tile(string label, string tooltip, Action<Painter2D, Vector2, float> draw, bool selected, Action onClick)
        {
            var tile = new Button(onClick) { tooltip = tooltip };
            tile.AddToClassList("forge-action-tile");
            // Taille fixe : les règles génériques des boutons les étireraient sur toute la ligne.
            tile.style.width = 96;
            tile.style.height = 74;
            tile.style.flexGrow = 0;
            tile.style.flexShrink = 0;
            tile.EnableInClassList("forge-action-tile--selected", selected);
            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList("forge-action-tile__icon");
            icon.generateVisualContent += context =>
            {
                var painter = context.painter2D;
                painter.strokeColor = painter.fillColor = icon.resolvedStyle.color;
                painter.lineWidth = 1.6f;
                painter.lineCap = LineCap.Round;
                painter.lineJoin = LineJoin.Round;
                draw(painter, icon.contentRect.center, Mathf.Min(icon.contentRect.width, icon.contentRect.height) * 0.42f);
            };
            tile.Add(icon);
            var text = ForgeUI.Label(label, "forge-action-tile__label");
            text.pickingMode = PickingMode.Ignore;
            tile.Add(text);
            return tile;
        }

        /// <summary>Déplacement : la cible (le joueur) est toujours à droite du pictogramme.</summary>
        public static void Movement(Painter2D painter, Vector2 center, float size, PatternMovement movement)
        {
            var player = center + new Vector2(size, 0f);
            switch (movement)
            {
                case PatternMovement.Chase:
                    Arrow(painter, center - new Vector2(size, 0f), player - new Vector2(size * 0.35f, 0f));
                    Dot(painter, player, size * 0.18f);
                    break;
                case PatternMovement.KeepDistance:
                    Arrow(painter, center, center - new Vector2(size, 0f));
                    Arrow(painter, center, player - new Vector2(size * 0.35f, 0f));
                    Dot(painter, player, size * 0.18f);
                    break;
                case PatternMovement.Circle:
                    painter.BeginPath();
                    painter.Arc(center, size * 0.75f, Angle.Degrees(200f), Angle.Degrees(520f));
                    painter.Stroke();
                    Head(painter, center + new Vector2(Mathf.Cos(160f * Mathf.Deg2Rad), Mathf.Sin(160f * Mathf.Deg2Rad)) * size * 0.75f, new Vector2(0.35f, 1f));
                    Dot(painter, center, size * 0.18f);
                    break;
                case PatternMovement.Flee:
                    Arrow(painter, player - new Vector2(size * 0.5f, 0f), center - new Vector2(size, 0f));
                    Dot(painter, player, size * 0.18f);
                    break;
                case PatternMovement.Hold:
                    painter.BeginPath();
                    painter.Arc(center, size * 0.55f, Angle.Degrees(0f), Angle.Degrees(360f));
                    painter.Stroke();
                    Dot(painter, center, size * 0.2f);
                    break;
                case PatternMovement.Charge:
                    for (var i = 0; i < 3; i++)
                    {
                        var x = center.x - size * 0.8f + i * size * 0.55f;
                        painter.BeginPath();
                        painter.MoveTo(new Vector2(x, center.y - size * 0.5f));
                        painter.LineTo(new Vector2(x + size * 0.4f, center.y));
                        painter.LineTo(new Vector2(x, center.y + size * 0.5f));
                        painter.Stroke();
                    }
                    break;
            }
        }

        /// <summary>Action d'une étape d'attaque : l'ennemi à gauche, le joueur (cible) à droite.</summary>
        public static void Action(Painter2D painter, Vector2 center, float size, StepAction action)
        {
            var origin = center - new Vector2(size * 0.7f, 0f);
            switch (action)
            {
                case StepAction.Strike:
                    Shape(painter, center, size, ZoneShape.Cone);
                    break;
                case StepAction.Projectiles:
                    Dot(painter, origin, size * 0.16f);
                    for (var i = -1; i <= 1; i++)
                        Shot(painter, origin, i * 25f, size * 1.4f);
                    break;
                case StepAction.Dash:
                    Movement(painter, center, size, PatternMovement.Charge);
                    break;
                case StepAction.Summon:
                    Dot(painter, center, size * 0.22f);
                    for (var i = 0; i < 3; i++)
                    {
                        var angle = (90f + i * 120f) * Mathf.Deg2Rad;
                        painter.BeginPath();
                        painter.Arc(center + new Vector2(Mathf.Cos(angle), -Mathf.Sin(angle)) * size * 0.75f, size * 0.2f, Angle.Degrees(0f), Angle.Degrees(360f));
                        painter.Stroke();
                    }
                    break;
                case StepAction.Teleport:
                    // Pointillés jusqu'à une silhouette.
                    for (var i = 0; i < 4; i++)
                    {
                        painter.BeginPath();
                        var x = center.x - size + i * size * 0.4f;
                        painter.MoveTo(new Vector2(x, center.y));
                        painter.LineTo(new Vector2(x + size * 0.2f, center.y));
                        painter.Stroke();
                    }
                    painter.BeginPath();
                    painter.Arc(center + new Vector2(size * 0.75f, 0f), size * 0.3f, Angle.Degrees(0f), Angle.Degrees(360f));
                    painter.Stroke();
                    break;
                case StepAction.Buff:
                    painter.BeginPath();
                    painter.Arc(center, size * 0.8f, Angle.Degrees(0f), Angle.Degrees(360f));
                    painter.Stroke();
                    Arrow(painter, center + new Vector2(0f, size * 0.45f), center + new Vector2(0f, -size * 0.45f));
                    break;
                case StepAction.Absorb:
                    Dot(painter, center, size * 0.22f);
                    for (var i = 0; i < 4; i++)
                    {
                        var angle = (45f + i * 90f) * Mathf.Deg2Rad;
                        var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                        Arrow(painter, center + direction * size * 1.0f, center + direction * size * 0.45f);
                    }
                    break;
                case StepAction.Phase:
                    // Deux marches : passage à la phase suivante.
                    painter.BeginPath();
                    painter.MoveTo(center + new Vector2(-size, size * 0.5f));
                    painter.LineTo(center + new Vector2(-size * 0.3f, size * 0.5f));
                    painter.LineTo(center + new Vector2(-size * 0.3f, -size * 0.1f));
                    painter.LineTo(center + new Vector2(size * 0.4f, -size * 0.1f));
                    painter.LineTo(center + new Vector2(size * 0.4f, -size * 0.7f));
                    painter.LineTo(center + new Vector2(size, -size * 0.7f));
                    painter.Stroke();
                    break;
                case StepAction.Pause:
                    painter.BeginPath();
                    painter.MoveTo(center + new Vector2(-size * 0.25f, -size * 0.5f));
                    painter.LineTo(center + new Vector2(-size * 0.25f, size * 0.5f));
                    painter.MoveTo(center + new Vector2(size * 0.25f, -size * 0.5f));
                    painter.LineTo(center + new Vector2(size * 0.25f, size * 0.5f));
                    painter.Stroke();
                    break;
            }
        }

        /// <summary>Forme d'une zone de frappe, l'ennemi au centre (ou à gauche pour le cône et la ligne).</summary>
        public static void Shape(Painter2D painter, Vector2 center, float size, ZoneShape shape)
        {
            var fill = painter.strokeColor;
            painter.fillColor = new Color(fill.r, fill.g, fill.b, fill.a * 0.25f);
            var origin = center - new Vector2(size * 0.8f, 0f);
            painter.BeginPath();
            switch (shape)
            {
                case ZoneShape.Circle:
                    painter.Arc(center, size * 0.85f, Angle.Degrees(0f), Angle.Degrees(360f));
                    painter.Fill();
                    painter.Stroke();
                    Dot(painter, center, size * 0.14f);
                    break;
                case ZoneShape.Ring:
                    painter.Arc(center, size * 0.85f, Angle.Degrees(0f), Angle.Degrees(360f));
                    painter.Stroke();
                    painter.BeginPath();
                    painter.Arc(center, size * 0.45f, Angle.Degrees(0f), Angle.Degrees(360f));
                    painter.Stroke();
                    Dot(painter, center, size * 0.14f);
                    break;
                case ZoneShape.Cone:
                    painter.MoveTo(origin);
                    painter.Arc(origin, size * 1.6f, Angle.Degrees(-35f), Angle.Degrees(35f));
                    painter.ClosePath();
                    painter.Fill();
                    painter.Stroke();
                    break;
                case ZoneShape.Line:
                    painter.MoveTo(origin + new Vector2(0f, -size * 0.25f));
                    painter.LineTo(origin + new Vector2(size * 1.6f, -size * 0.25f));
                    painter.LineTo(origin + new Vector2(size * 1.6f, size * 0.25f));
                    painter.LineTo(origin + new Vector2(0f, size * 0.25f));
                    painter.ClosePath();
                    painter.Fill();
                    painter.Stroke();
                    break;
            }
        }

        /// <summary>Disposition des projectiles.</summary>
        public static void Projectiles(Painter2D painter, Vector2 center, float size, ProjectilePattern pattern)
        {
            var origin = center - new Vector2(size * 0.7f, 0f);
            switch (pattern)
            {
                case ProjectilePattern.Aimed:
                    Dot(painter, origin, size * 0.16f);
                    Shot(painter, origin, 0f, size * 1.5f);
                    break;
                case ProjectilePattern.Spread:
                    Dot(painter, origin, size * 0.16f);
                    for (var i = -1; i <= 1; i++)
                        Shot(painter, origin, i * 30f, size * 1.4f);
                    break;
                case ProjectilePattern.Ring:
                    Dot(painter, center, size * 0.16f);
                    for (var i = 0; i < 8; i++)
                        Shot(painter, center, i * 45f, size * 0.95f, size * 0.35f);
                    break;
            }
        }

        /// <summary>Direction d'une ruée par rapport au joueur (à droite).</summary>
        public static void Dash(Painter2D painter, Vector2 center, float size, DashDirection direction)
        {
            var player = center + new Vector2(size, 0f);
            Dot(painter, player, size * 0.18f);
            switch (direction)
            {
                case DashDirection.TowardPlayer:
                    Arrow(painter, center - new Vector2(size, 0f), player - new Vector2(size * 0.35f, 0f));
                    break;
                case DashDirection.AwayFromPlayer:
                    Arrow(painter, center, center - new Vector2(size, 0f));
                    break;
                case DashDirection.Sideways:
                    Arrow(painter, center + new Vector2(-size * 0.2f, size * 0.6f), center + new Vector2(-size * 0.2f, -size * 0.7f));
                    break;
            }
        }

        /// <summary>Comportement par défaut, dessiné avec le déplacement ou l'attaque qui le résume.</summary>
        public static void Behaviour(Painter2D painter, Vector2 center, float size, EnemyBehaviour behaviour)
        {
            switch (behaviour)
            {
                case EnemyBehaviour.Melee: Movement(painter, center, size, PatternMovement.Chase); break;
                case EnemyBehaviour.Ranged: Movement(painter, center, size, PatternMovement.KeepDistance); break;
                case EnemyBehaviour.Charger: Movement(painter, center, size, PatternMovement.Charge); break;
                case EnemyBehaviour.Summoner: Action(painter, center, size, StepAction.Summon); break;
                default: Projectiles(painter, center, size, ProjectilePattern.Ring); break;
            }
        }

        static void Dot(Painter2D painter, Vector2 at, float radius)
        {
            painter.BeginPath();
            painter.Arc(at, radius, Angle.Degrees(0f), Angle.Degrees(360f));
            painter.Fill();
        }

        /// <summary>Trait de tir, avec une pointe, dans la direction donnée (degrés, 0 = vers la droite).</summary>
        static void Shot(Painter2D painter, Vector2 origin, float degrees, float length, float start = 0f)
        {
            var direction = new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), -Mathf.Sin(degrees * Mathf.Deg2Rad));
            Arrow(painter, origin + direction * start, origin + direction * length);
        }

        public static void Arrow(Painter2D painter, Vector2 from, Vector2 to)
        {
            painter.BeginPath();
            painter.MoveTo(from);
            painter.LineTo(to);
            painter.Stroke();
            Head(painter, to, (to - from).normalized);
        }

        static void Head(Painter2D painter, Vector2 tip, Vector2 direction)
        {
            direction = direction.normalized;
            var side = new Vector2(-direction.y, direction.x);
            painter.BeginPath();
            painter.MoveTo(tip - direction * 5f + side * 3.5f);
            painter.LineTo(tip);
            painter.LineTo(tip - direction * 5f - side * 3.5f);
            painter.Stroke();
        }
    }
}
