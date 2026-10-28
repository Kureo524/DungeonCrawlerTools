using System.Collections.Generic;
using TheDungeon.Data;
using UnityEditor;

namespace TheDungeon.Editor.Dialogues
{
    /// <summary>
    /// Modifications de l'arbre de dialogue via SerializedObject (annulables avec Ctrl+Z).
    /// Les identifiants de répliques sont attribués automatiquement et les liens sont réparés à chaque suppression.
    /// </summary>
    public static class DialogueEditing
    {
        public const int End = -1;

        public static SerializedProperty Nodes(SerializedObject npc) => npc.FindProperty("nodes");

        public static int IndexOf(SerializedObject npc, int id)
        {
            var nodes = Nodes(npc);
            for (var i = 0; i < nodes.arraySize; i++)
                if (nodes.GetArrayElementAtIndex(i).FindPropertyRelative("id").intValue == id)
                    return i;
            return -1;
        }

        public static SerializedProperty Node(SerializedObject npc, int id)
        {
            var index = IndexOf(npc, id);
            return index < 0 ? null : Nodes(npc).GetArrayElementAtIndex(index);
        }

        /// <summary>Ajoute une réplique et renvoie son identifiant. La première réplique devient le début de la conversation.</summary>
        public static int AddNode(SerializedObject npc, DialogueSpeaker speaker, string text)
        {
            var nextId = npc.FindProperty("nextId");
            var id = nextId.intValue;
            nextId.intValue++;

            var nodes = Nodes(npc);
            nodes.arraySize++;
            var node = nodes.GetArrayElementAtIndex(nodes.arraySize - 1);
            // Un élément ajouté recopie le précédent : on remet tout à zéro.
            node.FindPropertyRelative("id").intValue = id;
            node.FindPropertyRelative("speaker").enumValueIndex = (int)speaker;
            node.FindPropertyRelative("text").stringValue = text;
            node.FindPropertyRelative("next").intValue = End;
            node.FindPropertyRelative("choices").arraySize = 0;
            node.FindPropertyRelative("consequences").arraySize = 0;

            var start = npc.FindProperty("startNode");
            if (start.intValue < 0 || IndexOf(npc, start.intValue) < 0)
                start.intValue = id;
            return id;
        }

        /// <summary>Supprime une réplique ; tout ce qui y menait mène désormais à la fin de la conversation.</summary>
        public static void DeleteNode(SerializedObject npc, int id)
        {
            var index = IndexOf(npc, id);
            if (index < 0)
                return;
            var nodes = Nodes(npc);
            nodes.DeleteArrayElementAtIndex(index);

            for (var i = 0; i < nodes.arraySize; i++)
            {
                var node = nodes.GetArrayElementAtIndex(i);
                var next = node.FindPropertyRelative("next");
                if (next.intValue == id)
                    next.intValue = End;
                var choices = node.FindPropertyRelative("choices");
                for (var c = 0; c < choices.arraySize; c++)
                {
                    var target = choices.GetArrayElementAtIndex(c).FindPropertyRelative("target");
                    if (target.intValue == id)
                        target.intValue = End;
                }
            }

            var entries = npc.FindProperty("entries");
            for (var i = 0; i < entries.arraySize; i++)
            {
                var node = entries.GetArrayElementAtIndex(i).FindPropertyRelative("node");
                if (node.intValue == id)
                    node.intValue = End;
            }

            var start = npc.FindProperty("startNode");
            if (start.intValue == id)
                start.intValue = nodes.arraySize > 0 ? nodes.GetArrayElementAtIndex(0).FindPropertyRelative("id").intValue : End;
        }

        /// <summary>Ajoute une réponse du joueur à la réplique. Renvoie son index, ou -1 si le maximum est atteint.</summary>
        public static int AddChoice(SerializedObject npc, int nodeId, string text, int target, int maxChoices)
        {
            var node = Node(npc, nodeId);
            if (node == null)
                return -1;
            var choices = node.FindPropertyRelative("choices");
            if (choices.arraySize >= maxChoices)
                return -1;
            choices.arraySize++;
            var choice = choices.GetArrayElementAtIndex(choices.arraySize - 1);
            choice.FindPropertyRelative("text").stringValue = text;
            choice.FindPropertyRelative("target").intValue = target;
            choice.FindPropertyRelative("conditions").arraySize = 0;
            choice.FindPropertyRelative("consequences").arraySize = 0;
            // Une réplique avec des choix n'a plus de suite directe : ce sont les choix qui mènent à la suite.
            node.FindPropertyRelative("next").intValue = End;
            return choices.arraySize - 1;
        }

        /// <summary>
        /// Saisie « messagerie » : relie la nouvelle réplique à la suite logique de la réplique sélectionnée.
        /// Priorité à la dernière réponse du joueur qui ne mène encore nulle part, sinon à la suite directe.
        /// Renvoie false si rien n'a pu être relié (la réplique reste isolée).
        /// </summary>
        public static bool LinkAfter(SerializedObject npc, int fromId, int newId)
        {
            var node = Node(npc, fromId);
            if (node == null)
                return false;
            var choices = node.FindPropertyRelative("choices");
            for (var c = choices.arraySize - 1; c >= 0; c--)
            {
                var target = choices.GetArrayElementAtIndex(c).FindPropertyRelative("target");
                if (target.intValue != End)
                    continue;
                target.intValue = newId;
                return true;
            }
            var next = node.FindPropertyRelative("next");
            if (choices.arraySize == 0 && next.intValue == End)
            {
                next.intValue = newId;
                return true;
            }
            return false;
        }

        public static void SetStart(SerializedObject npc, int id) => npc.FindProperty("startNode").intValue = id;

        /// <summary>Choix « Réplique suivante » : la fin, chaque réplique existante, et l'identifiant correspondant.</summary>
        public static (List<string> labels, List<int> ids) TargetChoices(NpcData npc, int excludeId)
        {
            var labels = new List<string> { "Fin de la conversation" };
            var ids = new List<int> { End };
            foreach (var node in npc.Nodes)
            {
                if (node.id == excludeId)
                    continue;
                labels.Add($"#{node.id}  {Excerpt(node.text, 40)}");
                ids.Add(node.id);
            }
            return (labels, ids);
        }

        public static string Excerpt(string text, int length)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "(vide)";
            text = text.Replace('\n', ' ');
            return text.Length <= length ? text : text.Substring(0, length - 1) + "…";
        }
    }
}
