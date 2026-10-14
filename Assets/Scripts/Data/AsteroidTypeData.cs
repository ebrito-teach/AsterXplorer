using System.Collections.Generic;
using UnityEngine;

public enum AsteroidBehavior { Static, Slow, Fast, Breakable, Colossus, Debris }

[CreateAssetMenu(fileName = "Asteroid_", menuName = "Game/Asteroid Type")]
public class AsteroidTypeData : ScriptableObject
{
    [System.Serializable]
    public class SpriteGroup { public Sprite[] sprites; }

    [Header("Identidad")]
    public string id = "asteroid";
    public AsteroidBehavior behavior = AsteroidBehavior.Slow;

    [Header("Apariencia")]
    public Sprite[] sprites;
    [Tooltip("Escala mínima y máxima. ESCALA DE MUNDO: 1 unidad = 50 metros, así que " +
             "0.6 es una roca de 30 m (≈35.000 t) y 6 es una montaña de 300 m (≈35 millones de t)")]
    public Vector2 sizeRange = new Vector2(0.4f, 1.0f);
    [Tooltip("Más de 1 = más asteroides pequeños que grandes")]
    public float sizeSkew = 1.4f;
    [Tooltip("Radio mínimo en unidades. Evita trozos microscópicos que matan sin verse")]
    public float minWorldRadius = 0.13f;
    [Tooltip("Prefab propio (por ejemplo, el cometa con estela). Vacío = el del AsteroidPool")]
    public Asteroid prefabOverride;

    [Header("Movimiento")]
    [Tooltip("Velocidad mínima y máxima (unidades/s). La pantalla mide 19.4 unidades de ancho: " +
             "a 2.5 u/s se tarda 8 s en cruzarla (lento), a 6 u/s poco más de 3 s (bullet hell)")]
    public Vector2 speedRange = new Vector2(1.3f, 2.5f);
    [Tooltip("Giro mínimo y máximo (grados/s). Por debajo de ±100 un asteroide parece una foto")]
    public Vector2 spinRange = new Vector2(-45f, 45f);
    [Tooltip("Los pequeños van más rápido, giran más y siguen mejor la corriente")]
    public bool sizeAffectsSpeed = true;
    [Tooltip("Cuánto del aumento de velocidad por nivel se aplica (1 = todo, 0 = nada)")]
    [Range(0f, 1f)] public float speedScaling = 1f;

    [Header("Corriente")]
    [Range(0f, 1f)] public float followFlow = 0.85f;
    [Tooltip("Desviación propia respecto a la corriente (grados)")]
    public float flowSpread = 25f;
    [Tooltip("Grados/s con los que gira hacia la corriente. Más alto = arcos más cerrados")]
    public float turnRate = 40f;
    [Tooltip("Perturbaciones suaves de la trayectoria")]
    public float wander = 0.08f;
    [Tooltip("Tras un choque, la velocidad vuelve poco a poco a su valor")]
    public bool restoreSpeed = true;
    [Tooltip("Unidades/s² con las que recupera su velocidad. Bajo = el cinturón se va apagando")]
    public float restoreRate = 0.9f;
    [Tooltip("Al salir del área es sustituido por otro asteroide que entra por el lado contrario")]
    public bool recycle = true;

    [Header("Masa (esto es lo que hace que un golpe sea catastrófico)")]
    public PhysicsMaterial2D material;
    [Tooltip("Masa = tamaño^exponente × densidad × este valor. Un cuerpo real es un volumen, " +
             "así que el exponente CORRECTO es 3: al doble de tamaño, 8 veces la masa. " +
             "Con 2.5 el coloso pesaba 80 veces un guijarro; con 3 y el tamaño adecuado, 1000")]
    public float massPerArea = 1f;
    [Tooltip("3 = física real de un cuerpo macizo. No lo bajes: es de donde sale el peso")]
    public float massExponent = 3f;
    [Tooltip("Densidad de cada asteroide, al azar en este rango (roca ≈ 1, hielo ≈ 0.5, metálico ≈ 2)")]
    public Vector2 densityRange = new Vector2(0.8f, 1.3f);
    [Range(0.3f, 1.2f)] public float colliderFactor = 0.9f;

    [Header("Impacto")]
    [Tooltip("Exageración del empujón que recibo. 1 = física pura (la transferencia ya la " +
             "calcula el motor a partir de las masas). 1.2 - 1.4 para darle fuerza al juego")]
    public float launchBoost = 1.25f;
    [Tooltip("Tope tras el empujón, en múltiplos de mi velocidad propia. 0 = sin tope (recomendado: " +
             "si lo limitas, el golpe del coloso deja de leerse como catastrófico)")]
    public float impactSpeedCap = 0f;
    [Tooltip("Tras salir disparado, esta es mi nueva velocidad de crucero en vez de frenar. " +
             "1 = conserva todo el impulso, 0 = vuelve enseguida a su velocidad de antes")]
    [Range(0f, 1f)] public float keepsImpulse = 0.85f;
    [Tooltip("Cuánto se elimina del RETROCESO al estrellarme contra algo más pesado. " +
             "1 = no rebota nada hacia atrás, solo resbala de lado. Esto mata el efecto goma")]
    [Range(0f, 1f)] public float reboundDamping = 0.85f;
    [Tooltip("Energía extra que se pierde en ese choque")]
    [Range(0f, 1f)] public float impactEnergyLoss = 0.2f;
    [Tooltip("Giro que pierde al chocar")]
    [Range(0f, 1f)] public float impactSpinLoss = 0.3f;
    [Tooltip("Nunca queda por debajo de esta fracción de su velocidad. Impide que el cinturón se apague")]
    [Range(0f, 1f)] public float impactSpeedFloor = 0.55f;
    [Tooltip("Segundos tras un golpe en los que NO recupera su velocidad")]
    public float noRestoreSeconds = 0.5f;

    [Header("Interacción con otros asteroides")]
    [Tooltip("Un estático con esto activo se suelta si lo golpea algo mucho más pesado")]
    public bool canBeDislodged = false;
    [Tooltip("Masa mínima de quien golpea, en múltiplos de la masa de este")]
    public float dislodgeMassRatio = 4f;
    [Tooltip("Este asteroide desplaza a los estáticos a su paso (colosos y cometas)")]
    public bool dislodgesStatics = false;

    [Header("Órbitas — yo puedo quedar atrapado")]
    [Tooltip("Este asteroide puede quedar capturado en la órbita de un cuerpo mayor")]
    public bool canBeCaptured = false;
    [Tooltip("Probabilidad base. Se tira UNA vez por vida, en el primer encuentro válido, " +
             "y se multiplica por el Moon Capture Multiplier del cuerpo que lo atrapa")]
    [Range(0f, 1f)] public float captureChance = 0.12f;
    [Tooltip("Radio máximo en unidades para poder ser capturado. Solo lo pequeño")]
    public float captureMaxRadius = 0.32f;
    [Tooltip("Velocidad de choque mínima. Un roce de nada no cuenta como encuentro")]
    public float captureMinSpeed = 0.6f;
    [Tooltip("Velocidad de choque máxima. Si llega disparado, rebota en vez de quedar atrapado")]
    public float captureMaxSpeed = 3f;

    [Header("Órbitas — yo capturo (estáticos, colosos, cometas)")]
    [Tooltip("Los pequeños que me golpeen pueden quedar orbitándome")]
    public bool acceptsMoons = false;
    [Tooltip("Cuerpos en órbita como máximo")]
    public int maxMoons = 3;
    [Tooltip("Multiplica la probabilidad del pequeño. 1 en un estático, 4 - 6 en un coloso " +
             "o un cometa, que van barriendo el cinturón a su paso")]
    public float moonCaptureMultiplier = 1f;
    [Tooltip("Hueco entre mi superficie y la suya: ORBITAN, no se pegan")]
    public float moonGap = 0.3f;
    [Tooltip("Velocidad angular de la órbita (grados/s)")]
    public Vector2 moonOrbitSpeed = new Vector2(30f, 75f);
    [Tooltip("Cuánto se estira la órbita hacia atrás cuando me muevo. 0 = anillo, " +
             "0.6 = cortejo arrastrado detrás, como la cola de un cometa")]
    [Range(0f, 1f)] public float moonTrail = 0f;
    [Tooltip("Mis lunas matan al jugador. Falso en un estático (campo seguro), " +
             "verdadero en un coloso o un cometa (enjambre mortal que viaja con él)")]
    public bool moonsAreLethal = false;

    [Header("Campo magnético (colosos y cometas)")]
    [Tooltip("Radio en unidades dentro del cual atrae a los asteroides pequeños. 0 = no atrae")]
    public float pullRadius = 0f;
    [Tooltip("Aceleración que les imprime junto al propio cuerpo (unidades/s²)")]
    public float pullForce = 9f;
    [Tooltip("Radio máximo del asteroide al que puede arrastrar")]
    public float pullMaxTargetRadius = 0.45f;
    [Tooltip("Masa máxima del arrastrado, en fracción de la masa propia")]
    [Range(0f, 1f)] public float pullMaxTargetMassFraction = 0.08f;

    [Header("Reglas")]
    public bool killsPlayer = true;
    public bool canBreak = false;

    [Header("Rotura (solo tipos que se rompen)")]
    [Tooltip("Tipo Debris de los fragmentos")]
    public AsteroidTypeData fragmentType;
    [Tooltip("Un grupo de fragmentos por cada sprite del asteroide, en el mismo orden")]
    public SpriteGroup[] fragmentGroups;
    public Vector2Int fragmentCount = new Vector2Int(3, 5);
    [Tooltip("Los fragmentos salen más rápidos que el asteroide original")]
    public Vector2 fragmentBoost = new Vector2(1.3f, 1.6f);
    [Tooltip("Velocidad de choque mínima para que se rompa")]
    public float breakMinSpeed = 0.5f;
    [Tooltip("Segundos tras nacer en los que no se rompe")]
    public float breakShield = 0.4f;
    public int maxFragmentsAlive = 40;
    [Tooltip("Solo se rompe a la vista del jugador. Si no, el nivel se deshace fuera de pantalla")]
    public bool breakOnlyOnScreen = true;
    [Tooltip("Escribe en la consola POR QUÉ no se rompió. Déjalo activo mientras lo ajustas")]
    public bool logBreakRejections = false;

    [Header("Vida")]
    [Tooltip("Segundos hasta que desaparece (se desvanece al final). 0 = sin límite")]
    public float lifetime = 0f;

    [Header("Cometa (opcional)")]
    public CometProfile comet;

    [Header("Generación")]
    [Tooltip("Puntos que cuesta en el presupuesto del nivel")]
    public int cost = 1;
    public int minLevel = 1;
    [Tooltip("Probabilidad relativa de que el generador lo elija")]
    public float spawnWeight = 1f;

    [System.NonSerialized] List<int> bag;
    [System.NonSerialized] int lastIndex = -1;

    void OnEnable() { bag = null; lastIndex = -1; }

    /// Siguiente sprite: se usan todos los del tipo antes de repetir ninguno.
    public Sprite NextSprite()
    {
        if (sprites == null || sprites.Length == 0) return null;
        if (sprites.Length == 1) return sprites[0];
        if (bag == null || bag.Count == 0) RefillBag();

        int idx = bag[bag.Count - 1];
        bag.RemoveAt(bag.Count - 1);
        lastIndex = idx;
        return sprites[idx];
    }

    void RefillBag()
    {
        bag = new List<int>(sprites.Length);
        for (int i = 0; i < sprites.Length; i++) bag.Add(i);

        for (int i = bag.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (bag[i], bag[j]) = (bag[j], bag[i]);
        }

        // Evita repetir el último sprite al empezar una bolsa nueva
        if (bag[bag.Count - 1] == lastIndex)
            (bag[bag.Count - 1], bag[0]) = (bag[0], bag[bag.Count - 1]);
    }

    public float RandomSize() =>
        Mathf.Lerp(sizeRange.x, sizeRange.y, Mathf.Pow(Random.value, Mathf.Max(0.1f, sizeSkew)));

    /// Fragmentos que corresponden al sprite que lleva puesto el asteroide.
    public Sprite[] FragmentsFor(int variant)
    {
        if (fragmentGroups == null || fragmentGroups.Length == 0) return null;
        var g = fragmentGroups[Mathf.Clamp(variant, 0, fragmentGroups.Length - 1)];
        return (g != null && g.sprites != null && g.sprites.Length > 0) ? g.sprites : null;
    }
}
