using System.Collections.Generic;
using TheDungeon.Editor.Common;
using UnityEditor;

namespace TheDungeon.Editor.Biomes
{
    public class BiomePresetsWindow : ForgeToolWindow
    {
        [MenuItem("Tools/Biome Presets", priority = 10)]
        static void OpenWindow() => Open<BiomePresetsWindow>("Biome Presets");

        protected override string Subtitle => "Définissez chaque biome : visuels, ennemis, décors et butin. Les salles s'appuient sur ces presets.";
        protected override string HelpFile => "BiomePresets";

        protected override IEnumerable<ForgeTab> CreateTabs()
        {
            yield return new ForgeTab("Biomes", "Liste des presets de biome.", () => new BiomesTab(this).Build());
        }
    }
}
