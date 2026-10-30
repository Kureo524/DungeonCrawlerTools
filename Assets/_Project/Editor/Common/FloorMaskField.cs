using System;
using TheDungeon.Data;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Common
{
    /// <summary>Choix de plusieurs étages : une pastille par étage, à cocher ou décocher (enregistré comme un FloorSet).</summary>
    public sealed class FloorMaskField : VisualElement
    {
        readonly SerializedProperty property;
        readonly VisualElement chips;
        readonly int floorCount;
        readonly bool requireOne;
        readonly int fallback;

        /// <param name="requireOne">Refuse de décocher le dernier étage.</param>
        /// <param name="fallback">Étages affichés tant que la propriété est vide (ancien réglage d'un seul étage).</param>
        public FloorMaskField(string label, SerializedProperty property, int floorCount, string tooltip, bool requireOne = true, int fallback = 0)
        {
            this.fallback = fallback;
            this.property = property;
            this.floorCount = floorCount;
            this.requireOne = requireOne;
            this.tooltip = tooltip;
            AddToClassList("unity-base-field");
            AddToClassList("forge-floor-mask");
            if (!string.IsNullOrEmpty(label))
            {
                var title = new Label(label);
                title.AddToClassList("unity-base-field__label");
                Add(title);
            }
            chips = new VisualElement();
            chips.AddToClassList("forge-floor-mask__chips");
            Add(chips);
            Refresh();
            this.TrackPropertyValue(property, _ => Refresh());
        }

        void Refresh()
        {
            chips.Clear();
            var set = Current;
            for (var floor = 1; floor <= floorCount; floor++)
            {
                var current = floor;
                var on = FloorSet.Contains(set, floor);
                var chip = new Button(() => Toggle(current)) { text = $"Étage {floor}", tooltip = on ? "Cliquez pour retirer cet étage." : "Cliquez pour ajouter cet étage." };
                chip.AddToClassList("forge-floor-mask__chip");
                chip.EnableInClassList("forge-floor-mask__chip--on", on);
                chips.Add(chip);
            }
        }

        void Toggle(int floor)
        {
            property.serializedObject.Update();
            var set = FloorSet.With(Current, floor, !FloorSet.Contains(Current, floor));
            if (requireOne && set == 0)
            {
                ForgeLists.Notify("Gardez au moins un étage.");
                return;
            }
            property.intValue = set;
            property.serializedObject.ApplyModifiedProperties();
            Refresh();
            Changed?.Invoke(set);
        }

        int Current => property.intValue != 0 ? property.intValue : fallback;

        public event Action<int> Changed;
    }
}
