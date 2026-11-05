using TheDungeon.Data;
using UnityEngine;

namespace TheDungeon.Game
{
    /// <summary>Ce qui peut être touché et subir un effet (poison, étourdissement, projection…).</summary>
    public interface IStatusTarget
    {
        Vector2 Position { get; }
        void ApplyStatus(StatusEffect status, float power, float duration, Vector2 direction);
    }
}
