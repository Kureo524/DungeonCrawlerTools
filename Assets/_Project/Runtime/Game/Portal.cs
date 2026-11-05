using TheDungeon.Data;
using UnityEngine;

namespace TheDungeon.Game
{
    /// <summary>Portail qui apparaît quand le boss de l'étage est vaincu.</summary>
    public sealed class Portal : MonoBehaviour, IInteractable
    {
        bool last;

        public Vector3 Position => transform.position;
        public string Prompt => last ? "Sortir du donjon" : "Descendre à l'étage suivant";
        public bool CanInteract => true;

        public static void Spawn(DungeonGame game, Vector3 position, bool last)
        {
            var go = new GameObject("Portail");
            go.transform.position = position;
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = game.Database.portal != null ? game.Database.portal : GameFx.Circle;
            renderer.sortingOrder = 4;
            go.transform.localScale = Vector3.one * 1.5f;
            var portal = go.AddComponent<Portal>();
            portal.last = last;
            game.Register(portal);
            game.Track(go);
        }

        void Update() => transform.Rotate(0f, 0f, 45f * Time.deltaTime);

        public void Interact(DungeonGame game) => game.NextFloor();
    }
}
