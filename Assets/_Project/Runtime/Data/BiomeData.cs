using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheDungeon.Data
{
    [Serializable]
    public struct BiomeEnemy
    {
        [Tooltip("Ennemi pouvant apparaître dans ce biome.")]
        public EnemyData enemy;
        [Tooltip("Poids d'apparition : plus il est élevé, plus l'ennemi est fréquent.")]
        public int spawnWeight;
    }

    [Serializable]
    public struct BiomeProp
    {
        [Tooltip("Nom du décor, affiché dans la palette du Room Editor.")]
        public string name;
        [Tooltip("Visuel du décor.")]
        public Sprite sprite;
        [Tooltip("Coché : le décor bloque le passage (colonne, rocher…).")]
        public bool blocksMovement;
    }

    [Serializable]
    public struct RarityBoost
    {
        [Tooltip("Rareté concernée.")]
        public Rarity rarity;
        [Tooltip("Multiplicateur appliqué au poids de butin. 2 = deux fois plus fréquent, 0 = jamais.")]
        public float multiplier;
    }

    /// <summary>
    /// Preset de biome : visuels, ennemis, décors et butin autorisés. Une salle appartient toujours à un biome
    /// et ne peut utiliser que ce que son biome propose.
    /// </summary>
    [CreateAssetMenu(menuName = "The Dungeon/Biome", fileName = "BIO_NewBiome")]
    public class BiomeData : ScriptableObject, IDisplayNamed
    {
        [SerializeField, Tooltip("Nom du biome.")]
        string displayName;
        [SerializeField, Tooltip("Couleur du biome dans les outils (listes, plan des étages).")]
        Color color = new(0.5f, 0.6f, 0.7f);
        [SerializeField, Tooltip("Premier étage où ce biome peut apparaître.")]
        int minFloor = 1;
        [SerializeField, Tooltip("Dernier étage où ce biome peut apparaître.")]
        int maxFloor = 3;

        [SerializeField, Tooltip("Variantes de sol, mélangées automatiquement.")]
        List<Sprite> floorSprites = new();
        [SerializeField, Tooltip("Visuel des murs.")]
        Sprite wallSprite;
        [SerializeField, Tooltip("Visuel des trous (eau, lave, vide…).")]
        Sprite pitSprite;

        [SerializeField, Tooltip("Ennemis autorisés dans les salles de ce biome.")]
        List<BiomeEnemy> enemies = new();
        [SerializeField, Tooltip("Décors disponibles dans les salles de ce biome.")]
        List<BiomeProp> props = new();
        [SerializeField, Tooltip("Tables de butin utilisables par les coffres de ce biome.")]
        List<LootTable> chestLootTables = new();
        [SerializeField, Tooltip("Bonus de butin selon la rareté, appliqués à tous les tirages du biome.")]
        List<RarityBoost> rarityBoosts = new();

        public string DisplayName => displayName;
        public Color Color => color;
        public int MinFloor => minFloor;
        public int MaxFloor => maxFloor;
        public IReadOnlyList<Sprite> FloorSprites => floorSprites;
        public Sprite WallSprite => wallSprite;
        public Sprite PitSprite => pitSprite;
        public IReadOnlyList<BiomeEnemy> Enemies => enemies;
        public IReadOnlyList<BiomeProp> Props => props;
        public IReadOnlyList<LootTable> ChestLootTables => chestLootTables;
        public IReadOnlyList<RarityBoost> RarityBoosts => rarityBoosts;

        public bool AllowsEnemy(EnemyData enemy)
        {
            foreach (var entry in enemies)
                if (entry.enemy == enemy)
                    return true;
            return false;
        }

        public bool TryGetProp(Sprite sprite, out BiomeProp prop)
        {
            foreach (var candidate in props)
            {
                if (candidate.sprite == sprite)
                {
                    prop = candidate;
                    return true;
                }
            }
            prop = default;
            return false;
        }

        public float GetRarityMultiplier(Rarity rarity)
        {
            foreach (var boost in rarityBoosts)
                if (boost.rarity == rarity)
                    return boost.multiplier;
            return 1f;
        }

        public Sprite GetTileSprite(TileType tile, int x, int y) => tile switch
        {
            TileType.Wall => wallSprite,
            TileType.Pit => pitSprite,
            _ => floorSprites.Count == 0 ? null : floorSprites[Mathf.Abs(x * 73856093 ^ y * 19349663) % floorSprites.Count]
        };
    }
}
