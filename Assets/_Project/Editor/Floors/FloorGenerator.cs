using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using UnityEngine;

namespace TheDungeon.Editor.Floors
{
    /// <summary>
    /// Assemble un plan d'étage à partir des salles disponibles : une salle de départ au centre,
    /// des salles dont les portes correspondent à celles de leurs voisines, et la salle du boss placée en dernier
    /// (en fond d'étage). Toutes les salles sont du même biome. Recherche avec retour arrière, limitée en nombre d'essais.
    /// </summary>
    public static class FloorGenerator
    {
        const int MaxSteps = 50000;
        const int MaxDifferentAttempts = 64;

        public sealed class Result
        {
            public readonly Dictionary<Vector2Int, RoomData> Layout;
            public readonly RoomData Entrance;
            public readonly string Error;

            public Result(Dictionary<Vector2Int, RoomData> layout, RoomData entrance, string error)
            {
                Layout = layout;
                Entrance = entrance;
                Error = error;
            }

            public bool Success => Error == null;
        }

        /// <summary>
        /// Plan d'un seul biome (un étage ne mélange pas les biomes) : les biomes présents sont essayés dans un ordre tiré de la graine,
        /// jusqu'à en trouver un dont les salles permettent un plan complet.
        /// </summary>
        public static Result Generate(IReadOnlyList<RoomData> pool, int roomCount, Vector2Int gridSize, int seed)
        {
            var random = new System.Random(seed);
            var biomes = Shuffle(pool.GroupBy(r => r.Biome).OrderBy(g => g.Key != null ? g.Key.name : string.Empty).ToList(), random);
            if (biomes.Count <= 1)
                return GenerateBiome(pool, roomCount, gridSize, random);
            var errors = new List<string>();
            foreach (var biome in biomes)
            {
                var result = GenerateBiome(biome.ToList(), roomCount, gridSize, random);
                if (result.Success)
                    return result;
                errors.Add($"{(biome.Key != null ? biome.Key.DisplayName : "Sans biome")} : {result.Error}");
            }
            return Fail("Aucun biome ne permet un plan complet.\n" + string.Join("\n", errors));
        }

        /// <summary>
        /// Comme <see cref="Generate"/>, mais un plan identique au plan actuel est écarté : les graines suivantes sont essayées.
        /// <paramref name="usedSeed"/> est la graine du plan renvoyé ; si le plan reste le même malgré les essais,
        /// <paramref name="changed"/> est faux (ces salles ne permettent pas d'autre plan).
        /// </summary>
        public static Result GenerateDifferent(IReadOnlyList<RoomData> pool, int roomCount, Vector2Int gridSize, int seed,
            IReadOnlyDictionary<Vector2Int, RoomData> current, out int usedSeed, out bool changed)
        {
            usedSeed = seed;
            changed = false;
            var result = Generate(pool, roomCount, gridSize, seed);
            for (var attempt = 1; result.Success && attempt <= MaxDifferentAttempts; attempt++)
            {
                if (!SameLayout(result.Layout, current))
                {
                    usedSeed = seed + attempt - 1;
                    changed = true;
                    return result;
                }
                result = Generate(pool, roomCount, gridSize, seed + attempt);
            }
            return result;
        }

        static bool SameLayout(IReadOnlyDictionary<Vector2Int, RoomData> a, IReadOnlyDictionary<Vector2Int, RoomData> b) =>
            a.Count == b.Count && a.All(entry => b.TryGetValue(entry.Key, out var room) && room == entry.Value);

        static Result GenerateBiome(IReadOnlyList<RoomData> pool, int roomCount, Vector2Int gridSize, System.Random random)
        {
            var starts = pool.Where(r => r.RoomType == RoomType.Start).ToList();
            var bosses = pool.Where(r => r.RoomType == RoomType.Boss && r.ContainsBoss()).ToList();
            var others = pool.Where(r => r.RoomType != RoomType.Start && r.RoomType != RoomType.Boss).ToList();

            if (starts.Count == 0)
                return Fail("Aucune salle de départ disponible pour cet étage.");
            if (bosses.Count == 0)
                return Fail("Aucune salle de boss disponible pour cet étage.");
            if (others.Count < roomCount - 2)
                return Fail($"Pas assez de salles : il en faut {roomCount - 2} hors départ et boss, {others.Count} disponible(s).");

            var center = new Vector2Int(gridSize.x / 2, gridSize.y / 2);
            var steps = 0;
            foreach (var start in Shuffle(starts, random))
            {
                var layout = new Dictionary<Vector2Int, RoomData>();
                if (!Fits(layout, start, center, gridSize))
                    continue;
                layout[center] = start;
                if (Extend(layout, new HashSet<RoomData> { start }, others, bosses, roomCount, gridSize, random, ref steps))
                    return new Result(layout, start, null);
                if (steps > MaxSteps)
                    break;
            }
            return Fail("Aucune combinaison de salles ne relie toutes les portes. Ajoutez des salles avec d'autres entrées " +
                        "(par exemple des salles à 2 ou 3 portes) ou réduisez le nombre de salles.");
        }

        static bool Extend(Dictionary<Vector2Int, RoomData> layout, HashSet<RoomData> used, List<RoomData> others, List<RoomData> bosses,
            int roomCount, Vector2Int gridSize, System.Random random, ref int steps)
        {
            if (++steps > MaxSteps)
                return false;

            var open = OpenDoors(layout).ToList();
            if (layout.Count == roomCount)
                return open.Count == 0;
            if (open.Count == 0 || open.Count > roomCount - layout.Count)
                return false;

            // Chaque porte ouverte peut être la prochaine à remplir : l'ordre compte (la dernière porte doit accueillir le boss).
            var isLast = layout.Count == roomCount - 1;
            var tried = new HashSet<Vector2Int>();
            foreach (var (cell, side) in Shuffle(open, random))
            {
                var target = cell + FloorData.Offset(side);
                if (!tried.Add(target))
                    continue;
                var candidates = (isLast ? bosses : others).Where(room => !used.Contains(room) && Fits(layout, room, target, gridSize));
                foreach (var room in Shuffle(candidates.ToList(), random))
                {
                    layout[target] = room;
                    used.Add(room);
                    if (Extend(layout, used, others, bosses, roomCount, gridSize, random, ref steps))
                        return true;
                    layout.Remove(target);
                    used.Remove(room);
                    if (steps > MaxSteps)
                        return false;
                }
            }
            return false;
        }

        /// <summary>La salle peut aller sur cette case si ses portes correspondent à ses voisines et ne sortent pas du plan.</summary>
        public static bool Fits(IReadOnlyDictionary<Vector2Int, RoomData> layout, RoomData room, Vector2Int cell, Vector2Int gridSize)
        {
            if (cell.x < 0 || cell.y < 0 || cell.x >= gridSize.x || cell.y >= gridSize.y || layout.ContainsKey(cell))
                return false;
            foreach (var side in FloorData.Sides)
            {
                var next = cell + FloorData.Offset(side);
                if (layout.TryGetValue(next, out var neighbour))
                {
                    if (room.HasDoor(side) != neighbour.HasDoor(FloorData.Opposite(side)))
                        return false;
                }
                else if (room.HasDoor(side) && (next.x < 0 || next.y < 0 || next.x >= gridSize.x || next.y >= gridSize.y))
                {
                    return false;
                }
            }
            return true;
        }

        static IEnumerable<(Vector2Int cell, Doors side)> OpenDoors(Dictionary<Vector2Int, RoomData> layout)
        {
            foreach (var (cell, room) in layout)
                foreach (var side in FloorData.Sides)
                    if (room.HasDoor(side) && !layout.ContainsKey(cell + FloorData.Offset(side)))
                        yield return (cell, side);
        }

        static List<T> Shuffle<T>(List<T> list, System.Random random)
        {
            var copy = new List<T>(list);
            for (var i = copy.Count - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                (copy[i], copy[j]) = (copy[j], copy[i]);
            }
            return copy;
        }

        static Result Fail(string error) => new(null, null, error);
    }
}
