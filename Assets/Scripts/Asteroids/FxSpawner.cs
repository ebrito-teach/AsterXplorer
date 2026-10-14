using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

public class FxSpawner : MonoBehaviour
{
    [Header("Rotura de asteroides")]
    [Tooltip("Una variante por color de roca, en el mismo orden que los sprites del asteroide")]
    [SerializeField] private ParticleSystem[] breakFx;

    [Header("Muerte del jugador")]
    [SerializeField] private PlayerController player;
    [SerializeField] private ParticleSystem playerDeathFx;

    [Header("General")]
    [Tooltip("Segundos que dura un efecto antes de volver al pool")]
    [SerializeField] private float fxLifetime = 3f;

    readonly Dictionary<ParticleSystem, ObjectPool<ParticleSystem>> pools =
        new Dictionary<ParticleSystem, ObjectPool<ParticleSystem>>();

    void OnEnable()
    {
        AsteroidBreaker.Broken += OnBroken;
        if (player != null) player.OnDied += OnPlayerDied;
    }

    void OnDisable()
    {
        AsteroidBreaker.Broken -= OnBroken;
        if (player != null) player.OnDied -= OnPlayerDied;
    }

    void OnBroken(Vector2 pos, float size, int variant)
    {
        if (breakFx == null || breakFx.Length == 0) return;
        var prefab = breakFx[Mathf.Clamp(variant, 0, breakFx.Length - 1)];
        Play(prefab, pos, Mathf.Max(0.6f, size));
    }

    void OnPlayerDied()
    {
        // El jugador se desactiva al morir pero su transform queda donde murió
        if (playerDeathFx != null && player != null)
            Play(playerDeathFx, player.transform.position, 1f);
    }

    ObjectPool<ParticleSystem> GetPool(ParticleSystem prefab)
    {
        if (pools.TryGetValue(prefab, out var existing)) return existing;
        var created = new ObjectPool<ParticleSystem>(
            createFunc: () => Instantiate(prefab, transform),
            actionOnGet: x => x.gameObject.SetActive(true),
            actionOnRelease: x => x.gameObject.SetActive(false),
            actionOnDestroy: x => Destroy(x.gameObject),
            collectionCheck: false, defaultCapacity: 4, maxSize: 16);
        pools[prefab] = created;
        return created;
    }

    void Play(ParticleSystem prefab, Vector2 pos, float scale)
    {
        if (prefab == null) return;
        var ps = GetPool(prefab).Get();
        ps.transform.SetPositionAndRotation(pos, Quaternion.identity);
        ps.transform.localScale = Vector3.one * scale;
        ps.Clear(true);
        ps.Play(true);
        StartCoroutine(ReleaseLater(prefab, ps));
    }

    IEnumerator ReleaseLater(ParticleSystem prefab, ParticleSystem ps)
    {
        yield return new WaitForSeconds(fxLifetime);
        if (ps != null) GetPool(prefab).Release(ps);
    }
}