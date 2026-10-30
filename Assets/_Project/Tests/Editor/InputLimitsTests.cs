using NUnit.Framework;
using TheDungeon.Editor.Common;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Tests
{
    public class InputLimitsTests
    {
        static int LimitOf(VisualElement field)
        {
            // Le focus arrive sur la zone de saisie intérieure, pas sur le champ.
            var input = field.Q(className: "unity-base-text-field__input") ?? field;
            ForgeInputLimits.Apply(input);
            return field is TextInputBaseField<string> text ? text.maxLength : field.Q<TextField>().maxLength;
        }

        [Test]
        public void Fields_GetTheirMaximumLength()
        {
            Assert.AreEqual(ForgeInputLimits.Integer, ((IntegerField)Limit(new IntegerField())).maxLength);
            Assert.AreEqual(ForgeInputLimits.Decimal, ((FloatField)Limit(new FloatField())).maxLength);
            Assert.AreEqual(ForgeInputLimits.Text, LimitOf(new TextField()));
            Assert.AreEqual(-1, LimitOf(new TextField { multiline = true }));
            Assert.AreEqual(ForgeInputLimits.Integer, LimitOf(new SliderInt(0, 10) { showInputField = true }));
            Assert.AreEqual(ForgeInputLimits.Decimal, LimitOf(new Slider(0f, 10f) { showInputField = true }));
        }

        static VisualElement Limit(VisualElement field)
        {
            ForgeInputLimits.Apply(field.Q(className: "unity-base-text-field__input") ?? field);
            return field;
        }
    }
}
