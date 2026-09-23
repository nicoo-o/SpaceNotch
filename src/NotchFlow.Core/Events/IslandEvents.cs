using System;

namespace NotchFlow.Core.Events;

/// <summary>
/// Instantané d'un média en cours de lecture, volontairement neutre : le cœur ne
/// connaît ni WinRT ni les API Windows. La vignette d'album reste du ressort de
/// la couche plateforme.
/// </summary>
public sealed record MediaTrackSnapshot(
    string Title,
    string Artist,
    string AlbumTitle,
    string AppId,
    bool IsPlaying,
    TimeSpan Position,
    TimeSpan Duration);

/// <summary>Le média en cours a changé, ou sa progression a évolué.</summary>
public sealed record MediaChangedEvent(MediaTrackSnapshot? Track);

/// <summary>Le volume de sortie ou l'état muet a changé.</summary>
public sealed record VolumeChangedEvent(float Volume, bool IsMuted);

/// <summary>Un périphérique Bluetooth s'est connecté ou déconnecté.</summary>
public sealed record BluetoothDeviceChangedEvent(string DeviceName, bool IsConnected, int? BatteryPercent);

/// <summary>Le presse-papier a reçu un nouvel élément.</summary>
public sealed record ClipboardChangedEvent(string Kind, string? Preview);

/// <summary>Une notification applicative a été reçue.</summary>
public sealed record NotificationPostedEvent(string AppName, string Title, string Body);

/// <summary>Une activité a été publiée dans l'Island.</summary>
public sealed record ActivityStartedEvent(string ActivityId, string FeatureId, string SceneKey);

/// <summary>Une activité a quitté l'Island, quelle qu'en soit la raison.</summary>
public sealed record ActivityEndedEvent(string ActivityId, string FeatureId);

/// <summary>
/// La configuration d'affichage a changé. Exprimé en termes neutres afin que le
/// cœur n'ait pas à référencer les constantes de messages Win32.
/// </summary>
public sealed record DisplayChangedEvent(bool TopologyChanged, bool DpiChanged, bool WorkAreaChanged);

/// <summary>
/// Les préférences visuelles du système ont changé : animations, effets de
/// transparence ou contraste élevé. L'Island doit réévaluer son rendu.
/// </summary>
public sealed record SystemVisualStateChangedEvent(
    bool AnimationsEnabled,
    bool TransparencyEffectsEnabled,
    bool HighContrast);
