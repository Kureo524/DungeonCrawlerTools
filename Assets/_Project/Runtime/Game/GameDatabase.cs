using System;
using System.Collections.Generic;
using TheDungeon.Data;
using UnityEngine;

namespace TheDungeon.Game
{
    [Serializable]
    public struct RarityColor
    {
        public int rarity;
        public Color color;
    }

    /// <summary>
    /// Tout ce que le jeu doit charger, rassemblé automatiquement par les outils (Tools > Jeu) avant chaque partie
    /// et chaque build : étages, progression, PNJ, salles (pour le test d'une salle) et sprites communs.
    /// </summary>
    [CreateAssetMenu(menuName = "The Dungeon/Game Database", fileName = "GameDatabase")]
    public class GameDatabase : ScriptableObject
    {
        [Tooltip("Étages dans l'ordre de descente.")]
        public List<FloorData> floors = new();
        public PlayerProgression progression;
        public List<NpcData> npcs = new();
        [Tooltip("Toutes les salles, pour « Tester cette salle ».")]
        public List<RoomData> rooms = new();
        [Tooltip("Raretés, de la plus commune à la plus rare.")]
        public List<RarityColor> rarityColors = new();

        [Header("Sprites communs")]
        public Sprite player;
        public Sprite chestClosed;
        public Sprite chestOpen;
        public Sprite doorClosed;
        public Sprite portal;
        public Sprite gold;
        public Sprite pedestal;

        public Color GetRarityColor(int rarity)
        {
            foreach (var entry in rarityColors)
                if (entry.rarity == rarity)
                    return entry.color;
            return Color.white;
        }

        /// <summary>Rang de la rareté (0 = la plus commune) ; une rareté inconnue (supprimée) passe sous toutes les autres.</summary>
        public int GetRarityRank(int rarity)
        {
            var index = rarityColors.FindIndex(entry => entry.rarity == rarity);
            return index >= 0 ? index : -1;
        }
    }
}
