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
    /// <summary>
    /// Arène de test du comportement : on glisse le joueur et un mur, la règle déclenchée s'allume et son déplacement
    /// et ses tirs sont dessinés. Seuls les réglages utiles aux règles de l'ennemi (PV, biome, mécaniques…) sont proposés.
    /// Même logique de choix qu'en jeu (EnemyLogic).
    /// </summary>
    public sealed partial class BehaviourArena : VisualElement
    {
        const int Rows = 8;
        const int MaxColumns = 32;

        static readonly Color Floor = new(0.07f, 0.08f, 0.12f, 1f);
        static readonly Color Grid = new(1f, 1f, 1f, 0.04f);
        static readonly Color Wall = new(0.36f, 0.38f, 0.48f, 1f);
        static readonly Color Threshold = new(0.65f, 0.59f, 0.75f, 0.55f);
        static readonly Color Sight = new(0.62f, 0.74f, 0.52f, 0.7f);
        static readonly Color Blocked = new(0.88f, 0.38f, 0.42f, 0.7f);
        static readonly Color ActionColor = new(0.66f, 0.77f, 0.94f, 0.95f);
        static readonly Color ShotColor = new(1f, 0.45f, 0.38f, 1f);

        /// <summary>Situation de test, gardée d'un ennemi à l'autre.</summary>
        static readonly ArenaSituation situation = new();
        ArenaEvaluation evaluation = ArenaEvaluation.None;

        readonly Func<EnemyData> enemy;
        readonly Action<int, float[]> activeChanged;
        readonly VisualElement canvas;
        readonly Image enemyIcon;
        readonly Image playerIcon;
        readonly Label status;
        readonly ArenaControls controls;
        int active = -1;
        int activeAttack = -1;
        float[] chances = Array.Empty<float>();
        enum Drag { None, Player, Wall }
        Drag drag;
        Vector2 dragOffset;

        /// <summary>Règle sélectionnée dans l'éditeur : ses seuils de distance sont dessinés.</summary>
        public int SelectedRule { get; set; } = -1;

        /// <summary>Attaque sélectionnée dans l'éditeur : sa zone de portée est dessinée.</summary>
        public int SelectedAttack { get; set; } = -1;

        public BehaviourArena(Func<EnemyData> enemy, Action<int, float[]> activeChanged)
        {
            this.enemy = enemy;
            this.activeChanged = activeChanged;
            AddToClassList("forge-arena");

            canvas = new VisualElement { tooltip = "Glissez le joueur (ou cliquez pour l'y placer) et le mur : la règle déclenchée s'affiche." };
            canvas.AddToClassList("forge-arena__canvas");
            canvas.generateVisualContent += Draw;
            enemyIcon = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            enemyIcon.AddToClassList("forge-arena__icon");
            playerIcon = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore,
                sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{DungeonPaths.Art}/Common/SPR_Player.png") };
            playerIcon.AddToClassList("forge-arena__icon");
            canvas.Add(enemyIcon);
            canvas.Add(playerIcon);
            canvas.RegisterCallback<GeometryChangedEvent>(_ => PlaceIcons());
            canvas.RegisterCallback<PointerDownEvent>(OnPointerDown);
            canvas.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            canvas.RegisterCallback<PointerUpEvent>(OnPointerUp);
            Add(canvas);

            status = ForgeUI.Label(string.Empty, "forge-arena__status");
            Add(status);
            var controlsRoot = new VisualElement();
            controlsRoot.AddToClassList("forge-arena__controls");
            Add(controlsRoot);
            controls = new ArenaControls(situation, controlsRoot, Refresh);
        }

        #region Coordonnées

        // La hauteur fixe la taille d'une case ; l'arène prend toute la largeur disponible (autant de colonnes qu'il en tient).
        float Scale => Mathf.Max(1f, canvas.contentRect.height / Rows);
        int Columns => Mathf.Clamp(Mathf.FloorToInt(canvas.contentRect.width / Scale), 12, MaxColumns);
        Vector2 Origin => canvas.contentRect.center - new Vector2(Columns, Rows) * Scale / 2f;
        Vector2 ToPixels(Vector2 cases) => Origin + cases * Scale;
        Vector2 ToCases(Vector2 pixels) => (pixels - Origin) / Scale;

        #endregion

        #region Glisser le joueur et le mur

        void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0)
                return;
            var at = ToCases(evt.localPosition);
            var wall = new Rect(situation.WallCorner, WallSize);
            if (Vector2.Distance(at, situation.Player) < 0.8f)
                drag = Drag.Player;
            else if (wall.Contains(at))
            {
                drag = Drag.Wall;
                dragOffset = at - situation.WallCorner;
            }
            else
            {
                // Clic dans le vide : le joueur y va directement.
                drag = Drag.Player;
                MovePlayer(at);
            }
            canvas.CapturePointer(evt.pointerId);
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (drag == Drag.None || !canvas.HasPointerCapture(evt.pointerId))
                return;
            var at = ToCases(evt.localPosition);
            if (drag == Drag.Player)
                MovePlayer(at);
            else
            {
                situation.WallCorner = new Vector2(Mathf.Clamp(at.x - dragOffset.x, 0f, Columns - WallSize.x), Mathf.Clamp(at.y - dragOffset.y, 0f, Rows - WallSize.y));
                Refresh();
            }
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            drag = Drag.None;
            if (canvas.HasPointerCapture(evt.pointerId))
                canvas.ReleasePointer(evt.pointerId);
        }

        void MovePlayer(Vector2 at)
        {
            situation.Player = new Vector2(Mathf.Clamp(at.x, 0.5f, Columns - 0.5f), Mathf.Clamp(at.y, 0.5f, Rows - 0.5f));
            // Pas sur l'ennemi : au moins une demi-case d'écart.
            if (Vector2.Distance(situation.Player, EnemyPosition) < 0.6f)
                situation.Player = EnemyPosition + Vector2.right * 0.6f;
            Refresh();
        }

        #endregion

        /// <summary>Recalcule la règle déclenchée, les réglages utiles et le dessin.</summary>
        public void Refresh()
        {
            var data = enemy();
            if (data == null)
                return;
            enemyIcon.sprite = data.Sprite;
            controls.Rebuild(data);
            evaluation = ArenaEvaluation.Evaluate(data, situation, SelectedAttack);
            if (evaluation.ActivePattern != active || evaluation.ActiveAttack != activeAttack || !evaluation.Chances.SequenceEqual(chances))
            {
                active = evaluation.ActivePattern;
                activeAttack = evaluation.ActiveAttack;
                chances = evaluation.Chances;
                activeChanged?.Invoke(active, chances);
            }
            status.text = evaluation.Describe(data, situation);
            PlaceIcons();
            canvas.MarkDirtyRepaint();
        }

        void PlaceIcons()
        {
            var size = Scale * 1.1f;
            foreach (var (icon, at) in new[] { (enemyIcon, EnemyPosition), (playerIcon, situation.Player) })
            {
                var pixels = ToPixels(at);
                icon.style.width = icon.style.height = size;
                icon.style.left = pixels.x - size / 2f;
                icon.style.top = pixels.y - size / 2f;
            }
        }
    }
}
