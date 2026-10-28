using System;
using System.Collections.Generic;
using TheDungeon.Data;
using UnityEditor;
using UnityEngine;

namespace TheDungeon.Editor.Common
{
    [Serializable]
    public struct IntRange
    {
        public int min;
        public int max;

        public IntRange(int min, int max)
        {
            this.min = min;
            this.max = max;
        }

        public bool Contains(int value) => value >= min && value <= max;
        public int Clamp(int value) => Mathf.Clamp(value, min, max);
        public override string ToString() => $"{min} à {max}";
    }

    [Serializable]
    public struct FloatRange
    {
        public float min;
        public float max;

        public FloatRange(float min, float max)
        {
            this.min = min;
            this.max = max;
        }

        public bool Contains(float value) => value >= min && value <= max;
        public float Clamp(float value) => Mathf.Clamp(value, min, max);
        public override string ToString() => $"{min:0.##} à {max:0.##}";
    }

    [Serializable]
    public struct RarityRule
    {
        [Tooltip("Rareté concernée.")]
        public Rarity rarity;
        [Tooltip("Couleur utilisée dans tous les outils.")]
        public Color color;
        [Tooltip("Bonus de statistique maximum autorisé pour un objet de cette rareté.")]
        public int maxStatValue;
        [Tooltip("Prix autorisé pour un objet de cette rareté.")]
        public IntRange price;
        [Tooltip("Poids proposé par défaut quand l'objet est ajouté à une table de butin.")]
        public int defaultLootWeight;
    }

    /// <summary>
    /// Cadre fixé par les développeurs : les outils brident les champs à ces valeurs
    /// et le Validator s'en sert comme référence. Réservé aux développeurs (Tools > Settings (dev)).
    /// </summary>
    public class ForgeDesignLimits : ScriptableObject
    {
        const string AssetPath = DungeonPaths.Settings + "/ForgeDesignLimits.asset";

        [Header("Structure du donjon")]
        [Tooltip("Nombre d'étages du donjon.")]
        public int floorCount = 3;
        [Tooltip("Nombre de salles prévu par étage.")]
        public int roomsPerFloor = 10;
        [Tooltip("Taille du plan d'un étage, en salles.")]
        public Vector2Int floorGridSize = new(6, 6);

        [Header("Salles")]
        public IntRange roomWidth = new(6, 20);
        public IntRange roomHeight = new(5, 14);
        [Tooltip("Nombre maximum d'ennemis dans une salle.")]
        public int maxEnemiesPerRoom = 12;
        [Tooltip("Nombre maximum de coffres dans une salle.")]
        public int maxChestsPerRoom = 3;
        public IntRange roomDifficulty = new(1, 5);

        [Header("Biomes")]
        [Tooltip("Nombre maximum d'ennemis différents proposés par un biome.")]
        public int maxBiomeEnemies = 12;
        [Tooltip("Nombre maximum de décors proposés par un biome.")]
        public int maxBiomeProps = 12;
        [Tooltip("Nombre maximum de tables de butin de coffre par biome.")]
        public int maxBiomeLootTables = 6;
        [Tooltip("Variantes de sol maximum par biome.")]
        public int maxBiomeFloorVariants = 4;
        public IntRange biomeSpawnWeight = new(1, 100);
        public FloatRange biomeRarityBoost = new(0f, 5f);

        [Header("Objets")]
        public List<RarityRule> rarities = new()
        {
            new RarityRule { rarity = Rarity.Common, color = new Color(0.75f, 0.75f, 0.75f), maxStatValue = 5, price = new IntRange(1, 50), defaultLootWeight = 100 },
            new RarityRule { rarity = Rarity.Uncommon, color = new Color(0.35f, 0.8f, 0.35f), maxStatValue = 10, price = new IntRange(20, 150), defaultLootWeight = 50 },
            new RarityRule { rarity = Rarity.Rare, color = new Color(0.3f, 0.55f, 1f), maxStatValue = 18, price = new IntRange(100, 400), defaultLootWeight = 20 },
            new RarityRule { rarity = Rarity.Epic, color = new Color(0.7f, 0.35f, 0.95f), maxStatValue = 28, price = new IntRange(300, 1000), defaultLootWeight = 6 },
            new RarityRule { rarity = Rarity.Legendary, color = new Color(1f, 0.6f, 0.1f), maxStatValue = 40, price = new IntRange(800, 3000), defaultLootWeight = 1 }
        };
        [Tooltip("Nombre maximum de bonus de stats par objet.")]
        public int maxStatsPerItem = 4;
        [Tooltip("Nombre maximum d'effets par objet.")]
        public int maxEffectsPerItem = 2;
        public FloatRange effectValue = new(0f, 100f);
        public FloatRange effectDuration = new(0f, 30f);

        [Header("Ennemis")]
        public IntRange enemyHealth = new(1, 500);
        public IntRange enemyDamage = new(1, 60);
        public IntRange enemyDefense = new(0, 30);
        public FloatRange enemySpeed = new(0f, 8f);
        public IntRange enemyXp = new(1, 500);
        public FloatRange enemyFloorScaling = new(0f, 1f);

        [Header("Tables de butin")]
        [Tooltip("Nombre maximum d'objets différents dans une table.")]
        public int maxLootEntries = 12;
        public IntRange lootWeight = new(1, 1000);
        public IntRange lootRolls = new(1, 5);

        [Header("Progression du joueur")]
        public int maxLevel = 20;
        public IntRange playerHealth = new(10, 2000);
        public IntRange playerDamage = new(1, 300);
        public IntRange xpRequired = new(1, 100000);

        static ForgeDesignLimits instance;

        public static ForgeDesignLimits Instance
        {
            get
            {
                if (instance != null)
                    return instance;
                instance = AssetDatabase.LoadAssetAtPath<ForgeDesignLimits>(AssetPath);
                if (instance == null)
                {
                    instance = CreateInstance<ForgeDesignLimits>();
                    AssetFactory.EnsureFolder(DungeonPaths.Settings);
                    AssetDatabase.CreateAsset(instance, AssetPath);
                    AssetDatabase.SaveAssets();
                }
                return instance;
            }
        }

        public RarityRule GetRarity(Rarity rarity)
        {
            foreach (var rule in rarities)
                if (rule.rarity == rarity)
                    return rule;
            return new RarityRule { rarity = rarity, color = Color.white, maxStatValue = int.MaxValue, price = new IntRange(0, int.MaxValue), defaultLootWeight = 1 };
        }
    }
}
