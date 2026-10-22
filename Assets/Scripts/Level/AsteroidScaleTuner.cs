using UnityEngine;

/// Para probar rocas más grandes sin tocar cada tipo: multiplica el tamaño de cada clase.
///
/// El generador compra menos cuanto más grandes son (el coste crece con el área), así la
/// densidad se mantiene. La calibración fina va con el pendiente H. Los cambios se aplican a
/// las rocas que nacen desde ese momento (para verlo en todas, reinicia el nivel).
///
/// Va en un objeto vacío de la escena.
public class AsteroidScaleTuner : MonoBehaviour
{
    [Range(0.5f, 6f)] [SerializeField] private float statics = 1f;
    [Range(0.5f, 6f)] [SerializeField] private float slow = 1f;
    [Range(0.5f, 6f)] [SerializeField] private float fast = 1f;
    [Range(0.5f, 6f)] [SerializeField] private float breakable = 1f;
    [Range(0.5f, 6f)] [SerializeField] private float colossus = 1f;
    [Range(0.5f, 6f)] [SerializeField] private float comets = 1f;
    [Tooltip("Gravilla, fragmentos y piezas sueltas")]
    [Range(0.5f, 6f)] [SerializeField] private float debris = 1f;

    void Awake() => Apply();
    void OnValidate() => Apply();

    void Apply()
    {
        var s = AsteroidTypeData.BehaviorScale;
        s[(int)AsteroidBehavior.Static] = statics;
        s[(int)AsteroidBehavior.Slow] = slow;
        s[(int)AsteroidBehavior.Fast] = fast;
        s[(int)AsteroidBehavior.Breakable] = breakable;
        s[(int)AsteroidBehavior.Colossus] = colossus;
        s[(int)AsteroidBehavior.Debris] = debris;
        AsteroidTypeData.CometScale = comets;
    }
}
