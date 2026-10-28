using TheDungeon.Editor.Common;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Studio
{
    /// <summary>Édition des limites de conception et des objectifs de production (réservée aux développeurs).</summary>
    public static class SettingsView
    {
        public static VisualElement Build()
        {
            var scroll = new ScrollView();
            scroll.AddToClassList("forge-details");
            var warning = new HelpBox(
                "Ces valeurs encadrent le travail des designers : les champs des outils sont bridés à ces bornes, " +
                "et le Validator s'en sert comme référence. Modifiez-les seulement en accord avec l'équipe.", HelpBoxMessageType.Warning);
            scroll.Add(warning);
            var card = new VisualElement();
            card.AddToClassList("forge-card");
            card.Add(new InspectorElement(ForgeDesignLimits.Instance));
            scroll.Add(card);
            return scroll;
        }
    }
}
