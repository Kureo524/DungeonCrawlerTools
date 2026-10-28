using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheDungeon.Data
{
    [Serializable]
    public struct EnemySpawn
    {
        [Tooltip("Ennemi qui apparaît (doit appartenir au biome de la salle).")]
        public EnemyData enemy;
        [Tooltip("Case de la salle (0,0 = coin bas gauche).")]
        public Vector2Int position;
    }

    [Serializable]
    public struct ChestSpawn
    {
        [Tooltip("Table de butin du coffre (doit être proposée par le biome de la salle).")]
        public LootTable lootTable;
        [Tooltip("Case de la salle (0,0 = coin bas gauche).")]
        public Vector2Int position;
    }

    [Serializable]
    public struct PropSpawn
    {
        [Tooltip("Décor placé (doit appartenir au biome de la salle).")]
        public Sprite sprite;
        [Tooltip("Case de la salle (0,0 = coin bas gauche).")]
        public Vector2Int position;
    }

    [CreateAssetMenu(menuName = "The Dungeon/Room", fileName = "RM_NewRoom")]
    public class RoomData : ScriptableObject, IDisplayNamed
    {
        public static readonly Vector2Int NoSpawn = new(-1, -1);

        [SerializeField, Tooltip("Nom de la salle, visible dans les outils.")]
        string displayName;
        [SerializeField, Tooltip("Biome de la salle : il fournit les visuels, les ennemis, les décors et le butin autorisés.")]
        BiomeData biome;
        [SerializeField, Tooltip("Rôle de la salle dans l'étage.")]
        RoomType roomType = RoomType.Combat;
        [SerializeField, Tooltip("Largeur en cases.")]
        int width = 15;
        [SerializeField, Tooltip("Hauteur en cases.")]
        int height = 11;
        [SerializeField, Tooltip("Cases de la salle, ligne par ligne depuis le bas.")]
        TileType[] tiles;
        [SerializeField, Tooltip("Côtés de la salle qui possèdent une entrée.")]
        Doors doors;
        [SerializeField, Tooltip("Ennemis placés dans la salle.")]
        List<EnemySpawn> enemies = new();
        [SerializeField, Tooltip("Coffres placés dans la salle.")]
        List<ChestSpawn> chests = new();
        [SerializeField, Tooltip("Décors placés dans la salle.")]
        List<PropSpawn> props = new();
        [SerializeField, Tooltip("Case d'apparition du joueur (salles de départ uniquement).")]
        Vector2Int playerSpawn = NoSpawn;
        [SerializeField, Tooltip("Difficulté ressentie, de 1 (facile) à 5 (très dur).")]
        int difficulty = 1;
        [SerializeField, Tooltip("Mots-clés libres pour filtrer les salles (ex : piège, trésor).")]
        List<string> tags = new();

        public string DisplayName => displayName;
        public BiomeData Biome => biome;
        public RoomType RoomType => roomType;
        public int Width => width;
        public int Height => height;
        public Doors Doors => doors;
        public IReadOnlyList<EnemySpawn> Enemies => enemies;
        public IReadOnlyList<ChestSpawn> Chests => chests;
        public IReadOnlyList<PropSpawn> Props => props;
        public Vector2Int PlayerSpawn => playerSpawn;
        public bool HasPlayerSpawn => playerSpawn != NoSpawn;
        public int Difficulty => difficulty;
        public IReadOnlyList<string> Tags => tags;
        public bool HasValidTiles => tiles != null && tiles.Length == width * height;

        public bool HasDoor(Doors side) => (doors & side) != 0;

        public bool Contains(Vector2Int cell) => cell.x >= 0 && cell.y >= 0 && cell.x < width && cell.y < height;

        public TileType GetTile(int x, int y)
        {
            if (!HasValidTiles || !Contains(new Vector2Int(x, y)))
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

        /// <summary>Case située au milieu du côté donné, où se trouve l'entrée.</summary>
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
