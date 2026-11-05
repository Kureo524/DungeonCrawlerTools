using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TheDungeon.Editor.Studio
{
    /// <summary>Image PNG d'une fenêtre de l'éditeur, même masquée (outil de développement de <see cref="ForgeCapture"/>).</summary>
    internal static class WindowGrab
    {
        public static string Save(EditorWindow window, string name)
        {
            Directory.CreateDirectory(ForgeCapture.Folder);
            var texture = Grab(window);
            var path = Path.Combine(ForgeCapture.Folder, $"{name}.png");
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            return Path.GetFullPath(path);
        }

        /// <summary>
        /// Rendu de la fenêtre. D'abord par l'éditeur lui-même (GUIView.GrabPixels, fonctionne même fenêtre masquée),
        /// sinon en lisant les pixels de l'écran (la fenêtre doit alors être visible).
        /// </summary>
        static Texture2D Grab(EditorWindow window)
        {
            var scale = EditorGUIUtility.pixelsPerPoint;
            var width = Mathf.Max(1, Mathf.RoundToInt(window.position.width * scale));
            var height = Mathf.Max(1, Mathf.RoundToInt(window.position.height * scale));

            var parent = typeof(EditorWindow).GetField("m_Parent", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window);
            var grab = parent?.GetType().GetMethod("GrabPixels", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(RenderTexture), typeof(Rect) }, null);
            if (grab != null)
            {
                var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                try
                {
                    window.Repaint();
                    grab.Invoke(parent, new object[] { target, new Rect(0, 0, width, height) });
                    var previous = RenderTexture.active;
                    RenderTexture.active = target;
                    var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                    texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    texture.Apply();
                    RenderTexture.active = previous;
                    if (!IsBlank(texture))
                        return texture;
                    Object.DestroyImmediate(texture);
                    ForgeCapture.Report("GrabPixels a rendu une image vide : lecture de l'écran à la place");
                }
                finally
                {
                    RenderTexture.ReleaseTemporary(target);
                }
            }

            var pixels = InternalEditorUtility.ReadScreenPixel(window.position.position, (int)window.position.width, (int)window.position.height);
            var screen = new Texture2D((int)window.position.width, (int)window.position.height, TextureFormat.RGBA32, false);
            screen.SetPixels(pixels);
            screen.Apply();
            return screen;
        }

        static bool IsBlank(Texture2D texture)
        {
            var pixels = texture.GetPixels32();
            var first = pixels[0];
            for (var i = 0; i < pixels.Length; i += 97)
                if (pixels[i].r != first.r || pixels[i].g != first.g || pixels[i].b != first.b)
                    return false;
            return true;
        }
    }
}
