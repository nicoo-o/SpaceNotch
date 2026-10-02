using System.Linq;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Channel;
using SpaceNotch.Core.Events;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Features.Channel;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>Les cartes devenues vides sans la trame : ajustées à leur contenu, ou remplies utilement.</summary>
public class EmptyStateTests
{
    private const string Agent = "claude.1";

    private static AgentMessage Working(string detail) => new(Agent, ClaudeHook.AgentName, detail, null, ChannelState.Working);

    [Fact]
    public void ACard_FitsWhatItShows()
    {
        IslandFootprint catalog = IslandSceneCatalog.FootprintFor(IslandSceneCatalog.Card);
        IslandFootprint title = CardFit.For(eyebrow: false, subtitle: null, progress: false, actions: false, ActivityLayout.Card);
        IslandFootprint full = CardFit.For(eyebrow: true, subtitle: "Terminé · 9 s", progress: false, actions: true, ActivityLayout.Card);

        Assert.Equal(catalog.Width, title.Width);
        Assert.Equal(CardFit.Badge + SceneInsets.Top + SceneInsets.Bottom, title.Height);
        Assert.True(full.Height > title.Height);
        Assert.True(full.Height < catalog.Height + 20);

        // Un long sous-titre passe sur deux lignes ; la liste des actions s'ajoute en dessous.
        Assert.True(CardFit.ContentHeight(true, new string('x', 60), false, false, ActivityLayout.Stack)
            > CardFit.ContentHeight(true, "court", false, false, ActivityLayout.Stack));
        Assert.Equal(
            CardFit.ContentHeight(true, "x", false, false, ActivityLayout.Stack) + CardFit.RowGap + (3 * CardFit.RecentLine) + (2 * CardFit.RecentGap),
            CardFit.ContentHeight(true, "x", false, false, ActivityLayout.Stack, recentLines: 3));
    }

    [Fact]
    public async Task AnAgentCard_IsCompact_ThenExpandsOnTap()
    {
        var activities = new ActivityManager();
        var feature = new ChannelFeature(activities, new EventBus());

        feature.Receive(Working("Read sidebar.tsx"));
        feature.Receive(Working("Read sidebar.tsx"));
        feature.Receive(Working("Edit app.tsx"));
        feature.Receive(Working("Bash · npm test"));
        feature.Receive(Working("Edit app.tsx · +2"));

        IslandActivity compact = Assert.Single(activities.GetActiveActivities());
        var clawd = Assert.IsType<ClawdPayload>(compact.Payload);
        Assert.False(clawd.Expanded);
        Assert.Equal(0, clawd.ShownLines);

        // Trois dernières, sans doublon d'affilée, la plus récente en bas.
        Assert.Equal(["Edit app.tsx", "Bash · npm test", "Edit app.tsx · +2"], clawd.Recent);
        Assert.True(compact.Footprint.Height < IslandSceneCatalog.FootprintFor(IslandSceneCatalog.Card).Height);

        Assert.True(await feature.HandleActionAsync(new IslandActionRequest(ChannelFeature.Prefix + Agent, ClawdPayload.ToggleAction)));
        IslandActivity expanded = Assert.Single(activities.GetActiveActivities());
        Assert.Equal(3, Assert.IsType<ClawdPayload>(expanded.Payload).ShownLines);
        Assert.True(expanded.Footprint.Height > compact.Footprint.Height);
        Assert.Equal(compact.TrailingMetric, expanded.TrailingMetric);

        Assert.True(await feature.HandleActionAsync(new IslandActionRequest(ChannelFeature.Prefix + Agent, ClawdPayload.ToggleAction)));
        Assert.Equal(compact.Footprint, Assert.Single(activities.GetActiveActivities()).Footprint);
    }

    [Fact]
    public void AScriptNotification_OffersToOpen_OnlyWithATarget()
    {
        var activities = new ActivityManager();
        var feature = new ChannelFeature(activities, new EventBus());

        feature.Receive(new NotifyMessage("build", "Script terminé", "42 s", "build.ps1", @"C:\logs\build.log", "Ouvrir le journal", ChannelState.Done));
        IslandActivity withTarget = Assert.Single(activities.GetActiveActivities());
        Assert.Equal(["Ouvrir le journal", "Ignorer"], withTarget.Actions.Select(a => a.Label));
        Assert.Equal("build.ps1", withTarget.Eyebrow);

        feature.Receive(new NotifyMessage("build", "Script terminé", "42 s", "build.ps1", null, null, ChannelState.Done));
        IslandActivity plain = Assert.Single(activities.GetActiveActivities());
        Assert.Empty(plain.Actions);
        Assert.True(plain.Footprint.Height < withTarget.Footprint.Height);
    }

    [Fact]
    public async Task Dismiss_RemovesTheNotification()
    {
        var activities = new ActivityManager();
        var feature = new ChannelFeature(activities, new EventBus());
        feature.Receive(new NotifyMessage("build", "Script terminé", null, null, @"C:\logs\build.log", null, ChannelState.Done));

        Assert.True(await feature.HandleActionAsync(new IslandActionRequest(ChannelFeature.Prefix + "build", ChannelFeature.DismissAction)));
        Assert.Empty(activities.GetActiveActivities());
    }

    [Theory]
    [InlineData(@"C:\logs\build.log", true)]
    [InlineData("D:/out/report.html", true)]
    [InlineData(@"\\server\share\log.txt", true)]
    [InlineData("https://ci.example.com/run/42", true)]
    [InlineData(@"\\?\C:\x", false)]
    [InlineData("build.log", false)]
    [InlineData("calc.exe", false)]
    [InlineData("ms-settings:privacy", false)]
    [InlineData("file:///C:/x", false)]
    public void OnlyAbsolutePathsAndWebPages_CanBeOpened(string target, bool accepted)
        => Assert.Equal(accepted, ChannelProtocol.Target(target) is not null);

    [Fact]
    public void Notify_RoundTrips_AndComesFromTheCommandLine()
    {
        var sent = new NotifyMessage("build", "Script terminé", "42 s", "build.ps1", "https://ci.example.com/run/42", "Voir", ChannelState.Error);
        Assert.Equal(sent, ChannelProtocol.Parse(ChannelProtocol.Serialize(sent)));

        ChannelMessage? parsed = ChannelProtocol.FromArguments(["--notify", "--id", "build", "--title", "Fini", "--open", @"C:\logs\b.log", "--done"]);
        var notify = Assert.IsType<NotifyMessage>(parsed);
        Assert.Equal(@"C:\logs\b.log", notify.Open);
        Assert.Equal(ChannelState.Done, notify.State);
    }

    [Fact]
    public void TheHeadsetCard_HasNoBlankBelow()
        => Assert.Equal(40 + SceneInsets.Top + SceneInsets.Bottom, IslandSceneCatalog.FootprintFor(IslandSceneCatalog.Bluetooth).Height);
}
