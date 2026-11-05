using TheDungeon.Data;
using UnityEngine;

namespace TheDungeon.Game
{
    /// <summary>Élément avec lequel le joueur interagit (touche E).</summary>
    public interface IInteractable
    {
        Vector3 Position { get; }
        string Prompt { get; }
        bool CanInteract { get; }
        void Interact(DungeonGame game);
    }
}
