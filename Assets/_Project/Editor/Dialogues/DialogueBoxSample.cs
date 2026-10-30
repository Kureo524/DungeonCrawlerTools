using TheDungeon.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Dialogues
{
    /// <summary>Boîte de dialogue miniature aux couleurs du personnage : on voit tout de suite l'effet de chaque réglage.</summary>
    public sealed class DialogueBoxSample : VisualElement
    {
        readonly NpcData npc;
        readonly VisualElement box;
        readonly Image portrait;
        readonly Label plate;
        readonly Label text;
        readonly Label[] choices = new Label[2];

        public DialogueBoxSample(NpcData npc)
        {
            this.npc = npc;
            AddToClassList("forge-dialogue-sample");
            box = new VisualElement();
            box.AddToClassList("forge-dialogue-sample__box");
            portrait = new Image { scaleMode = ScaleMode.ScaleToFit };
            portrait.AddToClassList("forge-dialogue-sample__portrait");
            box.Add(portrait);
            var content = new VisualElement();
            content.AddToClassList("forge-dialogue-sample__content");
            plate = new Label();
            plate.AddToClassList("forge-dialogue-sample__plate");
            content.Add(plate);
            text = new Label();
            text.AddToClassList("forge-dialogue-sample__text");
            content.Add(text);
            for (var i = 0; i < choices.Length; i++)
            {
                choices[i] = new Label();
                choices[i].AddToClassList("forge-dialogue-sample__choice");
                content.Add(choices[i]);
            }
            box.Add(content);
            Add(box);
            Refresh();
        }

        public void Refresh()
        {
            var look = npc.Style;
            box.style.backgroundColor = Opaque(look.boxBackground);
            SetBorder(box, look.boxBorder);
            SetBorder(portrait, look.boxBorder);
            portrait.sprite = npc.Portrait;
            plate.text = string.IsNullOrWhiteSpace(npc.DisplayName) ? "PNJ" : npc.DisplayName;
            plate.style.backgroundColor = npc.Color;
            plate.style.color = look.nameText;
            var first = npc.FindNode(npc.StartNode);
            text.text = first != null && !string.IsNullOrWhiteSpace(first.text) ? DialogueEditing.Excerpt(first.text, 90) : "Bonjour, voyageur.";
            text.style.color = look.text;
            var replies = first?.choices;
            for (var i = 0; i < choices.Length; i++)
            {
                var reply = replies != null && i < replies.Count ? replies[i].text : i == 0 ? "Bonjour." : "Au revoir.";
                choices[i].text = $"{i + 1}.  {DialogueEditing.Excerpt(reply, 40)}";
                choices[i].style.backgroundColor = look.choiceBackground;
                choices[i].style.color = look.choiceText;
                SetBorder(choices[i], look.choiceBorder);
            }
        }

        /// <summary>Le fond de la boîte est posé sur le décor en jeu : ici on le montre sur un sol sombre.</summary>
        static Color Opaque(Color color) => Color.Lerp(new Color(0.11f, 0.1f, 0.13f), new Color(color.r, color.g, color.b), color.a);

        static void SetBorder(VisualElement element, Color color) =>
            element.style.borderTopColor = element.style.borderBottomColor = element.style.borderLeftColor = element.style.borderRightColor = color;
    }
}
