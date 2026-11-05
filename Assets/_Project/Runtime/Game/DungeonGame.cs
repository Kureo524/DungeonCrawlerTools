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
    public sealed partial class DungeonGame : MonoBehaviour
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
            Campfire,
            Paused,
            GameOver,
            Victory
        }

        [SerializeField] GameDatabase database;

        readonly List<MonoBehaviour> interactables = new();
        readonly List<GameObject> tracked = new();
        readonly System.Random random = new();
        // Cases déjà prises sur l'étage (décors, coffres, PNJ, portail…) : un feu de camp allumé plus tard ne s'y pose pas.
        HashSet<Vector3> occupied = new();
        int campfireOpenedFrame = -1;

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
                case Mode.Campfire:
                    // La touche qui a ouvert le feu de camp ne doit pas le refermer dans la même image.
                    if (Time.frameCount != campfireOpenedFrame && (GameInput.Interact || GameInput.Inventory || GameInput.Pause))
                        SetMode(Mode.Playing);
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
            State = new GameState(database.progression) { RarityRank = database.GetRarityRank };
            State.Message += Toast;
            Dialogue = new DialogueRunner(State);
            var index = testFloor >= 0 ? database.floors.FindIndex(f => f != null && f.FloorNumber == testFloor) : 0;
            LoadFloor(Mathf.Max(0, index));
            SetMode(Mode.Playing);
        }

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
            Toast(boss != null ? $"'{boss.Data.DisplayName}' vous barre la route." : "Les portes se referment.",
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
                var portal = NearestFloor(room, room.Center, occupied);
                Portal.Spawn(this, portal, last);
                occupied.Add(portal);
                Toast(last ? "Le donjon est vaincu. Un portail s'ouvre." : "Le boss est vaincu. Un portail s'ouvre.", new Color(0.6f, 0.9f, 1f));
            }
            else
            {
                Toast("Salle nettoyée. Un feu de camp s'allume.", new Color(0.6f, 1f, 0.6f));
            }
            // Personnages qui attendaient la fin du combat.
            foreach (var spawn in room.Data.Npcs.Where(s => s.npc != null && s.afterClear))
            {
                var position = room.ToWorld(spawn.position);
                NpcActor.Spawn(this, spawn.npc, position);
                occupied.Add(position);
                GameFx.Float(position + Vector3.up * 0.6f, spawn.npc.DisplayName, spawn.npc.Color);
                Toast($"'{spawn.npc.DisplayName}' apparaît.", new Color(0.8f, 0.85f, 1f));
            }
            PlaceCampfire(room, occupied);

        }

        public void OnEnemyKilled(EnemyController enemy, RoomInstance room)
        {
            State.Kills++;
            var feast = Mathf.RoundToInt(State.Passive(EffectType.KillHeal));
            if (feast > 0 && State.Hp < State.MaxHp)
            {
                State.Heal(feast);
                GameFx.Float(Player.transform.position, $"+{feast} PV", new Color(0.5f, 1f, 0.6f), 0.8f);
            }
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

        /// <summary>Message système à l'écran, écrit comme dans le roman : entre crochets.</summary>
        public void Toast(string text, Color color) =>
            Toasts.Add((text.StartsWith("[") ? text : $"[{text.Trim()}]", color, Time.unscaledTime));


        #endregion
    }
}
