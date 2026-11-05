using System.Linq;
using NUnit.Framework;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using TheDungeon.Editor.Dialogues;
using TheDungeon.Editor.Progression;
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
            var limits = ScriptableObject.CreateInstance<ForgeDesignLimits>();
            ProgressionDefaults.Reset(so, limits);
            TestData.Destroy(limits);
            ProgressionEditing.SetValues(so, 0, Enumerable.Range(0, 20).Select(i => 100f + i * 10).ToList());
            ProgressionEditing.SetValues(so, 1, Enumerable.Range(0, 20).Select(i => 10f + i).ToList());
            ProgressionEditing.SetValues(so, 2, Enumerable.Repeat(0f, 20).ToList());
            ProgressionEditing.SetValues(so, 3, Enumerable.Repeat(50f, 20).ToList());
        });

        static ItemData Item(ItemType type, int rarity, StatType stat, int value) => TestData.Create<ItemData>("ITM_Test", so =>
        {
            so.FindProperty("type").enumValueIndex = (int)type;
            so.FindProperty("rarity").intValue = rarity;
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
            var common = Item(ItemType.Weapon, 0, StatType.Damage, 2);
            var rare = Item(ItemType.Weapon, 2, StatType.Damage, 5);

            state.AddItem(common);
            state.AddItem(rare);

            Assert.AreEqual(rare, state.Weapon);
            Assert.Contains(common, state.Inventory);
            Assert.AreEqual(10 + 5, state.Attack);
            TestData.Destroy(progression, common, rare);
        }

        [Test]
        public void WeaponPassive_AttackBoost_RaisesAttack()
        {
            var progression = Progression();
            var state = new GameState(progression);
            var weapon = Item(ItemType.Weapon, 4, StatType.Damage, 10);
            var so = new UnityEditor.SerializedObject(weapon);
            var effects = so.FindProperty("effects");
            effects.arraySize = 1;
            effects.GetArrayElementAtIndex(0).FindPropertyRelative("type").enumValueIndex = (int)EffectType.AttackBoost;
            effects.GetArrayElementAtIndex(0).FindPropertyRelative("trigger").enumValueIndex = (int)EffectTrigger.Passive;
            effects.GetArrayElementAtIndex(0).FindPropertyRelative("value").floatValue = 50f;
            so.ApplyModifiedPropertiesWithoutUndo();

            state.AddItem(weapon);

            Assert.AreEqual(Mathf.RoundToInt((10 + 10) * 1.5f), state.Attack);
            TestData.Destroy(progression, weapon);
        }

        [Test]
        public void RarityRank_DecidesTheBetterItem_NotTheId()
        {
            var high = Item(ItemType.Weapon, 0, StatType.Damage, 1);
            var low = Item(ItemType.Weapon, 7, StatType.Damage, 9);

            // La rareté n°0 est rangée au-dessus de la n°7.
            Assert.IsTrue(GameState.IsBetter(high, low, id => id == 0 ? 5 : 1));
            TestData.Destroy(high, low);
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
