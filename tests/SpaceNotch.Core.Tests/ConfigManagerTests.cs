using System;
using System.IO;
using SpaceNotch.Core.Features;
using SpaceNotch.Infrastructure.Config;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>
/// Vérifie la persistance réelle de la configuration, sur un vrai fichier placé
/// dans un répertoire temporaire.
///
/// Ces tests existent parce que la configuration était intégralement inerte :
/// la sérialisation JSON par réflexion est désactivée dans une application WinUI
/// élaguée, et l'échec était absorbé par la tolérance d'écriture. Le résultat
/// était une configuration jamais lue, jamais écrite, et aucune alerte. Exercer
/// le chemin de fichier réel est donc la seule manière fiable de détecter cette
/// panne.
/// </summary>
public class ConfigManagerTests : IDisposable
{
    private readonly string _directory;

    public ConfigManagerTests()
    {
        _directory = Path.Combine(
            Path.GetTempPath(),
            "spacenotch-tests",
            Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private ConfigManager CreateManager() => new(_directory);

    [Fact]
    public void TestEnvironment_ReproducesTheApplicationsJsonConstraint()
    {
        // L'application tourne sans sérialisation par réflexion, parce que
        // l'élagage du toolchain WinUI la désactive. Si les tests cessaient de
        // reproduire cette contrainte, un retour à une sérialisation réflexive
        // passerait ici et échouerait dans l'application — exactement l'écart qui
        // avait rendu la configuration inerte sans aucun signe.
        bool declared = AppContext.TryGetSwitch(
            "System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault",
            out bool reflectionEnabled);

        // Déclaré explicitement, et non simplement absent : TryGetSwitch renvoie
        // faux dans les deux cas, donc les deux assertions sont nécessaires.
        Assert.True(declared, "La contrainte JSON n'est plus déclarée pour les tests.");
        Assert.False(reflectionEnabled);
    }

    [Fact]
    public void Save_WritesTheFileAndLeavesNoFailureSignal()
    {
        var manager = CreateManager();
        var failures = 0;
        manager.WriteFailed = (_, _) => failures++;

        manager.Save(new AppSettings());

        // C'est l'assertion centrale : sans elle, la panne silencieuse passerait
        // pour un succès.
        Assert.Equal(0, failures);
        Assert.True(File.Exists(manager.ConfigFilePath));
    }

    [Fact]
    public void Settings_SurviveAFullRoundTrip()
    {
        var manager = CreateManager();

        manager.Save(new AppSettings
        {
            Appearance = IslandAppearance.Light,
            CornerRadiusBottom = 14,
            SpringBounce = 0.6,
            ShowBluetooth = false,
            ShowClipboard = true
        });

        AppSettings loaded = CreateManager().Load();

        Assert.Equal(IslandAppearance.Light, loaded.Appearance);
        Assert.Equal(14, loaded.CornerRadiusBottom);
        Assert.Equal(0.6, loaded.SpringBounce);
        Assert.False(loaded.ShowBluetooth);
        Assert.True(loaded.ShowClipboard);
    }

    [Fact]
    public void FeaturePreferences_SurviveAFullRoundTrip()
    {
        var manager = CreateManager();
        var settings = new AppSettings();

        settings.BindFeature(FeatureKeys.Media, enabled: false);
        settings.BindFeature(FeatureKeys.Clipboard, enabled: true);

        manager.Save(settings);
        AppSettings loaded = CreateManager().Load();

        // C'est exactement ce que l'utilisateur attend d'une bascule dans le menu :
        // elle doit tenir après un redémarrage.
        Assert.False(loaded.IsFeatureEnabled(FeatureKeys.Media));
        Assert.True(loaded.IsFeatureEnabled(FeatureKeys.Clipboard));
    }

    [Fact]
    public void Load_CreatesTheFileWithDefaultsWhenMissing()
    {
        var manager = CreateManager();

        AppSettings settings = manager.Load();

        Assert.True(File.Exists(manager.ConfigFilePath));
        Assert.True(settings.IsFeatureEnabled(FeatureKeys.Media));
        Assert.False(settings.IsFeatureEnabled(FeatureKeys.Clipboard));
    }

    [Fact]
    public void Load_IsReadableAndEditableByHand()
    {
        var manager = CreateManager();

        // Les énumérations sont écrites en clair, et relues sans tenir compte de
        // la casse : le fichier doit rester modifiable à la main.
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            manager.ConfigFilePath,
            """{ "appearance": "light", "showclipboard": true, "cornerradiusbottom": 14 }""");

        AppSettings loaded = manager.Load();

        Assert.Equal(IslandAppearance.Light, loaded.Appearance);
        Assert.True(loaded.ShowClipboard);
        Assert.Equal(14, loaded.CornerRadiusBottom);
    }

    [Fact]
    public void Load_OnCorruptedFile_FallsBackAndReports()
    {
        var manager = CreateManager();

        Directory.CreateDirectory(_directory);
        File.WriteAllText(manager.ConfigFilePath, "{ ceci n'est pas du JSON");

        int reported = 0;
        manager.ReadFailed = (_, _) => reported++;

        AppSettings loaded = manager.Load();

        // L'application démarre quand même, mais la panne est signalée.
        Assert.Equal(1, reported);

        // Le repli est comparé à la valeur **déclarée**, et non à un nombre écrit
        // ici : un réglage de référence qui change doit faire échouer les tests qui
        // le contredisent, pas ceux qui le répètent.
        Assert.Equal(new AppSettings().SpringBounce, loaded.SpringBounce);
    }

    [Fact]
    public void Load_ClampsValuesEditedByHand()
    {
        // Une configuration éditée à la main est le cas réel : c'est ce que fait
        // quiconque veut pousser un réglage au-delà de ce que l'interface
        // autorise. Elle doit être ramenée dans les bornes, pas respectée.
        var manager = CreateManager();

        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            manager.ConfigFilePath,
            """{ "CornerRadiusBottom": 9999, "SpringResponseSeconds": -3, "SpringBounce": 40, "Density": 77 }""");

        AppSettings loaded = manager.Load();

        Assert.InRange(loaded.CornerRadiusBottom, 8, 40);
        Assert.InRange(loaded.SpringResponseSeconds, 0.18, 1.20);
        Assert.InRange(loaded.SpringBounce, 0.05, 1.20);

        // Une densité hors énumération doit ramener la valeur de référence : sans
        // cela, l'encombrement des cartes tomberait dans la branche par défaut du
        // choix, ce qui fonctionnerait — mais l'utilisateur ne saurait pas que son
        // réglage est ignoré.
        Assert.Equal(SpaceNotch.Core.Scenes.IslandContentDensity.Comfortable, loaded.Density);
    }

    [Fact]
    public void Load_TranslatesAConfigurationWrittenBeforeTheNewMotionVocabulary()
    {
        // Le cas qui donne son sens à la migration : un utilisateur avait poussé la
        // raideur. Le mouvement qu'il a choisi doit survivre à la réécriture du
        // vocabulaire, sinon la migration ne serait qu'un effacement poli.
        //
        // 300 de raideur, 22 d'amortissement, masse 1 : les identités d'un
        // oscillateur amorti donnent 2π/√300 ≈ 0,363 s et 22/(2√300) ≈ 0,635.
        var manager = CreateManager();

        Directory.CreateDirectory(_directory);
        File.WriteAllText(manager.ConfigFilePath, """{ "SpringStiffness": 300 }""");

        // Le chemin passe par le service, et non par AppSettings.Migrate() : c'est
        // le service qui branche la migration, et un test qui l'appellerait
        // lui-même continuerait de passer si le service cessait de le faire.
        var service = new SettingsService(manager);

        Assert.Equal(0.363, service.Current.SpringResponseSeconds, 3);
        Assert.Equal(0.635, service.Current.SpringBounce, 3);

        // Idempotence, vérifiée sur le fichier réécrit : la migration ramène les
        // anciens champs à leur valeur d'origine, donc une seconde lecture ne
        // remigre rien et ne réécrit pas le mouvement obtenu.
        service.Persist();

        Assert.Equal(0.363, new SettingsService(CreateManager()).Current.SpringResponseSeconds, 3);
    }

    [Fact]
    public void Load_LeavesAFreshConfigurationUntouched()
    {
        // L'autre moitié de la condition : une configuration neuve laisse les
        // anciens champs à leurs valeurs d'origine, donc rien n'est migré et le
        // mouvement enregistré est celui qui s'applique. Sans cette condition, la
        // migration écraserait le réglage de tout le monde.
        var manager = CreateManager();

        Directory.CreateDirectory(_directory);
        File.WriteAllText(manager.ConfigFilePath, """{ "SpringResponseSeconds": 0.30 }""");

        Assert.Equal(0.30, new SettingsService(manager).Current.SpringResponseSeconds, 3);
    }

    [Fact]
    public async System.Threading.Tasks.Task AsyncRoundTrip_MatchesTheSyncOne()
    {
        var manager = CreateManager();

        await manager.SaveAsync(new AppSettings { SpringResponseSeconds = 0.30, SpringBounce = 0.50 });
        AppSettings loaded = await CreateManager().LoadAsync();

        Assert.Equal(0.30, loaded.SpringResponseSeconds, 3);
        Assert.Equal(0.50, loaded.SpringBounce, 3);
    }
}
