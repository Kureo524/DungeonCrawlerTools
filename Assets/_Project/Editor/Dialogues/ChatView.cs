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
    /// Conversation présentée comme une messagerie : bulles du PNJ à gauche (avec portrait), du joueur à droite,
    /// narration au centre. Les réponses du joueur apparaissent en pastilles sous la réplique, avec leurs conditions et effets.
    /// Ordre de lecture : on suit la conversation depuis le début ; les répliques que rien n'atteint sont listées à la fin.
    /// </summary>
    public sealed class ChatView : VisualElement
    {
        readonly ScrollView thread;
        readonly Action<int> select;
        readonly Dictionary<int, VisualElement> bubbles = new();

        public ChatView(Action<int> select)
        {
            this.select = select;
            AddToClassList("forge-chat");
            thread = new ScrollView();
            thread.AddToClassList("forge-chat__thread");
            Add(thread);
        }

        public void Show(NpcData npc, int selectedId)
        {
            var scroll = thread.scrollOffset;
            thread.Clear();
            bubbles.Clear();

            if (npc.Nodes.Count == 0)
            {
                thread.Add(ForgeUI.EmptyState("La conversation est vide.\nÉcrivez la première réplique dans la barre du bas, puis appuyez sur Entrée."));
                return;
            }

            var order = ReadingOrder(npc, out var orphans);
            var shown = new HashSet<int>();
            for (var i = 0; i < order.Count; i++)
            {
                var node = order[i];
                if (orphans.Contains(node.id) && (i == 0 || !orphans.Contains(order[i - 1].id)))
                    thread.Add(System("Répliques que rien ne mène encore ici"));
                var nextShown = i + 1 < order.Count ? order[i + 1].id : int.MinValue;
                thread.Add(BuildNode(npc, node, node.id == selectedId, nextShown, shown));
                shown.Add(node.id);
            }

            thread.schedule.Execute(() => thread.scrollOffset = scroll);
        }

        public void ScrollTo(int id)
        {
            if (bubbles.TryGetValue(id, out var bubble))
                thread.schedule.Execute(() => thread.ScrollTo(bubble));
        }

        /// <summary>Parcours depuis le début (suite d'abord, puis chaque réponse), puis les répliques isolées.</summary>
        static List<DialogueNode> ReadingOrder(NpcData npc, out HashSet<int> orphans)
        {
            var order = new List<DialogueNode>();
            var visited = new HashSet<int>();
            var stack = new Stack<int>();
            var roots = new List<int> { npc.StartNode };
            roots.AddRange(npc.Entries.Select(e => e.node));
            foreach (var root in Enumerable.Reverse(roots))
                stack.Push(root);

            while (stack.Count > 0)
            {
                var id = stack.Pop();
                if (id < 0 || !visited.Add(id) || npc.FindNode(id) is not { } node)
                    continue;
                order.Add(node);
                var targets = new List<int> { node.next };
                targets.AddRange(node.choices.Select(c => c.target));
                foreach (var target in Enumerable.Reverse(targets))
                    stack.Push(target);
            }

            orphans = new HashSet<int>();
            foreach (var node in npc.Nodes.Where(n => !visited.Contains(n.id)))
            {
                order.Add(node);
                orphans.Add(node.id);
            }
            return order;
        }

        VisualElement BuildNode(NpcData npc, DialogueNode node, bool selected, int nextShown, HashSet<int> alreadyShown)
        {
            var block = new VisualElement();
            block.AddToClassList("forge-chat__block");

            var line = new VisualElement();
            line.AddToClassList("forge-chat__line");
            line.AddToClassList(node.speaker switch
            {
                DialogueSpeaker.Player => "forge-chat__line--player",
                DialogueSpeaker.Narrator => "forge-chat__line--narrator",
                _ => "forge-chat__line--npc"
            });

            if (node.speaker == DialogueSpeaker.Npc)
            {
                var avatar = new Image { sprite = npc.Portrait, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                avatar.AddToClassList("forge-chat__avatar");
                avatar.style.borderTopColor = avatar.style.borderBottomColor = avatar.style.borderLeftColor = avatar.style.borderRightColor = npc.Color;
                line.Add(avatar);
            }

            var bubble = new VisualElement { tooltip = $"{DialogueText.Speaker(node.speaker, npc)} · réplique #{node.id}\n\n{node.text}\n\nCliquez pour modifier." };
            bubble.AddToClassList("forge-chat__bubble");
            bubble.EnableInClassList("forge-chat__bubble--selected", selected);
            if (node.speaker == DialogueSpeaker.Npc)
                bubble.style.backgroundColor = new Color(npc.Color.r, npc.Color.g, npc.Color.b, 0.28f);

            var meta = new VisualElement();
            meta.AddToClassList("forge-chat__meta");
            meta.Add(ForgeUI.Label(DialogueText.Speaker(node.speaker, npc), "forge-chat__speaker"));
            meta.Add(ForgeUI.Label($"#{node.id}", "forge-chat__id"));
            if (node.id == npc.StartNode)
                meta.Add(Chip("DÉBUT", "forge-chip--start"));
            if (npc.Entries.Any(e => e.node == node.id))
                meta.Add(Chip("DÉBUT ALTERNATIF", "forge-chip--start"));
            bubble.Add(meta);

            var text = new Label(string.IsNullOrWhiteSpace(node.text) ? "(réplique vide)" : node.text);
            text.AddToClassList("forge-chat__text");
            text.EnableInClassList("forge-chat__text--empty", string.IsNullOrWhiteSpace(node.text));
            bubble.Add(text);

            if (node.consequences.Count > 0)
            {
                var chips = new VisualElement();
                chips.AddToClassList("forge-chat__chips");
                foreach (var consequence in node.consequences)
                    chips.Add(Chip(DialogueText.Describe(consequence), "forge-chip--effect"));
                bubble.Add(chips);
            }

            bubble.RegisterCallback<ClickEvent>(_ => select(node.id));
            bubbles[node.id] = bubble;
            line.Add(bubble);
            block.Add(line);

            if (node.choices.Count > 0)
            {
                var replies = new VisualElement();
                replies.AddToClassList("forge-chat__replies");
                foreach (var choice in node.choices)
                    replies.Add(Reply(choice, alreadyShown));
                block.Add(replies);
            }
            else if (node.next < 0)
            {
                block.Add(System("Fin de la conversation"));
            }
            else if (node.next != nextShown)
            {
                var jump = new Label($"↪ suite : réplique #{node.next}") { tooltip = "Cliquez pour aller à la suite." };
                jump.AddToClassList("forge-chat__jump");
                var target = node.next;
                jump.RegisterCallback<ClickEvent>(_ => GoTo(target));
                block.Add(jump);
            }
            return block;
        }

        VisualElement Reply(DialogueChoice choice, HashSet<int> alreadyShown)
        {
            var pill = new VisualElement
            {
                tooltip = $"Réponse du joueur : « {choice.text} »\n" +
                          (choice.target < 0 ? "Termine la conversation." : $"Mène à la réplique #{choice.target}. Cliquez pour y aller.")
            };
            pill.AddToClassList("forge-chat__reply");
            var text = new Label(string.IsNullOrWhiteSpace(choice.text) ? "(réponse vide)" : choice.text);
            text.AddToClassList("forge-chat__reply-text");
            pill.Add(text);

            var footer = new VisualElement();
            footer.AddToClassList("forge-chat__chips");
            foreach (var condition in choice.conditions)
                footer.Add(Chip(DialogueText.Describe(condition), "forge-chip--condition"));
            foreach (var consequence in choice.consequences)
                footer.Add(Chip(DialogueText.Describe(consequence), "forge-chip--effect"));
            var loop = choice.target >= 0 && alreadyShown.Contains(choice.target);
            footer.Add(Chip(choice.target < 0 ? "→ Fin" : loop ? $"↺ #{choice.target}" : $"→ #{choice.target}", "forge-chip--target"));
            pill.Add(footer);

            var target = choice.target;
            pill.RegisterCallback<ClickEvent>(_ => GoTo(target));
            return pill;
        }

        void GoTo(int id)
        {
            if (id < 0)
                return;
            select(id);
            ScrollTo(id);
        }

        static VisualElement System(string text)
        {
            var label = new Label(text);
            label.AddToClassList("forge-chat__system");
            return label;
        }

        static Label Chip(string text, string className)
        {
            var chip = new Label(text);
            chip.AddToClassList("forge-chip");
            chip.AddToClassList(className);
            return chip;
        }
    }
}
