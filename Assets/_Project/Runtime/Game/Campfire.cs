using TheDungeon.Data;
using UnityEngine;

namespace TheDungeon.Game
{
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
