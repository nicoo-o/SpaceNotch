using System;
using System.Collections.Generic;
using System.Linq;
using SpaceNotch.Core.Accessibility;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.Setup;
using SpaceNotch.Infrastructure.Config;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>
/// Phase C de l'audit d'octobre : accessibilité, réglages et installeur.
/// </summary>
public sealed class PhaseCTests
{
    // ---------------- Recherche des réglages ----------------

    [Theory]
    [InlineData("luminosite", true)]
    [InlineData("LUMINOSITÉ écran", true)]
    [InlineData("brightness", true)]
    [InlineData("screen bright", true)]
    [InlineData("volume", false)]
    [InlineData("", true)]
    public void La_recherche_trouve_une_carte_dans_les_deux_langues_sans_accents(string query, bool found)
    {
        string index = SettingsSearch.Index(["Luminosité de l'écran", "Screen brightness", "Règle l'écran interne."]);

        Assert.Equal(found, SettingsSearch.Matches(index, query));
    }

    // ---------------- Encoche de la caméra ----------------

    [Fact]
    public void L_encoche_agrandit_la_forme_au_repos_juste_assez()
    {
        var rest = new IslandFootprint(120, 22);

        IslandFootprint covered = CameraCutout.Cover(rest, CameraCutout.WidthFor(custom: false, 0));
        Assert.Equal(CameraCutout.DefaultWidth, covered.Width, 6);
        Assert.Equal(CameraCutout.Height, covered.Height, 6);

        // Plus large que l'encoche : rien ne change.
        var wide = new IslandFootprint(320, 40);
        Assert.Equal(wide, CameraCutout.Cover(wide, 200));

        // Personnalisée : bornée.
        Assert.Equal(CameraCutout.MaximumWidth, CameraCutout.WidthFor(custom: true, 9999), 6);
        Assert.Equal(CameraCutout.MinimumWidth, CameraCutout.WidthFor(custom: true, 10), 6);
        Assert.Equal(CameraCutout.DefaultWidth, CameraCutout.WidthFor(custom: true, double.NaN), 6);
    }

    [Fact]
    public void Gauche_et_droite_qui_ne_faisaient_rien_deviennent_l_encoche_ordinaire()
    {
        var settings = new AppSettings { CutoutMode = CameraCutoutMode.Left, CutoutWidth = 5000 };
        settings.Sanitize();

        Assert.Equal(CameraCutoutMode.Center, settings.CutoutMode);
        Assert.Equal(CameraCutout.MaximumWidth, settings.CutoutWidth, 6);
    }

    // ---------------- Apparence, diagnostics ----------------

    [Theory]
    [InlineData(IslandAppearance.Dark, true, false)]
    [InlineData(IslandAppearance.Light, false, true)]
    [InlineData(IslandAppearance.Auto, true, true)]
    [InlineData(IslandAppearance.Auto, false, false)]
    public void Automatique_suit_le_theme_de_Windows(IslandAppearance appearance, bool systemLight, bool light)
        => Assert.Equal(light, new AppSettings { Appearance = appearance }.UsesLightAppearance(systemLight));

    [Fact]
    public void Les_diagnostics_sont_desactives_par_defaut()
        => Assert.False(new AppSettings().EnableDiagnostics);

    // ---------------- Installeur ----------------

    [Fact]
    public void Les_notifications_Windows_sont_facultatives_et_retenues_pour_les_mises_a_jour()
    {
        var options = new InstallOptions(InstallScope.CurrentUser, true, true, WindowsNotifications: false);
        var command = new SetupCommand(SetupMode.InstallWorker, options, Quiet: true);

        SetupCommand parsed = SetupCommand.Parse(command.ToArguments(), null);
        Assert.False(parsed.Options.WindowsNotifications);

        InstallLayout layout = InstallLayout.For(InstallScope.CurrentUser, new SystemFolders(
            LocalAppData: @"C:\Users\ana\AppData\Local",
            RoamingAppData: @"C:\Users\ana\AppData\Roaming",
            ProgramFiles: @"C:\Program Files",
            UserPrograms: @"C:\Users\ana\AppData\Roaming\Microsoft\Windows\Start Menu\Programs",
            CommonPrograms: @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs",
            UserDesktop: @"C:\Users\ana\Desktop",
            CommonDesktop: @"C:\Users\Public\Desktop",
            Temp: @"C:\Users\ana\AppData\Local\Temp\"));
        Dictionary<string, object?> stored = UninstallEntry
            .Values(layout, options, "1.16.0", 1, new DateOnly(2026, 10, 3))
            .ToDictionary(v => v.Name, v => (object?)v.Text ?? v.Number);

        InstalledProduct? product = UninstallEntry.Read(name => stored.GetValueOrDefault(name), InstallScope.CurrentUser);
        Assert.NotNull(product);
        Assert.False(product.Options.WindowsNotifications);

        // Une installation d'avant la phase C (sans la valeur) garde les notifications.
        stored.Remove("SpaceNotchNotifications");
        Assert.True(UninstallEntry.Read(name => stored.GetValueOrDefault(name), InstallScope.CurrentUser)!.Options.WindowsNotifications);
    }

    [Fact]
    public void L_installeur_ne_promet_plus_l_absence_de_droits_d_administrateur()
    {
        Assert.DoesNotContain("administrateur", SetupText.French.ForMeDetail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("administrator", SetupText.English.ForMeDetail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("autorisation", SetupText.French.WindowsNotificationsDetail, StringComparison.Ordinal);
    }
}
