using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;

namespace SpaceNotch_App.Animations;

/// <summary>
/// Mesure de fluidité (<c>--frames</c>) : la raison de chaque GC, lue dans les
/// événements <c>GCStart</c> du runtime, dans le processus et sans outil.
///
/// <para>
/// La visite du 2026-10-06 comptait 212 GC de génération 2 pour 10 de génération 0
/// pendant les animations : une proportion qui ne vient pas de l'allocation
/// ordinaire. La raison (allocation, GC provoqué, mémoire faible, gros objet…)
/// dit d'où ils viennent (n° 46).
/// </para>
/// </summary>
internal sealed class GcReasons : EventListener
{
    private const string RuntimeSource = "Microsoft-Windows-DotNETRuntime";
    private const EventKeywords GcKeyword = (EventKeywords)0x1;

    private readonly object _lock = new();
    private readonly Dictionary<(uint Depth, uint Reason), int> _counts = [];

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (eventSource.Name == RuntimeSource)
        {
            EnableEvents(eventSource, EventLevel.Informational, GcKeyword);
        }
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        if (eventData.EventName is not { } name || !name.StartsWith("GCStart", StringComparison.Ordinal) || eventData.Payload is null)
        {
            return;
        }

        uint depth = Read(eventData, "Depth");
        uint reason = Read(eventData, "Reason");

        lock (_lock)
        {
            _counts[(depth, reason)] = _counts.GetValueOrDefault((depth, reason)) + 1;
        }
    }

    /// <summary>Résumé depuis le dernier appel, puis remise à zéro : « g2 provoqué 12, g0 allocation 3 ».</summary>
    public string Drain()
    {
        lock (_lock)
        {
            if (_counts.Count == 0)
            {
                return string.Empty;
            }

            string summary = string.Join(", ", _counts
                .OrderByDescending(c => c.Value)
                .Select(c => $"g{c.Key.Depth} {ReasonName(c.Key.Reason)} {c.Value}"));
            _counts.Clear();
            return summary;
        }
    }

    private static uint Read(EventWrittenEventArgs data, string field)
    {
        int index = data.PayloadNames?.IndexOf(field) ?? -1;
        return index >= 0 && data.Payload![index] is { } value ? Convert.ToUInt32(value, System.Globalization.CultureInfo.InvariantCulture) : uint.MaxValue;
    }

    // Raisons de GCStart (événements du runtime .NET, champ Reason).
    private static string ReasonName(uint reason) => reason switch
    {
        0 => "allocation",
        1 => "provoqué",
        2 => "mémoire faible",
        3 => "vide",
        4 => "gros objet",
        5 => "alloc. petite (oom)",
        6 => "alloc. grosse (oom)",
        7 => "provoqué non forcé",
        8 => "provoqué (bas niveau)",
        9 => "pression mémoire",
        10 => "provoqué compactant",
        11 => "suspension",
        12 => "région sans place",
        _ => $"raison {reason}"
    };
}
