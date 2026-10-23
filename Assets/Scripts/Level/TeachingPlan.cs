using UnityEngine;

/// Los 20 primeros niveles enseñan: cada mecánica aparece por primera vez en un nivel fijo,
/// sola, y después se combina. Hasta el nivel Specials Random From los mapas especiales (mundo
/// oscuro y grieta) solo salen en su nivel garantizado; los planetas, al azar desde Planets
/// Random From.
///
///   1   rocas lentas y estáticos             10  primer mundo oscuro (respiro)
///   2   rocas rápidas                        13  primera grieta: disparos, onda, cargas y QTE
///   3   rompibles · buff LightShield         16  grieta completa: además roce y empuje
///   4   primer cometa · buff Slow            21  desde aquí todo vuelve al azar
///   6   planeta con lunas (y su gravedad)
///   7   planeta con anillo · buff Ghost
///   8   coloso · planeta con cinturón
///   9   buffs XLR8D y TimeFreeze
///
/// Todo se puede mover en el Inspector. Desmarca Enabled para jugar sin enseñanza (todo al azar
/// desde el nivel 1, como antes). Va en un objeto vacío de la escena.
public class TeachingPlan : MonoBehaviour
{
    public static TeachingPlan Instance { get; private set; }

    [SerializeField] private bool enabledPlan = true;

    [Header("Rocas")]
    [SerializeField] private int fastFrom = 2;
    [SerializeField] private int breakableFrom = 3;
    [SerializeField] private int cometFrom = 4;
    [Tooltip("El primer coloso entra seguro en este nivel (a los pocos segundos de empezar)")]
    [SerializeField] private int colossusAt = 8;
    [SerializeField] private Vector2 colossusDelay = new Vector2(8f, 14f);

    [Header("Planetas")]
    [SerializeField] private int planetMoonsAt = 6;
    [SerializeField] private int planetRingAt = 7;
    [SerializeField] private int planetBeltAt = 8;
    [SerializeField] private int planetsRandomFrom = 9;

    [Header("Mapas especiales")]
    [SerializeField] private int darkAt = 10;
    [Tooltip("Primera grieta: disparos, onda, cargas y QTE (sin roce ni empuje)")]
    [SerializeField] private int fractureIntroAt = 13;
    [Tooltip("Grieta completa: además el roce y el empuje")]
    [SerializeField] private int fractureFullAt = 16;
    [Tooltip("Desde aquí el mundo oscuro y la grieta vuelven a salir al azar")]
    [SerializeField] private int specialsRandomFrom = 21;

    [Header("Buffs (primera aparición garantizada)")]
    [SerializeField] private int lightShieldAt = 3;
    [SerializeField] private int slowAt = 4;
    [SerializeField] private int ghostAt = 7;
    [SerializeField] private int xlr8dAt = 9;
    [SerializeField] private int timeFreezeAt = 9;

    void Awake() { Instance = this; }

    static bool On => Instance != null && Instance.enabledPlan;

    // ------------------------------------------------------------------ rocas
    /// ¿Puede salir este tipo de roca en este nivel?
    public static bool TypeAllowed(AsteroidTypeData t, int level)
    {
        if (!On || t == null) return true;
        var p = Instance;
        if (t.comet != null) return level >= p.cometFrom;
        switch (t.behavior)
        {
            case AsteroidBehavior.Fast: return level >= p.fastFrom;
            case AsteroidBehavior.Breakable: return level >= p.breakableFrom;
            case AsteroidBehavior.Colossus: return level >= p.colossusAt;
        }
        return true;
    }

    /// Nivel en el que el coloso entra seguro (-1 = ninguno) y cuándo
    public static int ColossusForcedLevel => On ? Instance.colossusAt : -1;
    public static float ColossusForcedDelay => On ? Random.Range(Instance.colossusDelay.x, Instance.colossusDelay.y) : 10f;

    // ------------------------------------------------------------------ planetas
    /// -1 = sin planetas en este nivel; 0 = al azar como siempre; 1 lunas, 2 anillo, 3 cinturón
    public static int PlanetRule(int level)
    {
        if (!On) return 0;
        var p = Instance;
        if (level == p.planetMoonsAt) return 1;
        if (level == p.planetRingAt) return 2;
        if (level == p.planetBeltAt) return 3;
        if (level < p.planetsRandomFrom) return -1;
        return 0;
    }

    // ------------------------------------------------------------------ mapas especiales
    /// true = este nivel lo es seguro; false = seguro que no; null = al azar
    public static bool? Dark(int level)
    {
        if (!On) return null;
        var p = Instance;
        if (level == p.darkAt) return true;
        if (level < p.specialsRandomFrom) return false;
        return null;
    }

    public static bool? Fracture(int level)
    {
        if (!On) return null;
        var p = Instance;
        if (level == p.fractureIntroAt || level == p.fractureFullAt) return true;
        if (level < p.specialsRandomFrom) return false;
        return null;
    }

    public static bool QteAllowed(int level) => !On || level >= Instance.fractureIntroAt;
    public static bool GrazeAllowed(int level) => !On || level >= Instance.fractureFullAt;
    public static bool PushAllowed(int level) => !On || level >= Instance.fractureFullAt;

    // ------------------------------------------------------------------ buffs
    public static bool BuffAllowed(BuffType t, int level) => !On || level >= Instance.BuffLevel(t);

    int BuffLevel(BuffType t)
    {
        switch (t)
        {
            case BuffType.LightShield: return lightShieldAt;
            case BuffType.Slow: return slowAt;
            case BuffType.Ghost: return ghostAt;
            case BuffType.XLR8D: return xlr8dAt;
            case BuffType.TimeFreeze: return timeFreezeAt;
        }
        return 1;
    }

    /// Buffs que se enseñan en este nivel (salen seguro). Vacío si ninguno
    public static System.Collections.Generic.List<BuffType> BuffsTaughtAt(int level)
    {
        var list = new System.Collections.Generic.List<BuffType>();
        if (!On) return list;
        foreach (BuffType t in System.Enum.GetValues(typeof(BuffType)))
            if (Instance.BuffLevel(t) == level) list.Add(t);
        return list;
    }

    /// Último nivel de la enseñanza
    public static int TeachingEnds => On ? Instance.specialsRandomFrom - 1 : 0;
}
