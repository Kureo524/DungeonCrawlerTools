using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Dialogue;
using UnityEngine;

namespace TheDungeon.Game
{
    /// <summary>Déroulé d'une conversation en jeu, avec la même logique que l'aperçu de l'outil Dialogues.</summary>
    public sealed class DialogueRunner
    {
        const float CharactersPerSecond = 55f;

        readonly IDialogueState state;
        float typed;

        public NpcData Npc { get; private set; }
        public DialogueNode Current { get; private set; }
        public bool Active => Current != null;
        public List<DialogueChoice> Choices { get; private set; } = new();
        string Text => Current?.text ?? string.Empty;
        public bool IsTyping => Current != null && typed < Text.Length;
        public string VisibleText => Text.Substring(0, Mathf.Min(Text.Length, Mathf.FloorToInt(typed)));

        public DialogueRunner(IDialogueState state)
        {
            this.state = state;
        }

        public void Start(NpcData npc)
        {
            Npc = npc;
            Show(DialogueLogic.Start(npc, state));
        }

        public void Tick(float unscaledDeltaTime)
        {
            if (IsTyping)
                typed += unscaledDeltaTime * CharactersPerSecond;
        }

        /// <summary>Clic ou Entrée : termine l'écriture du texte, sinon passe à la suite (s'il n'y a pas de choix).</summary>
        public void Advance()
        {
            if (Current == null)
                return;
            if (IsTyping)
            {
                typed = Text.Length;
                return;
            }
            // Sans réponse disponible (aucune, ou toutes verrouillées), on passe à la suite ou on termine.
            if (Choices.Count == 0)
                Show(Current.choices.Count == 0 && Current.next >= 0 ? Npc.FindNode(Current.next) : null);
        }

        public void Choose(int index)
        {
            if (IsTyping || index < 0 || index >= Choices.Count)
                return;
            var choice = Choices[index];
            DialogueLogic.ApplyAll(choice.consequences, state);
            Show(choice.target < 0 ? null : Npc.FindNode(choice.target));
        }

        void Show(DialogueNode node)
        {
            Current = node;
            typed = 0f;
            Choices = new List<DialogueChoice>();
            if (node == null)
                return;
            DialogueLogic.ApplyAll(node.consequences, state);
            Choices = DialogueLogic.AvailableChoices(node, state).ToList();
        }
    }
}
