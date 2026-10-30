using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Common
{
    /// <summary>
    /// Image de ce qu'affiche un élément d'interface d'une fenêtre de l'éditeur, rendue par l'éditeur lui-même
    /// (la fenêtre n'a pas besoin d'être au premier plan). Sert à l'export de l'arbre de dialogue en PNG.
    /// </summary>
    public static class ForgeGrab
    {
        /// <summary>Pixels de la zone occupée par <paramref name="element"/>, ou null si l'éditeur ne permet pas la capture.</summary>
        public static Texture2D Element(VisualElement element) => Region(element, element.worldBound);

        /// <summary>Pixels d'une zone du panneau de <paramref name="element"/> (coordonnées du panneau), ou null si la capture est impossible.</summary>
        public static Texture2D Region(VisualElement element, Rect bounds)
        {
            var panel = element.panel;
            if (panel == null)
                return null;
            // La vue de l'éditeur (GUIView) qui affiche ce panneau.
            var view = panel.GetType().GetProperty("ownerObject", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(panel);
            var grab = view?.GetType().GetMethod("GrabPixels", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(RenderTexture), typeof(Rect) }, null);
            if (grab == null)
                return null;

            var scale = EditorGUIUtility.pixelsPerPoint;
            var size = panel.visualTree.layout.size;
            var width = Mathf.Max(1, Mathf.RoundToInt(size.x * scale));
            var height = Mathf.Max(1, Mathf.RoundToInt(size.y * scale));
            var rect = new RectInt(Mathf.RoundToInt(bounds.x * scale), 0, Mathf.RoundToInt(bounds.width * scale), Mathf.RoundToInt(bounds.height * scale));
            // Les textures commencent en bas : la ligne du haut de l'élément est la plus haute de la texture.
            rect.y = height - Mathf.RoundToInt(bounds.yMax * scale);
            rect.xMin = Mathf.Clamp(rect.xMin, 0, width);
            rect.yMin = Mathf.Clamp(rect.yMin, 0, height);
            rect.width = Mathf.Clamp(rect.width, 1, width - rect.x);
            rect.height = Mathf.Clamp(rect.height, 1, height - rect.y);

            var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            try
            {
                grab.Invoke(view, new object[] { target, new Rect(0, 0, width, height) });
                RenderTexture.active = target;
                var texture = new Texture2D(rect.width, rect.height, TextureFormat.RGBA32, false);
                texture.ReadPixels(new Rect(rect.x, rect.y, rect.width, rect.height), 0, 0);
                texture.Apply();
                return texture;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }
        }
    }
}
