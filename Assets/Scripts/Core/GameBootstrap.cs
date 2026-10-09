using UnityEngine;

public class GameBootstrap : MonoBehaviour
{
    void Awake()
    {
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 0;
        Screen.sleepTimeout = SleepTimeout.NeverSleep; // evita que la pantalla se apague jugando
    }
}