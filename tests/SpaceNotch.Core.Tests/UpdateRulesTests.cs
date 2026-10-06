using System;
using System.Linq;
using SpaceNotch.Core.Setup;
using SpaceNotch.Core.Update;
using SpaceNotch.Infrastructure.Config;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>
/// Mise à jour automatique (v1.17.0) : lecture de la release, empreinte,
/// décision d'installer, relance après l'installation.
/// </summary>
public sealed class UpdateRulesTests
{
    private const string Download = "https://github.com/nicoo-o/SpaceNotch/releases/download/v1.17.0/";

    private static string Release(
        string tag = "v1.17.0",
        bool draft = false,
        bool prerelease = false,
        string setupUrl = Download + "SpaceNotch-Setup.exe",
        long size = 80_000_000,
        bool sums = true)
        => $$"""
        {
          "tag_name": "{{tag}}",
          "draft": {{(draft ? "true" : "false")}},
          "prerelease": {{(prerelease ? "true" : "false")}},
          "html_url": "https://github.com/nicoo-o/SpaceNotch/releases/tag/{{tag}}",
          "assets": [
            { "name": "SpaceNotch-Setup.exe", "size": {{size}}, "browser_download_url": "{{setupUrl}}" }
            {{(sums ? $", {{ \"name\": \"SHA256SUMS.txt\", \"size\": 300, \"browser_download_url\": \"{Download}SHA256SUMS.txt\" }}" : "")}}
          ]
        }
        """;

    [Theory]
    [InlineData("v1.17.0", "1.17.0")]
    [InlineData("1.17", "1.17.0")]
    [InlineData("V2.0.3-beta.1", "2.0.3")]
    [InlineData("v1.16.1+build", "1.16.1")]
    public void L_etiquette_donne_une_version_a_trois_chiffres(string tag, string expected)
        => Assert.Equal(Version.Parse(expected), UpdateRules.ParseTag(tag));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nightly")]
    public void Une_etiquette_illisible_ne_donne_rien(string? tag)
        => Assert.Null(UpdateRules.ParseTag(tag));

    [Fact]
    public void Seule_une_version_strictement_plus_recente_compte()
    {
        Assert.True(UpdateRules.IsNewer(new Version(1, 17, 0), new Version(1, 16, 1)));
        Assert.True(UpdateRules.IsNewer(new Version(1, 16, 1), new Version(1, 16, 0, 0)));
        Assert.False(UpdateRules.IsNewer(new Version(1, 16, 1), new Version(1, 16, 1, 0)));
        Assert.False(UpdateRules.IsNewer(new Version(1, 15, 9), new Version(1, 16, 0)));
    }

    [Fact]
    public void Une_release_publiee_donne_l_installeur_et_les_empreintes()
    {
        ReleaseInfo? release = UpdateRules.ParseRelease(Release());

        Assert.NotNull(release);
        Assert.Equal(new Version(1, 17, 0), release.Version);
        Assert.Equal(Download + "SpaceNotch-Setup.exe", release.SetupUrl);
        Assert.Equal(Download + "SHA256SUMS.txt", release.ChecksumsUrl);
        Assert.Equal(80_000_000, release.SetupSize);
        Assert.StartsWith("https://github.com/nicoo-o/SpaceNotch/", release.PageUrl);
    }

    [Fact]
    public void Brouillon_preversion_lien_etranger_et_taille_invraisemblable_sont_refuses()
    {
        Assert.Null(UpdateRules.ParseRelease(Release(draft: true)));
        Assert.Null(UpdateRules.ParseRelease(Release(prerelease: true)));
        Assert.Null(UpdateRules.ParseRelease(Release(setupUrl: "https://example.com/nicoo-o/SpaceNotch/releases/download/v1.17.0/SpaceNotch-Setup.exe")));
        Assert.Null(UpdateRules.ParseRelease(Release(setupUrl: "http://github.com/nicoo-o/SpaceNotch/releases/download/v1.17.0/SpaceNotch-Setup.exe")));
        Assert.Null(UpdateRules.ParseRelease(Release(setupUrl: "https://github.com/someone/else/releases/download/v1.17.0/SpaceNotch-Setup.exe")));
        Assert.Null(UpdateRules.ParseRelease(Release(size: 0)));
        Assert.Null(UpdateRules.ParseRelease(Release(size: UpdateRules.MaximumSetupSize + 1)));
        Assert.Null(UpdateRules.ParseRelease("{ pas du json"));
        Assert.Null(UpdateRules.ParseRelease(""));
    }

    [Fact]
    public void Sans_fichier_d_empreintes_la_release_reste_lisible()
        => Assert.Null(UpdateRules.ParseRelease(Release(sums: false))!.ChecksumsUrl);

    [Fact]
    public void L_empreinte_est_trouvee_par_le_nom_du_fichier()
    {
        string setup = new('A', 64);
        string other = new('b', 64);
        string sums = $"{other}  SpaceNotch.exe\r\n{setup} *SpaceNotch-Setup.exe\r\n";

        Assert.Equal(new string('a', 64), UpdateRules.FindChecksum(sums, "SpaceNotch-Setup.exe"));
        Assert.Null(UpdateRules.FindChecksum(sums, "SpaceNotch.zip"));
        Assert.Null(UpdateRules.FindChecksum("zz  SpaceNotch-Setup.exe", "SpaceNotch-Setup.exe"));
        Assert.Null(UpdateRules.FindChecksum(new string('g', 64) + "  SpaceNotch-Setup.exe", "SpaceNotch-Setup.exe"));
        Assert.Null(UpdateRules.FindChecksum(null, "SpaceNotch-Setup.exe"));
    }

    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    private static UpdateAction Decide(
        UpdateMode mode = UpdateMode.Automatic,
        bool downloaded = true,
        bool installed = true,
        double idleMinutes = 10,
        bool busy = false,
        DateTimeOffset? postponed = null)
        => UpdateRules.Decide(mode, downloaded, installed, TimeSpan.FromMinutes(idleMinutes), busy, postponed, Now);

    [Fact]
    public void Desactivees_rien_ne_se_passe()
        => Assert.Equal(UpdateAction.None, Decide(UpdateMode.Off));

    [Fact]
    public void Automatique_installe_au_calme_seulement()
    {
        Assert.Equal(UpdateAction.Install, Decide());
        Assert.Equal(UpdateAction.Wait, Decide(idleMinutes: 1));
        Assert.Equal(UpdateAction.Wait, Decide(busy: true));
        Assert.Equal(UpdateAction.Download, Decide(downloaded: false));
    }

    [Fact]
    public void Me_prevenir_propose_et_Plus_tard_se_tait_jusqu_a_l_echeance()
    {
        Assert.Equal(UpdateAction.Offer, Decide(UpdateMode.Notify, busy: true));
        Assert.Equal(UpdateAction.None, Decide(UpdateMode.Notify, postponed: Now.AddHours(1)));
        Assert.Equal(UpdateAction.Offer, Decide(UpdateMode.Notify, postponed: Now.AddHours(-1)));
    }

    [Fact]
    public void Portable_la_proposition_mene_a_la_page()
    {
        Assert.Equal(UpdateAction.Offer, Decide(installed: false));
        Assert.Equal(UpdateAction.None, Decide(installed: false, postponed: Now.AddHours(1)));
    }

    [Fact]
    public void Mise_a_jour_faite_seulement_quand_la_version_a_monte()
    {
        Assert.True(UpdateRules.JustUpdated("1.16.1", new Version(1, 17, 0)));
        Assert.False(UpdateRules.JustUpdated("1.17.0", new Version(1, 17, 0, 0)));
        Assert.False(UpdateRules.JustUpdated(null, new Version(1, 17, 0)));
        Assert.Equal("1.17.0", UpdateRules.Display(new Version(1, 17, 0, 0)));
    }

    [Fact]
    public void L_installation_silencieuse_demande_la_relance()
    {
        var options = new InstallOptions(InstallScope.CurrentUser, true, true);
        var command = new SetupCommand(SetupMode.Install, options, Quiet: true, Relaunch: true);

        SetupCommand parsed = SetupCommand.Parse(command.ToArguments(), null);

        Assert.Equal(SetupMode.Install, parsed.Mode);
        Assert.True(parsed.Quiet);
        Assert.True(parsed.Relaunch);
        Assert.False(SetupCommand.Parse(new SetupCommand(SetupMode.Install, options, Quiet: true).ToArguments(), null).Relaunch);
    }

    [Fact]
    public void Les_mises_a_jour_sont_automatiques_par_defaut_et_un_mode_inconnu_y_revient()
    {
        Assert.Equal(UpdateMode.Automatic, new AppSettings().UpdateMode);

        var settings = new AppSettings { UpdateMode = (UpdateMode)42 };
        settings.Sanitize();
        Assert.Equal(UpdateMode.Automatic, settings.UpdateMode);
    }
    // Copie de développement (2026-10-04) : un build lancé depuis bin a trouvé
    // l'installation dans le registre, l'a mise à jour par-dessus et a relancé
    // la notch de l'utilisateur. Seule la copie installée se met à jour.

    [Fact]
    public void La_copie_installee_se_met_a_jour()
        => Assert.True(UpdateRules.MayUpdateItself(
            @"C:\Users\x\AppData\Local\Programs\SpaceNotch\SpaceNotch.exe",
            @"c:\users\x\appdata\local\programs\spacenotch\SpaceNotch.exe"));

    [Fact]
    public void Une_copie_de_developpement_ne_touche_pas_a_l_installation()
        => Assert.False(UpdateRules.MayUpdateItself(
            @"C:\dev\SpaceNotch\src\SpaceNotch.App\bin\x64\Release\SpaceNotch.App.exe",
            @"C:\Users\x\AppData\Local\Programs\SpaceNotch\SpaceNotch.exe"));

    [Fact]
    public void Sans_installation_la_version_portable_garde_sa_surveillance()
        => Assert.True(UpdateRules.MayUpdateItself(@"D:\Outils\SpaceNotch\SpaceNotch.exe", installedExecutable: null));

    [Fact]
    public void Un_chemin_d_executable_inconnu_ne_met_rien_a_jour()
        => Assert.False(UpdateRules.MayUpdateItself(runningExecutable: null, @"C:\Programs\SpaceNotch\SpaceNotch.exe"));

    // Mise à jour ratée (n° 7) : l'installeur ferme la notch avant de copier.
    // Un échec, ou l'élévation refusée, la laissait fermée sans un mot.

    [Fact]
    public void Apres_une_installation_reussie_la_nouvelle_version_repart()
        => Assert.Equal(InstallFollowUp.Relaunch, UpdateRules.AfterInstall(exitCode: 0, executableExists: true));

    [Theory]
    [InlineData(1602)]
    [InlineData(1603)]
    public void Apres_un_echec_l_ancienne_version_repart_et_le_dit(int code)
        => Assert.Equal(InstallFollowUp.RelaunchReportingFailure, UpdateRules.AfterInstall(code, executableExists: true));

    [Fact]
    public void Sans_executable_rien_ne_repart()
        => Assert.Equal(InstallFollowUp.None, UpdateRules.AfterInstall(1603, executableExists: false));

    [Fact]
    public void L_argument_d_echec_porte_le_code_et_se_relit()
    {
        string argument = UpdateRules.FailureArgument(1602);

        Assert.Equal(1602, UpdateRules.ParseFailure(["SpaceNotch.exe", argument]));
        Assert.Null(UpdateRules.ParseFailure(["SpaceNotch.exe", "--startup"]));
        Assert.Null(UpdateRules.ParseFailure(["SpaceNotch.exe", "--update-failed=abc"]));
    }

    [Fact]
    public void L_elevation_refusee_se_distingue_de_l_echec()
    {
        Assert.True(UpdateRules.IsDeclined(1602));
        Assert.False(UpdateRules.IsDeclined(1603));
    }

    [Fact]
    public void Apres_un_echec_l_automatique_attend_l_echeance_au_lieu_de_reessayer_au_calme()
    {
        // Sans cela, une élévation refusée redemandait l'autorisation au moment
        // calme suivant, puis au suivant.
        Assert.Equal(UpdateAction.Wait, Decide(postponed: Now.AddHours(1)));
        Assert.Equal(UpdateAction.Install, Decide(postponed: Now.AddHours(-1)));
    }

    [Fact]
    public void La_carte_d_echec_dit_que_la_notch_reste_dans_sa_version()
    {
        var manager = new SpaceNotch.Core.Activities.ActivityManager();
        var feature = new SpaceNotch.Features.Update.UpdateFeature(manager, new SpaceNotch.Core.Events.EventBus(), isEnabled: true);

        feature.ShowFailed(new Version(1, 17, 1), declined: true);
        SpaceNotch.Core.Activities.IslandActivity refused = manager.GetActiveActivities().Single(a => a.Id == SpaceNotch.Features.Update.UpdateFeature.ActivityId);

        feature.ShowFailed(new Version(1, 17, 1), declined: false);
        SpaceNotch.Core.Activities.IslandActivity failed = manager.GetActiveActivities().Single(a => a.Id == SpaceNotch.Features.Update.UpdateFeature.ActivityId);

        Assert.Contains("1.17.1", refused.Subtitle, StringComparison.Ordinal);
        Assert.NotEqual(refused.Subtitle, failed.Subtitle);
        Assert.Contains(failed.Actions, a => a.Id == SpaceNotch.Features.Update.UpdateFeature.NotesAction);
    }
}
