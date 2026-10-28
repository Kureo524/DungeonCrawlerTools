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
    public class LootTable : ScriptableObject
    {
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

        /// <summary>Probabilité qu'un tirage donne cet objet précis.</summary>
        public float GetProbability(LootEntry entry)
        {
            var total = TotalWeight;
            return total == 0 ? 0f : dropChance * Mathf.Max(0, entry.weight) / total;
        }

        public ItemData Roll(System.Random random)
        {
            var total = TotalWeight;
            if (total == 0 || random.NextDouble() > dropChance)
                return null;

            var pick = random.Next(total);
            foreach (var entry in entries)
            {
                pick -= Mathf.Max(0, entry.weight);
                if (pick < 0)
                    return entry.item;
            }
            return null;
        }
    }
}
