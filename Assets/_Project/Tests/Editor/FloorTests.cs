using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using TheDungeon.Editor.Floors;
using TheDungeon.Editor.Validation;
using UnityEditor;
using UnityEngine;

namespace TheDungeon.Editor.Tests
{
    /// <summary>Règles du GDD sur les étages et générateur de plans, sur des données en mémoire.</summary>
    public class FloorTests
    {
        /// <summary>Soin au feu de camp : la règle de l'étage, sauf si la salle a la sienne ; 100 % sans étage.</summary>
        [Test]
        public void CampfireHeal_RoomRuleOverridesFloorRule()
        {
            var floor = TestData.Create<FloorData>("FLR_1", so => so.FindProperty("campfireHealPercent").intValue = 40);
            var room = TestData.Create<RoomData>("RM_Test");
            var custom = TestData.Create<RoomData>("RM_Custom", so =>
            {
                so.FindProperty("overrideCampfireHeal").boolValue = true;
                so.FindProperty("campfireHealPercent").intValue = 0;
            });

            Assert.AreEqual(40, room.CampfireHealPercent(floor));
            Assert.AreEqual(0, custom.CampfireHealPercent(floor));
            Assert.AreEqual(100, room.CampfireHealPercent(null));
            TestData.Destroy(floor, room, custom);
        }

        readonly List<Object> created = new();
        ForgeDesignLimits limits;
        EnemyData boss;

        [SetUp]
        public void SetUp()
        {
            limits = ScriptableObject.CreateInstance<ForgeDesignLimits>();
            boss = TestData.Create<EnemyData>("BOSS_Lich", so =>
            {
                so.FindProperty("displayName").stringValue = "Lich";
                so.FindProperty("isBoss").boolValue = true;
            });
            created.Add(limits);
            created.Add(boss);
        }

        [TearDown]
        public void TearDown()
        {
            TestData.Destroy(created.ToArray());
            created.Clear();
        }

        RoomData Room(string name, RoomType type, Doors doors, bool withBoss = false)
        {
            var room = TestData.Create<RoomData>($"RM_{name}", so =>
            {
                so.FindProperty("displayName").stringValue = name;
                so.FindProperty("roomType").enumValueIndex = (int)type;
                so.FindProperty("doors").intValue = (int)doors;
                if (withBoss)
                {
                    so.FindProperty("enemies").arraySize = 1;
                    so.FindProperty("enemies").GetArrayElementAtIndex(0).FindPropertyRelative("enemy").objectReferenceValue = boss;
                }
            });
            created.Add(room);
            return room;
        }

        FloorData Floor(RoomData entrance, params (RoomData room, Vector2Int cell)[] slots)
        {
            var floor = TestData.Create<FloorData>("FLR_1", so =>
            {
                var rooms = so.FindProperty("rooms");
                rooms.arraySize = slots.Length;
                for (var i = 0; i < slots.Length; i++)
                {
                    rooms.GetArrayElementAtIndex(i).FindPropertyRelative("room").objectReferenceValue = slots[i].room;
                    rooms.GetArrayElementAtIndex(i).FindPropertyRelative("cell").vector2IntValue = slots[i].cell;
                }
                so.FindProperty("entrance").objectReferenceValue = entrance;
            });
            created.Add(floor);
            return floor;
        }

        List<ValidationIssue> Validate(FloorData floor) =>
            new FloorRule().Run(new ValidationContext(new ItemData[0], new[] { boss }, new LootTable[0], new BiomeData[0],
                floor.Rooms.Select(s => s.room).ToArray(), new[] { floor }, new PlayerProgression[0], limits)).Where(i => i.Context == floor).ToList();

        [Test]
        public void ConnectedFloor_WithOneBoss_HasNoErrors()
        {
            var start = Room("Start", RoomType.Start, Doors.East);
            var hall = Room("Hall", RoomType.Combat, Doors.West | Doors.East);
            var lair = Room("Lair", RoomType.Boss, Doors.West, withBoss: true);
            var floor = Floor(start, (start, new Vector2Int(0, 0)), (hall, new Vector2Int(1, 0)), (lair, new Vector2Int(2, 0)));

            Assert.IsFalse(Validate(floor).Any(i => i.Severity == Severity.Error), string.Join("\n", Validate(floor).Select(i => i.Message)));
        }

        [Test]
        public void DoorLeadingNowhere_IsAnError()
        {
            var start = Room("Start", RoomType.Start, Doors.East | Doors.North);
            var lair = Room("Lair", RoomType.Boss, Doors.West, withBoss: true);
            var floor = Floor(start, (start, new Vector2Int(0, 0)), (lair, new Vector2Int(1, 0)));

            Assert.IsTrue(Validate(floor).Any(i => i.Message.Contains("mène dans le vide")));
        }

        [Test]
        public void IncompatibleDoors_AreAnError()
        {
            var start = Room("Start", RoomType.Start, Doors.East);
            var lair = Room("Lair", RoomType.Boss, Doors.North, withBoss: true);
            var floor = Floor(start, (start, new Vector2Int(0, 0)), (lair, new Vector2Int(1, 0)));

            Assert.IsTrue(Validate(floor).Any(i => i.Message.Contains("portes incompatibles")));
        }

        [Test]
        public void RoomNotConnectedToEntrance_IsUnreachable()
        {
            var start = Room("Start", RoomType.Start, Doors.None);
            var lair = Room("Lair", RoomType.Boss, Doors.None, withBoss: true);
            var floor = Floor(start, (start, new Vector2Int(0, 0)), (lair, new Vector2Int(3, 3)));

            Assert.IsTrue(Validate(floor).Any(i => i.Message.Contains("n'est pas atteignable")));
        }

        [Test]
        public void FloorWithoutBoss_IsAnError()
        {
            var start = Room("Start", RoomType.Start, Doors.East);
            var hall = Room("Hall", RoomType.Combat, Doors.West);
            var floor = Floor(start, (start, new Vector2Int(0, 0)), (hall, new Vector2Int(1, 0)));

            Assert.IsTrue(Validate(floor).Any(i => i.Message.Contains("exactement un")));
        }

        [Test]
        public void Generator_BuildsFullyConnectedFloor()
        {
            var pool = new List<RoomData>
            {
                Room("Start", RoomType.Start, Doors.East | Doors.West),
                Room("Lair", RoomType.Boss, Doors.West, withBoss: true)
            };
            for (var i = 0; i < 8; i++)
                pool.Add(Room($"Hall{i}", RoomType.Combat, i % 2 == 0 ? Doors.West | Doors.East : Doors.West | Doors.East | Doors.North | Doors.South));
            pool.Add(Room("DeadEndN", RoomType.Treasure, Doors.South));
            pool.Add(Room("DeadEndS", RoomType.Treasure, Doors.North));
            pool.Add(Room("DeadEndW", RoomType.Treasure, Doors.East));

            var result = FloorGenerator.Generate(pool, 6, new Vector2Int(6, 6), seed: 42);

            Assert.IsTrue(result.Success, result.Error);
            Assert.AreEqual(6, result.Layout.Count);
            Assert.IsTrue(FloorGraph.AllDoors(result.Layout).All(d => d.State == DoorState.Connected), "Toutes les portes doivent être reliées.");
            Assert.AreEqual(1, result.Layout.Values.Count(r => r.ContainsBoss()));
            FloorGraph.TryFindCell(result.Layout, result.Entrance, out var start);
            Assert.AreEqual(result.Layout.Count, FloorGraph.Reachable(result.Layout, start).Count);
        }

        BiomeData Biome(string name)
        {
            var biome = TestData.Create<BiomeData>($"BIO_{name}", so => so.FindProperty("displayName").stringValue = name);
            created.Add(biome);
            return biome;
        }

        RoomData RoomIn(BiomeData biome, string name, RoomType type, Doors doors, bool withBoss = false)
        {
            var room = Room(name, type, doors, withBoss);
            var so = new SerializedObject(room);
            so.FindProperty("biome").objectReferenceValue = biome;
            so.ApplyModifiedPropertiesWithoutUndo();
            return room;
        }

        [Test]
        public void Generator_StaysInOneBiome()
        {
            var crypt = Biome("Crypte");
            var volcano = Biome("Volcan");
            var pool = new List<RoomData>
            {
                RoomIn(crypt, "CryptStart", RoomType.Start, Doors.East),
                RoomIn(crypt, "CryptBoss", RoomType.Boss, Doors.West, true),
                RoomIn(volcano, "VolcanoStart", RoomType.Start, Doors.East),
                RoomIn(volcano, "VolcanoBoss", RoomType.Boss, Doors.West, true)
            };
            for (var seed = 0; seed < 20; seed++)
            {
                var result = FloorGenerator.Generate(pool, 2, new Vector2Int(6, 6), seed);
                Assert.IsTrue(result.Success, result.Error);
                Assert.AreEqual(1, result.Layout.Values.Select(r => r.Biome).Distinct().Count(), "Un étage n'a qu'un biome.");
            }
        }

        [Test]
        public void Generator_AvoidsTheCurrentLayout_OrSaysThereIsNoOther()
        {
            var start = Room("Start", RoomType.Start, Doors.East);
            var firstBoss = Room("BossA", RoomType.Boss, Doors.West, true);
            var secondBoss = Room("BossB", RoomType.Boss, Doors.West, true);
            var grid = new Vector2Int(6, 6);
            var pool = new List<RoomData> { start, firstBoss, secondBoss };

            var first = FloorGenerator.Generate(pool, 2, grid, 0);
            var other = FloorGenerator.GenerateDifferent(pool, 2, grid, 0, first.Layout, out var usedSeed, out var changed);
            Assert.IsTrue(changed);
            Assert.AreNotEqual(first.Layout.Values.Last(), other.Layout.Values.Last(), "L'autre boss est choisi.");
            Assert.AreEqual(other.Layout.Values.Last(), FloorGenerator.Generate(pool, 2, grid, usedSeed).Layout.Values.Last(), "La graine retenue redonne ce plan.");

            var only = new List<RoomData> { start, firstBoss };
            var current = FloorGenerator.Generate(only, 2, grid, 0).Layout;
            FloorGenerator.GenerateDifferent(only, 2, grid, 0, current, out _, out changed);
            Assert.IsFalse(changed, "Une seule combinaison possible : le plan ne change pas.");
        }

        [Test]
        public void FloorMixingBiomes_IsAnError()
        {
            var start = RoomIn(Biome("Crypte"), "Start", RoomType.Start, Doors.East);
            var other = RoomIn(Biome("Volcan"), "Boss", RoomType.Boss, Doors.West, true);
            var floor = Floor(start, (start, new Vector2Int(0, 0)), (other, new Vector2Int(1, 0)));
            var issues = new FloorRule().Run(new ValidationContext(new ItemData[0], new EnemyData[0], new LootTable[0], new BiomeData[0],
                new[] { start, other }, new[] { floor }, new PlayerProgression[0], limits)).ToList();
            Assert.IsTrue(issues.Any(i => i.Severity == Severity.Error && i.Message.Contains("n'a qu'un biome")));
        }

        [Test]
        public void WorkingCopyOfAPlacedRoom_IsConsideredPlaced()
        {
            var room = Room("Start", RoomType.Start, Doors.None);
            var floor = Floor(room, (room, new Vector2Int(0, 0)));
            var copy = Object.Instantiate(room);
            created.Add(copy);
            var context = new ValidationContext(new ItemData[0], new EnemyData[0], new LootTable[0], new BiomeData[0],
                new[] { copy }, new[] { floor }, new PlayerProgression[0], limits).WithWorkingCopy(room, copy);
            Assert.IsFalse(new FloorRule().Run(context).Any(i => i.Context == copy && i.Message.Contains("aucun étage")));
        }

        [Test]
        public void Generator_ExplainsMissingBossRoom()
        {
            var pool = new List<RoomData> { Room("Start", RoomType.Start, Doors.East), Room("Hall", RoomType.Combat, Doors.West) };
            var result = FloorGenerator.Generate(pool, 2, new Vector2Int(6, 6), seed: 1);
            Assert.IsFalse(result.Success);
            StringAssert.Contains("boss", result.Error);
        }
    }
}
