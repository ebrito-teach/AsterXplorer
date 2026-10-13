using UnityEngine;

[CreateAssetMenu(fileName = "CometProfile", menuName = "Game/Comet Profile")]
public class CometProfile : ScriptableObject
{
    [System.Serializable]
    public class Piece
    {
        public AsteroidTypeData type;
        public float weight = 1f;
    }

    [Header("Cuándo se desprenden piezas")]
    [Tooltip("Cada cuántos segundos se tira el dado")]
    public float shedInterval = 0.7f;
    [Tooltip("Probabilidad en cada tirada. No siempre se desprende")]
    [Range(0f, 1f)] public float shedChance = 0.4f;
    public Vector2Int piecesPerShed = new Vector2Int(1, 3);
    [Tooltip("Tope de piezas vivas a la vez (rendimiento en móvil)")]
    public int maxDebrisAlive = 40;

    [Header("Qué se desprende (letales e inofensivas)")]
    public Piece[] pieces;

    [Header("Comportamiento propio de cada pieza")]
    [Tooltip("Se queda casi quieta en el sitio")]
    [Range(0f, 1f)] public float stayChance = 0.2f;
    [Tooltip("Se va por su cuenta en una dirección al azar")]
    [Range(0f, 1f)] public float driftChance = 0.3f;
    // el resto hereda movimiento del cometa

    [Header("Si hereda")]
    [Range(0f, 1f)] public float inheritVelocity = 0.6f;
    [Range(0f, 1f)] public float velocityJitter = 0.35f;
    [Tooltip("Desviación de la dirección heredada, en grados")]
    public float directionJitter = 35f;
    [Tooltip("Empujón lateral al desprenderse")]
    public Vector2 ejectSpeed = new Vector2(0.2f, 0.9f);
    [Range(0f, 1f)] public float inheritSpin = 0.5f;

    [Header("Si va a la deriva o se queda")]
    public Vector2 driftSpeed = new Vector2(0.25f, 1.1f);
    public Vector2 staySpeed = new Vector2(0f, 0.12f);

    public bool HasPieces => pieces != null && pieces.Length > 0;

    public AsteroidTypeData PickPiece()
    {
        float total = 0f;
        foreach (var p in pieces)
            if (p != null && p.type != null && p.weight > 0f) total += p.weight;
        if (total <= 0f) return null;

        float pick = Random.value * total;
        foreach (var p in pieces)
        {
            if (p == null || p.type == null || p.weight <= 0f) continue;
            pick -= p.weight;
            if (pick <= 0f) return p.type;
        }
        return null;
    }

    /// Velocidad y giro de una pieza recién desprendida.
    public Vector2 RollVelocity(Vector2 parentVel, float parentSpin, Vector2 fwd, Vector2 side, out float spin)
    {
        float r = Random.value;

        if (r < stayChance)                       // se queda
        {
            spin = Random.Range(-30f, 30f);
            return Random.insideUnitCircle.normalized * Random.Range(staySpeed.x, staySpeed.y);
        }
        r -= stayChance;

        if (r < driftChance)                      // va a la deriva
        {
            spin = Random.Range(-90f, 90f);
            return Random.insideUnitCircle.normalized * Random.Range(driftSpeed.x, driftSpeed.y);
        }

        // hereda
        float k = inheritVelocity * (1f + Random.Range(-velocityJitter, velocityJitter));
        Vector2 v = Quaternion.Euler(0f, 0f, Random.Range(-directionJitter, directionJitter)) * (parentVel * k);
        v += side * Random.Range(-1f, 1f) * Random.Range(ejectSpeed.x, ejectSpeed.y);
        spin = parentSpin * inheritSpin + Random.Range(-45f, 45f);
        return v;
    }
}