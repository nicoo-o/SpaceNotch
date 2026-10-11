using System;
using System.Linq;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Motion;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.Scenes;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>Vague 7 : passage des yeux aux fonctions, Pixel vivant, aide, annulation, file d'attente.</summary>
public class Wave7CoreTests
{
    private static IslandActivity Activity(string scene, string? icon = null, object? payload = null, ActivityMotionState motion = ActivityMotionState.Idle)
        => new()
        {
            Id = "a",
            FeatureId = "f",
            SceneKey = scene,
            Title = "Titre",
            IconKey = icon,
            Payload = payload,
            MotionState = motion
        };

    [Theory]
    [InlineData("media", null, "music")]
    [InlineData("volume-hud", "VolumeHigh", "volume")]
    [InlineData("notification", "Notification", "notification")]
    [InlineData("card", "Download", "download")]
    [InlineData("bluetooth", "Headphones", "headphones")]
    [InlineData("timer", "Timer", "timer")]
    [InlineData("card", "Call", "call")]
    [InlineData("card", "Scooter", "delivery")]
    [InlineData("card", "Calendar", "meeting")]
    [InlineData("card", "Bolt", "charge")]
    [InlineData("card", "Moon", "quiet")]
    [InlineData("card", "Text", "copy")]
    [InlineData("color", null, "color")]
    [InlineData("share", null, "qr")]
    [InlineData("card", "Cpu", "monitor")]
    [InlineData("launcher", null, "search")]
    [InlineData("note", null, "note")]
    [InlineData("card", "Weird", "glyph")]
    public void EachFunction_HasItsRecipe(string scene, string? icon, string key)
        => Assert.Equal(key, PixelHandoff.For(Activity(scene, icon)).Key);

    [Fact]
    public void Payloads_ChooseFirst()
    {
        Assert.Equal("agent", PixelHandoff.For(Activity("card", "Agent", new AgentPayload(AgentMood.Thinking))).Key);
        Assert.Equal("progress", PixelHandoff.For(Activity("card", "Progress", new ProgressStepsPayload([1, 0.5]))).Key);
        Assert.Equal("voice", PixelHandoff.For(Activity("card", "Headphones", new VoicePayload("General", [], false))).Key);
        Assert.Equal("work", PixelHandoff.For(Activity("card", "Info", motion: ActivityMotionState.Working)).Key);
        Assert.Equal("glyph", PixelHandoff.For(Activity("card", "Info")).Key);
    }

    [Fact]
    public void Recipes_AreSane()
    {
        foreach (string scene in new[] { "media", "launcher", "note", "color", "share", "timer" })
        {
            HandoffRecipe r = PixelHandoff.For(Activity(scene));
            Assert.True(PixelHandoff.LeadMilliseconds(r.Lead) <= 520);
        }

        // Un agent (ADR-029) : les yeux de Pixel deviennent ceux de l'avatar, tels quels.
        HandoffRecipe agent = PixelHandoff.For(Activity("card", "Agent", new AgentPayload(AgentMood.Thinking)));
        Assert.Equal(HandoffAnchor.Avatar, agent.Anchor);
        Assert.Equal(HandoffAfter.Merge, agent.After);
        Assert.Equal(PixelAvatar.LeftEyeX, agent.Left.X);
        Assert.Equal(PixelAvatar.RightEyeX, agent.Right.X);
        Assert.Equal(PixelGaze.Shape(PixelMood.Awake).Height, agent.Left.Height);

        // Les yeux arrivent dans la forme de l'humeur : ronds quand l'agent demande.
        HandoffRecipe asking = PixelHandoff.For(Activity("card", "Agent", new AgentPayload(AgentMood.Asking)));
        Assert.Equal(PixelAvatar.Pose(AgentMood.Asking, 0).Eye.Width, asking.Left.Width);
        Assert.Equal(1, asking.Left.Roundness);

        // Les deux-points : l'un au-dessus de l'autre, au même endroit en largeur.
        HandoffRecipe timer = PixelHandoff.For(Activity("timer"));
        Assert.Equal(timer.Left.X, timer.Right.X);
        Assert.True(timer.Left.Y < timer.Right.Y);
        Assert.True(PixelHandoff.ReturnMilliseconds < PixelHandoff.TravelMilliseconds);
    }

    [Theory]
    [InlineData(80, false, 0.2, true, PixelCondition.Normal)]
    [InlineData(10, false, 0.2, true, PixelCondition.Tired)]
    [InlineData(10, true, 0.2, true, PixelCondition.Normal)]
    [InlineData(10, false, 0.95, true, PixelCondition.Hot)]
    [InlineData(10, false, 0.95, false, PixelCondition.Offline)]
    [InlineData(null, false, 0.1, true, PixelCondition.Normal)]
    public void Vitals_PickTheGravestState(int? battery, bool charging, double cpu, bool online, PixelCondition expected)
        => Assert.Equal(expected, PixelVitals.Condition(battery, charging, cpu, online));

    [Fact]
    public void Evening_IsTheHourBeforeSleep()
    {
        Assert.True(PixelVitals.IsEvening(new TimeOnly(22, 41)));
        Assert.False(PixelVitals.IsEvening(new TimeOnly(21, 59)));
        Assert.False(PixelVitals.IsEvening(new TimeOnly(23, 0)));
        Assert.True(PixelVitals.Drowsy.Height < PixelGaze.Shape(PixelMood.Awake).Height);

        for (int i = 0; i < 50; i++)
        {
            TimeSpan next = PixelVitals.NextYawn(i);
            Assert.InRange(next.TotalSeconds, 40, 90);
        }
    }

    [Fact]
    public void Beat_FollowsTheLevel()
    {
        Assert.Equal((0d, 0d), PixelVitals.Beat(1.1, 0));
        double highest = Enumerable.Range(0, 100).Select(i => -PixelVitals.Beat(i / 100.0, 1).Offset).Max();
        double soft = Enumerable.Range(0, 100).Select(i => -PixelVitals.Beat(i / 100.0, 0.1).Offset).Max();
        Assert.InRange(highest, 1.4, 1.6);
        Assert.True(soft < highest);

        EyeShape squashed = PixelVitals.Squashed(PixelGaze.Shape(PixelMood.Awake), 1);
        Assert.True(squashed.Width > 8 && squashed.Height < 10);
    }

    [Fact]
    public void Tint_WarmsInTheEvening_AndCoolsInTheMorning()
    {
        Assert.Equal(PixelVitals.Day, PixelVitals.Tint(new TimeOnly(14, 0)));
        Assert.Equal(PixelVitals.Night, PixelVitals.Tint(new TimeOnly(23, 30)));
        Assert.Equal(0.5, PixelVitals.Warmth(new TimeOnly(21, 0)), 3);
        Assert.Equal(0.5, PixelVitals.Warmth(new TimeOnly(7, 30)), 3);
        Assert.Equal(0, PixelVitals.Warmth(new TimeOnly(8, 0)));

        // Jamais de saut d'une minute à l'autre.
        for (int m = 0; m < 24 * 60 - 1; m++)
        {
            double a = PixelVitals.Warmth(new TimeOnly(m / 60, m % 60)), b = PixelVitals.Warmth(new TimeOnly((m + 1) / 60, (m + 1) % 60));
            Assert.True(Math.Abs(a - b) < 0.02, $"{m}");
        }
    }

    [Fact]
    public void GestureHelp_ShowsAtMostThree()
    {
        Assert.Equal(3, GestureHelp.For(null).Count);
        Assert.Contains(GestureHelp.For(Activity(IslandSceneCatalog.Media)), t => t.Effect == "pause");
        Assert.True(GestureHelp.For(Activity("card", "Info")).Count <= GestureHelp.MaxTips);
    }

    [Fact]
    public void Undo_LastsThreeSeconds()
    {
        var shelf = new UndoShelf();
        DateTimeOffset t0 = DateTimeOffset.UnixEpoch;
        IslandActivity a = Activity("card");

        shelf.Offer(a, t0);
        Assert.True(shelf.CanUndo(t0.AddSeconds(2.9)));
        Assert.Equal(0.5, shelf.Remaining(t0.AddSeconds(1.5)), 3);
        Assert.Same(a, shelf.Take(t0.AddSeconds(1)));
        Assert.Null(shelf.Take(t0.AddSeconds(1)));

        shelf.Offer(a, t0);
        Assert.Null(shelf.Take(t0.AddSeconds(3)));
    }

    [Fact]
    public void QueueDots_AreCenteredAndCapped()
    {
        Assert.Equal(0, QueueDots.Count(0));
        Assert.Equal(5, QueueDots.Count(9));
        Assert.Equal(-QueueDots.Offset(0, 3), QueueDots.Offset(2, 3));
        Assert.Equal(0, QueueDots.Offset(1, 3));
    }
}
