using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using TheDungeon.Editor.Common.Charts;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Dialogues
{
    /// <summary>Une réplique de l'arbre : en-tête, texte, réponses du joueur avec leur rond de sortie.</summary>
    internal sealed class DialogueNodeView : VisualElement
    {
        readonly DialogueGraphView graph;
        readonly VisualElement input;
        readonly List<VisualElement> outputs = new();

        public DialogueNode Node { get; }
        public int Id => Node.id;
        public Vector2 Position { get; private set; }

        public DialogueNodeView(DialogueGraphView graph, DialogueNode node, bool orphan)
        {
            this.graph = graph;
            Node = node;
            var npc = graph.Npc;
            AddToClassList("forge-dialogue-node");
            EnableInClassList("forge-dialogue-node--selected", node.id == graph.SelectedId);
            EnableInClassList("forge-dialogue-node--orphan", orphan);
            EnableInClassList($"forge-dialogue-node--{node.speaker.ToString().ToLowerInvariant()}", true);
            style.width = DialogueGraphView.NodeWidth;
            style.borderTopColor = graph.SpeakerColor(node.speaker);

            input = new VisualElement { pickingMode = PickingMode.Ignore };
            input.AddToClassList("forge-dialogue-node__input");
            Add(input);

            var header = new VisualElement();
            header.AddToClassList("forge-dialogue-node__header");
            header.Add(NodeLabel(DialogueText.Speaker(node.speaker, npc), "forge-dialogue-node__speaker"));
            header.Add(NodeLabel($"#{node.id}", "forge-dialogue-node__id"));
            foreach (var (text, className) in Tags())
            {
                var chip = NodeLabel(text, "forge-chip");
                chip.AddToClassList(className);
                header.Add(chip);
            }
            Add(header);

            var body = NodeLabel(string.IsNullOrWhiteSpace(node.text) ? ForgeText.Redacted : DialogueEditing.Excerpt(node.text, 140), "forge-dialogue-node__text");
            Add(body);

            if (node.consequences.Count > 0)
            {
                var effects = NodeLabel($"✦ {node.consequences.Count} effet(s)", "forge-dialogue-node__effects");
                Add(effects);
            }

            if (node.choices.Count > 0)
            {
                var choices = new VisualElement();
                choices.AddToClassList("forge-dialogue-node__choices");
                for (var i = 0; i < node.choices.Count; i++)
                {
                    var choice = node.choices[i];
                    var row = new VisualElement();
                    row.AddToClassList("forge-dialogue-node__choice");
                    // Un rond de chaque côté : le lien part de l'un ou de l'autre, jamais des deux.
                    var linked = choice.target >= 0;
                    row.Add(Port(i, true, DialogueGraphView.LinkColor(i, node), "forge-dialogue-node__port--left", linked && choice.linkFromLeft, linked && !choice.linkFromLeft));
                    if (choice.conditions.Count > 0)
                        row.Add(NodeLabel("◆", "forge-dialogue-node__lock"));
                    var choiceText = NodeLabel($"{i + 1}. {DialogueEditing.Excerpt(choice.text, 34)}", "forge-dialogue-node__choice-text");
                    // Une réponse aux couleurs propres apparaît dans l'arbre avec sa couleur de texte.
                    if (choice.customLook)
                        choiceText.style.color = new Color(choice.textColor.r, choice.textColor.g, choice.textColor.b, 1f);
                    row.Add(choiceText);
                    row.Add(Port(i, false, DialogueGraphView.LinkColor(i, node), "forge-dialogue-node__port--side", linked && !choice.linkFromLeft, linked && choice.linkFromLeft));
                    choices.Add(row);
                }
                Add(choices);
            }
            else
            {
                Add(Port(-1, false, ChartColors.Neutral, "forge-dialogue-node__port--bottom", node.next >= 0, false));
            }

            RegisterCallback<PointerDownEvent>(evt => graph.OnNodeDown(this, evt));
            RegisterCallback<PointerEnterEvent>(_ => graph.ScheduleCard(this));
            RegisterCallback<PointerLeaveEvent>(_ => graph.HideCard());
        }

        public IEnumerable<(string text, string className)> Tags()
        {
            if (Node.id == graph.Npc.StartNode)
                yield return ("DÉBUT", "forge-chip--start");
            if (graph.Npc.Entries.Any(e => e.node == Node.id))
                yield return ("DÉBUT ALT.", "forge-chip--start");
            if (Node.choices.Count == 0 && Node.next < 0)
                yield return ("FIN", "forge-chip--target");
        }

        /// <param name="linked">Le lien part de ce rond (rond plein).</param>
        /// <param name="otherSide">Le lien part du rond de l'autre côté : celui-ci est estompé.</param>
        VisualElement Port(int choice, bool left, Color color, string className, bool linked, bool otherSide)
        {
            var port = new VisualElement
            {
                tooltip = choice < 0
                    ? "Suite : glissez vers une réplique pour la relier (ou dans le vide pour en créer une). Ctrl+clic : retirer le lien."
                    : "Réponse : glissez ce rond (à gauche ou à droite) vers la réplique qu'elle déclenche, ou dans le vide pour en créer une. " +
                      "Un seul côté est relié à la fois. Ctrl+clic : retirer le lien."
            };
            port.AddToClassList("forge-dialogue-node__port");
            port.AddToClassList(className);
            port.EnableInClassList("forge-dialogue-node__port--idle", otherSide);
            port.style.borderTopColor = port.style.borderBottomColor = port.style.borderLeftColor = port.style.borderRightColor = color;
            port.style.backgroundColor = linked ? color : new Color(color.r, color.g, color.b, 0.12f);
            port.RegisterCallback<PointerDownEvent>(evt => graph.OnPortDown(this, choice, left, evt));
            outputs.Add(port);
            port.userData = (choice, left);
            return port;
        }

        public void MoveTo(Vector2 position)
        {
            Position = position;
            style.left = position.x;
            style.top = position.y;
        }

        public Vector2 PortPoint(int choice, bool left, VisualElement space)
        {
            var port = outputs.FirstOrDefault(p => ((int, bool))p.userData == (choice, choice >= 0 && left));
            if (port == null || float.IsNaN(port.layout.width))
                return space.WorldToLocal(new Vector2(worldBound.center.x, worldBound.yMax));
            return port.ChangeCoordinatesTo(space, port.contentRect.center);
        }

        public Vector2 InputPoint(VisualElement space) =>
            float.IsNaN(input.layout.width)
                ? this.ChangeCoordinatesTo(space, new Vector2(DialogueGraphView.NodeWidth / 2f, 0f))
                : input.ChangeCoordinatesTo(space, input.contentRect.center);

        static Label NodeLabel(string text, string className)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(className);
            return label;
        }
    }
}
