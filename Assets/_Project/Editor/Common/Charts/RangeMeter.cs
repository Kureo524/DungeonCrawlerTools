using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Common.Charts
{
    /// <summary>Jauge : échelle complète, plage autorisée en surbrillance, repère de la valeur (vert dans la plage, rouge en dehors).</summary>
    public sealed class RangeMeter : VisualElement
    {
        readonly Label title;
        readonly VisualElement band;
        readonly VisualElement marker;
        readonly Label minLabel;
        readonly Label maxLabel;
        readonly Label valueLabel;

        public RangeMeter(string label)
        {
            AddToClassList("forge-meter");
            var header = new VisualElement();
            header.AddToClassList("forge-row");
            title = new Label(label);
            title.AddToClassList("forge-meter__title");
            header.Add(title);
            valueLabel = new Label { enableRichText = true };
            valueLabel.AddToClassList("forge-meter__value");
            header.Add(valueLabel);
            Add(header);

            var track = new VisualElement();
            track.AddToClassList("forge-meter__track");
            band = new VisualElement();
            band.AddToClassList("forge-meter__band");
            track.Add(band);
            marker = new VisualElement();
            marker.AddToClassList("forge-meter__marker");
            track.Add(marker);
            Add(track);

            var scale = new VisualElement();
            scale.AddToClassList("forge-row");
            minLabel = new Label();
            minLabel.AddToClassList("forge-meter__scale");
            maxLabel = new Label();
            maxLabel.AddToClassList("forge-meter__scale");
            maxLabel.style.marginLeft = StyleKeyword.Auto;
            scale.Add(minLabel);
            scale.Add(maxLabel);
            Add(scale);
        }

        /// <param name="scaleMax">Valeur maximale de l'échelle (le minimum est 0).</param>
        public void SetValue(float value, float allowedMin, float allowedMax, float scaleMax, string unit)
        {
            scaleMax = Mathf.Max(scaleMax, value, 1f);
            var inRange = value >= allowedMin && value <= allowedMax;
            band.style.left = Length.Percent(allowedMin / scaleMax * 100f);
            band.style.width = Length.Percent(Mathf.Max(0.5f, (allowedMax - allowedMin) / scaleMax * 100f));
            marker.style.left = Length.Percent(Mathf.Clamp01(value / scaleMax) * 100f);
            marker.style.backgroundColor = inRange ? ChartColors.Good : ChartColors.Critical;
            valueLabel.text = inRange ? $"<b>{value:0.##}</b> {unit}" : $"<b>{value:0.##}</b> {unit}  ✖ hors plage";
            minLabel.text = "0";
            maxLabel.text = $"{scaleMax:0.##}";
            tooltip = $"Valeur : {value:0.##} {unit}\nPlage autorisée : {allowedMin:0.##} à {allowedMax:0.##}";
        }
    }
}
