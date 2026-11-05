using System;
using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using TheDungeon.Editor.Common.Charts;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Dialogues
{
    /// <summary>
    /// Arbre de la conversation, de haut en bas, à la manière des blueprints : molette pour zoomer, glisser le fond pour se déplacer,
    /// glisser une réplique pour la ranger, glisser un port (rond) vers une réplique pour changer la suite de la conversation.
    /// Lien gris = suite directe, bleu = réponse du joueur, violet = réponse soumise à condition. Les répliques que rien
    /// n'atteint sont encadrées en orange.
    /// </summary>
    public sealed partial class DialogueGraphView : VisualElement
    {
        internal const float NodeWidth = DialogueGraphLayout.NodeWidth;
        const float MinZoom = 0.25f;
        const float MaxZoom = 2f;
        const float GridStep = 24f;

        readonly SerializedObject serialized;
        readonly NpcData npc;
        readonly Action<Action> edit;
        readonly Action<int> select;

        readonly VisualElement viewport;
        readonly VisualElement content;
        readonly VisualElement edgesLayer;
        readonly VisualElement card;
        readonly Label zoomLabel;
        readonly Dictionary<int, DialogueNodeView> views = new();
        readonly Dictionary<int, float> measuredHeights = new();

        Vector2 pan = new(40f, 40f);
        float zoom = 1f;
        bool framed;
        int selectedId = -1;
        IVisualElementScheduledItem pendingCard;

        /// <summary>PNJ dont l'arbre est affiché.</summary>
        internal NpcData Npc => npc;

        /// <summary>Réplique sélectionnée, ou -1.</summary>
        internal int SelectedId => selectedId;

        public DialogueGraphView(SerializedObject serialized, NpcData npc, Action<Action> edit, Action<int> select)
        {
            this.serialized = serialized;
            this.npc = npc;
            this.edit = edit;
            this.select = select;
            AddToClassList("forge-dialogue-graph");

            var toolbar = new VisualElement();
            toolbar.AddToClassList("forge-dialogue-graph__toolbar");
            toolbar.Add(ChartLegend.Build(new[]
            {
                ("Suite directe", ChartColors.Neutral),
                ("Réponse du joueur", ChartColors.Series(0)),
                ("Réponse avec condition", ChartColors.Series(6)),
                ("Réplique inaccessible", ChartColors.Warning)
            }));
            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            toolbar.Add(spacer);
            zoomLabel = ForgeUI.Label("100 %", "forge-dialogue-graph__zoom");
            toolbar.Add(zoomLabel);
            toolbar.Add(ForgeUI.Button("Recentrer", "Affiche tout l'arbre (raccourci : F).", FrameAll));
            toolbar.Add(ForgeUI.Button("Réorganiser", "Range de nouveau toutes les répliques automatiquement, de haut en bas.", () =>
                edit(() => DialogueEditing.ClearPositions(serialized))));
            toolbar.Add(ForgeUI.Button("Exporter en PNG", "Enregistre une image de l'arbre complet, à taille réelle.", AskExportPng));
            Add(toolbar);

            viewport = new VisualElement { focusable = true };
            viewport.AddToClassList("forge-dialogue-graph__viewport");
            viewport.generateVisualContent += DrawGrid;
            content = new VisualElement { pickingMode = PickingMode.Ignore };
            content.AddToClassList("forge-dialogue-graph__content");
            edgesLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            edgesLayer.AddToClassList("forge-dialogue-graph__edges");
            edgesLayer.generateVisualContent += DrawEdges;
            content.Add(edgesLayer);
            viewport.Add(content);
            card = new VisualElement { pickingMode = PickingMode.Ignore };
            card.AddToClassList("forge-dialogue-card");
            card.style.display = DisplayStyle.None;
            viewport.Add(card);
            var hint = ForgeUI.Label("Molette : zoom · Glisser le fond : se déplacer · Glisser un rond : relier · Ctrl+clic sur un lien : le retirer · Clic droit : actions",
                "forge-dialogue-graph__hint");
            hint.pickingMode = PickingMode.Ignore;
            viewport.Add(hint);
            Add(viewport);

            viewport.RegisterCallback<WheelEvent>(OnWheel);
            viewport.RegisterCallback<PointerDownEvent>(OnBackgroundDown);
            viewport.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            viewport.RegisterCallback<PointerUpEvent>(OnPointerUp);
            viewport.RegisterCallback<KeyDownEvent>(OnKeyDown);
            viewport.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (!framed && views.Count > 0 && viewport.layout.width > 10f)
                {
                    framed = true;
                    FrameAll();
                }
            });
        }

        #region Construction

        public void Show(int selected)
        {
            selectedId = selected;
            HideCard();
            foreach (var view in views.Values)
                view.RemoveFromHierarchy();
            views.Clear();

            var reachable = TheDungeon.Dialogue.DialogueLogic.ReachableNodes(npc);
            foreach (var node in npc.Nodes)
            {
                var view = new DialogueNodeView(this, node, !reachable.Contains(node.id));
                views[node.id] = view;
                content.Add(view);
            }
            Layout();
            foreach (var view in views.Values)
                view.RegisterCallback<GeometryChangedEvent>(_ => OnNodeGeometry(view));
            ApplyTransform();
        }

        /// <summary>Hauteur réelle d'une réplique dès qu'elle est connue ; on range de nouveau si l'estimation était fausse.</summary>
        void OnNodeGeometry(DialogueNodeView view)
        {
            var height = view.layout.height;
            if (float.IsNaN(height) || height < 1f)
                return;
            var known = measuredHeights.TryGetValue(view.Id, out var previous);
            measuredHeights[view.Id] = height;
            if (!view.Node.placed && (!known || Mathf.Abs(previous - height) > 2f))
                schedule.Execute(Layout);
            edgesLayer.MarkDirtyRepaint();
        }

        float HeightOf(DialogueNode node) => measuredHeights.TryGetValue(node.id, out var height)
            ? height
            : 70f + Mathf.Ceil(Mathf.Min(140, node.text?.Length ?? 0) / 34f) * 15f + node.choices.Count * 26f;

        /// <summary>Range les répliques (voir <see cref="DialogueGraphLayout.Positions"/>) et redessine les liens.</summary>
        void Layout()
        {
            var positions = DialogueGraphLayout.Positions(npc, HeightOf);
            foreach (var (id, view) in views)
                if (positions.TryGetValue(id, out var position))
                    view.MoveTo(position);
            edgesLayer.MarkDirtyRepaint();
        }

        #endregion

        #region Vue : zoom et déplacement

        void ApplyTransform()
        {
            content.style.translate = new Translate(pan.x, pan.y);
            content.style.scale = new Scale(new Vector3(zoom, zoom, 1f));
            zoomLabel.text = $"{Mathf.RoundToInt(zoom * 100f)} %";
            viewport.MarkDirtyRepaint();
        }

        Rect ContentBounds()
        {
            if (views.Count == 0)
                return new Rect(0, 0, NodeWidth, 100);
            var rects = views.Values.Select(v => new Rect(v.Position, new Vector2(NodeWidth, HeightOf(v.Node)))).ToList();
            var min = new Vector2(rects.Min(r => r.xMin), rects.Min(r => r.yMin));
            var max = new Vector2(rects.Max(r => r.xMax), rects.Max(r => r.yMax));
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        public void FrameAll()
        {
            var size = viewport.layout.size;
            if (float.IsNaN(size.x) || size.x < 10f)
                return;
            var bounds = ContentBounds();
            zoom = Mathf.Clamp(Mathf.Min((size.x - 80f) / bounds.width, (size.y - 80f) / bounds.height), MinZoom, 1f);
            pan = size / 2f - bounds.center * zoom;
            ApplyTransform();
        }

        void OnWheel(WheelEvent evt)
        {
            var mouse = evt.localMousePosition;
            var previous = zoom;
            zoom = Mathf.Clamp(zoom * (evt.delta.y > 0 ? 1f / 1.12f : 1.12f), MinZoom, MaxZoom);
            // On zoome vers la souris : le point sous le curseur reste sous le curseur.
            pan = mouse - (mouse - pan) * (zoom / previous);
            ApplyTransform();
            HideCard();
            evt.StopPropagation();
        }

        Vector2 ToContent(Vector2 viewportPoint) => (viewportPoint - pan) / zoom;

        void DrawGrid(MeshGenerationContext context)
        {
            var rect = viewport.contentRect;
            var painter = context.painter2D;
            var step = GridStep * zoom;
            if (step < 6f)
                return;
            painter.lineWidth = 1f;
            for (var major = 0; major < 2; major++)
            {
                var spacing = major == 1 ? step * 5f : step;
                painter.strokeColor = new Color(1f, 1f, 1f, major == 1 ? 0.05f : 0.025f);
                painter.BeginPath();
                for (var x = Mathf.Repeat(pan.x, spacing); x < rect.width; x += spacing)
                {
                    painter.MoveTo(new Vector2(x, 0f));
                    painter.LineTo(new Vector2(x, rect.height));
                }
                for (var y = Mathf.Repeat(pan.y, spacing); y < rect.height; y += spacing)
                {
                    painter.MoveTo(new Vector2(0f, y));
                    painter.LineTo(new Vector2(rect.width, y));
                }
                painter.Stroke();
            }
        }

        #endregion

        #region Liens

        internal static Color LinkColor(int choice, DialogueNode node) =>
            choice < 0 ? ChartColors.Neutral : node.choices[choice].conditions.Count > 0 ? ChartColors.Series(6) : ChartColors.Series(0);

        void DrawEdges(MeshGenerationContext context)
        {
            var painter = context.painter2D;
            painter.lineCap = LineCap.Round;
            drawnLinks.Clear();
            foreach (var view in views.Values)
            {
                foreach (var (choice, target) in DialogueGraphLayout.Targets(view.Node))
                {
                    if (target < 0 || !views.TryGetValue(target, out var to))
                        continue;
                    var left = choice >= 0 && view.Node.choices[choice].linkFromLeft;
                    var start = view.PortPoint(choice, left, edgesLayer);
                    var end = to.InputPoint(edgesLayer);
                    var hovered = hoveredLink == (view.Id, choice);
                    var curve = DrawLink(painter, start, end, choice < 0 ? 0 : left ? -1 : 1,
                        hovered ? ChartColors.Critical : LinkColor(choice, view.Node), hovered ? 3.5f : 2f);
                    drawnLinks.Add((view.Id, choice, curve.a, curve.b, curve.c, curve.d));
                }
            }
            if (gesture == Gesture.Link && gestureMoved && views.TryGetValue(linkFrom.node, out var origin))
            {
                var start = origin.PortPoint(linkFrom.choice, linkFrom.left, edgesLayer);
                var end = content.ChangeCoordinatesTo(edgesLayer, linkEnd);
                DrawLink(painter, start, end, linkFrom.choice < 0 ? 0 : linkFrom.left ? -1 : 1, ChartColors.Series(0), 2.5f);
            }
        }

        /// <summary>
        /// Courbe d'un port vers le haut d'une réplique. <paramref name="side"/> : 0 = suite (part vers le bas),
        /// 1 = réponse liée par la droite, -1 = réponse liée par la gauche. Renvoie les points de la courbe.
        /// </summary>
        static (Vector2 a, Vector2 b, Vector2 c, Vector2 d) DrawLink(Painter2D painter, Vector2 start, Vector2 end, int side, Color color, float width)
        {
            var reach = Mathf.Clamp(Vector2.Distance(start, end) * 0.45f, 40f, 160f);
            var startTangent = side == 0 ? new Vector2(0f, reach) : new Vector2(side * reach, 0f);
            var control1 = start + startTangent;
            var control2 = end - new Vector2(0f, reach);
            painter.strokeColor = color;
            painter.lineWidth = width;
            painter.BeginPath();
            painter.MoveTo(start);
            painter.BezierCurveTo(control1, control2, end);
            painter.Stroke();
            // Pointe de flèche, vers le bas, sur le haut de la réplique.
            painter.fillColor = color;
            painter.BeginPath();
            painter.MoveTo(end);
            painter.LineTo(end + new Vector2(-5f, -9f));
            painter.LineTo(end + new Vector2(5f, -9f));
            painter.ClosePath();
            painter.Fill();
            return (start, control1, control2, end);
        }

        #endregion

        #region Carte de survol

        internal void ScheduleCard(DialogueNodeView view)
        {
            pendingCard?.Pause();
            if (gesture != Gesture.None)
                return;
            pendingCard = schedule.Execute(() => ShowCard(view)).StartingIn(380);
        }

        /// <summary>Déplace une réplique comme à la souris (outil de capture).</summary>
        internal void MoveNode(int id, Vector2 delta)
        {
            if (!views.TryGetValue(id, out var view))
                return;
            view.MoveTo(view.Position + delta);
            CommitPositions();
        }

        /// <summary>Affiche tout de suite la carte d'une réplique (outil de capture).</summary>
        internal void PreviewCard(int id)
        {
            if (views.TryGetValue(id, out var view))
                ShowCard(view);
        }

        internal void HideCard()
        {
            pendingCard?.Pause();
            card.style.display = DisplayStyle.None;
        }

        void ShowCard(DialogueNodeView view)
        {
            if (gesture != Gesture.None || view.panel == null)
                return;
            DialogueNodeCard.Fill(card, view, npc, SpeakerColor(view.Node.speaker));

            // À droite de la réplique, ou à gauche s'il n'y a pas la place ; toujours dans la zone visible.
            var bounds = view.ChangeCoordinatesTo(viewport, new Rect(Vector2.zero, view.layout.size));
            const float width = 300f;
            var left = bounds.xMax + 12f;
            if (left + width > viewport.layout.width - 8f)
                left = Mathf.Max(8f, bounds.xMin - width - 12f);
            card.style.left = left;
            card.style.top = Mathf.Clamp(bounds.yMin, 8f, Mathf.Max(8f, viewport.layout.height - 260f));
            card.style.display = DisplayStyle.Flex;
        }

        internal Color SpeakerColor(DialogueSpeaker speaker) => speaker switch
        {
            DialogueSpeaker.Player => ChartColors.Series(2),
            DialogueSpeaker.Narrator => ChartColors.Neutral,
            _ => npc.Color
        };

        #endregion

        #region Export PNG

        void AskExportPng()
        {
            var path = EditorUtility.SaveFilePanel("Exporter l'arbre en PNG", string.Empty, $"{npc.name}_Arbre.png", "png");
            if (!string.IsNullOrEmpty(path))
                ExportPng(path, null);
        }

        /// <summary>
        /// Image de l'arbre complet à taille réelle : la vue est déplacée morceau par morceau (zoom 100 %),
        /// chaque morceau est capturé puis recollé dans une seule image.
        /// </summary>
        public void ExportPng(string path, Action<bool> done)
        {
            HideCard();
            // L'arbre doit être à l'écran : on y fait défiler la fiche avant de commencer.
            var scroll = GetFirstAncestorOfType<ScrollView>();
            scroll?.ScrollTo(viewport);
            schedule.Execute(() => DialogueGraphExport.Run(this, ContentBounds(), path, scroll, done)).StartingIn(100);
        }

        /// <summary>Zone de la vue (dans son repère) où est dessiné l'arbre.</summary>
        internal VisualElement Viewport => viewport;

        /// <summary>Cadrage actuel : décalage et zoom.</summary>
        internal (Vector2 pan, float zoom) View
        {
            get => (pan, zoom);
            set
            {
                (pan, zoom) = value;
                ApplyTransform();
            }
        }

        #endregion
    }
}
