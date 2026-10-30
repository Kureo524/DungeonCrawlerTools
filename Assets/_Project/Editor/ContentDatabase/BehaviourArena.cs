using System;
using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.ContentDatabase
{
    /// <summary>
    /// Arène de test du comportement : on glisse le joueur et un mur, la règle déclenchée s'allume et son déplacement
    /// et ses tirs sont dessinés. Seuls les réglages utiles aux règles de l'ennemi (PV, biome, mécaniques…) sont proposés.
    /// Même logique de choix qu'en jeu (EnemyLogic).
    /// </summary>
    public sealed class BehaviourArena : VisualElement
    {
        const int Rows = 8;
        const int MaxColumns = 32;
        static readonly Vector2 EnemyPosition = new(4f, 4f);
        static readonly Vector2 WallSize = new(1f, 2.5f);

        static readonly Color Floor = new(0.07f, 0.08f, 0.12f, 1f);
        static readonly Color Grid = new(1f, 1f, 1f, 0.04f);
        static readonly Color Wall = new(0.36f, 0.38f, 0.48f, 1f);
        static readonly Color Threshold = new(0.65f, 0.59f, 0.75f, 0.55f);
        static readonly Color Sight = new(0.62f, 0.74f, 0.52f, 0.7f);
        static readonly Color Blocked = new(0.88f, 0.38f, 0.42f, 0.7f);
        static readonly Color ActionColor = new(0.66f, 0.77f, 0.94f, 0.95f);
        static readonly Color ShotColor = new(1f, 0.45f, 0.38f, 1f);

        /// <summary>Situation de test, gardée d'un ennemi à l'autre.</summary>
        sealed class Situation : IEnemySituation
        {
            public Vector2 Player = new(9f, 4f);
            public Vector2 WallCorner = new(6.5f, 1f);
            public float PlayerDistance => Vector2.Distance(Player, EnemyPosition);
            public bool PlayerInSight => !SegmentHitsRect(EnemyPosition, Player, new Rect(WallCorner, WallSize));
            public float PlayerHealth { get; set; } = 1f;
            public float OwnHealth { get; set; } = 1f;
            public int PlayerLevel { get; set; } = 1;
            public int Floor { get; set; } = 1;
            public BiomeData Biome { get; set; }
            public RoomType RoomType { get; set; } = RoomType.Combat;
            public int Allies { get; set; } = 2;
            public float FightTime { get; set; }
            public int Phase { get; set; }
            public readonly Dictionary<string, int> Mechanics = new();
            public readonly HashSet<string> Flags = new();
            public int MechanicLevel(string mechanicId) => mechanicId != null && Mechanics.TryGetValue(mechanicId, out var rank) ? rank : 0;
            public bool HasFlag(string flag) => Flags.Contains(flag);
        }

        static readonly Situation situation = new();
        List<ResolvedAttack> resolved = new();
        List<EnemyLogic.StepOutcome> outcomes = new();
        int shownAttack = -1;

        readonly Func<EnemyData> enemy;
        readonly Action<int, float[]> activeChanged;
        readonly VisualElement canvas;
        readonly Image enemyIcon;
        readonly Image playerIcon;
        readonly Label status;
        readonly VisualElement controls;
        string controlsSignature;
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
            controls = new VisualElement();
            controls.AddToClassList("forge-arena__controls");
            Add(controls);
        }

        #region Coordonnées

        // La hauteur fixe la taille d'une case ; l'arène prend toute la largeur disponible (autant de colonnes qu'il en tient).
        float Scale => Mathf.Max(1f, canvas.contentRect.height / Rows);
        int Columns => Mathf.Clamp(Mathf.FloorToInt(canvas.contentRect.width / Scale), 12, MaxColumns);
        Vector2 Origin => canvas.contentRect.center - new Vector2(Columns, Rows) * Scale / 2f;
        Vector2 ToPixels(Vector2 cases) => Origin + cases * Scale;
        Vector2 ToCases(Vector2 pixels) => (pixels - Origin) / Scale;

        static bool SegmentHitsRect(Vector2 a, Vector2 b, Rect rect)
        {
            // Liang-Barsky : le segment a-b entre-t-il dans le rectangle ?
            var t0 = 0f;
            var t1 = 1f;
            var d = b - a;
            float[] p = { -d.x, d.x, -d.y, d.y };
            float[] q = { a.x - rect.xMin, rect.xMax - a.x, a.y - rect.yMin, rect.yMax - a.y };
            for (var i = 0; i < 4; i++)
            {
                if (Mathf.Approximately(p[i], 0f))
                {
                    if (q[i] < 0f)
                        return false;
                    continue;
                }
                var t = q[i] / p[i];
                if (p[i] < 0f)
                    t0 = Mathf.Max(t0, t);
                else
                    t1 = Mathf.Min(t1, t);
                if (t0 > t1)
                    return false;
            }
            return true;
        }

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
            RebuildControls(data);
            resolved = data.Attacks.Select(a => a.Resolve()).ToList();
            var next = EnemyLogic.ActivePattern(data.Patterns, situation);
            // Toutes les attaques sont supposées rechargées : chances de chacune d'être lancée ici, d'après les poids.
            var nextChances = EnemyLogic.AttackChances(data.Attacks, resolved, situation);
            var nextAttack = EnemyLogic.ChooseAttack(data.Attacks, resolved, situation);
            if (next != active || nextAttack != activeAttack || !nextChances.SequenceEqual(chances))
            {
                active = next;
                activeAttack = nextAttack;
                chances = nextChances;
                activeChanged?.Invoke(active, chances);
            }
            var where = $"distance {situation.PlayerDistance:0.0} cases · {(situation.PlayerInSight ? "joueur en vue" : "joueur caché par le mur")}";
            string move;
            if (active >= 0)
            {
                var name = data.Patterns[active].name;
                move = $"<b>{ForgeText.Name(string.IsNullOrWhiteSpace(name) ? $"Règle {active + 1}" : name)}</b> : {EnemyPatternText.Action(data.Patterns[active])}";
            }
            else
                move = $"<b>Sinon</b> : {EnemyPatternText.Label(data.Behaviour).ToLowerInvariant()}";

            // Attaque déroulée : celle sélectionnée dans la liste (même si elle ne serait pas lancée ici, pour voir où elle porterait),
            // sinon la plus probable.
            var selectedValid = SelectedAttack >= 0 && SelectedAttack < resolved.Count;
            shownAttack = selectedValid ? SelectedAttack : activeAttack;
            var shownPossible = shownAttack >= 0 && shownAttack < chances.Length && chances[shownAttack] > 0f;
            outcomes = shownAttack >= 0 && resolved[shownAttack] != null
                ? EnemyLogic.Simulate(resolved[shownAttack], situation, EnemyPosition, situation.Player,
                    (a, b) => !SegmentHitsRect(a, b, new Rect(situation.WallCorner, WallSize)))
                : new List<EnemyLogic.StepOutcome>();
            string attack;
            if (data.Attacks.Count == 0)
                attack = "selon son comportement de base";
            else if (activeAttack < 0)
            {
                var reasons = data.Attacks.Select((a, i) => $"{ForgeText.Name(a.Name)} : {EnemyLogic.WhyNot(a, resolved[i], situation)}");
                attack = $"aucune <color=#8a8a9c>({string.Join(" · ", reasons)})</color>";
            }
            else
                attack = string.Join("  ·  ", Enumerable.Range(0, chances.Length).Where(i => chances[i] > 0f).OrderByDescending(i => chances[i])
                    .Select(i => $"<b>{chances[i]:P0}</b> {ForgeText.Name(data.Attacks[i].Name)}"));
            if (shownAttack >= 0 && resolved[shownAttack] != null)
            {
                var steps = resolved[shownAttack].Steps;
                var parts = outcomes.Select((o, i) =>
                {
                    var name = ForgeText.NameOrRedacted(steps[i].name);
                    var color = !o.Played ? "#8a8a9c" : o.Hit ? "#e0606a" : "#a3a6b6";
                    return $"<color={color}>{i + 1}. {name} : {o.Reason}</color>";
                });
                var prefix = shownPossible
                    ? $"\n   {ForgeText.Name(data.Attacks[shownAttack].Name)} ({chances[shownAttack]:P0}) : "
                    : $"\n   Si {ForgeText.Name(data.Attacks[shownAttack].Name)} était lancée ici : ";
                attack += prefix + string.Join("  →  ", parts);
            }
            status.text = $"<color=#9ebd85>▶</color> Déplacement {move}   <color=#8a8a9c>{where}</color>\n<color=#e0606a>⚔</color> Attaque : {attack}";
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

        #region Dessin

        void Draw(MeshGenerationContext context)
        {
            var data = enemy();
            if (data == null || canvas.contentRect.width < 10f)
                return;
            var painter = context.painter2D;
            var scale = Scale;
            var origin = Origin;

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

            // Seuils de distance de la règle sélectionnée : cercles autour de l'ennemi.
            var enemyAt = ToPixels(EnemyPosition);
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
            if (SelectedAttack >= 0 && SelectedAttack < resolved.Count && resolved[SelectedAttack] is { } selectedAttack)
            {
                painter.strokeColor = new Color(1f, 0.45f, 0.38f, 0.45f);
                painter.lineWidth = 1.5f;
                foreach (var radius in new[] { selectedAttack.Settings.minRange, selectedAttack.Settings.maxRange }.Where(r => r > 0f))
                    DashedCircle(painter, enemyAt, radius * scale);
            }

            // Mur.
            var wall = new Rect(ToPixels(situation.WallCorner), WallSize * scale);
            painter.fillColor = Wall;
            painter.BeginPath();
            painter.MoveTo(wall.min);
            painter.LineTo(new Vector2(wall.xMax, wall.yMin));
            painter.LineTo(wall.max);
            painter.LineTo(new Vector2(wall.xMin, wall.yMax));
            painter.ClosePath();
            painter.Fill();

            // Ligne de vue.
            var playerAt = ToPixels(situation.Player);
            painter.strokeColor = situation.PlayerInSight ? Sight : Blocked;
            painter.lineWidth = 1.2f;
            painter.BeginPath();
            painter.MoveTo(enemyAt);
            painter.LineTo(playerAt);
            painter.Stroke();

            DrawAction(painter, data, enemyAt, playerAt, scale);
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
            if (outcomes.Count > 0)
            {
                DrawOutcomes(painter, data, scale);
                if (shownAttack >= 0 && shownAttack < chances.Length && chances[shownAttack] > 0f)
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
        void DrawOutcomes(Painter2D painter, EnemyData data, float scale)
        {
            var steps = resolved[shownAttack].Steps;
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

        #endregion

        #region Réglages utiles

        /// <summary>
        /// Réglages de test des conditions utilisées par les règles de l'ennemi (la distance et la ligne de vue se règlent dans l'arène).
        /// Reconstruits seulement quand l'ensemble des conditions utilisées change, pour ne pas perdre la saisie en cours.
        /// </summary>
        void RebuildControls(EnemyData data)
        {
            var conditions = data.Patterns.SelectMany(p => p.conditions).Concat(data.Attacks.SelectMany(a => a.conditions)).ToList();
            var types = conditions.Select(c => c.type).Distinct().OrderBy(t => t).ToList();
            var mechanics = conditions.Where(c => c.type == EnemyConditionType.MechanicAtLeast && !string.IsNullOrEmpty(c.flag)).Select(c => c.flag).Distinct().ToList();
            var flags = conditions.Where(c => c.type == EnemyConditionType.FlagSet && !string.IsNullOrEmpty(c.flag)).Select(c => c.flag).Distinct().ToList();
            var signature = string.Join(",", types) + "|" + string.Join(",", mechanics) + "|" + string.Join(",", flags);
            if (signature == controlsSignature)
                return;
            controlsSignature = signature;
            controls.Clear();

            var limits = ForgeDesignLimits.Instance;
            foreach (var type in types)
            {
                switch (type)
                {
                    case EnemyConditionType.PlayerHealthBelow:
                        controls.Add(Slider("PV du joueur (%)", 0, 100, Mathf.RoundToInt(situation.PlayerHealth * 100f), v => situation.PlayerHealth = v / 100f));
                        break;
                    case EnemyConditionType.OwnHealthBelow:
                        controls.Add(Slider("PV de l'ennemi (%)", 0, 100, Mathf.RoundToInt(situation.OwnHealth * 100f), v => situation.OwnHealth = v / 100f));
                        break;
                    case EnemyConditionType.PlayerLevelAtLeast:
                        controls.Add(Slider("Niveau du joueur", 1, limits.maxLevelCap, situation.PlayerLevel, v => situation.PlayerLevel = v));
                        break;
                    case EnemyConditionType.FloorIn:
                        controls.Add(Slider("Étage", 1, limits.floorCount, situation.Floor, v => situation.Floor = v));
                        break;
                    case EnemyConditionType.AlliesAtMost:
                        controls.Add(Slider("Alliés restants", 0, limits.maxEnemiesPerRoom, situation.Allies, v => situation.Allies = v));
                        break;
                    case EnemyConditionType.PhaseIs:
                        controls.Add(Slider("Phase de l'ennemi", 0, Mathf.Max(1, data.Phases.Count), situation.Phase, v => situation.Phase = v));
                        break;
                    case EnemyConditionType.FightLongerThan:
                        controls.Add(Slider("Durée du combat (s)", 0, 300, Mathf.RoundToInt(situation.FightTime), v => situation.FightTime = v));
                        break;
                    case EnemyConditionType.BiomeIs:
                        var biomes = AssetFactory.LoadAll<BiomeData>();
                        var names = new List<string> { "Autre" }.Concat(biomes.Select(b => ForgeText.NameOrRedacted(b.DisplayName))).ToList();
                        var biome = new DropdownField("Biome", names, Mathf.Max(0, biomes.IndexOf(situation.Biome) + 1));
                        biome.RegisterValueChangedCallback(evt =>
                        {
                            var index = names.IndexOf(evt.newValue);
                            situation.Biome = index > 0 ? biomes[index - 1] : null;
                            Refresh();
                        });
                        controls.Add(biome);
                        break;
                    case EnemyConditionType.RoomTypeIs:
                        var roomType = new EnumField("Type de salle", situation.RoomType);
                        roomType.RegisterValueChangedCallback(evt =>
                        {
                            situation.RoomType = (RoomType)evt.newValue;
                            Refresh();
                        });
                        controls.Add(roomType);
                        break;
                }
            }
            var progression = TheDungeon.Editor.Play.GameLauncher.ActiveProgression;
            foreach (var id in mechanics)
            {
                var mechanic = progression?.Mechanics.FirstOrDefault(m => m.id == id);
                if (mechanic == null)
                    continue;
                var key = id;
                controls.Add(Slider($"{mechanic.displayName} (rang)", 0, mechanic.MaxRank, situation.MechanicLevel(id), v => situation.Mechanics[key] = v));
            }
            foreach (var flag in flags)
            {
                var key = flag;
                var toggle = new Toggle($"Marqueur {ForgeText.Name(flag)}") { value = situation.HasFlag(flag) };
                toggle.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue)
                        situation.Flags.Add(key);
                    else
                        situation.Flags.Remove(key);
                    Refresh();
                });
                controls.Add(toggle);
            }
            if (controls.childCount == 0)
                controls.Add(ForgeUI.Label("Les réglages des autres conditions (PV, niveau, biome, mécaniques…) apparaissent ici dès qu'une règle les utilise.",
                    "forge-hint"));
        }

        SliderInt Slider(string label, int min, int max, int value, Action<int> apply)
        {
            var slider = new SliderInt(label, min, max) { value = value, showInputField = true };
            slider.RegisterValueChangedCallback(evt =>
            {
                apply(evt.newValue);
                Refresh();
            });
            return slider;
        }

        #endregion
    }
}
