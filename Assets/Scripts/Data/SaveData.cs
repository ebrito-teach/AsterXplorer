using System;
using System.Collections.Generic;
using UnityEngine;

/// Todo lo que se guarda en disco, en un solo objeto serializable.
///
/// Dos decisiones que condicionan el resto:
///
/// 1. Lleva VERSIÓN desde el primer día. Cuando dentro de seis meses haya que añadir o
///    renombrar un campo, habrá jugadores reales con guardados que no se pueden perder.
///    Migrar es fácil si sabes de qué versión vienes; imposible si no.
///
/// 2. Nada de Dictionary. JsonUtility no los serializa, así que lo que sería un diccionario
///    va como lista de parejas. Es menos cómodo de leer en código y es la única forma de que
///    sobreviva al guardado.
[Serializable]
public class SaveData
{
    public const int CurrentVersion = 1;

    public int version = CurrentVersion;
    /// Identifica esta instalación. Lo usará la sincronización del ranking para no duplicar
    /// marcas cuando el mismo jugador juegue en dos móviles.
    public string installId = "";
    public string createdUtc = "";
    public string lastPlayedUtc = "";

    public RunState run = new RunState();
    public Records records = new Records();
    public Economy economy = new Economy();
    public Collection collection = new Collection();
    public Purchases purchases = new Purchases();
    public Settings settings = new Settings();
    public Telemetry telemetry = new Telemetry();
}

/// La partida en curso: lo que resucita el botón Continuar.
[Serializable]
public class RunState
{
    public bool active = false;
    public int level = 1;
    public int runSeed = 0;
    public int deaths = 0;
    public float seconds = 0f;
    /// Cristales recogidos en esta partida y aún NO ingresados. Se pierden al morir y se
    /// abonan al superar el nivel. Es lo que da tensión al tramo final.
    public int pendingCrystals = 0;
}

[Serializable]
public class Records
{
    /// Nivel más alto alcanzado nunca. Es la métrica del ranking.
    public int bestLevel = 0;
    public int totalLevelsCleared = 0;
    public int totalDeaths = 0;
    public float totalSeconds = 0f;
    public List<ScoreEntry> top = new List<ScoreEntry>();
}

[Serializable]
public class ScoreEntry
{
    public int level;
    public float seconds;
    public int deaths;
    public string dateUtc;
    public string characterId;
    public int runSeed;
    /// Ya enviada al ranking online. Mientras sea false, está pendiente de subir.
    public bool uploaded;

    /// Puntuación del ranking: manda el nivel y el tiempo desempata.
    /// Las tablas nativas solo admiten un entero, así que los dos criterios van en uno.
    public long LeaderboardScore =>
        (long)level * 100000L + Mathf.Clamp(99999 - Mathf.RoundToInt(seconds), 0, 99999);
}

[Serializable]
public class Economy
{
    public int crystals = 0;
    public int lifetimeCrystals = 0;
    public int crystalsSpent = 0;
    /// Cristales regalados por geodas en toda la vida del jugador. Lo comprado no cuenta aquí.
    /// Es dato, no límite: sirve para saber qué parte de lo que tiene el jugador se la hemos
    /// regalado nosotros, y eso dice si el regalo está bien puesto.
    public int giftedCrystals = 0;
    /// Geodas abiertas. Dato para la pantalla de estadísticas.
    public int geodesOpened = 0;
}

[Serializable]
public class Collection
{
    public List<string> unlocked = new List<string>();
    public string equipped = "";
}

[Serializable]
public class Purchases
{
    /// Ids de los productos no consumibles ya comprados. Es lo que repuebla la colección
    /// cuando el jugador pulsa Restaurar compras en un móvil nuevo.
    public List<string> productIds = new List<string>();
}

[Serializable]
public class Settings
{
    public bool sound = true;
    public bool music = true;
    public bool vibration = true;
    public bool screenEffects = true;        // sacudida, hitstop, destellos
    public ControlScheme control = ControlScheme.Lever;
    public string language = "";             // vacío = el del sistema
    public string playerName = "";
}

public enum ControlScheme { Lever = 0, Tilt = 1 }

/// Lo que alimenta el ajuste invisible de dificultad del Hito 3. Sin estos datos, la
/// dificultad se calibra a ojo.
[Serializable]
public class Telemetry
{
    public List<LevelStat> levels = new List<LevelStat>();

    public LevelStat For(int level, bool createIfMissing = true)
    {
        for (int i = 0; i < levels.Count; i++)
            if (levels[i].level == level) return levels[i];

        if (!createIfMissing) return null;
        var s = new LevelStat { level = level };
        levels.Add(s);
        return s;
    }
}

[Serializable]
public class LevelStat
{
    public int level;
    public int attempts;
    public int deaths;
    public float bestSeconds = -1f;
    public float totalSeconds;

    /// Muertes por intento. Por encima de 2.5 el nivel está mal calibrado.
    public float DeathRate => attempts > 0 ? (float)deaths / attempts : 0f;
}
