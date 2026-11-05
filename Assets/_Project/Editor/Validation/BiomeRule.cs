using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using UnityEngine;

namespace TheDungeon.Editor.Validation
{
    public sealed class BiomeRule : ValidationRule
    {
        public override string Name => "Biomes";
        public override string Description => "Visuels complets, au moins un ennemi, contenus et bonus cohérents.";

        public override IEnumerable<ValidationIssue> Run(ValidationContext context)
        {
            var limits = context.Limits;
            foreach (var biome in context.Biomes)
            {
                if (biome.FloorSprites.Count == 0 || biome.FloorSprites.Any(sprite => sprite == null))
                    yield return Error("Le biome doit avoir au moins un sprite de sol, et aucune ligne de sol vide.", biome);
                if (biome.WallSprite == null)
                    yield return Error("Le biome n'a pas de sprite de mur.", biome);
                if (biome.PitSprite == null)
                    yield return Warning("Le biome n'a pas de sprite de trou : les trous ne pourront pas être affichés.", biome);
                if (biome.MinFloor > biome.MaxFloor || biome.MinFloor < 1 || biome.MaxFloor > limits.floorCount)
                    yield return Error($"Étages incohérents ({biome.MinFloor} à {biome.MaxFloor}) : ils doivent être compris entre 1 et {limits.floorCount}.", biome);

                if (biome.Enemies.Count(entry => entry.enemy != null && !entry.enemy.IsBoss) == 0)
                    yield return Error("Le biome doit proposer au moins un ennemi (hors boss).", biome);
                if (!biome.Enemies.Any(entry => entry.enemy != null && entry.enemy.IsBoss))
                    yield return Warning("Aucun boss dans ce biome : impossible d'y créer une salle de boss.", biome);
                for (var i = 0; i < biome.Enemies.Count; i++)
                {
                    if (biome.Enemies[i].enemy == null)
                        yield return Error($"Ennemi n°{i + 1} : aucun ennemi choisi (manquant ou supprimé).", biome);
                    if (biome.Enemies[i].spawnWeight <= 0)
                        yield return Error($"Ennemi n°{i + 1} : poids d'apparition nul, il n'apparaîtra jamais.", biome);
                }
                if (biome.Enemies.Where(e => e.enemy != null).GroupBy(e => e.enemy).Any(group => group.Count() > 1))
                    yield return Warning("Un même ennemi apparaît plusieurs fois : additionnez plutôt les poids.", biome);

                for (var i = 0; i < biome.Props.Count; i++)
                {
                    if (biome.Props[i].sprite == null)
                        yield return Error($"Décor n°{i + 1} : aucun sprite choisi.", biome);
                    if (string.IsNullOrWhiteSpace(biome.Props[i].name))
                        yield return Warning($"Décor n°{i + 1} : sans nom, il sera difficile à reconnaître dans la palette.", biome);
                }
                if (biome.Props.Where(p => p.sprite != null).GroupBy(p => p.sprite).Any(group => group.Count() > 1))
                    yield return Error("Deux décors utilisent le même sprite : chaque décor doit être unique.", biome);

                if (biome.ChestLootTables.Count == 0)
                    yield return Warning("Aucune table de butin : les salles de ce biome ne pourront pas avoir de coffre.", biome);
                if (biome.ChestLootTables.Any(table => table == null))
                    yield return Error("Une table de butin de coffre est vide (manquante ou supprimée).", biome);

                foreach (var boost in biome.RarityBoosts)
                {
                    if (!context.Limits.HasRarity(boost.rarity))
                        yield return Error($"Le bonus de butin vise une rareté supprimée (n°{boost.rarity}) : retirez cette ligne ou choisissez une autre rareté.", biome);
                    else if (boost.multiplier < 0f)
                        yield return Error($"Le bonus de rareté {context.Limits.GetRarity(boost.rarity).DisplayName} est négatif.", biome);
                }
                if (biome.RarityBoosts.GroupBy(b => b.rarity).Any(group => group.Count() > 1))
                    yield return Error("Une même rareté a plusieurs bonus : gardez une seule ligne par rareté.", biome);
            }
        }
    }
}
