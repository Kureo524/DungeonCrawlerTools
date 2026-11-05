using System;
using System.Collections.Generic;
using TheDungeon.Data;
using TheDungeon.Dialogue;
using UnityEngine;

namespace TheDungeon.Editor.Dialogues
{
    /// <summary>État de jeu simulé pour l'aperçu des dialogues : niveau, or, étage, marqueurs, objets et mécaniques, modifiables à la main.</summary>
    public sealed class SimulatedDialogueState : IDialogueState
    {
        readonly HashSet<string> flags = new();
        readonly List<ItemData> items = new();
        readonly Dictionary<string, int> mechanics = new();

        public event Action Changed;
        public int PlayerLevel { get; set; } = 1;
        public int Gold { get; set; }
        public int Floor { get; set; } = 1;
        public int Xp { get; private set; }
        public int Healed { get; private set; }

        public bool HasFlag(string flag) => flags.Contains(flag);
        public bool HasItem(ItemData item) => items.Contains(item);
        public int MechanicLevel(string mechanicId) => mechanics.TryGetValue(mechanicId ?? string.Empty, out var level) ? level : 0;

        public void SetMechanicLevel(string mechanicId, int level)
        {
            mechanics[mechanicId] = Mathf.Max(0, level);
            Changed?.Invoke();
        }

        public void SetFlag(string flag, bool value)
        {
            if (string.IsNullOrEmpty(flag))
                return;
            if (value) flags.Add(flag); else flags.Remove(flag);
            Changed?.Invoke();
        }

        public void AddGold(int amount)
        {
            Gold = Mathf.Max(0, Gold + amount);
            Changed?.Invoke();
        }

        public void AddItem(ItemData item)
        {
            if (item != null && !items.Contains(item))
                items.Add(item);
            Changed?.Invoke();
        }

        public void RemoveItem(ItemData item)
        {
            items.Remove(item);
            Changed?.Invoke();
        }

        public void AddXp(int amount)
        {
            Xp += amount;
            Changed?.Invoke();
        }

        public void Heal(int amount)
        {
            Healed += amount;
            Changed?.Invoke();
        }

        public void Reset()
        {
            flags.Clear();
            items.Clear();
            mechanics.Clear();
            PlayerLevel = 1;
            Gold = 0;
            Xp = 0;
            Healed = 0;
            Changed?.Invoke();
        }
    }
}
