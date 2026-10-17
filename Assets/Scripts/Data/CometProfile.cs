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

    public enum ShedPattern { Line, Scatter, None }

    [Header("Patrón de cada desprendimiento")]
    [Tooltip("Piezas quietas sobre la trayectoria. Se quedan todo el nivel y dibujan el " +
             "camino que siguió el cometa. Los huecos entre tramos salen solos, cuando un " +
             "desprendimiento cae en otro patrón")]
    public float lineWeight = 55f;
    [Tooltip("Abanico detrás del cometa: unas se quedan, otras derivan, otras lo siguen. " +
             "Duran un tiempo limitado")]
    public float scatterWeight = 30f;
    [Tooltip("No suelta nada")]
    public float noneWeight = 15f;

    [Header("Línea (rastro permanente)")]
    [Tooltip("Piezas por tramo de línea")]
    public Vector2Int linePieces = new Vector2Int(2, 3);
    [Tooltip("Separación mínima entre piezas del mismo tramo (unidades)")]
    public float lineMinSpacing = 0.35f;
    [Tooltip("Desviación lateral máxima respecto a la trayectoria, en radios del cometa. " +
             "0 = línea perfecta")]
    [Range(0f, 1f)] public float lineJitter = 0.12f;
    [Tooltip("Tope de piezas de rastro en todo el nivel, sumando todos los cometas. Al " +
             "llegar, la más antigua se desvanece para dejar sitio a la nueva")]
    public int maxTrailPieces = 60;
    [Tooltip("Segundos que tarda en desvanecerse la pieza de rastro más antigua")]
    public float trailFadeSeconds = 2f;

    [Header("Disperso")]
    [Tooltip("Segundos de vida de cada pieza dispersa. 0 = la vida que diga su tipo")]
    public float scatterLifetime = 12f;

    [Header("Cuándo se desprenden piezas")]
    [Tooltip("Cada cuántos segundos se tira el dado del patrón")]
    public float shedInterval = 0.7f;
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

    public ShedPattern PickPattern()
    {
        float l = Mathf.Max(0f, lineWeight), s = Mathf.Max(0f, scatterWeight), n = Mathf.Max(0f, noneWeight);
        float total = l + s + n;
        if (total <= 0f) return ShedPattern.Scatter;
        float r = Random.value * total;
        if (r < l) return ShedPattern.Line;
        if (r < l + s) return ShedPattern.Scatter;
        return ShedPattern.None;
    }

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