using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TheDungeon.Editor.Common
{
    /// <summary>Enregistrement, renommage et suppression des données, toujours rangées et nommées selon la convention.</summary>
    public static class AssetFactory
    {
        /// <summary>Enregistre une copie de travail validée comme nouvel asset, dans le dossier de son type.</summary>
        public static T SaveNew<T>(T draft) where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            EditorUtility.CopySerialized(draft, asset);
            asset.hideFlags = HideFlags.None;
            asset.name = NamingConvention.GetExpectedName(asset);

            var folder = DungeonPaths.FolderFor(typeof(T));
            EnsureFolder(folder);
            AssetDatabase.CreateAsset(asset, AssetDatabase.GenerateUniqueAssetPath($"{folder}/{asset.name}.asset"));
            AssetDatabase.SaveAssets();
            return asset;
        }

        /// <summary>Recopie une copie de travail validée dans l'asset d'origine (annulable avec Ctrl+Z).</summary>
        public static void SaveInto<T>(T draft, T original) where T : ScriptableObject
        {
            Undo.RecordObject(original, $"Modifier {original.name}");
            var fileName = original.name;
            EditorUtility.CopySerialized(draft, original);
            original.name = fileName;
            original.hideFlags = HideFlags.None;
            EditorUtility.SetDirty(original);
            AssetDatabase.SaveAssetIfDirty(original);
            ApplyNamingConvention(original);
        }

        /// <summary>Renomme le fichier pour respecter la convention. Renvoie un message d'erreur, ou null si tout va bien.</summary>
        public static string ApplyNamingConvention(Object asset)
        {
            var expected = NamingConvention.GetExpectedName(asset);
            if (asset.name == expected)
                return null;

            var path = AssetDatabase.GetAssetPath(asset);
            var target = AssetDatabase.GenerateUniqueAssetPath($"{Path.GetDirectoryName(path)}/{expected}.asset");
            return AssetDatabase.RenameAsset(path, Path.GetFileNameWithoutExtension(target)) is { Length: > 0 } error ? error : null;
        }

        /// <summary>Supprime (vers la corbeille) après confirmation, en signalant les données qui utilisent encore l'asset.</summary>
        public static bool DeleteWithConfirmation(Object asset)
        {
            var users = FindUsers(asset).ToList();
            var message = $"Supprimer « {asset.name} » ?\nL'asset est envoyé dans la corbeille du système.";
            if (users.Count > 0)
                message += $"\n\nAttention, il est encore utilisé par :\n- {string.Join("\n- ", users.Take(8).Select(u => u.name))}"
                           + (users.Count > 8 ? $"\n… et {users.Count - 8} autre(s)" : string.Empty)
                           + "\nCes données deviendront invalides.";

            if (!EditorUtility.DisplayDialog("Supprimer", message, "Supprimer", "Annuler"))
                return false;
            return AssetDatabase.MoveAssetToTrash(AssetDatabase.GetAssetPath(asset));
        }

        /// <summary>Données du jeu qui référencent directement l'asset donné.</summary>
        public static IEnumerable<Object> FindUsers(Object asset)
        {
            var targetPath = AssetDatabase.GetAssetPath(asset);
            foreach (var guid in AssetDatabase.FindAssets("t:ScriptableObject", new[] { DungeonPaths.Data }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path != targetPath && AssetDatabase.GetDependencies(path, false).Contains(targetPath))
                    yield return AssetDatabase.LoadMainAssetAtPath(path);
            }
        }

        public static List<T> LoadAll<T>() where T : Object
        {
            if (!AssetDatabase.IsValidFolder(DungeonPaths.Data))
                return new List<T>();
            return AssetDatabase.FindAssets($"t:{typeof(T).Name}", new[] { DungeonPaths.Data })
                .Select(guid => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(asset => asset != null)
                .OrderBy(asset => asset.name)
                .ToList();
        }

        public static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
                return;
            var parent = Path.GetDirectoryName(folder)!.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
