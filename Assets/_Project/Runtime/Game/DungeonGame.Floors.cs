using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using UnityEngine;

namespace TheDungeon.Game
{
    /// <summary>Construction d'un étage : salles et leur contenu (ennemis, feu de camp, boutique, personnages), puis nettoyage du monde.</summary>
    public sealed partial class DungeonGame
    {
        void LoadFloor(int index)
        {
            ClearWorld();
            Dictionary<Vector2Int, RoomData> layout;
            RoomData entrance;
            var room = testRoom != null ? database.rooms.FirstOrDefault(r => r != null && r.name == testRoom) : null;
            if (room != null)
            {
                layout = new Dictionary<Vector2Int, RoomData> { [Vector2Int.zero] = room };
                entrance = room;
                FloorNumber = room.Biome != null ? room.Biome.MinFloor : 1;
                FloorTitle = $"Test de la salle « {room.DisplayName} »";
                // En test, la salle suit les règles de l'étage où elle est placée (s'il y en a un).
                currentFloor = database.floors.FirstOrDefault(f => f != null && f.Rooms.Any(s => s.room == room));
            }
            else
            {
                var floor = database.floors[index];
                currentFloor = floor;
                layout = new Dictionary<Vector2Int, RoomData>();
                foreach (var slot in floor.Rooms.Where(s => s.room != null))
                    layout[slot.cell] = slot.room;
                entrance = floor.Entrance != null && layout.ContainsValue(floor.Entrance) ? floor.Entrance : layout.Values.First();
                FloorNumber = floor.FloorNumber;
                var biome = entrance.Biome != null ? entrance.Biome.DisplayName : string.Empty;
                FloorTitle = $"Étage {FloorNumber}" + (biome.Length > 0 ? $" — {biome}" : string.Empty);
            }
            State.Floor = FloorNumber;
            currentFloorIndex = index;

            World = FloorBuilder.Build(layout, database.doorClosed, transform);
            var entranceRoom = World.Rooms.First(r => r.Data == entrance);
            var spawn = entrance.HasPlayerSpawn ? entranceRoom.ToWorld(entrance.PlayerSpawn) : NearestFloor(entranceRoom, entranceRoom.Center);

            occupied = new HashSet<Vector3>();
            foreach (var instance in World.Rooms)
                PopulateRoom(instance, occupied);
            PlaceNpcs(occupied);

            Player = PlayerController.Spawn(this, database.player, spawn);
            CameraFollow.Target = Player.transform;
            CameraFollow.SnapToTarget();
            CurrentRoom = null;
            Toast(FloorTitle, new Color(0.95f, 0.8f, 0.5f));
        }

        int currentFloorIndex;

        /// <summary>Décors, coffres et boutique : tout ce qui est posé dans la salle par l'outil Salles.</summary>
        void PopulateRoom(RoomInstance room, HashSet<Vector3> occupied)
        {
            var data = room.Data;
            foreach (var prop in data.Props)
            {
                if (prop.sprite == null)
                    continue;
                var go = new GameObject("Décor");
                go.transform.position = room.ToWorld(prop.position);
                go.AddComponent<SpriteRenderer>().sprite = prop.sprite;
                go.GetComponent<SpriteRenderer>().sortingOrder = 5;
                var blocking = data.Biome == null || !data.Biome.TryGetProp(prop.sprite, out var definition) || definition.blocksMovement;
                if (blocking)
                    go.AddComponent<BoxCollider2D>().size = new Vector2(0.8f, 0.8f);
                Track(go);
                occupied.Add(go.transform.position);
            }
            foreach (var chest in data.Chests)
            {
                var position = room.ToWorld(chest.position);
                Chest.Spawn(this, chest.lootTable, data.Biome, FloorNumber, position, room);
                occupied.Add(position);
            }
            if (data.RoomType == RoomType.Shop)
                PlaceShop(room, occupied);
            if (data.HasPlayerSpawn)
                occupied.Add(room.ToWorld(data.PlayerSpawn));
            // Salle sans ennemis : le feu de camp brûle dès l'arrivée.
            if (!room.IsCombat)
                PlaceCampfire(room, occupied);
        }

        /// <summary>Feu de camp de la salle, un peu sous son centre (le centre est réservé au portail du boss).</summary>
        void PlaceCampfire(RoomInstance room, HashSet<Vector3> taken)
        {
            var position = NearestFloor(room, room.Center + new Vector3(0f, -1.5f, 0f), taken);
            Campfire.Spawn(this, position, room.Data.CampfireHealPercent(currentFloor));
            taken.Add(position);
        }

        FloorData currentFloor;

        /// <summary>
        /// Repos au feu de camp : soigne selon la règle de la salle (ou de son étage), puis ouvre la répartition des points d'attributs.
        /// </summary>
        public void OpenCampfire(int healPercent)
        {
            var healed = Mathf.Min(State.MaxHp - State.Hp, Mathf.RoundToInt(State.MaxHp * healPercent / 100f));
            if (healed > 0)
            {
                State.Heal(healed);
                GameFx.Float(Player.transform.position, $"+{healed} PV", new Color(0.5f, 1f, 0.6f));
                Toast($"Vous vous reposez. {healed} PV rendus.", new Color(0.5f, 1f, 0.6f));
            }
            campfireOpenedFrame = Time.frameCount;
            SetMode(Mode.Campfire);
        }


        /// <summary>Boutique : trois objets tirés des tables de coffre du biome, vendus à leur prix.</summary>
        void PlaceShop(RoomInstance room, HashSet<Vector3> occupied)
        {
            var biome = room.Data.Biome;
            if (biome == null)
                return;
            var stock = biome.ChestLootTables.Where(t => t != null).SelectMany(t => t.Entries).Select(e => e.item)
                .Where(i => i != null).Distinct().OrderBy(_ => random.Next()).Take(3).ToList();
            for (var i = 0; i < stock.Count; i++)
            {
                var position = NearestFloor(room, room.Center + new Vector3((i - 1) * 2f, 0.5f, 0f), occupied);
                ShopPedestal.Spawn(this, stock[i], position);
                occupied.Add(position);
            }
        }

        /// <summary>
        /// PNJ de l'étage. Ceux posés dans une salle (outil Salles) apparaissent à leur case, ou une fois la salle nettoyée.
        /// Les autres PNJ de l'étage (outil Dialogues) vont dans les salles de PNJ, sinon dans les salles calmes : départ, boutique, trésor.
        /// </summary>
        void PlaceNpcs(HashSet<Vector3> occupied)
        {
            var placed = new HashSet<NpcData>();
            foreach (var room in World.Rooms)
            {
                foreach (var spawn in room.Data.Npcs.Where(s => s.npc != null))
                {
                    placed.Add(spawn.npc);
                    if (spawn.afterClear && room.IsCombat)
                        continue;
                    var position = room.ToWorld(spawn.position);
                    NpcActor.Spawn(this, spawn.npc, position);
                    occupied.Add(position);
                }
            }
            if (IsTestRun && testRoom != null)
                return;
            var npcs = database.npcs.Where(n => n != null && n.AppearsOn(FloorNumber) && !placed.Contains(n)).ToList();
            var calm = World.Rooms.Where(r => r.Data.RoomType == RoomType.Npc).ToList();
            if (calm.Count == 0)
                calm = World.Rooms.Where(r => r.Data.RoomType is RoomType.Start or RoomType.Shop or RoomType.Treasure).ToList();
            if (calm.Count == 0)
                calm = World.Rooms.Take(1).ToList();
            for (var i = 0; i < npcs.Count; i++)
            {
                var room = calm[i % calm.Count];
                var position = NearestFloor(room, room.Center + new Vector3(-2f + i % 3 * 2f, -1.5f, 0f), occupied);
                NpcActor.Spawn(this, npcs[i], position);
                occupied.Add(position);
            }
        }

        /// <summary>Case de sol libre la plus proche (recherche en spirale).</summary>
        Vector3 NearestFloor(RoomInstance room, Vector3 around, HashSet<Vector3> occupied = null)
        {
            var origin = new Vector2Int(Mathf.FloorToInt(around.x) - room.Origin.x, Mathf.FloorToInt(around.y) - room.Origin.y);
            for (var radius = 0; radius < 12; radius++)
                for (var dy = -radius; dy <= radius; dy++)
                    for (var dx = -radius; dx <= radius; dx++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != radius)
                            continue;
                        var local = origin + new Vector2Int(dx, dy);
                        if (local.x < 1 || local.y < 1 || local.x >= room.Data.Width - 1 || local.y >= room.Data.Height - 1)
                            continue;
                        var world = room.ToWorld(local);
                        if (room.Data.GetTile(local.x, local.y) == TileType.Floor && (occupied == null || !occupied.Contains(world)) &&
                            !room.Data.Enemies.Any(e => e.position == local))
                            return world;
                    }
            return room.Center;
        }

        void ClearWorld()
        {
            foreach (var go in tracked.Where(go => go != null))
                Destroy(go);
            tracked.Clear();
            interactables.Clear();
            if (World != null)
            {
                foreach (var enemy in World.Rooms.SelectMany(r => r.Alive).Where(e => e != null))
                    Destroy(enemy.gameObject);
                Destroy(World.Root);
            }
            foreach (var projectile in FindObjectsByType<Projectile>(FindObjectsSortMode.None))
                Destroy(projectile.gameObject);
            if (Player != null)
                Destroy(Player.gameObject);
            GameFx.Clear();
        }
    }
}
