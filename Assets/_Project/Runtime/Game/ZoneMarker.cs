using System.Collections.Generic;
using TheDungeon.Data;
using UnityEngine;

namespace TheDungeon.Game
{
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
}
