using System;
using UnityEngine;

/// El punto único por el que pasa todo lo que se guarda.
///
/// Nadie más toca SaveSystem: el resto del juego pide y avisa a través de aquí. Así el día
/// que haya que subir marcas al ranking o cambiar dónde se guarda, se cambia en un sitio.
///
/// Sobrevive al cambio de escena (DontDestroyOnLoad) y guarda al perder el foco, que en móvil
/// es lo que de verdad pasa: la gente no cierra el juego, se va a otra aplicación.
[DefaultExecutionOrder(-100)]
public class GameProgress : MonoBehaviour
{
    public static GameProgress Instance { get; private set; }

    [SerializeField] private EconomyProfile economy;
    [Tooltip("Guarda en cada hito importante. Desactívalo solo para depurar")]
    [SerializeField] private bool autoSave = true;
    [Tooltip("Personaje con el que se empieza, antes de comprar ninguno")]
    [SerializeField] private string defaultCharacterId = "boy";

    public SaveData Data { get; private set; }
    public EconomyProfile Economy => economy;

    public event Action<int> CrystalsChanged;
    public event Action<int> NewBestLevel;
    /// Geoda abierta: cuántos cristales dio y cuántas geodas van ya.
    public event Action<int, int> GeodeOpened;
    /// Personaje de hito desbloqueado: id y nivel que lo dio. Lo recoge el aviso en pantalla.
    public event Action<string, int> MilestoneUnlocked;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        Data = SaveSystem.Load();

        if (Data.collection.unlocked.Count == 0)
            Data.collection.unlocked.Add(defaultCharacterId);
        if (string.IsNullOrEmpty(Data.collection.equipped))
            Data.collection.equipped = Data.collection.unlocked[0];

        // Por si se añade un hito para un nivel que el jugador ya había pasado
        CheckMilestones();

        Debug.Log($"GameProgress: guardado cargado. Mejor nivel {Data.records.bestLevel}, " +
                  $"{Data.economy.crystals} cristales. Archivo: {SaveSystem.DebugPath}");
    }

    // En móvil nadie cierra el juego: se va a otra aplicación. Ahí es donde hay que guardar.
    void OnApplicationPause(bool paused) { if (paused) Save(); }
    void OnApplicationFocus(bool focused) { if (!focused) Save(); }
    void OnApplicationQuit() { Save(); }

    public void Save() { if (Data != null) SaveSystem.Save(Data); }

    // ---------------------------------------------------------------- partida
    public bool HasRun => Data.run.active;

    public void BeginRun(int seed, int level = 1)
    {
        Data.run.active = true;
        Data.run.level = Mathf.Max(1, level);
        Data.run.runSeed = seed;
        Data.run.deaths = 0;
        Data.run.seconds = 0f;
        Data.run.pendingCrystals = 0;
        if (autoSave) Save();
    }

    public void EndRun()
    {
        Data.run.active = false;
        Data.run.pendingCrystals = 0;
        if (autoSave) Save();
    }

    /// Nivel superado. Pasar un nivel NO paga cristales — lo único que se ingresa es la geoda
    /// que llevaras encima. Lo que da el nivel es el nivel: el ranking y los hitos.
    public void LevelCleared(int level, float seconds)
    {
        var stat = Data.telemetry.For(level);
        stat.attempts++;
        stat.totalSeconds += seconds;
        if (stat.bestSeconds < 0f || seconds < stat.bestSeconds) stat.bestSeconds = seconds;

        Data.records.totalLevelsCleared++;
        Data.records.totalSeconds += seconds;
        Data.run.seconds += seconds;

        // La geoda se cobra aquí, no al recogerla
        if (Data.run.pendingCrystals > 0)
        {
            int gained = Data.run.pendingCrystals;
            Data.run.pendingCrystals = 0;
            AddCrystals(gained);
        }

        if (level > Data.records.bestLevel)
        {
            Data.records.bestLevel = level;
            NewBestLevel?.Invoke(level);
            CheckMilestones();
        }

        Data.run.level = level + 1;
        if (autoSave) Save();
    }

    /// Muerte: se pierde lo recogido y no ingresado. Esa es toda la tensión de la geoda.
    public void PlayerDied(int level)
    {
        var stat = Data.telemetry.For(level);
        stat.attempts++;
        stat.deaths++;

        Data.records.totalDeaths++;
        Data.run.deaths++;
        Data.run.pendingCrystals = 0;
        if (autoSave) Save();
    }

    // ---------------------------------------------------------------- la geoda
    /// Geoda recogida. Aún NO es tuya: se abona al superar el nivel, y si mueres se pierde.
    ///
    /// La cantidad sale del nivel y la semilla, no del momento: si mueres y repites, la geoda
    /// vale lo mismo. Si fuera aleatoria en cada intento, el jugador aprendería a morir hasta
    /// que le saliera el máximo — con 100 cristales de diferencia entre el mínimo y el máximo,
    /// le compensaría intentarlo.
    ///
    /// No se agota. El que sigue jugando sigue recibiendo.
    public int CollectGeode(int level, int seed)
    {
        Data.economy.geodesOpened++;

        int amount = economy != null ? economy.GeodeRoll(level, seed) : 0;
        if (amount > 0)
        {
            Data.economy.giftedCrystals += amount;
            Data.run.pendingCrystals += amount;
        }

        GeodeOpened?.Invoke(amount, Data.economy.geodesOpened);
        return amount;
    }

    // ---------------------------------------------------------------- hitos
    /// Reparte los personajes de hito que ya toquen. Es idempotente: se puede llamar siempre.
    void CheckMilestones()
    {
        if (economy == null || economy.milestones == null) return;

        foreach (var m in economy.milestones)
        {
            if (m == null || string.IsNullOrEmpty(m.characterId)) continue;
            if (Data.records.bestLevel < m.level) continue;
            if (Data.collection.unlocked.Contains(m.characterId)) continue;

            Data.collection.unlocked.Add(m.characterId);
            MilestoneUnlocked?.Invoke(m.characterId, m.level);
            Debug.Log($"GameProgress: hito del nivel {m.level} → personaje {m.characterId}.");
        }
    }

    // ---------------------------------------------------------------- economía
    public int Crystals => Data.economy.crystals;

    public void AddCrystals(int amount)
    {
        if (amount <= 0) return;
        Data.economy.crystals += amount;
        Data.economy.lifetimeCrystals += amount;
        CrystalsChanged?.Invoke(Data.economy.crystals);
    }

    /// Pack de cristales comprado. Es consumible: NO se guarda en purchases.productIds, porque
    /// restaurar compras no debe devolver cristales ya gastados — solo los personajes.
    public bool GrantPack(string productId)
    {
        if (economy == null || economy.packs == null) return false;
        foreach (var p in economy.packs)
        {
            if (p == null || p.productId != productId) continue;
            AddCrystals(p.crystals);
            Debug.Log($"GameProgress: pack {p.productId} → {p.crystals} cristales.");
            if (autoSave) Save();
            return true;
        }
        Debug.LogError($"GameProgress: no hay ningún pack con el id '{productId}'. " +
                       "Tiene que coincidir exactamente con el de Play Console.");
        return false;
    }

    public bool SpendCrystals(int amount)
    {
        if (amount <= 0 || Data.economy.crystals < amount) return false;
        Data.economy.crystals -= amount;
        Data.economy.crystalsSpent += amount;
        CrystalsChanged?.Invoke(Data.economy.crystals);
        if (autoSave) Save();
        return true;
    }

    // ---------------------------------------------------------------- colección
    public bool IsUnlocked(string characterId) => Data.collection.unlocked.Contains(characterId);

    public void Unlock(string characterId, string productId = null)
    {
        if (!Data.collection.unlocked.Contains(characterId))
            Data.collection.unlocked.Add(characterId);
        // El id de producto se guarda aparte: es lo que repuebla la colección al restaurar
        // compras en un móvil nuevo
        if (!string.IsNullOrEmpty(productId) && !Data.purchases.productIds.Contains(productId))
            Data.purchases.productIds.Add(productId);
        if (autoSave) Save();
    }

    public void Equip(string characterId)
    {
        if (!IsUnlocked(characterId)) return;
        Data.collection.equipped = characterId;
        if (autoSave) Save();
    }

    // ---------------------------------------------------------------- marcas
    /// Cierra la partida y la mete en el top 10 si da la talla.
    public void SubmitRun()
    {
        if (!Data.run.active) return;

        var entry = new ScoreEntry
        {
            level = Data.records.bestLevel,
            seconds = Data.run.seconds,
            deaths = Data.run.deaths,
            dateUtc = DateTime.UtcNow.ToString("o"),
            characterId = Data.collection.equipped,
            runSeed = Data.run.runSeed,
            uploaded = false
        };

        var top = Data.records.top;
        top.Add(entry);
        top.Sort((a, b) => b.LeaderboardScore.CompareTo(a.LeaderboardScore));
        if (top.Count > 10) top.RemoveRange(10, top.Count - 10);

        EndRun();
    }

    [ContextMenu("Borrar el guardado")]
    public void DeleteSave()
    {
        SaveSystem.Delete();
        Data = SaveSystem.CreateNew();
        Data.collection.unlocked.Add(defaultCharacterId);
        Data.collection.equipped = defaultCharacterId;
        Debug.Log("GameProgress: guardado borrado.");
    }
}
