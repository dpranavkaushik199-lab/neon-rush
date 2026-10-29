using UnityEngine;

namespace NeonRunner
{
    /// <summary>
    /// Security shutter: a solid door that rolls up while every linked switch is on and
    /// drops again if one goes off. A light strip on its frame shows how many are on.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class PowerGate : MonoBehaviour
    {
        [SerializeField] PowerSwitch[] switches = new PowerSwitch[0];
        [SerializeField] float openHeight = 5f;
        [SerializeField] float openSpeed = 9f;
        [SerializeField] float closeSpeed = 6f;
        [Tooltip("One light per switch, in order.")]
        [SerializeField] Renderer[] statusLights = new Renderer[0];
        [SerializeField] Color offColor = new Color(1f, 0.2f, 0.2f);
        [SerializeField] Color onColor = new Color(0.35f, 1f, 0.7f);

        Rigidbody2D rb;
        Vector2 closedPosition;
        bool wasOpen;

        void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            closedPosition = rb.position;
        }

        void FixedUpdate()
        {
            bool open = switches.Length > 0;
            foreach (var s in switches)
                if (!s || !s.IsOn) open = false;

            if (open && !wasOpen) GameManager.Instance?.ShowPopup("GATE OPEN", onColor, 0.9f);
            wasOpen = open;

            // Never drop the shutter onto someone standing in the doorway.
            if (!open && PlayerInDoorway()) return;
            Vector2 target = closedPosition + (open ? Vector2.up * openHeight : Vector2.zero);
            rb.MovePosition(Vector2.MoveTowards(rb.position, target, (open ? openSpeed : closeSpeed) * Time.fixedDeltaTime));
        }

        bool PlayerInDoorway()
        {
            var player = GameManager.Instance ? GameManager.Instance.Player : null;
            if (!player || player.IsDead) return false;
            var door = new Bounds(closedPosition, GetComponent<Collider2D>().bounds.size);
            return door.Intersects(player.Body.bounds);
        }

        void Update()
        {
            for (int i = 0; i < statusLights.Length; i++)
            {
                bool on = i < switches.Length && switches[i] && switches[i].IsOn;
                Tint.Set(statusLights[i], (on ? onColor : offColor) * 3f);
            }
        }
    }
}
