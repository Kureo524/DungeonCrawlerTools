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

    public sealed class RoomRule : ValidationRule
    {
        public override string Name => "Salles";
        public override string Description =>
            "Biome obligatoire, contenu autorisé par le biome, entrées praticables, tout est accessible, un boss uniquement en salle de boss.";

        public override IEnumerable<ValidationIssue> Run(ValidationContext context)
        {
            var limits = context.Limits;
            foreach (var room in context.Rooms)
            {
                var biome = room.Biome;
                if (biome == null)
                {
                    yield return Error("La salle n'a pas de biome. Choisissez un preset de biome.", room);
                    continue;
                }
                if (!room.HasValidTiles)
                {
                    yield return Error("La grille de la salle est corrompue (taille incohérente). Redimensionnez la salle pour la régénérer.", room);
                    continue;
                }
                if (!limits.roomWidth.Contains(room.Width) || !limits.roomHeight.Contains(room.Height))
                    yield return Error($"Taille {room.Width}×{room.Height} hors limites ({limits.roomWidth} × {limits.roomHeight}).", room);

                foreach (var issue in CheckContent(room, biome, limits))
                    yield return issue;
                foreach (var issue in CheckDoorsAndAccess(room))
                    yield return issue;
                foreach (var issue in CheckRoomType(room))
                    yield return issue;
            }
        }

        static IEnumerable<ValidationIssue> CheckContent(RoomData room, BiomeData biome, Common.ForgeDesignLimits limits)
        {
            if (room.Enemies.Count > limits.maxEnemiesPerRoom)
                yield return Error($"Trop d'ennemis ({room.Enemies.Count}, max {limits.maxEnemiesPerRoom}).", room);
            if (room.Chests.Count > limits.maxChestsPerRoom)
                yield return Error($"Trop de coffres ({room.Chests.Count}, max {limits.maxChestsPerRoom}).", room);

            foreach (var spawn in room.Enemies)
            {
                if (spawn.enemy == null)
                    yield return Error($"Case {Cell(spawn.position)} : ennemi manquant ou supprimé.", room);
                else if (!biome.AllowsEnemy(spawn.enemy))
                    yield return Error($"Case {Cell(spawn.position)} : « {spawn.enemy.DisplayName} » n'appartient pas au biome {biome.DisplayName}.", room);
            }
            foreach (var chest in room.Chests)
            {
                if (chest.lootTable == null)
                    yield return Error($"Case {Cell(chest.position)} : coffre sans table de butin.", room);
                else if (!biome.ChestLootTables.Contains(chest.lootTable))
                    yield return Error($"Case {Cell(chest.position)} : la table « {chest.lootTable.name} » n'est pas proposée par le biome {biome.DisplayName}.", room);
            }
            foreach (var prop in room.Props)
            {
                if (prop.sprite == null || !biome.TryGetProp(prop.sprite, out _))
                    yield return Error($"Case {Cell(prop.position)} : ce décor n'appartient pas au biome {biome.DisplayName}.", room);
            }
            foreach (var spawn in room.Npcs)
            {
                if (spawn.npc == null)
                    yield return Error($"Case {Cell(spawn.position)} : personnage manquant ou supprimé.", room);
                else if (spawn.afterClear && room.RoomType is not (RoomType.Combat or RoomType.Boss))
                    yield return Warning($"« {spawn.npc.DisplayName} » attend la fin du combat, mais la salle n'est pas une salle de combat : il sera là dès l'arrivée.", room);
            }

            // Chaque objet doit être sur une case de sol, dans la salle, et seul sur sa case.
            var occupied = new Dictionary<Vector2Int, string>();
            foreach (var (position, label) in Placements(room))
            {
                if (!room.Contains(position))
                    yield return Error($"{label} placé hors de la salle ({Cell(position)}).", room);
                else if (room.GetTile(position.x, position.y) != TileType.Floor)
                    yield return Error($"{label} placé sur un {Describe(room.GetTile(position.x, position.y))} en {Cell(position)}.", room);
                if (occupied.TryGetValue(position, out var other))
                    yield return Error($"{label} et {other} sont sur la même case {Cell(position)}.", room);
                occupied[position] = label;
            }
        }

        static IEnumerable<ValidationIssue> CheckDoorsAndAccess(RoomData room)
        {
            if (room.Doors == Doors.None)
                yield return Error("La salle n'a aucune entrée : ajoutez au moins une porte.", room);

            var starts = new List<Vector2Int>();
            foreach (var side in FloorData.Sides)
            {
                if (!room.HasDoor(side))
                    continue;
                var cell = room.GetDoorCell(side);
                if (room.GetTile(cell.x, cell.y) != TileType.Floor)
                    yield return Error($"L'entrée {Side(side)} est bloquée ({Describe(room.GetTile(cell.x, cell.y))} en {Cell(cell)}).", room);
                else
                    starts.Add(cell);
            }
            if (room.HasPlayerSpawn && room.Contains(room.PlayerSpawn))
                starts.Add(room.PlayerSpawn);
            if (starts.Count == 0)
                yield break;

            var reachable = Reachable(room, starts);
            foreach (var (position, label) in Placements(room))
                if (room.Contains(position) && room.GetTile(position.x, position.y) == TileType.Floor && !reachable.Contains(position))
                    yield return Error($"{label} en {Cell(position)} est inaccessible depuis les entrées.", room);
            foreach (var side in FloorData.Sides)
            {
                if (!room.HasDoor(side))
                    continue;
                var cell = room.GetDoorCell(side);
                if (room.GetTile(cell.x, cell.y) == TileType.Floor && !reachable.Contains(cell))
                    yield return Error($"L'entrée {Side(side)} n'est pas reliée aux autres.", room);
            }
        }

        static IEnumerable<ValidationIssue> CheckRoomType(RoomData room)
        {
            var bosses = room.Enemies.Count(spawn => spawn.enemy != null && spawn.enemy.IsBoss);
            switch (room.RoomType)
            {
                case RoomType.Start:
                    if (!room.HasPlayerSpawn)
                        yield return Error("Une salle de départ doit avoir un point d'apparition du joueur.", room);
                    if (room.Enemies.Count > 0)
                        yield return Warning("Une salle de départ contient des ennemis.", room);
                    break;
                case RoomType.Combat:
                    if (room.Enemies.Count == 0)
                        yield return Error("Une salle de combat doit contenir au moins un ennemi.", room);
                    break;
                case RoomType.Treasure:
                    if (room.Chests.Count == 0)
                        yield return Error("Une salle au trésor doit contenir au moins un coffre.", room);
                    break;
                case RoomType.Npc:
                    if (room.Npcs.Count == 0)
                        yield return Warning("Salle de PNJ sans personnage posé : elle accueillera les PNJ de l'étage qui ne sont placés nulle part.", room);
                    if (room.Enemies.Count > 0)
                        yield return Warning("Une salle de PNJ contient des ennemis : ils ne seront pas activés.", room);
                    break;
                case RoomType.Boss:
                    if (bosses != 1)
                        yield return Error($"Une salle de boss doit contenir exactement un boss (actuellement {bosses}).", room);
                    break;
            }
            if (room.RoomType != RoomType.Boss && bosses > 0)
                yield return Error("Un boss ne peut être placé que dans une salle de type Boss.", room);
            if (room.RoomType != RoomType.Start && room.HasPlayerSpawn)
                yield return Warning("Seule la salle de départ utilise le point d'apparition du joueur.", room);
        }

        static IEnumerable<(Vector2Int position, string label)> Placements(RoomData room)
        {
            foreach (var spawn in room.Enemies)
                yield return (spawn.position, spawn.enemy != null ? $"L'ennemi « {spawn.enemy.DisplayName} »" : "Un ennemi");
            foreach (var chest in room.Chests)
                yield return (chest.position, "Un coffre");
            foreach (var prop in room.Props)
                yield return (prop.position, "Un décor");
            foreach (var spawn in room.Npcs)
                yield return (spawn.position, spawn.npc != null ? $"Le personnage « {spawn.npc.DisplayName} »" : "Un personnage");
            if (room.HasPlayerSpawn)
                yield return (room.PlayerSpawn, "Le point d'apparition du joueur");
        }

        /// <summary>Cases de sol atteignables à pied depuis les points de départ (les décors bloquants font obstacle).</summary>
        public static HashSet<Vector2Int> Reachable(RoomData room, IEnumerable<Vector2Int> starts)
        {
            var blocked = new HashSet<Vector2Int>();
            if (room.Biome != null)
                foreach (var prop in room.Props)
                    if (prop.sprite != null && room.Biome.TryGetProp(prop.sprite, out var definition) && definition.blocksMovement)
                        blocked.Add(prop.position);

            var visited = new HashSet<Vector2Int>();
            var queue = new Queue<Vector2Int>();
            foreach (var start in starts)
                if (visited.Add(start))
                    queue.Enqueue(start);

            var directions = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                foreach (var direction in directions)
                {
                    var next = cell + direction;
                    if (!room.Contains(next) || room.GetTile(next.x, next.y) != TileType.Floor || visited.Contains(next))
                        continue;
                    visited.Add(next);
                    // Un décor bloquant est « atteint » (on peut le voir) mais on ne passe pas au travers.
                    if (!blocked.Contains(next))
                        queue.Enqueue(next);
                }
            }
            return visited;
        }

        static string Cell(Vector2Int position) => $"({position.x}, {position.y})";

        static string Side(Doors side) => side switch
        {
            Doors.North => "nord",
            Doors.East => "est",
            Doors.South => "sud",
            Doors.West => "ouest",
            _ => side.ToString()
        };

        static string Describe(TileType tile) => tile switch
        {
            TileType.Wall => "mur",
            TileType.Pit => "trou",
            _ => "sol"
        };
    }
}
