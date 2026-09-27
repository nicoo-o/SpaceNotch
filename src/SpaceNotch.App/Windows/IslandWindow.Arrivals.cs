using System.Collections.Generic;
using System.Linq;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Motion;
using SpaceNotch_App.Views;

namespace SpaceNotch_App.Windows;

/// <summary>
/// Arrivées animées (F1, F12) : à la connexion d'un appareil, son icône joue
/// sa petite scène en pixels ; au branchement du chargeur, la batterie se
/// remplit jusqu'au niveau réel puis laisse place à l'éclair. Une fois par
/// activité : une republication (la batterie qui change d'un point) ne rejoue
/// rien.
/// </summary>
public sealed partial class IslandWindow
{
    private readonly HashSet<string> _arrivalsPlayed = [];

    private void PlayArrival(IslandActivity activity, GlyphView glyph)
    {
        IReadOnlyList<bool[]>? frames = activity.Payload switch
        {
            ChargePayload charge => DeviceAnimation.Charging(charge.Percent),
            BluetoothPayload { IsConnected: true } device => DeviceAnimation.Connect(device.Kind),
            _ => null
        };

        if (frames is null)
        {
            return;
        }

        // Une activité partie peut revenir (l'appareil rebranché) : elle rejouera.
        HashSet<string> active = _activityManager.GetActiveActivities().Select(a => a.Id).ToHashSet();
        _arrivalsPlayed.RemoveWhere(id => !active.Contains(id));

        if (_arrivalsPlayed.Add(activity.Id))
        {
            glyph.Play(frames, DeviceAnimation.FrameMilliseconds);
        }
    }
}
