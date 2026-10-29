using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Progression
{
    /// <summary>
    /// Mini-courbe d'une stat (listes et aperçu). Elle montre la forme : l'échelle va de la plus petite à la plus grande valeur,
    /// alors que l'éditeur de courbe, lui, va du min au max de la stat.
    /// </summary>
    public sealed class Sparkline : VisualElement
    {
        float[] values = Array.Empty<float>();
        Color color = Color.white;

        public Sparkline()
        {
            AddToClassList("forge-spark");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        public void SetData(IReadOnlyList<float> data, Color curveColor)
        {
            values = data.ToArray();
            color = curveColor;
            MarkDirtyRepaint();
        }

        void Draw(MeshGenerationContext context)
        {
            var rect = contentRect;
            if (values.Length < 2 || rect.width < 2f || rect.height < 4f)
                return;
            var low = values.Min();
            var high = values.Max();
            var span = Mathf.Max(0.0001f, high - low);
            var painter = context.painter2D;
            painter.strokeColor = color;
            painter.lineWidth = 1.6f;
            painter.lineJoin = LineJoin.Round;
            painter.lineCap = LineCap.Round;
            painter.BeginPath();
            for (var i = 0; i < values.Length; i++)
            {
                var point = new Vector2(rect.width * i / (values.Length - 1), rect.height - 2f - (rect.height - 4f) * (values[i] - low) / span);
                if (i == 0)
                    painter.MoveTo(point);
                else
                    painter.LineTo(point);
            }
            painter.Stroke();
        }
    }
}
