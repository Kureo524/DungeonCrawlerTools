using TheDungeon.Data;
using TheDungeon.Editor.Common;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Dialogues
{
    /// <summary>Contenu de la carte qui s'affiche au survol d'une réplique : interlocuteur, texte, effets et réponses avec leurs conditions.</summary>
    internal static class DialogueNodeCard
    {
        public static void Fill(VisualElement card, DialogueNodeView view, NpcData npc, Color speakerColor)
        {
            var node = view.Node;
            card.Clear();
            var header = new VisualElement { pickingMode = PickingMode.Ignore };
            header.AddToClassList("forge-dialogue-card__header");
            var dot = new VisualElement { pickingMode = PickingMode.Ignore };
            dot.AddToClassList("forge-dialogue-card__dot");
            dot.style.backgroundColor = speakerColor;
            header.Add(dot);
            header.Add(CardLabel(DialogueText.Speaker(node.speaker, npc), "forge-dialogue-card__speaker"));
            header.Add(CardLabel($"#{node.id}", "forge-dialogue-card__id"));
            foreach (var (text, className) in view.Tags())
                header.Add(CardLabel(text, $"forge-chip {className}"));
            card.Add(header);

            card.Add(CardLabel(ForgeText.Quote(node.text), "forge-dialogue-card__text"));

            if (node.consequences.Count > 0)
            {
                card.Add(CardLabel("Effets de la réplique", "forge-dialogue-card__title"));
                var chips = new VisualElement { pickingMode = PickingMode.Ignore };
                chips.AddToClassList("forge-chat__chips");
                foreach (var consequence in node.consequences)
                    chips.Add(CardLabel(DialogueText.Describe(consequence), "forge-chip forge-chip--effect"));
                card.Add(chips);
            }

            if (node.choices.Count > 0)
            {
                card.Add(CardLabel($"Réponses du joueur ({node.choices.Count})", "forge-dialogue-card__title"));
                for (var i = 0; i < node.choices.Count; i++)
                {
                    var choice = node.choices[i];
                    var row = new VisualElement { pickingMode = PickingMode.Ignore };
                    row.AddToClassList("forge-dialogue-card__choice");
                    row.Add(CardLabel($"{i + 1}. {ForgeText.Quote(choice.text)}", "forge-dialogue-card__choice-text"));
                    var chips = new VisualElement { pickingMode = PickingMode.Ignore };
                    chips.AddToClassList("forge-chat__chips");
                    foreach (var condition in choice.conditions)
                        chips.Add(CardLabel(DialogueText.Describe(condition), "forge-chip forge-chip--condition"));
                    foreach (var consequence in choice.consequences)
                        chips.Add(CardLabel(DialogueText.Describe(consequence), "forge-chip forge-chip--effect"));
                    chips.Add(CardLabel(choice.target < 0 ? "→ Fin" : $"→ #{choice.target}", "forge-chip forge-chip--target"));
                    row.Add(chips);
                    card.Add(row);
                }
            }
            else
            {
                card.Add(CardLabel(ForgeText.System(node.next < 0 ? "Fin de la conversation." : $"Suite : réplique #{node.next}."), "forge-dialogue-card__next"));
            }
            card.Add(CardLabel("Clic : modifier · Glisser : déplacer · Clic droit : actions", "forge-dialogue-card__hint"));
        }

        static Label CardLabel(string text, string classNames)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            foreach (var className in classNames.Split(' '))
                label.AddToClassList(className);
            return label;
        }
    }
}
