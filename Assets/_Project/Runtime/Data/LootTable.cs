using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheDungeon.Data
{
    [Serializable]
    public struct LootEntry
    {
        [Tooltip("Objet pouvant tomber.")]
        public ItemData item;
        [Tooltip("Poids relatif : plus il est élevé, plus l'objet tombe souvent.")]
        public int weight;
    }

    [CreateAssetMenu(menuName = "The Dungeon/Loot Table", fileName = "LT_NewTable")]
    public class LootTable : ScriptableObject, IDisplayNamed
    {
        [SerializeField, Tooltip("Nom de la table, visible uniquement dans les outils.")]
        string displayName;
        [SerializeField, Tooltip("Objets possibles et leur poids.")]
        List<LootEntry> entries = new();
        [SerializeField, Range(0f, 1f), Tooltip("Chance qu'un tirage donne un objet (sinon rien).")]
        float dropChance = 0.5f;
        [SerializeField, Tooltip("Nombre de tirages effectués.")]
        int rolls = 1;
        [SerializeField, Tooltip("Premier étage où cette table peut être utilisée.")]
        int minFloor = 1;
        [SerializeField, Tooltip("Dernier étage où cette table peut être utilisée.")]
        int maxFloor = 3;

        public string DisplayName => displayName;
        public IReadOnlyList<LootEntry> Entries => entries;
        public float DropChance => dropChance;
        public int Rolls => rolls;
        public int MinFloor => minFloor;
        public int MaxFloor => maxFloor;

        public int TotalWeight
        {
            get
            {
                var total = 0;
                foreach (var entry in entries)
                    total += Mathf.Max(0, entry.weight);
                return total;
            }
        }

        /// <summary>Poids effectif d'une ligne, éventuellement multiplié selon la rareté (bonus de biome).</summary>
        public static float EffectiveWeight(LootEntry entry, Func<Rarity, float> rarityMultiplier = null)
        {
            var weight = Mathf.Max(0, entry.weight);
            if (rarityMultiplier != null && entry.item != null)
                return weight * Mathf.Max(0f, rarityMultiplier(entry.item.Rarity));
            return weight;
        }

        /// <summary>Probabilité qu'un tirage donne cet objet précis.</summary>
        public float GetProbability(LootEntry entry, Func<Rarity, float> rarityMultiplier = null)
        {
            var total = 0f;
            foreach (var other in entries)
                total += EffectiveWeight(other, rarityMultiplier);
            return total <= 0f ? 0f : dropChance * EffectiveWeight(entry, rarityMultiplier) / total;
        }

        public ItemData Roll(System.Random random, Func<Rarity, float> rarityMultiplier = null)
        {
            if (random.NextDouble() > dropChance)
                return null;

            var total = 0f;
            foreach (var entry in entries)
                total += EffectiveWeight(entry, rarityMultiplier);
            if (total <= 0f)
                return null;

            var pick = (float)random.NextDouble() * total;
            foreach (var entry in entries)
            {
                pick -= EffectiveWeight(entry, rarityMultiplier);
                if (pick < 0f)
                    return entry.item;
            }
            return null;
        }
    }
}
