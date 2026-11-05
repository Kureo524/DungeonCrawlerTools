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
    public sealed class DialogueGraphView : VisualElement
    {
        internal const float NodeWidth = DialogueGraphLayout.NodeWidth;
        const float MinZoom = 0.25f;
        const float MaxZoom = 2f;
        const float GridStep = 24f;
        const float DragThreshold = 4f;

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

        // Déplacement du fond, d'une réplique ou tracé d'un lien en cours.
        enum Gesture { None, Pan, MoveNode, Link }
        Gesture gesture;
        int gesturePointer = -1;
        int gestureButton;
        Vector2 gestureStart;
        Vector2 gestureOrigin;
        bool gestureMoved;
        DialogueNodeView gestureNode;
        (int node, int choice, bool left) linkFrom;
        Vector2 linkEnd;
        // Liens dessinés (pour Ctrl+clic) et lien survolé avec Ctrl enfoncé.
        readonly List<(int from, int choice, Vector2 a, Vector2 b, Vector2 c, Vector2 d)> drawnLinks = new();
        (int from, int choice) hoveredLink = (-1, -1);

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

        #region Souris

        /// <summary>Position du pointeur dans la vue (quel que soit l'élément qui a reçu l'évènement).</summary>
        Vector2 Local(IPointerEvent evt) => viewport.WorldToLocal(evt.position);

        void OnBackgroundDown(PointerDownEvent evt)
        {
            viewport.Focus();
            HideCard();
            var point = Local(evt);
            // Ctrl+clic sur un lien : on le retire, les répliques restent.
            if (evt.button == 0 && evt.actionKey && TryRemoveLinkAt(point))
            {
                evt.StopPropagation();
                return;
            }
            if (evt.button is 0 or 1 or 2)
                BeginGesture(Gesture.Pan, evt.pointerId, evt.button, point, null);
        }

        bool TryRemoveLinkAt(Vector2 viewportPoint)
        {
            var (from, choice) = LinkAt(viewportPoint);
            if (from < 0)
                return false;
            hoveredLink = (-1, -1);
            edit(() => DialogueEditing.SetLink(serialized, from, choice, DialogueEditing.End));
            return true;
        }

        /// <summary>Lien sous le pointeur (à quelques pixels près), ou (-1, -1).</summary>
        (int from, int choice) LinkAt(Vector2 viewportPoint)
        {
            var point = content.ChangeCoordinatesTo(edgesLayer, ToContent(viewportPoint));
            var tolerance = 7f / zoom;
            foreach (var (from, choice, a, b, c, d) in drawnLinks)
            {
                var previous = a;
                for (var i = 1; i <= 32; i++)
                {
                    var t = i / 32f;
                    var u = 1f - t;
                    var current = u * u * u * a + 3f * u * u * t * b + 3f * u * t * t * c + t * t * t * d;
                    if (DistanceToSegment(point, previous, current) <= tolerance)
                        return (from, choice);
                    previous = current;
                }
            }
            return (-1, -1);
        }

        static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var t = ab.sqrMagnitude < 0.0001f ? 0f : Mathf.Clamp01(Vector2.Dot(point - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(point, a + ab * t);
        }

        void BeginGesture(Gesture kind, int pointerId, int button, Vector2 viewportPoint, DialogueNodeView node)
        {
            gesture = kind;
            gesturePointer = pointerId;
            gestureButton = button;
            gestureStart = viewportPoint;
            gestureMoved = false;
            gestureNode = node;
            gestureOrigin = kind == Gesture.Pan ? pan : node?.Position ?? Vector2.zero;
            viewport.CapturePointer(pointerId);
        }

        internal void OnNodeDown(DialogueNodeView view, PointerDownEvent evt)
        {
            viewport.Focus();
            HideCard();
            var point = Local(evt);
            if (evt.button == 0 && evt.actionKey && TryRemoveLinkAt(point))
            {
                evt.StopPropagation();
                return;
            }
            if (evt.button == 1)
            {
                ShowNodeMenu(view, point);
            }
            else if (evt.button == 2)
            {
                BeginGesture(Gesture.Pan, evt.pointerId, evt.button, point, null);
            }
            else if (evt.button == 0)
            {
                BeginGesture(Gesture.MoveNode, evt.pointerId, 0, point, view);
            }
            evt.StopPropagation();
        }

        internal void OnPortDown(DialogueNodeView view, int choice, bool left, PointerDownEvent evt)
        {
            viewport.Focus();
            HideCard();
            var point = Local(evt);
            if (evt.button == 1 || (evt.button == 0 && (evt.altKey || evt.actionKey)))
            {
                // Alt+clic, Ctrl+clic ou clic droit sur un rond : retirer le lien.
                ShowPortMenu(view, choice, point, evt.altKey || evt.actionKey);
            }
            else if (evt.button == 0)
            {
                BeginGesture(Gesture.Link, evt.pointerId, 0, point, view);
                linkFrom = (view.Id, choice, left);
                linkEnd = ToContent(point);
            }
            evt.StopPropagation();
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (gesture == Gesture.None)
            {
                // Ctrl enfoncé : le lien survolé s'allume en rouge, prêt à être retiré.
                var hovered = evt.actionKey ? LinkAt(Local(evt)) : (-1, -1);
                if (hovered != hoveredLink)
                {
                    hoveredLink = hovered;
                    edgesLayer.MarkDirtyRepaint();
                }
                return;
            }
            if (evt.pointerId != gesturePointer)
                return;
            var point = Local(evt);
            var delta = point - gestureStart;
            if (!gestureMoved && delta.magnitude < DragThreshold)
                return;
            gestureMoved = true;
            switch (gesture)
            {
                case Gesture.Pan:
                    pan = gestureOrigin + delta;
                    ApplyTransform();
                    break;
                case Gesture.MoveNode:
                    gestureNode.MoveTo(gestureOrigin + delta / zoom);
                    edgesLayer.MarkDirtyRepaint();
                    break;
                case Gesture.Link:
                    linkEnd = ToContent(point);
                    foreach (var view in views.Values)
                        view.EnableInClassList("forge-dialogue-node--drop", view.worldBound.Contains(viewport.LocalToWorld(point)) && view.Id != linkFrom.node);
                    edgesLayer.MarkDirtyRepaint();
                    break;
            }
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (gesture == Gesture.None || evt.pointerId != gesturePointer)
                return;
            var point = Local(evt);
            var kind = gesture;
            var node = gestureNode;
            var moved = gestureMoved;
            var button = gestureButton;
            gesture = Gesture.None;
            gestureNode = null;
            if (viewport.HasPointerCapture(evt.pointerId))
                viewport.ReleasePointer(evt.pointerId);

            switch (kind)
            {
                case Gesture.Pan when !moved && button == 1:
                    ShowBackgroundMenu(point);
                    break;
                case Gesture.MoveNode when moved:
                    CommitPositions();
                    break;
                case Gesture.MoveNode:
                    select(node.Id);
                    break;
                case Gesture.Link:
                    foreach (var view in views.Values)
                        view.EnableInClassList("forge-dialogue-node--drop", false);
                    edgesLayer.MarkDirtyRepaint();
                    if (moved)
                        FinishLink(point);
                    break;
            }
        }

        /// <summary>Après un déplacement, toutes les positions deviennent fixes : l'arbre reste tel qu'on l'a rangé.</summary>
        void CommitPositions()
        {
            var positions = views.ToDictionary(v => v.Key, v => v.Value.Position);
            edit(() =>
            {
                foreach (var (id, position) in positions)
                    DialogueEditing.SetPosition(serialized, id, new Vector2(Mathf.Round(position.x), Mathf.Round(position.y)));
            });
        }

        void FinishLink(Vector2 viewportPoint)
        {
            var world = viewport.LocalToWorld(viewportPoint);
            var target = views.Values.FirstOrDefault(v => v.worldBound.Contains(world));
            var (from, choice, left) = linkFrom;
            if (target != null)
            {
                if (target.Id == from)
                {
                    ForgeLists.Notify("Une réplique ne peut pas mener à elle-même.");
                    return;
                }
                edit(() => DialogueEditing.SetLink(serialized, from, choice, target.Id, choice >= 0 ? left : null));
                return;
            }
            // Lâché dans le vide : on crée la réplique suivante à cet endroit.
            var at = ToContent(viewportPoint) - new Vector2(NodeWidth / 2f, 12f);
            var menu = ForgeDropdowns.Menu();
            foreach (var (speaker, label) in new[]
                     {
                         (DialogueSpeaker.Npc, $"Nouvelle réplique de {DialogueText.Speaker(DialogueSpeaker.Npc, npc)}"),
                         (DialogueSpeaker.Narrator, "Nouvelle narration"),
                         (DialogueSpeaker.Player, "Nouvelle réplique du joueur")
                     })
                menu.AddItem(label, false, () => CreateLinked(from, choice, speaker, at, left));
            menu.DropDown(new Rect(viewport.LocalToWorld(viewportPoint), Vector2.zero), viewport, DropdownMenuSizeMode.Content);
        }

        void CreateLinked(int from, int choice, DialogueSpeaker speaker, Vector2 at, bool left = false)
        {
            if (npc.Nodes.Count >= ForgeDesignLimits.Instance.maxDialogueNodes)
            {
                ForgeLists.Notify($"Maximum atteint : {ForgeDesignLimits.Instance.maxDialogueNodes} répliques par personnage.");
                return;
            }
            var created = -1;
            var anyPlaced = npc.Nodes.Any(n => n.placed);
            edit(() =>
            {
                created = DialogueEditing.AddNode(serialized, speaker, "…");
                if (from >= 0)
                    DialogueEditing.SetLink(serialized, from, choice, created, choice >= 0 ? left : null);
                if (anyPlaced || from < 0)
                    DialogueEditing.SetPosition(serialized, created, at);
            });
            select(created);
        }

        void OnKeyDown(KeyDownEvent evt)
        {
            // Annuler / refaire quand l'arbre a le focus (le raccourci de l'éditeur ne lui parvient pas toujours).
            if (evt.actionKey && evt.keyCode is KeyCode.Z or KeyCode.Y)
            {
                if (evt.keyCode == KeyCode.Y || evt.shiftKey)
                    Undo.PerformRedo();
                else
                    Undo.PerformUndo();
                evt.StopPropagation();
                return;
            }
            switch (evt.keyCode)
            {
                case KeyCode.F:
                    FrameAll();
                    evt.StopPropagation();
                    break;
                case KeyCode.Delete:
                case KeyCode.Backspace when evt.actionKey:
                    if (npc.FindNode(selectedId) != null)
                    {
                        var id = selectedId;
                        edit(() => DialogueEditing.DeleteNode(serialized, id));
                        select(npc.StartNode);
                    }
                    evt.StopPropagation();
                    break;
            }
        }

        #endregion

        #region Menus

        void ShowNodeMenu(DialogueNodeView view, Vector2 viewportPoint)
        {
            var id = view.Id;
            var node = view.Node;
            var limits = ForgeDesignLimits.Instance;
            var menu = ForgeDropdowns.Menu();
            menu.AddItem("Modifier", false, () => select(id));
            menu.AddItem("Ajouter une réponse du joueur", false, () =>
            {
                var added = -1;
                edit(() => added = DialogueEditing.AddChoice(serialized, id, "Nouvelle réponse", DialogueEditing.End, limits.maxChoicesPerNode));
                if (added < 0)
                    ForgeLists.Notify($"Maximum atteint : {limits.maxChoicesPerNode} réponses par réplique.");
                select(id);
            });
            if (npc.StartNode != id)
                menu.AddItem("Définir comme début", false, () => edit(() => DialogueEditing.SetStart(serialized, id)));
            if (node.choices.Count == 0 && node.next >= 0)
                menu.AddItem("Retirer la suite", false, () => edit(() => DialogueEditing.SetLink(serialized, id, -1, DialogueEditing.End)));
            menu.AddSeparator(string.Empty);
            menu.AddItem("Supprimer la réplique", false, () =>
            {
                edit(() => DialogueEditing.DeleteNode(serialized, id));
                select(npc.StartNode);
            });
            menu.DropDown(new Rect(viewport.LocalToWorld(viewportPoint), Vector2.zero), viewport, DropdownMenuSizeMode.Content);
        }

        void ShowPortMenu(DialogueNodeView view, int choice, Vector2 viewportPoint, bool immediate)
        {
            var id = view.Id;
            void Unlink() => edit(() => DialogueEditing.SetLink(serialized, id, choice, DialogueEditing.End));
            if (immediate)
            {
                Unlink();
                return;
            }
            var menu = ForgeDropdowns.Menu();
            menu.AddItem("Retirer le lien (fin de la conversation)", false, Unlink);
            menu.DropDown(new Rect(viewport.LocalToWorld(viewportPoint), Vector2.zero), viewport, DropdownMenuSizeMode.Content);
        }

        void ShowBackgroundMenu(Vector2 viewportPoint)
        {
            var at = ToContent(viewportPoint);
            var menu = ForgeDropdowns.Menu();
            menu.AddItem($"Nouvelle réplique de {DialogueText.Speaker(DialogueSpeaker.Npc, npc)} ici", false, () => CreateLinked(-1, -1, DialogueSpeaker.Npc, at));
            menu.AddItem("Nouvelle narration ici", false, () => CreateLinked(-1, -1, DialogueSpeaker.Narrator, at));
            menu.AddSeparator(string.Empty);
            menu.AddItem("Recentrer", false, FrameAll);
            menu.AddItem("Réorganiser", false, () => edit(() => DialogueEditing.ClearPositions(serialized)));
            menu.DropDown(new Rect(viewport.LocalToWorld(viewportPoint), Vector2.zero), viewport, DropdownMenuSizeMode.Content);
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
