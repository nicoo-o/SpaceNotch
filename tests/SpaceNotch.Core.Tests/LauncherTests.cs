using System.Globalization;
using SpaceNotch.Core.Launcher;
using Xunit;

namespace SpaceNotch.Core.Tests;

public class LauncherRankingTests
{
    [Fact]
    public void Prefix_beats_word_start_beats_initials_beats_substring()
    {
        int prefix = LauncherRanking.Score("Paint", "pa", out _);
        int word = LauncherRanking.Score("Microsoft Paint", "pa", out _);
        int initials = LauncherRanking.Score("Visual Studio Code", "vsc", out _);
        int substring = LauncherRanking.Score("Spotify", "tif", out _);

        Assert.True(prefix > word);
        Assert.True(word > initials);
        Assert.True(initials > substring);
        Assert.True(substring > 0);
    }

    [Fact]
    public void Accents_and_case_do_not_matter_and_positions_stay_in_the_original()
    {
        int score = LauncherRanking.Score("Paramètres d'écran", "ECRAN", out IReadOnlyList<TextMatch> matches);

        Assert.True(score > 0);
        Assert.Equal(new TextMatch(13, 5), Assert.Single(matches));
    }

    [Fact]
    public void Initials_mark_each_letter()
    {
        LauncherRanking.Score("Visual Studio Code", "vsc", out IReadOnlyList<TextMatch> matches);

        Assert.Equal([new TextMatch(0, 1), new TextMatch(7, 1), new TextMatch(14, 1)], matches);
    }

    [Fact]
    public void No_match_scores_zero()
    {
        Assert.Equal(0, LauncherRanking.Score("Calculatrice", "xyz", out _));
        Assert.Equal(0, LauncherRanking.Score("Calculatrice", "  ", out _));
    }

    [Fact]
    public void Shorter_names_win_at_equal_relevance()
    {
        Assert.True(LauncherRanking.Score("Paint", "paint", out _) > LauncherRanking.Score("Paint 3D Studio Deluxe", "paint", out _));
    }
}

public class InlineCalculatorTests
{
    [Theory]
    [InlineData("12*7+3", 87)]
    [InlineData("12 × 7 + 3", 87)]
    [InlineData("(2+3)*4", 20)]
    [InlineData("2^10", 1024)]
    [InlineData("1,5*2", 3)]
    [InlineData("-4+10", 6)]
    [InlineData("200*15%", 30)]
    [InlineData("10/4", 2.5)]
    public void Evaluates(string text, double expected)
    {
        Assert.True(InlineCalculator.TryEvaluate(text, out double value));
        Assert.Equal(expected, value, 9);
    }

    [Theory]
    [InlineData("12")]
    [InlineData("code")]
    [InlineData("1/0")]
    [InlineData("(1+2")]
    [InlineData("3+")]
    [InlineData("")]
    [InlineData("-5")]
    public void Refuses_what_is_not_a_calculation(string text)
    {
        Assert.False(InlineCalculator.TryEvaluate(text, out _));
    }

    [Fact]
    public void Formats_for_the_culture()
    {
        Assert.Equal("1 234,5", InlineCalculator.Format(1234.5, new CultureInfo("fr-FR")).Replace('\u202F', ' ').Replace('\u00A0', ' '));
        Assert.Equal("0.3333333333", InlineCalculator.Format(1.0 / 3, CultureInfo.InvariantCulture));
    }
}

public class LauncherHistoryTests
{
    [Fact]
    public void Launch_moves_to_front_and_counts()
    {
        var history = new LauncherHistory();
        history.RecordLaunch("a");
        history.RecordLaunch("b");
        history.RecordLaunch("a");

        Assert.Equal(["a", "b"], history.Recents);
        Assert.Equal(2, history.LaunchesOf("A"));
    }

    [Fact]
    public void Recents_are_bounded()
    {
        var history = new LauncherHistory();

        for (int i = 0; i < 20; i++)
        {
            history.RecordLaunch("app" + i);
        }

        Assert.Equal(LauncherHistory.MaxRecents, history.Recents.Count);
        Assert.Equal("app19", history.Recents[0]);
    }

    [Fact]
    public void Pin_toggles()
    {
        var history = new LauncherHistory();

        Assert.True(history.TogglePin("x"));
        Assert.True(history.IsFavorite("X"));
        Assert.False(history.TogglePin("x"));
        Assert.False(history.IsFavorite("x"));
    }
}

public class LauncherSearchTests
{
    private static readonly CultureInfo Fr = new("fr-FR");

    private static readonly LauncherCandidate[] Catalogue =
    [
        new(LauncherResultKind.Application, "Blueprint Studio", "Application", @"C:\apps\blueprint.exe"),
        new(LauncherResultKind.Application, "Calculatrice", "Application", "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"),
        new(LauncherResultKind.Application, "Visual Studio Code", "Application", @"C:\apps\code.exe"),
        new(LauncherResultKind.Setting, "Bluetooth et appareils", "Paramètre Windows", "ms-settings:bluetooth", Keywords: "bluetooth casque"),
        new(LauncherResultKind.Setting, "Réseau et Internet", "Paramètre Windows", "ms-settings:network-status", Keywords: "wifi réseau"),
        new(LauncherResultKind.File, "Blueprint maison.pdf", "Téléchargements", @"C:\Users\a\Downloads\Blueprint maison.pdf")
    ];

    [Fact]
    public void Empty_query_shows_favorites_then_recents_never_the_alphabet()
    {
        var history = new LauncherHistory(favorites: [@"C:\apps\code.exe"], recents: [@"C:\apps\code.exe", "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"]);

        IReadOnlyList<LauncherSection> sections = LauncherSearch.Build("", Catalogue, history, LauncherText.French, Fr);

        Assert.Equal(["Favoris", "Récents"], sections.Select(s => s.Title));
        Assert.Equal("Visual Studio Code", Assert.Single(sections[0].Items).Title);
        Assert.Equal("Calculatrice", Assert.Single(sections[1].Items).Title);
    }

    [Fact]
    public void First_run_shows_recent_files()
    {
        IReadOnlyList<LauncherSection> sections = LauncherSearch.Build("", Catalogue, new LauncherHistory(), LauncherText.French, Fr);

        Assert.Equal("Fichiers récents", Assert.Single(sections).Title);
    }

    [Fact]
    public void Query_groups_results_and_ends_with_the_web()
    {
        IReadOnlyList<LauncherSection> sections = LauncherSearch.Build("blu", Catalogue, new LauncherHistory(), LauncherText.French, Fr);

        Assert.Contains(sections, s => s.Title == "Paramètres" && s.Items[0].Title == "Bluetooth et appareils");
        Assert.Contains(sections, s => s.Title == "Applications" && s.Items[0].Title == "Blueprint Studio");
        Assert.Contains(sections, s => s.Title == "Fichiers récents");
        Assert.Equal("Web", sections[^1].Title);
        Assert.Equal("https://www.bing.com/search?q=blu", sections[^1].Items[0].Target);
    }

    [Fact]
    public void Keywords_find_a_setting()
    {
        IReadOnlyList<LauncherSection> sections = LauncherSearch.Build("wifi", Catalogue, new LauncherHistory(), LauncherText.French, Fr);

        Assert.Contains(sections, s => s.Items.Any(i => i.Target == "ms-settings:network-status"));
    }

    [Fact]
    public void A_calculation_comes_first()
    {
        IReadOnlyList<LauncherSection> sections = LauncherSearch.Build("12*7+3", Catalogue, new LauncherHistory(), LauncherText.French, Fr);

        LauncherResult calc = Assert.Single(sections[0].Items);
        Assert.Equal(LauncherResultKind.Calculation, calc.Kind);
        Assert.Equal("87", calc.Target);
    }

    [Fact]
    public void Usage_breaks_ties()
    {
        var candidates = new LauncherCandidate[]
        {
            new(LauncherResultKind.Application, "Notes A", "Application", "a"),
            new(LauncherResultKind.Application, "Notes B", "Application", "b")
        };

        var history = new LauncherHistory(launches: new Dictionary<string, int> { ["b"] = 5 });
        IReadOnlyList<LauncherSection> sections = LauncherSearch.Build("notes", candidates, history, LauncherText.French, Fr);

        Assert.Equal("Notes B", sections[0].Items[0].Title);
    }

    [Fact]
    public void Actions_depend_on_the_kind()
    {
        var app = new LauncherResult("a", LauncherResultKind.Application, "A", "", "a", []);
        var web = new LauncherResult("w", LauncherResultKind.Web, "W", "", "w", []);

        Assert.Equal(5, LauncherActions.For(app).Count);
        Assert.Equal([LauncherAction.Open], LauncherActions.For(web));
        Assert.True(app.HasActions);
        Assert.False(web.HasActions);
    }

    [Fact]
    public void Every_setting_has_an_ms_settings_uri()
    {
        Assert.All(WindowsSettingsCatalog.All, s => Assert.StartsWith("ms-settings:", s.Uri, StringComparison.Ordinal));
        Assert.Equal(WindowsSettingsCatalog.All.Count, WindowsSettingsCatalog.All.Select(s => s.Uri).Distinct().Count());
    }
}

public class LauncherLayoutTests
{
    [Fact]
    public void Height_follows_the_results_and_is_capped()
    {
        LauncherResult Row(string id) => new(id, LauncherResultKind.Application, id, "", id, []);

        double one = LauncherLayout.HeightFor([new LauncherSection("A", [Row("a")])]);
        double two = LauncherLayout.HeightFor([new LauncherSection("A", [Row("a"), Row("b")])]);
        double many = LauncherLayout.HeightFor([new LauncherSection("A", Enumerable.Range(0, 40).Select(i => Row("r" + i)).ToList())]);

        Assert.Equal(LauncherLayout.Row, two - one);
        Assert.Equal(LauncherLayout.MaxHeight, many);
        Assert.Equal(LauncherLayout.EmptyHeight, LauncherLayout.HeightFor([]));
        Assert.Equal(LauncherLayout.Width, LauncherLayout.FootprintFor([]).Width);
    }

    [Fact]
    public void Actions_panel_makes_room_for_itself()
    {
        LauncherResult row = new("a", LauncherResultKind.Application, "a", "", "a", []);
        IReadOnlyList<LauncherSection> one = [new LauncherSection("A", [row])];

        Assert.True(LauncherLayout.FootprintFor(one).Height < LauncherLayout.ActionsPanelMinHeight);
        Assert.Equal(LauncherLayout.ActionsPanelMinHeight, LauncherLayout.FootprintFor(one, actionsOpen: true).Height);
    }
}

public sealed class QuickMenuLayoutTests
{
    [Fact]
    public void Dock_choices_add_one_row_of_height()
    {
        Assert.Equal(SpaceNotch.Core.Menu.QuickMenuLayout.Height, SpaceNotch.Core.Menu.QuickMenuLayout.HeightFor(false));
        Assert.Equal(
            SpaceNotch.Core.Menu.QuickMenuLayout.DockChoices,
            SpaceNotch.Core.Menu.QuickMenuLayout.HeightFor(true) - SpaceNotch.Core.Menu.QuickMenuLayout.HeightFor(false));
        Assert.True(SpaceNotch.Core.Scenes.IslandSceneCatalog.IsKnown(SpaceNotch.Core.Scenes.IslandSceneCatalog.QuickMenu));
    }
}
