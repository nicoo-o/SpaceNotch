using System;
using System.Linq;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Channel;
using SpaceNotch.Core.Events;
using SpaceNotch.Core.Motion;
using SpaceNotch.Features.Channel;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>Clawd, la mascotte de Claude Code, dans la notch (I4).</summary>
public class ClawdTests
{
    [Fact]
    public void TheSilhouette_IsTheTerminalOne_RowsDoubled_WithTwoByTwoEyes()
    {
        // Rangée 0 : le haut du corps, colonnes 3 à 15.
        Assert.True(Enumerable.Range(3, 13).All(c => Clawd.FrontPixel(c, 0)));
        Assert.False(Clawd.FrontPixel(2, 0));

        // Les bras : rangées 4 et 5, de la colonne 1 à 17.
        Assert.True(Clawd.FrontPixel(1, 4) && Clawd.FrontPixel(17, 5));
        Assert.False(Clawd.FrontPixel(0, 4));

        // Deux yeux de 2 × 2.
        foreach ((int c, int r) in new[] { (5, 2), (6, 2), (5, 3), (6, 3), (12, 2), (13, 2), (12, 3), (13, 3) })
        {
            Assert.False(Clawd.FrontPixel(c, r));
        }

        // Quatre pattes de deux pixels.
        foreach (int c in new[] { 3, 5, 13, 15 })
        {
            Assert.True(Clawd.FrontPixel(c, 8) && Clawd.FrontPixel(c, 9));
        }

        Assert.False(Clawd.FrontPixel(9, 9));
    }

    [Theory]
    [InlineData(ClawdMood.Thinking)]
    [InlineData(ClawdMood.Asking)]
    [InlineData(ClawdMood.Done)]
    [InlineData(ClawdMood.Error)]
    public void EveryMood_KeepsClawdInsideTheScene_AndMoves(ClawdMood mood)
    {
        var frames = Enumerable.Range(0, 60).Select(i => Clawd.Frame(mood, i / 16.0)).ToList();

        foreach (var frame in frames)
        {
            Assert.All(frame, p => Assert.InRange(p.X, 0, Clawd.Width - 1));
            Assert.All(frame, p => Assert.InRange(p.Y, 0, Clawd.Height - 1));

            // Clawd est toujours là, en entier ou presque (une patte levée, un œil qui cligne).
            Assert.InRange(frame.Count(p => p.Ink == ClawdInk.Body && p.X >= 2 && p.X <= 23 && p.Y >= 1), 100, 200);
        }

        // Il n'est jamais statique : au moins quatre images distinctes en moins de quatre secondes.
        int distinct = frames.Select(f => string.Join(';', f.OrderBy(p => p.Y).ThenBy(p => p.X).Select(p => $"{p.X},{p.Y},{p.Ink},{p.Alpha:0.00}"))).Distinct().Count();
        Assert.True(distinct >= 4, $"{mood} : {distinct} images distinctes");
    }

    [Fact]
    public void EachMood_SaysItsStateInColour()
    {
        static bool Shows(ClawdMood mood, ClawdInk ink) => Enumerable.Range(0, 40).Any(i => Clawd.Frame(mood, i / 16.0).Any(p => p.Ink == ink));

        Assert.True(Shows(ClawdMood.Asking, ClawdInk.Ask));
        Assert.True(Shows(ClawdMood.Done, ClawdInk.Ok));
        Assert.True(Shows(ClawdMood.Error, ClawdInk.Error));
        Assert.True(Shows(ClawdMood.Thinking, ClawdInk.Thought));
        Assert.False(Shows(ClawdMood.Thinking, ClawdInk.Error));
    }

    [Fact]
    public void AFrame_IsAFunctionOfTime()
        => Assert.Equal(Clawd.Frame(ClawdMood.Done, 0.4), Clawd.Frame(ClawdMood.Done, 0.4));

    [Fact]
    public void ClaudeCode_GetsClawd_OtherAgentsKeepTheGrid()
    {
        var activities = new ActivityManager();
        var feature = new ChannelFeature(activities, new EventBus());

        feature.Receive(new AgentMessage("claude.1", ClaudeHook.AgentName, "SpaceNotch", "Bash · ls", ChannelState.Waiting));
        feature.Receive(new AgentMessage("codex.1", "Codex", null, null, ChannelState.Working));

        var all = activities.GetActiveActivities();
        Assert.Equal(ClawdMood.Asking, Assert.IsType<ClawdPayload>(all.Single(a => a.Id.EndsWith("claude.1", StringComparison.Ordinal)).Payload).Mood);
        Assert.Null(all.Single(a => a.Id.EndsWith("codex.1", StringComparison.Ordinal)).Payload);
    }

    [Theory]
    [InlineData(ChannelState.Working, false, ClawdMood.Thinking)]
    [InlineData(ChannelState.Waiting, true, ClawdMood.Asking)]
    [InlineData(ChannelState.Waiting, false, ClawdMood.Error)]
    [InlineData(ChannelState.Done, false, ClawdMood.Done)]
    [InlineData(ChannelState.Error, false, ClawdMood.Error)]
    public void TheAgentState_PicksTheMood(ChannelState state, bool asks, ClawdMood mood)
        => Assert.Equal(mood, ChannelFeature.MoodOf(state, asks));
}
