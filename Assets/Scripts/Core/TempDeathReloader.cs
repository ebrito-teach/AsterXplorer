using UnityEngine;
using UnityEngine.SceneManagement;

public class TempDeathReloader : MonoBehaviour
{
    [SerializeField] private PlayerController player;
    [SerializeField] private float delay = 1.5f;

    void OnEnable()  { if (player != null) player.OnDied += HandleDied; }
    void OnDisable() { if (player != null) player.OnDied -= HandleDied; }

    void HandleDied() { Invoke(nameof(Reload), delay); }

    void Reload() { SceneManager.LoadScene(SceneManager.GetActiveScene().name); }
}