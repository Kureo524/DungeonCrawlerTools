namespace TheDungeon.Editor.Common
{
    /// <summary>
    /// Conventions d'écriture des messages système, reprises du roman <i>Omniscient Reader's Viewpoint</i> :
    /// le message est entre crochets, les noms propres entre apostrophes simples, une donnée inconnue est caviardée (■■■),
    /// et un passage cité (une réplique) est entre demi-crochets 「 」, comme les extraits du roman dans le roman.
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

        /// <summary>Titre de fenêtre de statut en texte riche : les crochets prennent la couleur des messages système.</summary>
        public static string WindowRich(string title) => $"<color={SystemColor}>[</color>{title}<color={SystemColor}>]</color>";

        /// <summary>Couleur des signes système (crochets), celle du texte d'accent du thème.</summary>
        public const string SystemColor = "#a9c4f0";

        /// <summary>Passage cité, entre demi-crochets : 「Bonjour, voyageur.」 (caviardé s'il est vide).</summary>
        public static string Quote(string text) => $"「{(string.IsNullOrWhiteSpace(text) ? Redacted : text.Trim())}」";

        /// <summary>Nom propre écrit comme dans le roman : 'Benoit le bon'.</summary>
        public static string Name(string name) => $"'{NameOrRedacted(name)}'";

        /// <summary>Nom affiché, ou caviardé s'il est vide.</summary>
        public static string NameOrRedacted(string name) => string.IsNullOrWhiteSpace(name) ? Redacted : name;
    }
}
