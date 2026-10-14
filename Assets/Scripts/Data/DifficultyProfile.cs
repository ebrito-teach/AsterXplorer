using UnityEngine;

[CreateAssetMenu(fileName = "DifficultyProfile", menuName = "Game/Difficulty Profile")]
public class DifficultyProfile : ScriptableObject
{
    [Header("Etapas")]
    [Tooltip("Cada cuántos niveles hay un salto de etapa")]
    public int tierLength = 20;

    [Header("Cantidad (presupuesto de puntos)")]
    [Tooltip("La pantalla mide 19.4 x 9 unidades. Con 26 puntos el cinturón se ve vacío; " +
             "a partir de 45 empieza a sentirse un cinturón de verdad")]
    public float baseBudget = 45f;
    [Tooltip("Aumento pequeño en cada nivel")]
    public float budgetPerLevel = 1.2f;
    [Tooltip("Aumento extra al entrar en cada etapa")]
    public float budgetPerTier = 10f;
    public float maxBudget = 180f;
    [Tooltip("Tope de asteroides en movimiento a la vez (rendimiento en móvil)")]
    public int maxMovingAsteroids = 55;

    [Header("Velocidad")]
    public float speedPerLevel = 0.006f;
    public float speedPerTier = 0.06f;
    public float maxSpeedMultiplier = 2.1f;

    [Header("Ritmo: niveles de respiro y niveles de apretón")]
    [Tooltip("Un nivel de cada tantos es un respiro. 0 = ninguno. Sin respiros, la " +
             "dificultad creciente se vuelve plana: el jugador deja de notar que sube")]
    public int calmEvery = 7;
    [Tooltip("Un nivel de cada tantos aprieta de golpe. Son los que se recuerdan")]
    public int surgeEvery = 5;
    [Range(0.3f, 1f)] public float calmFactor = 0.65f;
    [Range(1f, 2.5f)] public float surgeFactor = 1.45f;
    [Tooltip("Cuánto del pico afecta también a la velocidad (1 = igual que a la cantidad)")]
    [Range(0f, 1f)] public float moodAffectsSpeed = 0.5f;

    [Header("Corriente (grados de curvatura)")]
    public float flowAmplitudeBase = 45f;
    public float flowAmplitudePerTier = 6f;
    public float flowAmplitudeMax = 90f;

    public int Tier(int level) => Mathf.Max(0, (level - 1) / Mathf.Max(1, tierLength));

    /// 0.65 en un nivel de respiro, 1.45 en uno de apretón, 1 en los demás.
    /// Depende solo del número de nivel, así que es igual en todas las partidas.
    public float Mood(int level)
    {
        if (calmEvery > 0 && level % calmEvery == 0) return calmFactor;
        if (surgeEvery > 0 && level % surgeEvery == 0) return surgeFactor;
        return 1f;
    }

    public bool IsCalm(int level) => calmEvery > 0 && level % calmEvery == 0;
    public bool IsSurge(int level) => !IsCalm(level) && surgeEvery > 0 && level % surgeEvery == 0;

    public float Budget(int level) =>
        Mathf.Min(maxBudget,
            (baseBudget + (level - 1) * budgetPerLevel + Tier(level) * budgetPerTier) * Mood(level));

    public float SpeedMultiplier(int level)
    {
        float baseMul = 1f + (level - 1) * speedPerLevel + Tier(level) * speedPerTier;
        float mood = Mathf.Lerp(1f, Mood(level), Mathf.Clamp01(moodAffectsSpeed));
        return Mathf.Min(maxSpeedMultiplier, baseMul * mood);
    }

    public float FlowAmplitude(int level) =>
        Mathf.Min(flowAmplitudeMax, flowAmplitudeBase + Tier(level) * flowAmplitudePerTier);
}
