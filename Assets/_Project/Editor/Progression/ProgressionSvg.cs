using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using TheDungeon.Data;
using UnityEditor;
using UnityEngine;

namespace TheDungeon.Editor.Progression
{
    /// <summary>
    /// Export d'une progression en SVG lisible partout (navigateur, Inkscape, Figma) et réimport.
    /// Le SVG contient la progression complète (métadonnées) et une courbe par stat : si une courbe a été
    /// redessinée dans un logiciel de dessin, ce sont les points redessinés qui sont réimportés.
    /// </summary>
    public static class ProgressionSvg
    {
        const string MetadataId = "forge-progression";
        const float Width = 820f;
        const float HeaderHeight = 74f;
        const float PanelHeight = 220f;
        const float PlotLeft = 76f;
        const float PlotWidth = 714f;
        const float PlotTop = 38f;
        const float PlotHeight = 150f;

        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        public static string Export(PlayerProgression progression)
        {
            var levels = progression.MaxLevel;
            var height = HeaderHeight + PanelHeight * progression.Stats.Count + 20f;
            var svg = new StringBuilder();
            svg.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            svg.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{N(Width)}\" height=\"{N(height)}\" viewBox=\"0 0 {N(Width)} {N(height)}\" font-family=\"Segoe UI, Helvetica, Arial, sans-serif\">");
            // Le JSON ne doit pas fermer la section CDATA.
            var json = EditorJsonUtility.ToJson(progression).Replace("]]>", "]]]]><![CDATA[>");
            svg.AppendLine($"  <metadata id=\"{MetadataId}\"><![CDATA[{json}]]></metadata>");
            svg.AppendLine($"  <rect width=\"{N(Width)}\" height=\"{N(height)}\" fill=\"#ffffff\"/>");
            svg.AppendLine($"  <text x=\"24\" y=\"38\" font-size=\"22\" font-weight=\"bold\" fill=\"#1d1d20\">{X(progression.DisplayName)}</text>");
            svg.AppendLine($"  <text x=\"24\" y=\"60\" font-size=\"12\" fill=\"#6b6b70\">{levels} niveaux · {progression.Stats.Count} stats · exporté depuis Forge Studio. Déplacez les points d'une courbe puis réimportez le fichier.</text>");

            for (var s = 0; s < progression.Stats.Count; s++)
            {
                var stat = progression.Stats[s];
                var top = HeaderHeight + PanelHeight * s;
                var plot = new Rect(PlotLeft, top + PlotTop, PlotWidth, PlotHeight);
                var (yMin, yMax) = Scale(stat.values);
                var color = "#" + ColorUtility.ToHtmlStringRGB(stat.color);

                svg.AppendLine($"  <g id=\"stat-{s}\" class=\"forge-stat\" data-stat-index=\"{s}\" data-stat-name=\"{X(stat.displayName)}\" " +
                               $"data-plot=\"{N(plot.x)} {N(plot.y)} {N(plot.width)} {N(plot.height)}\" data-y-min=\"{N(yMin)}\" data-y-max=\"{N(yMax)}\">");
                svg.AppendLine($"    <text x=\"24\" y=\"{N(top + 22)}\" font-size=\"15\" font-weight=\"bold\" fill=\"{color}\">{X(stat.displayName)}" +
                               $"<tspan font-size=\"11\" font-weight=\"normal\" fill=\"#6b6b70\">  {X(ProgressionLabels.RoleLabel(stat.role))}</tspan></text>");
                for (var g = 0; g <= 4; g++)
                {
                    var y = plot.yMax - plot.height * g / 4f;
                    svg.AppendLine($"    <line x1=\"{N(plot.x)}\" y1=\"{N(y)}\" x2=\"{N(plot.xMax)}\" y2=\"{N(y)}\" stroke=\"{(g == 0 ? "#b0b0b5" : "#e6e6ea")}\" stroke-width=\"1\"/>");
                    svg.AppendLine($"    <text x=\"{N(plot.x - 8)}\" y=\"{N(y + 4)}\" font-size=\"10\" text-anchor=\"end\" fill=\"#6b6b70\">{X(stat.Format(yMin + (yMax - yMin) * g / 4f))}</text>");
                }
                var every = Mathf.Max(1, Mathf.CeilToInt(levels / 10f));
                for (var i = 0; i < levels; i += every)
                    svg.AppendLine($"    <text x=\"{N(LevelX(plot, i, levels))}\" y=\"{N(plot.yMax + 16)}\" font-size=\"10\" text-anchor=\"middle\" fill=\"#6b6b70\">{i + 1}</text>");

                var points = string.Join(" ", stat.values.Select((v, i) => $"{N(LevelX(plot, i, levels))},{N(ValueY(plot, v, yMin, yMax))}"));
                svg.AppendLine($"    <polyline class=\"forge-curve\" fill=\"none\" stroke=\"{color}\" stroke-width=\"2.5\" stroke-linejoin=\"round\" points=\"{points}\"/>");
                for (var i = 0; i < stat.values.Count; i++)
                    svg.AppendLine($"    <circle cx=\"{N(LevelX(plot, i, levels))}\" cy=\"{N(ValueY(plot, stat.values[i], yMin, yMax))}\" r=\"3\" fill=\"{color}\">" +
                                   $"<title>Niveau {i + 1} : {X(stat.Format(stat.values[i]))}</title></circle>");
                svg.AppendLine("  </g>");
            }
            svg.AppendLine("</svg>");
            return svg.ToString();
        }

        /// <summary>
        /// Remplit <paramref name="target"/> avec la progression contenue dans un SVG exporté par Forge Studio.
        /// Renvoie le nom des stats dont la courbe a été redessinée. Lève <see cref="FormatException"/> si le fichier ne convient pas.
        /// </summary>
        public static List<string> Import(string svgText, PlayerProgression target)
        {
            var document = ParseXml(svgText);
            var metadata = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "metadata" && (string)e.Attribute("id") == MetadataId);
            if (metadata == null)
                throw new FormatException("Ce SVG n'a pas été exporté par Forge Studio : il ne contient pas de progression.\n" +
                                          "Pour reprendre une courbe dessinée ailleurs, utilisez « Importer une courbe » dans la section Courbes.");
            EditorJsonUtility.FromJsonOverwrite(metadata.Value, target);

            var redrawn = new List<string>();
            var serialized = new SerializedObject(target);
            foreach (var group in StatGroups(document))
            {
                var index = (int)group.Attribute("data-stat-index");
                if (index < 0 || index >= target.Stats.Count)
                    continue;
                var stat = target.Stats[index];
                var drawn = ReadGroupValues(group, target.MaxLevel, stat.decimals);
                if (drawn == null || drawn.Length != stat.values.Count)
                    continue;

                // Les coordonnées sont arrondies à l'export : un écart de moins d'un demi-pixel n'est pas une retouche.
                var (yMin, yMax) = ((float)group.Attribute("data-y-min"), (float)group.Attribute("data-y-max"));
                var tolerance = (yMax - yMin) / ReadPlot(group).height * 0.5f;
                var changed = false;
                for (var i = 0; i < drawn.Length; i++)
                {
                    if (Mathf.Abs(drawn[i] - stat.values[i]) <= tolerance)
                        drawn[i] = stat.values[i];
                    else
                        changed = true;
                }
                if (!changed)
                    continue;
                ProgressionEditing.SetValues(serialized, index, drawn.Select(v => Mathf.Clamp(v, stat.min, stat.max)).ToList());
                redrawn.Add(stat.displayName);
            }
            return redrawn;
        }

        /// <summary>
        /// Courbe d'un SVG quelconque, ramenée entre 0 (point le plus bas) et 1 (point le plus haut) pour chaque niveau.
        /// Prend la première polyligne ou le premier tracé du fichier. Lève <see cref="FormatException"/> s'il n'y en a pas.
        /// </summary>
        public static float[] ImportShape(string svgText, int levels)
        {
            var document = ParseXml(svgText);
            var shape = document.Descendants().FirstOrDefault(e => e.Name.LocalName is "polyline" or "path" or "line" or "polygon");
            if (shape == null)
                throw new FormatException("Le SVG ne contient aucune courbe (polyline, path, line ou polygon).");
            var points = ShapePoints(shape).Select(Transform(shape, null)).ToList();
            if (points.Count < 2)
                throw new FormatException("La courbe du SVG a moins de deux points.");

            var minX = points.Min(p => p.x);
            var maxX = points.Max(p => p.x);
            var minY = points.Min(p => p.y);
            var maxY = points.Max(p => p.y);
            if (maxX - minX < 0.001f)
                throw new FormatException("La courbe du SVG est verticale : impossible d'en tirer une valeur par niveau.");
            var plot = new Rect(minX, minY, maxX - minX, Mathf.Max(0.001f, maxY - minY));
            return Sample(points, plot, levels).Select(y => (plot.yMax - y) / plot.height).ToArray();
        }

        static XDocument ParseXml(string svgText)
        {
            try
            {
                // Pas de DTD : un SVG ne doit pas pouvoir charger de ressources externes.
                using var reader = XmlReader.Create(new System.IO.StringReader(svgText), new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
                return XDocument.Load(reader);
            }
            catch (XmlException exception)
            {
                throw new FormatException($"Le fichier n'est pas un SVG valide : {exception.Message}");
            }
        }

        static IEnumerable<XElement> StatGroups(XDocument document) =>
            document.Descendants().Where(e => e.Name.LocalName == "g" && e.Attribute("data-stat-index") != null && e.Attribute("data-plot") != null);

        /// <summary>Valeurs lues sur la courbe d'un groupe de stat, ou null si la courbe est introuvable.</summary>
        static float[] ReadGroupValues(XElement group, int levels, int decimals)
        {
            var curve = group.Descendants().FirstOrDefault(e => e.Name.LocalName is "polyline" or "path" && ((string)e.Attribute("class"))?.Contains("forge-curve") == true)
                        ?? group.Descendants().FirstOrDefault(e => e.Name.LocalName is "polyline" or "path");
            if (curve == null)
                return null;
            var points = ShapePoints(curve).Select(Transform(curve, group)).ToList();
            if (points.Count < 2)
                return null;
            var plot = ReadPlot(group);
            var yMin = (float)group.Attribute("data-y-min");
            var yMax = (float)group.Attribute("data-y-max");
            return Sample(points, plot, levels)
                .Select(y => ProgressionEditing.Round(yMin + (plot.yMax - y) / plot.height * (yMax - yMin), decimals))
                .ToArray();
        }

        static Rect ReadPlot(XElement group)
        {
            var parts = ((string)group.Attribute("data-plot")).Split(' ').Select(p => float.Parse(p, Invariant)).ToArray();
            return new Rect(parts[0], parts[1], parts[2], parts[3]);
        }

        /// <summary>Ordonnée de la courbe à l'abscisse de chaque niveau (interpolation linéaire entre les points).</summary>
        static IEnumerable<float> Sample(List<Vector2> points, Rect plot, int levels)
        {
            var sorted = points.OrderBy(p => p.x).ToList();
            for (var i = 0; i < levels; i++)
            {
                var x = LevelX(plot, i, levels);
                if (x <= sorted[0].x)
                {
                    yield return sorted[0].y;
                    continue;
                }
                if (x >= sorted[^1].x)
                {
                    yield return sorted[^1].y;
                    continue;
                }
                var next = sorted.FindIndex(p => p.x >= x);
                var a = sorted[next - 1];
                var b = sorted[next];
                yield return b.x - a.x < 0.0001f ? b.y : Mathf.Lerp(a.y, b.y, (x - a.x) / (b.x - a.x));
            }
        }

        static float LevelX(Rect plot, int index, int levels) => plot.x + (levels > 1 ? plot.width * index / (levels - 1) : 0f);

        static float ValueY(Rect plot, float value, float yMin, float yMax) => plot.yMax - plot.height * (value - yMin) / (yMax - yMin);

        static (float min, float max) Scale(IReadOnlyCollection<float> values)
        {
            var min = Mathf.Min(0f, values.DefaultIfEmpty(0f).Min());
            var max = values.DefaultIfEmpty(1f).Max();
            return (min, Mathf.Max(min + 1f, max * 1.1f));
        }

        #region Lecture des formes SVG

        static IEnumerable<Vector2> ShapePoints(XElement shape)
        {
            switch (shape.Name.LocalName)
            {
                case "line":
                    return new[] { new Vector2(Attr(shape, "x1"), Attr(shape, "y1")), new Vector2(Attr(shape, "x2"), Attr(shape, "y2")) };
                case "path":
                    return PathPoints((string)shape.Attribute("d") ?? string.Empty);
                default:
                    var numbers = Numbers((string)shape.Attribute("points") ?? string.Empty);
                    return Enumerable.Range(0, numbers.Count / 2).Select(i => new Vector2(numbers[2 * i], numbers[2 * i + 1]));
            }
        }

        static float Attr(XElement element, string name) =>
            float.TryParse((string)element.Attribute(name), NumberStyles.Float, Invariant, out var value) ? value : 0f;

        static readonly Regex NumberPattern = new(@"[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?");
        static readonly Regex PathToken = new(@"[MmLlHhVvCcSsQqTtAaZz]|[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?");

        static List<float> Numbers(string text) => NumberPattern.Matches(text).Select(m => float.Parse(m.Value, Invariant)).ToList();

        /// <summary>Points d'arrivée de chaque segment d'un tracé (les courbes de Bézier sont réduites à leurs extrémités).</summary>
        static List<Vector2> PathPoints(string d)
        {
            var points = new List<Vector2>();
            var tokens = PathToken.Matches(d).Select(m => m.Value).ToList();
            var current = Vector2.zero;
            var start = Vector2.zero;
            var command = 'M';
            var i = 0;

            float Next() => float.Parse(tokens[i++], Invariant);
            bool HasNumber() => i < tokens.Count && !char.IsLetter(tokens[i][0]);

            while (i < tokens.Count)
            {
                if (char.IsLetter(tokens[i][0]))
                    command = tokens[i++][0];
                var relative = char.IsLower(command);
                var origin = relative ? current : Vector2.zero;
                switch (char.ToUpperInvariant(command))
                {
                    case 'Z':
                        current = start;
                        points.Add(current);
                        if (HasNumber())
                            return points; // Nombres sans commande après Z : tracé invalide.
                        continue;
                    case 'M':
                    case 'L':
                    case 'T':
                        if (!HasNumber()) return points;
                        current = origin + new Vector2(Next(), Next());
                        if (char.ToUpperInvariant(command) == 'M')
                        {
                            start = current;
                            command = relative ? 'l' : 'L'; // Les couples suivants d'un M sont des L.
                        }
                        break;
                    case 'H':
                        if (!HasNumber()) return points;
                        current = new Vector2((relative ? current.x : 0f) + Next(), current.y);
                        break;
                    case 'V':
                        if (!HasNumber()) return points;
                        current = new Vector2(current.x, (relative ? current.y : 0f) + Next());
                        break;
                    case 'C':
                        if (!HasNumber()) return points;
                        i += 4;
                        current = origin + new Vector2(Next(), Next());
                        break;
                    case 'S':
                    case 'Q':
                        if (!HasNumber()) return points;
                        i += 2;
                        current = origin + new Vector2(Next(), Next());
                        break;
                    case 'A':
                        if (!HasNumber()) return points;
                        i += 5;
                        current = origin + new Vector2(Next(), Next());
                        break;
                    default:
                        return points;
                }
                points.Add(current);
            }
            return points;
        }

        /// <summary>Applique les attributs transform de l'élément et de ses parents, jusqu'à <paramref name="stopAt"/> exclu.</summary>
        static Func<Vector2, Vector2> Transform(XElement element, XElement stopAt)
        {
            var matrices = new List<Matrix4x4>();
            for (var current = element; current != null && current != stopAt; current = current.Parent)
                matrices.Add(ParseTransform((string)current.Attribute("transform")));
            return point =>
            {
                foreach (var matrix in matrices)
                    point = matrix.MultiplyPoint3x4(point);
                return point;
            };
        }

        static Matrix4x4 ParseTransform(string text)
        {
            var result = Matrix4x4.identity;
            if (string.IsNullOrWhiteSpace(text))
                return result;
            foreach (Match match in Regex.Matches(text, @"(matrix|translate|scale)\s*\(([^)]*)\)"))
            {
                var n = Numbers(match.Groups[2].Value);
                var m = Matrix4x4.identity;
                switch (match.Groups[1].Value)
                {
                    case "matrix" when n.Count == 6:
                        m.m00 = n[0]; m.m10 = n[1]; m.m01 = n[2]; m.m11 = n[3]; m.m03 = n[4]; m.m13 = n[5];
                        break;
                    case "translate" when n.Count >= 1:
                        m.m03 = n[0]; m.m13 = n.Count > 1 ? n[1] : 0f;
                        break;
                    case "scale" when n.Count >= 1:
                        m.m00 = n[0]; m.m11 = n.Count > 1 ? n[1] : n[0];
                        break;
                }
                result *= m;
            }
            return result;
        }

        #endregion

        static string N(float value) => value.ToString("0.##", Invariant);

        static string X(string text) => SecurityElement.Escape(text ?? string.Empty);
    }
}
