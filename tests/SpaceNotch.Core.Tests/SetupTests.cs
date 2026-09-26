using System.Globalization;
using System.Reflection;
using SpaceNotch.Core.Setup;
using Xunit;

namespace SpaceNotch.Core.Tests;

public class SetupCommandTests
{
    [Fact]
    public void Ordinary_launch_is_not_setup()
    {
        SetupCommand command = SetupCommand.Parse([@"C:\Apps\SpaceNotch.exe", "--demo"], @"C:\Apps\SpaceNotch.exe");

        Assert.Equal(SetupMode.None, command.Mode);
    }

    [Theory]
    [InlineData(@"C:\Users\a\Downloads\SpaceNotch-Setup.exe")]
    [InlineData(@"C:\Users\a\Downloads\SpaceNotch-Setup (1).exe")]
    [InlineData(@"C:\Users\a\Downloads\spacenotch_setup.EXE")]
    [InlineData(@"D:\SpaceNotch-Installer.exe")]
    public void Setup_is_recognised_by_its_file_name(string path)
    {
        Assert.Equal(SetupMode.Install, SetupCommand.Parse([path], path).Mode);
    }

    [Fact]
    public void Uninstall_name_is_not_mistaken_for_install()
    {
        Assert.Equal(SetupMode.Uninstall, SetupCommand.ModeFromFileName(@"C:\x\SpaceNotch-Uninstall.exe"));
    }

    [Fact]
    public void Explicit_argument_wins_over_file_name()
    {
        string path = @"C:\x\SpaceNotch-Setup.exe";

        Assert.Equal(SetupMode.Uninstall, SetupCommand.Parse([path, "--uninstall"], path).Mode);
    }

    [Fact]
    public void Defaults_are_just_me_with_startup_and_desktop()
    {
        SetupCommand command = SetupCommand.Parse(["--install"], null);

        Assert.Equal(InstallOptions.Default, command.Options);
        Assert.False(command.Quiet);
    }

    [Fact]
    public void Arguments_round_trip()
    {
        var original = new SetupCommand(
            SetupMode.InstallWorker,
            new InstallOptions(InstallScope.AllUsers, StartWithWindows: false, DesktopShortcut: true),
            Quiet: true,
            RemoveSettings: true);

        SetupCommand parsed = SetupCommand.Parse(original.ToArguments(), null);

        Assert.Equal(original, parsed);
    }

    [Fact]
    public void Unknown_and_blank_arguments_are_ignored()
    {
        SetupCommand command = SetupCommand.Parse(["", "  ", "--what", "--scope=machine", "--install"], null);

        Assert.Equal(SetupMode.Install, command.Mode);
        Assert.Equal(InstallScope.AllUsers, command.Options.Scope);
    }

    [Theory]
    [InlineData("simple", "simple")]
    [InlineData("with space", "\"with space\"")]
    [InlineData("", "\"\"")]
    [InlineData(@"C:\Program Files\", "\"C:\\Program Files\\\\\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    public void Quote_follows_windows_rules(string argument, string expected)
    {
        Assert.Equal(expected, SetupCommand.Quote(argument));
    }
}

public class InstallLayoutTests
{
    private static readonly SystemFolders Folders = new(
        LocalAppData: @"C:\Users\ana\AppData\Local",
        RoamingAppData: @"C:\Users\ana\AppData\Roaming",
        ProgramFiles: @"C:\Program Files",
        UserPrograms: @"C:\Users\ana\AppData\Roaming\Microsoft\Windows\Start Menu\Programs",
        CommonPrograms: @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs",
        UserDesktop: @"C:\Users\ana\Desktop",
        CommonDesktop: @"C:\Users\Public\Desktop",
        Temp: @"C:\Users\ana\AppData\Local\Temp\");

    [Fact]
    public void Just_me_installs_in_the_profile_without_elevation()
    {
        InstallLayout layout = InstallLayout.For(InstallScope.CurrentUser, Folders);

        Assert.Equal(@"C:\Users\ana\AppData\Local\Programs\SpaceNotch", layout.Directory);
        Assert.Equal(@"C:\Users\ana\AppData\Local\Programs\SpaceNotch\SpaceNotch.exe", layout.Executable);
        Assert.Equal(@"C:\Users\ana\Desktop\SpaceNotch.lnk", layout.DesktopShortcut);
        Assert.EndsWith(@"Start Menu\Programs\SpaceNotch.lnk", layout.StartMenuShortcut, StringComparison.Ordinal);
        Assert.StartsWith(@"C:\Users\ana\AppData\Roaming", layout.StartMenuShortcut, StringComparison.Ordinal);
        Assert.False(layout.RequiresElevation);
    }

    [Fact]
    public void Everyone_installs_in_program_files_with_common_shortcuts()
    {
        InstallLayout layout = InstallLayout.For(InstallScope.AllUsers, Folders);

        Assert.Equal(@"C:\Program Files\SpaceNotch", layout.Directory);
        Assert.Equal(@"C:\Users\Public\Desktop\SpaceNotch.lnk", layout.DesktopShortcut);
        Assert.StartsWith(@"C:\ProgramData", layout.StartMenuShortcut, StringComparison.Ordinal);
        Assert.True(layout.RequiresElevation);
    }

    [Fact]
    public void Leftovers_keep_settings_unless_asked()
    {
        InstallLayout layout = InstallLayout.For(InstallScope.CurrentUser, Folders);

        IReadOnlyList<string> kept = layout.LeftoversAfterExit(Folders, removeSettings: false);
        IReadOnlyList<string> all = layout.LeftoversAfterExit(Folders, removeSettings: true);

        Assert.Contains(layout.Directory, kept);
        Assert.Contains(@"C:\Users\ana\AppData\Local\Temp\.net\SpaceNotch", kept);
        Assert.Contains(@"C:\Users\ana\AppData\Local\Temp\.net\SpaceNotch-Setup", kept);
        Assert.DoesNotContain(@"C:\Users\ana\AppData\Roaming\SpaceNotch", kept);
        Assert.Contains(@"C:\Users\ana\AppData\Roaming\SpaceNotch", all);
        Assert.Contains(@"C:\Users\ana\AppData\Local\SpaceNotch\logs", all);

        // Les greffons sont les fichiers de l'utilisateur : jamais effacés.
        Assert.DoesNotContain(all, p => p.EndsWith("plugins", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(@"C:\Users\ana\AppData\Local\SpaceNotch", all);
    }

    [Theory]
    [InlineData(@"C:\a\b", @"C:\a", true)]
    [InlineData(@"C:\a", @"C:\a\", true)]
    [InlineData(@"C:\ab", @"C:\a", false)]
    [InlineData(@"c:\A\B", @"C:\a", true)]
    public void IsUnder_compares_whole_segments(string path, string directory, bool expected)
    {
        Assert.Equal(expected, WindowsPath.IsUnder(path, directory));
    }
}

public class UninstallEntryTests
{
    private static readonly InstallLayout Layout = new(
        InstallScope.CurrentUser,
        @"C:\Users\ana\AppData\Local\Programs\SpaceNotch",
        @"C:\Users\ana\AppData\Local\Programs\SpaceNotch\SpaceNotch.exe",
        @"C:\s\SpaceNotch.lnk",
        @"C:\d\SpaceNotch.lnk");

    [Fact]
    public void Entry_describes_the_installation_for_windows()
    {
        var options = new InstallOptions(InstallScope.CurrentUser, true, false);
        Dictionary<string, RegistryValue> values = UninstallEntry
            .Values(Layout, options, "1.2.0", 300 * 1024 * 1024L, new DateOnly(2026, 9, 24))
            .ToDictionary(v => v.Name);

        Assert.Equal("SpaceNotch", values["DisplayName"].Text);
        Assert.Equal("1.2.0", values["DisplayVersion"].Text);
        Assert.Equal("20260924", values["InstallDate"].Text);
        Assert.Equal(
            "\"C:\\Users\\ana\\AppData\\Local\\Programs\\SpaceNotch\\SpaceNotch.exe\" --uninstall",
            values["UninstallString"].Text);
        Assert.EndsWith("--uninstall --quiet", values["QuietUninstallString"].Text, StringComparison.Ordinal);
        Assert.Equal(300 * 1024, values["EstimatedSize"].Number);
        Assert.Equal(1, values["NoModify"].Number);
    }

    [Fact]
    public void Entry_round_trips_the_choices_for_updates()
    {
        var options = new InstallOptions(InstallScope.AllUsers, StartWithWindows: false, DesktopShortcut: true);
        Dictionary<string, object?> stored = UninstallEntry
            .Values(Layout, options, "1.0.0", 1, new DateOnly(2026, 1, 1))
            .ToDictionary(v => v.Name, v => (object?)v.Text ?? v.Number);

        InstalledProduct? product = UninstallEntry.Read(name => stored.GetValueOrDefault(name), InstallScope.AllUsers);

        Assert.NotNull(product);
        Assert.Equal("1.0.0", product.Version);
        Assert.Equal(Layout.Directory, product.Directory);
        Assert.Equal(options, product.Options);
        Assert.Equal(Layout.Executable, product.Executable);
    }

    [Fact]
    public void Incomplete_entry_is_not_an_installation()
    {
        Assert.Null(UninstallEntry.Read(_ => null, InstallScope.CurrentUser));
        Assert.Null(UninstallEntry.Read(n => n == "DisplayVersion" ? "1.0.0" : null, InstallScope.CurrentUser));
    }
}

public class SetupVersionTests
{
    [Theory]
    [InlineData(null, "1.0.0", InstallKind.Fresh)]
    [InlineData("1.0.0", "1.1.0", InstallKind.Update)]
    [InlineData("1.0", "1.0.0", InstallKind.Reinstall)]
    [InlineData("v1.0.0", "1.0.0+abc123", InstallKind.Reinstall)]
    [InlineData("1.2.0", "1.1.9", InstallKind.Downgrade)]
    [InlineData("1.0.0.0", "1.0.0.1", InstallKind.Update)]
    [InlineData("garbage", "1.0.0", InstallKind.Update)]
    public void Classify(string? installed, string current, InstallKind expected)
    {
        Assert.Equal(expected, SetupVersion.Classify(installed, current));
    }

    [Theory]
    [InlineData("1.0.0.0", "1.0.0")]
    [InlineData("v2.1", "2.1.0")]
    [InlineData("1.0.0-beta.2", "1.0.0")]
    [InlineData("1.2.3.4", "1.2.3.4")]
    public void Display(string text, string expected)
    {
        Assert.Equal(expected, SetupVersion.Display(text));
    }
}

public class SetupTextTests
{
    [Fact]
    public void Language_follows_windows()
    {
        Assert.Same(SetupText.French, SetupText.For(new CultureInfo("fr-CA")));
        Assert.Same(SetupText.English, SetupText.For(new CultureInfo("de-DE")));
        Assert.Same(SetupText.English, SetupText.For(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Every_text_exists_in_both_languages()
    {
        foreach (PropertyInfo property in typeof(SetupText).GetProperties().Where(p => p.PropertyType == typeof(string)))
        {
            Assert.False(string.IsNullOrWhiteSpace((string?)property.GetValue(SetupText.French)), property.Name);
            Assert.False(string.IsNullOrWhiteSpace((string?)property.GetValue(SetupText.English)), property.Name);
        }
    }

    [Fact]
    public void Headline_and_action_follow_what_is_installed()
    {
        SetupText text = SetupText.French;

        Assert.Equal(text.Tagline, text.Headline(InstallKind.Fresh, null, "1.0.0"));
        Assert.Equal("Mise à jour 1.0.0 → 1.1.0", text.Headline(InstallKind.Update, "1.0.0.0", "1.1.0"));
        Assert.Equal(text.Update, text.PrimaryAction(InstallKind.Update));
        Assert.Equal(text.Install, text.PrimaryAction(InstallKind.Fresh));
    }

    [Fact]
    public void Every_step_has_words()
    {
        foreach (InstallStep step in Enum.GetValues<InstallStep>())
        {
            Assert.False(string.IsNullOrWhiteSpace(SetupText.English.For(step)));
        }

        Assert.Equal(1.0, InstallProgress.At(InstallStep.Done));
        Assert.True(InstallProgress.At(InstallStep.Copying) < InstallProgress.At(InstallStep.Shortcuts));
    }

    [Fact]
    public void Progress_within_a_step_stays_between_its_bounds()
    {
        Assert.Equal(InstallProgress.At(InstallStep.Copying), new SetupProgress(InstallStep.Copying, 0).Overall, 6);
        Assert.Equal(InstallProgress.At(InstallStep.Shortcuts), new SetupProgress(InstallStep.Copying, 1).Overall, 6);
        Assert.Equal(1.0, new SetupProgress(InstallStep.Done, 0.3).Overall, 6);

        double half = new SetupProgress(InstallStep.Copying, 0.5).Overall;
        Assert.InRange(half, InstallProgress.At(InstallStep.Copying), InstallProgress.At(InstallStep.Shortcuts));
    }
}

public class SelfDeleteTests
{
    [Fact]
    public void Waits_then_removes_each_folder()
    {
        string arguments = SelfDelete.Arguments([@"C:\Users\ana\AppData\Local\Programs\SpaceNotch\", @"C:\Program Files\SpaceNotch"]);

        Assert.StartsWith("/d /c ping -n 3 127.0.0.1 > nul", arguments, StringComparison.Ordinal);
        Assert.Contains("rd /s /q \"C:\\Users\\ana\\AppData\\Local\\Programs\\SpaceNotch\"", arguments, StringComparison.Ordinal);
        Assert.Contains("rd /s /q \"C:\\Program Files\\SpaceNotch\"", arguments, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"C:\Windows")]
    [InlineData("relative\\path")]
    [InlineData("C:\\bad\"quote\\x")]
    [InlineData("")]
    public void Never_removes_a_root_or_a_suspicious_path(string directory)
    {
        Assert.DoesNotContain("rd ", SelfDelete.Arguments([directory]), StringComparison.Ordinal);
    }
}
