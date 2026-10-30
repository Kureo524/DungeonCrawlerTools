using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Dialogue;

namespace TheDungeon.Editor.Validation
{
    /// <summary>
    /// Cohérence des dialogues : liens vers des répliques existantes, textes renseignés, conditions et conséquences complètes,
    /// objets cités existants, marqueurs testés mais jamais activés, répliques inaccessibles.
    /// </summary>
    public sealed class DialogueRule : ValidationRule
    {
        public override string Name => "Dialogues";
        public override string Description =>
            "Liens valides, textes renseignés, conditions et effets complets, marqueurs cohérents, répliques accessibles.";

        public override IEnumerable<ValidationIssue> Run(ValidationContext context)
        {
            var limits = context.Limits;
            var setFlags = new HashSet<string>(context.Npcs.SelectMany(SetFlags));

            foreach (var npc in context.Npcs)
            {
                if (npc.Portrait == null)
                    yield return Warning("Le personnage n'a pas de portrait.", npc);
                var outside = FloorSet.Floors(npc.Floors).Where(f => f > limits.floorCount).ToList();
                if (outside.Count > 0)
                    yield return Error($"Étage {outside[0]} invalide : le donjon compte {limits.floorCount} étages.", npc);
                if (npc.Nodes.Count == 0)
                {
                    yield return Error("La conversation est vide.", npc);
                    continue;
                }
                if (npc.Nodes.Count > limits.maxDialogueNodes)
                    yield return Error($"Trop de répliques ({npc.Nodes.Count}, max {limits.maxDialogueNodes}).", npc);
                if (npc.FindNode(npc.StartNode) == null)
                    yield return Error("La conversation n'a pas de début valide.", npc);
                foreach (var duplicate in npc.Nodes.GroupBy(n => n.id).Where(g => g.Count() > 1))
                    yield return Error($"Plusieurs répliques portent l'identifiant #{duplicate.Key}.", npc);

                for (var e = 0; e < npc.Entries.Count; e++)
                {
                    var entry = npc.Entries[e];
                    if (npc.FindNode(entry.node) == null)
                        yield return Error($"Le début alternatif n°{e + 1} ne mène à aucune réplique.", npc);
                    if (entry.conditions.Count == 0)
                        yield return Warning($"Le début alternatif n°{e + 1} n'a pas de condition : il remplace toujours le début normal.", npc);
                    foreach (var issue in Conditions(entry.conditions, $"Début alternatif n°{e + 1}", npc, setFlags, limits, context.Progressions))
                        yield return issue;
                }

                foreach (var node in npc.Nodes)
                {
                    var where = $"Réplique #{node.id}";
                    if (string.IsNullOrWhiteSpace(node.text))
                        yield return Error($"{where} : le texte est vide.", npc);
                    else if (node.text.Length > limits.maxLineLength)
                        yield return Warning($"{where} : {node.text.Length} caractères, au-delà des {limits.maxLineLength} conseillés (la bulle débordera).", npc);
                    if (node.next >= 0 && npc.FindNode(node.next) == null)
                        yield return Error($"{where} : la suite mène à une réplique supprimée (#{node.next}).", npc);
                    if (node.next == node.id)
                        yield return Error($"{where} : la réplique mène à elle-même, la conversation tournerait en rond.", npc);
                    if (node.choices.Count > limits.maxChoicesPerNode)
                        yield return Error($"{where} : trop de réponses ({node.choices.Count}, max {limits.maxChoicesPerNode}).", npc);
                    if (node.choices.Count > 0 && node.choices.All(c => c.conditions.Count > 0))
                        yield return Warning($"{where} : toutes les réponses ont une condition ; si aucune n'est remplie, la conversation est bloquée.", npc);
                    foreach (var issue in Consequences(node.consequences, where, npc, limits))
                        yield return issue;

                    for (var c = 0; c < node.choices.Count; c++)
                    {
                        var choice = node.choices[c];
                        var choiceWhere = $"{where}, réponse {c + 1}";
                        if (string.IsNullOrWhiteSpace(choice.text))
                            yield return Error($"{choiceWhere} : le texte est vide.", npc);
                        if (choice.target >= 0 && npc.FindNode(choice.target) == null)
                            yield return Error($"{choiceWhere} : mène à une réplique supprimée (#{choice.target}).", npc);
                        foreach (var issue in Conditions(choice.conditions, choiceWhere, npc, setFlags, limits, context.Progressions))
                            yield return issue;
                        foreach (var issue in Consequences(choice.consequences, choiceWhere, npc, limits))
                            yield return issue;
                    }
                }

                var reachable = DialogueLogic.ReachableNodes(npc);
                foreach (var node in npc.Nodes.Where(n => !reachable.Contains(n.id)))
                    yield return Warning($"Réplique #{node.id} : rien ne permet d'y arriver depuis un début.", npc);
                if (!npc.Nodes.Any(n => n.choices.Count == 0 && n.next < 0) && !npc.Nodes.Any(n => n.choices.Any(c => c.target < 0)))
                    yield return Warning("La conversation n'a aucune fin : elle ne peut pas se terminer.", npc);
            }
        }

        static IEnumerable<ValidationIssue> Conditions(IEnumerable<DialogueCondition> conditions, string where, NpcData npc,
            HashSet<string> setFlags, Common.ForgeDesignLimits limits, IReadOnlyList<PlayerProgression> progressions)
        {
            foreach (var condition in conditions)
            {
                switch (condition.type)
                {
                    case ConditionType.HasItem:
                    case ConditionType.LacksItem:
                        if (condition.item == null)
                            yield return Error($"{where} : la condition ne précise pas l'objet (manquant ou supprimé).", npc);
                        break;
                    case ConditionType.FlagSet:
                    case ConditionType.FlagNotSet:
                        if (string.IsNullOrWhiteSpace(condition.flag))
                            yield return Error($"{where} : la condition ne précise pas le marqueur.", npc);
                        else if (condition.type == ConditionType.FlagSet && !setFlags.Contains(condition.flag))
                            yield return Warning($"{where} : le marqueur « {condition.flag} » n'est activé par aucun dialogue, la condition ne sera jamais remplie.", npc);
                        break;
                    case ConditionType.PlayerLevelAtLeast:
                    case ConditionType.PlayerLevelBelow:
                        if (condition.value < 1 || condition.value > limits.maxLevelCap)
                            yield return Error($"{where} : niveau {condition.value} hors limites (1 à {limits.maxLevelCap}).", npc);
                        break;
                    case ConditionType.FloorAtLeast:
                        if (condition.value < 1 || condition.value > limits.floorCount)
                            yield return Error($"{where} : étage {condition.value} hors limites (1 à {limits.floorCount}).", npc);
                        break;
                    case ConditionType.MechanicAtLeast:
                        var mechanic = string.IsNullOrEmpty(condition.flag) ? null
                            : progressions.Where(p => p != null).Select(p => p.FindMechanic(condition.flag)).FirstOrDefault(m => m != null);
                        if (mechanic == null)
                            yield return Error($"{where} : la condition vise une mécanique qui n'existe pas (ou plus) dans la progression.", npc);
                        else if (condition.value < 1 || condition.value > mechanic.MaxRank)
                            yield return Error($"{where} : niveau {condition.value} hors limites pour « {mechanic.displayName} » (1 à {mechanic.MaxRank}).", npc);
                        break;
                    case ConditionType.FloorIn:
                        if (!FloorSet.Floors(condition.value).Any(f => f <= limits.floorCount))
                            yield return Error($"{where} : la condition « Étages précis » ne coche aucun étage.", npc);
                        else if (!FloorSet.Floors(condition.value).Any(npc.AppearsOn))
                            yield return Warning($"{where} : le personnage n'apparaît sur aucun des étages de la condition ({FloorSet.Describe(condition.value)}), elle ne sera jamais remplie.", npc);
                        break;
                    case ConditionType.GoldAtLeast:
                        if (condition.value < 0)
                            yield return Error($"{where} : quantité d'or négative.", npc);
                        break;
                }
            }
        }

        static IEnumerable<ValidationIssue> Consequences(IEnumerable<DialogueConsequence> consequences, string where, NpcData npc,
            Common.ForgeDesignLimits limits)
        {
            foreach (var consequence in consequences)
            {
                switch (consequence.type)
                {
                    case ConsequenceType.GiveItem:
                    case ConsequenceType.TakeItem:
                        if (consequence.item == null)
                            yield return Error($"{where} : l'effet ne précise pas l'objet (manquant ou supprimé).", npc);
                        break;
                    case ConsequenceType.SetFlag:
                    case ConsequenceType.ClearFlag:
                        if (string.IsNullOrWhiteSpace(consequence.flag))
                            yield return Error($"{where} : l'effet ne précise pas le marqueur.", npc);
                        break;
                    default:
                        if (consequence.value <= 0)
                            yield return Error($"{where} : la valeur de l'effet doit être positive.", npc);
                        break;
                }
            }
        }

        static IEnumerable<string> SetFlags(NpcData npc) =>
            npc.Nodes.SelectMany(n => n.consequences.Concat(n.choices.SelectMany(c => c.consequences)))
                .Where(c => c.type == ConsequenceType.SetFlag && !string.IsNullOrWhiteSpace(c.flag))
                .Select(c => c.flag);
    }
}
