using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheDungeon.Data
{
    [Serializable]
    public struct LevelEntry
    {
        [Tooltip("Points de vie maximum à ce niveau.")]
        public int maxHealth;
        [Tooltip("Dégâts de base du joueur à ce niveau.")]
        public int damage;
        [Tooltip("XP totale nécessaire pour passer au niveau suivant.")]
        public int xpRequired;
        [Tooltip("Capacité ou contenu débloqué en atteignant ce niveau (facultatif).")]
        public string unlock;
    }

    [CreateAssetMenu(menuName = "The Dungeon/Player Progression", fileName = "PRG_Player")]
    public class PlayerProgression : ScriptableObject
    {
        [SerializeField, Tooltip("Une ligne par niveau, la première correspond au niveau 1.")]
        List<LevelEntry> levels = new();

        public IReadOnlyList<LevelEntry> Levels => levels;
        public int MaxLevel => levels.Count;

        public LevelEntry GetLevel(int level) => levels[Mathf.Clamp(level - 1, 0, levels.Count - 1)];
    }
}
