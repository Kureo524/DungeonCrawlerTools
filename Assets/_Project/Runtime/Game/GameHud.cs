using System.Linq;
using TheDungeon.Data;
using UnityEngine;

namespace TheDungeon.Game
{
    /// <summary>
    /// Interface du jeu (IMGUI, mise à l'échelle sur une hauteur de 720) : vie, XP, or, équipement, mini-carte,
    /// boss, dialogues, inventaire et écrans titre / pause / fin.
    /// </summary>
    [RequireComponent(typeof(DungeonGame))]
    public sealed class GameHud : MonoBehaviour
    {
        const float ReferenceHeight = 720f;

        static readonly Color Panel = new(0.06f, 0.05f, 0.09f, 0.82f);
        static readonly Color Gold = new(1f, 0.85f, 0.35f);
        static readonly Color Health = new(0.86f, 0.25f, 0.28f);
        static readonly Color Experience = new(0.3f, 0.6f, 1f);
        static readonly Color Muted = new(0.75f, 0.72f, 0.68f);

        DungeonGame game;
        float scale;
        float width;
        GUIStyle text;
        GUIStyle small;
        GUIStyle centered;
        GUIStyle title;
        GUIStyle big;
        GUIStyle choice;

        void Awake() => game = GetComponent<DungeonGame>();

        void OnGUI()
        {
            scale = Screen.height / ReferenceHeight;
            width = Screen.width / scale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            EnsureStyles();

            if (game.Current == DungeonGame.Mode.Title)
            {
                DrawTitle();
                return;
            }

            DrawWorldOverlays();
            DrawStatus();
            DrawMinimap();
            DrawBossBar();
            DrawToasts();

            switch (game.Current)
            {
                case DungeonGame.Mode.Playing:
                    DrawPrompt();
                    break;
                case DungeonGame.Mode.Dialogue:
                    DrawDialogue();
                    break;
                case DungeonGame.Mode.Inventory:
                    DrawInventory();
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

        void EnsureStyles()
        {
            if (text != null)
                return;
            text = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true, richText = true };
            text.normal.textColor = new Color(0.95f, 0.93f, 0.9f);
            small = new GUIStyle(text) { fontSize = 12 };
            centered = new GUIStyle(text) { alignment = TextAnchor.MiddleCenter };
            title = new GUIStyle(centered) { fontSize = 44, fontStyle = FontStyle.Bold };
            big = new GUIStyle(centered) { fontSize = 22, fontStyle = FontStyle.Bold };
            choice = new GUIStyle(GUI.skin.button) { fontSize = 15, alignment = TextAnchor.MiddleLeft, wordWrap = true, richText = true, padding = new RectOffset(12, 12, 6, 6) };
            choice.normal.textColor = new Color(0.95f, 0.93f, 0.9f);
            choice.hover.textColor = Gold;
        }

        #region Écrans

        void DrawTitle()
        {
            Fill(new Rect(0, 0, width, ReferenceHeight), new Color(0.04f, 0.03f, 0.06f, 1f));
            GUI.Label(new Rect(0, 150, width, 60), "<color=#e8a05a>THE DUNGEON</color>", title);
            GUI.Label(new Rect(0, 210, width, 30), "Trois étages. Un boss par étage. Descendez.", centered);

            var database = game.Database;
            if (!game.HasPlayableData)
            {
                GUI.Label(new Rect(width / 2 - 320, 300, 640, 80),
                    "<color=#ff8a80>Aucun étage jouable.</color>\nCréez des étages valides dans Forge Studio > Étages, puis relancez avec « Jouer ».", centered);
                return;
            }
            var pulse = 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 3f);
            GUI.Label(new Rect(0, 290, width, 30), $"<color=#{ColorUtility.ToHtmlStringRGB(Color.Lerp(Muted, Color.white, pulse))}>Appuyez sur Entrée pour commencer</color>", big);

            var controls = new Rect(width / 2 - 260, 360, 520, 190);
            Fill(controls, Panel);
            GUI.Label(new Rect(controls.x + 20, controls.y + 12, 480, 170),
                "<b>Commandes</b>\n" +
                "ZQSD / WASD : se déplacer        Souris : viser, clic : tirer\n" +
                "Espace : esquive                       E : interagir (coffre, PNJ, portail)\n" +
                "F : utiliser un consommable        I : inventaire        Échap : pause\n" +
                "Manette : sticks, gâchette droite, A / B / Y\n\n" +
                $"<color=#bbbbbb>Contenu chargé : {database.floors.Count} étage(s), {database.floors.Sum(f => f.Rooms.Count)} salle(s), {database.npcs.Count} PNJ.</color>",
                small);
        }

        void DrawCenterScreen(string heading, string detail, Color color)
        {
            Fill(new Rect(0, 0, width, ReferenceHeight), new Color(0f, 0f, 0f, 0.6f));
            GUI.Label(new Rect(0, 260, width, 60), $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>{heading}</color>", title);
            GUI.Label(new Rect(0, 330, width, 30), detail, centered);
        }

        void DrawEnd(string heading, Color color)
        {
            var state = game.State;
            DrawCenterScreen(heading,
                $"Niveau {state.Level}   ·   Étage {state.Floor}   ·   {state.Kills} ennemi(s) vaincu(s)   ·   {state.Gold} or\n\nEntrée : rejouer", color);
        }

        #endregion

        #region Jeu

        void DrawStatus()
        {
            var state = game.State;
            var panel = new Rect(14, 14, 300, 116);
            Fill(panel, Panel);
            Bar(new Rect(26, 26, 276, 20), state.Hp / (float)state.MaxHp, Health, $"PV  {state.Hp} / {state.MaxHp}");
            var xpRatio = state.XpToNext > 0 ? state.Xp / (float)state.XpToNext : 1f;
            Bar(new Rect(26, 52, 276, 14), xpRatio, Experience, state.XpToNext > 0 ? $"Niveau {state.Level}   {state.Xp} / {state.XpToNext} XP" : $"Niveau {state.Level} (max)");
            GUI.Label(new Rect(26, 72, 140, 22), $"<color=#{ColorUtility.ToHtmlStringRGB(Gold)}>{state.Gold} or</color>", text);
            var potions = state.Inventory.Count(i => i.Type == ItemType.Consumable);
            GUI.Label(new Rect(120, 72, 190, 22), $"F : consommable ×{potions}", small);
            GUI.Label(new Rect(26, 94, 200, 22), $"ATQ {state.Attack}   DÉF {state.Defense}", small);
            if (game.Player != null)
                Bar(new Rect(196, 98, 106, 8), game.Player.DashReady, Muted, null);

            var slots = new[] { (state.Weapon, "Arme"), (state.Armor, "Armure"), (state.Accessory, "Accessoire") };
            for (var i = 0; i < slots.Length; i++)
            {
                var rect = new Rect(14 + i * 52, 136, 46, 46);
                var (item, label) = slots[i];
                Fill(rect, Panel);
                if (item != null)
                {
                    Outline(rect, game.Database.GetRarityColor(item.Rarity), 2f);
                    DrawSprite(new Rect(rect.x + 7, rect.y + 7, 32, 32), item.Icon);
                }
                else
                {
                    GUI.Label(rect, $"<color=#777777>{label}</color>", new GUIStyle(centered) { fontSize = 9 });
                }
            }
        }

        void DrawMinimap()
        {
            var world = game.World;
            if (world == null || world.ByCell.Count == 0)
                return;
            const float cell = 20f;
            const float gap = 4f;
            var min = new Vector2Int(world.ByCell.Keys.Min(c => c.x), world.ByCell.Keys.Min(c => c.y));
            var max = new Vector2Int(world.ByCell.Keys.Max(c => c.x), world.ByCell.Keys.Max(c => c.y));
            var size = new Vector2((max.x - min.x + 1) * (cell + gap), (max.y - min.y + 1) * (cell + gap));
            var panel = new Rect(width - size.x - 34, 14, size.x + 20, size.y + 40);
            Fill(panel, Panel);
            GUI.Label(new Rect(panel.x, panel.yMax - 24, panel.width, 20), game.FloorTitle, new GUIStyle(centered) { fontSize = 11 });

            foreach (var (position, room) in world.ByCell)
            {
                var known = room.Visited || FloorNeighbours(world, position).Any(n => n.Visited);
                if (!known)
                    continue;
                var rect = new Rect(panel.x + 10 + (position.x - min.x) * (cell + gap), panel.y + 10 + (max.y - position.y) * (cell + gap), cell, cell);
                var color = room.Data.Biome != null ? room.Data.Biome.Color : Color.gray;
                Fill(rect, room.Visited ? new Color(color.r, color.g, color.b, 0.85f) : new Color(1f, 1f, 1f, 0.12f));
                if (room == game.CurrentRoom)
                    Outline(rect, Color.white, 2f);
                if (room.Data.ContainsBoss() && !room.Cleared)
                    Fill(new Rect(rect.center.x - 3, rect.center.y - 3, 6, 6), Health);
                if (room.Data.RoomType == RoomType.Shop)
                    Fill(new Rect(rect.center.x - 3, rect.center.y - 3, 6, 6), Gold);
            }
        }

        static System.Collections.Generic.IEnumerable<RoomInstance> FloorNeighbours(FloorWorld world, Vector2Int cell) =>
            FloorData.Sides.Select(side => cell + FloorData.Offset(side)).Where(world.ByCell.ContainsKey).Select(c => world.ByCell[c]);

        void DrawBossBar()
        {
            var boss = game.CurrentRoom?.Alive.FirstOrDefault(e => e != null && e.IsBoss);
            if (boss == null)
                return;
            var rect = new Rect(width / 2 - 260, ReferenceHeight - 70, 520, 22);
            GUI.Label(new Rect(rect.x, rect.y - 26, rect.width, 24), $"<b>{boss.Data.DisplayName}</b>", big);
            Bar(rect, boss.Hp / (float)boss.MaxHp, Health, $"{boss.Hp} / {boss.MaxHp}");
        }

        void DrawWorldOverlays()
        {
            var camera = game.Camera;
            if (camera == null)
                return;

            // Barres de vie des ennemis touchés récemment.
            if (game.CurrentRoom != null)
            {
                foreach (var enemy in game.CurrentRoom.Alive.Where(e => e != null && !e.IsBoss && Time.time - e.LastHit < 3f))
                {
                    var point = ToGui(camera, enemy.transform.position + Vector3.up * 0.7f);
                    Bar(new Rect(point.x - 18, point.y, 36, 5), enemy.Hp / (float)enemy.MaxHp, Health, null);
                }
            }

            foreach (var floating in GameFx.Texts)
            {
                var point = ToGui(camera, floating.Position);
                var style = new GUIStyle(centered) { fontSize = Mathf.RoundToInt(16 * floating.Size), fontStyle = FontStyle.Bold };
                var color = floating.Color;
                color.a = GameFx.Alpha(floating);
                style.normal.textColor = new Color(0f, 0f, 0f, color.a * 0.8f);
                GUI.Label(new Rect(point.x - 99, point.y - 11, 200, 24), floating.Text, style);
                style.normal.textColor = color;
                GUI.Label(new Rect(point.x - 100, point.y - 12, 200, 24), floating.Text, style);
            }
        }

        Vector2 ToGui(Camera camera, Vector3 world)
        {
            var screen = camera.WorldToScreenPoint(world);
            return new Vector2(screen.x / scale, (Screen.height - screen.y) / scale);
        }

        void DrawToasts()
        {
            var y = 150f;
            foreach (var (message, color, time) in game.Toasts.AsEnumerable().Reverse().Take(4))
            {
                var age = Time.unscaledTime - time;
                var alpha = Mathf.Clamp01(2.5f - age) * Mathf.Clamp01(age * 6f);
                var style = new GUIStyle(big) { fontSize = 18 };
                style.normal.textColor = new Color(color.r, color.g, color.b, alpha);
                var toastWidth = Mathf.Clamp(style.CalcSize(new GUIContent(message)).x + 40f, 240f, width - 40f);
                Fill(new Rect(width / 2 - toastWidth / 2, y, toastWidth, 30), new Color(0f, 0f, 0f, 0.45f * alpha));
                GUI.Label(new Rect(width / 2 - toastWidth / 2, y, toastWidth, 30), message, style);
                y += 34f;
            }
        }

        void DrawPrompt()
        {
            if (game.Player == null)
                return;
            var interactable = game.Nearest(game.Player.transform.position);
            if (interactable == null)
                return;
            // Le bandeau s'élargit avec son texte (nom d'objet long, prix…).
            var label = $"<color=#e8a05a><b>[E]</b></color>  {interactable.Prompt}";
            var promptWidth = Mathf.Clamp(centered.CalcSize(new GUIContent(label)).x + 40f, 240f, width - 40f);
            var rect = new Rect(width / 2 - promptWidth / 2, ReferenceHeight - 130, promptWidth, 34);
            Fill(rect, Panel);
            GUI.Label(rect, label, centered);
        }

        #endregion

        #region Dialogue et inventaire

        void DrawDialogue()
        {
            var dialogue = game.Dialogue;
            if (!dialogue.Active)
                return;
            var npc = dialogue.Npc;
            var node = dialogue.Current;
            var narration = node.speaker == DialogueSpeaker.Narrator;
            var textStyle = new GUIStyle(text) { fontSize = 17, fontStyle = narration ? FontStyle.Italic : FontStyle.Normal };

            // La boîte s'adapte au contenu : hauteur calculée sur le texte complet (stable pendant l'écriture) et sur chaque choix.
            const float boxWidth = 840f;
            var textWidth = boxWidth - (narration ? 40f : 180f);
            var textHeight = Mathf.Max(40f, textStyle.CalcHeight(new GUIContent(node.text ?? string.Empty), textWidth));
            var choiceHeights = dialogue.Choices.Select(c => Mathf.Max(28f, choice.CalcHeight(new GUIContent($"9.  {c.text}"), textWidth))).ToList();
            var contentHeight = 50f + textHeight + 10f + (choiceHeights.Count > 0 ? choiceHeights.Sum() + 3f * choiceHeights.Count : 30f) + 12f;
            var boxHeight = Mathf.Min(ReferenceHeight - 40f, Mathf.Max(narration ? 120f : 160f, contentHeight));
            var box = new Rect(width / 2 - boxWidth / 2, ReferenceHeight - boxHeight - 20f, boxWidth, boxHeight);
            Fill(box, new Color(0.05f, 0.04f, 0.07f, 0.94f));
            Outline(box, new Color(0.78f, 0.7f, 0.55f), 2f);

            var textLeft = box.x + 20;
            if (!narration)
            {
                var portrait = new Rect(box.x + 18, box.y + 18, 120, 120);
                Fill(portrait, new Color(1f, 1f, 1f, 0.05f));
                Outline(portrait, new Color(0.78f, 0.7f, 0.55f), 2f);
                DrawSprite(new Rect(portrait.x + 10, portrait.y + 10, 100, 100), node.speaker == DialogueSpeaker.Player ? game.Database.player : npc.Portrait);
                var name = node.speaker == DialogueSpeaker.Player ? "Vous" : npc.DisplayName;
                var plate = new Rect(box.x + 156, box.y + 16, 220, 26);
                Fill(plate, node.speaker == DialogueSpeaker.Npc ? npc.Color : new Color(0.3f, 0.6f, 0.4f));
                GUI.Label(plate, $"<b>{name}</b>", centered);
                textLeft = box.x + 160;
            }

            GUI.Label(new Rect(textLeft, box.y + 50, textWidth, textHeight), dialogue.VisibleText, textStyle);

            if (dialogue.IsTyping)
                return;
            if (dialogue.Choices.Count > 0)
            {
                var y = box.y + 50 + textHeight + 10;
                for (var i = 0; i < dialogue.Choices.Count; i++)
                {
                    if (GUI.Button(new Rect(textLeft, y, textWidth, choiceHeights[i]), $"{i + 1}.  {dialogue.Choices[i].text}", choice))
                        dialogue.Choose(i);
                    y += choiceHeights[i] + 3;
                }
            }
            else
            {
                var hint = new GUIStyle(small) { alignment = TextAnchor.MiddleRight };
                hint.normal.textColor = new Color(0.78f, 0.7f, 0.55f);
                if (GUI.Button(new Rect(box.xMax - 260, box.yMax - 34, 240, 24), "▼ Clic ou Entrée pour continuer", hint))
                    dialogue.Advance();
            }
        }

        void DrawInventory()
        {
            var state = game.State;
            var panel = new Rect(width / 2 - 380, 90, 760, 520);
            Fill(new Rect(0, 0, width, ReferenceHeight), new Color(0f, 0f, 0f, 0.5f));
            Fill(panel, new Color(0.06f, 0.05f, 0.09f, 0.96f));
            Outline(panel, new Color(0.78f, 0.7f, 0.55f), 2f);
            GUI.Label(new Rect(panel.x, panel.y + 10, panel.width, 30), "Inventaire", big);
            GUI.Label(new Rect(panel.x + 20, panel.y + 46, 300, 160),
                $"<b>Niveau {state.Level}</b>\nPV {state.Hp} / {state.MaxHp}\nAttaque {state.Attack}\nDéfense {state.Defense}\nVitesse {state.MoveSpeed:0.0}\n" +
                $"<color=#{ColorUtility.ToHtmlStringRGB(Gold)}>{state.Gold} or</color>\nEnnemis vaincus : {state.Kills}", text);

            GUI.Label(new Rect(panel.x + 260, panel.y + 46, 300, 22), "<b>Équipé</b>", text);
            var equipped = state.Equipped.ToList();
            for (var i = 0; i < equipped.Count; i++)
                ItemRow(new Rect(panel.x + 260, panel.y + 72 + i * 44, 480, 40), equipped[i], null, null);

            GUI.Label(new Rect(panel.x + 20, panel.y + 220, 300, 22), $"<b>Sac</b> ({state.Inventory.Count})", text);
            var row = 0;
            foreach (var item in state.Inventory.ToList())
            {
                if (row >= 6)
                {
                    GUI.Label(new Rect(panel.x + 20, panel.y + 246 + row * 44, 400, 22), $"… et {state.Inventory.Count - row} autre(s)", small);
                    break;
                }
                var rect = new Rect(panel.x + 20, panel.y + 246 + row * 44, 720, 40);
                if (item.Type == ItemType.Consumable)
                    ItemRow(rect, item, "Utiliser", () => state.Use(item));
                else if (item.Type is ItemType.Weapon or ItemType.Armor or ItemType.Accessory)
                    ItemRow(rect, item, "Équiper", () => state.Equip(item));
                else
                    ItemRow(rect, item, null, null);
                row++;
            }
            GUI.Label(new Rect(panel.x, panel.yMax - 30, panel.width, 22), "<color=#999999>I ou Échap : fermer</color>", centered);
        }

        void ItemRow(Rect rect, ItemData item, string action, System.Action onAction)
        {
            Fill(rect, new Color(1f, 1f, 1f, 0.04f));
            var color = game.Database.GetRarityColor(item.Rarity);
            Fill(new Rect(rect.x, rect.y, 4, rect.height), color);
            DrawSprite(new Rect(rect.x + 10, rect.y + 4, 32, 32), item.Icon);
            var stats = string.Join("  ", item.Stats.Select(s => $"{s.stat} {s.value:+0;-0}"));
            GUI.Label(new Rect(rect.x + 50, rect.y + 2, 400, 20), $"<color=#{ColorUtility.ToHtmlStringRGB(color)}><b>{item.DisplayName}</b></color>  <color=#999999>{item.Type}</color>", text);
            GUI.Label(new Rect(rect.x + 50, rect.y + 20, 400, 18), stats, small);
            if (action != null && GUI.Button(new Rect(rect.xMax - 100, rect.y + 7, 90, 26), action))
                onAction();
        }

        #endregion

        #region Dessin

        static void Fill(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        static void Outline(Rect rect, Color color, float thickness)
        {
            Fill(new Rect(rect.x, rect.y, rect.width, thickness), color);
            Fill(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            Fill(new Rect(rect.x, rect.y, thickness, rect.height), color);
            Fill(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }

        void Bar(Rect rect, float ratio, Color color, string label)
        {
            Fill(rect, new Color(0f, 0f, 0f, 0.55f));
            Fill(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(ratio), rect.height), color);
            if (!string.IsNullOrEmpty(label))
                GUI.Label(rect, label, new GUIStyle(centered) { fontSize = Mathf.Min(13, Mathf.RoundToInt(rect.height - 4)) });
        }

        static void DrawSprite(Rect rect, Sprite sprite)
        {
            if (sprite == null)
                return;
            var texture = sprite.texture;
            var source = sprite.textureRect;
            var uv = new Rect(source.x / texture.width, source.y / texture.height, source.width / texture.width, source.height / texture.height);
            GUI.DrawTextureWithTexCoords(rect, texture, uv);
        }

        #endregion
    }
}
