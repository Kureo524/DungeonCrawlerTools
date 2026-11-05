using System.Linq;
using TheDungeon.Data;
using UnityEngine;
using static TheDungeon.Game.HudSkin;

namespace TheDungeon.Game
{
    /// <summary>
    /// Interface du jeu (IMGUI, mise à l'échelle sur une hauteur de 720) : écrans titre / pause / fin, et pendant la partie
    /// le HUD de jeu (<see cref="HudGameplay"/>) et les fenêtres de dialogue, d'inventaire et de feu de camp (<see cref="HudMenus"/>).
    /// </summary>
    [RequireComponent(typeof(DungeonGame))]
    public sealed class GameHud : MonoBehaviour
    {
        readonly HudSkin skin = new();
        DungeonGame game;
        HudGameplay gameplay;
        HudMenus menus;

        void Awake()
        {
            game = GetComponent<DungeonGame>();
            gameplay = new HudGameplay(game, skin);
            menus = new HudMenus(game, skin);
        }

        void OnGUI()
        {
            skin.Begin();

            if (game.Current == DungeonGame.Mode.Title)
            {
                DrawTitle();
                return;
            }

            gameplay.DrawWorldOverlays();
            gameplay.DrawStatus();
            gameplay.DrawMinimap();
            gameplay.DrawBossBar();
            gameplay.DrawToasts();

            switch (game.Current)
            {
                case DungeonGame.Mode.Playing:
                    gameplay.DrawPrompt();
                    gameplay.DrawMechanics();
                    break;
                case DungeonGame.Mode.Dialogue:
                    menus.DrawDialogue();
                    break;
                case DungeonGame.Mode.Inventory:
                    menus.DrawInventory();
                    break;
                case DungeonGame.Mode.Campfire:
                    menus.DrawCampfire();
                    break;
                case DungeonGame.Mode.Paused:
                    DrawCenterScreen("Pause", "Échap : reprendre    R : recommencer la partie", Muted);
                    break;
                case DungeonGame.Mode.GameOver:
                    DrawEnd("Vous êtes mort", Health);
                    break;
                case DungeonGame.Mode.Victory:
                    DrawEnd(game.IsTestRun ? "Test terminé" : "Le donjon est vaincu !", Gold);
                    break;
            }
        }

        #region Écrans

        void DrawTitle()
        {
            Fill(new Rect(0, 0, skin.Width, ReferenceHeight), new Color(0.04f, 0.03f, 0.06f, 1f));
            GUI.Label(new Rect(0, 150, skin.Width, 60), "<color=#e8a05a>THE DUNGEON</color>", skin.Title);
            GUI.Label(new Rect(0, 210, skin.Width, 30), "Trois étages. Un boss par étage. Descendez.", skin.Centered);

            var database = game.Database;
            if (!game.HasPlayableData)
            {
                GUI.Label(new Rect(skin.Width / 2 - 320, 300, 640, 80),
                    "<color=#ff8a80>Aucun étage jouable.</color>\nCréez des étages valides dans Forge Studio > Étages, puis relancez avec « Jouer ».", skin.Centered);
                return;
            }
            var pulse = 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 3f);
            GUI.Label(new Rect(0, 290, skin.Width, 30), $"<color=#{ColorUtility.ToHtmlStringRGB(Color.Lerp(Muted, Color.white, pulse))}>Appuyez sur Entrée pour commencer</color>", skin.Big);

            var controls = new Rect(skin.Width / 2 - 260, 360, 520, 190);
            Fill(controls, Panel);
            GUI.Label(new Rect(controls.x + 20, controls.y + 12, 480, 170),
                "<b>Commandes</b>\n" +
                "ZQSD / WASD : se déplacer        Souris : viser, clic : tirer\n" +
                "Espace : esquive                       E : interagir (coffre, PNJ, portail)\n" +
                "F : utiliser un consommable        I : inventaire        Échap : pause\n" +
                "Manette : sticks, gâchette droite, A / B / Y\n\n" +
                $"<color=#bbbbbb>Contenu chargé : {database.floors.Count} étage(s), {database.floors.Sum(f => f.Rooms.Count)} salle(s), {database.npcs.Count} PNJ.</color>",
                skin.Small);
        }

        void DrawCenterScreen(string heading, string detail, Color color)
        {
            Fill(new Rect(0, 0, skin.Width, ReferenceHeight), new Color(0f, 0f, 0f, 0.6f));
            GUI.Label(new Rect(0, 260, skin.Width, 60), $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>{heading}</color>", skin.Title);
            GUI.Label(new Rect(0, 330, skin.Width, 30), detail, skin.Centered);
        }

        void DrawEnd(string heading, Color color)
        {
            var state = game.State;
            DrawCenterScreen(heading,
                $"Niveau {state.Level}   ·   Étage {state.Floor}   ·   {state.Kills} ennemi(s) vaincu(s)   ·   {state.Gold} or\n\nEntrée : rejouer", color);
        }

        #endregion
    }
}
