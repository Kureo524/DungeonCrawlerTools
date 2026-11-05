using System;
using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using UnityEngine;

namespace TheDungeon.Editor.Dialogues
{
    /// <summary>Rangement de l'arbre d'une conversation, de haut en bas : positions des répliques dans le repère de l'arbre.</summary>
    public static class DialogueGraphLayout
    {
        public const float NodeWidth = 236f;
        public const float RowGap = 74f;
        public const float ColumnGap = 36f;

        /// <summary>
        /// Positions : celles choisies à la main, sinon un rangement par profondeur depuis le début (une ligne par profondeur,
        /// chaque réplique sous celles qui y mènent). Une nouvelle réplique se place sous la réplique qui y mène.
        /// </summary>
        public static Dictionary<int, Vector2> Positions(NpcData npc, Func<DialogueNode, float> heightOf)
        {
            var positions = AutoLayout(npc, heightOf);
            var anyPlaced = npc.Nodes.Any(n => n.placed);
            if (anyPlaced)
            {
                var placedBottom = npc.Nodes.Where(n => n.placed).Select(n => n.graphPosition.y + heightOf(n)).DefaultIfEmpty(0f).Max();
                var fixedPositions = new Dictionary<int, Vector2>();
                foreach (var node in npc.Nodes.Where(n => n.placed))
                    fixedPositions[node.id] = node.graphPosition;
                // Les répliques non rangées suivent leur parent déjà placé, sinon passent sous l'arbre.
                var pending = npc.Nodes.Where(n => !n.placed).ToList();
                for (var pass = 0; pass < 4 && pending.Count > 0; pass++)
                {
                    foreach (var node in pending.ToList())
                    {
                        var parent = Parents(npc, node.id).FirstOrDefault(p => fixedPositions.ContainsKey(p.node.id));
                        if (parent.node == null)
                            continue;
                        var outputs = Math.Max(1, parent.node.choices.Count);
                        var slot = parent.choice < 0 ? 0 : parent.choice;
                        var origin = fixedPositions[parent.node.id];
                        fixedPositions[node.id] = new Vector2(origin.x + (slot - (outputs - 1) / 2f) * (NodeWidth + ColumnGap),
                            origin.y + heightOf(parent.node) + RowGap);
                        pending.Remove(node);
                    }
                }
                var offset = 0f;
                foreach (var node in pending)
                {
                    fixedPositions[node.id] = new Vector2(offset, placedBottom + RowGap);
                    offset += NodeWidth + ColumnGap;
                }
                positions = fixedPositions;
            }
            return positions;
        }

        /// <summary>Rangement par profondeur depuis le début : une ligne par profondeur, chaque réplique sous celles qui y mènent.</summary>
        static Dictionary<int, Vector2> AutoLayout(NpcData npc, Func<DialogueNode, float> heightOf)
        {
            var depth = Depths(npc);
            var orphanRow = depth.Count == 0 ? 0 : depth.Values.Max() + 1;
            var rows = npc.Nodes.GroupBy(n => depth.TryGetValue(n.id, out var d) ? d : orphanRow).OrderBy(g => g.Key).ToList();
            var positions = new Dictionary<int, Vector2>();
            var y = 0f;
            foreach (var row in rows)
            {
                // Chaque réplique vise le centre de celles qui y mènent ; on évite ensuite les chevauchements.
                var wanted = row.Select(node =>
                {
                    var parents = Parents(npc, node.id).Where(p => positions.ContainsKey(p.node.id)).ToList();
                    var x = parents.Count == 0
                        ? 0f
                        : parents.Average(p =>
                        {
                            var outputs = Math.Max(1, p.node.choices.Count);
                            var slot = p.choice < 0 ? 0 : p.choice;
                            return positions[p.node.id].x + (slot - (outputs - 1) / 2f) * (NodeWidth + ColumnGap);
                        });
                    return (node, x);
                }).OrderBy(entry => entry.x).ToList();
                var placedX = new List<float>();
                foreach (var (_, x) in wanted)
                    placedX.Add(placedX.Count == 0 ? x : Mathf.Max(x, placedX[^1] + NodeWidth + ColumnGap));
                var shift = wanted.Average(w => w.x) - placedX.Average();
                for (var i = 0; i < wanted.Count; i++)
                    positions[wanted[i].node.id] = new Vector2(placedX[i] + shift, y);
                y += row.Max(heightOf) + RowGap;
            }
            return positions;
        }

        /// <summary>Profondeur de chaque réplique atteignable (parcours en largeur depuis les débuts).</summary>
        static Dictionary<int, int> Depths(NpcData npc)
        {
            var depth = new Dictionary<int, int>();
            var queue = new Queue<int>();
            foreach (var root in new[] { npc.StartNode }.Concat(npc.Entries.Select(e => e.node)))
            {
                if (root < 0 || depth.ContainsKey(root) || npc.FindNode(root) == null)
                    continue;
                depth[root] = 0;
                queue.Enqueue(root);
            }
            while (queue.Count > 0)
            {
                var node = npc.FindNode(queue.Dequeue());
                foreach (var target in Targets(node).Select(t => t.target))
                {
                    if (target < 0 || depth.ContainsKey(target) || npc.FindNode(target) == null)
                        continue;
                    depth[target] = depth[node.id] + 1;
                    queue.Enqueue(target);
                }
            }
            return depth;
        }

        /// <summary>Liens sortants : la suite directe (choix -1) quand il n'y a pas de réponse, sinon chaque réponse.</summary>
        public static IEnumerable<(int choice, int target)> Targets(DialogueNode node)
        {
            if (node.choices.Count == 0)
            {
                yield return (-1, node.next);
                yield break;
            }
            for (var i = 0; i < node.choices.Count; i++)
                yield return (i, node.choices[i].target);
        }

        static IEnumerable<(DialogueNode node, int choice)> Parents(NpcData npc, int id) =>
            npc.Nodes.SelectMany(n => Targets(n).Where(t => t.target == id).Select(t => (n, t.choice)));
    }
}
