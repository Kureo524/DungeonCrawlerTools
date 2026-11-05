using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using UnityEditor;
using UnityEngine;

namespace TheDungeon.Editor.Progression
{
    /// <summary>
    /// Import et export SVG d'une progression depuis l'onglet : choix du fichier, conversion par <see cref="ProgressionSvg"/>,
    /// et mise à l'échelle d'une forme de courbe importée dans les valeurs d'une stat.
    /// </summary>
    public static class ProgressionImportExport
    {
        public static void ExportSvg(PlayerProgression progression)
        {
            var path = EditorUtility.SaveFilePanel("Exporter la progression en SVG", string.Empty, $"{progression.name}.svg", "svg");
            if (string.IsNullOrEmpty(path))
                return;
            File.WriteAllText(path, ProgressionSvg.Export(progression));
            ForgeLists.Notify($"Exportée : {Path.GetFileName(path)}");
        }

        /// <summary>Lit une progression exportée en SVG et la confie à <paramref name="open"/>, qui doit en faire une copie (elle est détruite ensuite).</summary>
        public static void ImportProgression(Action<PlayerProgression> open)
        {
            var path = EditorUtility.OpenFilePanel("Importer une progression (SVG)", string.Empty, "svg");
            if (string.IsNullOrEmpty(path))
                return;
            var imported = ScriptableObject.CreateInstance<PlayerProgression>();
            try
            {
                var redrawn = ProgressionSvg.Import(File.ReadAllText(path), imported);
                open(imported);
                ForgeLists.Notify(redrawn.Count > 0
                    ? $"Importée. Courbes retouchées reprises : {string.Join(", ", redrawn)}. Validez pour la créer."
                    : "Importée. Validez pour la créer.");
            }
            catch (Exception exception) when (exception is FormatException or ArgumentException or IOException)
            {
                ForgeDialogs.Inform("Import impossible", exception.Message);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(imported);
            }
        }

        /// <summary>Remplace la courbe d'une stat par la forme d'une courbe SVG choisie par l'utilisateur. Renvoie vrai si la courbe a changé.</summary>
        public static bool ImportShape(SerializedObject serializedObject, PlayerProgression draft, int statIndex)
        {
            if (statIndex >= draft.Stats.Count)
                return false;
            var path = EditorUtility.OpenFilePanel("Importer une courbe (SVG)", string.Empty, "svg");
            if (string.IsNullOrEmpty(path))
                return false;
            try
            {
                var shape = ProgressionSvg.ImportShape(File.ReadAllText(path), draft.MaxLevel);
                var stat = draft.Stats[statIndex];
                var values = FitShape(shape, stat, out var low, out var high);
                ProgressionEditing.SetValues(serializedObject, statIndex, values);
                ForgeLists.Notify($"Courbe importée entre {stat.Format(low)} et {stat.Format(high)} (Ctrl+Z pour annuler).");
                return true;
            }
            catch (Exception exception) when (exception is FormatException or IOException)
            {
                ForgeDialogs.Inform("Import impossible", exception.Message);
                return false;
            }
        }

        /// <summary>
        /// Place une forme normalisée (0 à 1) entre la valeur la plus basse et la plus haute de la stat (ses bornes si la courbe est plate),
        /// arrondie à ses décimales et gardée dans ses bornes.
        /// </summary>
        public static List<float> FitShape(IEnumerable<float> shape, ProgressionStat stat, out float low, out float high)
        {
            low = stat.values.DefaultIfEmpty(stat.min).Min();
            high = stat.values.DefaultIfEmpty(stat.max).Max();
            if (high - low < 0.0001f)
                (low, high) = (stat.min, stat.max);
            var (from, to) = (low, high);
            return shape.Select(t => Mathf.Clamp(ProgressionEditing.Round(Mathf.Lerp(from, to, t), stat.decimals), stat.min, stat.max)).ToList();
        }
    }
}
