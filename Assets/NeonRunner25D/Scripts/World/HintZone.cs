using UnityEngine;

namespace NeonRunner
{
    /// <summary>Shows a tutorial line the first time the player walks through.</summary>
    [RequireComponent(typeof(Collider2D))]
    public class HintZone : MonoBehaviour
    {
        [SerializeField, TextArea] string message;
        [SerializeField] Color color = new Color(0.4f, 1f, 1f);
        [SerializeField] float duration = 3f;

        bool shown;

        void OnTriggerEnter2D(Collider2D other)
        {
            if (shown || !TriggerUtil.GetPlayer(other)) return;
            shown = true;
            GameManager.Instance?.ShowHint(message, duration);
        }
    }
}
