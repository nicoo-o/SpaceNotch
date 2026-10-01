using System;
using System.Linq;
using System.Text.Json.Nodes;
using SpaceNotch.Core.Calendar;
using SpaceNotch.Core.Capture;
using SpaceNotch.Core.Channel;
using SpaceNotch.Core.Motion;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>Vague 6b : agents IA, progression ouverte, silence de réunion, focus calé, capture de texte, écran de veille.</summary>
public class Wave6bCoreTests
{
    // ---------------- Canal local ----------------

    [Fact]
    public void Progress_RoundTrips_AndIsBounded()
    {
        var sent = new ProgressMessage("build", "Build · SpaceNotch", "Tests", 3, 4, 0.5, ChannelState.Working);
        var back = Assert.IsType<ProgressMessage>(ChannelProtocol.Parse(ChannelProtocol.Serialize(sent)));
        Assert.Equal(sent, back);

        var wild = Assert.IsType<ProgressMessage>(ChannelProtocol.Parse("""{"type":"progress","id":"X","title":"a\u0007b","step":40,"steps":99,"progress":7}"""));
        Assert.Equal("x", wild.Id);
        Assert.Equal("a b", wild.Title);
        Assert.Equal(ChannelProtocol.MaxSteps, wild.Steps);
        Assert.Equal(wild.Steps, wild.Step);
        Assert.Equal(1, wild.Fraction);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("""{"type":"progress"}""")]
    [InlineData("""{"type":"progress","id":"../../etc"}""")]
    [InlineData("""{"type":"launch","id":"a"}""")]
    public void BadLines_AreIgnored(string line) => Assert.Null(ChannelProtocol.Parse(line));

    [Fact]
    public void LongTexts_AreClipped()
    {
        string text = new('é', 300);
        var m = Assert.IsType<AgentMessage>(ChannelProtocol.Parse($$"""{"type":"agent","id":"a","name":"{{text}}","state":"waiting","question":"ok ?"}"""));
        Assert.Equal(ChannelProtocol.MaxText, m.Name.Length);
        Assert.EndsWith("…", m.Name, StringComparison.Ordinal);
        Assert.Equal("ok ?", m.Question);

        var working = Assert.IsType<AgentMessage>(ChannelProtocol.Parse("""{"type":"agent","id":"a","name":"x","question":"ignored"}"""));
        Assert.Null(working.Question);
    }

    [Fact]
    public void CommandLine_BecomesAProgress()
    {
        var p = Assert.IsType<ProgressMessage>(ChannelProtocol.FromArguments(["--progress", "--id", "Build", "--title", "Build", "--step", "3/4", "--label", "Tests", "--percent", "40"]));
        Assert.Equal(("build", 3, 4, 0.4, ChannelState.Working), (p.Id, p.Step, p.Steps, p.Fraction!.Value, p.State));

        Assert.Equal(ChannelState.Done, Assert.IsType<ProgressMessage>(ChannelProtocol.FromArguments(["--id", "b", "--done"])).State);
        Assert.IsType<ClearMessage>(ChannelProtocol.FromArguments(["--id", "b", "--clear"]));
        Assert.Null(ChannelProtocol.FromArguments(["--id", "bad id!"]));
    }

    [Fact]
    public void Steps_FillInOrder()
    {
        var p = new ProgressMessage("b", "B", null, 3, 4, 0.5, ChannelState.Working);
        Assert.Equal([1.0, 1.0, 0.5, 0.0], ProgressSteps.Segments(p));
        Assert.Equal(2.5 / 4, ProgressSteps.Overall(p), 6);
        Assert.Equal("3/4", ProgressSteps.Metric(p));

        var plain = new ProgressMessage("b", "B", null, 0, 0, 0.62, ChannelState.Working);
        Assert.Equal("62 %", ProgressSteps.Metric(plain));
        Assert.Single(ProgressSteps.Segments(plain));

        Assert.Equal(1, ProgressSteps.Overall(p with { State = ChannelState.Done }));
        Assert.Equal("✓", ProgressSteps.Metric(p with { State = ChannelState.Done }));
    }

    // ---------------- Hooks de Claude Code ----------------

    [Fact]
    public void Hooks_BecomeAgentStates()
    {
        var thinking = ClaudeHook.Translate("""{"hook_event_name":"UserPromptSubmit","session_id":"AB12-cd34-ef","cwd":"C:\\dev\\SpaceNotch"}""");
        Assert.False(thinking.AwaitsDecision);
        Assert.Equal(("claude.ab12cd34", "SpaceNotch", ChannelState.Working), (thinking.Message!.Id, thinking.Message.Detail, thinking.Message.State));

        var ask = ClaudeHook.Translate("""{"hook_event_name":"PermissionRequest","session_id":"AB12-cd34-ef","tool_name":"Bash","tool_input":{"command":"dotnet test tests/SpaceNotch.Core.Tests"}}""");
        Assert.True(ask.AwaitsDecision);
        Assert.Equal(ChannelState.Waiting, ask.Message!.State);
        Assert.Equal("Bash · dotnet test tests/SpaceNotch.Core.Tests", ask.Message.Question);

        var edit = ClaudeHook.Translate("""{"hook_event_name":"PermissionRequest","tool_name":"Edit","tool_input":{"file_path":"/home/me/src/Program.cs"}}""");
        Assert.Equal("Edit · Program.cs", edit.Message!.Question);

        Assert.Equal(ChannelState.Done, ClaudeHook.Translate("""{"hook_event_name":"Stop","session_id":"x"}""").Message!.State);
        Assert.Equal(ChannelState.Waiting, ClaudeHook.Translate("""{"hook_event_name":"Notification","notification_type":"idle_prompt"}""").Message!.State);
        Assert.Null(ClaudeHook.Translate("""{"hook_event_name":"Notification","notification_type":"auth_success"}""").Message);
        Assert.Null(ClaudeHook.Translate("""{"hook_event_name":"PreToolUse"}""").Message);
        Assert.Null(ClaudeHook.Translate("{oops").Message);
    }

    [Fact]
    public void Decision_IsWhatClaudeCodeExpects()
    {
        var o = JsonNode.Parse(ClaudeHook.Decision(allow: true))!;
        Assert.Equal("PermissionRequest", (string?)o["hookSpecificOutput"]!["hookEventName"]);
        Assert.Equal("allow", (string?)o["hookSpecificOutput"]!["decision"]!["behavior"]);
        Assert.Equal("deny", (string?)JsonNode.Parse(ClaudeHook.Decision(false))!["hookSpecificOutput"]!["decision"]!["behavior"]);
    }

    [Fact]
    public void Install_AddsOurHooks_KeepsTheRest_AndIsIdempotent()
    {
        const string exe = @"C:\Users\ana\AppData\Local\Programs\SpaceNotch\SpaceNotch.exe";
        const string existing = """
            {"model":"opus","hooks":{"Stop":[{"matcher":"*","hooks":[{"type":"command","command":"say done"}]}]}}
            """;

        string once = ClaudeHook.Install(existing, exe);
        string twice = ClaudeHook.Install(once, exe);
        Assert.Equal(once, twice);
        Assert.True(ClaudeHook.IsInstalled(once));

        var root = JsonNode.Parse(once)!;
        Assert.Equal("opus", (string?)root["model"]);

        foreach (string name in ClaudeHook.Events)
        {
            string text = root["hooks"]![name]!.ToJsonString();
            Assert.Contains("--hook", text, StringComparison.Ordinal);
        }

        Assert.Contains("say done", root["hooks"]!["Stop"]!.ToJsonString(), StringComparison.Ordinal);

        string removed = ClaudeHook.Uninstall(once);
        Assert.False(ClaudeHook.IsInstalled(removed));
        Assert.Contains("say done", removed, StringComparison.Ordinal);
        Assert.Null(JsonNode.Parse(removed)!["hooks"]!["PermissionRequest"]);

        Assert.True(ClaudeHook.IsInstalled(ClaudeHook.Install(null, exe)));
    }

    // ---------------- Silence de réunion, focus calé ----------------

    [Fact]
    public void MeetingQuiet_IsOfferedBeforeTheMeeting_UntilItsEnd()
    {
        var start = new DateTimeOffset(2026, 9, 28, 10, 30, 0, TimeSpan.Zero);
        var end = start.AddMinutes(30);

        Assert.False(MeetingQuiet.Offer(start, end, start.AddMinutes(-20), alreadyQuiet: false));
        Assert.True(MeetingQuiet.Offer(start, end, start.AddMinutes(-4), alreadyQuiet: false));
        Assert.False(MeetingQuiet.Offer(start, end, start.AddMinutes(-4), alreadyQuiet: true));

        Assert.Equal(end, MeetingQuiet.Until(start, end));
        Assert.Equal(start + MeetingQuiet.Fallback, MeetingQuiet.Until(start, start));
        Assert.Equal(start + MeetingQuiet.Longest, MeetingQuiet.Until(start, start.AddHours(9)));
    }

    [Fact]
    public void Focus_EndsBeforeTheNextMeeting()
    {
        var now = new DateTimeOffset(2026, 9, 28, 10, 10, 0, TimeSpan.Zero);
        var twentyFive = TimeSpan.FromMinutes(25);

        Assert.Equal(new FocusFit(twentyFive, false, null, null), FocusPlan.Fit(twentyFive, now, null));
        Assert.False(FocusPlan.Fit(twentyFive, now, now.AddHours(2)).Shortened);

        FocusFit fit = FocusPlan.Fit(twentyFive, now, now.AddMinutes(20).AddSeconds(30), "Point équipe");
        Assert.True(fit.Shortened);
        Assert.Equal(TimeSpan.FromMinutes(18), fit.Duration);
        Assert.Equal("Point équipe", fit.Meeting);

        Assert.Equal(TimeSpan.Zero, FocusPlan.Fit(twentyFive, now, now.AddMinutes(6)).Duration);
    }

    // ---------------- Capture de texte ----------------

    [Fact]
    public void Ocr_JoinsLinesAndHyphens()
    {
        string text = OcrText.Join(["FACTURE  n° 2026-118", "", "Total TTC : 1 284,00 €", "Un point impor-", "tant à noter"]);
        Assert.Equal("FACTURE n° 2026-118\r\nTotal TTC : 1 284,00 €\r\nUn point important à noter", text);
        Assert.Equal(3, OcrText.LineCount(text));
        Assert.Equal("2026-118", OcrText.Join(["2026-", "118"]).Split("\r\n")[0] + OcrText.Join(["2026-", "118"]).Split("\r\n")[1]);
        Assert.Equal("FACTURE n° 2026-118 · Total TTC : 1 284,00 € · Un…", OcrText.Preview(text, 50));
        Assert.Equal((10, 20, 30, 40), OcrText.Selection(40, 60, 10, 20));
        Assert.False(OcrText.IsUsable(4, 100));
    }

    // ---------------- Écran de veille ----------------

    [Fact]
    public void Life_FollowsB3S23_AndIsTheSameEachNight()
    {
        uint seed = LifeGrid.SeedFor(new DateOnly(2026, 9, 28), 61);
        Assert.Equal(seed, LifeGrid.SeedFor(new DateOnly(2026, 9, 28), 61));
        Assert.NotEqual(seed, LifeGrid.SeedFor(new DateOnly(2026, 9, 29), 61));

        var a = new LifeGrid(40, 12, seed);
        var b = new LifeGrid(40, 12, seed);

        for (int i = 0; i < 30; i++)
        {
            a.Step();
            b.Step();
        }

        Assert.Equal(Enumerable.Range(0, 480).Select(i => a.IsAlive(i % 40, i / 40)), Enumerable.Range(0, 480).Select(i => b.IsAlive(i % 40, i / 40)));
    }

    [Fact]
    public void Life_NeverFreezes()
    {
        var grid = new LifeGrid(12, 6, 7);

        for (int i = 0; i < 400; i++)
        {
            grid.Step();
            Assert.True(grid.Population > 0);
        }

        Assert.True(grid.Sowings >= 1);
    }

    [Theory]
    [InlineData(true, 6, false, false, false, true)]
    [InlineData(false, 6, false, false, false, false)]
    [InlineData(true, 4, false, false, false, false)]
    [InlineData(true, 6, true, false, false, false)]
    [InlineData(true, 6, false, true, false, false)]
    [InlineData(true, 6, false, false, true, false)]
    public void Screensaver_OnlyWhenTrulyIdle(bool enabled, int minutes, bool battery, bool fullscreen, bool activity, bool expected)
        => Assert.Equal(expected, ScreensaverPolicy.ShouldRun(enabled, TimeSpan.FromMinutes(minutes), battery, fullscreen, activity));
}
