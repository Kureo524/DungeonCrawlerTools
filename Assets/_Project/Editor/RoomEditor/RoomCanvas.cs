using System;
using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Validation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.RoomEditor
{
    /// <summary>
    /// Grille de la salle : clic gauche (maintenu) pour peindre avec le pinceau choisi, clic droit pour gommer.
    /// Les cases inaccessibles depuis les entrées sont teintées en rouge.
    /// </summary>
    public sealed class RoomCanvas : VisualElement
    {
        readonly SerializedObject room;
        readonly RoomData draft;
        readonly Func<RoomBrush> currentBrush;
        readonly Func<string, string> apply;
        readonly VisualElement grid;
        readonly Label status;
        readonly List<VisualElement> cells = new();

        int builtWidth;
        int builtHeight;
        int cellSize = 32;
        bool painting;
        bool erasing;
        Vector2Int lastPainted = new(-1, -1);
        HashSet<Vector2Int> reachable;

        public bool ShowUnreachable { get; set; } = true;

        /// <param name="apply">Applique une opération : renvoie un message d'erreur à afficher, ou null.</param>
        public RoomCanvas(SerializedObject room, RoomData draft, Func<RoomBrush> currentBrush, Func<string, string> apply)
        {
            this.room = room;
            this.draft = draft;
            this.currentBrush = currentBrush;
            this.apply = apply;
            AddToClassList("forge-canvas");

            grid = new VisualElement();
            grid.AddToClassList("forge-canvas__grid");
            grid.RegisterCallback<PointerDownEvent>(OnPointerDown);
            grid.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            grid.RegisterCallback<PointerUpEvent>(OnPointerUp);
            grid.RegisterCallback<PointerLeaveEvent>(_ => status.text = DefaultStatus());
            Add(grid);

            status = new Label();
            status.AddToClassList("forge-canvas__status");
            Add(status);
        }

        public void SetZoom(int size)
        {
            cellSize = size;
            builtWidth = 0;
            Redraw();
        }

        /// <summary>Met à jour l'affichage à partir des données de la copie de travail.</summary>
        public void Redraw()
        {
            room.Update();
            var width = draft.Width;
            var height = draft.Height;
            if (width != builtWidth || height != builtHeight || cells.Count != width * height)
                BuildCells(width, height);

            reachable = ComputeReachable();
            var objects = ObjectsByCell();
            var doorCells = FloorData.Sides.Where(draft.HasDoor).Select(draft.GetDoorCell).ToHashSet();

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var cellPosition = new Vector2Int(x, y);
                    var cell = cells[y * width + x];
                    var tile = draft.GetTile(x, y);
                    var sprite = draft.Biome != null ? draft.Biome.GetTileSprite(tile, x, y) : null;
                    cell.style.backgroundImage = sprite != null ? new StyleBackground(sprite) : StyleKeyword.None;
                    cell.style.backgroundColor = sprite != null ? StyleKeyword.Null : TileColor(tile);

                    var overlay = cell.Q<Image>();
                    objects.TryGetValue(cellPosition, out var content);
                    overlay.sprite = content.sprite;
                    overlay.style.display = content.sprite != null ? DisplayStyle.Flex : DisplayStyle.None;

                    var isDoor = doorCells.Contains(cellPosition);
                    cell.EnableInClassList("forge-canvas__cell--door", isDoor);
                    cell.EnableInClassList("forge-canvas__cell--unreachable",
                        ShowUnreachable && tile == TileType.Floor && reachable != null && !reachable.Contains(cellPosition));
                    cell.EnableInClassList("forge-canvas__cell--boss", content.isBoss);
                    cell.tooltip = Describe(cellPosition, tile, content.label, isDoor);
                }
            }
            status.text = DefaultStatus();
        }

        void BuildCells(int width, int height)
        {
            grid.Clear();
            cells.Clear();
            for (var i = 0; i < width * height; i++)
            {
                var cell = new VisualElement { pickingMode = PickingMode.Ignore };
                cell.AddToClassList("forge-canvas__cell");
                cell.style.width = cellSize;
                cell.style.height = cellSize;
                cell.Add(new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore });
                cells.Add(cell);
            }
            // Ligne du haut affichée en premier : y = 0 est en bas, comme dans le jeu.
            for (var y = height - 1; y >= 0; y--)
            {
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("forge-canvas__row");
                for (var x = 0; x < width; x++)
                    row.Add(cells[y * width + x]);
                grid.Add(row);
            }
            grid.style.width = width * cellSize;
            grid.style.height = height * cellSize;
            builtWidth = width;
            builtHeight = height;
        }

        HashSet<Vector2Int> ComputeReachable()
        {
            if (!draft.HasValidTiles)
                return null;
            var starts = FloorData.Sides.Where(draft.HasDoor).Select(draft.GetDoorCell)
                .Where(cell => draft.GetTile(cell.x, cell.y) == TileType.Floor).ToList();
            if (draft.HasPlayerSpawn)
                starts.Add(draft.PlayerSpawn);
            return starts.Count == 0 ? null : RoomRule.Reachable(draft, starts);
        }

        Dictionary<Vector2Int, (Sprite sprite, string label, bool isBoss)> ObjectsByCell()
        {
            var result = new Dictionary<Vector2Int, (Sprite, string, bool)>();
            foreach (var prop in draft.Props)
            {
                var name = draft.Biome != null && prop.sprite != null && draft.Biome.TryGetProp(prop.sprite, out var definition) ? definition.name : "Décor";
                result[prop.position] = (prop.sprite, $"Décor : {name}", false);
            }
            foreach (var chest in draft.Chests)
                result[chest.position] = (RoomArt.Chest, $"Coffre : {(chest.lootTable != null ? chest.lootTable.DisplayName : "sans table")}", false);
            foreach (var spawn in draft.Enemies)
                result[spawn.position] = (spawn.enemy != null ? spawn.enemy.Sprite : null,
                    spawn.enemy != null ? $"{(spawn.enemy.IsBoss ? "Boss" : "Ennemi")} : {spawn.enemy.DisplayName}" : "Ennemi manquant",
                    spawn.enemy != null && spawn.enemy.IsBoss);
            foreach (var spawn in draft.Npcs)
                result[spawn.position] = (spawn.npc != null ? spawn.npc.Portrait : null,
                    spawn.npc != null ? $"Personnage : {spawn.npc.DisplayName}" + (spawn.afterClear ? " (après le combat)" : string.Empty) : "Personnage manquant",
                    false);
            if (draft.HasPlayerSpawn)
                result[draft.PlayerSpawn] = (RoomArt.Player, "Apparition du joueur", false);
            return result;
        }

        static Color TileColor(TileType tile) => tile switch
        {
            TileType.Wall => new Color(0.25f, 0.25f, 0.28f),
            TileType.Pit => new Color(0.05f, 0.05f, 0.1f),
            _ => new Color(0.45f, 0.42f, 0.38f)
        };

        string Describe(Vector2Int cell, TileType tile, string content, bool isDoor)
        {
            var text = $"Case ({cell.x}, {cell.y}) · {TileName(tile)}";
            if (isDoor)
                text += " · Entrée";
            if (!string.IsNullOrEmpty(content))
                text += $" · {content}";
            if (tile == TileType.Floor && reachable != null && !reachable.Contains(cell) && ShowUnreachable)
                text += "\n⚠ Inaccessible depuis les entrées.";
            return text;
        }

        static string TileName(TileType tile) => tile switch
        {
            TileType.Wall => "Mur",
            TileType.Pit => "Trou",
            _ => "Sol"
        };

        string DefaultStatus()
        {
            var limits = Common.ForgeDesignLimits.Instance;
            return $"{draft.Width}×{draft.Height} cases · Ennemis {draft.Enemies.Count}/{limits.maxEnemiesPerRoom} · " +
                   $"Coffres {draft.Chests.Count}/{limits.maxChestsPerRoom} · Décors {draft.Props.Count}   " +
                   "(clic gauche : peindre, clic droit : gommer)";
        }

        bool TryGetCell(Vector2 local, out Vector2Int cell)
        {
            var x = Mathf.FloorToInt(local.x / cellSize);
            var row = Mathf.FloorToInt(local.y / cellSize);
            cell = new Vector2Int(x, builtHeight - 1 - row);
            return x >= 0 && row >= 0 && x < builtWidth && row < builtHeight;
        }

        void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0 && evt.button != 1)
                return;
            painting = true;
            erasing = evt.button == 1;
            lastPainted = new Vector2Int(-1, -1);
            Undo.IncrementCurrentGroup();
            grid.CapturePointer(evt.pointerId);
            PaintAt(evt.localPosition);
            evt.StopPropagation();
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (painting)
                PaintAt(evt.localPosition);
            else if (TryGetCell(evt.localPosition, out var cell))
                status.text = cells[cell.y * builtWidth + cell.x].tooltip.Replace("\n", "  ");
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (!painting)
                return;
            painting = false;
            grid.ReleasePointer(evt.pointerId);
            Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
        }

        void PaintAt(Vector2 local)
        {
            if (!TryGetCell(local, out var cell) || cell == lastPainted)
                return;
            lastPainted = cell;
            var brush = erasing ? RoomBrush.Eraser() : currentBrush();
            if (brush == null)
                return;

            var limits = Common.ForgeDesignLimits.Instance;
            if (brush.Kind == BrushKind.Eraser)
            {
                RoomEditing.RemoveObjectsAt(room, cell);
                apply(null);
                return;
            }
            var error = brush.Kind switch
            {
                BrushKind.Floor => RoomEditing.SetTile(room, cell, TileType.Floor),
                BrushKind.Wall => RoomEditing.SetTile(room, cell, TileType.Wall),
                BrushKind.Pit => RoomEditing.SetTile(room, cell, TileType.Pit),
                BrushKind.Enemy => RoomEditing.PlaceEnemy(room, cell, brush.Enemy, limits.maxEnemiesPerRoom),
                BrushKind.Chest => RoomEditing.PlaceChest(room, cell, brush.LootTable, limits.maxChestsPerRoom),
                BrushKind.Prop => RoomEditing.PlaceProp(room, cell, brush.PropSprite),
                BrushKind.PlayerSpawn => RoomEditing.SetPlayerSpawn(room, cell),
                BrushKind.Npc => RoomEditing.PlaceNpc(room, cell, brush.Npc),
                _ => null
            };
            apply(error);
        }
    }
}
