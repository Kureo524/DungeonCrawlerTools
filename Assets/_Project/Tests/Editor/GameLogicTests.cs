using System.Linq;
using NUnit.Framework;
using TheDungeon.Data;
using TheDungeon.Editor.Dialogues;
using TheDungeon.Editor.RoomEditor;
using TheDungeon.Game;
using UnityEditor;
using UnityEngine;

namespace TheDungeon.Editor.Tests
{
    /// <summary>Logique du jeu indépendante de l'affichage : placement des salles, progression, équipement, combat, dialogue.</summary>
    public class GameLogicTests
    {
        static RoomData Room(int width, int height, Doors doors) => TestData.Create<RoomData>("RM_Test", so =>
        {
            so.FindProperty("width").intValue = width;
            so.FindProperty("height").intValue = height;
            so.FindProperty("doors").intValue = (int)doors;
            RoomEditing.Reset(so);
        });

        static PlayerProgression Progression() => TestData.Create<PlayerProgression>("PRG_Player", so =>
        {
            var levels = so.FindProperty("levels");
            levels.arraySize = 20;
            for (var i = 0; i < 20; i++)
            {
                levels.GetArrayElementAtIndex(i).FindPropertyRelative("maxHealth").intValue = 100 + i * 10;
                levels.GetArrayElementAtIndex(i).FindPropertyRelative("damage").intValue = 10 + i;
                levels.GetArrayElementAtIndex(i).FindPropertyRelative("xpRequired").intValue = 50;
            }
        });

        static ItemData Item(ItemType type, Rarity rarity, StatType stat, int value) => TestData.Create<ItemData>("ITM_Test", so =>
        {
            so.FindProperty("type").enumValueIndex = (int)type;
            so.FindProperty("rarity").enumValueIndex = (int)rarity;
            var stats = so.FindProperty("stats");
            stats.arraySize = 1;
            stats.GetArrayElementAtIndex(0).FindPropertyRelative("stat").enumValueIndex = (int)stat;
            stats.GetArrayElementAtIndex(0).FindPropertyRelative("value").intValue = value;
        });

        [Test]
        public void NeighbourRoomsOfDifferentSizes_HaveFacingDoors()
        {
            var small = Room(9, 7, Doors.East);
            var large = Room(17, 13, Doors.West);
            var slot = FloorLayout.SlotSize(new[] { small, large });

            var east = FloorLayout.DoorTile(small, FloorLayout.Origin(Vector2Int.zero, small, slot), Doors.East);
            var west = FloorLayout.DoorTile(large, FloorLayout.Origin(Vector2Int.right, large, slot), Doors.West);

            Assert.AreEqual(east.y, west.y, "Les deux portes doivent être sur la même ligne pour que le couloir soit droit.");
            Assert.Greater(west.x - east.x, 1, "Il doit rester de la place pour un couloir.");
            TestData.Destroy(small, large);
        }

        [Test]
        public void Xp_LevelsUpAndRefillsHealth()
        {
            var progression = Progression();
            var state = new GameState(progression);
            state.TakeDamage(50);
            state.AddXp(120);

            Assert.AreEqual(3, state.Level);
            Assert.AreEqual(20, state.Xp);
            Assert.AreEqual(state.MaxHp, state.Hp);
            TestData.Destroy(progression);
        }

        [Test]
        public void BetterItem_IsEquipped_AndOldOneGoesToInventory()
        {
            var progression = Progression();
            var state = new GameState(progression);
            var common = Item(ItemType.Weapon, Rarity.Common, StatType.Damage, 2);
            var rare = Item(ItemType.Weapon, Rarity.Rare, StatType.Damage, 5);

            state.AddItem(common);
            state.AddItem(rare);

            Assert.AreEqual(rare, state.Weapon);
            Assert.Contains(common, state.Inventory);
            Assert.AreEqual(10 + 5, state.Attack);
            TestData.Destroy(progression, common, rare);
        }

        [Test]
        public void Defense_ReducesDamage_ButNeverBelowOne()
        {
            Assert.AreEqual(7, Combat.Mitigate(10, 3));
            Assert.AreEqual(1, Combat.Mitigate(2, 50));
        }

        [Test]
        public void InGameDialogue_AppliesChoiceConsequences()
        {
            var progression = Progression();
            var npc = TestData.Create<NpcData>("NPC_Test", so =>
            {
                var first = DialogueEditing.AddNode(so, DialogueSpeaker.Npc, "Tiens, pour la route.");
                DialogueEditing.AddChoice(so, first, "Merci !", DialogueEditing.End, 4);
                var effects = so.FindProperty("nodes").GetArrayElementAtIndex(0).FindPropertyRelative("choices").GetArrayElementAtIndex(0)
                    .FindPropertyRelative("consequences");
                effects.arraySize = 1;
                effects.GetArrayElementAtIndex(0).FindPropertyRelative("type").enumValueIndex = (int)ConsequenceType.GiveGold;
                effects.GetArrayElementAtIndex(0).FindPropertyRelative("value").intValue = 30;
            });
            var state = new GameState(progression);
            var runner = new DialogueRunner(state);

            runner.Start(npc);
            runner.Advance(); // affiche tout le texte
            runner.Choose(0);

            Assert.AreEqual(30, state.Gold);
            Assert.IsFalse(runner.Active);
            TestData.Destroy(progression, npc);
        }
    }
}
