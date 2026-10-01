using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using QRCoder;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Events;
using SpaceNotch.Core.Features;
using SpaceNotch.Core.Localization;
using SpaceNotch.Core.Motion;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.Share;
using SpaceNotch.Core.State;
using SpaceNotch.Platform.Windows.Share;

namespace SpaceNotch.Features.Share;

/// <summary>
/// Partage PC → téléphone (F8) : « Partager » sur un fichier de l'étagère
/// affiche un QR code ; le téléphone, sur le même Wi-Fi, télécharge le
/// fichier directement, une fois, dans les dix minutes (ADR-011).
/// </summary>
public sealed class ShareFeature : IslandFeatureBase
{
    public const string FeatureKey = FeatureKeys.Share;

    public const string ActivityId = "feature.share.current";

    public const string CancelAction = "share.cancel";

    private readonly ShareServer _server = new();
    private readonly object _gate = new();
    private SharePayload? _shown;

    public ShareFeature(IActivityManager activities, IEventBus events, bool isEnabled = true)
        : base(FeatureKey, "Partage", activities, events, isEnabled)
    {
        _server.Served += OnServed;
        _server.Stopped += OnStopped;
    }

    /// <summary>Partage <paramref name="path"/> ; faux s'il n'y a pas de réseau local.</summary>
    public bool Share(string path)
    {
        if (!IsEnabled || !File.Exists(path))
        {
            return false;
        }

        string? host = ShareServer.LocalAddress();

        if (host is null)
        {
            PublishActivity(new IslandActivity
            {
                Id = ActivityId,
                FeatureId = FeatureKey,
                SceneKey = IslandSceneCatalog.Card,
                Title = Lang.T("Pas de réseau local", "No local network"),
                Subtitle = Lang.T("Le téléphone doit être sur le même Wi-Fi", "Your phone must be on the same Wi-Fi"),
                IconKey = "Qr",
                State = IslandActivityState.Idle,
                Priority = ActivityPriority.Normal,
                Policy = ActivityPresentationPolicy.Temporary,
                Duration = TimeSpan.FromSeconds(4)
            });
            return false;
        }

        var link = new ShareLink(ShareLink.TokenFrom(RandomNumberGenerator.GetBytes(16)), Path.GetFileName(path), DateTimeOffset.UtcNow);

        lock (_gate)
        {
            _server.Start(link, path, host);
            string url = link.Url(host, _server.Port);
            (bool[] modules, int size) = Encode(url);
            _shown = new SharePayload(url, link.FileName, modules, size, link.ExpiresAt);
        }

        Publish(_shown);
        return true;
    }

    /// <summary>Montre un QR code sans servir de fichier (visite filmée).</summary>
    public void Preview(string url, string fileName)
    {
        (bool[] modules, int size) = Encode(url);
        _shown = new SharePayload(url, fileName, modules, size, DateTimeOffset.UtcNow + ShareLink.Lifetime);
        Publish(_shown);
    }

    /// <summary>Le QR code de <paramref name="text"/>, sans marge, rangée par rangée.</summary>
    public static (bool[] Modules, int Size) Encode(string text)
    {
        using var generator = new QRCodeGenerator();
        using QRCodeData data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        List<System.Collections.BitArray> matrix = data.ModuleMatrix;

        // QRCoder entoure le code d'une marge de quatre modules : la scène
        // dessine la sienne.
        const int Quiet = 4;
        int size = matrix.Count - (2 * Quiet);
        var modules = new bool[size * size];

        for (int r = 0; r < size; r++)
        {
            for (int c = 0; c < size; c++)
            {
                modules[(r * size) + c] = matrix[r + Quiet][c + Quiet];
            }
        }

        return (modules, size);
    }

    public override Task<bool> HandleActionAsync(IslandActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ActionId != CancelAction)
        {
            return Task.FromResult(false);
        }

        _server.Stop();
        RemoveActivity(ActivityId);
        return Task.FromResult(true);
    }

    protected override Task OnStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    protected override Task OnStopAsync()
    {
        _server.Stop();
        RemoveActivity(ActivityId);
        return Task.CompletedTask;
    }

    protected override void OnDisposed() => _server.Dispose();

    private void OnServed()
    {
        if (_shown is not { } shown)
        {
            return;
        }

        PublishActivity(new IslandActivity
        {
            Id = ActivityId,
            FeatureId = FeatureKey,
            SceneKey = IslandSceneCatalog.Card,
            Title = Lang.T("Envoyé", "Sent"),
            Eyebrow = shown.FileName,
            IconKey = "Check",
            State = IslandActivityState.Idle,
            Priority = ActivityPriority.Normal,
            Policy = ActivityPresentationPolicy.Temporary,
            MotionState = ActivityMotionState.Completing,
            Duration = TimeSpan.FromSeconds(4)
        });
        _shown = null;
    }

    private void OnStopped()
    {
        // Expiré sans téléchargement : le QR code n'a plus de sens.
        if (_shown is not null)
        {
            _shown = null;
            RemoveActivity(ActivityId);
        }
    }

    private void Publish(SharePayload payload)
        => PublishActivity(new IslandActivity
        {
            Id = ActivityId,
            FeatureId = FeatureKey,
            SceneKey = IslandSceneCatalog.Share,
            Title = payload.FileName,
            Subtitle = Lang.T("Scanne avec ton téléphone", "Scan with your phone"),
            Source = Lang.T("Partage", "Share"),
            IconKey = "Qr",
            State = IslandActivityState.Idle,
            Priority = ActivityPriority.High,
            Policy = ActivityPresentationPolicy.Passive,
            Duration = ShareLink.Lifetime,
            Payload = payload,
            Actions = [new ActivityAction(CancelAction, Lang.T("Arrêter", "Stop"), "Close")]
        });
}
