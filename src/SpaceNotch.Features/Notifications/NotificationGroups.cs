using System;
using System.Collections.Generic;
using System.Linq;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.State;
using SpaceNotch.Core.Localization;

namespace SpaceNotch.Features.Notifications;

/// <summary>
/// Regroupement des notifications par application.
///
/// <para>
/// Quatre messages Discord ne sont pas quatre cartes qui se chassent : c'est
/// « Discord 4 ». Chaque application a une activité stable — son identifiant
/// ne dépend que de l'application — que chaque nouvelle notification met à
/// jour. Le groupe se déplie en liste d'expéditeurs quand on ouvre la notch.
/// </para>
///
/// <para>La classe est pure : l'horloge est fournie, ce qui la rend vérifiable.</para>
/// </summary>
public sealed class NotificationGroups
{
    /// <summary>Durée pendant laquelle une notification compte dans son groupe.</summary>
    public static TimeSpan Window { get; } = TimeSpan.FromMinutes(2);

    /// <summary>Durée de présence du groupe dans la notch après la dernière arrivée.</summary>
    /// <summary>Hauteur ajoutée par l'éventail des messages précédents, en DIPs.</summary>
    public const double FanRow = CardFan.CardHeight + 8;

    public static TimeSpan Lifetime { get; } = TimeSpan.FromSeconds(6);

    /// <summary>Plafond d'un groupe : au-delà, les plus anciennes sont oubliées.</summary>
    public const int Capacity = 20;

    /// <summary>Lignes supplémentaires montrées dans la scène ouverte.</summary>
    public const int VisibleHistory = 3;

    private readonly Dictionary<string, List<NotificationItem>> _groups = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Identifiant d'activité stable d'une application.</summary>
    public static string ActivityIdFor(string appName)
        => $"notification.{(string.IsNullOrWhiteSpace(appName) ? "app" : appName.Trim().ToLowerInvariant())}";

    /// <summary>Ajoute une notification et renvoie l'activité de son groupe.</summary>
    public IslandActivity Add(string featureId, string appName, string title, string body, DateTimeOffset now, byte[]? appIcon = null)
    {
        string app = string.IsNullOrWhiteSpace(appName) ? Lang.T("Application", "App") : appName.Trim();

        if (!_groups.TryGetValue(app, out List<NotificationItem>? items))
        {
            items = [];
            _groups[app] = items;
        }

        // Ce qui est trop ancien n'appartient plus à la conversation en cours.
        items.RemoveAll(item => now - item.ReceivedAt > Window);
        items.Insert(0, new NotificationItem(title, body, now));

        if (items.Count > Capacity)
        {
            items.RemoveRange(Capacity, items.Count - Capacity);
        }

        return Build(featureId, app, items, now, appIcon);
    }

    /// <summary>Oublie le groupe d'une application — après lecture, par exemple.</summary>
    public void Clear(string appName) => _groups.Remove(appName.Trim());

    private static IslandActivity Build(string featureId, string app, List<NotificationItem> items, DateTimeOffset now, byte[]? appIcon)
    {
        NotificationItem latest = items[0];
        int count = items.Count;
        int history = Math.Min(count - 1, VisibleHistory);
        IslandFootprint scene = IslandSceneCatalog.FootprintFor(IslandSceneCatalog.Notification);

        return new IslandActivity
        {
            Id = ActivityIdFor(app),
            FeatureId = featureId,
            SceneKey = IslandSceneCatalog.Notification,
            Title = latest.Title,
            Subtitle = latest.Body,
            Eyebrow = app,
            Source = app,
            Metric = count > 1 ? count.ToString(System.Globalization.CultureInfo.CurrentCulture) : null,
            IconKey = "Notification",

            // Le vrai logo de l'application, quand Windows le donne : il prend
            // la place de la cloche, dans la pastille comme dans la scène.
            Artwork = appIcon,
            State = IslandActivityState.Notification,
            Priority = ActivityPriority.Normal,
            Policy = ActivityPresentationPolicy.Temporary,
            CreatedAt = now,
            Duration = Lifetime,

            // Les messages précédents forment un éventail de cartes (S1) : une
            // rangée de plus dès qu'il y en a un.
            ExpandedFootprint = new IslandFootprint(scene.Width, scene.Height + (history > 0 ? FanRow : 0)),
            Payload = new NotificationGroupPayload(app, items.ToArray())
        };
    }
}
