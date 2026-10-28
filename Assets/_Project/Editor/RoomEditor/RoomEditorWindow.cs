using System.Collections.Generic;
using TheDungeon.Editor.Common;
using UnityEditor;

namespace TheDungeon.Editor.RoomEditor
{
    public class RoomEditorWindow : ForgeToolWindow
    {
        [MenuItem("Tools/Room Editor", priority = 11)]
        static void OpenWindow() => Open<RoomEditorWindow>("Room Editor", 1400, 860);

        protected override string Subtitle => "Dessinez les salles sur une grille avec la palette de leur biome : terrain, entrées, ennemis, coffres et décors.";
        protected override string HelpFile => "RoomEditor";

        protected override IEnumerable<ForgeTab> CreateTabs()
        {
            yield return new ForgeTab("Salles", "Toutes les salles du donjon.", () => new RoomsTab(this).Build());
        }
    }
}
