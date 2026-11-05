using NUnit.Framework;
using TheDungeon.Editor.Studio;

namespace TheDungeon.Editor.Tests
{
    /// <summary>Modifications non validées de Forge Studio : ordre, blocage par les erreurs et validation à la fermeture.</summary>
    public class UnsavedChangesTests
    {
        [Test]
        public void Entries_KeepTheirOrderAndSection()
        {
            var changes = new UnsavedChanges();
            object first = new(), second = new();
            changes.Set(first, true, null, false, "items");
            changes.Set(second, true, null, false, "enemies");
            changes.Set(first, true, null, false, "progression");

            Assert.AreEqual(2, changes.Count);
            Assert.AreEqual((first, "items"), changes.Entries[0], "La section retenue est celle où la modification a commencé.");
            changes.Set(first, false, null, false, "items");
            Assert.AreEqual(1, changes.Count);
            Assert.AreEqual(second, changes.Entries[0].owner);
        }

        [Test]
        public void ErrorsBlock_UntilTheyAreFixed()
        {
            var changes = new UnsavedChanges();
            var owner = new object();
            changes.Set(owner, true, () => true, true, "items");
            Assert.IsTrue(changes.IsBlocked(owner));
            changes.Set(owner, true, () => true, false, "items");
            Assert.AreEqual(0, changes.BlockedCount);
        }

        [Test]
        public void SaveAll_ForgetsChangesOnlyWhenEverythingIsSaved()
        {
            var changes = new UnsavedChanges();
            var saved = 0;
            changes.Set(new object(), true, () => ++saved > 0, false, "items");
            changes.Set(new object(), true, () => false, false, "enemies");

            Assert.AreEqual(1, changes.CommitAll());
            Assert.IsFalse(changes.SaveAll());
            Assert.AreEqual(2, changes.Count, "Une validation a échoué : rien n'est oublié.");

            changes.Set(changes.Entries[1].owner, false, null, false, null);
            Assert.IsTrue(changes.SaveAll());
            Assert.AreEqual(0, changes.Count);
        }
    }
}
