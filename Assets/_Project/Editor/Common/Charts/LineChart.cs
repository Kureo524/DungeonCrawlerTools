using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Common.Charts
{
    /// <summary>
    /// Courbe(s) sur un seul axe vertical partant de zéro. Survol : ligne de repère + infobulle avec toutes les valeurs.
    /// Les points signalés (ex : valeur qui baisse) sont marqués en rouge.
    /// </summary>
    public sealed class LineChart : VisualElement
    {
        public sealed class Series
        {
            public string Name;
            public Color Color;
            public float[] Values;
            /// <summary>Série de comparaison : trait plus fin, sans remplissage.</summary>
            public bool Secondary;
        }

        const float Left = 44f;
        const float Right = 10f;
        const float Top = 10f;
        const float Bottom = 20f;

        readonly Label title;
        readonly VisualElement legendHost;
        readonly VisualElement plot;
        readonly VisualElement labels;
        readonly Label hoverTip;
        readonly ChartAnimation animation;

        List<Series> series = new();
        string[] xLabels = Array.Empty<string>();
        Func<float, string> format = v => v.ToString("0");
        HashSet<int> alerts = new();
        string xName = string.Empty;
        float yMax = 1f;
        int hover = -1;

        public LineChart(string chartTitle, float height = 150f)
        {
            AddToClassList("forge-chart");
            title = new Label(chartTitle);
            title.AddToClassList("forge-chart__title");
            Add(title);
            legendHost = new VisualElement();
            Add(legendHost);

            plot = new VisualElement();
            plot.AddToClassList("forge-chart__plot");
            plot.style.height = height;
            plot.generateVisualContent += Draw;
            plot.RegisterCallback<GeometryChangedEvent>(_ => RebuildLabels());
            plot.RegisterCallback<PointerMoveEvent>(evt => SetHover(IndexAt(evt.localPosition.x), evt.localPosition));
            plot.RegisterCallback<PointerLeaveEvent>(_ => SetHover(-1, Vector2.zero));
            Add(plot);

            labels = new VisualElement { pickingMode = PickingMode.Ignore };
            labels.AddToClassList("forge-chart__labels");
            plot.Add(labels);

            hoverTip = new Label { pickingMode = PickingMode.Ignore };
            hoverTip.AddToClassList("forge-chart__tooltip");
            plot.Add(hoverTip);

            animation = new ChartAnimation(this, plot.MarkDirtyRepaint);
        }

        /// <param name="xName">Nom de l'axe horizontal affiché dans l'infobulle (ex : « Niveau »).</param>
        /// <param name="alertPoints">Index des points à signaler en rouge.</param>
        public void SetData(string xName, string[] xLabels, IEnumerable<Series> data, Func<float, string> valueFormat = null,
            IEnumerable<int> alertPoints = null)
        {
            this.xName = xName;
            this.xLabels = xLabels;
            series = data.Where(s => s.Values != null && s.Values.Length > 0).ToList();
            format = valueFormat ?? (v => v.ToString("0"));
            alerts = alertPoints != null ? new HashSet<int>(alertPoints) : new HashSet<int>();

            var max = series.Count == 0 ? 0f : series.SelectMany(s => s.Values).DefaultIfEmpty(0f).Max();
            yMax = NiceCeiling(Mathf.Max(1f, max * 1.05f));

            legendHost.Clear();
            if (series.Count >= 2)
                legendHost.Add(ChartLegend.Build(series.Select(s => (s.Name, s.Color))));

            RebuildLabels();
            animation.PlayOnce();
            plot.MarkDirtyRepaint();
        }

        int Count => series.Count == 0 ? 0 : series.Max(s => s.Values.Length);

        Rect Inner
        {
            get
            {
                var rect = plot.contentRect;
                return new Rect(Left, Top, Mathf.Max(1f, rect.width - Left - Right), Mathf.Max(1f, rect.height - Top - Bottom));
            }
        }

        Vector2 Point(int index, float value)
        {
            var inner = Inner;
            var step = Count > 1 ? inner.width / (Count - 1) : 0f;
            return new Vector2(inner.x + step * index, inner.yMax - inner.height * Mathf.Clamp01(value / yMax));
        }

        int IndexAt(float x)
        {
            if (Count == 0)
                return -1;
            var inner = Inner;
            var step = Count > 1 ? inner.width / (Count - 1) : 1f;
            return Mathf.Clamp(Mathf.RoundToInt((x - inner.x) / step), 0, Count - 1);
        }

        void Draw(MeshGenerationContext context)
        {
            if (Count == 0)
                return;
            var painter = context.painter2D;
            var inner = Inner;

            // Grille discrète : 4 lignes horizontales.
            painter.lineWidth = 1f;
            for (var i = 0; i <= 4; i++)
            {
                var y = inner.yMax - inner.height * i / 4f;
                painter.strokeColor = i == 0 ? ChartColors.Axis : ChartColors.Grid;
                painter.BeginPath();
                painter.MoveTo(new Vector2(inner.x, y));
                painter.LineTo(new Vector2(inner.xMax, y));
                painter.Stroke();
            }

            var visible = Mathf.Max(1, Mathf.CeilToInt(Count * animation.Progress));
            foreach (var line in series)
            {
                var count = Mathf.Min(visible, line.Values.Length);
                if (!line.Secondary && series.Count(s => !s.Secondary) == 1 && count > 1)
                {
                    // Aire sous la courbe, très légère, pour la série principale unique.
                    painter.fillColor = new Color(line.Color.r, line.Color.g, line.Color.b, 0.12f);
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(Point(0, 0).x, inner.yMax));
                    for (var i = 0; i < count; i++)
                        painter.LineTo(Point(i, line.Values[i]));
                    painter.LineTo(new Vector2(Point(count - 1, 0).x, inner.yMax));
                    painter.ClosePath();
                    painter.Fill();
                }

                painter.strokeColor = line.Secondary ? new Color(line.Color.r, line.Color.g, line.Color.b, 0.7f) : line.Color;
                painter.lineWidth = line.Secondary ? 1.5f : 2f;
                painter.lineJoin = LineJoin.Round;
                painter.lineCap = LineCap.Round;
                painter.BeginPath();
                painter.MoveTo(Point(0, line.Values[0]));
                for (var i = 1; i < count; i++)
                    painter.LineTo(Point(i, line.Values[i]));
                painter.Stroke();

                if (Count <= 6)
                    for (var i = 0; i < count; i++)
                        Marker(painter, Point(i, line.Values[i]), line.Color, 3.5f);
            }

            foreach (var index in alerts.Where(i => i < visible))
                foreach (var line in series.Where(s => !s.Secondary && index < s.Values.Length))
                    Marker(painter, Point(index, line.Values[index]), ChartColors.Critical, 4.5f);

            if (hover >= 0)
            {
                var x = Point(hover, 0).x;
                painter.strokeColor = ChartColors.Axis;
                painter.lineWidth = 1f;
                painter.BeginPath();
                painter.MoveTo(new Vector2(x, inner.y));
                painter.LineTo(new Vector2(x, inner.yMax));
                painter.Stroke();
                foreach (var line in series.Where(s => hover < s.Values.Length))
                    Marker(painter, Point(hover, line.Values[hover]), line.Color, 4.5f);
            }
        }

        /// <summary>Point de 8 px minimum, cerclé de la couleur du fond pour se détacher de la courbe.</summary>
        static void Marker(Painter2D painter, Vector2 center, Color color, float radius)
        {
            painter.fillColor = ChartColors.Surface;
            painter.BeginPath();
            painter.Arc(center, radius + 2f, Angle.Degrees(0f), Angle.Degrees(360f));
            painter.Fill();
            painter.fillColor = color;
            painter.BeginPath();
            painter.Arc(center, radius, Angle.Degrees(0f), Angle.Degrees(360f));
            painter.Fill();
        }

        void RebuildLabels()
        {
            labels.Clear();
            if (Count == 0 || plot.contentRect.width <= 1f)
                return;
            var inner = Inner;
            for (var i = 0; i <= 4; i++)
            {
                var label = new Label(format(yMax * i / 4f));
                label.AddToClassList("forge-chart__axis-label");
                label.AddToClassList("forge-chart__axis-label--y");
                label.style.top = inner.yMax - inner.height * i / 4f - 7f;
                label.style.width = Left - 6f;
                labels.Add(label);
            }
            var every = Mathf.Max(1, Mathf.CeilToInt(Count / 8f));
            for (var i = 0; i < Count; i += every)
            {
                var label = new Label(i < xLabels.Length ? xLabels[i] : (i + 1).ToString());
                label.AddToClassList("forge-chart__axis-label");
                label.AddToClassList("forge-chart__axis-label--x");
                label.style.left = Point(i, 0).x - 20f;
                label.style.top = inner.yMax + 3f;
                labels.Add(label);
            }
        }

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

            var text = new StringBuilder($"<b>{xName} {(index < xLabels.Length ? xLabels[index] : (index + 1).ToString())}</b>");
            foreach (var line in series.Where(s => index < s.Values.Length))
                text.Append($"\n{line.Name} : <b>{format(line.Values[index])}</b>");
            if (alerts.Contains(index))
                text.Append("\n⚠ Baisse par rapport au point précédent");
            hoverTip.text = text.ToString();
            hoverTip.style.display = DisplayStyle.Flex;

            var x = Point(index, 0).x;
            var flip = x > plot.contentRect.width * 0.6f;
            hoverTip.style.left = flip ? StyleKeyword.Auto : x + 10f;
            hoverTip.style.right = flip ? plot.contentRect.width - x + 10f : StyleKeyword.Auto;
            hoverTip.style.top = Mathf.Clamp(position.y - 20f, 0f, Mathf.Max(0f, plot.contentRect.height - 60f));
        }

        /// <summary>Arrondit vers le haut à une valeur « ronde » (1, 2, 2.5, 5 × 10^n) pour des graduations lisibles.</summary>
        static float NiceCeiling(float value)
        {
            var magnitude = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(value)));
            foreach (var step in new[] { 1f, 2f, 2.5f, 5f, 10f })
                if (step * magnitude >= value)
                    return step * magnitude;
            return 10f * magnitude;
        }
    }
}
