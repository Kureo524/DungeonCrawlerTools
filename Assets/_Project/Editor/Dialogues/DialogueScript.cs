using System.Linq;
using System.Text;
using TheDungeon.Data;
using TheDungeon.Dialogue;

namespace TheDungeon.Editor.Dialogues
{
    /// <summary>
    /// Version texte d'une conversation (Markdown), pour la relecture hors d'Unity : chaque réplique avec son numéro,
    /// les réponses du joueur, leurs conditions, leurs effets et leur suite.
    /// </summary>
    public static class DialogueScript
    {
        public static string Build(NpcData npc)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"# {npc.DisplayName}" + (string.IsNullOrWhiteSpace(npc.Role) ? string.Empty : $" — {npc.Role}"));
            builder.AppendLine($"{FloorSet.Describe(npc.Floors)} · {npc.LineCount} répliques · début : #{npc.StartNode}");
            builder.AppendLine();

            foreach (var entry in npc.Entries)
                builder.AppendLine($"> Début alternatif → #{entry.node} si {string.Join(" et ", entry.conditions.Select(DialogueText.Describe))}");
            if (npc.Entries.Count > 0)
                builder.AppendLine();

            var reachable = DialogueLogic.ReachableNodes(npc);
            foreach (var node in npc.Nodes.OrderBy(n => reachable.Contains(n.id) ? 0 : 1).ThenBy(n => n.id))
            {
                var speaker = node.speaker == DialogueSpeaker.Player ? "Joueur" : DialogueText.Speaker(node.speaker, npc);
                builder.AppendLine($"**#{node.id} {speaker}** : {node.text}" + (reachable.Contains(node.id) ? string.Empty : "  *(inaccessible)*"));
                foreach (var consequence in node.consequences)
                    builder.AppendLine($"  - *Effet : {DialogueText.Describe(consequence)}*");
                foreach (var choice in node.choices)
                {
                    var conditions = choice.conditions.Count == 0 ? string.Empty : $" [si {string.Join(" et ", choice.conditions.Select(DialogueText.Describe))}]";
                    var effects = choice.consequences.Count == 0 ? string.Empty : $" {{{string.Join(", ", choice.consequences.Select(DialogueText.Describe))}}}";
                    builder.AppendLine($"  - Joueur : « {choice.text} »{conditions}{effects} → {DialogueText.NodeRef(choice.target)}");
                }
                if (node.choices.Count == 0)
                    builder.AppendLine($"  - → {DialogueText.NodeRef(node.next)}");
                builder.AppendLine();
            }
            return builder.ToString();
        }
    }
}
