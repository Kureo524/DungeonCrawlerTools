using System.Collections.Generic;
using System.Linq;
using TheDungeon.Editor.Validation;
using UnityEditor;
using UnityEngine;

namespace TheDungeon.Editor.Common
{
    /// <summary>
    /// Copie de travail d'un asset. Les outils modifient uniquement la copie ; l'asset réel n'est créé
    /// ou mis à jour qu'au clic sur « Valider », et seulement si la validation ne trouve aucune erreur.
    /// </summary>
    public sealed class DraftSession<T> where T : ScriptableObject
    {
        string snapshot;

        /// <summary>Asset enregistré, ou null tant qu'une création n'a pas été validée.</summary>
        public T Original { get; private set; }
        public T Draft { get; }
        public bool IsNew => Original == null;
        public bool IsDirty => IsNew || EditorJsonUtility.ToJson(Draft) != snapshot;

        DraftSession(T original, T draft)
        {
            Original = original;
            Draft = draft;
            Draft.hideFlags = HideFlags.DontSave;
            snapshot = EditorJsonUtility.ToJson(Draft);
        }

        public static DraftSession<T> Edit(T original)
        {
            var draft = Object.Instantiate(original);
            draft.name = original.name;
            return new DraftSession<T>(original, draft);
        }

        public static DraftSession<T> CreateNew(string displayName)
        {
            var draft = ScriptableObject.CreateInstance<T>();
            var serialized = new SerializedObject(draft);
            var nameProperty = serialized.FindProperty("displayName");
            if (nameProperty != null)
            {
                nameProperty.stringValue = displayName;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            // Sans champ « nom » (ex : progression), le nom proposé sert de base au nom du fichier.
            draft.name = NamingConvention.ToPascalCase(displayName);
            draft.name = NamingConvention.GetExpectedName(draft);
            return new DraftSession<T>(null, draft);
        }

        public static DraftSession<T> Duplicate(T source)
        {
            var draft = Object.Instantiate(source);
            var serialized = new SerializedObject(draft);
            var nameProperty = serialized.FindProperty("displayName");
            if (nameProperty != null)
            {
                nameProperty.stringValue += " Copie";
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            draft.name = NamingConvention.GetExpectedName(draft);
            return new DraftSession<T>(null, draft);
        }

        /// <summary>Nouvelle copie de travail reprenant le contenu d'un modèle (import d'un fichier…), à valider pour créer l'asset.</summary>
        public static DraftSession<T> CreateFrom(T template)
        {
            var draft = Object.Instantiate(template);
            draft.name = NamingConvention.GetExpectedName(draft);
            return new DraftSession<T>(null, draft);
        }

        /// <summary>Problèmes de la copie de travail, évaluée avec le reste du projet.</summary>
        public List<ValidationIssue> Validate()
        {
            Draft.name = NamingConvention.GetExpectedName(Draft);
            var context = ValidationContext.FromProject(Original, Draft);
            return ValidationRunner.Run(context).Where(issue => issue.Context == Draft).ToList();
        }

        /// <summary>Enregistre si aucune erreur n'est trouvée. Renvoie les problèmes bloquants (vide si enregistré).</summary>
        public List<ValidationIssue> TryCommit()
        {
            var errors = Validate().Where(issue => issue.Severity == Severity.Error).ToList();
            if (errors.Count > 0)
                return errors;

            if (IsNew)
                Original = AssetFactory.SaveNew(Draft);
            else
                AssetFactory.SaveInto(Draft, Original);

            Draft.name = Original.name;
            snapshot = EditorJsonUtility.ToJson(Draft);
            ValidationCache.Invalidate();
            return errors;
        }

        /// <summary>Annule les modifications non validées.</summary>
        public void Revert()
        {
            if (IsNew)
                return;
            EditorUtility.CopySerialized(Original, Draft);
            Draft.hideFlags = HideFlags.DontSave;
            snapshot = EditorJsonUtility.ToJson(Draft);
        }

        public void Dispose()
        {
            if (Draft != null)
                Object.DestroyImmediate(Draft);
        }
    }
}
