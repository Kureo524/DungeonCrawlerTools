using System.Collections.Generic;
using System.Linq;

namespace TheDungeon.Data
{
    /// <summary>
    /// Ensemble d'étages rangé dans un entier : un bit par étage (1 = étage 1, 2 = étage 2, 4 = étage 3…).
    /// Sert aux PNJ (étages où on les rencontre) et à la condition de dialogue « Étages précis ».
    /// </summary>
    public static class FloorSet
    {
        public const int MaxFloors = 30;

        public static int Of(int floor) => floor is >= 1 and <= MaxFloors ? 1 << (floor - 1) : 0;

        public static int Of(IEnumerable<int> floors) => floors.Aggregate(0, (set, floor) => set | Of(floor));

        public static bool Contains(int set, int floor) => (set & Of(floor)) != 0;

        public static int With(int set, int floor, bool included) => included ? set | Of(floor) : set & ~Of(floor);

        public static IEnumerable<int> Floors(int set)
        {
            for (var floor = 1; floor <= MaxFloors; floor++)
                if (Contains(set, floor))
                    yield return floor;
        }

        /// <summary>Premier étage de l'ensemble, 1 s'il est vide.</summary>
        public static int First(int set) => Floors(set).DefaultIfEmpty(1).First();

        /// <summary>« Étage 2 », « Étages 1, 3 », « Aucun étage ».</summary>
        public static string Describe(int set)
        {
            var floors = Floors(set).ToList();
            return floors.Count switch
            {
                0 => "Aucun étage",
                1 => $"Étage {floors[0]}",
                _ => "Étages " + string.Join(", ", floors)
            };
        }
    }
}
