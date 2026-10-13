/*using System.Collections.Generic;
using UnityEngine;

// Se ejecuta después de GameBounds y de PlayerStart
[DefaultExecutionOrder(10)]
public class AsteroidTestSpawner : MonoBehaviour
{
    [System.Serializable]
    public class Entry
    {
        public AsteroidTypeData type;
        public int count = 3;
    }

    [SerializeField] private AsteroidPool pool;
    [SerializeField] private Transform player;
    [Tooltip("Radio libre de asteroides alrededor del jugador")]
    [SerializeField] private float safeRadius = 2.5f;
    [Tooltip("Margen respecto al borde para los estáticos (siempre dentro de la pantalla)")]
    [SerializeField] private float staticEdgeMargin = 0.8f;
    [SerializeField] private Entry[] entries;

    void Start() { Spawn(); }

    [ContextMenu("Respawn")]
    public void Spawn()
    {
        var bounds = GameBounds.Instance;
        if (bounds == null || !bounds.IsReady)
        {
            Debug.LogWarning("AsteroidTestSpawner: GameBounds no está listo.");
            return;
        }

        pool.ReleaseAll();
        Vector2 playerPos = player != null ? (Vector2)player.position : bounds.Area.center;
        var placed = new List<(Vector2 pos, float r)>();

        foreach (var e in entries)
        {
            if (e.type == null || e.type.sprites == null || e.type.sprites.Length == 0)
            {
                Debug.LogWarning("AsteroidTestSpawner: un tipo está vacío o no tiene sprites.");
                continue;
            }

            bool isStatic = e.type.behavior == AsteroidBehavior.Static;

            // Los estáticos solo dentro de la pantalla; los demás, repartidos por todo el cinturón
            Rect rect = isStatic || FlowField.Instance == null
                ? Shrink(bounds.Area, staticEdgeMargin)
                : FlowField.Instance.SpawnRect;

            for (int i = 0; i < e.count; i++)
            {
                float size = Random.Range(e.type.sizeRange.x, e.type.sizeRange.y);
                float approxR = size * 0.7f;

                for (int attempt = 0; attempt < 30; attempt++)
                {
                    var p = new Vector2(Random.Range(rect.xMin, rect.xMax),
                                        Random.Range(rect.yMin, rect.yMax));

                    if (Vector2.Distance(p, playerPos) < safeRadius + approxR) continue;

                    bool free = true;
                    foreach (var o in placed)
                        if (Vector2.Distance(p, o.pos) < approxR + o.r + 0.1f) { free = false; break; }
                    if (!free) continue;

                    pool.Spawn(e.type, p, size);
                    placed.Add((p, approxR));
                    break;
                }
            }
        }

        Debug.Log($"AsteroidTestSpawner: {pool.Active.Count} asteroides generados.");
    }

    static Rect Shrink(Rect r, float m) =>
        new Rect(r.xMin + m, r.yMin + m, r.width - 2f * m, r.height - 2f * m);
}*/