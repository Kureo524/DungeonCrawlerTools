using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Common
{
    /// <summary>Galerie de sprites avec recherche : un clic choisit le sprite.</summary>
    public class SpritePickerWindow : EditorWindow
    {
        Action<Sprite> onPicked;
        string folder;
        List<Sprite> sprites;
        VisualElement grid;
        ToolbarSearchField search;

        public static void Show(string artFolder, Action<Sprite> onPicked)
        {
            var window = CreateInstance<SpritePickerWindow>();
            window.titleContent = new GUIContent($"Choisir un sprite ({artFolder})");
            window.folder = string.IsNullOrEmpty(artFolder) ? DungeonPaths.Art : $"{DungeonPaths.Art}/{artFolder}";
            window.onPicked = onPicked;
            window.minSize = new Vector2(420, 360);
            var main = EditorGUIUtility.GetMainWindowPosition();
            window.position = new Rect(main.center.x - 300, main.center.y - 250, 600, 500);
            window.ShowAuxWindow();
        }

        public void CreateGUI()
        {
            ForgeUI.ApplyStyle(rootVisualElement);
            sprites = AssetDatabase.FindAssets("t:Sprite", new[] { folder })
                .Select(guid => AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(sprite => sprite != null)
                .OrderBy(sprite => sprite.name)
                .ToList();

            search = new ToolbarSearchField { tooltip = "Filtre les sprites par nom." };
            search.style.width = StyleKeyword.Auto;
            search.RegisterValueChangedCallback(_ => Fill());
            rootVisualElement.Add(search);

            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;
            grid = new VisualElement();
            grid.AddToClassList("forge-gallery");
            scroll.Add(grid);
            rootVisualElement.Add(scroll);
            Fill();
            search.Focus();
        }

        void Fill()
        {
            grid.Clear();
            var text = search.value?.Trim() ?? string.Empty;
            var shown = sprites.Where(s => text.Length == 0 || s.name.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
            if (shown.Count == 0)
                grid.Add(ForgeUI.EmptyState($"Aucun sprite trouvé dans {folder}."));

            foreach (var sprite in shown)
            {
                var cell = new VisualElement { tooltip = sprite.name };
                cell.AddToClassList("forge-gallery__cell");
                cell.Add(new Image { sprite = sprite, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore });
                var label = new Label(sprite.name.Replace("SPR_", string.Empty)) { pickingMode = PickingMode.Ignore };
                cell.Add(label);
                cell.RegisterCallback<ClickEvent>(_ =>
                {
                    onPicked?.Invoke(sprite);
                    Close();
                });
                grid.Add(cell);
            }
        }
    }
}
