using System.Linq;
using NUnit.Framework;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using TheDungeon.Editor.RoomEditor;
using TheDungeon.Editor.Validation;
using UnityEditor;
using UnityEngine;

namespace TheDungeon.Editor.Tests
{
    public class RoomTests
    {
        ForgeDesignLimits limits;
        Sprite sprite;
        EnemyData goblin;
        EnemyData boss;
        EnemyData stranger;
        BiomeData biome;
        RoomData room;
        SerializedObject serializedRoom;

        [SetUp]
        public void SetUp()
        {
            limits = ScriptableObject.CreateInstance<ForgeDesignLimits>();
            sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), Vector2.zero);
            goblin = TestData.Create<EnemyData>("ENM_Goblin", so => so.FindProperty("displayName").stringValue = "Goblin");
            stranger = TestData.Create<EnemyData>("ENM_Stranger", so => so.FindProperty("displayName").stringValue = "Stranger");
            boss = TestData.Create<EnemyData>("BOSS_Lich", so =>
            {
                so.FindProperty("displayName").stringValue = "Lich";
                so.FindProperty("isBoss").boolValue = true;
            });
            biome = TestData.Create<BiomeData>("BIO_Crypt", so =>
            {
                so.FindProperty("displayName").stringValue = "Crypt";
                var enemies = so.FindProperty("enemies");
                enemies.arraySize = 2;
                enemies.GetArrayElementAtIndex(0).FindPropertyRelative("enemy").objectReferenceValue = goblin;
                enemies.GetArrayElementAtIndex(0).FindPropertyRelative("spawnWeight").intValue = 10;
                enemies.GetArrayElementAtIndex(1).FindPropertyRelative("enemy").objectReferenceValue = boss;
                enemies.GetArrayElementAtIndex(1).FindPropertyRelative("spawnWeight").intValue = 1;
                so.FindProperty("floorSprites").arraySize = 1;
                so.FindProperty("floorSprites").GetArrayElementAtIndex(0).objectReferenceValue = sprite;
                so.FindProperty("wallSprite").objectReferenceValue = sprite;
                so.FindProperty("pitSprite").objectReferenceValue = sprite;
            });
            room = TestData.Create<RoomData>("RM_Test", so =>
            {
                so.FindProperty("displayName").stringValue = "Test";
                so.FindProperty("biome").objectReferenceValue = biome;
                so.FindProperty("width").intValue = 9;
                so.FindProperty("height").intValue = 7;
                so.FindProperty("doors").intValue = (int)(Doors.West | Doors.East);
                RoomEditing.Reset(so);
            });
            serializedRoom = new SerializedObject(room);
        }

        [TearDown]
        public void TearDown() => TestData.Destroy(room, biome, goblin, boss, stranger, sprite, limits);

        System.Collections.Generic.List<ValidationIssue> Validate() =>
            new RoomRule().Run(new ValidationContext(new ItemData[0], new[] { goblin, boss, stranger }, new LootTable[0],
                new[] { biome }, new[] { room }, new FloorData[0], new PlayerProgression[0], limits)).ToList();

        void Edit(System.Func<string> operation, bool expectSuccess = true)
        {
            var error = operation();
            serializedRoom.ApplyModifiedPropertiesWithoutUndo();
            if (expectSuccess)
                Assert.IsNull(error, error);
            else
                Assert.IsNotNull(error);
        }

        [Test]
        public void Reset_BuildsWalledRoomWithOpenDoors()
        {
            Assert.AreEqual(TileType.Wall, room.GetTile(0, 0));
            Assert.AreEqual(TileType.Floor, room.GetTile(4, 3));
            Assert.AreEqual(TileType.Floor, room.GetTile(0, 3), "L'entrée ouest doit être ouverte.");
            Assert.AreEqual(TileType.Floor, room.GetTile(8, 3), "L'entrée est doit être ouverte.");
        }

        [Test]
        public void CombatRoom_WithBiomeEnemy_IsValid()
        {
            Edit(() => RoomEditing.PlaceEnemy(serializedRoom, new Vector2Int(4, 3), goblin, limits.maxEnemiesPerRoom));
            Assert.IsFalse(Validate().Any(i => i.Severity == Severity.Error), string.Join("\n", Validate().Select(i => i.Message)));
        }

        [Test]
        public void Enemy_OutsideBiome_IsAnError()
        {
            Edit(() => RoomEditing.PlaceEnemy(serializedRoom, new Vector2Int(4, 3), stranger, limits.maxEnemiesPerRoom));
            Assert.IsTrue(Validate().Any(i => i.Message.Contains("n'appartient pas au biome")));
        }

        [Test]
        public void Placing_OnWall_IsRefused()
        {
            Edit(() => RoomEditing.PlaceEnemy(serializedRoom, new Vector2Int(0, 0), goblin, limits.maxEnemiesPerRoom), expectSuccess: false);
            Assert.AreEqual(0, room.Enemies.Count);
        }

        [Test]
        public void Wall_OnDoorCell_IsRefused()
        {
            Edit(() => RoomEditing.SetTile(serializedRoom, new Vector2Int(0, 3), TileType.Wall), expectSuccess: false);
        }

        [Test]
        public void EnemyBehindWall_IsUnreachable()
        {
            // Mur vertical complet en x = 6 : la zone de droite reste reliée à l'entrée est, on ferme donc celle-ci.
            RoomEditing.SetDoor(serializedRoom, Doors.East, false);
            for (var y = 1; y < 6; y++)
                RoomEditing.SetTile(serializedRoom, new Vector2Int(6, y), TileType.Wall);
            Edit(() => RoomEditing.PlaceEnemy(serializedRoom, new Vector2Int(7, 3), goblin, limits.maxEnemiesPerRoom));

            Assert.IsTrue(Validate().Any(i => i.Message.Contains("inaccessible")));
        }

        [Test]
        public void Boss_OutsideBossRoom_IsAnError()
        {
            Edit(() => RoomEditing.PlaceEnemy(serializedRoom, new Vector2Int(4, 3), boss, limits.maxEnemiesPerRoom));
            Assert.IsTrue(Validate().Any(i => i.Message.Contains("salle de type Boss")));
        }

        [Test]
        public void Resize_KeepsContentAndReopensDoors()
        {
            Edit(() => RoomEditing.PlaceEnemy(serializedRoom, new Vector2Int(2, 2), goblin, limits.maxEnemiesPerRoom));
            RoomEditing.Resize(serializedRoom, 13, 9);
            serializedRoom.ApplyModifiedPropertiesWithoutUndo();

            Assert.AreEqual(13 * 9, room.Width * room.Height);
            Assert.IsTrue(room.HasValidTiles);
            Assert.AreEqual(1, room.Enemies.Count);
            Assert.AreEqual(TileType.Floor, room.GetTile(12, 4), "L'entrée est doit suivre le nouveau bord.");
        }

        [Test]
        public void Room_WithoutBiome_IsAnError()
        {
            serializedRoom.FindProperty("biome").objectReferenceValue = null;
            serializedRoom.ApplyModifiedPropertiesWithoutUndo();
            Assert.IsTrue(Validate().Any(i => i.Message.Contains("pas de biome")));
        }
    }
}
