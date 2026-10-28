using System;
using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace TheDungeon.Editor.Common
{
    /// <summary>
    /// Liste déroulante qui ne propose que les assets <b>valides</b> d'un type (et éventuellement filtrés),
    /// pour empêcher de référencer une donnée cassée.
    /// </summary>
    public sealed class AssetPicker<T> : VisualElement where T : Object
    {
        const string None = "(aucun)";

        readonly SerializedProperty property;
        readonly Func<T, bool> filter;
        readonly DropdownField dropdown;
        readonly Image preview;
        List<T> choices = new();

        public AssetPicker(string label, SerializedProperty property, string tooltip, Func<T, bool> filter = null, bool allowNone = true)
        {
            this.property = property;
            this.filter = filter;
            AddToClassList("forge-row");
            AddToClassList("forge-picker");

            preview = new Image { scaleMode = ScaleMode.ScaleToFit };
            preview.AddToClassList("forge-picker__preview");

            dropdown = new DropdownField(label) { tooltip = tooltip + "\nSeuls les éléments valides sont proposés." };
            dropdown.style.flexGrow = 1;
            dropdown.RegisterCallback<PointerDownEvent>(_ => RefreshChoices(allowNone), TrickleDown.TrickleDown);
            dropdown.RegisterValueChangedCallback(evt =>
            {
                var index = dropdown.choices.IndexOf(evt.newValue);
                var offset = allowNone ? 1 : 0;
                var selected = index >= offset && index - offset < choices.Count ? choices[index - offset] : null;
                if (selected == null && !allowNone)
                    return;
                property.serializedObject.Update();
                property.objectReferenceValue = selected;
                property.serializedObject.ApplyModifiedProperties();
                UpdatePreview();
            });

            Add(preview);
            Add(dropdown);
            RefreshChoices(allowNone);
            this.TrackPropertyValue(property, _ => RefreshChoices(allowNone));
        }

        void RefreshChoices(bool allowNone)
        {
            choices = AssetFactory.LoadAll<T>()
                .Where(asset => ValidationCache.IsValid(asset) && (filter == null || filter(asset)))
                .ToList();
            var labels = choices.Select(Label).ToList();
            if (allowNone)
                labels.Insert(0, None);
            dropdown.choices = labels;

            var current = property.objectReferenceValue as T;
            if (current == null)
                dropdown.SetValueWithoutNotify(None);
            else if (choices.Contains(current))
                dropdown.SetValueWithoutNotify(Label(current));
            else
                dropdown.SetValueWithoutNotify($"⚠ {Label(current)} (invalide)");
            UpdatePreview();
        }

        void UpdatePreview()
        {
            var sprite = PreviewOf(property.objectReferenceValue);
            preview.sprite = sprite;
            preview.style.display = sprite != null ? DisplayStyle.Flex : DisplayStyle.None;
        }

        static string Label(T asset) => asset is IDisplayNamed named && !string.IsNullOrWhiteSpace(named.DisplayName)
            ? $"{named.DisplayName}  ({asset.name})"
            : asset.name;

        public static Sprite PreviewOf(Object asset) => asset switch
        {
            ItemData item => item.Icon,
            EnemyData enemy => enemy.Sprite,
            BiomeData biome => biome.FloorSprites.Count > 0 ? biome.FloorSprites[0] : null,
            _ => null
        };
    }
}
