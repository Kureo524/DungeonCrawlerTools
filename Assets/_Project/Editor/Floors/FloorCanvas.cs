using System;
using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common.Charts;
using TheDungeon.Editor.Validation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Floors
{
    /// <summary>
    /// Plan d'un étage : chaque case est une salle (dessinée en miniature), reliée à ses voisines par des couloirs.
    /// Couloir vert = portes reliées, rouge = porte dans le vide ou portes incompatibles.
    /// Clic : placer la salle choisie, sélectionner, déplacer. Clic droit : retirer. Double-clic : ouvrir la salle.
    /// </summary>
    public sealed class FloorCanvas : VisualElement
    {
        const float Gap = 18f;
        const float Padding = 10f;

        readonly SerializedObject floor;
        readonly FloorData draft;
        readonly Vector2Int gridSize;
        readonly Func<RoomData> placing;
        readonly Action<Action> edit;
        readonly Action<RoomData> reveal;
        readonly Action placingDone;
        readonly VisualElement board;
        readonly VisualElement labels;
        readonly Label status;

        float cellSize = 96f;
        Dictionary<Vector2Int, RoomData> map = new();
        HashSet<Vector2Int> reachable = new();
        Vector2Int? hover;
        Vector2Int? selected;

        /// <param name="edit">Applique une modification de <paramref name="floor"/> puis rafraîchit l'outil.</param>
        public FloorCanvas(SerializedObject floor, FloorData draft, Vector2Int gridSize, Func<RoomData> placing, Action<Action> edit,
            Action<RoomData> reveal, Action placingDone)
        {
            this.floor = floor;
            this.draft = draft;
            this.gridSize = gridSize;
            this.placing = placing;
            this.edit = edit;
            this.reveal = reveal;
            this.placingDone = placingDone;
            AddToClassList("forge-floor");

            board = new VisualElement();
            board.AddToClassList("forge-floor__board");
            board.generateVisualContent += Draw;
            board.RegisterCallback<PointerDownEvent>(OnPointerDown);
            board.RegisterCallback<PointerMoveEvent>(evt => SetHover(CellAt(evt.localPosition)));
            board.RegisterCallback<PointerLeaveEvent>(_ => SetHover(null));
            Add(board);

            labels = new VisualElement { pickingMode = PickingMode.Ignore };
            labels.AddToClassList("forge-floor__labels");
            board.Add(labels);

            status = new Label { enableRichText = true };
            status.AddToClassList("forge-canvas__status");
            Add(status);
            Resize();
        }

        /// <summary>Salle sélectionnée sur le plan (pour les actions « entrée », « retirer »…).</summary>
        public RoomData SelectedRoom => selected.HasValue && map.TryGetValue(selected.Value, out var room) ? room : null;

        public event Action SelectionChanged;

        public void SetZoom(float size)
        {
            cellSize = size;
            Resize();
            Redraw();
        }

        void Resize()
        {
            board.style.width = Padding * 2 + gridSize.x * cellSize + (gridSize.x - 1) * Gap;
            board.style.height = Padding * 2 + gridSize.y * cellSize + (gridSize.y - 1) * Gap;
        }

        public void Redraw()
        {
            map = FloorGraph.Map(draft.Rooms);
            reachable = draft.Entrance != null && FloorGraph.TryFindCell(map, draft.Entrance, out var start)
                ? FloorGraph.Reachable(map, start)
                : new HashSet<Vector2Int>();
            if (selected.HasValue && !map.ContainsKey(selected.Value))
                selected = null;
            RebuildLabels();
            board.MarkDirtyRepaint();
            status.text = DefaultStatus();
        }

        Rect CellRect(Vector2Int cell) => new(
            Padding + cell.x * (cellSize + Gap),
            Padding + (gridSize.y - 1 - cell.y) * (cellSize + Gap),
            cellSize, cellSize);

        Vector2Int? CellAt(Vector2 position)
        {
            for (var y = 0; y < gridSize.y; y++)
                for (var x = 0; x < gridSize.x; x++)
                    if (CellRect(new Vector2Int(x, y)).Contains(position))
                        return new Vector2Int(x, y);
            return null;
        }

        void Draw(MeshGenerationContext context)
        {
            var painter = context.painter2D;
            var toPlace = placing();
            var layoutWithoutPlacing = toPlace == null ? map : map.Where(e => e.Value != toPlace).ToDictionary(e => e.Key, e => e.Value);

            // Couloirs d'abord, sous les salles.
            foreach (var door in FloorGraph.AllDoors(map))
                DrawCorridor(painter, door);

            for (var y = 0; y < gridSize.y; y++)
            {
                for (var x = 0; x < gridSize.x; x++)
                {
                    var cell = new Vector2Int(x, y);
                    var rect = CellRect(cell);
                    if (map.TryGetValue(cell, out var room))
                    {
                        painter.fillColor = new Color(0f, 0f, 0f, 0.35f);
                        RoomMinimap.Rectangle(painter, rect);
                        RoomMinimap.Draw(painter, new Rect(rect.x + 5f, rect.y + 16f, rect.width - 10f, rect.height - 21f), room);
                        Outline(painter, rect, OutlineColor(cell, room), OutlineWidth(cell, room));
                    }
                    else
                    {
                        var fits = toPlace != null && FloorGenerator.Fits(layoutWithoutPlacing, toPlace, cell, gridSize);
                        painter.fillColor = fits ? new Color(ChartColors.Good.r, ChartColors.Good.g, ChartColors.Good.b, 0.22f) : ChartColors.Track;
                        RoomMinimap.Rectangle(painter, rect);
                        if (hover == cell)
                            Outline(painter, rect, ChartColors.Axis, 1.5f);
                    }
                }
            }
        }

        void DrawCorridor(Painter2D painter, FloorDoor door)
        {
            var rect = CellRect(door.Cell);
            var offset = FloorData.Offset(door.Side);
            var direction = new Vector2(offset.x, -offset.y);
            var edge = rect.center + new Vector2(direction.x * rect.width / 2f, direction.y * rect.height / 2f);

            painter.lineCap = LineCap.Butt;
            if (door.State == DoorState.Connected)
            {
                // Dessiné une seule fois par paire de portes.
                if (door.Side != Doors.East && door.Side != Doors.North)
                    return;
                painter.strokeColor = ChartColors.Good;
                painter.lineWidth = 8f;
                painter.BeginPath();
                painter.MoveTo(edge);
                painter.LineTo(edge + direction * Gap);
                painter.Stroke();
                return;
            }

            // Porte non reliée : amorce rouge terminée par une croix.
            var tip = edge + direction * (Gap * 0.75f);
            painter.strokeColor = ChartColors.Critical;
            painter.lineWidth = 6f;
            painter.BeginPath();
            painter.MoveTo(edge);
            painter.LineTo(tip);
            painter.Stroke();
            painter.lineWidth = 2f;
            const float cross = 4f;
            painter.BeginPath();
            painter.MoveTo(tip + new Vector2(-cross, -cross));
            painter.LineTo(tip + new Vector2(cross, cross));
            painter.MoveTo(tip + new Vector2(-cross, cross));
            painter.LineTo(tip + new Vector2(cross, -cross));
            painter.Stroke();
        }

        Color OutlineColor(Vector2Int cell, RoomData room)
        {
            if (selected == cell)
                return new Color(0.91f, 0.63f, 0.35f);
            if (room == draft.Entrance)
                return ChartColors.Good;
            if (room.ContainsBoss())
                return ChartColors.Critical;
            if (draft.Entrance != null && !reachable.Contains(cell))
                return ChartColors.Warning;
            return hover == cell ? ChartColors.Axis : new Color(0f, 0f, 0f, 0f);
        }

        float OutlineWidth(Vector2Int cell, RoomData room) =>
            selected == cell || room == draft.Entrance || room.ContainsBoss() ? 3f : 2f;

        static void Outline(Painter2D painter, Rect rect, Color color, float width)
        {
            if (color.a <= 0f)
                return;
            painter.strokeColor = color;
            painter.lineWidth = width;
            painter.lineJoin = LineJoin.Miter;
            painter.BeginPath();
            painter.MoveTo(new Vector2(rect.xMin, rect.yMin));
            painter.LineTo(new Vector2(rect.xMax, rect.yMin));
            painter.LineTo(new Vector2(rect.xMax, rect.yMax));
            painter.LineTo(new Vector2(rect.xMin, rect.yMax));
            painter.ClosePath();
            painter.Stroke();
        }

        void RebuildLabels()
        {
            labels.Clear();
            foreach (var (cell, room) in map)
            {
                var rect = CellRect(cell);
                var name = new Label(Short(room)) { pickingMode = PickingMode.Ignore };
                name.AddToClassList("forge-floor__room-name");
                name.style.left = rect.x + 4f;
                name.style.top = rect.y + 1f;
                name.style.width = rect.width - 8f;
                labels.Add(name);

                var tag = room == draft.Entrance ? "ENTRÉE" : room.ContainsBoss() ? "BOSS" : null;
                if (tag == null)
                    continue;
                var badge = new Label(tag) { pickingMode = PickingMode.Ignore };
                badge.AddToClassList("forge-floor__tag");
                badge.style.backgroundColor = room == draft.Entrance ? ChartColors.Good : ChartColors.Critical;
                badge.style.left = rect.x + 4f;
                badge.style.top = rect.yMax - 16f;
                labels.Add(badge);
            }
        }

        static string Short(RoomData room) => string.IsNullOrWhiteSpace(room.DisplayName) ? room.name : room.DisplayName;

        void OnPointerDown(PointerDownEvent evt)
        {
            var cell = CellAt(evt.localPosition);
            if (!cell.HasValue)
                return;
            map.TryGetValue(cell.Value, out var room);

            if (evt.button == 1)
            {
                if (room != null)
                    edit(() => FloorEditing.RemoveAt(floor, cell.Value));
                return;
            }
            if (evt.button != 0)
                return;

            var toPlace = placing();
            if (toPlace != null)
            {
                edit(() => FloorEditing.Place(floor, toPlace, cell.Value));
                placingDone();
                Select(cell.Value);
                return;
            }
            if (room != null)
            {
                if (evt.clickCount == 2)
                    reveal(room);
                Select(cell.Value);
                return;
            }
            if (selected.HasValue && map.TryGetValue(selected.Value, out var moving))
            {
                edit(() => FloorEditing.Place(floor, moving, cell.Value));
                Select(cell.Value);
                return;
            }
            Select(null);
        }

        void Select(Vector2Int? cell)
        {
            selected = cell;
            board.MarkDirtyRepaint();
            SelectionChanged?.Invoke();
        }

        void SetHover(Vector2Int? cell)
        {
            if (hover == cell)
                return;
            hover = cell;
            board.MarkDirtyRepaint();
            board.tooltip = cell.HasValue ? Describe(cell.Value) : null;
            status.text = cell.HasValue ? Describe(cell.Value).Replace("\n", "   ") : DefaultStatus();
        }

        string Describe(Vector2Int cell)
        {
            if (!map.TryGetValue(cell, out var room))
            {
                var toPlace = placing();
                return toPlace == null
                    ? $"Case ({cell.x}, {cell.y}) : vide"
                    : $"Case ({cell.x}, {cell.y}) : cliquez pour y placer « {Short(toPlace)} »";
            }
            var doors = FloorGraph.AllDoors(map).Where(d => d.Cell == cell).ToList();
            var text = $"<b>{Short(room)}</b> · {room.RoomType} · {(room.Biome != null ? room.Biome.DisplayName : "sans biome")}";
            foreach (var door in doors)
                text += door.State switch
                {
                    DoorState.Connected => $"\n✔ Porte {FloorGraph.SideName(door.Side)} reliée à « {Short(door.Neighbour)} »",
                    DoorState.Blocked => $"\n✖ Porte {FloorGraph.SideName(door.Side)} : « {Short(door.Neighbour)} » n'a pas de porte en face",
                    _ => $"\n✖ Porte {FloorGraph.SideName(door.Side)} : mène dans le vide"
                };
            if (draft.Entrance != null && !reachable.Contains(cell))
                text += "\n⚠ Inaccessible depuis l'entrée";
            text += "\nDouble-clic : ouvrir la salle · Clic droit : retirer";
            return text;
        }

        string DefaultStatus()
        {
            var limits = Common.ForgeDesignLimits.Instance;
            var broken = FloorGraph.AllDoors(map).Count(d => d.State != DoorState.Connected);
            return $"{map.Count}/{limits.roomsPerFloor} salles · {FloorGraph.BossCount(map.Values)} boss · {broken} porte(s) non reliée(s)   " +
                   "(clic : placer / sélectionner / déplacer · clic droit : retirer · double-clic : ouvrir)";
        }
    }
}
