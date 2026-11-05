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
            public readonly Dictionary<string, int> Mechanics = new();
            public int MechanicLevel(string mechanicId) => Mechanics.TryGetValue(mechanicId, out var level) ? level : 0;
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
        public void PlayerAnswer_CanSayALineThatLeadsToAnotherPlayerLine()
        {
            var greeting = Add("Que veux-tu ?");
            var reply = Add("Très bien.");
            DialogueEditing.AddChoice(serialized, greeting, "Parler de la quête", reply, 4);
            var spoken = DialogueEditing.AddSpokenLine(serialized, greeting, 0, "Je cherche le forgeron disparu.");
            var more = DialogueEditing.AddLineAfter(serialized, spoken, DialogueSpeaker.Player, "On m'a dit qu'il était ici.");
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Assert.AreEqual("Parler de la quête", npc.FindNode(greeting).choices[0].text, "L'intitulé reste le texte cliquable.");
            Assert.AreEqual(spoken, npc.FindNode(greeting).choices[0].target, "La réponse mène à la réplique dite par le joueur.");
            Assert.AreEqual(DialogueSpeaker.Player, npc.FindNode(spoken).speaker);
            Assert.AreEqual(more, npc.FindNode(spoken).next, "Une réplique du joueur peut mener à une autre réplique du joueur.");
            Assert.AreEqual(reply, npc.FindNode(more).next, "La chaîne reprend la suite d'origine de la réponse.");
            Assert.IsEmpty(Validate().Where(i => i.Severity == Severity.Error));
        }

        [Test]
        public void GraphLayout_PlacesAnswersBelowAndSideBySide()
        {
            var start = Add("Bonjour.");
            var left = Add("Gauche.");
            var right = Add("Droite.");
            var orphan = Add("Personne n'y mène.");
            DialogueEditing.AddChoice(serialized, start, "A", left, 4);
            DialogueEditing.AddChoice(serialized, start, "B", right, 4);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var positions = DialogueGraphLayout.Positions(npc, _ => 100f);

            Assert.AreEqual(0f, positions[start].y);
            Assert.AreEqual(100f + DialogueGraphLayout.RowGap, positions[left].y, "Les réponses sont une ligne plus bas.");
            Assert.AreEqual(positions[left].y, positions[right].y);
            Assert.AreEqual(DialogueGraphLayout.NodeWidth + DialogueGraphLayout.ColumnGap, positions[right].x - positions[left].x, 0.01f,
                "Deux réponses côte à côte, sans chevauchement.");
            Assert.Greater(positions[orphan].y, positions[left].y, "Une réplique que rien n'atteint passe sous l'arbre.");
        }

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

        [Test]
        public void FloorSet_DescribesAndContainsFloors()
        {
            var set = FloorSet.Of(new[] { 1, 3 });
            Assert.IsTrue(FloorSet.Contains(set, 1));
            Assert.IsFalse(FloorSet.Contains(set, 2));
            Assert.IsTrue(FloorSet.Contains(set, 3));
            Assert.AreEqual("Étages 1, 3", FloorSet.Describe(set));
            Assert.AreEqual("Étage 2", FloorSet.Describe(FloorSet.Of(2)));
            Assert.AreEqual(1, FloorSet.First(FloorSet.With(set, 3, false)));
        }

        [Test]
        public void Npc_AppearsOnSeveralFloors_AndOldSingleFloorStillWorks()
        {
            serialized.FindProperty("floor").intValue = 2;
            serialized.FindProperty("floors").intValue = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.IsTrue(npc.AppearsOn(2), "Sans liste d'étages, l'ancien réglage d'un seul étage s'applique.");
            Assert.IsFalse(npc.AppearsOn(1));

            serialized.FindProperty("floors").intValue = FloorSet.Of(new[] { 1, 3 });
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.IsTrue(npc.AppearsOn(1));
            Assert.IsFalse(npc.AppearsOn(2));
            Assert.IsTrue(npc.AppearsOn(3));
        }

        [Test]
        public void Condition_FloorIn_OnlyOnCheckedFloors()
        {
            var condition = new DialogueCondition { type = ConditionType.FloorIn, value = FloorSet.Of(new[] { 1, 3 }) };
            Assert.IsTrue(DialogueLogic.IsMet(condition, new State { Floor = 1 }));
            Assert.IsFalse(DialogueLogic.IsMet(condition, new State { Floor = 2 }));
            Assert.IsTrue(DialogueLogic.IsMet(condition, new State { Floor = 3 }));
            Assert.AreEqual("Étages 1, 3", DialogueText.Describe(condition));
        }

        [Test]
        public void Condition_Mechanic_NeedsTheMechanicAtThatLevel()
        {
            var condition = new DialogueCondition { type = ConditionType.MechanicAtLeast, flag = "dash", value = 2 };
            var state = new State();
            Assert.IsFalse(DialogueLogic.IsMet(condition, state), "Mécanique pas débloquée.");
            state.Mechanics["dash"] = 1;
            Assert.IsFalse(DialogueLogic.IsMet(condition, state), "Débloquée mais pas encore au niveau 2.");
            state.Mechanics["dash"] = 2;
            Assert.IsTrue(DialogueLogic.IsMet(condition, state));
        }

        [Test]
        public void Rule_FloorInWithoutFloor_IsAnError()
        {
            var a = Add("Bonjour.");
            DialogueEditing.AddChoice(serialized, a, "Salut.", DialogueEditing.End, 4);
            var conditions = Node(a).FindPropertyRelative("choices").GetArrayElementAtIndex(0).FindPropertyRelative("conditions");
            conditions.arraySize = 1;
            conditions.GetArrayElementAtIndex(0).FindPropertyRelative("type").enumValueIndex = (int)ConditionType.FloorIn;
            conditions.GetArrayElementAtIndex(0).FindPropertyRelative("value").intValue = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.IsTrue(Validate().Any(i => i.Severity == Severity.Error && i.Message.Contains("aucun étage")));
        }

        [Test]
        public void Tree_LinksAndPositions_AreEditable()
        {
            var a = Add("Bonjour.");
            var b = Add("Que veux-tu ?");
            DialogueEditing.SetLink(serialized, a, -1, b);
            DialogueEditing.AddChoice(serialized, b, "Rien.", DialogueEditing.End, 4);
            DialogueEditing.SetLink(serialized, b, 0, a);
            DialogueEditing.SetPosition(serialized, b, new Vector2(120f, 300f));
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Assert.AreEqual(b, npc.FindNode(a).next);
            Assert.AreEqual(a, npc.FindNode(b).choices[0].target);
            Assert.IsTrue(npc.FindNode(b).placed);
            Assert.AreEqual(new Vector2(120f, 300f), npc.FindNode(b).graphPosition);

            DialogueEditing.ClearPositions(serialized);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.IsFalse(npc.FindNode(b).placed, "« Réorganiser » rend le rangement automatique.");
        }

        [Test]
        public void Transfer_ExportThenImport_KeepsTheWholeDiscussion()
        {
            var a = Add("Bonjour.");
            DialogueEditing.AddChoice(serialized, a, "Salut.", DialogueEditing.End, 4);
            serialized.FindProperty("floors").intValue = FloorSet.Of(new[] { 2, 3 });
            serialized.FindProperty("style.boxBorder").colorValue = Color.red;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var copy = DialogueTransfer.FromJson(DialogueTransfer.ToJson(npc));
            try
            {
                Assert.IsNotNull(copy);
                Assert.AreEqual("Smith", copy.DisplayName);
                Assert.AreEqual("Salut.", copy.FindNode(a).choices[0].text);
                Assert.IsTrue(copy.AppearsOn(3));
                Assert.AreEqual(Color.red, copy.Style.boxBorder);
            }
            finally
            {
                Object.DestroyImmediate(copy);
            }
            Assert.IsNull(DialogueTransfer.FromJson("{\"items\": []}"), "Un autre fichier JSON n'est pas une discussion.");
        }

        [Test]
        public void Undo_RevertsAnEditOnAWorkingCopy()
        {
            // Les fiches modifient une copie de travail non enregistrée (DontSave) : Ctrl+Z doit la ramener en arrière.
            var copy = Object.Instantiate(npc);
            copy.hideFlags = HideFlags.DontSave;
            try
            {
                var so = new SerializedObject(copy);
                var a = DialogueEditing.AddNode(so, DialogueSpeaker.Npc, "Bonjour.");
                so.ApplyModifiedPropertiesWithoutUndo();
                Undo.IncrementCurrentGroup();
                so.Update();
                DialogueEditing.Node(so, a).FindPropertyRelative("text").stringValue = "Salut.";
                so.ApplyModifiedProperties();
                Assert.AreEqual("Salut.", copy.FindNode(a).text);
                Undo.PerformUndo();
                Assert.AreEqual("Bonjour.", copy.FindNode(a).text);
            }
            finally
            {
                Undo.ClearUndo(copy);
                Object.DestroyImmediate(copy);
            }
        }

        [Test]
        public void ChoiceLook_UsesItsOwnColorsOnlyWhenAsked()
        {
            var style = new DialogueStyle();
            var choice = new DialogueChoice { background = Color.red };
            Assert.AreEqual(style.choiceBackground, style.ChoiceLook(choice).background);
            choice.customLook = true;
            Assert.AreEqual(Color.red, style.ChoiceLook(choice).background);
        }

        [Test]
        public void Tree_LinkFromLeftSide_IsRemembered()
        {
            var a = Add("Bonjour.");
            var b = Add("Suite.");
            DialogueEditing.AddChoice(serialized, a, "Oui.", DialogueEditing.End, 4);
            DialogueEditing.SetLink(serialized, a, 0, b, true);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.IsTrue(npc.FindNode(a).choices[0].linkFromLeft);
            DialogueEditing.SetLink(serialized, a, 0, DialogueEditing.End);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.AreEqual(DialogueEditing.End, npc.FindNode(a).choices[0].target, "Retirer le lien ne supprime pas de réplique.");
            Assert.AreEqual(2, npc.Nodes.Count);
        }

        SerializedProperty Node(int id) => DialogueEditing.Node(serialized, id);
    }
}
