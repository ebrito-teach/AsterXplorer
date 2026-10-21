using UnityEngine;

/// Un buff suelto por el mapa. Se recoge solo tocándolo con el collider del personaje.
///
/// Lo crea el BuffSpawner (no hace falta prefab): el icono, su aura latiendo detrás y una
/// zona de contacto. Puede estar quieto, ir a la deriva (si salió entre los fragmentos de una
/// roca o un cometa) u orbitar a un coloso (si lo trae él). Si el jugador ya tiene los dos
/// contenedores en uso, no se recoge y sigue ahí.
public class BuffPickup : MonoBehaviour
{
    public BuffType Type { get; private set; }

    SpriteRenderer icon, aura;
    Vector2 velocity;
    float drag;
    Asteroid host;
    int hostSerial;
    float orbitRadius, orbitAngle, orbitSpeed;
    float born;
    bool taken;

    public static BuffPickup Create(BuffType type, Vector2 position, float worldSize, string layer)
    {
        var bs = BuffSystem.Instance;
        var info = bs != null ? bs.Info(type) : null;

        var go = new GameObject("Buff_" + type);
        int l = LayerMask.NameToLayer(layer);
        if (l >= 0) go.layer = l;
        go.transform.position = position;
        var p = go.AddComponent<BuffPickup>();
        p.Type = type;
        p.born = Time.time;

        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        var col = go.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = worldSize * 0.5f;

        if (info != null && info.aura != null)
        {
            var a = new GameObject("Aura");
            a.transform.SetParent(go.transform, false);
            p.aura = a.AddComponent<SpriteRenderer>();
            p.aura.sprite = info.aura;
            p.aura.sortingLayerName = "Player";
            p.aura.sortingOrder = -6;
            float s = worldSize * 1.6f / Mathf.Max(0.01f, info.aura.bounds.size.y);
            a.transform.localScale = Vector3.one * s;
        }

        var ic = new GameObject("Icono");
        ic.transform.SetParent(go.transform, false);
        p.icon = ic.AddComponent<SpriteRenderer>();
        p.icon.sortingLayerName = "Player";
        p.icon.sortingOrder = -5;
        if (info != null && info.icon != null)
        {
            p.icon.sprite = info.icon;
            ic.transform.localScale = Vector3.one * (worldSize / Mathf.Max(0.01f, info.icon.bounds.size.y));
        }
        return p;
    }

    /// Sale despedido entre los fragmentos y se va frenando
    public void Drift(Vector2 v, float dragPerSecond)
    {
        velocity = v;
        drag = dragPerSecond;
    }

    /// Lo lleva un coloso dando vueltas a su alrededor. Si el coloso se va, se va con él
    public void Orbit(Asteroid colossus, float radius, float degPerSecond)
    {
        host = colossus;
        hostSerial = colossus.Serial;
        orbitRadius = radius;
        orbitSpeed = degPerSecond;
        orbitAngle = Random.Range(0f, 360f);
    }

    void Update()
    {
        float t = Time.time - born;
        if (aura != null)
        {
            aura.transform.localRotation = Quaternion.Euler(0f, 0f, t * 25f);
            float k = 0.6f + 0.25f * Mathf.Sin(t * 4f);
            aura.color = new Color(1f, 1f, 1f, k);
        }
        if (icon != null)
            icon.transform.localPosition = new Vector3(0f, Mathf.Sin(t * 2.2f) * 0.08f, 0f);

        if (Asteroid.WorldFrozen) return;   // TimeFreeze: también se queda quieto

        if (host != null)
        {
            if (!host.isActiveAndEnabled || host.Serial != hostSerial) { Destroy(gameObject); return; }
            orbitAngle += orbitSpeed * Time.deltaTime;
            float r = orbitAngle * Mathf.Deg2Rad;
            transform.position = host.Body.position + new Vector2(Mathf.Cos(r), Mathf.Sin(r)) * orbitRadius;
            return;
        }

        if (velocity.sqrMagnitude > 0.0001f)
        {
            transform.position += (Vector3)(velocity * Time.deltaTime);
            velocity *= Mathf.Exp(-drag * Time.deltaTime);
            var gb = GameBounds.Instance;
            if (gb != null && gb.IsReady)
            {
                Rect a = gb.Area;
                Vector3 p = transform.position;
                p.x = Mathf.Clamp(p.x, a.xMin + 0.5f, a.xMax - 0.5f);
                p.y = Mathf.Clamp(p.y, a.yMin + 0.5f, a.yMax - 0.5f);
                transform.position = p;
            }
        }
    }

    void OnTriggerEnter2D(Collider2D other) => TryTake(other);
    void OnTriggerStay2D(Collider2D other) => TryTake(other);

    void TryTake(Collider2D other)
    {
        if (taken) return;
        var pc = other.GetComponentInParent<PlayerController>();
        if (pc == null || pc.IsDead || pc.IsExpelling || pc.IsAbsorbed) return;
        var bs = BuffSystem.Instance;
        if (bs == null || !bs.TryAdd(Type)) return;
        taken = true;
        Destroy(gameObject);
    }
}
