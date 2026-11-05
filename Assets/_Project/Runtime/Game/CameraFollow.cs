using System.Collections.Generic;
using TheDungeon.Data;
using UnityEngine;

namespace TheDungeon.Game
{
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
