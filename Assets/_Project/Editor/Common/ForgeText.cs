namespace TheDungeon.Editor.Common
{
    /// <summary>
    /// Conventions d'écriture des messages système, reprises du roman <i>Omniscient Reader's Viewpoint</i> :
    /// le message est entre crochets, les noms propres entre apostrophes simples, une donnée inconnue est caviardée (■■■).
    /// </summary>
    public static class ForgeText
    {
        public const string Redacted = "■■■";

        /// <summary>« Progression « Standard » validée. » devient « [Progression 'Standard' validée.] ».</summary>
        public static string System(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return $"[{Redacted}]";
            var text = message.Trim().Replace("« ", "'").Replace(" »", "'").Replace("«", "'").Replace("»", "'");
            return text.StartsWith("[") && text.EndsWith("]") ? text : $"[{text}]";
        }

        /// <summary>Titre d'une fenêtre de statut : « [Informations du personnage] ».</summary>
        public static string Window(string title) => $"[{title}]";

        /// <summary>Nom affiché, ou caviardé s'il est vide.</summary>
        public static string NameOrRedacted(string name) => string.IsNullOrWhiteSpace(name) ? Redacted : name;
    }
}
