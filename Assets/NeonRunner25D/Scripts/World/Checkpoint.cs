using UnityEngine;

namespace NeonRunner
{
    /// <summary>Sets the respawn point when touched.</summary>
    [RequireComponent(typeof(Collider2D))]
    public class Checkpoint : MonoBehaviour
    {
        [SerializeField] Transform spawnPoint;
        [SerializeField] Renderer beacon;
        [SerializeField] Light beaconLight;
        [SerializeField] Color activeColor = new Color(0.3f, 1f, 1f);

        void OnTriggerEnter2D(Collider2D other)
        {
            if (!TriggerUtil.GetPlayer(other) || !GameManager.Instance) return;
            GameManager.Instance.SetCheckpoint(spawnPoint ? spawnPoint.position : transform.position);
            Tint.Set(beacon, activeColor * 2f);
            if (beaconLight) beaconLight.color = activeColor;
        }
    }
}
