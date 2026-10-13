using System.Collections.Generic;
using UnityEngine;

// Se ejecuta después de GameBounds y de PlayerStart
[DefaultExecutionOrder(10)]
public class LevelPopulator : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private AsteroidPool pool;
    [SerializeField] private Transform player;
    [SerializeField] private DifficultyProfile difficulty;
    [SerializeField] private ColossusDirector colossus;     // opcional
    [SerializeField] private AsteroidTypeData[] types;

    [Header("Nivel (para probar)")]
    [SerializeField] private int level = 1;
    [SerializeField] private bool randomSeed = true;
    [SerializeField] private int seed = 12345;

    [Header("Colocación")]
    [SerializeField] private float playerSafeRadius = 2.5f;
    [SerializeField] private float staticEdgeMargin = 0.8f;
    [SerializeField] private float blockedMargin = 0.6f;
    [SerializeField] private float goalMargin = 1.5f;

    readonly List<AsteroidTypeData> unlocked = new List<AsteroidTypeData>();
    int currentLevel;

    void Start() { StartLevel(level, randomSeed ? Random.Range(0, int.MaxValue) : seed); }

    void OnDestroy() { if (pool != null) pool.OnExited -= HandleExit; }

    [ContextMenu("Reiniciar nivel")]
    void RestartFromMenu() { StartLevel(level, randomSeed ? Random.Range(0, int.MaxValue) : seed); }

    /// Con la misma semilla, el reparto inicial es el mismo.
    public void StartLevel(int newLevel, int newSeed)
    {
        var bounds = GameBounds.Instance;
        if (bounds == null || !bounds.IsReady) { Debug.LogWarning("LevelPopulator: GameBounds no está listo."); return; }
        if (pool == null || difficulty == null) { Debug.LogError("LevelPopulator: falta el Pool o el Difficulty Profile."); return; }

        currentLevel = Mathf.Max(1, newLevel);
        Random.InitState(newSeed);

        pool.OnExited -= HandleExit;
        pool.OnExited += HandleExit;
        pool.ReleaseAll();
        if (colossus != null) colossus.BeginLevel(currentLevel);

        Asteroid.SpeedMultiplier = difficulty.SpeedMultiplier(currentLevel);
        if (FlowField.Instance != null)
            FlowField.Instance.SetAmplitude(difficulty.FlowAmplitude(currentLevel));

        // Tipos disponibles en este nivel (los colosos los gestiona su director)
        unlocked.Clear();
        foreach (var t in types)
        {
            if (t == null || t.behavior == AsteroidBehavior.Colossus || t.behavior == AsteroidBehavior.Debris) continue;
            if (t.sprites == null || t.sprites.Length == 0 || t.minLevel > currentLevel) continue;
            unlocked.Add(t);
        }
        if (unlocked.Count == 0) { Debug.LogWarning("LevelPopulator: no hay tipos disponibles para este nivel."); return; }

        // Compra asteroides con el presupuesto del nivel
        float budget = difficulty.Budget(currentLevel);
        var shopping = new List<AsteroidTypeData>();
        int moving = 0;
        for (int guard = 0; guard < 500; guard++)
        {
            var t = PickWeighted(unlocked, budget, moving >= difficulty.maxMovingAsteroids);
            if (t == null) break;
            shopping.Add(t);
            budget -= Cost(t);
            if (t.behavior != AsteroidBehavior.Static) moving++;
        }

        // Colócalos
        Rect area = bounds.Area;
        Rect staticRect = Shrink(area, staticEdgeMargin);
        Rect movingRect = FlowField.Instance != null ? FlowField.Instance.SpawnRect : area;
        Rect goal = Expand(bounds.GoalCorner, goalMargin);
        Vector2 playerPos = player != null ? (Vector2)player.position : area.center;
        var placed = new List<(Vector2 pos, float r)>();
        int count = 0;

        foreach (var t in shopping)
        {
            bool isStatic = t.behavior == AsteroidBehavior.Static;
            float size = t.RandomSize();
            float approxR = size * 0.7f;
            Rect rect = isStatic ? staticRect : movingRect;

            for (int attempt = 0; attempt < 40; attempt++)
            {
                var p = new Vector2(Random.Range(rect.xMin, rect.xMax), Random.Range(rect.yMin, rect.yMax));

                if (Vector2.Distance(p, playerPos) < playerSafeRadius + approxR) continue;
                if (isStatic && (goal.Contains(p) || bounds.IsBlocked(p, blockedMargin + approxR))) continue;
                if (Overlaps(placed, p, approxR)) continue;

                pool.Spawn(t, p, size);
                placed.Add((p, approxR));
                count++;
                break;
            }
        }

        Debug.Log($"LevelPopulator: nivel {currentLevel} (etapa {difficulty.Tier(currentLevel)}), semilla {newSeed}: " +
                  $"{count}/{shopping.Count} asteroides, presupuesto {difficulty.Budget(currentLevel):0.#}, " +
                  $"velocidad x{Asteroid.SpeedMultiplier:0.00}");
    }

    // Un asteroide salió del área: entra otro por el lado de entrada
    void HandleExit(Asteroid a)
    {
        bool wasColossus = a.Type.behavior == AsteroidBehavior.Colossus;
        var oldType = a.Type;
        pool.Release(a);

        if (wasColossus && colossus != null) colossus.NotifyGone();
        if (FlowField.Instance == null) return;

        // ¿Toca un coloso en lugar de un asteroide normal?
        if (!wasColossus && colossus != null && colossus.ShouldSpawn(currentLevel))
        {
            colossus.Spawn(currentLevel);
            return;
        }

        // Un coloso que sale se sustituye por uno normal; los demás, por otro del mismo comportamiento
        var type = wasColossus ? PickAnyMoving() : PickReplacement(oldType);
        if (type == null) return;

        Vector2 avoid = player != null ? (Vector2)player.position : Vector2.zero;
        Vector2 p = FlowField.Instance.RandomEntryPoint(avoid, playerSafeRadius);
        pool.Spawn(type, p);
    }

    AsteroidTypeData PickReplacement(AsteroidTypeData old)
    {
        float total = 0f;
        foreach (var t in unlocked)
            if (t.behavior == old.behavior) total += Mathf.Max(0f, t.spawnWeight);
        if (total <= 0f) return old;

        float pick = Random.value * total;
        foreach (var t in unlocked)
        {
            float w = Mathf.Max(0f, t.spawnWeight);
            if (t.behavior != old.behavior || w <= 0f) continue;
            pick -= w;
            if (pick <= 0f) return t;
        }
        return old;
    }

    AsteroidTypeData PickAnyMoving()
    {
        float total = 0f;
        foreach (var t in unlocked)
            if (t.behavior != AsteroidBehavior.Static) total += Mathf.Max(0f, t.spawnWeight);
        if (total <= 0f) return null;

        float pick = Random.value * total;
        foreach (var t in unlocked)
        {
            float w = Mathf.Max(0f, t.spawnWeight);
            if (t.behavior == AsteroidBehavior.Static || w <= 0f) continue;
            pick -= w;
            if (pick <= 0f) return t;
        }
        return null;
    }

    static float Cost(AsteroidTypeData t) => Mathf.Max(0.5f, t.cost);

    static AsteroidTypeData PickWeighted(List<AsteroidTypeData> list, float maxCost, bool onlyStatic)
    {
        float total = 0f;
        foreach (var t in list)
            if (Cost(t) <= maxCost && (!onlyStatic || t.behavior == AsteroidBehavior.Static))
                total += Mathf.Max(0f, t.spawnWeight);
        if (total <= 0f) return null;

        float pick = Random.value * total;
        foreach (var t in list)
        {
            float w = Mathf.Max(0f, t.spawnWeight);
            if (Cost(t) > maxCost || (onlyStatic && t.behavior != AsteroidBehavior.Static) || w <= 0f) continue;
            pick -= w;
            if (pick <= 0f) return t;
        }
        return null;
    }

    static bool Overlaps(List<(Vector2 pos, float r)> placed, Vector2 p, float r)
    {
        foreach (var o in placed)
            if (Vector2.Distance(p, o.pos) < r + o.r + 0.1f) return true;
        return false;
    }

    static Rect Shrink(Rect r, float m) =>
        new Rect(r.xMin + m, r.yMin + m, r.width - 2f * m, r.height - 2f * m);

    static Rect Expand(Rect r, float m) =>
        new Rect(r.xMin - m, r.yMin - m, r.width + 2f * m, r.height + 2f * m);
}