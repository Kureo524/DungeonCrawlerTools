using TheDungeon.Data;
using UnityEngine;

namespace TheDungeon.Game
{
    /// <summary>Présentoir de la boutique : un objet à acheter à son prix (champ « Prix » de l'outil Objets).</summary>
    public sealed class ShopPedestal : MonoBehaviour, IInteractable
    {
        ItemData item;
        DungeonGame game;

        public Vector3 Position => transform.position;
        public string Prompt => item == null ? string.Empty : $"Acheter {item.DisplayName} — {item.Price} or";
        public bool CanInteract => item != null;

        public static void Spawn(DungeonGame game, ItemData item, Vector3 position)
        {
            var go = new GameObject("Présentoir");
            go.transform.position = position;
            var pedestal = go.AddComponent<SpriteRenderer>();
            pedestal.sprite = game.Database.pedestal;
            pedestal.sortingOrder = 3;
            var icon = new GameObject("Objet");
            icon.transform.SetParent(go.transform, false);
            icon.transform.localPosition = Vector3.up * 0.45f;
            icon.transform.localScale = Vector3.one * 0.8f;
            var renderer = icon.AddComponent<SpriteRenderer>();
            renderer.sprite = item.Icon != null ? item.Icon : GameFx.Circle;
            renderer.sortingOrder = 4;
            var shop = go.AddComponent<ShopPedestal>();
            shop.item = item;
            shop.game = game;
            game.Register(shop);
            game.Track(go);
        }

        public void Interact(DungeonGame dungeon)
        {
            if (item == null)
                return;
            if (!game.State.Spend(item.Price))
            {
                game.Toast($"Pas assez d'or ({item.Price} or nécessaires)", new Color(1f, 0.5f, 0.4f));
                return;
            }
            game.State.AddItem(item);
            item = null;
            Destroy(transform.GetChild(0).gameObject);
        }
    }
}
