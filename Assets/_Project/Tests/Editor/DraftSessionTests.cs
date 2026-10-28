using NUnit.Framework;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using UnityEditor;

namespace TheDungeon.Editor.Tests
{
    /// <summary>Vérifie qu'un asset n'est créé que si la validation passe.</summary>
    public class DraftSessionTests
    {
        const string TestName = "Zz Test Draft Item";
        ItemData created;

        [TearDown]
        public void TearDown()
        {
            if (created != null)
                AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(created));
        }

        [Test]
        public void InvalidDraft_IsNotCreated()
        {
            var session = DraftSession<ItemData>.CreateNew(TestName);
            var serialized = new SerializedObject(session.Draft);
            serialized.FindProperty("price").intValue = -5;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var errors = session.TryCommit();

            Assert.IsNotEmpty(errors);
            Assert.IsTrue(session.IsNew, "Aucun asset ne doit avoir été créé.");
            Assert.IsEmpty(AssetDatabase.FindAssets("ITM_ZzTestDraftItem"));
            session.Dispose();
        }

        [Test]
        public void ValidDraft_IsCreatedWithConventionalName()
        {
            var session = DraftSession<ItemData>.CreateNew(TestName);
            var serialized = new SerializedObject(session.Draft);
            serialized.FindProperty("price").intValue = ForgeDesignLimits.Instance.GetRarity(Rarity.Common).price.min;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var errors = session.TryCommit();
            created = session.Original;

            Assert.IsEmpty(errors);
            Assert.IsNotNull(created);
            Assert.AreEqual("ITM_ZzTestDraftItem", created.name);
            StringAssert.StartsWith(DungeonPaths.Items, AssetDatabase.GetAssetPath(created));
            session.Dispose();
        }
    }
}
