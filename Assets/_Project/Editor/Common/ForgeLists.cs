using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Common
{
    /// <summary>Listes éditables liées à un tableau sérialisé, avec un nombre de lignes plafonné par les limites.</summary>
    public static class ForgeLists
    {
        /// <param name="makeRow">Crée une ligne pour l'élément donné (propriété et index).</param>
        /// <param name="initialize">Valeurs d'un élément ajouté. Renvoie false pour refuser l'ajout.</param>
        public static VisualElement Array(SerializedObject serializedObject, string path, string title, string tooltip, int maxCount,
            Func<SerializedProperty, int, VisualElement> makeRow, Func<SerializedProperty, bool> initialize = null)
        {
            var container = new VisualElement { tooltip = tooltip };
            container.AddToClassList("forge-section");

            var header = new VisualElement();
            header.AddToClassList("forge-row");
            var titleLabel = ForgeUI.Label(title, "forge-section-title");
            titleLabel.style.flexGrow = 1;
            header.Add(titleLabel);
            var addButton = ForgeUI.Button("+ Ajouter", $"Ajoute une ligne (maximum {maxCount}).", () =>
            {
                var property = serializedObject.FindProperty(path);
                if (property.arraySize >= maxCount)
                {
                    Notify($"Maximum atteint : {maxCount} ligne(s).");
                    return;
                }
                property.arraySize++;
                var element = property.GetArrayElementAtIndex(property.arraySize - 1);
                if (initialize != null && !initialize(element))
                {
                    property.arraySize--;
                    serializedObject.ApplyModifiedPropertiesWithoutUndo();
                    return;
                }
                serializedObject.ApplyModifiedProperties();
            });
            header.Add(addButton);
            container.Add(header);
            ForgeUI.Collapsible(container, header, title);
            ForgeLabels.Track(container);

            var rows = new VisualElement();
            container.Add(rows);

            void Rebuild()
            {
                serializedObject.Update();
                var property = serializedObject.FindProperty(path);
                titleLabel.text = $"{ForgeText.WindowRich(title)}  <color=#888888>{property.arraySize}/{maxCount}</color>";
                addButton.SetEnabled(property.arraySize < maxCount);
                rows.Clear();
                if (property.arraySize == 0)
                    rows.Add(ForgeUI.Label(ForgeText.System("Aucune ligne. Cliquez sur « + Ajouter »."), "forge-hint"));

                for (var i = 0; i < property.arraySize; i++)
                {
                    var index = i;
                    var row = new VisualElement();
                    row.AddToClassList("forge-array-row");
                    var content = makeRow(property.GetArrayElementAtIndex(i), i);
                    content.style.flexGrow = 1;
                    row.Add(content);
                    row.Add(ForgeUI.CloseButton("Supprimer cette ligne.", () =>
                    {
                        var array = serializedObject.FindProperty(path);
                        array.DeleteArrayElementAtIndex(index);
                        serializedObject.ApplyModifiedProperties();
                    }));
                    rows.Add(row);
                }
            }

            Rebuild();
            container.TrackPropertyValue(serializedObject.FindProperty(path + ".Array.size"), _ => Rebuild());
            return container;
        }

        public static VisualElement Row(params VisualElement[] children)
        {
            var row = new VisualElement();
            row.AddToClassList("forge-row");
            foreach (var child in children)
                row.Add(child);
            return row;
        }

        public static void Notify(string message)
        {
            // Message système, écrit comme dans le roman : entre crochets, les noms entre apostrophes.
            message = ForgeText.System(message);
            if (EditorWindow.focusedWindow != null)
                EditorWindow.focusedWindow.ShowNotification(new GUIContent(message), 2);
            else
                Debug.LogWarning(message);

        }
    }
}
