using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TheDungeon.Editor.Tests
{
    /// <summary>Crée des données en mémoire (sans fichier) et renseigne leurs champs privés comme le ferait l'éditeur.</summary>
    public static class TestData
    {
        public static T Create<T>(string name, Action<SerializedObject> setup = null) where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            asset.name = name;
            if (setup != null)
            {
                var serialized = new SerializedObject(asset);
                setup(serialized);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            return asset;
        }

        public static void Destroy(params Object[] assets)
        {
            foreach (var asset in assets)
                Object.DestroyImmediate(asset);
        }
    }
}
