using System;
using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.ContentDatabase
{
    /// <summary>Champs bornés et pastilles « avant [valeur] après » des éditeurs d'attaque : une saisie hors limites est ramenée dans les limites.</summary>
    public static class AttackFields
    {
        public static IntegerField IntField(SerializedProperty property, float min, float max)
        {
            var input = new IntegerField();
            input.AddToClassList("forge-chip__field");
            input.BindProperty(property);
            input.RegisterValueChangedCallback(evt =>
            {
                var clamped = Mathf.RoundToInt(Mathf.Clamp(evt.newValue, min, max));
                if (clamped != evt.newValue)
                    input.value = clamped;
            });
            return input;
        }

        public static FloatField FloatField(SerializedProperty property, float min, float max)
        {
            var input = new FloatField();
            input.AddToClassList("forge-chip__field");
            input.BindProperty(property);
            input.RegisterValueChangedCallback(evt =>
            {
                var clamped = Mathf.Clamp(evt.newValue, min, max);
                if (!Mathf.Approximately(clamped, evt.newValue))
                    input.value = clamped;
            });
            return input;
        }

        public static VisualElement Pill(string before, VisualElement field, string after, string tooltip)
        {
            var pill = new VisualElement { tooltip = tooltip };
            pill.AddToClassList("forge-chip");
            pill.AddToClassList("forge-chip--setting");
            pill.Add(ForgeUI.Label(before, "forge-chip__text"));
            pill.Add(field);
            if (!string.IsNullOrEmpty(after))
                pill.Add(ForgeUI.Label(after, "forge-chip__text"));
            return pill;
        }

        public static VisualElement FloatPill(SerializedProperty step, string property, string before, string after, FloatRange range, string tooltip) =>
            Pill(before, FloatField(step.FindPropertyRelative(property), range.min, range.max), after, $"{tooltip}\nValeurs autorisées : {range}.");

        public static VisualElement IntPill(SerializedProperty step, string property, string before, string after, IntRange range, string tooltip) =>
            Pill(before, IntField(step.FindPropertyRelative(property), range.min, range.max), after, $"{tooltip}\nValeurs autorisées : {range}.");
    }
}
