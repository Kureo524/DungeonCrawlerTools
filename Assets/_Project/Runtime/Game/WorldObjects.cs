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
            var color = game.Database.GetRarityColor(item.Rarity);
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
                GameFx.Float(transform.position, item.DisplayName, game.Database.GetRarityColor(item.Rarity), 0.9f);
            }
            if (gold > 0)
            {
                game.State.AddGold(gold);
                GameFx.Float(transform.position, $"+{gold} or", new Color(1f, 0.85f, 0.3f), 0.8f);
            }
            Destroy(gameObject);
        }
    }

    /// <summary>Coffre : à l'ouverture, tire sa table de butin (bonus du biome compris).</summary>
    public sealed class Chest : MonoBehaviour, IInteractable
    {
        LootTable table;
        BiomeData biome;
        int floor;
        bool opened;
        SpriteRenderer sprite;
        Sprite openSprite;

        public Vector3 Position => transform.position;
        public string Prompt => "Ouvrir le coffre";
        public bool CanInteract => !opened;

        public static void Spawn(DungeonGame game, LootTable table, BiomeData biome, int floor, Vector3 position)
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
            game.Register(chest);
            game.Track(go);
        }

        public void Interact(DungeonGame game)
        {
            if (opened)
                return;
            opened = true;
            sprite.sprite = openSprite != null ? openSprite : sprite.sprite;
            var dropped = game.DropLoot(table, biome, floor, transform.position, guaranteed: true);
            if (dropped == 0)
                Pickup.SpawnGold(game, 5 + floor * 5, transform.position + Vector3.down * 0.6f);
            game.CameraFollow.Shake(0.1f);
        }
    }

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

    /// <summary>
    /// Feu de camp : le seul endroit où le joueur répartit ses points d'attributs. Il brûle dès l'arrivée dans une salle
    /// sans ennemis, et s'allume dans une salle de combat une fois tous les ennemis vaincus. Dessiné en code (aucun sprite requis).
    /// </summary>
    public sealed class Campfire : MonoBehaviour, IInteractable
    {
        static Sprite square;

        Transform flame;
        Transform core;
        SpriteRenderer glow;
        float seed;
        int healPercent;

        public Vector3 Position => transform.position;
        public string Prompt => "Se reposer au feu de camp";
        public bool CanInteract => true;

        /// <param name="heal">PV rendus en se reposant, en % des PV max (règle de la salle ou de son étage).</param>
        public static void Spawn(DungeonGame game, Vector3 position, int heal)
        {
            var go = new GameObject("Feu de camp");
            go.transform.position = position;
            go.AddComponent<BoxCollider2D>().size = new Vector2(0.7f, 0.4f);
            var campfire = go.AddComponent<Campfire>();
            campfire.seed = Random.value * 100f;
            campfire.healPercent = heal;
            campfire.glow = Part(go.transform, GameFx.Circle, new Color(1f, 0.55f, 0.15f, 0.22f), new Vector3(0f, 0.05f), new Vector3(2.6f, 2.2f), 0f, 3);
            Part(go.transform, Square, new Color(0.42f, 0.26f, 0.14f), new Vector3(0f, -0.2f), new Vector3(0.75f, 0.13f), 22f, 4);
            Part(go.transform, Square, new Color(0.35f, 0.21f, 0.11f), new Vector3(0f, -0.2f), new Vector3(0.75f, 0.13f), -22f, 4);
            campfire.flame = Part(go.transform, GameFx.Circle, new Color(1f, 0.45f, 0.1f), new Vector3(0f, 0.02f), new Vector3(0.5f, 0.75f), 0f, 5).transform;
            campfire.core = Part(go.transform, GameFx.Circle, new Color(1f, 0.88f, 0.35f), new Vector3(0f, -0.04f), new Vector3(0.26f, 0.42f), 0f, 6).transform;
            game.Register(campfire);
            game.Track(go);
        }

        static SpriteRenderer Part(Transform parent, Sprite sprite, Color color, Vector3 position, Vector3 scale, float angle, int order)
        {
            var go = new GameObject("Partie");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = new Vector3(scale.x, scale.y, 1f);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = order;
            return renderer;
        }

        /// <summary>Carré blanc de 1 unité, à teinter (bûches).</summary>
        static Sprite Square
        {
            get
            {
                if (square != null)
                    return square;
                var texture = new Texture2D(4, 4) { filterMode = FilterMode.Point };
                var pixels = new Color[16];
                for (var i = 0; i < pixels.Length; i++)
                    pixels[i] = Color.white;
                texture.SetPixels(pixels);
                texture.Apply();
                square = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4);
                return square;
            }
        }

        /// <summary>La flamme vacille (bruit de Perlin) et le halo respire.</summary>
        void Update()
        {
            var time = Time.time * 6f + seed;
            var flicker = Mathf.PerlinNoise(time, seed);
            flame.localScale = new Vector3(0.45f + 0.1f * flicker, 0.65f + 0.2f * Mathf.PerlinNoise(seed, time), 1f);
            core.localScale = new Vector3(0.24f + 0.05f * flicker, 0.36f + 0.1f * Mathf.PerlinNoise(time * 1.3f, seed), 1f);
            glow.color = new Color(1f, 0.55f, 0.15f, 0.16f + 0.1f * flicker);
        }

        public void Interact(DungeonGame game) => game.OpenCampfire(healPercent);
    }
}
