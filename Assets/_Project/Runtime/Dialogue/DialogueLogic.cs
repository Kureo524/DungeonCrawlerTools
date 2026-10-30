using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;

namespace TheDungeon.Dialogue
{
    /// <summary>État du jeu consulté et modifié par les dialogues (implémenté par le jeu, et simulé par l'aperçu des outils).</summary>
    public interface IDialogueState
    {
        int PlayerLevel { get; }
        int Gold { get; }
        int Floor { get; }
        bool HasFlag(string flag);
        bool HasItem(ItemData item);
        /// <summary>Niveau d'une mécanique du personnage (progression), 0 si elle n'est pas débloquée.</summary>
        int MechanicLevel(string mechanicId);
        void SetFlag(string flag, bool value);
        void AddGold(int amount);
        void AddItem(ItemData item);
        void RemoveItem(ItemData item);
        void AddXp(int amount);
        void Heal(int amount);
    }

    /// <summary>Règles d'exécution des dialogues, partagées par le jeu et l'aperçu des outils.</summary>
    public static class DialogueLogic
    {
        public static bool IsMet(DialogueCondition condition, IDialogueState state) => condition.type switch
        {
            ConditionType.PlayerLevelAtLeast => state.PlayerLevel >= condition.value,
            ConditionType.PlayerLevelBelow => state.PlayerLevel < condition.value,
            ConditionType.HasItem => condition.item != null && state.HasItem(condition.item),
            ConditionType.LacksItem => condition.item == null || !state.HasItem(condition.item),
            ConditionType.FlagSet => !string.IsNullOrEmpty(condition.flag) && state.HasFlag(condition.flag),
            ConditionType.FlagNotSet => string.IsNullOrEmpty(condition.flag) || !state.HasFlag(condition.flag),
            ConditionType.GoldAtLeast => state.Gold >= condition.value,
            ConditionType.FloorAtLeast => state.Floor >= condition.value,
            ConditionType.FloorIn => FloorSet.Contains(condition.value, state.Floor),
            ConditionType.MechanicAtLeast => !string.IsNullOrEmpty(condition.flag) && state.MechanicLevel(condition.flag) >= condition.value,
            _ => true
        };

        public static bool AllMet(IEnumerable<DialogueCondition> conditions, IDialogueState state) =>
            conditions == null || conditions.All(condition => IsMet(condition, state));

        public static void Apply(DialogueConsequence consequence, IDialogueState state)
        {
            switch (consequence.type)
            {
                case ConsequenceType.SetFlag: state.SetFlag(consequence.flag, true); break;
                case ConsequenceType.ClearFlag: state.SetFlag(consequence.flag, false); break;
                case ConsequenceType.GiveItem: state.AddItem(consequence.item); break;
                case ConsequenceType.TakeItem: state.RemoveItem(consequence.item); break;
                case ConsequenceType.GiveGold: state.AddGold(consequence.value); break;
                case ConsequenceType.TakeGold: state.AddGold(-consequence.value); break;
                case ConsequenceType.GiveXp: state.AddXp(consequence.value); break;
                case ConsequenceType.Heal: state.Heal(consequence.value); break;
            }
        }

        public static void ApplyAll(IEnumerable<DialogueConsequence> consequences, IDialogueState state)
        {
            if (consequences == null)
                return;
            foreach (var consequence in consequences)
                Apply(consequence, state);
        }

        /// <summary>Première réplique : le premier début alternatif dont les conditions sont remplies, sinon le début normal.</summary>
        public static DialogueNode Start(NpcData npc, IDialogueState state)
        {
            foreach (var entry in npc.Entries)
                if (AllMet(entry.conditions, state) && npc.FindNode(entry.node) is { } node)
                    return node;
            return npc.FindNode(npc.StartNode);
        }

        /// <summary>Identifiants des répliques atteignables depuis le début normal ou un début alternatif.</summary>
        public static HashSet<int> ReachableNodes(NpcData npc)
        {
            var visited = new HashSet<int>();
            var stack = new Stack<int>(new[] { npc.StartNode }.Concat(npc.Entries.Select(e => e.node)));
            while (stack.Count > 0)
            {
                var id = stack.Pop();
                if (id < 0 || npc.FindNode(id) is not { } node || !visited.Add(id))
                    continue;
                stack.Push(node.next);
                foreach (var choice in node.choices)
                    stack.Push(choice.target);
            }
            return visited;
        }

        /// <summary>Réponses proposées au joueur dans l'état actuel.</summary>
        public static IEnumerable<DialogueChoice> AvailableChoices(DialogueNode node, IDialogueState state) =>
            node.choices.Where(choice => AllMet(choice.conditions, state));
    }
}
