using System.Collections.Generic;
using TheDungeon.Data;
using UnityEngine;

namespace TheDungeon.Game
{
    /// <summary>Petits effets visuels : chiffres flottants, flash à l'impact, secousse de caméra.</summary>
    public static class GameFx
    {
        public sealed class FloatingText
        {
            public Vector3 Position;
            public string Text;
            public Color Color;
            public float Age;
            public float Size;
        }

        const float TextLifetime = 0.9f;

        public static readonly List<FloatingText> Texts = new();
        static Sprite circle;

        public static void Float(Vector3 position, string text, Color color, float size = 1f)
        {
            Texts.Add(new FloatingText
            {
                Position = position + new Vector3(Random.Range(-0.2f, 0.2f), 0.4f, 0f),
                Text = text,
                Color = color,
                Size = size
            });
        }

        public static void Tick(float deltaTime)
        {
            for (var i = Texts.Count - 1; i >= 0; i--)
            {
                var text = Texts[i];
                text.Age += deltaTime;
                text.Position += Vector3.up * deltaTime * 1.2f;
                if (text.Age > TextLifetime)
                    Texts.RemoveAt(i);
            }
        }

        public static float Alpha(FloatingText text) => 1f - Mathf.Clamp01((text.Age - TextLifetime * 0.5f) / (TextLifetime * 0.5f));

        public static void Clear() => Texts.Clear();

        /// <summary>Disque blanc généré une fois (projectiles, halos), à teinter.</summary>
        public static Sprite Circle
        {
            get
            {
                if (circle != null)
                    return circle;
                const int size = 32;
                var texture = new Texture2D(size, size) { filterMode = FilterMode.Bilinear };
                var center = (size - 1) / 2f;
                for (var y = 0; y < size; y++)
                    for (var x = 0; x < size; x++)
                    {
                        var distance = new Vector2(x - center, y - center).magnitude / center;
                        texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01((1f - distance) * 4f)));
                    }
                texture.Apply();
                circle = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
                return circle;
            }
        }
    }

    /// <summary>
    /// Zone d'une frappe d'ennemi, dessinée au sol : annonce pendant la préparation (qui se remplit jusqu'à l'impact),
    /// puis éclat bref à l'impact. Cercle, anneau, cône ou ligne, comme dans l'outil Attaques.
    /// </summary>
    public sealed class ZoneMarker : MonoBehaviour
    {
        static Material material;
        MeshRenderer meshRenderer;
        Color color;
        float duration;
        float age;
        bool flash;

        /// <param name="flash">Éclat d'impact (s'efface) ; sinon annonce (s'intensifie jusqu'à la fin).</param>
        public static ZoneMarker Show(AttackStep step, Vector2 origin, Vector2 aim, float duration, bool flash)
        {
            var go = new GameObject(flash ? "Impact" : "Annonce");
            go.transform.position = new Vector3(origin.x, origin.y, 0f);
            go.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(aim.y, aim.x) * Mathf.Rad2Deg);
            go.AddComponent<MeshFilter>().sharedMesh = Build(step);
            var marker = go.AddComponent<ZoneMarker>();
            marker.meshRenderer = go.AddComponent<MeshRenderer>();
            marker.meshRenderer.sharedMaterial = material ??= new Material(Shader.Find("Sprites/Default"));
            marker.meshRenderer.sortingOrder = 3;
            marker.color = flash ? new Color(1f, 0.55f, 0.35f, 0.75f) : new Color(1f, 0.3f, 0.25f, 0.35f);
            marker.duration = Mathf.Max(0.05f, duration);
            marker.flash = flash;
            marker.Apply(0f);
            return marker;
        }

        /// <summary>Déplace la zone (zone qui suit la visée, avance, pivote) ; <paramref name="scale"/> la fait grandir.</summary>
        public void Place(Vector2 origin, Vector2 aim, float scale = 1f)
        {
            transform.SetPositionAndRotation(new Vector3(origin.x, origin.y, 0f), Quaternion.Euler(0f, 0f, Mathf.Atan2(aim.y, aim.x) * Mathf.Rad2Deg));
            transform.localScale = Vector3.one * Mathf.Max(0.01f, scale);
        }

        void Update()
        {
            age += Time.deltaTime;
            Apply(Mathf.Clamp01(age / duration));
            if (age >= duration)
                Destroy(gameObject);
        }

        void Apply(float progress)
        {
            var alpha = flash ? color.a * (1f - progress) : color.a * Mathf.Lerp(0.35f, 1f, progress);
            var block = new MaterialPropertyBlock();
            block.SetColor("_Color", new Color(color.r, color.g, color.b, alpha));
            meshRenderer.SetPropertyBlock(block);
        }

        /// <summary>Maillage de la zone, orienté vers +X (la direction visée).</summary>
        static Mesh Build(AttackStep step)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            const int Segments = 40;
            void Sector(float inner, float outer, float from, float to)
            {
                for (var i = 0; i < Segments; i++)
                {
                    var a0 = Mathf.Lerp(from, to, i / (float)Segments) * Mathf.Deg2Rad;
                    var a1 = Mathf.Lerp(from, to, (i + 1) / (float)Segments) * Mathf.Deg2Rad;
                    var d0 = new Vector3(Mathf.Cos(a0), Mathf.Sin(a0));
                    var d1 = new Vector3(Mathf.Cos(a1), Mathf.Sin(a1));
                    var start = vertices.Count;
                    vertices.Add(d0 * inner);
                    vertices.Add(d0 * outer);
                    vertices.Add(d1 * outer);
                    vertices.Add(d1 * inner);
                    triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
                }
            }
            switch (step.shape)
            {
                case ZoneShape.Circle:
                    Sector(0f, step.radius, 0f, 360f);
                    break;
                case ZoneShape.Ring:
                    Sector(Mathf.Max(0f, step.innerRadius), step.radius, 0f, 360f);
                    break;
                case ZoneShape.Cone:
                    Sector(0f, step.radius, -step.angle / 2f, step.angle / 2f);
                    break;
                case ZoneShape.Line:
                    var half = step.width / 2f;
                    vertices.AddRange(new[] { new Vector3(0f, -half), new Vector3(step.radius, -half), new Vector3(step.radius, half), new Vector3(0f, half) });
                    triangles.AddRange(new[] { 0, 2, 1, 0, 3, 2 });
                    break;
            }
            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            // Les deux faces : le sens des triangles ne compte pas.
            var both = new List<int>(triangles);
            for (var i = 0; i < triangles.Count; i += 3)
                both.AddRange(new[] { triangles[i], triangles[i + 2], triangles[i + 1] });
            mesh.SetTriangles(both, 0);
            return mesh;
        }
    }

    /// <summary>Flash de couleur bref sur un sprite (impact, invulnérabilité).</summary>
    public sealed class SpriteFlash : MonoBehaviour
    {
        SpriteRenderer target;
        Color baseColor = Color.white;
        float remaining;
        Color flashColor;

        void Awake()
        {
            target = GetComponent<SpriteRenderer>();
            if (target != null)
                baseColor = target.color;
        }

        public void Flash(Color color, float duration = 0.1f)
        {
            flashColor = color;
            remaining = duration;
        }

        void Update()
        {
            if (target == null)
                return;
            if (remaining > 0f)
            {
                remaining -= Time.deltaTime;
                target.color = flashColor;
            }
            else
            {
                target.color = baseColor;
            }
        }
    }

    /// <summary>Caméra qui suit le joueur en douceur, avec secousse à l'impact.</summary>
    public sealed class CameraFollow : MonoBehaviour
    {
        public Transform Target;
        Vector3 velocity;
        float shake;

        public void Shake(float amount) => shake = Mathf.Max(shake, amount);

        public void SnapToTarget()
        {
            if (Target != null)
                transform.position = new Vector3(Target.position.x, Target.position.y, -10f);
        }

        void LateUpdate()
        {
            if (Target == null)
                return;
            var goal = new Vector3(Target.position.x, Target.position.y, -10f);
            var position = Vector3.SmoothDamp(transform.position, goal, ref velocity, 0.12f, Mathf.Infinity, Time.unscaledDeltaTime);
            if (shake > 0f)
            {
                position += (Vector3)(Random.insideUnitCircle * shake);
                shake = Mathf.MoveTowards(shake, 0f, Time.unscaledDeltaTime * 1.5f);
            }
            transform.position = position;
        }
    }
}
