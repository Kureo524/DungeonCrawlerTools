using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using UnityEngine;

namespace TheDungeon.Game
{
    /// <summary>
    /// Point d'entrée du jeu. Construit chaque étage à partir des données produites par les outils
    /// (plans d'étage, salles, biomes, ennemis, butin, progression, dialogues) et orchestre la partie.
    /// </summary>
    public sealed class DungeonGame : MonoBehaviour
    {
        /// <summary>Clés de lancement posées par les outils (« Tester cet étage », « Tester cette salle »).</summary>
        public const string PlayFloorKey = "forge.play.floor";
        public const string PlayRoomKey = "forge.play.room";

        const float InteractRadius = 1.4f;

        public enum Mode
        {
            Title,
            Playing,
            Dialogue,
            Inventory,
            Paused,
            GameOver,
            Victory
        }

        [SerializeField] GameDatabase database;

        readonly List<MonoBehaviour> interactables = new();
        readonly List<GameObject> tracked = new();
        readonly System.Random random = new();

        string testRoom;
        int testFloor = -1;

        public GameDatabase Database => database;
        public Mode Current { get; private set; } = Mode.Title;
        public bool IsPlaying => Current == Mode.Playing;
        public GameState State { get; private set; }
        public DialogueRunner Dialogue { get; private set; }
        public PlayerController Player { get; private set; }
        public Camera Camera { get; private set; }
        public CameraFollow CameraFollow { get; private set; }
        public FloorWorld World { get; private set; }
        public RoomInstance CurrentRoom { get; private set; }
        public int FloorNumber { get; private set; } = 1;
        public string FloorTitle { get; private set; } = string.Empty;
        public bool IsTestRun => testRoom != null || testFloor >= 0;
        public readonly List<(string text, Color color, float time)> Toasts = new();

        public bool HasPlayableData => database != null && (database.floors.Count > 0 || IsTestRun);

        public void SetDatabase(GameDatabase value) => database = value;

        void Awake()
        {
            Camera = Camera.main;
            if (Camera == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                Camera = go.AddComponent<Camera>();
            }
            Camera.orthographic = true;
            Camera.orthographicSize = 7f;
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = new Color(0.05f, 0.04f, 0.07f);
            // Les éléments plus bas à l'écran passent devant : profondeur naturelle en vue du dessus.
            Camera.transparencySortMode = TransparencySortMode.CustomAxis;
            Camera.transparencySortAxis = Vector3.up;
            // Pas d'opérateur ?? avec les composants Unity : un composant absent n'est pas « null » au sens C#.
            CameraFollow = Camera.GetComponent<CameraFollow>();
            if (CameraFollow == null)
                CameraFollow = Camera.gameObject.AddComponent<CameraFollow>();
            if (GetComponent<GameHud>() == null)
                gameObject.AddComponent<GameHud>();

            ReadLaunchOptions();
            if (IsTestRun && HasPlayableData)
                BeginRun();
        }

        void ReadLaunchOptions()
        {
            if (PlayerPrefs.HasKey(PlayRoomKey))
                testRoom = PlayerPrefs.GetString(PlayRoomKey);
            if (PlayerPrefs.HasKey(PlayFloorKey))
                testFloor = PlayerPrefs.GetInt(PlayFloorKey);
            PlayerPrefs.DeleteKey(PlayRoomKey);
            PlayerPrefs.DeleteKey(PlayFloorKey);
        }

        void Update()
        {
            GameFx.Tick(Time.unscaledDeltaTime);
            Toasts.RemoveAll(t => Time.unscaledTime - t.time > 2.5f);

            switch (Current)
            {
                case Mode.Title:
                    if (GameInput.Confirm && HasPlayableData)
                        BeginRun();
                    break;
                case Mode.Playing:
                    if (GameInput.Pause) SetMode(Mode.Paused);
                    else if (GameInput.Inventory) SetMode(Mode.Inventory);
                    State.Tick(Time.deltaTime);
                    UpdateRooms();
                    break;
                case Mode.Dialogue:
                    UpdateDialogue();
                    break;
                case Mode.Inventory:
                    if (GameInput.Inventory || GameInput.Pause) SetMode(Mode.Playing);
                    break;
                case Mode.Paused:
                    if (GameInput.Pause) SetMode(Mode.Playing);
                    else if (GameInput.Restart) BeginRun();
                    break;
                case Mode.GameOver:
                case Mode.Victory:
                    if (GameInput.Confirm || GameInput.Restart) BeginRun();
                    break;
            }
        }

        public void SetMode(Mode mode)
        {
            Current = mode;
            Time.timeScale = mode == Mode.Playing ? 1f : 0f;
        }

        public void BeginRun()
        {
            State = new GameState(database.progression);
            State.Message += Toast;
            Dialogue = new DialogueRunner(State);
            var index = testFloor >= 0 ? database.floors.FindIndex(f => f != null && f.FloorNumber == testFloor) : 0;
            LoadFloor(Mathf.Max(0, index));
            SetMode(Mode.Playing);
        }

        #region Construction d'un étage

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
            }
            else
            {
                var floor = database.floors[index];
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

            var occupied = new HashSet<Vector3>();
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
                Chest.Spawn(this, chest.lootTable, data.Biome, FloorNumber, position);
                occupied.Add(position);
            }
            if (data.RoomType == RoomType.Shop)
                PlaceShop(room, occupied);
            if (data.HasPlayerSpawn)
                occupied.Add(room.ToWorld(data.PlayerSpawn));
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

        /// <summary>Les PNJ de l'étage (outil Dialogues) sont placés dans les salles calmes : départ, boutique, trésor.</summary>
        void PlaceNpcs(HashSet<Vector3> occupied)
        {
            if (IsTestRun && testRoom != null)
                return;
            var npcs = database.npcs.Where(n => n != null && n.Floor == FloorNumber).ToList();
            var calm = World.Rooms.Where(r => r.Data.RoomType is RoomType.Start or RoomType.Shop or RoomType.Treasure).ToList();
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

        #endregion

        #region Salles et combat

        void UpdateRooms()
        {
            if (Player == null || World == null)
                return;
            var room = World.RoomAt(Player.transform.position);
            if (room != null && room != CurrentRoom)
            {
                CurrentRoom = room;
                room.Visited = true;
                if (!room.Activated)
                    Activate(room);
            }
            if (CurrentRoom != null && CurrentRoom.Activated && !CurrentRoom.Cleared && CurrentRoom.Alive.Count == 0)
                Clear(CurrentRoom);
        }

        /// <summary>Entrée dans une salle de combat : les ennemis apparaissent et les portes se ferment jusqu'à la victoire.</summary>
        void Activate(RoomInstance room)
        {
            room.Activated = true;
            if (!room.IsCombat)
            {
                room.Cleared = true;
                return;
            }
            foreach (var spawn in room.Data.Enemies.Where(s => s.enemy != null))
                EnemyController.Spawn(this, spawn.enemy, FloorNumber, room, room.ToWorld(spawn.position));
            World.LockDoors(room);
            var boss = room.Alive.FirstOrDefault(e => e.IsBoss);
            Toast(boss != null ? $"{boss.Data.DisplayName} vous barre la route !" : "Les portes se referment !",
                boss != null ? new Color(1f, 0.4f, 0.35f) : new Color(1f, 0.75f, 0.5f));
            CameraFollow.Shake(boss != null ? 0.4f : 0.15f);
        }

        void Clear(RoomInstance room)
        {
            room.Cleared = true;
            World.UnlockDoors(room);
            if (room.Data.RoomType == RoomType.Boss || room.Data.ContainsBoss())
            {
                var last = IsTestRun || currentFloorIndex + 1 >= database.floors.Count;
                Portal.Spawn(this, NearestFloor(room, room.Center), last);
                Toast(last ? "Le donjon est vaincu ! Un portail s'ouvre." : "Le boss est vaincu ! Un portail s'ouvre.", new Color(0.6f, 0.9f, 1f));
            }
            else
            {
                Toast("Salle nettoyée", new Color(0.6f, 1f, 0.6f));
            }
        }

        public void OnEnemyKilled(EnemyController enemy, RoomInstance room)
        {
            State.Kills++;
            var xp = enemy.Data.GetXpReward(FloorNumber);
            State.AddXp(xp);
            GameFx.Float(enemy.transform.position + Vector3.up * 0.6f, $"+{xp} XP", new Color(0.6f, 0.8f, 1f), 0.8f);
            DropLoot(enemy.Data.LootTable, room.Data.Biome, FloorNumber, enemy.transform.position, false);
            if (random.NextDouble() < 0.6)
                Pickup.SpawnGold(this, random.Next(1, 3 + FloorNumber * 2) * (enemy.IsBoss ? 10 : 1), enemy.transform.position);
            if (enemy.IsBoss)
                CameraFollow.Shake(0.6f);
        }

        /// <summary>
        /// Tire une table de butin (conditions d'apparition : étages de la table ; bonus de rareté du biome).
        /// Renvoie le nombre d'objets lâchés.
        /// </summary>
        public int DropLoot(LootTable table, BiomeData biome, int floor, Vector3 position, bool guaranteed)
        {
            if (table == null || floor < table.MinFloor || floor > table.MaxFloor)
                return 0;
            var count = 0;
            var attempts = guaranteed ? 3 : 1;
            for (var attempt = 0; attempt < attempts && count == 0; attempt++)
            {
                for (var roll = 0; roll < Mathf.Max(1, table.Rolls); roll++)
                {
                    var item = table.Roll(random, biome != null ? biome.GetRarityMultiplier : null);
                    if (item == null)
                        continue;
                    Pickup.SpawnItem(this, item, position + (Vector3)(Random.insideUnitCircle * 0.6f));
                    count++;
                }
            }
            return count;
        }

        public void OnPlayerDied()
        {
            SetMode(Mode.GameOver);
            Player.gameObject.SetActive(false);
        }

        public void NextFloor()
        {
            if (IsTestRun || currentFloorIndex + 1 >= database.floors.Count)
            {
                SetMode(Mode.Victory);
                return;
            }
            LoadFloor(currentFloorIndex + 1);
            State.Heal(State.MaxHp / 2);
        }

        #endregion

        #region Interactions et dialogues

        public void Register(IInteractable interactable) => interactables.Add((MonoBehaviour)interactable);

        public void Track(GameObject go) => tracked.Add(go);

        public IInteractable Nearest(Vector3 position) => interactables
            .Where(i => i != null)
            .Cast<IInteractable>()
            .Where(i => i.CanInteract && (i.Position - position).sqrMagnitude < InteractRadius * InteractRadius)
            .OrderBy(i => (i.Position - position).sqrMagnitude)
            .FirstOrDefault();

        public void StartDialogue(NpcData npc)
        {
            Dialogue.Start(npc);
            if (Dialogue.Active)
                SetMode(Mode.Dialogue);
        }

        void UpdateDialogue()
        {
            Dialogue.Tick(Time.unscaledDeltaTime);
            var choice = GameInput.Choice;
            if (choice >= 0)
                Dialogue.Choose(choice);
            else if (GameInput.Confirm || GameInput.Interact)
                Dialogue.Advance();
            if (!Dialogue.Active)
                SetMode(State.IsDead ? Mode.GameOver : Mode.Playing);
        }

        public void Toast(string text, Color color) => Toasts.Add((text, color, Time.unscaledTime));

        #endregion
    }
}
