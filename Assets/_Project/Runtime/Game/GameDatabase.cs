using System;
using System.Collections.Generic;
using TheDungeon.Data;
using UnityEngine;

namespace TheDungeon.Game
{
    [Serializable]
    public struct RarityColor
    {
        public Rarity rarity;
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
        public List<RarityColor> rarityColors = new();

        [Header("Sprites communs")]
        public Sprite player;
        public Sprite chestClosed;
        public Sprite chestOpen;
        public Sprite doorClosed;
        public Sprite portal;
        public Sprite gold;
        public Sprite pedestal;

        public Color GetRarityColor(Rarity rarity)
        {
            foreach (var entry in rarityColors)
                if (entry.rarity == rarity)
                    return entry.color;
            return Color.white;
        }
    }
}
