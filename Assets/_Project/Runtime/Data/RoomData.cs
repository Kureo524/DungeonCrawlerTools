using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheDungeon.Data
{
    [Serializable]
    public struct EnemySpawn
    {
        [Tooltip("Ennemi qui apparaît.")]
        public EnemyData enemy;
        [Tooltip("Case de la salle (0,0 = coin bas gauche).")]
        public Vector2Int position;
    }

    [Serializable]
    public struct ChestSpawn
    {
        [Tooltip("Table de butin du coffre.")]
        public LootTable lootTable;
        [Tooltip("Case de la salle (0,0 = coin bas gauche).")]
        public Vector2Int position;
    }

    [CreateAssetMenu(menuName = "The Dungeon/Room", fileName = "RM_F1_01")]
    public class RoomData : ScriptableObject
    {
        [SerializeField, Tooltip("Largeur en cases.")]
        int width = 10;
        [SerializeField, Tooltip("Hauteur en cases.")]
        int height = 8;
        [SerializeField, Tooltip("Cases de la salle, ligne par ligne depuis le bas.")]
        TileType[] tiles;
        [SerializeField, Tooltip("Côtés de la salle qui possèdent une porte.")]
        Doors doors;
        [SerializeField, Tooltip("Ennemis placés dans la salle.")]
        List<EnemySpawn> enemies = new();
        [SerializeField, Tooltip("Coffres placés dans la salle.")]
        List<ChestSpawn> chests = new();
        [SerializeField, Tooltip("Difficulté ressentie, de 1 (facile) à 5 (très dur).")]
        int difficulty = 1;
        [SerializeField, Tooltip("Mots-clés libres pour filtrer les salles (ex : piège, trésor).")]
        List<string> tags = new();

        public int Width => width;
        public int Height => height;
        public Doors Doors => doors;
        public IReadOnlyList<EnemySpawn> Enemies => enemies;
        public IReadOnlyList<ChestSpawn> Chests => chests;
        public int Difficulty => difficulty;
        public IReadOnlyList<string> Tags => tags;

        public bool HasDoor(Doors side) => (doors & side) != 0;

        public TileType GetTile(int x, int y)
        {
            if (tiles == null || tiles.Length != width * height || x < 0 || y < 0 || x >= width || y >= height)
                return TileType.Wall;
            return tiles[y * width + x];
        }

        public bool ContainsBoss()
        {
            foreach (var spawn in enemies)
                if (spawn.enemy != null && spawn.enemy.IsBoss)
                    return true;
            return false;
        }

        /// <summary>Case située au milieu du côté donné, où la porte est dessinée.</summary>
        public Vector2Int GetDoorCell(Doors side) => side switch
        {
            Doors.North => new Vector2Int(width / 2, height - 1),
            Doors.South => new Vector2Int(width / 2, 0),
            Doors.East => new Vector2Int(width - 1, height / 2),
            Doors.West => new Vector2Int(0, height / 2),
            _ => throw new ArgumentOutOfRangeException(nameof(side))
        };
    }
}
