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
    /// <summary>Souris et clavier de l'arbre : déplacer le fond ou une réplique, tracer et retirer des liens, menus contextuels.</summary>
    public sealed partial class DialogueGraphView
    {
        const float DragThreshold = 4f;

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
    }
}
