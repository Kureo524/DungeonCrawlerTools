using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using TheDungeon.Editor.Validation;
using TheDungeon.Game;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDungeon.Editor.Play
{
    /// <summary>
    /// Lance le jeu depuis les outils : met à jour la base de jeu (données produites par les outils),
    /// crée la scène si besoin et démarre la partie, éventuellement sur un étage ou une salle précis.
    /// </summary>
    [InitializeOnLoad]
    public static class GameLauncher
    {
        public const string ScenePath = DungeonPaths.Root + "/Scenes/Game.unity";
        public const string DatabasePath = DungeonPaths.Settings + "/GameDatabase.asset";

        static GameLauncher()
        {
            // Même un lancement par le bouton Play d'Unity utilise des données à jour.
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode)
                    RefreshDatabase();
            };
        }

        [MenuItem("Tools/Jouer", priority = 1)]
        public static void PlayFromStart() => Play(-1, null);

        [MenuItem("Tools/Jeu/Mettre à jour la base de jeu", priority = 61)]
        static void RefreshFromMenu()
        {
            var database = RefreshDatabase();
            Debug.Log($"[Forge] Base de jeu à jour : {database.floors.Count} étage(s), {database.rooms.Count} salle(s), {database.npcs.Count} PNJ.", database);
        }

        /// <summary>Crée (ou répare) la scène de jeu et met à jour la base de jeu, sans lancer la partie.</summary>
        [MenuItem("Tools/Jeu/Créer ou réparer la scène de jeu", priority = 62)]
        public static void SetUpGameScene()
        {
            var database = RefreshDatabase();
            EnsureScene(database);
            var scene = EditorSceneManager.OpenScene(ScenePath);
            foreach (var game in Object.FindObjectsByType<DungeonGame>(FindObjectsSortMode.None))
                if (game.Database != database)
                {
                    game.SetDatabase(database);
                    EditorUtility.SetDirty(game);
                }
            EditorSceneManager.SaveScene(scene);
        }

        public static void PlayFloor(int floorNumber) => Play(floorNumber, null);

        public static void PlayRoom(RoomData room) => Play(-1, room.name);

        static void Play(int floorNumber, string roomName)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                ForgeLists.Notify("Le jeu est déjà lancé.");
                return;
            }

            var errors = ValidationRunner.Run(ValidationContext.FromProject()).Count(i => i.Severity == Severity.Error);
            if (errors > 0 && !ForgeDialogs.Confirm("Données invalides",
                    $"Le projet contient {errors} erreur(s) (voir le tableau de bord de Forge Studio). Le jeu risque de mal se comporter.\n\nJouer quand même ?",
                    "Jouer quand même", "Annuler"))
                return;

            var database = RefreshDatabase();
            if (roomName == null && database.floors.Count == 0)
            {
                ForgeDialogs.Inform("Rien à jouer", "Aucun étage n'existe. Créez au moins un étage dans Forge Studio > Étages.");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            EnsureScene(database);
            if (SceneManager.GetActiveScene().path != ScenePath)
                EditorSceneManager.OpenScene(ScenePath);

            PlayerPrefs.DeleteKey(DungeonGame.PlayFloorKey);
            PlayerPrefs.DeleteKey(DungeonGame.PlayRoomKey);
            if (floorNumber >= 0)
                PlayerPrefs.SetInt(DungeonGame.PlayFloorKey, floorNumber);
            if (roomName != null)
                PlayerPrefs.SetString(DungeonGame.PlayRoomKey, roomName);
            PlayerPrefs.Save();
            EditorApplication.EnterPlaymode();
        }

        /// <summary>
        /// Progression jouée : celle choisie dans l'outil Progression (mémorisée dans la base de jeu), sinon la première trouvée.
        /// </summary>
        public static PlayerProgression ActiveProgression
        {
            get
            {
                var all = AssetFactory.LoadAll<PlayerProgression>();
                var chosen = AssetDatabase.LoadAssetAtPath<GameDatabase>(DatabasePath)?.progression;
                return chosen != null && all.Contains(chosen) ? chosen : all.FirstOrDefault();
            }
            set
            {
                var database = LoadOrCreateDatabase();
                database.progression = value;
                EditorUtility.SetDirty(database);
                AssetDatabase.SaveAssetIfDirty(database);
            }
        }

        static GameDatabase LoadOrCreateDatabase()
        {
            var database = AssetDatabase.LoadAssetAtPath<GameDatabase>(DatabasePath);
            if (database != null)
                return database;
            database = ScriptableObject.CreateInstance<GameDatabase>();
            AssetFactory.EnsureFolder(DungeonPaths.Settings);
            AssetDatabase.CreateAsset(database, DatabasePath);
            return database;
        }

        /// <summary>Rassemble dans la base de jeu tout ce que les outils ont produit.</summary>
        public static GameDatabase RefreshDatabase()
        {
            var progression = ActiveProgression;
            var database = LoadOrCreateDatabase();
            database.floors = AssetFactory.LoadAll<FloorData>().Where(f => f.Rooms.Any(s => s.room != null)).OrderBy(f => f.FloorNumber).ToList();
            database.progression = progression;
            database.npcs = AssetFactory.LoadAll<NpcData>();
            database.rooms = AssetFactory.LoadAll<RoomData>().Where(r => r.HasValidTiles).ToList();
            database.rarityColors = ForgeDesignLimits.Instance.rarities
                .Select(r => new RarityColor { rarity = r.rarity, color = r.color }).ToList();

            database.player = Sprite("Common/SPR_Player");
            database.chestClosed = Sprite("Common/SPR_Chest");
            database.chestOpen = Sprite("Common/SPR_ChestOpen");
            database.doorClosed = Sprite("Common/SPR_DoorClosed");
            database.portal = Sprite("Items/Misc/SPR_Orb");
            database.gold = Sprite("Items/Misc/SPR_GoldPile");
            database.pedestal = Sprite("Biomes/Ice/SPR_IcePedestal");

            EditorUtility.SetDirty(database);
            AssetDatabase.SaveAssetIfDirty(database);
            return database;
        }

        static Sprite Sprite(string path) => AssetDatabase.LoadAssetAtPath<Sprite>($"{DungeonPaths.Art}/{path}.png");

        /// <summary>Crée la scène de jeu (caméra + DungeonGame) si elle n'existe pas, et l'ajoute aux scènes du build.</summary>
        public static void EnsureScene(GameDatabase database)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var camera = new GameObject("Main Camera") { tag = "MainCamera" };
                camera.AddComponent<Camera>();
                camera.transform.position = new Vector3(0f, 0f, -10f);
                var game = new GameObject("The Dungeon");
                game.AddComponent<DungeonGame>().SetDatabase(database);
                AssetFactory.EnsureFolder(DungeonPaths.Root + "/Scenes");
                EditorSceneManager.SaveScene(scene, ScenePath);
            }

            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }

    /// <summary>Un build embarque toujours des données à jour.</summary>
    public sealed class RefreshDatabaseBeforeBuild : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;
        public void OnPreprocessBuild(BuildReport report) => GameLauncher.RefreshDatabase();
    }
}
