namespace SpaceNotch.Core.Motion;

/// <summary>
/// Écran de veille génératif (P5) : un jeu de la vie en pixels dans la notch
/// élargie, quand le PC est inactif. La graine vient de la date et du temps
/// qu'il fait, donc chaque nuit dessine autre chose — et la même nuit dessine
/// la même chose, ce qui se teste.
///
/// <para>
/// Règles B3/S23 sur un tore. Une population éteinte, figée ou qui oscille sur
/// deux temps est ressemée : l'écran de veille ne s'arrête jamais sur une image
/// morte.
/// </para>
/// </summary>
public sealed class LifeGrid
{
    /// <summary>Images par seconde.</summary>
    public const int FramesPerSecond = 8;

    /// <summary>Âge (en générations) au-delà duquel une cellule prend la teinte la plus sombre.</summary>
    public const int OldAge = 6;

    private bool[] _cells;
    private readonly int[] _ages;
    private ulong _history1;
    private ulong _history2;
    private uint _seed;

    public LifeGrid(int columns, int rows, uint seed)
    {
        Columns = Math.Max(3, columns);
        Rows = Math.Max(3, rows);
        _cells = new bool[Columns * Rows];
        _ages = new int[Columns * Rows];
        _seed = seed == 0 ? 0x9E3779B9u : seed;
        Sow();
    }

    public int Columns { get; }

    public int Rows { get; }

    /// <summary>Générations depuis la dernière semence.</summary>
    public int Generation { get; private set; }

    /// <summary>Nombre de semences, la première comprise.</summary>
    public int Sowings { get; private set; }

    public bool IsAlive(int column, int row) => _cells[(row * Columns) + column];

    /// <summary>Âge d'une cellule vivante, en générations (0 pour une naissance).</summary>
    public int Age(int column, int row) => _ages[(row * Columns) + column];

    public int Population => _cells.Count(c => c);

    /// <summary>Graine d'une nuit : la date et le code météo (WMO), ou 0 sans météo.</summary>
    public static uint SeedFor(DateOnly date, int weatherCode)
        => unchecked((uint)((date.DayNumber * 2654435761L) ^ (weatherCode * 40503L) ^ 0x5F3759DF));

    /// <summary>Avance d'une génération.</summary>
    public void Step()
    {
        var next = new bool[_cells.Length];

        for (int row = 0; row < Rows; row++)
        {
            for (int column = 0; column < Columns; column++)
            {
                int n = 0;

                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if ((dx != 0 || dy != 0) && _cells[(((row + dy + Rows) % Rows) * Columns) + ((column + dx + Columns) % Columns)])
                        {
                            n++;
                        }
                    }
                }

                int i = (row * Columns) + column;
                next[i] = n == 3 || (_cells[i] && n == 2);
                _ages[i] = next[i] ? (_cells[i] ? _ages[i] + 1 : 0) : 0;
            }
        }

        _cells = next;
        Generation++;

        ulong signature = Signature();

        if (Population == 0 || signature == _history1 || signature == _history2)
        {
            Sow();
            return;
        }

        _history2 = _history1;
        _history1 = signature;
    }

    private void Sow()
    {
        for (int i = 0; i < _cells.Length; i++)
        {
            _cells[i] = Next() % 100 < 30;
            _ages[i] = 0;
        }

        Generation = 0;
        Sowings++;
        _history1 = _history2 = 0;
    }

    private uint Next()
    {
        // xorshift32 : rapide, déterministe, suffisant pour semer des pixels.
        _seed ^= _seed << 13;
        _seed ^= _seed >> 17;
        _seed ^= _seed << 5;
        return _seed;
    }

    private ulong Signature()
    {
        ulong h = 14695981039346656037UL;

        for (int i = 0; i < _cells.Length; i++)
        {
            h ^= _cells[i] ? 1UL : 0UL;
            h *= 1099511628211UL;
        }

        return h;
    }
}

/// <summary>Quand l'écran de veille de la notch a le droit de tourner.</summary>
public static class ScreensaverPolicy
{
    /// <summary>Inactivité avant de commencer.</summary>
    public static readonly TimeSpan IdleBefore = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Seulement si l'utilisateur l'a voulu, sur secteur, sans plein écran ni
    /// activité à montrer, après cinq minutes sans clavier ni souris.
    /// </summary>
    public static bool ShouldRun(bool enabled, TimeSpan idle, bool onBattery, bool fullscreen, bool hasActivity)
        => enabled && !onBattery && !fullscreen && !hasActivity && idle >= IdleBefore;
}
