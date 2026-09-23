using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace NotchFlow.Infrastructure.Logging;

/// <summary>
/// Journal minimaliste du projet.
///
/// Trois contraintes ont dicté sa forme :
/// le chemin d'écriture ne doit <em>jamais</em> bloquer un thread d'interface —
/// les entrées sont donc mises en file et vidées par un unique thread
/// d'arrière-plan ; le fichier ne doit pas croître indéfiniment — il tourne donc
/// au-delà d'une taille plafonnée ; et l'Island devant rester à ~0 % de CPU au
/// repos, le thread de vidage dort sur un signal et ne consomme rien quand rien
/// n'est journalisé.
/// </summary>
public static class MiniLogger
{
    /// <summary>Taille au-delà de laquelle le journal est archivé puis tronqué.</summary>
    private const long MaxLogBytes = 2 * 1024 * 1024;

    /// <summary>
    /// Plafond de la file : au-delà, les entrées sont abandonnées plutôt que de
    /// faire enfler la mémoire. Un journal incomplet vaut mieux qu'une fuite.
    /// </summary>
    private const int MaxQueuedEntries = 2048;

    private static readonly BlockingCollection<string> Queue = new(MaxQueuedEntries);
    private static readonly string LogDirectory;
    private static readonly string LogPath;
    private static readonly Thread WriterThread;

    private static bool _started;

    static MiniLogger()
    {
        LogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NotchFlow",
            "logs");

        LogPath = Path.Combine(LogDirectory, "notchflow.log");

        WriterThread = new Thread(DrainQueue)
        {
            IsBackground = true,
            Name = "NotchFlow.LogWriter"
        };
    }

    /// <summary>Démarre le thread d'écriture. Idempotent.</summary>
    public static void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;

        try
        {
            Directory.CreateDirectory(LogDirectory);
            WriterThread.Start();
        }
        catch (Exception)
        {
            // Un journal indisponible ne doit pas empêcher l'application de démarrer.
        }
    }

    public static void Log(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        Start();

        string line = string.Create(
            CultureInfo.InvariantCulture,
            $"[{DateTime.Now:HH:mm:ss.fff}] {message}");

        // TryAdd : on abandonne silencieusement si la file est saturée.
        Queue.TryAdd(line);
    }

    public static void Log(string message, Exception exception)
        => Log($"{message} :: {Describe(exception)}");

    /// <summary>
    /// Décrit une exception de façon exploitable même lorsqu'elle n'a pas de
    /// message.
    ///
    /// Un <c>COMException</c> — ce que WinUI lève lorsqu'un appel atteint
    /// l'interface depuis le mauvais fil — porte souvent un message <em>vide</em>.
    /// Le journal se réduisait alors à « COMException: », une ligne qui n'oriente
    /// vers rien. Le type, le code de retour et la chaîne des exceptions internes
    /// sont donc écrits, et la profondeur est bornée pour qu'un enchaînement
    /// pathologique ne produise pas une ligne illisible.
    /// </summary>
    private static string Describe(Exception exception)
    {
        const int MaxDepth = 4;

        var builder = new StringBuilder();
        Exception? current = exception;

        for (int depth = 0; current is not null && depth < MaxDepth; depth++)
        {
            if (depth > 0)
            {
                builder.Append(" ← ");
            }

            builder.Append(current.GetType().Name);
            builder.Append(" (0x");
            builder.Append(current.HResult.ToString("X8", CultureInfo.InvariantCulture));
            builder.Append(')');

            string text = current.Message;

            if (!string.IsNullOrWhiteSpace(text))
            {
                builder.Append(" : ");
                builder.Append(text.Trim());
            }

            current = current.InnerException;
        }

        return builder.ToString();
    }

    public static void Info(string message) => Log(message);

    /// <summary>
    /// Vide la file de façon séquentielle. Le thread ne consomme rien tant
    /// qu'aucune entrée n'est disponible.
    /// </summary>
    private static void DrainQueue()
    {
        var buffer = new StringBuilder(8192);
        long writtenBytes = 0;

        try
        {
            writtenBytes = new FileInfo(LogPath).Exists ? new FileInfo(LogPath).Length : 0;
        }
        catch (Exception)
        {
            writtenBytes = 0;
        }

        foreach (string line in Queue.GetConsumingEnumerable())
        {
            // On vide aussi tout ce qui s'est accumulé, en un seul accès disque.
            buffer.Clear().AppendLine(line);

            while (Queue.TryTake(out string? extra))
            {
                buffer.AppendLine(extra);
            }

            try
            {
                string payload = buffer.ToString();
                File.AppendAllText(LogPath, payload);
                writtenBytes += payload.Length;

                if (writtenBytes > MaxLogBytes)
                {
                    Rotate();
                    writtenBytes = 0;
                }
            }
            catch (Exception)
            {
                // Disque plein ou verrou : on abandonne ce lot sans interrompre le thread.
            }
        }
    }

    /// <summary>Archive le journal courant puis repart d'un fichier vide.</summary>
    private static void Rotate()
    {
        try
        {
            string archive = Path.Combine(LogDirectory, "notchflow.previous.log");

            if (File.Exists(archive))
            {
                File.Delete(archive);
            }

            if (File.Exists(LogPath))
            {
                File.Move(LogPath, archive);
            }
        }
        catch (Exception)
        {
            // Si la rotation échoue, on tronque : mieux vaut perdre l'historique
            // que laisser le fichier croître sans limite.
            try
            {
                File.WriteAllText(LogPath, string.Empty);
            }
            catch (Exception)
            {
                // Ignoré.
            }
        }
    }

    /// <summary>Attend la vidage de la file. Utilisé à l'arrêt de l'application.</summary>
    public static void Flush(TimeSpan timeout)
    {
        if (!_started)
        {
            return;
        }

        Queue.CompleteAdding();

        try
        {
            WriterThread.Join(timeout);
        }
        catch (Exception)
        {
            // Ignoré : l'arrêt ne doit jamais échouer à cause du journal.
        }
    }

    /// <summary>
    /// Dossier du journal, exposé pour les diagnostics. Nommé explicitement pour
    /// ne pas masquer <see cref="System.IO.Directory"/>.
    /// </summary>
    public static string LogDirectoryPath => LogDirectory;

    [Conditional("DEBUG")]
    public static void Debug(string message) => Log(message);
}
