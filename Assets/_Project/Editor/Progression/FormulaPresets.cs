using System;
using System.Collections.Generic;
using System.Linq;
using TheDungeon.Editor.Common;
using UnityEditor;
using UnityEngine;

namespace TheDungeon.Editor.Progression
{
    [Serializable]
    public sealed class FormulaPreset
    {
        public string name;
        public string expression;

        public FormulaPreset()
        {
        }

        public FormulaPreset(string name, string expression)
        {
            this.name = name;
            this.expression = expression;
        }
    }

    /// <summary>
    /// Fonctions de progression réutilisables. Les presets intégrés sont fixes ; ceux des designers sont enregistrés
    /// dans Settings/ProgressionPresets.asset (versionné, donc partagé avec l'équipe).
    /// </summary>
    public sealed class FormulaPresets : ScriptableObject
    {
        const string AssetPath = DungeonPaths.Settings + "/ProgressionPresets.asset";

        public static readonly FormulaPreset[] BuiltIn =
        {
            new("Linéaire", "start + (end - start) * t"),
            new("Exponentielle", "start * pow(end / start, t)"),
            new("Accélérée (quadratique)", "start + (end - start) * t^2"),
            new("Rapide puis lente (racine)", "start + (end - start) * sqrt(t)"),
            new("Logarithmique", "start + (end - start) * ln(1 + 9 * t) / ln(10)"),
            new("Courbe en S", "start + (end - start) * (3 * t^2 - 2 * t^3)"),
            new("Paliers tous les 5 niveaux", "start + (end - start) * floor((n - 1) / 5) / floor((N - 1) / 5)")
        };

        [SerializeField] List<FormulaPreset> presets = new();

        /// <summary>Presets intégrés puis presets enregistrés par l'équipe.</summary>
        public static IReadOnlyList<FormulaPreset> All => BuiltIn.Concat(Load()?.presets ?? Enumerable.Empty<FormulaPreset>()).ToList();

        public static bool IsBuiltIn(string name) => BuiltIn.Any(p => p.name == name);

        static FormulaPresets Load() => AssetDatabase.LoadAssetAtPath<FormulaPresets>(AssetPath);

        /// <summary>Ajoute ou remplace un preset d'équipe. L'asset n'est créé qu'au premier enregistrement.</summary>
        public static void Save(string name, string expression)
        {
            var asset = Load();
            if (asset == null)
            {
                asset = CreateInstance<FormulaPresets>();
                AssetFactory.EnsureFolder(DungeonPaths.Settings);
                AssetDatabase.CreateAsset(asset, AssetPath);
            }
            Undo.RecordObject(asset, "Enregistrer un preset de progression");
            asset.presets.RemoveAll(p => p.name == name);
            asset.presets.Add(new FormulaPreset(name, expression));
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
        }

        public static void Delete(string name)
        {
            var asset = Load();
            if (asset == null)
                return;
            Undo.RecordObject(asset, "Supprimer un preset de progression");
            asset.presets.RemoveAll(p => p.name == name);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
        }
    }
}
