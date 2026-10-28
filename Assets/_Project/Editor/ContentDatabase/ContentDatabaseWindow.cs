using System.Collections.Generic;
using TheDungeon.Editor.Common;
using UnityEditor;

namespace TheDungeon.Editor.ContentDatabase
{
    /// <summary>Fenêtre unique pour créer et modifier objets, ennemis, tables de butin et progression du joueur.</summary>
    public class ContentDatabaseWindow : ForgeToolWindow
    {
        [MenuItem("Tools/Content Database", priority = 0)]
        static void Open() => GetWindow<ContentDatabaseWindow>("Content Database");

        protected override string Subtitle => "Créez et modifiez le contenu du jeu : objets, ennemis, butin et progression du joueur.";
        protected override string HelpFile => "ContentDatabase";

        protected override IEnumerable<ForgeTab> CreateTabs()
        {
            yield return new ForgeTab("Objets", "Les 40 objets du jeu : armes, armures, consommables…", () => new ItemsTab().Build());
            yield return new ForgeTab("Ennemis", "Les ennemis et les boss, avec leurs stats par étage.", () => new EnemiesTab().Build());
            yield return new ForgeTab("Tables de butin", "Ce que lâchent les ennemis et les coffres.", () => new LootTablesTab().Build());
            yield return new ForgeTab("Progression", "PV, dégâts et XP du joueur pour chaque niveau.", () => new ProgressionTab().Build());
        }
    }
}
