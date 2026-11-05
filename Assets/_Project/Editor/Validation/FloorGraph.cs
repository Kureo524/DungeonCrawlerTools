using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using UnityEngine;

namespace TheDungeon.Editor.Validation
{
    public enum DoorState
    {
        /// <summary>La porte donne sur une salle voisine qui a la porte opposée.</summary>
        Connected,
        /// <summary>La porte donne sur une case vide du plan.</summary>
        LeadsNowhere,
        /// <summary>La porte donne sur une salle voisine qui n'a pas de porte de ce côté.</summary>
        Blocked
    }

    /// <summary>Une porte d'une salle placée sur le plan d'un étage, et ce qu'elle rejoint.</summary>
    public readonly struct FloorDoor
    {
        public readonly Vector2Int Cell;
        public readonly Doors Side;
        public readonly RoomData Room;
        public readonly RoomData Neighbour;
        public readonly DoorState State;

        public FloorDoor(Vector2Int cell, Doors side, RoomData room, RoomData neighbour, DoorState state)
        {
            Cell = cell;
            Side = side;
            Room = room;
            Neighbour = neighbour;
            State = state;
        }
    }

    /// <summary>
    /// Plan d'un étage vu comme un graphe : deux salles voisines sont reliées si elles ont des portes face à face.
    /// Utilisé par la validation, l'outil Étages et le générateur de plans.
    /// </summary>
    public static class FloorGraph
    {
        /// <summary>Biome d'un étage : celui de ses salles (le plus présent si elles en mélangent plusieurs), ou null si aucune n'en a.</summary>
        public static BiomeData BiomeOf(IEnumerable<RoomData> rooms) =>
            rooms.Where(room => room != null && room.Biome != null).GroupBy(room => room.Biome)
                .OrderByDescending(group => group.Count()).Select(group => group.Key).FirstOrDefault();

        public static Dictionary<Vector2Int, RoomData> Map(IEnumerable<FloorRoomSlot> slots)
        {
            var map = new Dictionary<Vector2Int, RoomData>();
            foreach (var slot in slots)
                if (slot.room != null && !map.ContainsKey(slot.cell))
                    map[slot.cell] = slot.room;
            return map;
        }

        public static IEnumerable<FloorDoor> AllDoors(IReadOnlyDictionary<Vector2Int, RoomData> map)
        {
            foreach (var (cell, room) in map)
            {
                foreach (var side in FloorData.Sides)
                {
                    if (!room.HasDoor(side))
                        continue;
                    var next = cell + FloorData.Offset(side);
                    if (!map.TryGetValue(next, out var neighbour))
                        yield return new FloorDoor(cell, side, room, null, DoorState.LeadsNowhere);
                    else if (!neighbour.HasDoor(FloorData.Opposite(side)))
                        yield return new FloorDoor(cell, side, room, neighbour, DoorState.Blocked);
                    else
                        yield return new FloorDoor(cell, side, room, neighbour, DoorState.Connected);
                }
            }
        }

        public static bool AreConnected(IReadOnlyDictionary<Vector2Int, RoomData> map, Vector2Int cell, Doors side) =>
            map.TryGetValue(cell, out var room) && room.HasDoor(side) &&
            map.TryGetValue(cell + FloorData.Offset(side), out var neighbour) && neighbour.HasDoor(FloorData.Opposite(side));

        /// <summary>Cases atteignables depuis l'entrée en passant uniquement par des portes reliées.</summary>
        public static HashSet<Vector2Int> Reachable(IReadOnlyDictionary<Vector2Int, RoomData> map, Vector2Int start)
        {
            var visited = new HashSet<Vector2Int>();
            if (!map.ContainsKey(start))
                return visited;
            var queue = new Queue<Vector2Int>();
            visited.Add(start);
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                foreach (var side in FloorData.Sides)
                {
                    var next = cell + FloorData.Offset(side);
                    if (!visited.Contains(next) && AreConnected(map, cell, side))
                    {
                        visited.Add(next);
                        queue.Enqueue(next);
                    }
                }
            }
            return visited;
        }

        public static bool TryFindCell(IReadOnlyDictionary<Vector2Int, RoomData> map, RoomData room, out Vector2Int cell)
        {
            foreach (var (candidate, placed) in map)
            {
                if (placed == room)
                {
                    cell = candidate;
                    return true;
                }
            }
            cell = default;
            return false;
        }

        public static int BossCount(IEnumerable<RoomData> rooms) =>
            rooms.Sum(room => room.Enemies.Count(spawn => spawn.enemy != null && spawn.enemy.IsBoss));

        public static string SideName(Doors side) => side switch
        {
            TheDungeon.Data.Doors.North => "nord",
            TheDungeon.Data.Doors.East => "est",
            TheDungeon.Data.Doors.South => "sud",
            TheDungeon.Data.Doors.West => "ouest",
            _ => side.ToString()
        };
    }
}
