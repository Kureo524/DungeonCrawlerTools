using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheDungeon.Data
{
    [Serializable]
    public struct FloorRoomSlot
    {
        [Tooltip("Salle placée sur le plan de l'étage.")]
        public RoomData room;
        [Tooltip("Position de la salle sur le plan (une case = une salle).")]
        public Vector2Int cell;
    }

    [CreateAssetMenu(menuName = "The Dungeon/Floor", fileName = "FLR_1")]
    public class FloorData : ScriptableObject
    {
        [SerializeField, Tooltip("Numéro de l'étage, de 1 (surface) à 3 (le plus profond).")]
        int floorNumber = 1;
        [SerializeField, Tooltip("Salles de l'étage et leur position sur le plan.")]
        List<FloorRoomSlot> rooms = new();
        [SerializeField, Tooltip("Salle où le joueur arrive sur l'étage.")]
        RoomData entrance;

        public int FloorNumber => floorNumber;
        public IReadOnlyList<FloorRoomSlot> Rooms => rooms;
        public RoomData Entrance => entrance;

        public static Vector2Int Offset(Doors side) => side switch
        {
            Doors.North => Vector2Int.up,
            Doors.South => Vector2Int.down,
            Doors.East => Vector2Int.right,
            Doors.West => Vector2Int.left,
            _ => Vector2Int.zero
        };

        public static Doors Opposite(Doors side) => side switch
        {
            Doors.North => Doors.South,
            Doors.South => Doors.North,
            Doors.East => Doors.West,
            Doors.West => Doors.East,
            _ => Doors.None
        };

        public static readonly Doors[] Sides = { Doors.North, Doors.East, Doors.South, Doors.West };

        public bool TryGetRoomAt(Vector2Int cell, out RoomData room)
        {
            foreach (var slot in rooms)
            {
                if (slot.cell == cell && slot.room != null)
                {
                    room = slot.room;
                    return true;
                }
            }
            room = null;
            return false;
        }
    }
}
