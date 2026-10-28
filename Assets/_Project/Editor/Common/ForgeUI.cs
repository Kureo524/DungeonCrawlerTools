using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Common
{
    /// <summary>Petits constructeurs d'éléments d'interface partagés par tous les outils.</summary>
    public static class ForgeUI
    {
        public static void ApplyStyle(VisualElement root)
        {
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(DungeonPaths.StyleSheet);
            if (sheet != null)
                root.styleSheets.Add(sheet);
        }

        public static Label Label(string text, string className = null)
        {
            var label = new Label(text) { enableRichText = true };
            if (className != null)
                label.AddToClassList(className);
            return label;
        }

        public static VisualElement Section(string title, VisualElement parent)
        {
            var section = new VisualElement();
            section.AddToClassList("forge-section");
            section.Add(Label(title, "forge-section-title"));
            parent.Add(section);
            return section;
        }

        public static Button Button(string text, string tooltip, Action onClick, string className = null)
        {
            var button = new Button(onClick) { text = text, tooltip = tooltip };
            button.AddToClassList("forge-button");
            if (className != null)
                button.AddToClassList(className);
            return button;
        }

        public static TextField Text(string label, string bindingPath, string tooltip, bool multiline = false)
        {
            var field = new TextField(label) { bindingPath = bindingPath, tooltip = tooltip, multiline = multiline };
            if (multiline)
                field.AddToClassList("forge-multiline");
            return field;
        }

        public static SliderInt Slider(string label, string bindingPath, IntRange range, string tooltip)
        {
            return new SliderInt(label, range.min, range.max)
            {
                bindingPath = bindingPath,
                showInputField = true,
                tooltip = $"{tooltip}\nValeurs autorisées : {range}."
            };
        }

        public static Slider Slider(string label, string bindingPath, FloatRange range, string tooltip)
        {
            return new Slider(label, range.min, range.max)
            {
                bindingPath = bindingPath,
                showInputField = true,
                tooltip = $"{tooltip}\nValeurs autorisées : {range}."
            };
        }

        public static EnumField Enum<T>(string label, string bindingPath, string tooltip) where T : Enum
        {
            return new EnumField(label, default(T)) { bindingPath = bindingPath, tooltip = tooltip };
        }

        public static ObjectField Object<T>(string label, string bindingPath, string tooltip) where T : UnityEngine.Object
        {
            return new ObjectField(label) { objectType = typeof(T), bindingPath = bindingPath, allowSceneObjects = false, tooltip = tooltip };
        }

        public static Toggle Toggle(string label, string bindingPath, string tooltip)
        {
            return new Toggle(label) { bindingPath = bindingPath, tooltip = tooltip };
        }

        /// <summary>Pastille de couleur (rareté, statut…).</summary>
        public static VisualElement Badge(string text, Color color, string tooltip = null)
        {
            var badge = Label(text, "forge-badge");
            badge.style.backgroundColor = color;
            badge.tooltip = tooltip;
            return badge;
        }

        public static VisualElement EmptyState(string message)
        {
            var label = Label(message, "forge-empty");
            return label;
        }
    }
}
