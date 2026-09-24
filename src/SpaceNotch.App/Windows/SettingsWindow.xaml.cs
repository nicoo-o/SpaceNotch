using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SpaceNotch.Core.Features;
using SpaceNotch.Core.Motion;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Infrastructure.Config;
using SpaceNotch.Infrastructure.Logging;
using SpaceNotch.Infrastructure.Plugins;
using SpaceNotch.Platform.Windows.System;
using SpaceNotch_App.Composition;
using Windows.Graphics;
using Windows.UI;
using WinRT.Interop;

namespace SpaceNotch_App.Windows;

/// <summary>
/// Fenêtre de réglages.
///
/// Elle ne détient aucun état : elle lit les préférences, écrit des
/// modifications, et se redessine sur le signal de changement. L'Island est
/// abonnée au même signal, ce qui garantit que l'aperçu et la réalité ne peuvent
/// pas diverger — il n'y a pas deux chemins d'application.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    /// <summary>
    /// Délai de regroupement des écritures, en millisecondes.
    ///
    /// Un curseur déplacé produit des dizaines de valeurs par seconde. L'Island
    /// doit suivre en direct, mais écrire le fichier de configuration à chaque
    /// pixel serait du travail inutile : les écritures sont donc regroupées.
    /// </summary>
    private static readonly TimeSpan PersistDelay = TimeSpan.FromMilliseconds(300);

    private readonly SettingsService _settings;
    private readonly IslandFeatureRegistry _features;
    private readonly Dictionary<string, ToggleSwitch> _featureToggles = new(StringComparer.Ordinal);
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _persistTimer;

    /// <summary>
    /// Vrai tant que les contrôles ne sont pas chargés — et donc dès le premier
    /// instant, avant même <c>InitializeComponent</c>.
    ///
    /// Le verrou doit être posé par défaut, et non activé par
    /// <see cref="LoadFromSettings"/> : la construction du XAML modifie la valeur
    /// des curseurs, ce qui déclenche leurs notifications, qui remontent ici avant
    /// que les préférences soient disponibles. Une notification traitée à cet
    /// instant précis ferait échouer l'ouverture de la fenêtre, avec un message
    /// d'erreur qui désigne le contrôle fautif plutôt que la cause réelle.
    /// </summary>
    private bool _loading = true;

    public SettingsWindow(SettingsService settings, IslandFeatureRegistry features)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(features);

        _settings = settings;
        _features = features;

        InitializeComponent();

        ConfigureWindow();
        BuildFeatureToggles();
        BuildComboItems();
        LoadFromSettings();

        // Minuteur à usage unique : il se désarme après l'écriture. Aucune
        // vérification périodique n'existe donc, même fenêtre ouverte.
        _persistTimer = DispatcherQueue.CreateTimer();
        _persistTimer.IsRepeating = false;
        _persistTimer.Interval = PersistDelay;
        _persistTimer.Tick += (_, _) =>
        {
            _persistTimer.Stop();
            _settings.Persist();
        };

        PathText.Text = _settings.ConfigFilePath;

        // L'aperçu suit exactement le même signal que l'Island : une seule source
        // de vérité, donc aucune possibilité d'afficher autre chose que le réel.
        _settings.Changed += OnSettingsChanged;

        Closed += OnWindowClosed;

        MiniLogger.Log("Fenêtre de réglages ouverte");
    }

    private void ConfigureWindow()
    {
        IntPtr handle = WindowNative.GetWindowHandle(this);
        AppWindow appWindow = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(handle));

        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = false;
            presenter.IsResizable = true;
            presenter.IsMaximizable = false;
        }

        appWindow.Resize(new SizeInt32(560, 820));

        // La fenêtre de réglages se place au centre de l'écran principal : les
        // réglages se lisent, ils ne doivent pas aller chercher l'utilisateur.
        RectInt32 area = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        appWindow.Move(new PointInt32(
            area.X + ((area.Width - appWindow.Size.Width) / 2),
            area.Y + ((area.Height - appWindow.Size.Height) / 2)));
    }

    private void BuildFeatureToggles()
    {
        foreach (IIslandFeature feature in _features.Features)
        {
            // Une fonctionnalité sans préférence persistée ne peut pas être
            // mémorisée : mieux vaut la présenter inerte que laisser croire à une
            // bascule qui oublierait son état au redémarrage.
            bool persisted = AppSettings.HasFeaturePreference(feature.Id);

            var toggle = new ToggleSwitch
            {
                Header = persisted ? feature.DisplayName : $"{feature.DisplayName} (sans préférence)",
                OnContent = "Active",
                OffContent = "Inactive",
                IsEnabled = persisted,
                IsOn = feature.IsEnabled
            };

            IIslandFeature captured = feature;

            toggle.Toggled += async (_, _) => await OnFeatureToggledAsync(captured, toggle);

            _featureToggles[feature.Id] = toggle;
            FeaturesHost.Children.Add(toggle);
        }
    }

    private void BuildComboItems()
    {
        AppearanceBox.ItemsSource = new[] { "Sombre", "Clair", "Automatique" };
        BackdropBox.ItemsSource = new[] { "Automatique", "Transparent", "Flouté", "Opaque" };
        DisplayBox.ItemsSource = new[] { "Écran principal", "Écran du curseur", "Écran désigné" };
        DensityBox.ItemsSource = new[] { "Compacte", "Confortable", "Aérée" };
        CutoutBox.ItemsSource = new[] { "Aucune", "Centrée", "À gauche", "À droite", "Personnalisée" };

        // L'ordre suit l'énumération MotionStyle : l'index sélectionné en est la valeur.
        MotionStyleBox.ItemsSource = new[] { "Calme", "Naturel", "Dynamique", "Personnalisé" };
    }

    /// <summary>
    /// Recharge tous les contrôles depuis les préférences. Appelé à l'ouverture et
    /// après une réinitialisation.
    /// </summary>
    private void LoadFromSettings()
    {
        AppSettings settings = _settings.Current;

        _loading = true;

        try
        {
            AppearanceBox.SelectedIndex = (int)settings.Appearance;
            BackdropBox.SelectedIndex = (int)settings.BackdropMode;
            DisplayBox.SelectedIndex = (int)settings.DisplayMode;
            CutoutBox.SelectedIndex = (int)settings.CutoutMode;

            DensityBox.SelectedIndex = (int)settings.Density;
            RadiusSlider.Value = settings.CornerRadiusBottom;
            ExpandedRadiusSlider.Value = settings.CornerRadiusExpanded;
            ShoulderSlider.Value = settings.ShoulderRadius;
            SmoothingToggle.IsOn = settings.CornerSmoothing > IslandShape.Circular;

            MotionStyleBox.SelectedIndex = (int)settings.MotionStyle;
            ResponseSlider.Value = settings.SpringResponseSeconds;
            BounceSlider.Value = settings.SpringBounce;

            BouncyToggle.IsOn = settings.AllowBouncyAnimations;
            HypnoticToggle.IsOn = settings.AllowHypnoticMotion;
            RainToggle.IsOn = settings.ShowBinaryRain;
            HoverToggle.IsOn = settings.HoverToPreview;
            FullscreenToggle.IsOn = settings.HideOverFullscreen;
            StackToggle.IsOn = settings.ShowActivityStack;
            ClockToggle.IsOn = settings.ShowClockAtRest;
            DiagnosticsToggle.IsOn = settings.EnableDiagnostics;
            CompositionToggle.IsOn = settings.UseCompositionAtmosphere;

            string executable = Environment.ProcessPath ?? string.Empty;
            StartupToggle.IsOn = executable.Length > 0
                && StartupRegistration.IsRegistered(StartupRegistration.BuildCommand(executable));

            foreach (KeyValuePair<string, ToggleSwitch> pair in _featureToggles)
            {
                pair.Value.IsOn = _features.Find(pair.Key)?.IsEnabled ?? false;
            }
        }
        finally
        {
            _loading = false;
        }

        UpdateValueLabels();
        UpdatePreview();

        StatusText.Text = string.Empty;
    }

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        // Le rechargement complet est volontairement évité pendant la manipulation
        // d'un curseur : réécrire la valeur en cours de glissement ferait sauter le
        // contrôle sous le doigt. Seuls l'aperçu, les libellés et le caractère de
        // mouvement suivent — ce dernier bascule sur « Personnalisé » dès qu'un
        // curseur de réglage fin bouge, ou change depuis le menu de la zone de
        // notification.
        SyncMotionStyle(settings);
        UpdateValueLabels();
        UpdatePreview();
    }

    private void UpdateValueLabels()
    {
        AppSettings settings = _settings.Current;

        // Les libellés sont des mesures : ils s'écrivent de la même façon quelle
        // que soit la locale, sans séparateur décimal inattendu.
        RadiusValue.Text = $"{settings.CornerRadiusBottom.ToString("0", CultureInfo.InvariantCulture)} px";
        ExpandedRadiusValue.Text = $"{settings.CornerRadiusExpanded.ToString("0", CultureInfo.InvariantCulture)} px";
        ShoulderValue.Text = $"{settings.ShoulderRadius.ToString("0", CultureInfo.InvariantCulture)} px";
        ResponseValue.Text = $"{settings.SpringResponseSeconds.ToString("0.00", CultureInfo.InvariantCulture)} s";
        BounceValue.Text = settings.SpringBounce.ToString("0.00", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Met l'aperçu à jour à partir des préférences.
    ///
    /// Les formes sont tracées par la même géométrie que la notch — épaules,
    /// congés interpolés selon la hauteur, superellipse — et non par des bords
    /// arrondis : un aperçu qui dessinerait quatre coins ronds montrerait une
    /// capsule là où l'utilisateur obtient une notch. Le fond suit le mode de
    /// composition choisi : montrer un dégradé transparent alors que le mode
    /// opaque est sélectionné serait un mensonge d'aperçu.
    /// </summary>
    private void UpdatePreview()
    {
        AppSettings settings = _settings.Current;

        bool light = settings.Appearance == IslandAppearance.Light;
        bool opaque = settings.BackdropMode == IslandBackdropMode.Opaque;

        Brush body = opaque
            ? AtmosphericMaskHelper.CreateOpaqueSurface(light: light)
            : AtmosphericMaskHelper.CreateAtmosphericGradient(light: light);

        Color foreground = light
            ? Color.FromArgb(0xF0, 0x14, 0x14, 0x18)
            : Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF);

        NotchGeometry geometry = settings.Geometry;

        TraceShape(PreviewIdleHost, PreviewIdle, IslandFootprint.For(IslandPresentationTier.Idle), geometry, body);
        TraceShape(PreviewSignalHost, PreviewSignal, IslandFootprint.For(IslandPresentationTier.Signal), geometry, body);
        TraceShape(PreviewCardHost, PreviewCard, IslandFootprint.For(IslandPresentationTier.Card, settings.Density), geometry, body);

        // Une forme ouverte de taille moyenne : c'est là que l'arrondi ouvert se
        // juge, sur une surface large où le congé n'est plus une fraction
        // importante de la hauteur.
        TraceShape(PreviewExpandedHost, PreviewExpanded, new IslandFootprint(336, 112), geometry, body);

        var ink = new SolidColorBrush(foreground);
        PreviewExpandedText.Foreground = ink;
        PreviewSignalText.Foreground = ink;
        PreviewCardHeadline.Foreground = ink;

        // Le contenu se mesure depuis les flancs, comme dans la notch : les
        // épaules appartiennent au bord de l'écran.
        double shoulder = geometry.ShoulderFor(IslandFootprint.For(IslandPresentationTier.Card, settings.Density));
        double padding = IslandFootprint.CardVerticalPadding(settings.Density);
        PreviewCardBody.Margin = new Thickness(14 + shoulder, padding, 14 + shoulder, padding);

        UpdateHypnoticPreview(settings);
    }

    /// <summary>Trace une forme d'aperçu à la taille de son encombrement.</summary>
    private static void TraceShape(
        FrameworkElement host,
        Microsoft.UI.Xaml.Shapes.Path path,
        IslandFootprint footprint,
        NotchGeometry geometry,
        Brush fill)
    {
        host.Width = footprint.Width;
        host.Height = footprint.Height;

        // Une fabrique neuve à chaque tracé : sa mémoire sert à éviter les
        // reconstructions image par image dans la notch, ce qui n'a pas de sens
        // pour un aperçu redessiné à chaque réglage.
        Geometry? shape = new IslandGeometryFactory().Build(
            footprint,
            geometry.RadiusFor(footprint),
            geometry.Smoothing,
            shoulder: geometry.ShoulderFor(footprint));

        if (shape is not null)
        {
            path.Data = shape;
        }

        path.Fill = fill;
    }

    /// <summary>
    /// Aligne le choix de caractère sur les préférences, sans déclencher de
    /// modification en retour.
    /// </summary>
    private void SyncMotionStyle(AppSettings settings)
    {
        int index = (int)settings.MotionStyle;

        if (MotionStyleBox.SelectedIndex == index)
        {
            return;
        }

        bool wasLoading = _loading;
        _loading = true;

        try
        {
            MotionStyleBox.SelectedIndex = index;

            // Un préréglage choisi ailleurs — le menu de la zone de notification —
            // déplace aussi les curseurs de réglage fin.
            if (settings.MotionStyle != MotionStyle.Custom)
            {
                ResponseSlider.Value = settings.SpringResponseSeconds;
                BounceSlider.Value = settings.SpringBounce;
            }
        }
        finally
        {
            _loading = wasLoading;
        }
    }

    // ------------------------------------------------------------------
    // Modification des préférences
    // ------------------------------------------------------------------

    private void Apply(Action<AppSettings> mutate)
    {
        if (_loading)
        {
            return;
        }

        _settings.Update(mutate);
    }

    /// <summary>
    /// Applique une modification continue — un curseur — sans écrire le fichier,
    /// et programme une écriture unique à la fin du glissement.
    /// </summary>
    private void ApplyContinuous(Action<AppSettings> mutate)
    {
        if (_loading)
        {
            return;
        }

        _settings.UpdateTransient(mutate);

        _persistTimer.Stop();
        _persistTimer.Start();
    }

    private void OnAppearanceChanged(object sender, SelectionChangedEventArgs e)
        => Apply(s => s.Appearance = (IslandAppearance)Math.Max(0, AppearanceBox.SelectedIndex));

    private void OnBackdropChanged(object sender, SelectionChangedEventArgs e)
        => Apply(s => s.BackdropMode = (IslandBackdropMode)Math.Max(0, BackdropBox.SelectedIndex));

    private void OnDisplayModeChanged(object sender, SelectionChangedEventArgs e)
        => Apply(s => s.DisplayMode = (IslandDisplayMode)Math.Max(0, DisplayBox.SelectedIndex));

    private void OnCutoutChanged(object sender, SelectionChangedEventArgs e)
        => Apply(s => s.CutoutMode = (CameraCutoutMode)Math.Max(0, CutoutBox.SelectedIndex));

    private void OnDensityChanged(object sender, SelectionChangedEventArgs e)
        => Apply(s => s.Density = (IslandContentDensity)Math.Max(0, DensityBox.SelectedIndex));

    private void OnRadiusChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        => ApplyContinuous(s => s.CornerRadiusBottom = e.NewValue);

    private void OnExpandedRadiusChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        => ApplyContinuous(s => s.CornerRadiusExpanded = e.NewValue);

    private void OnShoulderChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        => ApplyContinuous(s => s.ShoulderRadius = e.NewValue);

    private void OnSmoothingToggled(object sender, RoutedEventArgs e)
        => Apply(s => s.CornerSmoothing = SmoothingToggle.IsOn ? IslandShape.Squircle : IslandShape.Circular);

    private void OnResponseChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        => ApplyContinuous(s =>
        {
            s.SpringResponseSeconds = e.NewValue;
            s.MotionStyle = MotionStyle.Custom;
        });

    private void OnBounceChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        => ApplyContinuous(s =>
        {
            s.SpringBounce = e.NewValue;
            s.MotionStyle = MotionStyle.Custom;
        });

    /// <summary>
    /// Un caractère choisi pose la vitesse et le rebond ; les curseurs de
    /// réglage fin suivent, sans renvoyer leur propre modification.
    /// </summary>
    private void OnMotionStyleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || MotionStyleBox.SelectedIndex < 0)
        {
            return;
        }

        var style = (MotionStyle)MotionStyleBox.SelectedIndex;

        _settings.Update(s => s.ApplyMotionStyle(style));

        _loading = true;

        try
        {
            ResponseSlider.Value = _settings.Current.SpringResponseSeconds;
            BounceSlider.Value = _settings.Current.SpringBounce;
        }
        finally
        {
            _loading = false;
        }

        UpdateValueLabels();
    }

    private void OnHypnoticToggled(object sender, RoutedEventArgs e)
        => Apply(s => s.AllowHypnoticMotion = HypnoticToggle.IsOn);

    private void OnRainToggled(object sender, RoutedEventArgs e)
        => Apply(s => s.ShowBinaryRain = RainToggle.IsOn);

    private void OnFullscreenToggled(object sender, RoutedEventArgs e)
        => Apply(s => s.HideOverFullscreen = FullscreenToggle.IsOn);

    private void OnBouncyToggled(object sender, RoutedEventArgs e)
        => Apply(s => s.AllowBouncyAnimations = BouncyToggle.IsOn);


    private void OnClockToggled(object sender, RoutedEventArgs e)
        => Apply(s => s.ShowClockAtRest = ClockToggle.IsOn);

    private void OnHoverToggled(object sender, RoutedEventArgs e)
        => Apply(s => s.HoverToPreview = HoverToggle.IsOn);

    private void OnStackToggled(object sender, RoutedEventArgs e)
        => Apply(s => s.ShowActivityStack = StackToggle.IsOn);

    private void OnDiagnosticsToggled(object sender, RoutedEventArgs e)
        => Apply(s => s.EnableDiagnostics = DiagnosticsToggle.IsOn);

    private void OnCompositionToggled(object sender, RoutedEventArgs e)
        => Apply(s => s.UseCompositionAtmosphere = CompositionToggle.IsOn);

    /// <summary>
    /// La seule préférence qui écrit hors de l'application : l'inscription au
    /// démarrage. Elle n'est appliquée que sur action explicite, et son échec est
    /// signalé au lieu d'être absorbé — une case à cocher sans effet serait pire
    /// que pas de case du tout.
    /// </summary>
    private void OnStartupToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        string executable = Environment.ProcessPath ?? string.Empty;

        if (executable.Length == 0)
        {
            StatusText.Text = "Chemin de l'exécutable introuvable : inscription au démarrage impossible.";
            return;
        }

        bool enabled = StartupToggle.IsOn;

        if (!StartupRegistration.SetEnabled(enabled, StartupRegistration.BuildCommand(executable), out string? error))
        {
            StatusText.Text = $"Inscription au démarrage refusée : {error}";

            // Le contrôle revient à l'état réel : il ne doit pas prétendre avoir
            // obtenu ce que le système a refusé.
            _loading = true;
            StartupToggle.IsOn = StartupRegistration.IsRegistered(StartupRegistration.BuildCommand(executable));
            _loading = false;

            return;
        }

        StatusText.Text = enabled
            ? "Inscription au démarrage enregistrée pour ce compte."
            : string.Empty;

        _settings.Update(s => s.StartWithWindows = enabled);
    }

    private async Task OnFeatureToggledAsync(IIslandFeature feature, ToggleSwitch toggle)
    {
        if (_loading)
        {
            return;
        }

        bool enabled = toggle.IsOn;

        // L'ordre compte : on applique d'abord le cycle de vie, puis on enregistre.
        // L'inverse laisserait une préférence active alors que le démarrage a
        // échoué, ce qui produirait une tentative silencieuse à chaque lancement.
        await _features.SetEnabledAsync(feature.Id, enabled);
        _settings.SetFeatureEnabled(feature.Id, enabled);

        string outcome = feature.State switch
        {
            FeatureState.Running => "active",
            FeatureState.Stopped => "inactive",
            FeatureState.Faulted => "en échec — API système indisponible",
            _ => feature.State.ToString()
        };

        StatusText.Text = $"{feature.DisplayName} : {outcome}";

        MiniLogger.Log($"[SETTINGS] {feature.DisplayName} : {outcome}");
    }

    private void OnResetClicked(object sender, RoutedEventArgs e)
    {
        _settings.ResetToDefaults();

        // Une réinitialisation change aussi les bascules de fonctionnalités : le
        // rechargement complet est ici légitime, contrairement au glissement d'un
        // curseur.
        LoadFromSettings();

        StatusText.Text = "Valeurs par défaut rétablies.";
    }

    private void OnOpenConfigClicked(object sender, RoutedEventArgs e)
        => OpenFolder(Path.GetDirectoryName(_settings.ConfigFilePath));

    /// <summary>
    /// Ouvre le dossier des greffons. Il est créé au besoin : un emplacement qu'on
    /// ne peut pas trouver n'est pas un emplacement.
    /// </summary>
    private void OnOpenPluginsClicked(object sender, RoutedEventArgs e)
        => OpenFolder(PluginLoader.ResolveDefaultDirectory());

    private void OpenFolder(string? directory)
    {
        try
        {
            if (string.IsNullOrEmpty(directory))
            {
                StatusText.Text = "Dossier introuvable.";
                return;
            }

            Directory.CreateDirectory(directory);

            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{directory}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Impossible d'ouvrir le dossier : {ex.Message}";
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

    /// <summary>Saut vers une section : son titre vient en haut de la zone de défilement.</summary>
    private void OnSectionClicked(object sender, RoutedEventArgs e)
    {
        FrameworkElement? header = (sender as FrameworkElement)?.Tag switch
        {
            "Appearance" => SectionAppearance,
            "Behavior" => SectionBehavior,
            "Activities" => SectionActivities,
            "Motion" => SectionMotion,
            "Displays" => SectionDisplays,
            "Advanced" => SectionAdvanced,
            _ => null
        };

        header?.StartBringIntoView(new BringIntoViewOptions
        {
            VerticalAlignmentRatio = 0,
            AnimationDesired = true
        });
    }

    /// <summary>
    /// Grille hypnotique de l'aperçu : la vraie, jouée si le mouvement est
    /// autorisé, figée sinon — exactement ce que la notch fera.
    /// </summary>
    private void UpdateHypnoticPreview(AppSettings settings)
    {
        _previewHypnotic ??= HypnoticSurface.TryAttach(PreviewHypnoticHost);

        if (_previewHypnotic is null)
        {
            return;
        }

        PreviewCardGlyph.Visibility = Visibility.Collapsed;
        _previewHypnotic.SetPreset(HypnoticPreset.Think, settings.AllowHypnoticMotion && settings.AllowBouncyAnimations);
    }

    private HypnoticSurface? _previewHypnotic;

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _settings.Changed -= OnSettingsChanged;
        _previewHypnotic?.Dispose();

        // Une écriture en attente ne doit pas être perdue parce que la fenêtre se
        // ferme juste après le dernier glissement de curseur.
        if (_persistTimer.IsRunning)
        {
            _persistTimer.Stop();
            _settings.Persist();
        }

        MiniLogger.Log("Fenêtre de réglages fermée");
    }
}
