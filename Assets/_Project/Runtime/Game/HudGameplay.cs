using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using UnityEngine;
using static TheDungeon.Game.HudSkin;

namespace TheDungeon.Game
{
    /// <summary>HUD pendant le jeu : stats et équipement, barre d'actions, mini-carte, barre du boss, barres de vie, textes flottants, messages et invite d'interaction.</summary>
    internal sealed class HudGameplay
    {
        readonly DungeonGame game;
        readonly HudSkin skin;

        public HudGameplay(DungeonGame game, HudSkin skin)
        {
            this.game = game;
            this.skin = skin;
        }

        public void DrawStatus()
        {
            var state = game.State;
            // Les stats « HUD » de la progression : barres d'abord, puis une ligne de valeurs (ATQ 12   DÉF 3…).
            var hud = state.Stats.Where(s => s.visibility == StatVisibility.Hud).ToList();
            var bars = hud.Where(s => s.format == StatFormat.Bar).ToList();
            var numbers = string.Join("   ", hud.Where(s => s.format != StatFormat.Bar).Select(s => $"{s.displayName} {s.Format(state.Value(s))}"));
            var panel = new Rect(14, 14, 300, 24 + bars.Sum(BarHeight) + bars.Count * 6 + 22 + (numbers.Length > 0 ? 22 : 0) + 4);
            Fill(panel, Panel);

            var y = 26f;
            foreach (var stat in bars)
            {
                var (ratio, label) = BarContent(state, stat);
                skin.Bar(new Rect(26, y, 276, BarHeight(stat)), ratio, stat.color, label);
                y += BarHeight(stat) + 6;
            }
            GUI.Label(new Rect(26, y, 140, 22), $"<color=#{ColorUtility.ToHtmlStringRGB(Gold)}>{state.Gold} or</color>", skin.Text);
            var potions = state.Inventory.Count(i => i.Type == ItemType.Consumable);
            GUI.Label(new Rect(120, y, 190, 22), $"F : consommable ×{potions}", skin.Small);
            y += 22;
            if (numbers.Length > 0)
                GUI.Label(new Rect(26, y, 276, 22), numbers, skin.Small);

            var slotsTop = panel.yMax + 6;
            if (state.AttributePoints > 0)
                GUI.Label(new Rect(14 + 3 * 52 + 6, slotsTop + 4, 220, 40),
                    $"<color=#{ColorUtility.ToHtmlStringRGB(Gold)}><b>+{state.AttributePoints} point(s) à répartir</b></color>\nau prochain feu de camp", skin.Small);
            var slots = new[] { (state.Weapon, "Arme"), (state.Armor, "Armure"), (state.Accessory, "Accessoire") };
            for (var i = 0; i < slots.Length; i++)
            {
                var rect = new Rect(14 + i * 52, slotsTop, 46, 46);
                var (item, label) = slots[i];
                Fill(rect, Panel);
                if (item != null)
                {
                    Outline(rect, game.Database.GetRarityColor(item.RarityId), 2f);
                    DrawSprite(new Rect(rect.x + 7, rect.y + 7, 32, 32), item.Icon);
                }
                else
                {
                    GUI.Label(rect, $"<color=#777777>{label}</color>", new GUIStyle(skin.Centered) { fontSize = 9 });
                }
            }
        }

        static float BarHeight(ProgressionStat stat) => stat.role == StatRole.XpToNext ? 14f : 20f;

        /// <summary>Remplissage et texte d'une barre : PV actuels, XP vers le niveau suivant, ou valeur rapportée au maximum de la stat.</summary>
        static (float ratio, string label) BarContent(GameState state, ProgressionStat stat) => stat.role switch
        {
            StatRole.MaxHealth => (state.Hp / (float)state.MaxHp, $"{stat.displayName}  {state.Hp} / {state.MaxHp}"),
            StatRole.XpToNext => state.XpToNext > 0
                ? (state.Xp / (float)state.XpToNext, $"Niveau {state.Level}   {state.Xp} / {state.XpToNext} {stat.displayName}")
                : (1f, $"Niveau {state.Level} (max)"),
            _ when stat.resource => (state.Value(stat) > 0f ? state.Current(stat) / state.Value(stat) : 0f,
                $"{stat.displayName}  {Mathf.FloorToInt(state.Current(stat))} / {stat.Format(state.Value(stat))}"),
            _ => (Mathf.InverseLerp(stat.min, stat.max, state.Value(stat)), $"{stat.displayName}  {stat.Format(state.Value(stat))}")
        };

        /// <summary>Barre d'actions en bas de l'écran : touche, recharge, et coût (en rouge si la jauge ne suffit pas).</summary>
        public void DrawMechanics()
        {
            if (game.Player == null)
                return;
            var state = game.State;
            var mechanics = state.Mechanics.Where(state.IsUnlocked).Take(6).ToList();
            const float boxWidth = 112f;
            var left = skin.Width / 2 - (mechanics.Count * (boxWidth + 6) - 6) / 2;
            var tiny = new GUIStyle(skin.Small) { fontSize = 10, alignment = TextAnchor.MiddleCenter };
            for (var i = 0; i < mechanics.Count; i++)
            {
                var mechanic = mechanics[i];
                var box = new Rect(left + i * (boxWidth + 6), ReferenceHeight - 64, boxWidth, 48);
                Fill(box, Panel);
                var ready = game.Player.Readiness(mechanic);
                if (ready < 1f)
                    Fill(new Rect(box.x, box.y, box.width * (1f - ready), box.height), new Color(0f, 0f, 0f, 0.55f));
                Outline(box, ready >= 1f ? mechanic.color : new Color(mechanic.color.r, mechanic.color.g, mechanic.color.b, 0.35f), 2f);
                GUI.Label(new Rect(box.x, box.y + 3, box.width, 18), $"<b>{mechanic.displayName}</b>", tiny);
                var stat = state.CostStat(mechanic);
                var cost = state.ValuesOf(mechanic).Cost;
                var price = stat != null && cost > 0f
                    ? $"  <color={(state.CanAfford(mechanic) ? "#bbbbbb" : "#ff6060")}>{cost:0.#} {stat.displayName}</color>"
                    : string.Empty;
                GUI.Label(new Rect(box.x, box.y + 24, box.width, 18), $"<color=#e8a05a>{GameState.InputName(mechanic.input)}</color>{price}", tiny);
            }
        }

        public void DrawMinimap()
        {
            var world = game.World;
            if (world == null || world.ByCell.Count == 0)
                return;
            const float cell = 20f;
            const float gap = 4f;
            var min = new Vector2Int(world.ByCell.Keys.Min(c => c.x), world.ByCell.Keys.Min(c => c.y));
            var max = new Vector2Int(world.ByCell.Keys.Max(c => c.x), world.ByCell.Keys.Max(c => c.y));
            var size = new Vector2((max.x - min.x + 1) * (cell + gap), (max.y - min.y + 1) * (cell + gap));
            var panel = new Rect(skin.Width - size.x - 34, 14, size.x + 20, size.y + 40);
            Fill(panel, Panel);
            GUI.Label(new Rect(panel.x, panel.yMax - 24, panel.width, 20), game.FloorTitle, new GUIStyle(skin.Centered) { fontSize = 11 });

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

        static IEnumerable<RoomInstance> FloorNeighbours(FloorWorld world, Vector2Int cell) =>
            FloorData.Sides.Select(side => cell + FloorData.Offset(side)).Where(world.ByCell.ContainsKey).Select(c => world.ByCell[c]);

        public void DrawBossBar()
        {
            var boss = game.CurrentRoom?.Alive.FirstOrDefault(e => e != null && e.IsBoss);
            if (boss == null)
                return;
            var rect = new Rect(skin.Width / 2 - 260, ReferenceHeight - 70, 520, 22);
            GUI.Label(new Rect(rect.x, rect.y - 26, rect.width, 24), $"<b>{boss.Data.DisplayName}</b>", skin.Big);
            skin.Bar(rect, boss.Hp / (float)boss.MaxHp, Health, $"{boss.Hp} / {boss.MaxHp}");
        }

        public void DrawWorldOverlays()
        {
            var camera = game.Camera;
            if (camera == null)
                return;

            // Barres de vie des ennemis touchés récemment.
            if (game.CurrentRoom != null)
            {
                foreach (var enemy in game.CurrentRoom.Alive.Where(e => e != null && !e.IsBoss && Time.time - e.LastHit < 3f))
                {
                    var point = skin.ToGui(camera, enemy.transform.position + Vector3.up * 0.7f);
                    skin.Bar(new Rect(point.x - 18, point.y, 36, 5), enemy.Hp / (float)enemy.MaxHp, Health, null);
                }
            }

            foreach (var floating in GameFx.Texts)
            {
                var point = skin.ToGui(camera, floating.Position);
                var style = new GUIStyle(skin.Centered) { fontSize = Mathf.RoundToInt(16 * floating.Size), fontStyle = FontStyle.Bold };
                var color = floating.Color;
                color.a = GameFx.Alpha(floating);
                style.normal.textColor = new Color(0f, 0f, 0f, color.a * 0.8f);
                GUI.Label(new Rect(point.x - 99, point.y - 11, 200, 24), floating.Text, style);
                style.normal.textColor = color;
                GUI.Label(new Rect(point.x - 100, point.y - 12, 200, 24), floating.Text, style);
            }
        }

        public void DrawToasts()
        {
            var y = 150f;
            foreach (var (message, color, time) in game.Toasts.AsEnumerable().Reverse().Take(4))
            {
                var age = Time.unscaledTime - time;
                var alpha = Mathf.Clamp01(2.5f - age) * Mathf.Clamp01(age * 6f);
                var style = new GUIStyle(skin.Big) { fontSize = 18 };
                style.normal.textColor = new Color(color.r, color.g, color.b, alpha);
                var toastWidth = Mathf.Clamp(style.CalcSize(new GUIContent(message)).x + 40f, 240f, skin.Width - 40f);
                Fill(new Rect(skin.Width / 2 - toastWidth / 2, y, toastWidth, 30), new Color(0f, 0f, 0f, 0.45f * alpha));
                GUI.Label(new Rect(skin.Width / 2 - toastWidth / 2, y, toastWidth, 30), message, style);
                y += 34f;
            }
        }

        public void DrawPrompt()
        {
            if (game.Player == null)
                return;
            var interactable = game.Nearest(game.Player.transform.position);
            if (interactable == null)
                return;
            // Le bandeau s'élargit avec son texte (nom d'objet long, prix…).
            var label = $"<color=#e8a05a><b>[E]</b></color>  {interactable.Prompt}";
            var promptWidth = Mathf.Clamp(skin.Centered.CalcSize(new GUIContent(label)).x + 40f, 240f, skin.Width - 40f);
            var rect = new Rect(skin.Width / 2 - promptWidth / 2, ReferenceHeight - 130, promptWidth, 34);
            Fill(rect, Panel);
            GUI.Label(rect, label, skin.Centered);
        }
    }
}
