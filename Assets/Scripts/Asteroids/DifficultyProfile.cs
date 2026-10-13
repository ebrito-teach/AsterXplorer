using UnityEngine;

[CreateAssetMenu(fileName = "DifficultyProfile", menuName = "Game/Difficulty Profile")]
public class DifficultyProfile : ScriptableObject
{
    [Header("Etapas")]
    [Tooltip("Cada cuántos niveles hay un salto de etapa")]
    public int tierLength = 30;

    [Header("Cantidad (presupuesto de puntos)")]
    public float baseBudget = 26f;
    [Tooltip("Aumento pequeño en cada nivel")]
    public float budgetPerLevel = 0.9f;
    [Tooltip("Aumento extra al entrar en cada etapa")]
    public float budgetPerTier = 8f;
    public float maxBudget = 160f;
    [Tooltip("Tope de asteroides en movimiento a la vez (rendimiento en móvil)")]
    public int maxMovingAsteroids = 45;

    [Header("Velocidad")]
    public float speedPerLevel = 0.006f;
    public float speedPerTier = 0.06f;
    public float maxSpeedMultiplier = 1.8f;

    [Header("Corriente (grados de curvatura)")]
    public float flowAmplitudeBase = 45f;
    public float flowAmplitudePerTier = 6f;
    public float flowAmplitudeMax = 90f;

    public int Tier(int level) => Mathf.Max(0, (level - 1) / Mathf.Max(1, tierLength));

    public float Budget(int level) =>
        Mathf.Min(maxBudget, baseBudget + (level - 1) * budgetPerLevel + Tier(level) * budgetPerTier);

    public float SpeedMultiplier(int level) =>
        Mathf.Min(maxSpeedMultiplier, 1f + (level - 1) * speedPerLevel + Tier(level) * speedPerTier);

    public float FlowAmplitude(int level) =>
        Mathf.Min(flowAmplitudeMax, flowAmplitudeBase + Tier(level) * flowAmplitudePerTier);
}