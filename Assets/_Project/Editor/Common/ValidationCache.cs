using System.Collections.Generic;
using TheDungeon.Editor.Validation;
using UnityEditor;
using UnityEngine;

namespace TheDungeon.Editor.Common
{
    /// <summary>Liste des assets valides du projet, recalculée seulement quand le projet change.</summary>
    [InitializeOnLoad]
    public static class ValidationCache
    {
        static HashSet<Object> invalid;

        static ValidationCache()
        {
            EditorApplication.projectChanged += Invalidate;
        }

        public static void Invalidate() => invalid = null;

        /// <summary>Vrai si l'asset existe et ne contient aucune erreur (avertissements tolérés).</summary>
        public static bool IsValid(Object asset)
        {
            if (asset == null)
                return false;
            if (invalid == null)
            {
                invalid = new HashSet<Object>();
                foreach (var issue in ValidationRunner.Run(ValidationContext.FromProject()))
                    if (issue.Severity == Severity.Error && issue.Context != null)
                        invalid.Add(issue.Context);
            }
            return !invalid.Contains(asset);
        }
    }
}
