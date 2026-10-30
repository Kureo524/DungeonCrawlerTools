using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Common
{
    /// <summary>
    /// Réordonner une liste de cartes par glisser-déposer : on attrape la poignée (trois traits), une copie de la carte
    /// suit le pointeur, les autres cartes s'écartent pour montrer la place visée, et la liste n'est réordonnée qu'au lâcher.
    /// Les cartes ne sont pas déplacées dans la hiérarchie pendant le glisser : le pointeur reste capturé jusqu'au bout.
    /// </summary>
    public sealed class ForgeReorder
    {
        readonly VisualElement list;
        readonly VisualElement overlay;
        readonly ScrollView scroll;
        readonly Action<int, int> moved;

        VisualElement dragged;
        VisualElement ghost;
        int from;
        int to;
        float grabOffset;
        float[] centers;

        /// <param name="list">Conteneur des cartes (une carte = un enfant).</param>
        /// <param name="overlay">Élément qui accueille la copie flottante (positionnée en absolu au-dessus de tout).</param>
        /// <param name="scroll">Zone qui défile près des bords pendant le glisser, ou null.</param>
        /// <param name="moved">Appelé au lâcher avec l'ancienne et la nouvelle place de la carte.</param>
        public ForgeReorder(VisualElement list, VisualElement overlay, ScrollView scroll, Action<int, int> moved)
        {
            this.list = list;
            this.overlay = overlay;
            this.scroll = scroll;
            this.moved = moved;
        }

        public bool IsDragging => dragged != null;

        /// <summary>Poignée à trois traits pour cette carte.</summary>
        /// <param name="makeGhost">Contenu de la copie flottante (nom, couleur…).</param>
        public VisualElement Handle(VisualElement card, Func<VisualElement> makeGhost, string tooltip)
        {
            card.AddToClassList("forge-reorder-item");
            var handle = new VisualElement { tooltip = tooltip };
            handle.AddToClassList("forge-drag-handle");
            handle.generateVisualContent += context =>
            {
                var center = handle.contentRect.center;
                var painter = context.painter2D;
                painter.strokeColor = handle.resolvedStyle.color;
                painter.lineWidth = 2f;
                painter.lineCap = LineCap.Round;
                painter.BeginPath();
                for (var line = -1; line <= 1; line++)
                {
                    painter.MoveTo(center + new Vector2(-8f, line * 5.5f));
                    painter.LineTo(center + new Vector2(8f, line * 5.5f));
                }
                painter.Stroke();
            };
            handle.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || dragged != null)
                    return;
                handle.CapturePointer(evt.pointerId);
                Start(card, makeGhost(), evt.position);
                evt.StopPropagation();
            });
            handle.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (dragged == card && handle.HasPointerCapture(evt.pointerId))
                    DragTo(evt.position);
            });
            handle.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (handle.HasPointerCapture(evt.pointerId))
                    handle.ReleasePointer(evt.pointerId);
                End();
            });
            handle.RegisterCallback<PointerCaptureOutEvent>(_ => End());
            return handle;
        }

        void Start(VisualElement card, VisualElement content, Vector2 pointer)
        {
            var children = list.Children().ToList();
            from = to = children.IndexOf(card);
            centers = children.Select(c => c.layout.center.y).ToArray();
            var cardTop = overlay.WorldToLocal(card.worldBound.position);
            grabOffset = overlay.WorldToLocal(pointer).y - cardTop.y;

            ghost = new VisualElement { pickingMode = PickingMode.Ignore };
            ghost.AddToClassList("forge-reorder-ghost");
            ghost.style.position = Position.Absolute;
            ghost.style.left = cardTop.x;
            ghost.style.top = cardTop.y;
            ghost.style.width = card.worldBound.width;
            // Opaque : la copie passe au-dessus des autres cartes sans se mélanger à leur texte.
            ghost.style.backgroundColor = new Color(0.10f, 0.11f, 0.16f, 1f);
            ghost.Add(content);
            overlay.Add(ghost);

            dragged = card;
            card.AddToClassList("forge-reorder--placeholder");
        }

        void DragTo(Vector2 pointer)
        {
            if (scroll != null)
            {
                // Défilement automatique près des bords.
                var viewport = scroll.contentViewport.worldBound;
                if (pointer.y < viewport.yMin + 40f)
                    scroll.scrollOffset -= new Vector2(0f, 14f);
                else if (pointer.y > viewport.yMax - 40f)
                    scroll.scrollOffset += new Vector2(0f, 14f);
            }
            ghost.style.top = overlay.WorldToLocal(pointer).y - grabOffset;

            // Place visée : nombre de cartes (hors celle déplacée) dont le milieu est au-dessus du pointeur.
            var pointerInList = list.WorldToLocal(pointer).y;
            var target = 0;
            for (var i = 0; i < centers.Length; i++)
                if (i != from && centers[i] < pointerInList)
                    target++;
            if (target == to)
                return;
            to = target;

            // Les cartes entre l'ancienne et la nouvelle place glissent pour ouvrir la place (cartes de hauteurs différentes comprises).
            var children = list.Children().ToList();
            var gap = children.Count > 1 ? Mathf.Max(0f, children[Mathf.Min(from + 1, children.Count - 1)].layout.y - children[from].layout.yMax) : 0f;
            var size = dragged.layout.height + gap;
            for (var i = 0; i < children.Count; i++)
            {
                float offset = 0f;
                if (i == from)
                    offset = to > from ? children[to].layout.yMax - dragged.layout.yMax : children[to].layout.y - dragged.layout.y;
                else if (from < i && i <= to)
                    offset = -size;
                else if (to <= i && i < from)
                    offset = size;
                children[i].style.translate = new Translate(0f, offset);
            }
        }

        void End()
        {
            if (dragged == null)
                return;
            ghost.RemoveFromHierarchy();
            foreach (var card in list.Children())
                card.style.translate = StyleKeyword.Null;
            dragged.RemoveFromClassList("forge-reorder--placeholder");
            dragged = null;
            if (from != to)
                moved(from, to);
        }
    }
}
