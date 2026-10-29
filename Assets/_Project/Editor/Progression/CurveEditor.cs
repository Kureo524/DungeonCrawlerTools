using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TheDungeon.Editor.Common.Charts;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Progression
{
    /// <summary>
    /// Courbe d'une stat, modifiable à la souris : glisser un point change sa valeur, Maj+glisser entraîne aussi
    /// les niveaux voisins, Ctrl+glisser dessine la courbe à main levée. Une courbe en pointillés montre un aperçu.
    /// </summary>
    public sealed class CurveEditor : VisualElement
    {
        const float Left = 56f;
        const float Right = 16f;
        const float Top = 14f;
        const float Bottom = 24f;
        const float GrabDistance = 14f;
        const int SmoothRadius = 3;

        readonly VisualElement plot;
        readonly VisualElement labels;
        readonly Label hoverTip;

        float[] values = Array.Empty<float>();
        float[] preview;
        float[] dragStart;
        HashSet<int> alerts = new();
        Color color = Color.white;
        string statName = string.Empty;
        Func<float, string> format = v => v.ToString("0");
        float min;
        float max = 1f;
        int decimals;
        float yMin;
        float yMax = 1f;
        int hover = -1;
        int dragIndex = -1;
        int freehandLast = -1;
        int capturedPointer = -1;

        /// <summary>Valeurs modifiées à la souris (appelé à chaque mouvement pendant le glisser).</summary>
        public event Action<float[]> Edited;
        public event Action EditStarted;
        public event Action EditFinished;

        public bool IsEditing => dragIndex >= 0 || freehandLast >= 0;

        public CurveEditor(float height)
        {
            AddToClassList("forge-chart");
            AddToClassList("forge-curve-editor");
            plot = new VisualElement { focusable = true };
            plot.AddToClassList("forge-chart__plot");
            plot.style.height = height;
            plot.generateVisualContent += Draw;
            plot.RegisterCallback<GeometryChangedEvent>(_ => RebuildLabels());
            plot.RegisterCallback<PointerDownEvent>(OnPointerDown);
            plot.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            plot.RegisterCallback<PointerUpEvent>(_ => EndEdit());
            plot.RegisterCallback<PointerCaptureOutEvent>(_ => EndEdit());
            plot.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (!IsEditing)
                    SetHover(-1, Vector2.zero);
            });
            Add(plot);

            labels = new VisualElement { pickingMode = PickingMode.Ignore };
            labels.AddToClassList("forge-chart__labels");
            plot.Add(labels);

            hoverTip = new Label { pickingMode = PickingMode.Ignore };
            hoverTip.AddToClassList("forge-chart__tooltip");
            plot.Add(hoverTip);
        }

        public float PlotHeight
        {
            set => plot.style.height = value;
        }

        /// <param name="bounds">Valeurs autorisées : un point glissé ne peut pas en sortir.</param>
        /// <param name="previewValues">Courbe en pointillés (aperçu d'une génération), ou null.</param>
        public void SetData(string name, Color curveColor, IReadOnlyList<float> data, (float min, float max) bounds, int valueDecimals,
            Func<float, string> valueFormat, IEnumerable<int> alertPoints, IReadOnlyList<float> previewValues)
        {
            // Pendant un glisser, la courbe affichée fait foi : on ne la remplace pas par une relecture différée.
            if (IsEditing)
                return;
            statName = name;
            color = curveColor;
            values = data.ToArray();
            (min, max) = bounds;
            decimals = valueDecimals;
            format = valueFormat ?? (v => v.ToString("0"));
            alerts = alertPoints != null ? new HashSet<int>(alertPoints) : new HashSet<int>();
            preview = previewValues?.ToArray();
            Rescale();
            plot.MarkDirtyRepaint();
        }

        /// <summary>L'axe vertical couvre les bornes de la stat (élargi si des valeurs en sortent) : le haut du graphique est le maximum autorisé.</summary>
        void Rescale()
        {
            var all = (preview != null ? values.Concat(preview) : values).ToList();
            yMin = Mathf.Min(min, all.DefaultIfEmpty(min).Min());
            yMax = Mathf.Max(max, all.DefaultIfEmpty(max).Max());
            if (yMax - yMin < 0.0001f)
                yMax = yMin + 1f;
            RebuildLabels();
        }

        Rect Inner
        {
            get
            {
                var rect = plot.contentRect;
                return new Rect(Left, Top, Mathf.Max(1f, rect.width - Left - Right), Mathf.Max(1f, rect.height - Top - Bottom));
            }
        }

        float StepX => values.Length > 1 ? Inner.width / (values.Length - 1) : 0f;

        Vector2 Point(int index, float value)
        {
            var inner = Inner;
            return new Vector2(inner.x + StepX * index, inner.yMax - inner.height * (value - yMin) / (yMax - yMin));
        }

        int IndexAt(float x)
        {
            if (values.Length == 0)
                return -1;
            var step = values.Length > 1 ? StepX : 1f;
            return Mathf.Clamp(Mathf.RoundToInt((x - Inner.x) / step), 0, values.Length - 1);
        }

        /// <summary>Valeur sous le pointeur, arrondie aux décimales de la stat et ramenée dans ses bornes.</summary>
        float ValueAt(float y)
        {
            var inner = Inner;
            var raw = yMin + (inner.yMax - y) / inner.height * (yMax - yMin);
            return Mathf.Clamp(ProgressionEditing.Round(raw, decimals), min, max);
        }

        #region Souris

        void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0 || values.Length == 0)
                return;
            var index = IndexAt(evt.localPosition.x);
            if (evt.actionKey)
            {
                freehandLast = index;
                Begin(evt.pointerId);
                values[index] = ValueAt(evt.localPosition.y);
                Edited?.Invoke(values.ToArray());
            }
            else if (Mathf.Abs(Point(index, values[index]).y - evt.localPosition.y) <= GrabDistance)
            {
                dragIndex = index;
                dragStart = values.ToArray();
                Begin(evt.pointerId);
            }
            else
                return;
            SetHover(index, evt.localPosition);
            evt.StopPropagation();
        }

        void Begin(int pointerId)
        {
            capturedPointer = pointerId;
            plot.CapturePointer(pointerId);
            EditStarted?.Invoke();
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (dragIndex >= 0)
            {
                var target = ValueAt(evt.localPosition.y);
                var delta = target - dragStart[dragIndex];
                for (var i = 0; i < values.Length; i++)
                {
                    var distance = Mathf.Abs(i - dragIndex);
                    // Maj : les voisins suivent le mouvement, de moins en moins avec la distance.
                    var weight = distance == 0 ? 1f : evt.shiftKey && distance <= SmoothRadius ? 1f - distance / (SmoothRadius + 1f) : 0f;
                    values[i] = Mathf.Clamp(ProgressionEditing.Round(dragStart[i] + delta * weight, decimals), min, max);
                }
                values[dragIndex] = target;
                Edited?.Invoke(values.ToArray());
                SetHover(dragIndex, evt.localPosition);
                return;
            }
            if (freehandLast >= 0)
            {
                var index = IndexAt(evt.localPosition.x);
                var value = ValueAt(evt.localPosition.y);
                for (var i = Mathf.Min(index, freehandLast); i <= Mathf.Max(index, freehandLast); i++)
                    values[i] = value;
                freehandLast = index;
                Edited?.Invoke(values.ToArray());
                SetHover(index, evt.localPosition);
                return;
            }
            SetHover(IndexAt(evt.localPosition.x), evt.localPosition);
        }

        void EndEdit()
        {
            if (!IsEditing)
                return;
            dragIndex = -1;
            freehandLast = -1;
            dragStart = null;
            if (capturedPointer >= 0 && plot.HasPointerCapture(capturedPointer))
                plot.ReleasePointer(capturedPointer);
            capturedPointer = -1;
            Rescale();
            plot.MarkDirtyRepaint();
            EditFinished?.Invoke();
        }

        #endregion

        #region Dessin

        void Draw(MeshGenerationContext context)
        {
            if (values.Length == 0)
                return;
            var painter = context.painter2D;
            var inner = Inner;

            painter.lineWidth = 1f;
            var ticks = Ticks();
            for (var i = 0; i < ticks.Count; i++)
            {
                var y = Point(0, ticks[i]).y;
                painter.strokeColor = i == 0 ? ChartColors.Axis : ChartColors.Grid;
                Line(painter, new Vector2(inner.x, y), new Vector2(inner.xMax, y));
            }

            // Bornes de la stat, quand des valeurs en sortent (l'axe est alors élargi) : un point glissé ne peut pas les franchir.
            painter.strokeColor = ChartColors.Warning;
            foreach (var bound in new[] { min, max }.Where(b => b > yMin && b < yMax))
                Dashed(painter, new Vector2(inner.x, Point(0, bound).y), new Vector2(inner.xMax, Point(0, bound).y), 3f, 5f);

            if (values.Length > 1)
            {
                painter.fillColor = new Color(color.r, color.g, color.b, 0.1f);
                painter.BeginPath();
                painter.MoveTo(new Vector2(inner.x, inner.yMax));
                for (var i = 0; i < values.Length; i++)
                    painter.LineTo(Point(i, values[i]));
                painter.LineTo(new Vector2(Point(values.Length - 1, 0f).x, inner.yMax));
                painter.ClosePath();
                painter.Fill();
            }

            if (preview != null && preview.Length == values.Length)
            {
                painter.strokeColor = ChartColors.Neutral;
                painter.lineWidth = 2f;
                for (var i = 1; i < preview.Length; i++)
                    Dashed(painter, Point(i - 1, preview[i - 1]), Point(i, preview[i]), 6f, 4f);
            }

            painter.strokeColor = color;
            painter.lineWidth = 2.5f;
            painter.lineJoin = LineJoin.Round;
            painter.lineCap = LineCap.Round;
            painter.BeginPath();
            painter.MoveTo(Point(0, values[0]));
            for (var i = 1; i < values.Length; i++)
                painter.LineTo(Point(i, values[i]));
            painter.Stroke();

            if (hover >= 0 && hover < values.Length)
            {
                painter.strokeColor = ChartColors.Axis;
                painter.lineWidth = 1f;
                Line(painter, new Vector2(Point(hover, 0f).x, inner.y), new Vector2(Point(hover, 0f).x, inner.yMax));
            }

            for (var i = 0; i < values.Length; i++)
            {
                var active = i == hover || i == dragIndex;
                Marker(painter, Point(i, values[i]), alerts.Contains(i) ? ChartColors.Critical : color, active ? 6f : 4f);
            }
        }

        static void Line(Painter2D painter, Vector2 from, Vector2 to)
        {
            painter.BeginPath();
            painter.MoveTo(from);
            painter.LineTo(to);
            painter.Stroke();
        }

        static void Dashed(Painter2D painter, Vector2 from, Vector2 to, float dash, float gap)
        {
            var length = Vector2.Distance(from, to);
            if (length < 0.01f)
                return;
            var direction = (to - from) / length;
            for (var d = 0f; d < length; d += dash + gap)
                Line(painter, from + direction * d, from + direction * Mathf.Min(length, d + dash));
        }

        static void Marker(Painter2D painter, Vector2 center, Color fill, float radius)
        {
            painter.fillColor = ChartColors.Surface;
            painter.BeginPath();
            painter.Arc(center, radius + 2f, Angle.Degrees(0f), Angle.Degrees(360f));
            painter.Fill();
            painter.fillColor = fill;
            painter.BeginPath();
            painter.Arc(center, radius, Angle.Degrees(0f), Angle.Degrees(360f));
            painter.Fill();
        }

        /// <summary>Graduations : le bas de l'axe, puis des valeurs rondes (1, 2, 2,5 ou 5 × 10^n) jusqu'au haut.</summary>
        List<float> Ticks()
        {
            var ticks = new List<float> { yMin };
            var raw = (yMax - yMin) / 4f;
            if (raw <= 0f)
                return ticks;
            var magnitude = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(raw)));
            var step = new[] { 1f, 2f, 2.5f, 5f, 10f }.Select(f => f * magnitude).First(s => s >= raw * 0.999f);
            var minGap = 14f / Mathf.Max(1f, Inner.height) * (yMax - yMin);
            for (var tick = Mathf.Ceil(yMin / step) * step; tick <= yMax + step * 0.001f; tick += step)
                if (tick - yMin >= minGap)
                    ticks.Add(tick);
            return ticks;
        }

        void RebuildLabels()

        {
            labels.Clear();
            if (values.Length == 0 || plot.contentRect.width <= 1f)
                return;
            var inner = Inner;
            foreach (var tick in Ticks())
            {
                var label = new Label(format(tick));
                label.AddToClassList("forge-chart__axis-label");
                label.AddToClassList("forge-chart__axis-label--y");
                label.style.top = Point(0, tick).y - 7f;
                label.style.width = Left - 8f;
                labels.Add(label);
            }
            var every = Mathf.Max(1, Mathf.CeilToInt(values.Length / Mathf.Max(4f, inner.width / 40f)));
            for (var i = 0; i < values.Length; i += every)
            {
                var label = new Label((i + 1).ToString());
                label.AddToClassList("forge-chart__axis-label");
                label.AddToClassList("forge-chart__axis-label--x");
                label.style.left = Point(i, 0f).x - 20f;
                label.style.top = inner.yMax + 4f;
                labels.Add(label);
            }
        }

        #endregion

        void SetHover(int index, Vector2 position)
        {
            if (index == hover && index < 0)
                return;
            hover = index;
            plot.MarkDirtyRepaint();
            if (index < 0)
            {
                hoverTip.style.display = DisplayStyle.None;
                return;
            }

            var text = new StringBuilder($"<b>Niveau {index + 1}</b>\n{statName} : <b>{format(values[index])}</b>");
            if (preview != null && index < preview.Length)
                text.Append($"\nAperçu : {format(preview[index])}");
            if (alerts.Contains(index))
                text.Append("\n⚠ Ne respecte pas l'évolution demandée");
            if (!IsEditing)
                text.Append(Mathf.Abs(Point(index, values[index]).y - position.y) <= GrabDistance
                    ? "\n<i>Glisser pour modifier (Maj : lisser les voisins)</i>"
                    : "\n<i>Ctrl + glisser : dessiner à main levée</i>");
            hoverTip.text = text.ToString();
            hoverTip.style.display = DisplayStyle.Flex;

            var x = Point(index, 0f).x;
            var flip = x > plot.contentRect.width * 0.6f;
            hoverTip.style.left = flip ? StyleKeyword.Auto : x + 12f;
            hoverTip.style.right = flip ? plot.contentRect.width - x + 12f : StyleKeyword.Auto;
            hoverTip.style.top = Mathf.Clamp(position.y - 24f, 0f, Mathf.Max(0f, plot.contentRect.height - 80f));
        }
    }
}
