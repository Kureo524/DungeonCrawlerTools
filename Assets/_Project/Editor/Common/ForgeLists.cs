using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Common
{
    /// <summary>Listes liées à un tableau sérialisé, avec un nombre d'éléments plafonné par les limites.</summary>
    public static class ForgeLists
    {
        /// <param name="makeItem">Crée une ligne ; ses champs utilisent des bindingPath relatifs à l'élément.</param>
        /// <param name="initialize">Valeurs par défaut d'un élément ajouté.</param>
        public static ListView Bound(SerializedObject serializedObject, string bindingPath, string title, string tooltip,
            int maxCount, Func<VisualElement> makeItem, Action<SerializedProperty> initialize = null)
        {
            var list = new ListView
            {
                bindingPath = bindingPath,
                headerTitle = $"{title} (max {maxCount})",
                tooltip = tooltip,
                makeItem = makeItem,
                showFoldoutHeader = true,
                showAddRemoveFooter = true,
                showBoundCollectionSize = false,
                reorderable = true,
                reorderMode = ListViewReorderMode.Animated,
                virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight
            };

            list.onAdd = _ =>
            {
                var property = serializedObject.FindProperty(bindingPath);
                if (property.arraySize >= maxCount)
                {
                    Notify($"Maximum atteint : {maxCount} élément(s).");
                    return;
                }
                property.arraySize++;
                initialize?.Invoke(property.GetArrayElementAtIndex(property.arraySize - 1));
                serializedObject.ApplyModifiedProperties();
            };
            return list;
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
            if (EditorWindow.focusedWindow != null)
                EditorWindow.focusedWindow.ShowNotification(new GUIContent(message), 2);
            else
                Debug.LogWarning(message);
        }
    }
}
