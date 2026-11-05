using TheDungeon.Data;
using UnityEngine;

namespace TheDungeon.Game
{
    /// <summary>Objet ou or au sol : attiré vers le joueur quand il s'approche, ramassé au contact.</summary>
    public sealed class Pickup : MonoBehaviour
    {
        const float MagnetRadius = 1.8f;
        const float CollectRadius = 0.45f;

        DungeonGame game;
        ItemData item;
        int gold;
        Vector3 basePosition;
        float age;

        public static void SpawnItem(DungeonGame game, ItemData item, Vector3 position)
        {
            var pickup = Create(game, item.Icon, position, 0.9f);
            pickup.item = item;
            // Halo de la couleur de rareté sous l'objet.
            var halo = new GameObject("Halo");
            halo.transform.SetParent(pickup.transform, false);
            halo.transform.localScale = Vector3.one * 1.4f;
            var renderer = halo.AddComponent<SpriteRenderer>();
            renderer.sprite = GameFx.Circle;
            var color = game.Database.GetRarityColor(item.RarityId);
            renderer.color = new Color(color.r, color.g, color.b, 0.45f);
            renderer.sortingOrder = 3;
        }

        public static void SpawnGold(DungeonGame game, int amount, Vector3 position)
        {
            var pickup = Create(game, game.Database.gold, position, 0.6f);
            pickup.gold = amount;
        }

        static Pickup Create(DungeonGame game, Sprite sprite, Vector3 position, float scale)
        {
            var go = new GameObject("Butin");
            go.transform.position = position;
            go.transform.localScale = Vector3.one * scale;
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite != null ? sprite : GameFx.Circle;
            renderer.sortingOrder = 4;
            var pickup = go.AddComponent<Pickup>();
            pickup.game = game;
            pickup.basePosition = position;
            game.Track(go);
            return pickup;
        }

        void Update()
        {
            if (game.Player == null)
                return;
            age += Time.deltaTime;
            var toPlayer = game.Player.transform.position - basePosition;
            if (age > 0.4f && toPlayer.magnitude < MagnetRadius)
                basePosition = Vector3.MoveTowards(basePosition, game.Player.transform.position, Time.deltaTime * 7f);
            transform.position = basePosition + Vector3.up * Mathf.Sin(age * 4f) * 0.06f;
            if (age > 0.4f && (game.Player.transform.position - basePosition).magnitude < CollectRadius)
                Collect();
        }

        void Collect()
        {
            if (item != null)
            {
                game.State.AddItem(item);
                GameFx.Float(transform.position, item.DisplayName, game.Database.GetRarityColor(item.RarityId), 0.9f);
            }
            if (gold > 0)
            {
                game.State.AddGold(gold);
                GameFx.Float(transform.position, $"+{gold} or", new Color(1f, 0.85f, 0.3f), 0.8f);
            }
            Destroy(gameObject);
        }
    }
}
