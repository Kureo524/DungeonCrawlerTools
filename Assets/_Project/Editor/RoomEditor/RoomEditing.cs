using System;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using UnityEditor;
using UnityEngine;

namespace TheDungeon.Editor.RoomEditor
{
    /// <summary>
    /// Opérations d'édition d'une salle, appliquées via SerializedObject (donc annulables avec Ctrl+Z).
    /// Chaque opération refuse proprement ce qui est interdit et renvoie un message expliquant pourquoi.
    /// </summary>
    public static class RoomEditing
    {
        const string Tiles = "tiles";
        const string Enemies = "enemies";
        const string Chests = "chests";
        const string Props = "props";

        public static int Width(SerializedObject room) => room.FindProperty("width").intValue;
        public static int Height(SerializedObject room) => room.FindProperty("height").intValue;

        public static TileType GetTile(SerializedObject room, Vector2Int cell)
        {
            var tiles = room.FindProperty(Tiles);
            var index = cell.y * Width(room) + cell.x;
            return index >= 0 && index < tiles.arraySize ? (TileType)tiles.GetArrayElementAtIndex(index).enumValueIndex : TileType.Wall;
        }

        /// <summary>Change une case. Un mur ou un trou efface ce qui était posé dessus.</summary>
        public static string SetTile(SerializedObject room, Vector2Int cell, TileType tile)
        {
            if (tile != TileType.Floor && IsDoorCell(room, cell))
                return "Cette case est une entrée : retirez la porte avant d'y mettre un mur ou un trou.";
            var tiles = room.FindProperty(Tiles);
            tiles.GetArrayElementAtIndex(cell.y * Width(room) + cell.x).enumValueIndex = (int)tile;
            if (tile != TileType.Floor)
                RemoveObjectsAt(room, cell);
            return null;
        }

        public static string PlaceEnemy(SerializedObject room, Vector2Int cell, EnemyData enemy, int max)
        {
            if (CountAt(room, Enemies, cell) == 0 && room.FindProperty(Enemies).arraySize >= max)
                return $"Maximum atteint : {max} ennemis par salle.";
            return Place(room, cell, Enemies, element => element.FindPropertyRelative("enemy").objectReferenceValue = enemy);
        }

        public static string PlaceChest(SerializedObject room, Vector2Int cell, LootTable lootTable, int max)
        {
            if (CountAt(room, Chests, cell) == 0 && room.FindProperty(Chests).arraySize >= max)
                return $"Maximum atteint : {max} coffres par salle.";
            return Place(room, cell, Chests, element => element.FindPropertyRelative("lootTable").objectReferenceValue = lootTable);
        }

        public static string PlaceProp(SerializedObject room, Vector2Int cell, Sprite sprite) =>
            Place(room, cell, Props, element => element.FindPropertyRelative("sprite").objectReferenceValue = sprite);

        public static string SetPlayerSpawn(SerializedObject room, Vector2Int cell)
        {
            if (GetTile(room, cell) != TileType.Floor)
                return "Le joueur doit apparaître sur une case de sol.";
            RemoveObjectsAt(room, cell);
            room.FindProperty("playerSpawn").vector2IntValue = cell;
            return null;
        }

        static string Place(SerializedObject room, Vector2Int cell, string listName, Action<SerializedProperty> fill)
        {
            if (GetTile(room, cell) != TileType.Floor)
                return "Les éléments ne peuvent être posés que sur une case de sol.";
            RemoveObjectsAt(room, cell);
            var list = room.FindProperty(listName);
            list.arraySize++;
            var element = list.GetArrayElementAtIndex(list.arraySize - 1);
            fill(element);
            element.FindPropertyRelative("position").vector2IntValue = cell;
            return null;
        }

        /// <summary>Retire ennemi, coffre, décor ou point d'apparition présents sur la case. Renvoie true si quelque chose a été retiré.</summary>
        public static bool RemoveObjectsAt(SerializedObject room, Vector2Int cell)
        {
            var removed = RemoveAt(room, Enemies, cell) | RemoveAt(room, Chests, cell) | RemoveAt(room, Props, cell);
            var spawn = room.FindProperty("playerSpawn");
            if (spawn.vector2IntValue == cell)
            {
                spawn.vector2IntValue = RoomData.NoSpawn;
                removed = true;
            }
            return removed;
        }

        static bool RemoveAt(SerializedObject room, string listName, Vector2Int cell)
        {
            var list = room.FindProperty(listName);
            var removed = false;
            for (var i = list.arraySize - 1; i >= 0; i--)
            {
                if (list.GetArrayElementAtIndex(i).FindPropertyRelative("position").vector2IntValue != cell)
                    continue;
                list.DeleteArrayElementAtIndex(i);
                removed = true;
            }
            return removed;
        }

        static int CountAt(SerializedObject room, string listName, Vector2Int cell)
        {
            var list = room.FindProperty(listName);
            var count = 0;
            for (var i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).FindPropertyRelative("position").vector2IntValue == cell)
                    count++;
            return count;
        }

        public static Doors GetDoors(SerializedObject room) => (Doors)room.FindProperty("doors").intValue;

        public static void SetDoor(SerializedObject room, Doors side, bool open)
        {
            var doors = GetDoors(room);
            doors = open ? doors | side : doors & ~side;
            room.FindProperty("doors").intValue = (int)doors;
            var cell = DoorCell(side, Width(room), Height(room));
            SetTileRaw(room, cell, open ? TileType.Floor : TileType.Wall);
            if (!open)
                RemoveObjectsAt(room, cell);
        }

        public static bool IsDoorCell(SerializedObject room, Vector2Int cell)
        {
            var doors = GetDoors(room);
            foreach (var side in FloorData.Sides)
                if ((doors & side) != 0 && DoorCell(side, Width(room), Height(room)) == cell)
                    return true;
            return false;
        }

        public static Vector2Int DoorCell(Doors side, int width, int height) => side switch
        {
            Doors.North => new Vector2Int(width / 2, height - 1),
            Doors.South => new Vector2Int(width / 2, 0),
            Doors.East => new Vector2Int(width - 1, height / 2),
            Doors.West => new Vector2Int(0, height / 2),
            _ => throw new ArgumentOutOfRangeException(nameof(side))
        };

        /// <summary>Redimensionne en gardant le contenu existant ; les nouvelles cases sont du sol, le contour devient un mur.</summary>
        public static void Resize(SerializedObject room, int width, int height)
        {
            var oldWidth = Width(room);
            var oldHeight = Height(room);
            var tiles = room.FindProperty(Tiles);
            var old = new TileType[tiles.arraySize];
            for (var i = 0; i < old.Length; i++)
                old[i] = (TileType)tiles.GetArrayElementAtIndex(i).enumValueIndex;
            var oldValid = old.Length == oldWidth * oldHeight;

            room.FindProperty("width").intValue = width;
            room.FindProperty("height").intValue = height;
            tiles.arraySize = width * height;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var border = x == 0 || y == 0 || x == width - 1 || y == height - 1;
                    var keep = oldValid && x < oldWidth - 1 && y < oldHeight - 1 && x > 0 && y > 0;
                    var tile = border ? TileType.Wall : keep ? old[y * oldWidth + x] : TileType.Floor;
                    tiles.GetArrayElementAtIndex(y * width + x).enumValueIndex = (int)tile;
                }
            }

            foreach (var list in new[] { Enemies, Chests, Props })
            {
                var property = room.FindProperty(list);
                for (var i = property.arraySize - 1; i >= 0; i--)
                {
                    var position = property.GetArrayElementAtIndex(i).FindPropertyRelative("position").vector2IntValue;
                    if (position.x <= 0 || position.y <= 0 || position.x >= width - 1 || position.y >= height - 1)
                        property.DeleteArrayElementAtIndex(i);
                }
            }
            var spawn = room.FindProperty("playerSpawn");
            var spawnCell = spawn.vector2IntValue;
            if (spawnCell != RoomData.NoSpawn && (spawnCell.x <= 0 || spawnCell.y <= 0 || spawnCell.x >= width - 1 || spawnCell.y >= height - 1))
                spawn.vector2IntValue = RoomData.NoSpawn;

            CarveDoors(room);
        }

        /// <summary>Remet un mur sur tout le contour, en gardant les entrées ouvertes.</summary>
        public static void WallBorder(SerializedObject room)
        {
            var width = Width(room);
            var height = Height(room);
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                    if (x == 0 || y == 0 || x == width - 1 || y == height - 1)
                    {
                        SetTileRaw(room, new Vector2Int(x, y), TileType.Wall);
                        RemoveObjectsAt(room, new Vector2Int(x, y));
                    }
            CarveDoors(room);
        }

        /// <summary>Sol partout, murs sur le contour, entrées ouvertes, aucun élément posé.</summary>
        public static void Reset(SerializedObject room)
        {
            foreach (var list in new[] { Enemies, Chests, Props })
                room.FindProperty(list).arraySize = 0;
            room.FindProperty("playerSpawn").vector2IntValue = RoomData.NoSpawn;
            var width = Width(room);
            var height = Height(room);
            room.FindProperty(Tiles).arraySize = width * height;
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                    SetTileRaw(room, new Vector2Int(x, y), TileType.Floor);
            WallBorder(room);
        }

        /// <summary>Retire ce qui n'appartient pas au biome de la salle (après un changement de biome).</summary>
        public static int RemoveForeignContent(SerializedObject room, BiomeData biome)
        {
            var removed = 0;
            removed += RemoveWhere(room, Enemies, e => biome == null || !(e.FindPropertyRelative("enemy").objectReferenceValue is EnemyData enemy && biome.AllowsEnemy(enemy)));
            removed += RemoveWhere(room, Chests, c => biome == null || !biome.ChestLootTables.Contains(c.FindPropertyRelative("lootTable").objectReferenceValue as LootTable));
            removed += RemoveWhere(room, Props, p => biome == null || !(p.FindPropertyRelative("sprite").objectReferenceValue is Sprite sprite && biome.TryGetProp(sprite, out _)));
            return removed;
        }

        static int RemoveWhere(SerializedObject room, string listName, Func<SerializedProperty, bool> predicate)
        {
            var list = room.FindProperty(listName);
            var removed = 0;
            for (var i = list.arraySize - 1; i >= 0; i--)
            {
                if (!predicate(list.GetArrayElementAtIndex(i)))
                    continue;
                list.DeleteArrayElementAtIndex(i);
                removed++;
            }
            return removed;
        }

        static void CarveDoors(SerializedObject room)
        {
            var doors = GetDoors(room);
            foreach (var side in FloorData.Sides)
                if ((doors & side) != 0)
                    SetTileRaw(room, DoorCell(side, Width(room), Height(room)), TileType.Floor);
        }

        static void SetTileRaw(SerializedObject room, Vector2Int cell, TileType tile)
        {
            var tiles = room.FindProperty(Tiles);
            var index = cell.y * Width(room) + cell.x;
            if (index >= 0 && index < tiles.arraySize)
                tiles.GetArrayElementAtIndex(index).enumValueIndex = (int)tile;
        }

        /// <summary>Taille par défaut d'une salle neuve : centre des limites autorisées.</summary>
        public static Vector2Int DefaultSize(ForgeDesignLimits limits) =>
            new((limits.roomWidth.min + limits.roomWidth.max) / 2 | 1, (limits.roomHeight.min + limits.roomHeight.max) / 2 | 1);
    }
}
