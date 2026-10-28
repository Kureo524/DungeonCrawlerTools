using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using UnityEngine;

namespace TheDungeon.Editor.Floors
{
    /// <summary>
    /// Assemble un plan d'étage à partir des salles disponibles : une salle de départ au centre,
    /// des salles dont les portes correspondent à celles de leurs voisines, et la salle du boss placée en dernier
    /// (en fond d'étage). Recherche avec retour arrière, limitée en nombre d'essais.
    /// </summary>
    public static class FloorGenerator
    {
        const int MaxSteps = 50000;

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

        public static Result Generate(IReadOnlyList<RoomData> pool, int roomCount, Vector2Int gridSize, int seed)
        {
            var random = new System.Random(seed);
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
