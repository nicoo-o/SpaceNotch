using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.Scenes;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>
/// Annonces Narrateur : une arrivée se dit une fois, jamais à chaque seconde (n° 1, P11).
/// </summary>
public class AnnouncementTests
{
    private static IslandActivity Timer(string title)
        => new() { Id = "t", FeatureId = "timer", SceneKey = IslandSceneCatalog.Timer, Title = title, Priority = ActivityPriority.Normal };

    [Fact]
    public void ATickingTimer_KeepsTheSameKey_SoNarratorIsNotRepeating()
        => Assert.Equal(Announcement.Key(Timer("12:34")), Announcement.Key(Timer("12:33")));

    [Fact]
    public void ADifferentWording_ChangesTheKey()
        => Assert.NotEqual(Announcement.Key(Timer("12:34")), Announcement.Key(Timer("Temps écoulé")));

    [Fact]
    public void ARepublication_SaysNothing()
        => Assert.Null(Announcement.For(Timer("Temps écoulé"), isNew: false));

    [Fact]
    public void AnArrival_IsAnnouncedWithItsTitle()
        => Assert.Equal("Temps écoulé", Announcement.For(Timer("Temps écoulé"), isNew: true)?.Text);
}
