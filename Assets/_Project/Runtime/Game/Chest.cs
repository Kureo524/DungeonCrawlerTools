using TheDungeon.Data;
using UnityEngine;

namespace TheDungeon.Game
{
    /// <summary>Coffre : à l'ouverture, tire sa table de butin (bonus du biome compris).</summary>
    public sealed class Chest : MonoBehaviour, IInteractable
    {
        static readonly Color SealedTint = new(0.45f, 0.45f, 0.52f);

        LootTable table;
        BiomeData biome;
        int floor;
        bool opened;
        SpriteRenderer sprite;
        Sprite openSprite;
        RoomInstance room;

        public Vector3 Position => transform.position;
        public string Prompt => "Ouvrir le coffre";
        // Dans une salle de combat, le coffre reste scellé tant que la salle n'est pas nettoyée.
        public bool CanInteract => !opened && !Sealed;
        bool Sealed => room != null && room.IsCombat && !room.Cleared;

        public static void Spawn(DungeonGame game, LootTable table, BiomeData biome, int floor, Vector3 position, RoomInstance room = null)
        {
            var go = new GameObject("Coffre");
            go.transform.position = position;
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = game.Database.chestClosed;
            renderer.sortingOrder = 4;
            go.AddComponent<BoxCollider2D>().size = new Vector2(0.8f, 0.6f);
            var chest = go.AddComponent<Chest>();
            chest.table = table;
            chest.biome = biome;
            chest.floor = floor;
            chest.sprite = renderer;
            chest.openSprite = game.Database.chestOpen;
            chest.room = room;
            if (chest.Sealed)
                renderer.color = SealedTint;
            game.Register(chest);
            game.Track(go);
        }

        void Update()
        {
            // La salle vient d'être nettoyée : le coffre se descelle.
            if (room != null && sprite.color != Color.white && !Sealed)
            {
                sprite.color = Color.white;
                GameFx.Float(transform.position + Vector3.up * 0.6f, "Coffre descellé", new Color(1f, 0.85f, 0.45f), 0.8f);
            }
        }

        public void Interact(DungeonGame game)
        {
            if (opened || Sealed)
                return;
            opened = true;
            sprite.sprite = openSprite != null ? openSprite : sprite.sprite;
            var dropped = game.DropLoot(table, biome, floor, transform.position, guaranteed: true);
            if (dropped == 0)
                Pickup.SpawnGold(game, 5 + floor * 5, transform.position + Vector3.down * 0.6f);
            game.CameraFollow.Shake(0.1f);
        }
    }
}
