using System;
using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using TheDungeon.Editor.Common.Charts;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Dialogues
{
    /// <summary>
    /// Arbre de la conversation : une colonne par profondeur depuis le début. Lien gris = suite directe,
    /// lien bleu = réponse du joueur, lien violet = réponse soumise à condition. Les répliques que rien n'atteint
    /// sont rangées dans une dernière colonne, encadrées en orange.
    /// </summary>
    public sealed class DialogueGraphView : VisualElement
    {
        const float BoxWidth = 180f;
        const float BoxHeight = 62f;
        const float ColumnGap = 90f;
        const float RowGap = 22f;
        const float Margin = 16f;

        readonly Action<int> select;
        readonly VisualElement canvas;
        readonly VisualElement edgesLayer;
        readonly Dictionary<int, Rect> boxes = new();
        readonly List<(int from, int to, string label, bool conditional)> edges = new();

        public DialogueGraphView(Action<int> select)
        {
            this.select = select;
            AddToClassList("forge-dialogue-graph");
            Add(ChartLegend.Build(new[]
            {
                ("Suite directe", ChartColors.Neutral),
                ("Réponse du joueur", ChartColors.Series(0)),
                ("Réponse avec condition", ChartColors.Series(6)),
                ("Réplique inaccessible", ChartColors.Warning)
            }));
            var scroll = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            scroll.AddToClassList("forge-dialogue-graph__scroll");
            canvas = new VisualElement();
            canvas.AddToClassList("forge-dialogue-graph__canvas");
            edgesLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            edgesLayer.AddToClassList("forge-dialogue-graph__edges");
            edgesLayer.generateVisualContent += DrawEdges;
            canvas.Add(edgesLayer);
            scroll.Add(canvas);
            Add(scroll);
        }

        public void Show(NpcData npc, int selectedId)
        {
            foreach (var child in canvas.Children().Where(c => c != edgesLayer).ToList())
                child.RemoveFromHierarchy();
            boxes.Clear();
            edges.Clear();

            var depth = Depths(npc);
            var columns = new Dictionary<int, List<DialogueNode>>();
            var orphanColumn = depth.Count == 0 ? 0 : depth.Values.Max() + 1;
            foreach (var node in npc.Nodes)
            {
                var column = depth.TryGetValue(node.id, out var d) ? d : orphanColumn;
                if (!columns.TryGetValue(column, out var list))
                    columns[column] = list = new List<DialogueNode>();
                list.Add(node);
            }

            var height = 0f;
            foreach (var (column, nodes) in columns)
            {
                for (var row = 0; row < nodes.Count; row++)
                {
                    var rect = new Rect(Margin + column * (BoxWidth + ColumnGap), Margin + row * (BoxHeight + RowGap), BoxWidth, BoxHeight);
                    boxes[nodes[row].id] = rect;
                    height = Mathf.Max(height, rect.yMax);
                    canvas.Add(Box(npc, nodes[row], rect, nodes[row].id == selectedId, !depth.ContainsKey(nodes[row].id)));
                }
            }

            foreach (var node in npc.Nodes)
            {
                if (node.choices.Count == 0 && node.next >= 0)
                    edges.Add((node.id, node.next, null, false));
                foreach (var choice in node.choices.Where(c => c.target >= 0))
                    edges.Add((node.id, choice.target, DialogueEditing.Excerpt(choice.text, 18), choice.conditions.Count > 0));
            }
            foreach (var (from, to, label, _) in edges)
            {
                if (label == null || !boxes.TryGetValue(from, out var a) || !boxes.TryGetValue(to, out var b))
                    continue;
                var tag = new Label(label) { pickingMode = PickingMode.Ignore };
                tag.AddToClassList("forge-dialogue-graph__edge-label");
                var forward = b.x > a.x;
                tag.style.left = forward ? (a.xMax + b.x) / 2f - 40f : a.center.x - 40f;
                tag.style.top = forward ? (a.center.y + b.center.y) / 2f - 16f : Mathf.Max(a.yMax, b.yMax) + 2f;
                canvas.Add(tag);
            }

            var width = columns.Count == 0 ? 200f : Margin * 2 + (columns.Keys.Max() + 1) * (BoxWidth + ColumnGap);
            canvas.style.width = width;
            canvas.style.height = height + Margin * 2 + 30f;
            edgesLayer.MarkDirtyRepaint();
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
                foreach (var target in new[] { node.next }.Concat(node.choices.Select(c => c.target)))
                {
                    if (target < 0 || depth.ContainsKey(target) || npc.FindNode(target) == null)
                        continue;
                    depth[target] = depth[node.id] + 1;
                    queue.Enqueue(target);
                }
            }
            return depth;
        }

        VisualElement Box(NpcData npc, DialogueNode node, Rect rect, bool selected, bool orphan)
        {
            var box = new VisualElement { tooltip = $"#{node.id} · {DialogueText.Speaker(node.speaker, npc)}\n\n{node.text}\n\nCliquez pour modifier." };
            box.AddToClassList("forge-dialogue-graph__box");
            box.EnableInClassList("forge-dialogue-graph__box--selected", selected);
            box.EnableInClassList("forge-dialogue-graph__box--orphan", orphan);
            box.style.left = rect.x;
            box.style.top = rect.y;
            box.style.width = rect.width;
            box.style.height = rect.height;

            var stripe = new VisualElement { pickingMode = PickingMode.Ignore };
            stripe.AddToClassList("forge-dialogue-graph__stripe");
            stripe.style.backgroundColor = node.speaker switch
            {
                DialogueSpeaker.Player => ChartColors.Series(2),
                DialogueSpeaker.Narrator => ChartColors.Neutral,
                _ => npc.Color
            };
            box.Add(stripe);

            var body = new VisualElement { pickingMode = PickingMode.Ignore };
            body.style.flexGrow = 1;
            var header = new VisualElement { pickingMode = PickingMode.Ignore };
            header.AddToClassList("forge-row");
            header.Add(new Label($"#{node.id} · {DialogueText.Speaker(node.speaker, npc)}") { pickingMode = PickingMode.Ignore });
            if (node.id == npc.StartNode)
                header.Add(Tag("DÉBUT", ChartColors.Good));
            if (node.choices.Count == 0 && node.next < 0)
                header.Add(Tag("FIN", ChartColors.Neutral));
            body.Add(header);
            var text = new Label(DialogueEditing.Excerpt(node.text, 60)) { pickingMode = PickingMode.Ignore };
            text.AddToClassList("forge-dialogue-graph__text");
            body.Add(text);
            box.Add(body);

            box.RegisterCallback<ClickEvent>(_ => select(node.id));
            return box;
        }

        static Label Tag(string text, Color color)
        {
            var tag = new Label(text) { pickingMode = PickingMode.Ignore };
            tag.AddToClassList("forge-floor__tag");
            tag.style.position = Position.Relative;
            tag.style.marginLeft = 4;
            tag.style.backgroundColor = color;
            return tag;
        }

        void DrawEdges(MeshGenerationContext context)
        {
            var painter = context.painter2D;
            painter.lineCap = LineCap.Round;
            foreach (var (from, to, label, conditional) in edges)
            {
                if (!boxes.TryGetValue(from, out var a) || !boxes.TryGetValue(to, out var b))
                    continue;
                var color = label == null ? ChartColors.Neutral : conditional ? ChartColors.Series(6) : ChartColors.Series(0);
                painter.strokeColor = color;
                painter.lineWidth = 2f;
                painter.BeginPath();

                Vector2 end;
                Vector2 direction;
                if (b.x > a.x)
                {
                    // Lien vers l'avant : courbe de la droite de la source vers la gauche de la cible.
                    var start = new Vector2(a.xMax, a.center.y);
                    end = new Vector2(b.xMin, b.center.y);
                    painter.MoveTo(start);
                    painter.BezierCurveTo(start + new Vector2(ColumnGap * 0.5f, 0f), end - new Vector2(ColumnGap * 0.5f, 0f), end);
                    direction = Vector2.right;
                }
                else
                {
                    // Retour en arrière (boucle) : arc qui passe sous les boîtes.
                    var start = new Vector2(a.center.x, a.yMax);
                    end = new Vector2(b.center.x + 12f, b.yMax);
                    var low = Mathf.Max(a.yMax, b.yMax) + 34f;
                    painter.MoveTo(start);
                    painter.BezierCurveTo(new Vector2(start.x, low), new Vector2(end.x, low), end);
                    // En coordonnées d'interface, y croît vers le bas : la flèche remonte vers la boîte.
                    direction = new Vector2(0f, -1f);
                }
                painter.Stroke();

                // Pointe de flèche.
                painter.fillColor = color;
                var normal = new Vector2(-direction.y, direction.x);
                painter.BeginPath();
                painter.MoveTo(end);
                painter.LineTo(end - direction * 8f + normal * 4.5f);
                painter.LineTo(end - direction * 8f - normal * 4.5f);
                painter.ClosePath();
                painter.Fill();
            }
        }
    }
}
