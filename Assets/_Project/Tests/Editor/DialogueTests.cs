using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TheDungeon.Data;
using TheDungeon.Dialogue;
using TheDungeon.Editor.Common;
using TheDungeon.Editor.Dialogues;
using TheDungeon.Editor.Validation;
using UnityEditor;
using UnityEngine;

namespace TheDungeon.Editor.Tests
{
    public class DialogueTests
    {
        sealed class State : IDialogueState
        {
            public readonly HashSet<string> Flags = new();
            public readonly List<ItemData> Items = new();
            public int PlayerLevel { get; set; } = 1;
            public int Gold { get; set; }
            public int Floor { get; set; } = 1;
            public int Xp;
            public bool HasFlag(string flag) => Flags.Contains(flag);
            public bool HasItem(ItemData item) => Items.Contains(item);
            public void SetFlag(string flag, bool value) { if (value) Flags.Add(flag); else Flags.Remove(flag); }
            public void AddGold(int amount) => Gold += amount;
            public void AddItem(ItemData item) => Items.Add(item);
            public void RemoveItem(ItemData item) => Items.Remove(item);
            public void AddXp(int amount) => Xp += amount;
            public void Heal(int amount) { }
        }

        NpcData npc;
        SerializedObject serialized;
        ForgeDesignLimits limits;

        [SetUp]
        public void SetUp()
        {
            npc = TestData.Create<NpcData>("NPC_Smith", so => so.FindProperty("displayName").stringValue = "Smith");
            serialized = new SerializedObject(npc);
            limits = ScriptableObject.CreateInstance<ForgeDesignLimits>();
        }

        [TearDown]
        public void TearDown() => TestData.Destroy(npc, limits);

        int Add(string text, DialogueSpeaker speaker = DialogueSpeaker.Npc)
        {
            var id = DialogueEditing.AddNode(serialized, speaker, text);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return id;
        }

        List<ValidationIssue> Validate(params NpcData[] others) =>
            new DialogueRule().Run(new ValidationContext(new ItemData[0], new EnemyData[0], new LootTable[0], new BiomeData[0],
                new RoomData[0], new FloorData[0], new PlayerProgression[0], limits, others.Append(npc).ToArray())).ToList();

        [Test]
        public void FirstNode_BecomesStart_AndIdsAreUnique()
        {
            var a = Add("Bonjour.");
            var b = Add("Au revoir.");
            Assert.AreEqual(a, npc.StartNode);
            Assert.AreNotEqual(a, b);
        }

        [Test]
        public void ChatFlow_LinksNpcLineAfterPlayerReply()
        {
            var greeting = Add("Tu cherches du travail ?");
            DialogueEditing.AddChoice(serialized, greeting, "Oui.", DialogueEditing.End, 4);
            var answer = DialogueEditing.AddNode(serialized, DialogueSpeaker.Npc, "Ramène-moi du minerai.");
            Assert.IsTrue(DialogueEditing.LinkAfter(serialized, greeting, answer));
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Assert.AreEqual(answer, npc.FindNode(greeting).choices[0].target, "La réplique doit répondre au choix encore sans suite.");
        }

        [Test]
        public void DeletingNode_RedirectsLinksToEnd()
        {
            var a = Add("A");
            var b = Add("B");
            DialogueEditing.LinkAfter(serialized, a, b);
            DialogueEditing.DeleteNode(serialized, b);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Assert.IsNull(npc.FindNode(b));
            Assert.AreEqual(DialogueEditing.End, npc.FindNode(a).next);
        }

        [Test]
        public void ConditionalChoice_IsHiddenUntilFlagIsSet()
        {
            var a = Add("Alors ?");
            DialogueEditing.AddChoice(serialized, a, "J'ai le minerai.", DialogueEditing.End, 4);
            var condition = Node(a).FindPropertyRelative("choices").GetArrayElementAtIndex(0).FindPropertyRelative("conditions");
            condition.arraySize = 1;
            condition.GetArrayElementAtIndex(0).FindPropertyRelative("type").enumValueIndex = (int)ConditionType.FlagSet;
            condition.GetArrayElementAtIndex(0).FindPropertyRelative("flag").stringValue = "minerai_trouve";
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var state = new State();
            Assert.AreEqual(0, DialogueLogic.AvailableChoices(npc.FindNode(a), state).Count());
            state.SetFlag("minerai_trouve", true);
            Assert.AreEqual(1, DialogueLogic.AvailableChoices(npc.FindNode(a), state).Count());
        }

        [Test]
        public void Consequences_ChangeTheState()
        {
            var a = Add("Voilà ta récompense.");
            var effects = Node(a).FindPropertyRelative("consequences");
            effects.arraySize = 2;
            effects.GetArrayElementAtIndex(0).FindPropertyRelative("type").enumValueIndex = (int)ConsequenceType.GiveGold;
            effects.GetArrayElementAtIndex(0).FindPropertyRelative("value").intValue = 50;
            effects.GetArrayElementAtIndex(1).FindPropertyRelative("type").enumValueIndex = (int)ConsequenceType.SetFlag;
            effects.GetArrayElementAtIndex(1).FindPropertyRelative("flag").stringValue = "quete_finie";
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var state = new State();
            DialogueLogic.ApplyAll(npc.FindNode(a).consequences, state);
            Assert.AreEqual(50, state.Gold);
            Assert.IsTrue(state.HasFlag("quete_finie"));
        }

        [Test]
        public void AlternativeEntry_IsUsedWhenConditionsAreMet()
        {
            var normal = Add("Bienvenue, étranger.");
            var again = Add("Re-bonjour !");
            var entries = serialized.FindProperty("entries");
            entries.arraySize = 1;
            entries.GetArrayElementAtIndex(0).FindPropertyRelative("node").intValue = again;
            var conditions = entries.GetArrayElementAtIndex(0).FindPropertyRelative("conditions");
            conditions.arraySize = 1;
            conditions.GetArrayElementAtIndex(0).FindPropertyRelative("type").enumValueIndex = (int)ConditionType.PlayerLevelAtLeast;
            conditions.GetArrayElementAtIndex(0).FindPropertyRelative("value").intValue = 5;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Assert.AreEqual(normal, DialogueLogic.Start(npc, new State { PlayerLevel = 1 }).id);
            Assert.AreEqual(again, DialogueLogic.Start(npc, new State { PlayerLevel = 5 }).id);
        }

        [Test]
        public void Rule_FlagTestedButNeverSet_IsAWarning()
        {
            var a = Add("Alors ?");
            DialogueEditing.AddChoice(serialized, a, "Oui.", DialogueEditing.End, 4);
            var condition = Node(a).FindPropertyRelative("choices").GetArrayElementAtIndex(0).FindPropertyRelative("conditions");
            condition.arraySize = 1;
            condition.GetArrayElementAtIndex(0).FindPropertyRelative("type").enumValueIndex = (int)ConditionType.FlagSet;
            condition.GetArrayElementAtIndex(0).FindPropertyRelative("flag").stringValue = "faute_de_frappe";
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Assert.IsTrue(Validate().Any(i => i.Severity == Severity.Warning && i.Message.Contains("faute_de_frappe")));
        }

        [Test]
        public void Rule_UnreachableNodeAndEmptyText_AreReported()
        {
            Add("Début.");
            Add(string.Empty);
            var issues = Validate();
            Assert.IsTrue(issues.Any(i => i.Message.Contains("rien ne permet d'y arriver")));
            Assert.IsTrue(issues.Any(i => i.Severity == Severity.Error && i.Message.Contains("texte est vide")));
        }

        [Test]
        public void Script_ListsLinesChoicesAndTargets()
        {
            var a = Add("Tu cherches du travail ?");
            DialogueEditing.AddChoice(serialized, a, "Oui.", DialogueEditing.End, 4);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var script = DialogueScript.Build(npc);
            StringAssert.Contains("Tu cherches du travail ?", script);
            StringAssert.Contains("Joueur : « Oui. » → Fin", script);
        }

        [Test]
        public void FlagNames_AreSanitized()
        {
            Assert.AreEqual("quete_acceptee", DialogueRulesEditor.Sanitize("Quête acceptée"));
        }

        SerializedProperty Node(int id) => DialogueEditing.Node(serialized, id);
    }
}
