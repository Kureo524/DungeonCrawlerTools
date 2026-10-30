using UnityEngine.UIElements;

namespace TheDungeon.Editor.Common
{
    /// <summary>
    /// Longueur maximale de saisie dans tous les champs des outils : un nombre ou un nom démesuré ne peut plus
    /// élargir un champ au point de pousser l'interface hors de l'écran. Les textes sur plusieurs lignes
    /// (descriptions, répliques) passent à la ligne au lieu de s'élargir : ils ne sont pas limités.
    /// </summary>
    public static class ForgeInputLimits
    {
        public const int Integer = 20;
        public const int Decimal = 25;
        public const int Text = 50;

        /// <summary>La limite est posée quand un champ prend le focus : elle couvre aussi les champs créés plus tard.</summary>
        public static void Attach(VisualElement root) =>
            root.RegisterCallback<FocusInEvent>(evt => Apply(evt.target as VisualElement), TrickleDown.TrickleDown);

        /// <summary>Pose la limite du champ qui contient <paramref name="target"/> (la zone de saisie qui reçoit le focus).</summary>
        public static void Apply(VisualElement target)
        {
            TextField sliderInput = null;
            for (var element = target; element != null; element = element.parent)
            {
                switch (element)
                {
                    // La case de saisie d'un curseur est un champ texte : la limite dépend du curseur qui la contient.
                    case TextField field when field.ClassListContains("unity-base-slider__text-field"):
                        sliderInput = field;
                        break;
                    case BaseSlider<int>:
                        if (sliderInput != null)
                            sliderInput.maxLength = Integer;
                        return;
                    case BaseSlider<float>:
                        if (sliderInput != null)
                            sliderInput.maxLength = Decimal;
                        return;
                    case TextInputBaseField<int> field:
                        field.maxLength = Integer;
                        return;
                    case TextInputBaseField<long> field:
                        field.maxLength = Integer;
                        return;
                    case TextInputBaseField<float> field:
                        field.maxLength = Decimal;
                        return;
                    case TextInputBaseField<double> field:
                        field.maxLength = Decimal;
                        return;
                    case TextField field when sliderInput == null:
                        if (!field.multiline)
                            field.maxLength = Text;
                        return;
                }
            }
        }
    }
}
