using System.Collections.Generic;
using TheDungeon.Data;
using UnityEditor;
using UnityEngine;

namespace TheDungeon.Editor.Floors
{
    /// <summary>Modifications du plan d'un étage via SerializedObject (donc annulables avec Ctrl+Z).</summary>
    public static class FloorEditing
    {
        const string Rooms = "rooms";

        /// <summary>Place la salle sur la case. Si elle était déjà sur le plan, elle est déplacée ; la salle qui occupait la case est retirée.</summary>
        public static void Place(SerializedObject floor, RoomData room, Vector2Int cell)
        {
            RemoveWhere(floor, slot => slot.FindPropertyRelative("room").objectReferenceValue == room ||
                                       slot.FindPropertyRelative("cell").vector2IntValue == cell);
            var rooms = floor.FindProperty(Rooms);
            rooms.arraySize++;
            var element = rooms.GetArrayElementAtIndex(rooms.arraySize - 1);
            element.FindPropertyRelative("room").objectReferenceValue = room;
            element.FindPropertyRelative("cell").vector2IntValue = cell;
        }

        public static void RemoveAt(SerializedObject floor, Vector2Int cell)
        {
            var entrance = floor.FindProperty("entrance");
            RemoveWhere(floor, slot =>
            {
                if (slot.FindPropertyRelative("cell").vector2IntValue != cell)
                    return false;
                if (slot.FindPropertyRelative("room").objectReferenceValue == entrance.objectReferenceValue)
                    entrance.objectReferenceValue = null;
                return true;
            });
        }

        public static void SetEntrance(SerializedObject floor, RoomData room) => floor.FindProperty("entrance").objectReferenceValue = room;

        public static void Replace(SerializedObject floor, IReadOnlyDictionary<Vector2Int, RoomData> layout, RoomData entrance)
        {
            var rooms = floor.FindProperty(Rooms);
            rooms.arraySize = 0;
            foreach (var (cell, room) in layout)
            {
                rooms.arraySize++;
                var element = rooms.GetArrayElementAtIndex(rooms.arraySize - 1);
                element.FindPropertyRelative("room").objectReferenceValue = room;
                element.FindPropertyRelative("cell").vector2IntValue = cell;
            }
            SetEntrance(floor, entrance);
        }

        public static void Clear(SerializedObject floor)
        {
            floor.FindProperty(Rooms).arraySize = 0;
            SetEntrance(floor, null);
        }

        static void RemoveWhere(SerializedObject floor, System.Func<SerializedProperty, bool> predicate)
        {
            var rooms = floor.FindProperty(Rooms);
            for (var i = rooms.arraySize - 1; i >= 0; i--)
                if (predicate(rooms.GetArrayElementAtIndex(i)))
                    rooms.DeleteArrayElementAtIndex(i);
        }
    }
}
