using UnityEngine;

/// La economía, en un asset para ajustarla sin tocar código.
///
/// El modelo, en una frase: **los cristales se compran; la geoda es un regalo.**
///
/// Pasar un nivel NO da cristales. Eso es deliberado: en cuanto pasar un nivel paga, el nivel 1
/// — el más fácil — se convierte en el sitio donde farmear, y la tienda deja de premiar llegar
/// lejos. El nivel que alcanzas ya tiene su recompensa, que es el ranking y los personajes de
/// hito.
///
/// La geoda da entre 20 y 120 cristales, igual en el nivel 1 que en el 500, y no se agota
/// nunca. No es un pago ni una recompensa: es un regalo. El jugador que sigue jugando sigue
/// recibiéndolo.
///
/// Lo que eso significa, para que lo sepas y no para calibrarlo: unos 5 cristales por nivel,
/// así que un personaje común cada ~238 niveles. El del nivel 1000 se habrá ganado cuatro de
/// los cincuenta. La colección completa son 22.000 niveles, o sea nunca.
[CreateAssetMenu(fileName = "EconomyProfile", menuName = "Game/Economy Profile")]
public class EconomyProfile : ScriptableObject
{
    [Header("Geoda — el regalo")]
    [Tooltip("Probabilidad de que aparezca una geoda en un nivel")]
    [Range(0f, 1f)] public float geodeChance = 0.12f;
    [Tooltip("Mínimo que puede dar")]
    public int geodeMin = 20;
    [Tooltip("Máximo que puede dar")]
    public int geodeMax = 120;
    [Tooltip("Una geoda recogida no vuelve a aparecer si repites el nivel")]
    public bool geodeOncePerLevel = true;

    [Header("Precios de los personajes")]
    public int priceCommon = 1200;
    public int priceUncommon = 2400;
    public int priceRare = 4000;

    [Header("Packs de cristales (IAP)")]
    [Tooltip("Mismo precio en todos los mercados. El sobrante de cada pack es deliberado: " +
             "siempre te quedas casi en el siguiente personaje")]
    public CrystalPack[] packs = new CrystalPack[]
    {
        new CrystalPack { productId = "crystals_small",  crystals = 1500, usd = 0.99f, label = "Puñado" },
        new CrystalPack { productId = "crystals_medium", crystals = 3300, usd = 1.99f, label = "Bolsa"  },
        new CrystalPack { productId = "crystals_large",  crystals = 5400, usd = 2.99f, label = "Veta"   },
    };

    [Header("Personajes de hito — gratis, no salen en la tienda")]
    [Tooltip("Se desbloquean al alcanzar el nivel. Son los únicos que NO se compran, y son " +
             "la identidad del jugador que no paga")]
    public MilestoneReward[] milestones = new MilestoneReward[]
    {
        new MilestoneReward { level = 10,  characterId = "hito_01" },
        new MilestoneReward { level = 30,  characterId = "hito_02" },
        new MilestoneReward { level = 60,  characterId = "hito_03" },
        new MilestoneReward { level = 100, characterId = "hito_04" },
        new MilestoneReward { level = 160, characterId = "hito_05" },
        new MilestoneReward { level = 250, characterId = "hito_06" },
    };

    /// ¿Aparece geoda en este nivel? Determinista con la semilla, para que repetir el nivel
    /// dé el mismo reparto que el resto del nivel.
    public bool GeodeAppears(int level, int seed)
    {
        var prev = Random.state;
        Random.InitState(seed ^ (level * 7919));
        bool appears = Random.value < geodeChance;
        Random.state = prev;
        return appears;
    }

    /// Cuánto lleva dentro la geoda de este nivel. También determinista: si mueres y repites,
    /// la geoda vale lo mismo. Si no, el jugador aprendería a morir hasta que le saliera 120.
    public int GeodeRoll(int level, int seed)
    {
        var prev = Random.state;
        Random.InitState((seed * 31) ^ (level * 104729) ^ 0x5F3A);
        int amount = Random.Range(Mathf.Min(geodeMin, geodeMax), Mathf.Max(geodeMin, geodeMax) + 1);
        Random.state = prev;
        return amount;
    }

    public int PriceFor(CharacterRarity rarity)
    {
        switch (rarity)
        {
            case CharacterRarity.Uncommon: return priceUncommon;
            case CharacterRarity.Rare:     return priceRare;
            default:                       return priceCommon;
        }
    }

#if UNITY_EDITOR
    [ContextMenu("Comprobar la economía")]
    void Report()
    {
        float avg = (geodeMin + geodeMax) * 0.5f;
        // Se supone que el jugador la coge y supera el nivel unas 6 de cada 10 veces
        float perLevel = geodeChance * avg * 0.6f;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("EconomyProfile");
        sb.AppendLine($"  geoda: {geodeMin}-{geodeMax} cristales, aparece en el {geodeChance * 100:0}% " +
                      $"de los niveles (una cada {1f / Mathf.Max(0.001f, geodeChance):0.0})");
        sb.AppendLine($"  regalo: ~{perLevel:0.0} cristales por nivel jugado, sin techo");
        sb.AppendLine($"  → un común cada {priceCommon / Mathf.Max(0.01f, perLevel):0} niveles, " +
                      $"un raro cada {priceRare / Mathf.Max(0.01f, perLevel):0}");
        sb.AppendLine($"  → la colección de 50 jugando: unos " +
                      $"{(20 * priceCommon + 18 * priceUncommon + 12 * priceRare) / Mathf.Max(0.01f, perLevel):0} niveles");
        foreach (var p in packs)
            sb.AppendLine($"  pack {p.label}: {p.crystals} cr por ${p.usd:0.00}  " +
                          $"({p.crystals / Mathf.Max(0.01f, p.usd):0} cr/$)  " +
                          $"→ 1 común y sobran {p.crystals - priceCommon}");
        Debug.Log(sb.ToString());
    }
#endif
}

public enum CharacterRarity { Common = 0, Uncommon = 1, Rare = 2 }

[System.Serializable]
public class CrystalPack
{
    [Tooltip("El id tal cual lo registres en Play Console. Tiene que coincidir exactamente")]
    public string productId;
    public string label;
    public int crystals;
    [Tooltip("Solo informativo: el precio real lo pone la tienda del sistema")]
    public float usd;
}

[System.Serializable]
public class MilestoneReward
{
    public int level;
    public string characterId;
}
