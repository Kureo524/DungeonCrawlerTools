using System;
using System.IO;
using TheDungeon.Editor.Common;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Dialogues
{
    /// <summary>
    /// Export PNG de l'arbre complet à taille réelle : la vue est déplacée morceau par morceau (zoom 100 %),
    /// chaque morceau est capturé à l'écran puis recollé dans une seule image.
    /// </summary>
    internal sealed class DialogueGraphExport
    {
        const string ExportClass = "forge-dialogue-graph--export";
        const float Margin = 32f;

        readonly DialogueGraphView graph;
        readonly string path;
        readonly Action<bool> done;
        readonly (Vector2 pan, float zoom) savedView;
        readonly Rect bounds;
        readonly Vector2 visibleOffset;
        readonly Vector2 tile;
        readonly int columns;
        readonly int rows;
        readonly float scale;
        Texture2D image;
        int index;

        DialogueGraphExport(DialogueGraphView graph, Rect contentBounds, string path, Rect visible, Action<bool> done)
        {
            this.graph = graph;
            this.path = path;
            this.done = done;
            savedView = graph.View;
            bounds = Rect.MinMaxRect(contentBounds.xMin - Margin, contentBounds.yMin - Margin, contentBounds.xMax + Margin, contentBounds.yMax + Margin);
            visibleOffset = visible.position - graph.Viewport.worldBound.position;
            tile = new Vector2(Mathf.Floor(visible.width) - 2f, Mathf.Floor(visible.height) - 2f);
            columns = Mathf.CeilToInt(bounds.width / tile.x);
            rows = Mathf.CeilToInt(bounds.height / tile.y);
            scale = EditorGUIUtility.pixelsPerPoint;
        }

        /// <param name="scroll">Fiche qui contient l'arbre : seule sa partie visible à l'écran peut être capturée.</param>
        /// <param name="done">Appelé à la fin, avec vrai si l'image a été enregistrée.</param>
        public static void Run(DialogueGraphView graph, Rect contentBounds, string path, ScrollView scroll, Action<bool> done)
        {
            graph.AddToClassList(ExportClass);
            var export = new DialogueGraphExport(graph, contentBounds, path, VisibleArea(graph.Viewport, scroll), done);
            if (export.tile.x < 40f || export.tile.y < 40f)
            {
                graph.RemoveFromClassList(ExportClass);
                ForgeLists.Notify("Agrandissez la fenêtre pour que l'arbre soit visible, puis recommencez.");
                done?.Invoke(false);
                return;
            }
            export.image = new Texture2D(Mathf.CeilToInt(export.bounds.width * export.scale), Mathf.CeilToInt(export.bounds.height * export.scale),
                TextureFormat.RGBA32, false);
            export.Next();
        }

        /// <summary>Partie de la vue réellement affichée à l'écran (la fiche peut en cacher une partie).</summary>
        static Rect VisibleArea(VisualElement viewport, ScrollView scroll)
        {
            var area = viewport.worldBound;
            if (scroll != null)
            {
                var shown = scroll.contentViewport.worldBound;
                area = Rect.MinMaxRect(Mathf.Max(area.xMin, shown.xMin), Mathf.Max(area.yMin, shown.yMin),
                    Mathf.Min(area.xMax, shown.xMax), Mathf.Min(area.yMax, shown.yMax));
            }
            return area;
        }

        void Next()
        {
            if (index >= columns * rows)
            {
                Finish(true);
                return;
            }
            var column = index % columns;
            var row = index / columns;
            graph.View = (visibleOffset + Vector2.one - new Vector2(bounds.xMin + column * tile.x, bounds.yMin + row * tile.y), 1f);
            // On laisse l'éditeur redessiner la vue avant de la capturer.
            graph.schedule.Execute(() =>
            {
                if (!Capture(column, row))
                {
                    Finish(false);
                    return;
                }
                index++;
                Next();
            }).StartingIn(120);
        }

        /// <summary>Capture le morceau affiché et le copie à sa place dans l'image (les textures commencent en bas à gauche).</summary>
        bool Capture(int column, int row)
        {
            var area = graph.Viewport.worldBound;
            var grabbed = ForgeGrab.Region(graph.Viewport, new Rect(area.position + visibleOffset + Vector2.one, tile));
            if (grabbed == null)
                return false;
            var x = Mathf.RoundToInt(column * tile.x * scale);
            var yTop = Mathf.RoundToInt(row * tile.y * scale);
            var width = Mathf.Min(grabbed.width, image.width - x);
            var height = Mathf.Min(grabbed.height, image.height - yTop);
            if (width > 0 && height > 0)
                image.SetPixels(x, image.height - yTop - height, width, height, grabbed.GetPixels(0, grabbed.height - height, width, height));
            UnityEngine.Object.DestroyImmediate(grabbed);
            return true;
        }

        void Finish(bool success)
        {
            graph.RemoveFromClassList(ExportClass);
            graph.View = savedView;
            if (success)
            {
                File.WriteAllBytes(path, image.EncodeToPNG());
                ForgeLists.Notify($"Arbre exporté : {Path.GetFileName(path)} ({image.width}×{image.height}).");
            }
            else
            {
                ForgeLists.Notify("L'éditeur n'a pas permis la capture de l'arbre.");
            }
            UnityEngine.Object.DestroyImmediate(image);
            done?.Invoke(success);
        }
    }
}
