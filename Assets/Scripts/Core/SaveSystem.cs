using System;
using System.IO;
using UnityEngine;

/// Lectura y escritura del guardado. Una sola responsabilidad: que el archivo no se corrompa.
///
/// Escribe en un temporal y después reemplaza. Si el móvil se queda sin batería a mitad de
/// la escritura, lo que se corrompe es el temporal y el guardado bueno sigue intacto. Hacerlo
/// al revés — escribir directamente sobre el archivo real — es la causa número uno de
/// "perdí todo mi progreso" en juegos móviles.
///
/// Y guarda además una copia de seguridad del guardado anterior, por si algún día un error
/// nuestro escribe algo inválido.
public static class SaveSystem
{
    const string FileName = "asterxplorer.save.json";
    const string TempName = "asterxplorer.save.tmp";
    const string BackupName = "asterxplorer.save.bak";

    static string Dir => Application.persistentDataPath;
    static string Path => System.IO.Path.Combine(Dir, FileName);
    static string TempPath => System.IO.Path.Combine(Dir, TempName);
    static string BackupPath => System.IO.Path.Combine(Dir, BackupName);

    public static bool Exists => File.Exists(Path);

    public static SaveData Load()
    {
        var data = TryRead(Path);
        if (data == null)
        {
            data = TryRead(BackupPath);
            if (data != null) Debug.LogWarning("SaveSystem: guardado dañado, se recuperó la copia.");
        }

        if (data == null) return CreateNew();

        if (data.version != SaveData.CurrentVersion) Migrate(data);
        if (string.IsNullOrEmpty(data.installId)) data.installId = Guid.NewGuid().ToString("N");
        return data;
    }

    static SaveData TryRead(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) return null;
            var data = JsonUtility.FromJson<SaveData>(json);
            // Un JSON válido pero de otra cosa deserializa a un objeto sin versión
            return (data != null && data.version > 0) ? data : null;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"SaveSystem: no se pudo leer {System.IO.Path.GetFileName(path)}: {e.Message}");
            return null;
        }
    }

    public static SaveData CreateNew()
    {
        string now = DateTime.UtcNow.ToString("o");
        return new SaveData
        {
            installId = Guid.NewGuid().ToString("N"),
            createdUtc = now,
            lastPlayedUtc = now
        };
    }

    public static bool Save(SaveData data)
    {
        if (data == null) return false;
        data.lastPlayedUtc = DateTime.UtcNow.ToString("o");

        try
        {
            string json = JsonUtility.ToJson(data, prettyPrint: true);
            File.WriteAllText(TempPath, json);

            // Copia del anterior antes de pisarlo
            if (File.Exists(Path))
            {
                if (File.Exists(BackupPath)) File.Delete(BackupPath);
                File.Move(Path, BackupPath);
            }
            File.Move(TempPath, Path);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"SaveSystem: no se pudo guardar: {e.Message}");
            return false;
        }
    }

    /// Borra el guardado. Solo desde Ajustes y con confirmación del jugador.
    public static void Delete()
    {
        foreach (var p in new[] { Path, TempPath, BackupPath })
            try { if (File.Exists(p)) File.Delete(p); } catch { }
    }

    /// Aquí van las conversiones cuando cambie el esquema. Hoy no hay nada que migrar,
    /// pero el sitio tiene que existir desde el principio: cuando haga falta, ya habrá
    /// jugadores reales con guardados que no se pueden tirar.
    static void Migrate(SaveData data)
    {
        int from = data.version;
        // if (data.version < 2) { ...; data.version = 2; }
        data.version = SaveData.CurrentVersion;
        Debug.Log($"SaveSystem: guardado migrado de la versión {from} a la {data.version}.");
    }

    /// Para depurar: la ruta real del archivo en este dispositivo.
    public static string DebugPath => Path;
}
