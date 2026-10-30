using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Common
{
    /// <summary>
    /// Alignement des champs : dans une même section, les libellés ont tous la largeur du plus long, plus un petit écart.
    /// Les champs sont donc alignés entre eux, juste après le texte, au lieu d'une large colonne de libellés fixe.
    /// </summary>
    public static class ForgeLabels
    {
        const string Scope = "forge-label-scope";
        const float Gap = 16f;

        /// <summary>Aligne les champs de ce conteneur (hors sous-sections) et les réaligne à chaque changement de taille.</summary>
        public static void Track(VisualElement container)
        {
            container.AddToClassList(Scope);
            container.RegisterCallback<GeometryChangedEvent>(_ => Align(container));
        }

        public static void Align(VisualElement container)
        {
            var labels = container.Query<Label>(className: "unity-base-field__label").ToList()
                .Where(label => ScopeOf(label) == container && Eligible(label)).ToList();
            if (labels.Count == 0)
                return;
            var width = Mathf.Ceil(labels.Max(label =>
                label.MeasureTextSize(label.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x)) + Gap;
            if (width <= Gap)
                return;
            foreach (var label in labels)
            {
                if (label.style.width.keyword == StyleKeyword.Undefined && Mathf.Abs(label.style.width.value.value - width) < 0.5f)
                    continue;
                label.style.width = width;
                label.style.minWidth = width;
                label.style.maxWidth = width;
                label.style.flexShrink = 0;
            }
        }

        static VisualElement ScopeOf(VisualElement element)
        {
            for (var parent = element.parent; parent != null; parent = parent.parent)
                if (parent.ClassListContains(Scope))
                    return parent;
            return null;
        }

        /// <summary>Champ avec un libellé, empilé avec d'autres champs (pas dans une ligne de plusieurs champs côte à côte).</summary>
        static bool Eligible(Label label)
        {
            if (string.IsNullOrEmpty(label.text) || label.resolvedStyle.display == DisplayStyle.None)
                return false;
            var field = label.parent;
            if (field?.parent == null || field.ClassListContains("forge-door-toggle"))
                return false;
            if (field.parent.resolvedStyle.flexDirection is FlexDirection.Row or FlexDirection.RowReverse)
                return false;
            return !IsInside(field, "forge-rule-row");
        }

        static bool IsInside(VisualElement element, string className)
        {
            for (var parent = element.parent; parent != null; parent = parent.parent)
                if (parent.ClassListContains(className))
                    return true;
            return false;
        }
    }
}
