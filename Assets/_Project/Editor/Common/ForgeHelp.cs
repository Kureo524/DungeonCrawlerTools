using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Common
{
    /// <summary>
    /// Affiche un fichier d'aide Markdown simplifié (titres #, ##, puces -, **gras**).
    /// Les textes sont dans Editor/Help et se modifient sans recompiler.
    /// </summary>
    public static class ForgeHelp
    {
        public static VisualElement Build(string helpFile)
        {
            var scroll = new ScrollView();
            scroll.AddToClassList("forge-help");

            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>($"{DungeonPaths.Help}/{helpFile}.md");
            if (asset == null)
            {
                scroll.Add(ForgeUI.EmptyState($"Aucune aide trouvée ({DungeonPaths.Help}/{helpFile}.md)."));
                return scroll;
            }

            foreach (var rawLine in asset.text.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r');
                if (line.StartsWith("## "))
                    scroll.Add(ForgeUI.Label(Inline(line[3..]), "forge-help-h2"));
                else if (line.StartsWith("# "))
                    scroll.Add(ForgeUI.Label(Inline(line[2..]), "forge-help-h1"));
                else if (line.StartsWith("- "))
                    scroll.Add(ForgeUI.Label("•  " + Inline(line[2..]), "forge-help-bullet"));
                else if (line.Trim().Length == 0)
                    scroll.Add(new VisualElement { name = "spacer", style = { height = 6 } });
                else
                    scroll.Add(ForgeUI.Label(Inline(line), "forge-help-text"));
            }
            return scroll;
        }

        /// <summary>Convertit **gras** et `code` en balises de texte enrichi.</summary>
        static string Inline(string text)
        {
            text = ReplacePairs(text, "**", "<b>", "</b>");
            return ReplacePairs(text, "`", "<color=#E8A05A>", "</color>");
        }

        static string ReplacePairs(string text, string marker, string open, string close)
        {
            var opening = true;
            var index = text.IndexOf(marker, System.StringComparison.Ordinal);
            while (index >= 0)
            {
                var replacement = opening ? open : close;
                text = text.Remove(index, marker.Length).Insert(index, replacement);
                opening = !opening;
                index = text.IndexOf(marker, index + replacement.Length, System.StringComparison.Ordinal);
            }
            return text;
        }
    }
}
