using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using SpaceNotch.Core.SystemInfo;

namespace SpaceNotch.Platform.Windows.Power;

/// <summary>Le processus qui occupe le plus le processeur.</summary>
public readonly record struct HeavyProcess(string Name, int Id, double Percent);

/// <summary>
/// Mesure du processeur (F6). <see cref="Sample"/> ne coûte qu'un appel à
/// <c>GetSystemTimes</c> ; la recherche du processus gourmand, plus chère,
/// n'a lieu qu'au moment d'une alerte (<see cref="FindHeaviest"/>).
/// </summary>
public sealed class CpuSampler
{
    private long _idle, _kernel, _user;
    private bool _primed;

    /// <summary>Charge du processeur depuis l'appel précédent, en pourcentage ; 0 au premier appel.</summary>
    public double Sample()
    {
        if (!GetSystemTimes(out long idle, out long kernel, out long user))
        {
            return 0;
        }

        double percent = _primed ? CpuWatch.Percent(idle - _idle, kernel - _kernel, user - _user) : 0;
        (_idle, _kernel, _user, _primed) = (idle, kernel, user, true);
        return percent;
    }

    /// <summary>
    /// Deux relevés des temps processeur à <paramref name="window"/> d'écart :
    /// le processus qui a consommé le plus, hors processus protégés.
    /// </summary>
    public static HeavyProcess? FindHeaviest(TimeSpan window)
    {
        int own = Environment.ProcessId;
        var before = new Dictionary<int, (string Name, TimeSpan Cpu)>();

        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (!CpuWatch.IsProtected(process.ProcessName, process.Id, own))
                    {
                        before[process.Id] = (process.ProcessName, process.TotalProcessorTime);
                    }
                }
                catch (Exception)
                {
                    // Processus protégé ou déjà terminé : ignoré.
                }
            }
        }

        Thread.Sleep(window);
        HeavyProcess? heaviest = null;

        foreach ((int id, (string name, TimeSpan cpu)) in before)
        {
            try
            {
                using Process process = Process.GetProcessById(id);
                double used = (process.TotalProcessorTime - cpu).TotalMilliseconds;
                double percent = 100 * used / (window.TotalMilliseconds * Environment.ProcessorCount);

                if (heaviest is null || percent > heaviest.Value.Percent)
                {
                    heaviest = new HeavyProcess(name, id, Math.Round(percent, 1));
                }
            }
            catch (Exception)
            {
                // Terminé entre les deux relevés.
            }
        }

        return heaviest;
    }

    /// <summary>Ferme un processus, sauf s'il est protégé. Vrai s'il a été fermé.</summary>
    public static bool TryClose(int id)
    {
        try
        {
            using Process process = Process.GetProcessById(id);

            if (CpuWatch.IsProtected(process.ProcessName, id, Environment.ProcessId))
            {
                return false;
            }

            // Une application avec une fenêtre est d'abord priée de se fermer ;
            // si elle ne répond pas en deux secondes, elle est arrêtée.
            if (process.CloseMainWindow() && process.WaitForExit(2000))
            {
                return true;
            }

            process.Kill();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);
}
