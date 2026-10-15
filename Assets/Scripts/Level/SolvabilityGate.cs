using System.Collections.Generic;
using UnityEngine;

/// La garantía de que un nivel se puede pasar.
///
/// El problema real de un esquivador infinito no es que sea difícil: es que el generador,
/// tirando dados, acabe produciendo una pantalla en la que **no existe ninguna jugada**. Eso
/// no es dificultad, es una moneda al aire, y el jugador lo nota enseguida aunque no sepa
/// explicarlo. Pero la solución tampoco puede ser bajar la cantidad hasta que nunca pase:
/// eso regala todos los demás niveles.
///
/// Así que se comprueba, nivel a nivel, y solo se toca lo justo cuando hace falta.
///
/// CÓMO
///
/// Un nivel no es una foto: los asteroides se mueven, y un hueco cerrado ahora está abierto
/// dentro de dos segundos. Por eso no sirve preguntar "¿hay camino?" sobre la pantalla
/// quieta. Lo que se hace es simular el nivel hacia delante en rebanadas de tiempo y buscar
/// camino **en el espacio-tiempo**: desde la casilla del jugador en t=0, a cualquier casilla
/// vecina que esté libre en t+1, y así. Si alguna de esas ramas llega a la meta, el nivel se
/// puede pasar — aunque haga falta jugarlo perfecto.
///
/// El paso de tiempo se elige para que el jugador avance exactamente una casilla por
/// rebanada, así que el camino que encuentra es un camino que de verdad se puede recorrer a
/// la velocidad a la que se mueve.
///
/// CUÁNDO TOCA ALGO
///
/// Solo si no hay ninguna rama que llegue. Entonces busca qué asteroide está tapando el
/// frente de avance, lo retira, y vuelve a comprobar. Retira de uno en uno y vuelve a probar
/// cada vez, así que quita el mínimo: en cuanto se abre un paso, para. Un nivel que pasa a la
/// primera no se toca en absoluto.
///
/// Dos cosas quedan fuera a propósito: los colosos, que traen su propio pasillo garantizado,
/// y los asteroides no letales, que empujan pero no matan y por tanto no cierran un camino.
///
/// SI ESTO SALTA MUCHO, EL PROBLEMA NO ES ESTO
///
/// El log dice cuántos retira. Si en el nivel 60 está retirando cinco cada vez, el
/// verificador está tapando un agujero que no es suyo: lo que está mal es la curva de
/// cantidad. Esa es la señal que hay que mirar.
public static class SolvabilityGate
{
    public struct Report
    {
        public bool solvable;      // ¿se puede pasar, después de lo que haya hecho?
        public bool wasDeathTrap;  // el jugador moría hiciera lo que hiciera
        public int removed;        // cuántos asteroides retiró
        public int steps;          // rebanadas de tiempo simuladas
        public float millis;

        public string Describe() =>
            !solvable ? $"SIN PASO tras retirar {removed}"
            : removed == 0 ? "paso libre"
            : $"paso abierto retirando {removed}{(wasDeathTrap ? " (era muerte segura)" : "")}";
    }

    // Buffers reutilizados: esto corre una vez por nivel y no debe generar basura
    static int[] owner;              // índice del asteroide que ocupa la casilla, -1 = libre
    static bool[] reachedNow, reachedNext;
    static int cols, rows, cells;

    static readonly List<Vector2> pos = new List<Vector2>();
    static readonly List<Vector2> vel = new List<Vector2>();
    static readonly List<float> rad = new List<float>();
    static readonly List<Asteroid> refs = new List<Asteroid>();
    static readonly List<bool> live = new List<bool>();
    static readonly List<int> culprits = new List<int>();

    /// Comprueba el nivel y, si no hay paso, abre uno retirando lo mínimo.
    /// `remove` es lo que hace desaparecer el asteroide (normalmente pool.Release).
    public static Report Enforce(IReadOnlyList<Asteroid> active,
                                 Vector2 playerPos, float playerRadius, float playerSpeed,
                                 Rect area, Rect goal, DifficultyProfile d,
                                 System.Action<Asteroid> remove)
    {
        var report = new Report();
        if (active == null || d == null) { report.solvable = true; return report; }

        float t0 = Time.realtimeSinceStartup;

        float cell = Mathf.Max(0.2f, d.solveCell);
        cols = Mathf.Max(2, Mathf.CeilToInt(area.width / cell));
        rows = Mathf.Max(2, Mathf.CeilToInt(area.height / cell));
        cells = cols * rows;

        if (owner == null || owner.Length < cells)
        {
            owner = new int[cells];
            reachedNow = new bool[cells];
            reachedNext = new bool[cells];
        }

        // El paso de tiempo hace que el jugador avance una casilla por rebanada: así el
        // camino encontrado es uno que se puede recorrer de verdad, no uno teletransportado
        float speed = Mathf.Max(0.5f, playerSpeed);
        float dt = cell / speed;
        int steps = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(1f, d.solveHorizon) / dt), 4, 240);
        report.steps = steps;

        Snapshot(active, playerRadius + Mathf.Max(0f, d.solveClearance));

        int maxRemovals = Mathf.Max(0, d.solveMaxRemovals);
        for (int pass = 0; pass <= maxRemovals; pass++)
        {
            bool reachedGoal = Search(playerPos, area, goal, cell, dt, steps,
                                      out bool diedOut, out int failStep);
            if (reachedGoal) { report.solvable = true; break; }

            report.wasDeathTrap |= diedOut;
            if (pass == maxRemovals) break;

            int victim = PickCulprit(area, cell, dt, failStep);
            if (victim < 0) break;                 // no hay nada que se pueda retirar

            live[victim] = false;
            if (refs[victim] != null) remove?.Invoke(refs[victim]);
            report.removed++;
        }

        report.millis = (Time.realtimeSinceStartup - t0) * 1000f;
        return report;
    }

    // ------------------------------------------------------------------ datos
    static void Snapshot(IReadOnlyList<Asteroid> active, float inflate)
    {
        pos.Clear(); vel.Clear(); rad.Clear(); refs.Clear(); live.Clear();

        for (int i = 0; i < active.Count; i++)
        {
            var a = active[i];
            if (a == null || a.Type == null || a.Body == null) continue;
            // Lo que no mata no cierra un camino: te empuja y sigues vivo
            if (!a.Lethal) continue;

            refs.Add(a);
            pos.Add(a.Body.position);
            vel.Add(a.Body.linearVelocity);
            rad.Add(a.WorldRadius + inflate);
            live.Add(true);
        }
    }

    // --------------------------------------------------------------- búsqueda
    /// Flood fill en el espacio-tiempo. Devuelve true si alguna rama llega a la meta.
    /// `diedOut` dice si el jugador se quedó sin ninguna casilla: eso es muerte segura.
    static bool Search(Vector2 playerPos, Rect area, Rect goal, float cell, float dt, int steps,
                       out bool diedOut, out int failStep)
    {
        diedOut = false;
        failStep = steps;

        Rasterize(area, cell, 0f);
        System.Array.Clear(reachedNow, 0, cells);

        int start = NearestFreeCell(playerPos, area, cell);
        if (start < 0) { diedOut = true; failStep = 0; return false; }
        reachedNow[start] = true;
        if (InGoal(start, area, cell, goal)) return true;

        for (int s = 1; s <= steps; s++)
        {
            Rasterize(area, cell, s * dt);
            System.Array.Clear(reachedNext, 0, cells);

            bool any = false, won = false;
            for (int idx = 0; idx < cells; idx++)
            {
                if (!reachedNow[idx]) continue;
                int cx = idx % cols, cy = idx / cols;

                for (int dy = -1; dy <= 1; dy++)
                {
                    int ny = cy + dy;
                    if (ny < 0 || ny >= rows) continue;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = cx + dx;
                        if (nx < 0 || nx >= cols) continue;

                        int n = ny * cols + nx;
                        if (reachedNext[n] || owner[n] >= 0) continue;

                        reachedNext[n] = true;
                        any = true;
                        if (InGoal(n, area, cell, goal)) won = true;
                    }
                }
            }

            if (won) return true;
            if (!any) { diedOut = true; failStep = s; return false; }

            var swap = reachedNow; reachedNow = reachedNext; reachedNext = swap;
        }

        // Sobrevivió todo el horizonte pero no llegó: no es muerte segura, pero tampoco
        // es un nivel que se pueda pasar desde esta posición de salida
        failStep = steps;
        return false;
    }

    static void Rasterize(Rect area, float cell, float t)
    {
        for (int i = 0; i < cells; i++) owner[i] = -1;

        for (int i = 0; i < refs.Count; i++)
        {
            if (!live[i]) continue;
            Vector2 p = pos[i] + vel[i] * t;
            float r = rad[i];

            int x0 = Mathf.Max(0, Mathf.FloorToInt((p.x - r - area.xMin) / cell));
            int x1 = Mathf.Min(cols - 1, Mathf.CeilToInt((p.x + r - area.xMin) / cell));
            int y0 = Mathf.Max(0, Mathf.FloorToInt((p.y - r - area.yMin) / cell));
            int y1 = Mathf.Min(rows - 1, Mathf.CeilToInt((p.y + r - area.yMin) / cell));
            if (x1 < x0 || y1 < y0) continue;

            float r2 = r * r;
            for (int y = y0; y <= y1; y++)
            {
                float cyw = area.yMin + (y + 0.5f) * cell;
                float ddy = cyw - p.y;
                for (int x = x0; x <= x1; x++)
                {
                    float cxw = area.xMin + (x + 0.5f) * cell;
                    float ddx = cxw - p.x;
                    if (ddx * ddx + ddy * ddy <= r2) owner[y * cols + x] = i;
                }
            }
        }
    }

    /// El asteroide que está tapando el frente: de los que bloquean una casilla pegada a
    /// donde el jugador podía llegar, el más grande. Los colosos no cuentan — traen su
    /// propio pasillo y retirarlos rompería su lógica.
    static int PickCulprit(Rect area, float cell, float dt, int failStep)
    {
        Rasterize(area, cell, failStep * dt);
        culprits.Clear();

        for (int idx = 0; idx < cells; idx++)
        {
            if (!reachedNow[idx]) continue;
            int cx = idx % cols, cy = idx / cols;
            for (int dy = -1; dy <= 1; dy++)
            {
                int ny = cy + dy;
                if (ny < 0 || ny >= rows) continue;
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = cx + dx;
                    if (nx < 0 || nx >= cols) continue;
                    int o = owner[ny * cols + nx];
                    if (o >= 0 && !culprits.Contains(o)) culprits.Add(o);
                }
            }
        }

        int best = -1; float bestR = -1f;
        for (int i = 0; i < culprits.Count; i++)
        {
            int c = culprits[i];
            var a = refs[c];
            if (a == null || !live[c] || a.Unstoppable) continue;
            if (rad[c] > bestR) { bestR = rad[c]; best = c; }
        }

        // Nadie pegado al frente se puede retirar: prueba con el más grande que quede
        if (best < 0)
        {
            for (int i = 0; i < refs.Count; i++)
            {
                if (!live[i] || refs[i] == null || refs[i].Unstoppable) continue;
                if (rad[i] > bestR) { bestR = rad[i]; best = i; }
            }
        }
        return best;
    }

    // ------------------------------------------------------------------ util
    static int NearestFreeCell(Vector2 p, Rect area, float cell)
    {
        int cx = Mathf.Clamp(Mathf.FloorToInt((p.x - area.xMin) / cell), 0, cols - 1);
        int cy = Mathf.Clamp(Mathf.FloorToInt((p.y - area.yMin) / cell), 0, rows - 1);
        if (owner[cy * cols + cx] < 0) return cy * cols + cx;

        // En espiral hacia fuera: si el jugador aparece pegado a una roca, el camino
        // empieza en la casilla libre más cercana, no en ninguna
        for (int ring = 1; ring <= 6; ring++)
            for (int dy = -ring; dy <= ring; dy++)
                for (int dx = -ring; dx <= ring; dx++)
                {
                    if (Mathf.Abs(dx) != ring && Mathf.Abs(dy) != ring) continue;
                    int nx = cx + dx, ny = cy + dy;
                    if (nx < 0 || nx >= cols || ny < 0 || ny >= rows) continue;
                    if (owner[ny * cols + nx] < 0) return ny * cols + nx;
                }
        return -1;
    }

    static bool InGoal(int idx, Rect area, float cell, Rect goal)
    {
        float x = area.xMin + (idx % cols + 0.5f) * cell;
        float y = area.yMin + (idx / cols + 0.5f) * cell;
        return goal.Contains(new Vector2(x, y));
    }
}
