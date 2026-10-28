using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace TheDungeon.Game
{
    /// <summary>Une salle construite dans le monde.</summary>
    public sealed class RoomInstance
    {
        public RoomData Data;
        public Vector2Int Cell;
        /// <summary>Case du monde correspondant à la case (0, 0) de la salle.</summary>
        public Vector2Int Origin;
        public readonly List<Vector2Int> DoorTiles = new();
        public readonly List<EnemyController> Alive = new();
        public bool Visited;
        public bool Activated;
        public bool Cleared;

        public RectInt Bounds => new(Origin.x, Origin.y, Data.Width, Data.Height);
        public Vector3 ToWorld(Vector2Int local) => new(Origin.x + local.x + 0.5f, Origin.y + local.y + 0.5f, 0f);
        public Vector3 Center => new(Origin.x + Data.Width / 2f, Origin.y + Data.Height / 2f, 0f);

        /// <summary>Vrai si la position est à l'intérieur des murs (les portes, sur le contour, n'en font pas partie).</summary>
        public bool ContainsInner(Vector2 position) =>
            position.x > Origin.x + 1f && position.x < Origin.x + Data.Width - 1f &&
            position.y > Origin.y + 1f && position.y < Origin.y + Data.Height - 1f;

        public bool IsCombat => Data.Enemies.Count > 0 && Data.RoomType is RoomType.Combat or RoomType.Boss;
    }

    /// <summary>Calcul de la position des salles : une case du plan = un emplacement assez grand pour la plus grande salle + un couloir.</summary>
    public static class FloorLayout
    {
        public const int CorridorLength = 4;

        public static Vector2Int SlotSize(IEnumerable<RoomData> rooms)
        {
            var list = rooms.ToList();
            var width = list.Count == 0 ? 10 : list.Max(r => r.Width);
            var height = list.Count == 0 ? 10 : list.Max(r => r.Height);
            return new Vector2Int(width + CorridorLength, height + CorridorLength);
        }

        /// <summary>
        /// Origine de la salle : centrée dans son emplacement de façon à ce que ses portes tombent exactement
        /// sur l'axe central de l'emplacement, donc face aux portes des salles voisines.
        /// </summary>
        public static Vector2Int Origin(Vector2Int cell, RoomData room, Vector2Int slot)
        {
            var center = new Vector2Int(cell.x * slot.x + slot.x / 2, cell.y * slot.y + slot.y / 2);
            return new Vector2Int(center.x - room.Width / 2, center.y - room.Height / 2);
        }

        public static Vector2Int DoorTile(RoomData room, Vector2Int origin, Doors side) => origin + room.GetDoorCell(side);
    }

    /// <summary>Monde d'un étage : tilemaps, salles, couloirs et portes verrouillables.</summary>
    public sealed class FloorWorld
    {
        public GameObject Root;
        public Tilemap Floor;
        public Tilemap Walls;
        public Tilemap Pits;
        public readonly List<RoomInstance> Rooms = new();
        public readonly Dictionary<Vector2Int, RoomInstance> ByCell = new();
        public TileBase DoorTile;

        public RoomInstance RoomAt(Vector2 position) => Rooms.FirstOrDefault(r => r.ContainsInner(position));

        public void LockDoors(RoomInstance room)
        {
            foreach (var door in room.DoorTiles)
                Walls.SetTile((Vector3Int)door, DoorTile);
        }

        public void UnlockDoors(RoomInstance room)
        {
            foreach (var door in room.DoorTiles)
                Walls.SetTile((Vector3Int)door, null);
        }
    }

    /// <summary>Construit un étage à partir de son plan (données produites par les outils Salles et Étages).</summary>
    public static class FloorBuilder
    {
        public const int PitLayer = 4; // Calque « Water » : les trous bloquent la marche mais pas les projectiles.

        static readonly Dictionary<(Sprite, bool), Tile> Tiles = new();
        static Sprite fallback;

        public static FloorWorld Build(IReadOnlyDictionary<Vector2Int, RoomData> layout, Sprite doorSprite, Transform parent)
        {
            var world = new FloorWorld { Root = new GameObject("Floor") };
            world.Root.transform.SetParent(parent, false);
            var grid = world.Root.AddComponent<Grid>();
            grid.cellSize = Vector3.one;

            world.Floor = CreateTilemap(world.Root, "Sol", 0, collider: false);
            world.Pits = CreateTilemap(world.Root, "Trous", 1, collider: true);
            world.Pits.gameObject.layer = PitLayer;
            world.Walls = CreateTilemap(world.Root, "Murs", 2, collider: true);
            world.DoorTile = Tile(doorSprite != null ? doorSprite : Fallback(), true);

            var slot = FloorLayout.SlotSize(layout.Values);
            foreach (var (cell, room) in layout)
            {
                var instance = new RoomInstance { Data = room, Cell = cell, Origin = FloorLayout.Origin(cell, room, slot) };
                PaintRoom(world, instance);
                world.Rooms.Add(instance);
                world.ByCell[cell] = instance;
            }

            foreach (var room in world.Rooms)
                ConnectDoors(world, room, layout);
            return world;
        }

        static Tilemap CreateTilemap(GameObject root, string name, int order, bool collider)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            var tilemap = go.AddComponent<Tilemap>();
            go.AddComponent<TilemapRenderer>().sortingOrder = order;
            if (collider)
                go.AddComponent<TilemapCollider2D>();
            return tilemap;
        }

        static void PaintRoom(FloorWorld world, RoomInstance room)
        {
            var data = room.Data;
            var biome = data.Biome;
            for (var y = 0; y < data.Height; y++)
            {
                for (var x = 0; x < data.Width; x++)
                {
                    var tile = data.GetTile(x, y);
                    var position = (Vector3Int)(room.Origin + new Vector2Int(x, y));
                    var sprite = biome != null ? biome.GetTileSprite(tile, x, y) : null;
                    switch (tile)
                    {
                        case TileType.Wall:
                            world.Walls.SetTile(position, Tile(sprite, true));
                            break;
                        case TileType.Pit:
                            world.Pits.SetTile(position, Tile(sprite, true));
                            break;
                        default:
                            world.Floor.SetTile(position, Tile(sprite, false));
                            break;
                    }
                }
            }
        }

        /// <summary>Relie chaque porte à la salle voisine par un couloir, ou la mure si elle ne mène nulle part.</summary>
        static void ConnectDoors(FloorWorld world, RoomInstance room, IReadOnlyDictionary<Vector2Int, RoomData> layout)
        {
            foreach (var side in FloorData.Sides)
            {
                if (!room.Data.HasDoor(side))
                    continue;
                var door = FloorLayout.DoorTile(room.Data, room.Origin, side);
                var neighbourCell = room.Cell + FloorData.Offset(side);
                if (!world.ByCell.TryGetValue(neighbourCell, out var neighbour) || !neighbour.Data.HasDoor(FloorData.Opposite(side)))
                {
                    var wall = room.Data.Biome != null ? room.Data.Biome.WallSprite : null;
                    world.Walls.SetTile((Vector3Int)door, Tile(wall, true));
                    continue;
                }

                room.DoorTiles.Add(door);
                // Chaque couloir est creusé une seule fois, depuis la salle de gauche ou du bas.
                if (side != Doors.East && side != Doors.North)
                    continue;
                var other = FloorLayout.DoorTile(neighbour.Data, neighbour.Origin, FloorData.Opposite(side));
                Carve(world, room, door, other, side == Doors.East);
            }
        }

        static void Carve(FloorWorld world, RoomInstance room, Vector2Int from, Vector2Int to, bool horizontal)
        {
            var biome = room.Data.Biome;
            var floorSprite = biome != null ? biome.GetTileSprite(TileType.Floor, 0, 0) : null;
            var wallSprite = biome != null ? biome.WallSprite : null;
            var step = horizontal ? Vector2Int.right : Vector2Int.up;
            var side = horizontal ? Vector2Int.up : Vector2Int.right;
            for (var tile = from + step; tile != to; tile += step)
            {
                world.Floor.SetTile((Vector3Int)tile, Tile(floorSprite, false));
                foreach (var edge in new[] { tile + side, tile - side })
                    if (!world.Floor.HasTile((Vector3Int)edge))
                        world.Walls.SetTile((Vector3Int)edge, Tile(wallSprite, true));
            }
        }

        static Tile Tile(Sprite sprite, bool solid)
        {
            sprite ??= Fallback();
            if (Tiles.TryGetValue((sprite, solid), out var cached) && cached != null)
                return cached;
            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = sprite;
            tile.colliderType = solid ? UnityEngine.Tilemaps.Tile.ColliderType.Grid : UnityEngine.Tilemaps.Tile.ColliderType.None;
            Tiles[(sprite, solid)] = tile;
            return tile;
        }

        /// <summary>Sprite gris d'une case, pour les visuels manquants.</summary>
        public static Sprite Fallback()
        {
            if (fallback != null)
                return fallback;
            var texture = new Texture2D(1, 1) { filterMode = FilterMode.Point };
            texture.SetPixel(0, 0, Color.gray);
            texture.Apply();
            fallback = Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            return fallback;
        }
    }
}
