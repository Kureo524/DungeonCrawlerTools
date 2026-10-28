using TheDungeon.Editor.Common;
using UnityEditor;
using UnityEngine;

namespace TheDungeon.Editor.RoomEditor
{
    /// <summary>Sprites communs utilisés par l'éditeur de salles (coffre, porte, joueur).</summary>
    public static class RoomArt
    {
        public static Sprite Chest => Load("SPR_Chest");
        public static Sprite Door => Load("SPR_DoorOpen");
        public static Sprite Player => Load("SPR_Player");

        static Sprite Load(string name) => AssetDatabase.LoadAssetAtPath<Sprite>($"{DungeonPaths.Art}/Common/{name}.png");
    }
}
