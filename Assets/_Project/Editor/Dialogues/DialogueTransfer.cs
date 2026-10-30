using System.IO;
using TheDungeon.Data;
using UnityEditor;
using UnityEngine;

namespace TheDungeon.Editor.Dialogues
{
    /// <summary>
    /// Export et import d'une discussion complète (personnage, apparence, étages, répliques, réponses, conditions et effets)
    /// dans un fichier JSON. Les objets cités (portrait, objets donnés…) sont retrouvés par leur identifiant dans le projet.
    /// </summary>
    public static class DialogueTransfer
    {
        public const string Extension = "json";

        public static string ToJson(NpcData npc) => EditorJsonUtility.ToJson(npc, true);

        /// <summary>Personnage lu depuis le texte d'un export, ou null si ce n'est pas une discussion Forge.</summary>
        public static NpcData FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || !json.Contains("\"nodes\"") || !json.Contains("\"startNode\""))
                return null;
            var npc = ScriptableObject.CreateInstance<NpcData>();
            try
            {
                EditorJsonUtility.FromJsonOverwrite(json, npc);
                return npc;
            }
            catch (System.ArgumentException)
            {
                Object.DestroyImmediate(npc);
                return null;
            }
        }

        public static void Export(NpcData npc, string path) => File.WriteAllText(path, ToJson(npc));

        public static NpcData Import(string path) => File.Exists(path) ? FromJson(File.ReadAllText(path)) : null;
    }
}
