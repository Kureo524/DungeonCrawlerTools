using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Common
{
    /// <summary>Édition des limites de conception (réservée aux développeurs).</summary>
    public class SettingsWindow : ForgeToolWindow
    {
        [MenuItem("Tools/Settings (dev)", priority = 100)]
        static void OpenWindow() => Open<SettingsWindow>("Forge Settings", 700, 700);

        protected override string Subtitle => "Limites imposées aux outils de conception. Réservé aux développeurs.";
        protected override string HelpFile => "Settings";

        protected override IEnumerable<ForgeTab> CreateTabs()
        {
            yield return new ForgeTab("Limites", "Bornes appliquées par tous les outils et vérifiées par le Validator.", BuildLimits);
        }

        VisualElement BuildLimits()
        {
            var scroll = new ScrollView();
            scroll.Add(new HelpBox(
                "Ces valeurs encadrent le travail des designers : les champs des outils sont bridés à ces bornes. " +
                "Modifiez-les seulement en accord avec l'équipe.", HelpBoxMessageType.Warning));
            scroll.Add(new InspectorElement(Limits));
            return scroll;
        }
    }
}
