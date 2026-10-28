using System;
using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.RoomEditor
{
    /// <summary>Palette de pinceaux construite à partir du biome de la salle : seul le contenu du biome est proposé.</summary>
    public sealed class RoomPalette : VisualElement
    {
        readonly List<(RoomBrush brush, VisualElement button)> buttons = new();
        BiomeData biome;

        public RoomBrush Selected { get; private set; }
        public event Action<RoomBrush> Changed;

        public RoomPalette()
        {
            AddToClassList("forge-palette");
        }

        public void SetBiome(BiomeData newBiome)
        {
            biome = newBiome;
            var previous = Selected;
            Clear();
            buttons.Clear();

            if (biome == null)
            {
                Add(ForgeUI.Label("Choisissez un biome pour afficher la palette.", "forge-hint"));
                Selected = null;
                return;
            }

            Group("Terrain",
                RoomBrush.Tile(BrushKind.Floor, "Sol", biome.GetTileSprite(TileType.Floor, 0, 0)),
                RoomBrush.Tile(BrushKind.Wall, "Mur", biome.WallSprite),
                RoomBrush.Tile(BrushKind.Pit, "Trou", biome.PitSprite));

            var enemies = biome.Enemies.Where(e => e.enemy != null && ValidationCache.IsValid(e.enemy)).Select(e => e.enemy).ToList();
            Group("Ennemis", enemies.Where(e => !e.IsBoss).Select(RoomBrush.ForEnemy).ToArray());
            var bosses = enemies.Where(e => e.IsBoss).Select(RoomBrush.ForEnemy).ToArray();
            if (bosses.Length > 0)
                Group("Boss (salle de boss uniquement)", bosses);

            Group("Coffres", biome.ChestLootTables.Where(t => t != null && ValidationCache.IsValid(t))
                .Select(t => RoomBrush.ForChest(t, RoomArt.Chest)).ToArray());
            Group("Décors", biome.Props.Where(p => p.sprite != null).Select(RoomBrush.ForProp).ToArray());
            Group("Autres", RoomBrush.Spawn(RoomArt.Player), RoomBrush.Eraser());

            var restored = buttons.Select(b => b.brush).FirstOrDefault(b => b.SameAs(previous));
            Select(restored ?? buttons[0].brush);
        }

        void Group(string title, params RoomBrush[] brushes)
        {
            Add(ForgeUI.Label(title, "forge-palette__title"));
            if (brushes.Length == 0)
            {
                Add(ForgeUI.Label("Rien dans ce biome.", "forge-hint"));
                return;
            }
            var grid = new VisualElement();
            grid.AddToClassList("forge-palette__grid");
            foreach (var brush in brushes)
            {
                var button = new VisualElement { tooltip = Describe(brush) };
                button.AddToClassList("forge-palette__brush");
                var icon = new Image { sprite = brush.Icon, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                icon.AddToClassList("forge-palette__icon");
                if (brush.Kind == BrushKind.Eraser)
                    icon.image = UnityEditor.EditorGUIUtility.IconContent("Grid.EraserTool").image;
                button.Add(icon);
                button.Add(new Label(brush.Label) { pickingMode = PickingMode.Ignore });
                button.RegisterCallback<ClickEvent>(_ => Select(brush));
                grid.Add(button);
                buttons.Add((brush, button));
            }
            Add(grid);
        }

        public void Select(RoomBrush brush)
        {
            Selected = brush;
            foreach (var (candidate, button) in buttons)
                button.EnableInClassList("forge-palette__brush--selected", candidate == brush);
            Changed?.Invoke(brush);
        }

        static string Describe(RoomBrush brush) => brush.Kind switch
        {
            BrushKind.Floor => "Sol : case praticable.",
            BrushKind.Wall => "Mur : bloque le passage. Efface ce qui était posé sur la case.",
            BrushKind.Pit => "Trou : infranchissable. Efface ce qui était posé sur la case.",
            BrushKind.Enemy => $"Ennemi « {brush.Label} » : clic pour le placer sur une case de sol." + (brush.Enemy.IsBoss ? "\nBoss : réservé aux salles de type Boss." : string.Empty),
            BrushKind.Chest => $"Coffre utilisant la table « {brush.LootTable.name} ».",
            BrushKind.Prop => $"Décor « {brush.Label} ».",
            BrushKind.PlayerSpawn => "Point d'apparition du joueur (salle de départ uniquement). Un seul par salle.",
            BrushKind.Eraser => "Gomme : retire l'élément posé sur la case. Raccourci : clic droit.",
            _ => brush.Label
        };
    }
}
