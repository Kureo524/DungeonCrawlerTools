using TheDungeon.Data;

namespace TheDungeon.Editor.Dialogues
{
    /// <summary>Formulations en français des conditions et conséquences, pour les étiquettes, l'aperçu et la validation.</summary>
    public static class DialogueText
    {
        public static string Describe(DialogueCondition condition) => condition.type switch
        {
            ConditionType.PlayerLevelAtLeast => $"Niveau ≥ {condition.value}",
            ConditionType.PlayerLevelBelow => $"Niveau < {condition.value}",
            ConditionType.HasItem => $"Possède {ItemName(condition.item)}",
            ConditionType.LacksItem => $"Ne possède pas {ItemName(condition.item)}",
            ConditionType.FlagSet => $"Si '{Flag(condition.flag)}'",
            ConditionType.FlagNotSet => $"Si pas '{Flag(condition.flag)}'",
            ConditionType.GoldAtLeast => $"Or ≥ {condition.value}",
            ConditionType.FloorAtLeast => $"Étage ≥ {condition.value}",
            ConditionType.FloorIn => FloorSet.Describe(condition.value),
            ConditionType.MechanicAtLeast => $"{MechanicName(condition.flag)} Lv.{condition.value}+",
            _ => condition.type.ToString()
        };

        public static string Describe(DialogueConsequence consequence) => consequence.type switch
        {
            ConsequenceType.SetFlag => $"Active '{Flag(consequence.flag)}'",
            ConsequenceType.ClearFlag => $"Désactive '{Flag(consequence.flag)}'",
            ConsequenceType.GiveItem => $"Donne {ItemName(consequence.item)}",
            ConsequenceType.TakeItem => $"Reprend {ItemName(consequence.item)}",
            ConsequenceType.GiveGold => $"+{consequence.value} or",
            ConsequenceType.TakeGold => $"−{consequence.value} or",
            ConsequenceType.GiveXp => $"+{consequence.value} XP",
            ConsequenceType.Heal => $"Soigne {consequence.value} PV",
            _ => consequence.type.ToString()
        };

        public static string Label(ConditionType type) => type switch
        {
            ConditionType.PlayerLevelAtLeast => "Niveau minimum",
            ConditionType.PlayerLevelBelow => "Niveau inférieur à",
            ConditionType.HasItem => "Possède l'objet",
            ConditionType.LacksItem => "Ne possède pas l'objet",
            ConditionType.FlagSet => "Marqueur actif",
            ConditionType.FlagNotSet => "Marqueur inactif",
            ConditionType.GoldAtLeast => "Or minimum",
            ConditionType.FloorAtLeast => "Étage minimum",
            ConditionType.FloorIn => "Étages précis",
            ConditionType.MechanicAtLeast => "Mécanique (niveau min.)",
            _ => type.ToString()
        };

        public static string Label(ConsequenceType type) => type switch
        {
            ConsequenceType.SetFlag => "Activer un marqueur",
            ConsequenceType.ClearFlag => "Désactiver un marqueur",
            ConsequenceType.GiveItem => "Donner un objet",
            ConsequenceType.TakeItem => "Reprendre un objet",
            ConsequenceType.GiveGold => "Donner de l'or",
            ConsequenceType.TakeGold => "Prendre de l'or",
            ConsequenceType.GiveXp => "Donner de l'XP",
            ConsequenceType.Heal => "Soigner",
            _ => type.ToString()
        };

        public static bool UsesValue(ConditionType type) =>
            type is ConditionType.PlayerLevelAtLeast or ConditionType.PlayerLevelBelow or ConditionType.GoldAtLeast or ConditionType.FloorAtLeast;

        public static bool UsesFloors(ConditionType type) => type == ConditionType.FloorIn;
        public static bool UsesMechanic(ConditionType type) => type == ConditionType.MechanicAtLeast;

        /// <summary>Mécanique de la progression utilisée en jeu (outil Progression), ou null.</summary>
        public static CharacterMechanic FindMechanic(string id) =>
            string.IsNullOrEmpty(id) ? null : TheDungeon.Editor.Play.GameLauncher.ActiveProgression?.FindMechanic(id);

        /// <summary>« 'Esquive' », ou caviardé si la mécanique n'existe pas (ou plus).</summary>
        public static string MechanicName(string id) => FindMechanic(id) is { } mechanic ? $"'{mechanic.displayName}'" : $"'{Common.ForgeText.Redacted}'";
        public static bool UsesFlag(ConditionType type) => type is ConditionType.FlagSet or ConditionType.FlagNotSet;
        public static bool UsesItem(ConditionType type) => type is ConditionType.HasItem or ConditionType.LacksItem;

        public static bool UsesValue(ConsequenceType type) =>
            type is ConsequenceType.GiveGold or ConsequenceType.TakeGold or ConsequenceType.GiveXp or ConsequenceType.Heal;

        public static bool UsesFlag(ConsequenceType type) => type is ConsequenceType.SetFlag or ConsequenceType.ClearFlag;
        public static bool UsesItem(ConsequenceType type) => type is ConsequenceType.GiveItem or ConsequenceType.TakeItem;

        public static string Speaker(DialogueSpeaker speaker, NpcData npc) => speaker switch
        {
            DialogueSpeaker.Player => "Joueur",
            DialogueSpeaker.Narrator => "Narration",
            _ => npc != null && !string.IsNullOrWhiteSpace(npc.DisplayName) ? npc.DisplayName : "PNJ"
        };

        public static string NodeRef(int id) => id < 0 ? "Fin" : $"#{id}";

        static string ItemName(ItemData item) => item != null ? $"'{item.DisplayName}'" : $"'{Common.ForgeText.Redacted}'";
        static string Flag(string flag) => string.IsNullOrWhiteSpace(flag) ? Common.ForgeText.Redacted : flag;
    }
}
