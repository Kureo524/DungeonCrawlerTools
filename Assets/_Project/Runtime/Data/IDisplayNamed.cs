namespace TheDungeon.Data
{
    /// <summary>Donnée qui porte un nom lisible, utilisé par les outils et pour nommer le fichier.</summary>
    public interface IDisplayNamed
    {
        string DisplayName { get; }
    }
}
