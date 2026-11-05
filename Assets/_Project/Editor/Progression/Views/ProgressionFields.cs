using System;
using System.Collections.Generic;
using System.Linq;
using TheDungeon.Editor.Common;
using TheDungeon.Editor.Common.Charts;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Progression
{
    /// <summary>Éléments d'interface communs aux vues de l'onglet Progression (lignes, cartes, pastilles, champs liés).</summary>
    internal static class ProgressionFields
    {
        public static VisualElement Row()
        {
            var row = new VisualElement();
            row.AddToClassList("forge-stat-card__row");
            return row;
        }

        public static Button SmallButton(string text, string tooltip, Action click, bool enabled, string className = null)
        {
            var button = ForgeUI.Button(text, tooltip, click, className);
            button.AddToClassList("forge-stat-card__button");
            button.SetEnabled(enabled);
            return button;
        }

        public static VisualElement Dot(Color color)
        {
            var dot = new VisualElement { pickingMode = PickingMode.Ignore };
            dot.AddToClassList("forge-dot");
            dot.style.backgroundColor = color;
            return dot;
        }

        /// <summary>Carte avec couleur et nom (attribut, compétence, mécanique) ; les boutons sont ajoutés à <paramref name="header"/>.</summary>
        public static VisualElement Card(SerializedProperty property, string nameTooltip, out VisualElement header)
        {
            var card = new VisualElement();
            card.AddToClassList("forge-stat-card");
            header = Row();
            var colorField = new ColorField { showAlpha = false, tooltip = "Couleur dans la fiche du joueur." };
            colorField.AddToClassList("forge-stat-card__color");
            header.Add(Bound(colorField, property.FindPropertyRelative("color")));
            var name = new TextField { tooltip = nameTooltip };
            name.AddToClassList("forge-stat-card__name");
            header.Add(Bound(name, property.FindPropertyRelative("displayName")));
            card.Add(header);
            return card;
        }

        public static VisualElement Description(SerializedProperty property, string tooltip)
        {
            var row = Row();
            var field = new TextField("Description") { tooltip = tooltip };
            field.BindProperty(property.FindPropertyRelative("description"));
            row.Add(field);
            return row;
        }

        public static T Bound<T>(T field, SerializedProperty property) where T : IBindable
        {
            field.BindProperty(property);
            return field;
        }

        /// <summary>Couleur d'un nouvel élément : la palette des graphiques (8 couleurs) reprend au début une fois épuisée.</summary>
        public static Color NextColor(int index) => ChartColors.Series(index % ChartColors.SeriesCount);

        public static string UniqueName(IEnumerable<string> existing, string name)
        {
            var names = existing.ToHashSet();
            var candidate = name;
            for (var i = 2; names.Contains(candidate); i++)
                candidate = $"{name} {i}";
            return candidate;
        }
    }
}
