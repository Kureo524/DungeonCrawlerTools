using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using UnityEngine;
using static TheDungeon.Game.HudSkin;

namespace TheDungeon.Game
{
    /// <summary>Fenêtres qui interrompent le jeu : dialogue avec un PNJ, inventaire et feu de camp (répartition des points d'attributs).</summary>
    internal sealed class HudMenus
    {
        readonly DungeonGame game;
        readonly HudSkin skin;

        public HudMenus(DungeonGame game, HudSkin skin)
        {
            this.game = game;
            this.skin = skin;
        }

        public void DrawDialogue()
        {
            var dialogue = game.Dialogue;
            if (!dialogue.Active)
                return;
            var npc = dialogue.Npc;
            var node = dialogue.Current;
            var narration = node.speaker == DialogueSpeaker.Narrator;
            // Couleurs propres au personnage (outil Dialogues > Apparence de la boîte de dialogue).
            var look = npc.Style;
            var textStyle = new GUIStyle(skin.Text) { fontSize = 17, fontStyle = narration ? FontStyle.Italic : FontStyle.Normal };
            textStyle.normal.textColor = narration ? new Color(look.text.r, look.text.g, look.text.b, look.text.a * 0.8f) : look.text;

            // La boîte s'adapte au contenu : hauteur calculée sur le texte complet (stable pendant l'écriture) et sur chaque choix.
            const float boxWidth = 840f;
            var textWidth = boxWidth - (narration ? 40f : 180f);
            var textHeight = Mathf.Max(40f, textStyle.CalcHeight(new GUIContent(node.text ?? string.Empty), textWidth));
            var choiceHeights = dialogue.Choices.Select(c => Mathf.Max(28f, skin.Choice.CalcHeight(new GUIContent($"9.  {c.text}"), textWidth))).ToList();
            var contentHeight = 50f + textHeight + 10f + (choiceHeights.Count > 0 ? choiceHeights.Sum() + 3f * choiceHeights.Count : 30f) + 12f;
            var boxHeight = Mathf.Min(ReferenceHeight - 40f, Mathf.Max(narration ? 120f : 160f, contentHeight));
            var box = new Rect(skin.Width / 2 - boxWidth / 2, ReferenceHeight - boxHeight - 20f, boxWidth, boxHeight);
            Fill(box, look.boxBackground);
            Outline(box, look.boxBorder, 2f);

            var textLeft = narration ? box.x + 20 : DrawSpeaker(box, npc, node, look);

            GUI.Label(new Rect(textLeft, box.y + 50, textWidth, textHeight), dialogue.VisibleText, textStyle);

            if (dialogue.IsTyping)
                return;
            if (dialogue.Choices.Count > 0)
                DrawChoices(dialogue, look, new Rect(textLeft, box.y + 50 + textHeight + 10, textWidth, 0f), choiceHeights);
            else
                DrawContinue(dialogue, look, box);
        }

        /// <summary>Portrait et nom de qui parle ; renvoie le bord gauche du texte, décalé par le portrait.</summary>
        float DrawSpeaker(Rect box, NpcData npc, DialogueNode node, DialogueStyle look)
        {
            var portrait = new Rect(box.x + 18, box.y + 18, 120, 120);
            Fill(portrait, new Color(1f, 1f, 1f, 0.05f));
            Outline(portrait, look.boxBorder, 2f);
            DrawSprite(new Rect(portrait.x + 10, portrait.y + 10, 100, 100), node.speaker == DialogueSpeaker.Player ? game.Database.player : npc.Portrait);
            var name = node.speaker == DialogueSpeaker.Player ? "Vous" : npc.DisplayName;
            var plate = new Rect(box.x + 156, box.y + 16, 220, 26);
            Fill(plate, node.speaker == DialogueSpeaker.Npc ? npc.Color : look.playerPlate);
            GUI.Label(plate, $"<b><color=#{ColorUtility.ToHtmlStringRGBA(look.nameText)}>{name}</color></b>", skin.Centered);
            return box.x + 160;
        }

        /// <summary>Réponses du joueur : ses couleurs (ou celles du personnage), plus marquées au survol ; un clic choisit.</summary>
        void DrawChoices(DialogueRunner dialogue, DialogueStyle look, Rect area, List<float> choiceHeights)
        {
            var y = area.y;
            var choiceLabel = new GUIStyle(skin.Choice);
            for (var i = 0; i < dialogue.Choices.Count; i++)
            {
                var (background, border, color) = look.ChoiceLook(dialogue.Choices[i]);
                foreach (var state in new[] { choiceLabel.normal, choiceLabel.hover, choiceLabel.active, choiceLabel.focused })
                {
                    state.background = null;
                    state.textColor = color;
                }
                var rect = new Rect(area.x, y, area.width, choiceHeights[i]);
                var hover = rect.Contains(Event.current.mousePosition);
                Fill(rect, hover ? Color.Lerp(background, border, 0.3f) : background);
                Outline(rect, hover ? new Color(border.r, border.g, border.b, 1f) : border, 1f);
                if (GUI.Button(rect, $"{i + 1}.  {dialogue.Choices[i].text}", choiceLabel))
                {
                    // Choisir passe à la réplique suivante et remplace la liste des réponses : on arrête de la parcourir.
                    dialogue.Choose(i);
                    break;
                }
                y += choiceHeights[i] + 3;
            }
        }

        void DrawContinue(DialogueRunner dialogue, DialogueStyle look, Rect box)
        {
            var hint = new GUIStyle(skin.Small) { alignment = TextAnchor.MiddleRight };
            hint.normal.textColor = look.boxBorder;
            if (GUI.Button(new Rect(box.xMax - 260, box.yMax - 34, 240, 24), "▼ Clic ou Entrée pour continuer", hint))
                dialogue.Advance();
        }

        public void DrawInventory()
        {
            var state = game.State;
            // Une colonne de plus pour les attributs et compétences, si la progression en a.
            var side = state.Attributes.Count > 0 || state.UnlockedSkills.Any();
            var panel = side ? new Rect(skin.Width / 2 - 520, 90, 1040, 520) : new Rect(skin.Width / 2 - 380, 90, 760, 520);
            Fill(new Rect(0, 0, skin.Width, ReferenceHeight), new Color(0f, 0f, 0f, 0.5f));
            Fill(panel, new Color(0.06f, 0.05f, 0.09f, 0.96f));
            Outline(panel, new Color(0.78f, 0.7f, 0.55f), 2f);
            GUI.Label(new Rect(panel.x, panel.y + 10, panel.width, 30), "Inventaire", skin.Big);
            // Fiche du personnage : toutes les stats visibles (HUD ou fiche), avec les bonus d'équipement.
            var sheet = state.Stats.Where(s => s.visibility != StatVisibility.Hidden)
                .Select(s => s.role == StatRole.MaxHealth ? $"{s.displayName} {state.Hp} / {state.MaxHp}"
                    : s.role == StatRole.XpToNext ? $"{s.displayName} {state.Xp} / {state.XpToNext}"
                    : s.resource ? $"{s.displayName} {Mathf.FloorToInt(state.Current(s))} / {s.Format(state.Value(s))}"

                    : $"{s.displayName} {s.Format(state.Value(s))}")
                .Take(7);
            GUI.Label(new Rect(panel.x + 20, panel.y + 46, 230, 170),
                $"<b>Niveau {state.Level}</b>\n{string.Join("\n", sheet)}\n" +
                $"<color=#{ColorUtility.ToHtmlStringRGB(Gold)}>{state.Gold} or</color>   Ennemis vaincus : {state.Kills}", skin.Small);

            GUI.Label(new Rect(panel.x + 260, panel.y + 46, 300, 22), "<b>Équipé</b>", skin.Text);
            var equipped = state.Equipped.ToList();
            for (var i = 0; i < equipped.Count; i++)
                ItemRow(new Rect(panel.x + 260, panel.y + 72 + i * 44, 480, 40), equipped[i], null, null);

            GUI.Label(new Rect(panel.x + 20, panel.y + 220, 300, 22), $"<b>Sac</b> ({state.Inventory.Count})", skin.Text);
            var row = 0;
            foreach (var item in state.Inventory.ToList())
            {
                if (row >= 6)
                {
                    GUI.Label(new Rect(panel.x + 20, panel.y + 246 + row * 44, 400, 22), $"… et {state.Inventory.Count - row} autre(s)", skin.Small);
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
            if (side)
                DrawCharacter(state, new Rect(panel.x + 760, panel.y + 46, 260, panel.height - 80), false);
            GUI.Label(new Rect(panel.x, panel.yMax - 30, panel.width, 22), "<color=#999999>I ou Échap : fermer</color>", skin.Centered);
        }

        /// <summary>Feu de camp : on y répartit ses points d'attributs (bouton « + »), à la Elden Ring.</summary>
        public void DrawCampfire()
        {
            var state = game.State;
            var panel = new Rect(skin.Width / 2 - 170, 110, 340, 480);
            Fill(new Rect(0, 0, skin.Width, ReferenceHeight), new Color(0f, 0f, 0f, 0.5f));
            Fill(panel, new Color(0.06f, 0.05f, 0.09f, 0.96f));
            Outline(panel, new Color(1f, 0.55f, 0.15f), 2f);
            GUI.Label(new Rect(panel.x, panel.y + 10, panel.width, 30), "<color=#ff9a3c>Feu de camp</color>", skin.Big);
            DrawCharacter(state, new Rect(panel.x + 30, panel.y + 50, panel.width - 60, panel.height - 90), true);
            GUI.Label(new Rect(panel.x, panel.yMax - 30, panel.width, 22), "<color=#999999>E ou Échap : reprendre la route</color>", skin.Centered);
        }

        /// <summary>
        /// Attributs et compétences débloquées. Les points ne se répartissent (bouton « + ») qu'au feu de camp ;
        /// ailleurs, la fiche rappelle seulement qu'il en reste.
        /// </summary>
        void DrawCharacter(GameState state, Rect area, bool canInvest)
        {
            var points = state.AttributePoints > 0
                ? $"  <color=#{ColorUtility.ToHtmlStringRGB(Gold)}>{state.AttributePoints} à répartir{(canInvest ? string.Empty : " (feu de camp)")}</color>"
                : string.Empty;
            GUI.Label(new Rect(area.x, area.y, area.width, 22), $"<b>Attributs</b>{points}", skin.Text);
            var y = area.y + 26;
            foreach (var attribute in state.Attributes.Take(6))
            {
                var row = new Rect(area.x, y, area.width, 38);
                Fill(row, new Color(1f, 1f, 1f, 0.04f));
                Fill(new Rect(row.x, row.y, 4, row.height), attribute.color);
                var current = state.Points(attribute);
                GUI.Label(new Rect(row.x + 10, row.y + 1, 190, 20), $"<b>{attribute.displayName}</b>  <color=#999999>{current} / {attribute.maxPoints}</color>", skin.Small);
                var bonuses = attribute.bonuses
                    .Select(b => (bonus: b, stat: state.Stats.FirstOrDefault(s => s.id == b.statId)))
                    .Where(p => p.stat != null)
                    .Select(p => $"{p.stat.displayName} +{p.stat.Format(p.bonus.Total(current))}");
                var detail = new GUIStyle(skin.Small) { fontSize = 10 };
                GUI.Label(new Rect(row.x + 10, row.y + 18, 200, 18), $"<color=#999999>{string.Join("  ", bonuses)}</color>", detail);
                if (canInvest && state.CanInvest(attribute) && GUI.Button(new Rect(row.xMax - 34, row.y + 6, 28, 26), "+"))

                    state.Invest(attribute);
                y += 42;
            }
            if (state.Attributes.Count == 0)
            {
                GUI.Label(new Rect(area.x, y, area.width, 20), "<color=#777777>Aucun attribut</color>", skin.Small);
                y += 22;
            }

            y += 8;
            GUI.Label(new Rect(area.x, y, area.width, 22), "<b>Compétences</b>", skin.Text);
            y += 26;
            var skills = state.UnlockedSkills.ToList();
            foreach (var skill in skills.Take(Mathf.Max(0, Mathf.FloorToInt((area.yMax - y) / 34f))))
            {
                var rank = state.SkillRank(skill);
                GUI.Label(new Rect(area.x, y, area.width, 18),
                    $"<color=#{ColorUtility.ToHtmlStringRGB(skill.color)}><b>{skill.displayName}</b></color>  <color=#999999>rang {rank} / {skill.MaxRank}</color>", skin.Small);
                GUI.Label(new Rect(area.x, y + 16, area.width, 18), $"<color=#999999>{skill.EffectText(rank)}</color>", new GUIStyle(skin.Small) { fontSize = 10 });
                y += 34;
            }
            if (skills.Count == 0)
                GUI.Label(new Rect(area.x, y, area.width, 20), "<color=#777777>Aucune compétence débloquée</color>", skin.Small);
        }


        void ItemRow(Rect rect, ItemData item, string action, System.Action onAction)
        {
            Fill(rect, new Color(1f, 1f, 1f, 0.04f));
            var color = game.Database.GetRarityColor(item.RarityId);
            Fill(new Rect(rect.x, rect.y, 4, rect.height), color);
            DrawSprite(new Rect(rect.x + 10, rect.y + 4, 32, 32), item.Icon);
            var stats = string.Join("  ", item.Stats.Select(s => $"{s.stat} {s.value:+0;-0}")
                .Concat(item.PassiveEffects.Select(e => $"<color=#a597c0>{EffectInfo.Summary(e)}</color>")));
            GUI.Label(new Rect(rect.x + 50, rect.y + 2, 400, 20), $"<color=#{ColorUtility.ToHtmlStringRGB(color)}><b>{item.DisplayName}</b></color>  <color=#999999>{item.Type}</color>", skin.Text);
            GUI.Label(new Rect(rect.x + 50, rect.y + 20, 400, 18), stats, skin.Small);
            if (action != null && GUI.Button(new Rect(rect.xMax - 100, rect.y + 7, 90, 26), action))
                onAction();
        }
    }
}
