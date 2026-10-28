using TheDungeon.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Floors
{
    /// <summary>Dessine une salle en miniature : sol teinté par le biome, murs, trous, ennemis, boss et coffres.</summary>
    public static class RoomMinimap
    {
        static readonly Color Wall = new(0.16f, 0.16f, 0.18f);
        static readonly Color Pit = new(0.03f, 0.03f, 0.06f);
        static readonly Color Enemy = new(0.9f, 0.45f, 0.4f);
        static readonly Color Boss = new(0.82f, 0.23f, 0.23f);
        static readonly Color Chest = new(0.95f, 0.8f, 0.3f);
        static readonly Color Prop = new(0.55f, 0.55f, 0.55f);
        static readonly Color Spawn = new(0.4f, 0.85f, 0.45f);

        public static void Draw(Painter2D painter, Rect area, RoomData room)
        {
            if (room == null || !room.HasValidTiles)
                return;
            var tile = Mathf.Min(area.width / room.Width, area.height / room.Height);
            var origin = new Vector2(area.x + (area.width - tile * room.Width) / 2f, area.y + (area.height - tile * room.Height) / 2f);
            var floorColor = room.Biome != null ? Color.Lerp(room.Biome.Color, Color.gray, 0.25f) : Color.gray;

            for (var y = 0; y < room.Height; y++)
            {
                for (var x = 0; x < room.Width; x++)
                {
                    painter.fillColor = room.GetTile(x, y) switch
                    {
                        TileType.Wall => Wall,
                        TileType.Pit => Pit,
                        _ => floorColor
                    };
                    Rectangle(painter, Cell(origin, tile, room.Height, x, y, 0f));
                }
            }

            foreach (var prop in room.Props)
                Dot(painter, origin, tile, room.Height, prop.position, Prop, 0.35f);
            foreach (var chest in room.Chests)
                Dot(painter, origin, tile, room.Height, chest.position, Chest, 0.45f);
            foreach (var spawn in room.Enemies)
            {
                var isBoss = spawn.enemy != null && spawn.enemy.IsBoss;
                Dot(painter, origin, tile, room.Height, spawn.position, isBoss ? Boss : Enemy, isBoss ? 0.9f : 0.45f);
            }
            if (room.HasPlayerSpawn)
                Dot(painter, origin, tile, room.Height, room.PlayerSpawn, Spawn, 0.5f);
        }

        /// <summary>Case (x, y) de la salle ; y = 0 en bas, comme dans le jeu.</summary>
        static Rect Cell(Vector2 origin, float tile, int height, int x, int y, float inset) =>
            new(origin.x + x * tile + inset, origin.y + (height - 1 - y) * tile + inset, tile - inset * 2f, tile - inset * 2f);

        static void Dot(Painter2D painter, Vector2 origin, float tile, int height, Vector2Int cell, Color color, float radiusFactor)
        {
            var rect = Cell(origin, tile, height, cell.x, cell.y, 0f);
            painter.fillColor = color;
            painter.BeginPath();
            painter.Arc(rect.center, Mathf.Max(1.2f, tile * radiusFactor), Angle.Degrees(0f), Angle.Degrees(360f));
            painter.Fill();
        }

        public static void Rectangle(Painter2D painter, Rect rect)
        {
            painter.BeginPath();
            painter.MoveTo(new Vector2(rect.xMin, rect.yMin));
            painter.LineTo(new Vector2(rect.xMax, rect.yMin));
            painter.LineTo(new Vector2(rect.xMax, rect.yMax));
            painter.LineTo(new Vector2(rect.xMin, rect.yMax));
            painter.ClosePath();
            painter.Fill();
        }
    }
}
