using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;

namespace TheDungeon.Editor.Validation
{
    /// <summary>
    /// Règles de cohérence du GDD sur les étages : portes compatibles entre salles reliées,
    /// toute salle atteignable depuis l'entrée, exactement un boss par étage.
    /// </summary>
    public sealed class FloorRule : ValidationRule
    {
        public override string Name => "Étages";
        public override string Description =>
            "Portes compatibles entre salles reliées, toutes les salles atteignables depuis l'entrée, exactement un boss par étage.";

        public override IEnumerable<ValidationIssue> Run(ValidationContext context)
        {
            var limits = context.Limits;
            foreach (var floor in context.Floors)
            {
                if (floor.FloorNumber < 1 || floor.FloorNumber > limits.floorCount)
                    yield return Error($"Numéro d'étage {floor.FloorNumber} invalide (1 à {limits.floorCount}).", floor);
                if (context.Floors.Count(other => other.FloorNumber == floor.FloorNumber) > 1)
                    yield return Error($"Plusieurs plans portent le numéro d'étage {floor.FloorNumber}.", floor);

                foreach (var issue in CheckSlots(floor, context))
                    yield return issue;

                var map = FloorGraph.Map(floor.Rooms);
                if (map.Count == 0)
                {
                    yield return Error("Le plan de l'étage est vide.", floor);
                    continue;
                }
                if (map.Count != limits.roomsPerFloor)
                    yield return Warning($"L'étage contient {map.Count} salle(s) au lieu des {limits.roomsPerFloor} prévues par le GDD.", floor);

                foreach (var door in FloorGraph.AllDoors(map))
                {
                    var where = $"La porte {FloorGraph.SideName(door.Side)} de « {door.Room.DisplayName} » ({door.Cell.x}, {door.Cell.y})";
                    if (door.State == DoorState.LeadsNowhere)
                        yield return Error($"{where} mène dans le vide : placez une salle de ce côté ou choisissez une salle sans cette porte.", floor);
                    else if (door.State == DoorState.Blocked)
                        yield return Error($"{where} donne sur « {door.Neighbour.DisplayName} », qui n'a pas de porte {FloorGraph.SideName(FloorData.Opposite(door.Side))} : portes incompatibles.", floor);
                }

                foreach (var issue in CheckEntranceAndAccess(floor, map))
                    yield return issue;

                var bosses = FloorGraph.BossCount(map.Values);
                if (bosses != 1)
                    yield return Error($"L'étage contient {bosses} boss : il en faut exactement un (GDD).", floor);
            }

            // Une salle qui n'est sur aucun étage ne sera jamais jouée.
            var placed = new HashSet<RoomData>(context.Floors.SelectMany(f => f.Rooms).Select(s => s.room).Where(r => r != null));
            foreach (var room in context.Rooms.Where(room => !placed.Contains(room)))
                yield return Warning("Cette salle n'est placée sur aucun étage (Forge Studio > Étages).", room);
        }

        static IEnumerable<ValidationIssue> CheckSlots(FloorData floor, ValidationContext context)
        {
            var limits = context.Limits;
            var seenCells = new HashSet<UnityEngine.Vector2Int>();
            var seenRooms = new HashSet<RoomData>();
            foreach (var slot in floor.Rooms)
            {
                if (slot.room == null)
                {
                    yield return Error($"La case ({slot.cell.x}, {slot.cell.y}) référence une salle manquante ou supprimée.", floor);
                    continue;
                }
                if (slot.cell.x < 0 || slot.cell.y < 0 || slot.cell.x >= limits.floorGridSize.x || slot.cell.y >= limits.floorGridSize.y)
                    yield return Error($"« {slot.room.DisplayName} » est hors du plan ({slot.cell.x}, {slot.cell.y}).", floor);
                if (!seenCells.Add(slot.cell))
                    yield return Error($"Deux salles occupent la même case ({slot.cell.x}, {slot.cell.y}).", floor);
                if (!seenRooms.Add(slot.room))
                    yield return Error($"« {slot.room.DisplayName} » est placée plusieurs fois sur l'étage.", floor);
                if (slot.room.Biome != null && (floor.FloorNumber < slot.room.Biome.MinFloor || floor.FloorNumber > slot.room.Biome.MaxFloor))
                    yield return Error($"« {slot.room.DisplayName} » est du biome {slot.room.Biome.DisplayName}, réservé aux étages {slot.room.Biome.MinFloor} à {slot.room.Biome.MaxFloor}.", floor);

                var others = context.Floors.Where(other => other != floor && other.Rooms.Any(s => s.room == slot.room)).ToList();
                if (others.Count > 0)
                    yield return Error($"« {slot.room.DisplayName} » est aussi placée sur l'étage {others[0].FloorNumber} : chaque salle n'appartient qu'à un étage.", floor);
            }
        }

        static IEnumerable<ValidationIssue> CheckEntranceAndAccess(FloorData floor, Dictionary<UnityEngine.Vector2Int, RoomData> map)
        {
            if (floor.Entrance == null)
            {
                yield return Error("L'étage n'a pas de salle d'entrée.", floor);
                yield break;
            }
            if (!FloorGraph.TryFindCell(map, floor.Entrance, out var start))
            {
                yield return Error($"La salle d'entrée « {floor.Entrance.DisplayName} » n'est pas placée sur le plan.", floor);
                yield break;
            }
            if (floor.Entrance.RoomType != RoomType.Start)
                yield return Warning($"La salle d'entrée « {floor.Entrance.DisplayName} » n'est pas une salle de départ (type {floor.Entrance.RoomType}).", floor);

            var reachable = FloorGraph.Reachable(map, start);
            foreach (var (cell, room) in map.Where(entry => !reachable.Contains(entry.Key)))
                yield return Error($"« {room.DisplayName} » ({cell.x}, {cell.y}) n'est pas atteignable depuis l'entrée.", floor);
        }
    }
}
