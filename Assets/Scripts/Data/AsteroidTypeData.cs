using System.Collections.Generic;
using UnityEngine;

public enum AsteroidBehavior { Static, Slow, Fast, Breakable, Colossus, Debris }

[CreateAssetMenu(fileName = "Asteroid_", menuName = "Game/Asteroid Type")]
public class AsteroidTypeData : ScriptableObject
{
    [Header("Identidad")]
    public string id = "asteroid";
    public AsteroidBehavior behavior = AsteroidBehavior.Slow;

    [Header("Apariencia")]
    public Sprite[] sprites;
    [Tooltip("Escala mínima y máxima del asteroide")]
    public Vector2 sizeRange = new Vector2(0.4f, 1.0f);
    [Tooltip("Más de 1 = más asteroides pequeños que grandes")]
    public float sizeSkew = 1.4f;
    [Tooltip("Prefab propio (por ejemplo, el cometa con estela). Vacío = el del AsteroidPool")]
    public Asteroid prefabOverride;

    [Header("Movimiento")]
    [Tooltip("Velocidad mínima y máxima (unidades/s). 0 para los estáticos")]
    public Vector2 speedRange = new Vector2(1.3f, 2.5f);
    [Tooltip("Giro mínimo y máximo (grados/s). Puede ser negativo")]
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
    public float restoreRate = 0.2f;
    [Tooltip("Al salir del área es sustituido por otro asteroide que entra por el lado contrario")]
    public bool recycle = true;

    [Header("Física y peso")]
    public PhysicsMaterial2D material;
    [Tooltip("Masa = tamaño^exponente × densidad × este valor")]
    public float massPerArea = 1f;
    [Tooltip("2.5 = los grandes pesan mucho más que los pequeños")]
    public float massExponent = 2.5f;
    [Tooltip("Densidad de cada asteroide, al azar en este rango")]
    public Vector2 densityRange = new Vector2(0.8f, 1.3f);
    [Range(0.3f, 1.2f)] public float colliderFactor = 0.9f;

    [Header("Interacción con otros asteroides")]
    [Tooltip("Un estático con esto activo se suelta si lo golpea algo mucho más pesado")]
    public bool canBeDislodged = false;
    [Tooltip("Masa mínima de quien golpea, en múltiplos de la masa de este")]
    public float dislodgeMassRatio = 4f;
    [Tooltip("Este asteroide desplaza a los estáticos a su paso (colosos y cometas)")]
    public bool dislodgesStatics = false;

    [Header("Reglas")]
    public bool killsPlayer = true;
    public bool canBreak = false;     // se usa en la fase 4

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
}