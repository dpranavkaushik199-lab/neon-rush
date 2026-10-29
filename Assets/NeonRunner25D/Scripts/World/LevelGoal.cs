using UnityEngine;

namespace NeonRunner
{
    /// <summary>End of the level.</summary>
    [RequireComponent(typeof(Collider2D))]
    public class LevelGoal : MonoBehaviour
    {
        void OnTriggerEnter2D(Collider2D other)
        {
            if (TriggerUtil.GetPlayer(other)) GameManager.Instance?.Finish();
        }
    }
}
