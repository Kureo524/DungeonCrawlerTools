using NUnit.Framework;
using TheDungeon.Data;
using TheDungeon.Editor.Common;

namespace TheDungeon.Editor.Tests
{
    public class NamingConventionTests
    {
        [TestCase("Épée longue +1", "EpeeLongue1")]
        [TestCase("  potion de soin ", "PotionDeSoin")]
        [TestCase("arc_court", "Arc_Court")]
        [TestCase("!!!", "")]
        public void ToPascalCase_ConvertsDesignerNames(string input, string expected)
        {
            Assert.AreEqual(expected, NamingConvention.ToPascalCase(input));
        }

        [Test]
        public void ExpectedName_UsesBossPrefixForBosses()
        {
            var boss = TestData.Create<EnemyData>("ENM_Liche", so =>
            {
                so.FindProperty("displayName").stringValue = "Liche";
                so.FindProperty("isBoss").boolValue = true;
            });

            Assert.AreEqual("BOSS_Liche", NamingConvention.GetExpectedName(boss));
            Assert.IsFalse(NamingConvention.IsValid(boss));
            TestData.Destroy(boss);
        }

        [Test]
        public void IsValid_AcceptsConventionalName()
        {
            var item = TestData.Create<ItemData>("ITM_EpeeCourte");
            Assert.IsTrue(NamingConvention.IsValid(item));
            TestData.Destroy(item);
        }
    }
}
