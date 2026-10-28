using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Common.Charts
{
    /// <summary>
    /// Anneau de répartition (part d'un tout, 6 segments au plus : le reste est regroupé dans « Autres »).
    /// Légende avec valeurs et pourcentages ; survol d'un segment ou d'une ligne de légende pour le mettre en avant.
    /// </summary>
    public sealed class DonutChart : VisualElement
    {
        public readonly struct Segment
        {
            public readonly string Label;
            public readonly float Value;
            public readonly Color Color;

            public Segment(string label, float value, Color color)
            {
                Label = label;
                Value = value;
                Color = color;
            }
        }

        const int MaxSegments = 6;
        const float Thickness = 14f;
        const float GapDegrees = 1.5f;

        readonly VisualElement ring;
        readonly Label centerValue;
        readonly Label centerCaption;
        readonly VisualElement legend;
        readonly ChartAnimation animation;
        readonly float size;

        List<Segment> segments = new();
        Func<float, string> format = v => v.ToString("0");
        int hover = -1;

        public DonutChart(string title, float size = 120f)
        {
            this.size = size;
            AddToClassList("forge-chart");
            var titleLabel = new Label(title);
            titleLabel.AddToClassList("forge-chart__title");
            Add(titleLabel);

            var body = new VisualElement();
            body.AddToClassList("forge-donut");
            Add(body);

            ring = new VisualElement();
            ring.AddToClassList("forge-donut__ring");
            ring.style.width = size;
            ring.style.height = size;
            ring.generateVisualContent += Draw;
            ring.RegisterCallback<PointerMoveEvent>(evt => SetHover(SegmentAt(evt.localPosition)));
            ring.RegisterCallback<PointerLeaveEvent>(_ => SetHover(-1));
            body.Add(ring);

            var center = new VisualElement { pickingMode = PickingMode.Ignore };
            center.AddToClassList("forge-donut__center");
            centerValue = new Label { pickingMode = PickingMode.Ignore };
            centerValue.AddToClassList("forge-donut__value");
            centerCaption = new Label { pickingMode = PickingMode.Ignore };
            centerCaption.AddToClassList("forge-donut__caption");
            center.Add(centerValue);
            center.Add(centerCaption);
            ring.Add(center);

            legend = new VisualElement();
            legend.AddToClassList("forge-donut__legend");
            body.Add(legend);

            animation = new ChartAnimation(this, ring.MarkDirtyRepaint);
        }

        /// <param name="caption">Texte sous le total, au centre (ex : « objets »).</param>
        /// <param name="centerText">Texte central ; par défaut le total.</param>
        public void SetData(IEnumerable<Segment> data, string caption, Func<float, string> valueFormat = null, string centerText = null)
        {
            format = valueFormat ?? (v => v.ToString("0"));
            var ordered = data.Where(s => s.Value > 0f).ToList();
            if (ordered.Count > MaxSegments)
            {
                var others = ordered.Skip(MaxSegments - 1).Sum(s => s.Value);
                ordered = ordered.Take(MaxSegments - 1).Append(new Segment("Autres", others, ChartColors.Neutral)).ToList();
            }
            segments = ordered;

            var total = segments.Sum(s => s.Value);
            centerValue.text = centerText ?? format(total);
            centerCaption.text = caption;
            RebuildLegend(total);
            animation.PlayOnce();
        }

        void RebuildLegend(float total)
        {
            legend.Clear();
            if (segments.Count == 0)
            {
                legend.Add(ForgeUI.Label("Aucune donnée", "forge-hint"));
                return;
            }
            for (var i = 0; i < segments.Count; i++)
            {
                var index = i;
                var segment = segments[i];
                var row = new VisualElement { tooltip = $"{segment.Label} : {format(segment.Value)} ({segment.Value / total:P0})" };
                row.AddToClassList("forge-donut__legend-row");
                var swatch = new VisualElement();
                swatch.AddToClassList("forge-chart__swatch");
                swatch.style.backgroundColor = segment.Color;
                row.Add(swatch);
                var label = new Label(segment.Label);
                label.AddToClassList("forge-donut__legend-label");
                row.Add(label);
                var value = new Label($"{format(segment.Value)}  <color=#888888>{segment.Value / total:P0}</color>") { enableRichText = true };
                value.AddToClassList("forge-donut__legend-value");
                row.Add(value);
                row.RegisterCallback<PointerEnterEvent>(_ => SetHover(index));
                row.RegisterCallback<PointerLeaveEvent>(_ => SetHover(-1));
                legend.Add(row);
            }
        }

        void Draw(MeshGenerationContext context)
        {
            var painter = context.painter2D;
            var center = new Vector2(size / 2f, size / 2f);
            var radius = size / 2f - Thickness / 2f - 3f;

            painter.lineCap = LineCap.Butt;
            painter.lineWidth = Thickness;
            painter.strokeColor = ChartColors.Track;
            painter.BeginPath();
            painter.Arc(center, radius, Angle.Degrees(0f), Angle.Degrees(360f));
            painter.Stroke();

            var total = segments.Sum(s => s.Value);
            if (total <= 0f)
                return;

            var sweep = 360f * animation.Progress;
            var start = -90f;
            for (var i = 0; i < segments.Count; i++)
            {
                var angle = sweep * segments[i].Value / total;
                var gap = segments.Count > 1 ? Mathf.Min(GapDegrees, angle / 3f) : 0f;
                painter.strokeColor = segments[i].Color;
                painter.lineWidth = i == hover ? Thickness + 5f : Thickness;
                painter.BeginPath();
                painter.Arc(center, radius, Angle.Degrees(start + gap / 2f), Angle.Degrees(start + angle - gap / 2f));
                painter.Stroke();
                start += angle;
            }
        }

        int SegmentAt(Vector2 position)
        {
            var offset = position - new Vector2(size / 2f, size / 2f);
            var distance = offset.magnitude;
            var radius = size / 2f - Thickness / 2f - 3f;
            if (distance < radius - Thickness || distance > radius + Thickness)
                return -1;
            var angle = (Mathf.Atan2(offset.y, offset.x) * Mathf.Rad2Deg + 90f + 360f) % 360f;
            var total = segments.Sum(s => s.Value);
            var accumulated = 0f;
            for (var i = 0; i < segments.Count; i++)
            {
                accumulated += 360f * segments[i].Value / total;
                if (angle <= accumulated)
                    return i;
            }
            return -1;
        }

        void SetHover(int index)
        {
            if (index == hover)
                return;
            hover = index;
            ring.MarkDirtyRepaint();
            var rows = legend.Query(className: "forge-donut__legend-row").ToList();
            for (var i = 0; i < rows.Count; i++)
                rows[i].EnableInClassList("forge-donut__legend-row--hover", i == index);
            ring.tooltip = index >= 0 ? rows[index].tooltip : null;
        }
    }
}
