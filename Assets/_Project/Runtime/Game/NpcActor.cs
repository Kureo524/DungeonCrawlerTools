using TheDungeon.Data;
using UnityEngine;

namespace TheDungeon.Game
{
    /// <summary>Personnage non joueur : E pour lancer son dialogue (outil Dialogues).</summary>
    public sealed class NpcActor : MonoBehaviour, IInteractable
    {
        public NpcData Npc { get; private set; }

        public Vector3 Position => transform.position;
        public string Prompt => $"Parler à {Npc.DisplayName}";
        public bool CanInteract => true;

        public static void Spawn(DungeonGame game, NpcData npc, Vector3 position)
        {
            var go = new GameObject(npc.DisplayName);
            go.transform.position = position;
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = npc.Portrait != null ? npc.Portrait : GameFx.Circle;
            renderer.sortingOrder = 5;
            go.AddComponent<CircleCollider2D>().radius = 0.35f;
            var body = go.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Static;
            var actor = go.AddComponent<NpcActor>();
            actor.Npc = npc;
            game.Register(actor);
            game.Track(go);
        }

        public void Interact(DungeonGame game) => game.StartDialogue(Npc);
    }
}
