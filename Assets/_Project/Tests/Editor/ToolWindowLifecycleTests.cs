using System.Collections;
using System.Linq;
using NUnit.Framework;
using TheDungeon.Editor.Studio;
using UnityEditor;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Tests
{
    /// <summary>
    /// Ouvre la vraie fenêtre Forge Studio. Régression : ancrer ou déplacer une fenêtre détache puis rattache
    /// son interface ; les boutons doivent continuer à fonctionner ensuite.
    /// </summary>
    public class ToolWindowLifecycleTests
    {
        [UnityTest]
        public IEnumerator Biomes_SurvivesDocking() => DockThenAddRows(ForgeStudioWindow.Biomes);

        [UnityTest]
        public IEnumerator Items_SurvivesDocking() => DockThenAddRows(ForgeStudioWindow.Items);

        [UnityTest]
        public IEnumerator EverySection_Opens()
        {
            var window = ForgeStudioWindow.Open(ForgeStudioWindow.Home);
            try
            {
                foreach (var id in new[] { ForgeStudioWindow.Home, ForgeStudioWindow.Items, ForgeStudioWindow.Enemies, ForgeStudioWindow.LootTables,
                             ForgeStudioWindow.Progression, ForgeStudioWindow.Biomes, ForgeStudioWindow.Rooms, ForgeStudioWindow.Settings })
                {
                    Step($"section {id}");
                    window.ShowSection(id);
                    yield return Frames(3);
                    Assert.IsNotNull(window.ActiveContent, id);
                }
            }
            finally
            {
                window.DiscardAllUnsaved();
                window.Close();
            }
        }

        static IEnumerator DockThenAddRows(string section)
        {
            Step("ouverture");
            var window = ForgeStudioWindow.Open(section);
            try
            {
                yield return Frames(5);
                Step("clic Nouveau");
                Click(window.ActiveContent, "+ Nouveau");
                yield return Frames(5);
                Step("ancrage simulé");

                // Ancrage simulé : l'interface est détachée puis rattachée sans être reconstruite.
                var root = window.rootVisualElement;
                var child = root[0];
                child.RemoveFromHierarchy();
                root.Insert(0, child);
                var content = window.ActiveContent;
                var parent = content.parent;
                content.RemoveFromHierarchy();
                parent.Add(content);
                yield return Frames(5);

                Step("clic Ajouter");
                Click(window.ActiveContent, "+ Ajouter");
                yield return Frames(5);
                Step("fin");
            }
            finally
            {
                // Abandonne la copie de travail sans boîte de dialogue.
                window.DiscardAllUnsaved();
                window.Close();
            }
        }

        [SetUp]
        public void LogTestName() => UnityEngine.Debug.Log($"[ForgeTest] début {TestContext.CurrentContext.Test.Name}");

        static void Step(string message) => UnityEngine.Debug.Log($"[ForgeTest] {message}");

        static IEnumerator Frames(int count)
        {
            for (var i = 0; i < count; i++)
                yield return null;
        }

        static void Click(VisualElement root, string text)
        {
            var buttons = root.Query<Button>().Where(b => b.text == text && b.enabledInHierarchy).ToList();
            Assert.IsNotEmpty(buttons, $"Aucun bouton « {text} ».");
            var field = typeof(Clickable).GetField("clicked", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            foreach (var button in buttons)
                ((System.Action)field!.GetValue(button.clickable))?.Invoke();
        }
    }
}
