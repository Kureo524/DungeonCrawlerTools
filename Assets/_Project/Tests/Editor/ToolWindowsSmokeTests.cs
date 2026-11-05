using System.Linq;
using NUnit.Framework;
using TheDungeon.Editor.Biomes;
using TheDungeon.Editor.Common;
using TheDungeon.Editor.ContentDatabase;
using TheDungeon.Editor.Progression;
using TheDungeon.Editor.RoomEditor;
using TheDungeon.Editor.Studio;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Tests
{
    /// <summary>Construit l'interface de chaque outil pour détecter les exceptions sans ouvrir de fenêtre.</summary>
    public class ToolWindowsSmokeTests
    {
        static void AssertBuilds(System.Func<ForgeStudioWindow, VisualElement> build)
        {
            var window = ScriptableObject.CreateInstance<ForgeStudioWindow>();
            try
            {
                var root = build(window);
                Assert.IsNotNull(root);
                Assert.Greater(root.childCount, 0);
            }
            finally
            {
                // Sans cela, Unity ouvrirait sa boîte « modifications non enregistrées » et bloquerait le test.
                window.DiscardAllUnsaved();
                Object.DestroyImmediate(window);
            }
        }

        [Test] public void ItemsTab_Builds() => AssertBuilds(w => new ItemsTab(w).Build());
        [Test] public void EnemiesTab_Builds() => AssertBuilds(w => new EnemiesTab(w).Build());
        [Test] public void LootTablesTab_Builds() => AssertBuilds(w => new LootTablesTab(w).Build());
        [Test] public void ProgressionTab_Builds() => AssertBuilds(w => new ProgressionTab(w).Build());
        [Test] public void BiomesTab_Builds() => AssertBuilds(w => new BiomesTab(w).Build());
        [Test] public void RoomsTab_Builds() => AssertBuilds(w => new RoomsTab(w).Build());
        [Test] public void FloorsTab_Builds() => AssertBuilds(w => new TheDungeon.Editor.Floors.FloorsTab(w).Build());
        [Test] public void DialoguesTab_Builds() => AssertBuilds(w => new TheDungeon.Editor.Dialogues.DialoguesTab(w).Build());

        /// <summary>Personnage réel (créé puis supprimé) : les trois vues se construisent et la barre de saisie ajoute une réplique.</summary>
        [Test]
        public void Dialogues_WithRealData_Builds()
        {
            var created = new System.Collections.Generic.List<Object>();
            try
            {
                Commit<TheDungeon.Data.NpcData>("Zz Smoke Npc", so =>
                {
                    var first = TheDungeon.Editor.Dialogues.DialogueEditing.AddNode(so, TheDungeon.Data.DialogueSpeaker.Npc, "Bonjour.");
                    TheDungeon.Editor.Dialogues.DialogueEditing.AddChoice(so, first, "Salut.", -1, 4);
                }, created);

                AssertBuilds(w =>
                {
                    var root = new TheDungeon.Editor.Dialogues.DialoguesTab(w).Build();
                    ClickAll(root, "Arbre");
                    Assert.Greater(root.Query(className: "forge-dialogue-node").ToList().Count, 0, "L'arbre doit montrer les répliques.");
                    Assert.IsNotNull(root.Q(className: "forge-dialogue-node__port"), "Les réponses doivent avoir un rond de sortie.");
                    Assert.IsNotNull(root.Q(className: "forge-dialogue-sample"), "Aperçu de l'apparence manquant.");
                    // Les sections se replient au clic sur leur en-tête.
                    var section = root.Query(className: "forge-section").Where(e => e.Q(className: "forge-section__header") != null).First();
                    var collapsed = section.ClassListContains("forge-section--collapsed");
                    ForgeUI.ToggleCollapsed(section);
                    Assert.AreNotEqual(collapsed, section.ClassListContains("forge-section--collapsed"), "La section doit se replier ou se déplier.");
                    ForgeUI.ToggleCollapsed(section);
                    ClickAll(root, "Aperçu en jeu");
                    ClickAll(root, "Conversation");
                    var composer = root.Q<TextField>(className: "forge-composer__input");
                    Assert.IsNotNull(composer, "Barre de saisie introuvable.");
                    composer.value = "Une nouvelle réplique.";
                    ClickAll(root, "Envoyer");
                    StringAssert.Contains("Une nouvelle réplique.", string.Join("|", root.Query<Label>(className: "forge-chat__text").ToList().Select(l => l.text)));
                    return root;
                });
            }
            finally
            {
                for (var i = created.Count - 1; i >= 0; i--)
                    UnityEditor.AssetDatabase.DeleteAsset(UnityEditor.AssetDatabase.GetAssetPath(created[i]));
            }
        }

        /// <summary>Progression réelle (créée puis supprimée) : chaque catégorie de l'onglet se construit sans erreur.</summary>
        [Test]
        public void Progression_EveryCategory_Builds()
        {
            var created = new System.Collections.Generic.List<Object>();
            try
            {
                Commit<TheDungeon.Data.PlayerProgression>("Zz Smoke Progression", so => ProgressionDefaults.Reset(so, ForgeDesignLimits.Instance), created);
                AssertBuilds(w =>
                {
                    var root = new ProgressionTab(w).Build();
                    var categories = root.Query<Button>(className: "forge-seg__item").ToList();
                    Assert.AreEqual(6, categories.Count, "Aucune progression ouverte : catégories introuvables.");
                    foreach (var category in categories)
                        Click(category);
                    return root;
                });
            }
            finally
            {
                for (var i = created.Count - 1; i >= 0; i--)
                    UnityEditor.AssetDatabase.DeleteAsset(UnityEditor.AssetDatabase.GetAssetPath(created[i]));
            }
        }

        [Test] public void Dashboard_Builds() => AssertBuilds(w => new DashboardView(w).Build());
        [Test] public void Settings_Builds() => AssertBuilds(_ => SettingsView.Build());

        /// <summary>Crée de vraies données temporaires (validées) pour construire l'éditeur de salle complet, puis les supprime.</summary>
        [Test]
        public void RoomEditor_WithRealData_Builds()
        {
            var created = new System.Collections.Generic.List<Object>();
            try
            {
                var floorSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>($"{DungeonPaths.Art}/Biomes/Crypt/SPR_CryptFloor.png");
                Assert.IsNotNull(floorSprite, "Les sprites du biome Crypte doivent être importés.");

                var enemy = Commit<TheDungeon.Data.EnemyData>("Zz Smoke Enemy", _ => { }, created);
                var biome = Commit<TheDungeon.Data.BiomeData>("Zz Smoke Biome", so =>
                {
                    so.FindProperty("floorSprites").arraySize = 1;
                    so.FindProperty("floorSprites").GetArrayElementAtIndex(0).objectReferenceValue = floorSprite;
                    so.FindProperty("wallSprite").objectReferenceValue = floorSprite;
                    so.FindProperty("pitSprite").objectReferenceValue = floorSprite;
                    so.FindProperty("enemies").arraySize = 1;
                    so.FindProperty("enemies").GetArrayElementAtIndex(0).FindPropertyRelative("enemy").objectReferenceValue = enemy;
                    so.FindProperty("enemies").GetArrayElementAtIndex(0).FindPropertyRelative("spawnWeight").intValue = 5;
                }, created);
                Commit<TheDungeon.Data.RoomData>("Zz Smoke Room", so =>
                {
                    so.FindProperty("biome").objectReferenceValue = biome;
                    so.FindProperty("roomType").enumValueIndex = (int)TheDungeon.Data.RoomType.Combat;
                    so.FindProperty("doors").intValue = (int)TheDungeon.Data.Doors.West;
                    RoomEditing.Reset(so);
                    RoomEditing.PlaceEnemy(so, new Vector2Int(5, 5), enemy, 10);
                }, created);

                Debug.Log("[ForgeTest] construction RoomsTab");
                AssertBuilds(w => new RoomsTab(w).Build());
                Debug.Log("[ForgeTest] construction BiomesTab");
                AssertBuilds(w =>
                {
                    var root = new BiomesTab(w).Build();
                    ClickAll(root, "+ Ajouter");
                    return root;
                });
            }
            finally
            {
                for (var i = created.Count - 1; i >= 0; i--)
                    UnityEditor.AssetDatabase.DeleteAsset(UnityEditor.AssetDatabase.GetAssetPath(created[i]));
            }
        }

        /// <summary>Simule un clic sur chaque bouton portant ce texte.</summary>
        static void ClickAll(VisualElement root, string text)
        {
            var buttons = root.Query<Button>().Where(b => b.text == text).ToList();
            Assert.IsNotEmpty(buttons, $"Aucun bouton « {text} ».");
            foreach (var button in buttons)
                Click(button);
        }

        static void Click(Button button)
        {
            var field = typeof(Clickable).GetField("clicked", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            ((System.Action)field!.GetValue(button.clickable))?.Invoke();
        }


        static T Commit<T>(string name, System.Action<UnityEditor.SerializedObject> setup, System.Collections.Generic.List<Object> created)
            where T : ScriptableObject
        {
            Debug.Log($"[ForgeTest] {name} : préparation");
            var session = DraftSession<T>.CreateNew(name);
            var serialized = new UnityEditor.SerializedObject(session.Draft);
            setup(serialized);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"[ForgeTest] {name} : validation");
            var errors = session.TryCommit();
            Debug.Log($"[ForgeTest] {name} : enregistré");
            Assert.IsEmpty(errors, $"{name} : " + string.Join(" / ", errors.ConvertAll(e => e.Message)));
            created.Add(session.Original);
            session.Dispose();
            return (T)session.Original;
        }

        [Test]
        public void HelpFiles_Exist()
        {
            foreach (var help in new[] { "Dashboard", "ContentDatabase", "Dialogues", "BiomePresets", "RoomEditor", "Floors", "Settings" })
                Assert.IsNotNull(UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>($"{DungeonPaths.Help}/{help}.md"), help);
        }
    }
}
