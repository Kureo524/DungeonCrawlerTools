using System;
using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Dialogues
{
    /// <summary>
    /// Réglages du joueur simulé de l'aperçu des dialogues : niveau, or, étage, puis seulement les mécaniques,
    /// marqueurs et objets que la conversation cite. Chaque changement relance la conversation.
    /// </summary>
    internal sealed class SimulatedPlayerPanel : VisualElement
    {
        readonly SimulatedDialogueState state;
        readonly Func<NpcData> npc;
        readonly Action restart;

        /// <param name="restart">Relance la conversation avec le nouvel état.</param>
        public SimulatedPlayerPanel(SimulatedDialogueState state, Func<NpcData> npc, Action restart)
        {
            this.state = state;
            this.npc = npc;
            this.restart = restart;
            ForgeLabels.Track(this);
        }

        public void Refresh()
        {
            Clear();
            var data = npc();
            AddBasics();
            AddMechanics(data);
            AddFlags(data);
            AddItems(data);
        }

        /// <summary>Niveau (borné par la progression utilisée en jeu), or et étage.</summary>
        void AddBasics()
        {
            var limits = ForgeDesignLimits.Instance;
            var progression = TheDungeon.Editor.Play.GameLauncher.ActiveProgression;
            var maxLevel = progression != null ? Mathf.Clamp(progression.MaxLevel, 1, limits.maxLevelCap) : limits.maxLevelCap;
            state.PlayerLevel = Mathf.Clamp(state.PlayerLevel, 1, maxLevel);
            var level = new SliderInt("Niveau", 1, maxLevel)
            {
                value = state.PlayerLevel,
                showInputField = true,
                tooltip = progression != null
                    ? $"Niveau du joueur, pour tester les conditions de niveau (1 à {maxLevel}, progression '{progression.DisplayName}')."
                    : "Niveau du joueur, pour tester les conditions de niveau."
            };
            level.RegisterValueChangedCallback(evt => { state.PlayerLevel = Mathf.Clamp(evt.newValue, 1, maxLevel); restart(); });
            Add(level);
            var gold = new IntegerField("Or") { value = state.Gold, tooltip = "Or du joueur." };
            gold.RegisterValueChangedCallback(evt => { state.Gold = Mathf.Max(0, evt.newValue); restart(); });
            Add(gold);
            var floor = new SliderInt("Étage", 1, limits.floorCount) { value = state.Floor, showInputField = true, tooltip = "Étage où a lieu la rencontre." };
            floor.RegisterValueChangedCallback(evt => { state.Floor = evt.newValue; restart(); });
            Add(floor);
        }

        /// <summary>Mécaniques citées par les conditions : leur niveau (0 = pas encore débloquée).</summary>
        void AddMechanics(NpcData data)
        {
            var mechanicIds = data == null ? new List<string>() : data.Entries.SelectMany(e => e.conditions)
                .Concat(data.Nodes.SelectMany(n => n.choices).SelectMany(c => c.conditions))
                .Where(c => c.type == ConditionType.MechanicAtLeast && !string.IsNullOrEmpty(c.flag)).Select(c => c.flag).Distinct().ToList();
            if (mechanicIds.Count == 0)
                return;
            Add(ForgeUI.Label("Mécaniques", "forge-palette__title"));
            foreach (var id in mechanicIds)
            {
                var mechanic = DialogueText.FindMechanic(id);
                var slider = new SliderInt(mechanic != null ? mechanic.displayName : ForgeText.Redacted, 0, Mathf.Max(1, mechanic?.MaxRank ?? 1))
                    { value = state.MechanicLevel(id), showInputField = true, tooltip = "Niveau de la mécanique (0 = pas encore débloquée)." };
                slider.RegisterValueChangedCallback(evt => { state.SetMechanicLevel(id, evt.newValue); restart(); });
                Add(slider);
            }
        }

        void AddFlags(NpcData data)
        {
            Add(ForgeUI.Label("Marqueurs d'histoire", "forge-palette__title"));
            var flags = data == null ? new List<string>() : DialogueRulesEditor.FlagsUsedIn(new[] { data }).Distinct().OrderBy(f => f).ToList();
            if (flags.Count == 0)
                Add(ForgeUI.Label("Aucun marqueur dans cette conversation.", "forge-hint"));
            foreach (var flag in flags)
            {
                var toggle = new Toggle(flag) { value = state.HasFlag(flag), tooltip = "Cochez pour simuler un marqueur déjà activé (ex : quête acceptée plus tôt)." };
                toggle.RegisterValueChangedCallback(evt => { state.SetFlag(flag, evt.newValue); restart(); });
                Add(toggle);
            }
        }

        void AddItems(NpcData data)
        {
            Add(ForgeUI.Label("Inventaire", "forge-palette__title"));
            var items = data == null ? new List<ItemData>() : ItemsUsedIn(data).ToList();
            if (items.Count == 0)
                Add(ForgeUI.Label("Aucun objet cité dans cette conversation.", "forge-hint"));
            foreach (var item in items)
            {
                var toggle = new Toggle(item.DisplayName) { value = state.HasItem(item), tooltip = "Cochez si le joueur possède déjà cet objet." };
                toggle.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue) state.AddItem(item); else state.RemoveItem(item);
                    restart();
                });
                Add(toggle);
            }
        }

        /// <summary>Objets cités par les conditions et les effets de la conversation.</summary>
        static IEnumerable<ItemData> ItemsUsedIn(NpcData npc)
        {
            var conditions = npc.Entries.SelectMany(e => e.conditions)
                .Concat(npc.Nodes.SelectMany(n => n.choices).SelectMany(c => c.conditions));
            var consequences = npc.Nodes.SelectMany(n => n.consequences)
                .Concat(npc.Nodes.SelectMany(n => n.choices).SelectMany(c => c.consequences));
            return conditions.Select(c => c.item).Concat(consequences.Select(c => c.item)).Where(i => i != null).Distinct();
        }
    }
}
