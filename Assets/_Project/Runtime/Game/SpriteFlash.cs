using System.Collections.Generic;
using TheDungeon.Data;
using UnityEngine;

namespace TheDungeon.Game
{
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
}
