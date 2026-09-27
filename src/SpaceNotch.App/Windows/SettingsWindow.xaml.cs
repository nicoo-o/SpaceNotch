using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SpaceNotch.Core.Features;
using SpaceNotch.Core.Motion;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.Localization;
using SpaceNotch.Infrastructure.Config;
using SpaceNotch.Infrastructure.Logging;
using SpaceNotch.Infrastructure.Plugins;
using SpaceNotch.Platform.Windows.System;
using SpaceNotch_App.Composition;
using SpaceNotch_App.Views;
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

    /// <summary>« Revoir la présentation » : la notch la rejoue (fourni par la fenêtre de la notch).</summary>
    public Action? ReplayWelcome { get; set; }

    /// <summary>Demande l'accès aux notifications Windows (fenêtre de consentement du système).</summary>
    public Func<Task<SpaceNotch.Platform.Windows.Notifications.NotificationAccess>>? RequestNotificationAccess { get; set; }

    /// <summary>Raccourci global de la recherche, pour l'afficher (« Alt+Espace »).</summary>
    public string? SearchHotkey
    {
        get => _searchHotkey;
        set
        {
            _searchHotkey = value;
            ShowHotkey();
        }
    }

    private string? _searchHotkey;

    public SettingsWindow(SettingsService settings, IslandFeatureRegistry features)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(features);

        _settings = settings;
        _features = features;

        InitializeComponent();

        ConfigureWindow();
        BuildLogos();
        BuildFeatureToggles();
        BuildComboItems();
        LoadFromSettings();
        SelectPage(PageGeneral, NavGeneral);
        ShowPendingPlugins();
        ShowHotkey();
        UpdateNotificationCard();

        string version = typeof(SettingsWindow).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : string.Empty;
        VersionText.Text = $"Version {version}";
        NavVersionText.Text = $"SpaceNotch {version}";

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
        AppIcon.ApplyTo(appWindow);

        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = false;
            presenter.IsResizable = true;
            presenter.IsMaximizable = false;
        }

        // 920 × 660 DIP : le menu latéral et une colonne de cartes lisible, à
        // toutes les échelles d'affichage (AppWindow parle en pixels physiques).
        double scale = GetDpiForWindow(handle) / 96.0;
        appWindow.Resize(new SizeInt32((int)(920 * scale), (int)(660 * scale)));

        // Barre de titre noire, comme la fenêtre : la barre blanche par défaut
        // coupait l'OLED en deux.
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            Color black = Color.FromArgb(0xFF, 0x05, 0x05, 0x06);
            Color ink = Color.FromArgb(0xFF, 0xE8, 0xE8, 0xEA);
            appWindow.TitleBar.BackgroundColor = black;
            appWindow.TitleBar.InactiveBackgroundColor = black;
            appWindow.TitleBar.ForegroundColor = ink;
            appWindow.TitleBar.ButtonBackgroundColor = black;
            appWindow.TitleBar.ButtonInactiveBackgroundColor = black;
            appWindow.TitleBar.ButtonForegroundColor = ink;
            appWindow.TitleBar.ButtonHoverBackgroundColor = Color.FromArgb(0xFF, 0x1A, 0x1B, 0x1F);
            appWindow.TitleBar.ButtonHoverForegroundColor = ink;
        }

        // La fenêtre de réglages se place au centre de l'écran principal : les
        // réglages se lisent, ils ne doivent pas aller chercher l'utilisateur.
        RectInt32 area = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        appWindow.Move(new PointInt32(
            area.X + ((area.Width - appWindow.Size.Width) / 2),
            area.Y + ((area.Height - appWindow.Size.Height) / 2)));
    }

    /// <summary>
    /// Une carte par activité réglable. Les fonctionnalités internes — menu
    /// rapide, présentation, recherche — n'ont pas de préférence à retenir : elles
    /// n'apparaissent pas, plutôt qu'en interrupteur inerte « sans préférence ».
    /// </summary>
    private void BuildFeatureToggles()
    {
        foreach (IIslandFeature feature in _features.Features)
        {
            if (!AppSettings.HasFeaturePreference(feature.Id))
            {
                continue;
            }

            (string glyph, string title, string description) = DescribeFeature(feature);

            var toggle = new ToggleSwitch
            {
                OnContent = string.Empty,
                OffContent = string.Empty,
                MinWidth = 0,
                IsOn = feature.IsEnabled,
                VerticalAlignment = VerticalAlignment.Center
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle, title);

            IIslandFeature captured = feature;
            toggle.Toggled += async (_, _) => await OnFeatureToggledAsync(captured, toggle);

            _featureToggles[feature.Id] = toggle;
            FeaturesHost.Children.Add(SettingsCard(glyph, title, description, toggle));
        }
    }

    private static (string Glyph, string Title, string Description) DescribeFeature(IIslandFeature feature) => feature.Id switch
    {
        FeatureKeys.Media => ("Music", Lang.T("Musique et vidéos", "Music and video"), Lang.T("Pochette, titre, lecture — Spotify, navigateur, Apple Music…", "Artwork, title, playback — Spotify, browser, Apple Music…")),
        FeatureKeys.Notifications => ("Notification", "Notifications", Lang.T("Un aperçu dans la notch, puis elles se rangent.", "A glimpse in the notch, then they tidy themselves away.")),
        FeatureKeys.Clipboard => ("Clipboard", Lang.T("Presse-papier", "Clipboard"), Lang.T("Les derniers éléments copiés, épinglables.", "Your latest copied items, pinnable.")),
        FeatureKeys.Bluetooth => ("Bluetooth", "Bluetooth", Lang.T("Connexion, déconnexion et batterie de tes appareils.", "Connections, disconnections and battery of your devices.")),
        FeatureKeys.VolumeHud => ("VolumeHigh", "Volume", Lang.T("Remplace l’indicateur de volume de Windows.", "Replaces the Windows volume indicator.")),
        FeatureKeys.Pomodoro => ("Timer", "Focus", Lang.T("Des sessions de 25 minutes, puis une pause.", "25-minute sessions, then a break.")),
        FeatureKeys.FileShelf => ("Folder", Lang.T("Étagère", "Shelf"), Lang.T("Dépose des fichiers sur la notch, reprends-les plus tard.", "Drop files on the notch, pick them up later.")),
        FeatureKeys.Downloads => ("Download", Lang.T("Téléchargements", "Downloads"), Lang.T("La progression de ce que tu télécharges.", "The progress of what you download.")),
        FeatureKeys.Privacy => ("Camera", Lang.T("Caméra et micro", "Camera and mic"), Lang.T("Un point quand une application les utilise.", "A dot when an app is using them.")),
        _ => ("Launcher", feature.DisplayName, string.Empty)
    };

    /// <summary>
    /// La trame du menu latéral : la matière de la notch, au cyan du logo,
    /// sous le dernier onglet. Rien en contraste élevé.
    /// </summary>
    private void OnNavColumnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        NavTrame.Resize(Math.Max(0, NavColumn.ActualWidth - 20), Math.Max(0, NavColumn.ActualHeight - 28), 0, 0);
        RefreshNavTrame();
    }

    /// <summary>La trame du menu suit le réglage : le noir pur s'applique aussi ici.</summary>
    private void RefreshNavTrame()
    {
        NavTrame.IsAllowed = _settings.Current.ShowTrame && !new global::Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;
        NavTrame.Animate = GlyphView.AnimationsEnabled;
        NavTrame.Present(NavColumn, Color.FromArgb(0xFF, 0x7F, 0xE6, 0xFF), music: false);
    }

    /// <summary>Carte OLED construite en code, identique à celles du XAML.</summary>
    private static Border SettingsCard(string glyph, string title, string description, FrameworkElement control)
    {
        var grid = new Grid { ColumnSpacing = 14 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(new GlyphView
        {
            Key = glyph,
            Size = 16,
            Tint = new SolidColorBrush(Color.FromArgb(0xB8, 0xFF, 0xFF, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center
        });

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = title, Style = (Style)Application.Current.Resources["NfSettingsTitleStyle"] });

        if (description.Length > 0)
        {
            texts.Children.Add(new TextBlock { Text = description, Style = (Style)Application.Current.Resources["NfSettingsDescriptionStyle"] });
        }

        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);

        Grid.SetColumn(control, 2);
        grid.Children.Add(control);

        return new Border
        {
            Style = (Style)Application.Current.Resources["NfSettingsCardStyle"],
            Tag = $"{title} {description}",
            Child = grid
        };
    }

    private void BuildComboItems()
    {
        AppearanceBox.ItemsSource = new[] { Lang.T("Sombre", "Dark"), Lang.T("Clair", "Light"), Lang.T("Automatique", "Automatic") };
        BackdropBox.ItemsSource = new[] { Lang.T("Automatique", "Automatic"), "Transparent", Lang.T("Flouté", "Blurred"), "Opaque" };
        DisplayBox.ItemsSource = new[] { Lang.T("Écran principal", "Main display"), Lang.T("Écran du curseur", "Display with the pointer"), Lang.T("Écran où elle a été accrochée", "Display it was docked to") };
        EdgeBox.ItemsSource = new[] { Lang.T("Haut", "Top"), Lang.T("Gauche", "Left"), Lang.T("Droite", "Right") };
        SurfaceTintBox.ItemsSource = new[] { Lang.T("Noir OLED", "OLED black"), "Graphite", Lang.T("Personnalisée", "Custom") };
        BubbleSizeBox.ItemsSource = new[] { Lang.T("Petite", "Small"), Lang.T("Normale", "Normal"), Lang.T("Grande", "Large") };
        TabSizeBox.ItemsSource = new[] { Lang.T("Petite", "Small"), Lang.T("Normale", "Normal"), Lang.T("Grande", "Large") };
        DetachFeelBox.ItemsSource = new[] { Lang.T("Souple", "Soft"), Lang.T("Naturelle", "Natural"), Lang.T("Ferme", "Firm") };
        DensityBox.ItemsSource = new[] { Lang.T("Compacte", "Compact"), Lang.T("Confortable", "Comfortable"), Lang.T("Aérée", "Airy") };
        CutoutBox.ItemsSource = new[] { Lang.T("Aucune", "None"), Lang.T("Centrée", "Centred"), Lang.T("À gauche", "Left"), Lang.T("À droite", "Right"), Lang.T("Personnalisée", "Custom") };

        // L'ordre suit l'énumération MotionStyle : l'index sélectionné en est la valeur.
        MotionStyleBox.ItemsSource = new[] { Lang.T("Calme", "Calm"), Lang.T("Naturel", "Natural"), Lang.T("Dynamique", "Lively"), Lang.T("Personnalisé", "Custom") };

        WebSearchBox.ItemsSource = WebEngines.Select(e => e.Label).ToArray();
        ClipboardSizeBox.ItemsSource = ClipboardSizes.Select(n => Lang.T($"{n} éléments", $"{n} items")).ToArray();
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
            HoverExpandToggle.IsOn = settings.HoverToExpand;
            DetachToggle.IsOn = settings.AllowDetach;
            EdgeBox.SelectedIndex = (int)settings.DockEdge;
            SideEdgesToggle.IsOn = settings.AllowSideEdges;
            SurfaceTintBox.SelectedIndex = (int)settings.SurfaceTint;
            CustomColorBox.Text = settings.CustomSurfaceColor;
            OpacitySlider.Value = settings.SurfaceOpacity * 100;
            SideShoulderSlider.Value = settings.SideShoulderRadius;
            FloatingRadiusSlider.Value = settings.FloatingRadius;
            ShadowSlider.Value = settings.FloatingShadowOpacity * 100;
            OutlineToggle.IsOn = settings.ShowOutline;
            TrameToggle.IsOn = settings.ShowTrame;
            OutlineSlider.Value = settings.OutlineOpacity * 100;
            BubbleSizeBox.SelectedIndex = (int)settings.BubbleSize;
            TabSizeBox.SelectedIndex = (int)settings.TabSize;
            DetachFeelBox.SelectedIndex = (int)settings.DetachFeel;
            StretchSlider.Value = settings.StretchAmount * 100;
            TearSlider.Value = settings.TearDistance;
            MagnetsToggle.IsOn = settings.MagnetsEnabled;
            GooToggle.IsOn = settings.GooEnabled;
            MonitorResistanceToggle.IsOn = settings.MonitorResistance;
            BubbleToggle.IsOn = settings.ShowSplitBubble;
            FullscreenToggle.IsOn = settings.HideOverFullscreen;
            StackToggle.IsOn = settings.ShowActivityStack;
            ClockToggle.IsOn = settings.ShowClockAtRest;
            DiagnosticsToggle.IsOn = settings.EnableDiagnostics;
            CompositionToggle.IsOn = settings.UseCompositionAtmosphere;
            ClipboardSecretsToggle.IsOn = settings.ClipboardIgnoreSecrets;
            WebSearchBox.SelectedIndex = Math.Max(0, Array.FindIndex(WebEngines, e => e.Key == settings.WebSearchEngine));

            int size = Array.FindIndex(ClipboardSizes, n => n >= settings.ClipboardHistoryLimit);
            ClipboardSizeBox.SelectedIndex = size < 0 ? ClipboardSizes.Length - 1 : size;

            ShowIgnoredApps(settings.IgnoredNotificationApps);

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
        ShowIgnoredApps(settings.IgnoredNotificationApps);
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
        OpacityValue.Text = $"{(settings.SurfaceOpacity * 100).ToString("0", CultureInfo.InvariantCulture)} %";
        SideShoulderValue.Text = $"{settings.SideShoulderRadius.ToString("0", CultureInfo.InvariantCulture)} px";
        FloatingRadiusValue.Text = $"{settings.FloatingRadius.ToString("0", CultureInfo.InvariantCulture)} px";
        ShadowValue.Text = $"{(settings.FloatingShadowOpacity * 100).ToString("0", CultureInfo.InvariantCulture)} %";
        OutlineValue.Text = $"{(settings.OutlineOpacity * 100).ToString("0", CultureInfo.InvariantCulture)} %";
        StretchValue.Text = $"{(settings.StretchAmount * 100).ToString("0", CultureInfo.InvariantCulture)} %";
        TearValue.Text = $"{settings.TearDistance.ToString("0", CultureInfo.InvariantCulture)} px";

        // La couleur personnalisée ne sert que si elle est choisie ; l'aperçu
        // de la pastille dit ce qu'elle donnera.
        bool custom = settings.SurfaceTint == SurfaceTint.Custom;
        CustomColorBox.IsEnabled = custom;
        (byte a, byte r, byte g, byte b) = settings.SurfaceColor();
        CustomColorSwatch.Background = new SolidColorBrush(global::Windows.UI.Color.FromArgb(a, r, g, b));
        OutlineSlider.IsEnabled = settings.ShowOutline;
        EdgeBox.IsEnabled = settings.AllowSideEdges;
    }

    private void OnEdgeChanged(object sender, SelectionChangedEventArgs e)
        => Apply(s => s.DockEdge = (NotchEdge)Math.Max(0, EdgeBox.SelectedIndex));

    private void OnSideEdgesToggled(object sender, RoutedEventArgs e)
        => Apply(s => s.AllowSideEdges = SideEdgesToggle.IsOn);

    private void OnSurfaceTintChanged(object sender, SelectionChangedEventArgs e)
        => Apply(s => s.SurfaceTint = (SurfaceTint)Math.Max(0, SurfaceTintBox.SelectedIndex));

    private void OnCustomColorChanged(object sender, TextChangedEventArgs e)
    {
        // Une couleur à moitié tapée ne s'applique pas : seule une couleur
        // complète et valide remplace la précédente.
        if (AppSettings.TryParseColor(CustomColorBox.Text, out _))
        {
            Apply(s => s.CustomSurfaceColor = "#" + CustomColorBox.Text.Trim().TrimStart('#').ToUpperInvariant());
        }
    }

    private void OnOpacityChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        => ApplyContinuous(s => s.SurfaceOpacity = e.NewValue / 100);

    private void OnSideShoulderChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        => ApplyContinuous(s => s.SideShoulderRadius = e.NewValue);

    private void OnFloatingRadiusChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        => ApplyContinuous(s => s.FloatingRadius = e.NewValue);

    private void OnShadowChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        => ApplyContinuous(s => s.FloatingShadowOpacity = e.NewValue / 100);

    private void OnOutlineToggled(object sender, RoutedEventArgs e)
        => Apply(s => s.ShowOutline = OutlineToggle.IsOn);

    private void OnTrameToggled(object sender, RoutedEventArgs e)
    {
        Apply(s => s.ShowTrame = TrameToggle.IsOn);
        RefreshNavTrame();
    }

    private void OnOutlineChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        => ApplyContinuous(s => s.OutlineOpacity = e.NewValue / 100);

    private void OnBubbleSizeChanged(object sender, SelectionChangedEventArgs e)
        => Apply(s => s.BubbleSize = (ElementSize)Math.Max(0, BubbleSizeBox.SelectedIndex));

    private void OnTabSizeChanged(object sender, SelectionChangedEventArgs e)
        => Apply(s => s.TabSize = (ElementSize)Math.Max(0, TabSizeBox.SelectedIndex));

    private void OnDetachFeelChanged(object sender, SelectionChangedEventArgs e)
        => Apply(s => s.DetachFeel = (DetachFeel)Math.Max(0, DetachFeelBox.SelectedIndex));

    private void OnStretchChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        => ApplyContinuous(s => s.StretchAmount = e.NewValue / 100);

    private void OnTearChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        => ApplyContinuous(s => s.TearDistance = e.NewValue);

    private void OnMagnetsToggled(object sender, RoutedEventArgs e)
        => Apply(s => s.MagnetsEnabled = MagnetsToggle.IsOn);

    private void OnGooToggled(object sender, RoutedEventArgs e)
        => Apply(s => s.GooEnabled = GooToggle.IsOn);

    private void OnMonitorResistanceToggled(object sender, RoutedEventArgs e)
        => Apply(s => s.MonitorResistance = MonitorResistanceToggle.IsOn);

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

    private void OnHoverExpandToggled(object sender, RoutedEventArgs e)
        => Apply(s => s.HoverToExpand = HoverExpandToggle.IsOn);

    private void OnDetachToggled(object sender, RoutedEventArgs e)
        => Apply(s => s.AllowDetach = DetachToggle.IsOn);

    private void OnBubbleToggled(object sender, RoutedEventArgs e)
        => Apply(s => s.ShowSplitBubble = BubbleToggle.IsOn);

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
            StatusText.Text = Lang.T("Chemin de l’exécutable introuvable : inscription au démarrage impossible.", "Executable path not found: cannot register at startup.");
            return;
        }

        bool enabled = StartupToggle.IsOn;

        if (!StartupRegistration.SetEnabled(enabled, StartupRegistration.BuildCommand(executable), out string? error))
        {
            StatusText.Text = Lang.T($"Inscription au démarrage refusée : {error}", $"Startup registration refused: {error}");

            // Le contrôle revient à l'état réel : il ne doit pas prétendre avoir
            // obtenu ce que le système a refusé.
            _loading = true;
            StartupToggle.IsOn = StartupRegistration.IsRegistered(StartupRegistration.BuildCommand(executable));
            _loading = false;

            return;
        }

        StatusText.Text = enabled
            ? Lang.T("Inscription au démarrage enregistrée pour ce compte.", "Registered to start with this account.")
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
            FeatureState.Running => Lang.T("active", "on"),
            FeatureState.Stopped => Lang.T("inactive", "off"),
            FeatureState.Faulted => Lang.T("en échec — API système indisponible", "failed — system API unavailable"),
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

        StatusText.Text = Lang.T("Valeurs par défaut rétablies.", "Defaults restored.");
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
                StatusText.Text = Lang.T("Dossier introuvable.", "Folder not found.");
                return;
            }

            Directory.CreateDirectory(directory);

            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{directory}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText.Text = Lang.T($"Impossible d’ouvrir le dossier : {ex.Message}", $"Could not open the folder: {ex.Message}");
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

    // ------------------------------------------------------------------
    // Pages et recherche
    // ------------------------------------------------------------------

    private static readonly (string Key, string Label)[] WebEngines =
    [
        ("bing", "Bing"),
        ("google", "Google"),
        ("duckduckgo", "DuckDuckGo")
    ];

    private static readonly int[] ClipboardSizes = [20, 50, 100];

    private StackPanel[] Pages => [PageGeneral, PageNotch, PageAppearance, PageActivities, PageMotion, PageDisplays, PageAbout];

    private Button[] NavButtons => [NavGeneral, NavNotch, NavAppearance, NavActivities, NavMotion, NavDisplays, NavAbout];

    private StackPanel _currentPage = null!;

    private void OnNavClicked(object sender, RoutedEventArgs e)
    {
        int index = Array.IndexOf(NavButtons, sender as Button);

        if (index >= 0)
        {
            SearchSettingsBox.Text = string.Empty;
            SelectPage(Pages[index], NavButtons[index]);
        }
    }

    /// <summary>Une page à la fois ; l'entrée du menu reçoit le fond de sélection et la barre cyan.</summary>
    private void SelectPage(StackPanel page, Button nav)
    {
        _currentPage = page;

        foreach (StackPanel candidate in Pages)
        {
            candidate.Visibility = ReferenceEquals(candidate, page) ? Visibility.Visible : Visibility.Collapsed;
        }

        foreach (Button button in NavButtons)
        {
            bool on = ReferenceEquals(button, nav);
            button.Background = on
                ? (Brush)Application.Current.Resources["NfSelectionBrush"]
                : new SolidColorBrush(Microsoft.UI.Colors.Transparent);

            if (button.Content is Grid { Children.Count: > 0 } content && content.Children[0] is Border bar)
            {
                bar.Opacity = on ? 1 : 0;
            }
        }

        SettingsScroll.ChangeView(null, 0, null, disableAnimation: true);
    }

    /// <summary>
    /// Recherche : toutes les pages à la fois, seules les cartes dont le titre ou
    /// la phrase contient le texte ; titres de groupe masqués le temps de chercher.
    /// </summary>
    private void OnSearchSettingsChanged(object sender, TextChangedEventArgs e)
    {
        string query = SearchSettingsBox.Text.Trim();

        if (query.Length == 0)
        {
            SearchHeader.Visibility = Visibility.Collapsed;
            SetCardsVisible(all: true, query);
            SelectPage(_currentPage, NavButtons[Array.IndexOf(Pages, _currentPage)]);
            return;
        }

        int found = SetCardsVisible(all: false, query);
        SearchHeader.Text = found == 0 ? Lang.T($"Aucun réglage pour « {query} »", $"No settings for “{query}”") : Lang.T($"Résultats pour « {query} »", $"Results for “{query}”");
        SearchHeader.Visibility = Visibility.Visible;
    }

    private int SetCardsVisible(bool all, string query)
    {
        int found = 0;

        foreach (StackPanel page in Pages)
        {
            int inPage = 0;

            foreach (UIElement child in AllChildren(page))
            {
                switch (child)
                {
                    case Border { Tag: string text } card:
                        bool match = all || text.Contains(query, StringComparison.CurrentCultureIgnoreCase);
                        card.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
                        inPage += match ? 1 : 0;
                        break;

                    case TextBlock label:
                        // Titre de page, phrase d'introduction, titres de groupe.
                        label.Visibility = all ? Visibility.Visible : Visibility.Collapsed;
                        break;

                    case Border preview when !all:
                        preview.Visibility = Visibility.Collapsed;
                        break;

                    case Border preview:
                        preview.Visibility = Visibility.Visible;
                        break;
                }
            }

            found += inPage;

            if (!all)
            {
                page.Visibility = inPage > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        return found;
    }

    /// <summary>Les enfants directs d'une page, et ceux de la liste des activités.</summary>
    private static IEnumerable<UIElement> AllChildren(StackPanel page)
    {
        foreach (UIElement child in page.Children)
        {
            if (child is StackPanel nested)
            {
                foreach (UIElement inner in nested.Children)
                {
                    yield return inner;
                }
            }
            else
            {
                yield return child;
            }
        }
    }

    // ------------------------------------------------------------------
    // Général : présentation, raccourci, moteur web, notifications
    // ------------------------------------------------------------------

    private void OnReplayWelcomeClicked(object sender, RoutedEventArgs e)
    {
        ReplayWelcome?.Invoke();
        Close();
    }

    private void ShowHotkey()
    {
        HotkeyCaps.Children.Clear();

        if (string.IsNullOrWhiteSpace(_searchHotkey))
        {
            HotkeyCaps.Children.Add(new TextBlock
            {
                Text = Lang.T("Aucun (Alt+Espace et Win+Maj+Espace sont pris)", "None (Alt+Space and Win+Shift+Space are taken)"),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)),
                VerticalAlignment = VerticalAlignment.Center
            });
            return;
        }

        foreach (string key in _searchHotkey.Split('+'))
        {
            HotkeyCaps.Children.Add(new Border
            {
                Style = (Style)Application.Current.Resources["NfKeyCapStyle"],
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = key, Style = (Style)Application.Current.Resources["NfKeyCapTextStyle"] }
            });
        }
    }

    private void OnWebSearchChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WebSearchBox.SelectedIndex is int index and >= 0)
        {
            Apply(s => s.WebSearchEngine = WebEngines[index].Key);
        }
    }

    private void UpdateNotificationCard()
    {
        var access = SpaceNotch.Platform.Windows.Notifications.WindowsNotificationListener.GetAccess();
        var green = new SolidColorBrush(Color.FromArgb(0xFF, 0x8F, 0xF0, 0xA4));
        var dim = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));

        (string Status, Brush Brush, string? Button) card = access switch
        {
            SpaceNotch.Platform.Windows.Notifications.NotificationAccess.Allowed => (Lang.T("● Autorisé", "● Allowed"), green, null),
            SpaceNotch.Platform.Windows.Notifications.NotificationAccess.Denied => (Lang.T("Bloqué par Windows", "Blocked by Windows"), dim, Lang.T("Ouvrir les paramètres", "Open Settings")),
            SpaceNotch.Platform.Windows.Notifications.NotificationAccess.NotAsked => (string.Empty, dim, Lang.T("Autoriser", "Allow")),
            _ => (Lang.T("Demande SpaceNotch installé", "Requires SpaceNotch to be installed"), dim, null)
        };
        (string status, Brush brush, string? button) = card;

        NotificationStatusText.Text = status;
        NotificationStatusText.Foreground = brush;
        NotificationButton.Content = button;
        NotificationButton.Visibility = button is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void OnNotificationButtonClicked(object sender, RoutedEventArgs e)
    {
        var access = SpaceNotch.Platform.Windows.Notifications.WindowsNotificationListener.GetAccess();

        if (access == SpaceNotch.Platform.Windows.Notifications.NotificationAccess.Denied)
        {
            // Le refus se lève dans Paramètres › Confidentialité › Notifications.
            _ = await global::Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:privacy-notifications"));
            return;
        }

        if (RequestNotificationAccess is not null)
        {
            await RequestNotificationAccess();
        }

        UpdateNotificationCard();
    }

    private void ShowIgnoredApps(List<string> apps)
    {
        IgnoredAppsHost.Children.Clear();
        IgnoredAppsHost.Visibility = apps.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        foreach (string app in apps)
        {
            var chip = new Button
            {
                Style = (Style)Application.Current.Resources["NfChipButtonStyle"],
                Content = $"{app}  ✕"
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(chip, Lang.T($"Ne plus ignorer {app}", $"Stop ignoring {app}"));

            string captured = app;
            chip.Click += (_, _) => Apply(s => s.IgnoredNotificationApps.RemoveAll(a => string.Equals(a, captured, StringComparison.OrdinalIgnoreCase)));
            IgnoredAppsHost.Children.Add(chip);
        }
    }

    private void OnIgnoredAppKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != global::Windows.System.VirtualKey.Enter)
        {
            return;
        }

        e.Handled = true;
        string app = IgnoredAppBox.Text.Trim();

        if (app.Length == 0)
        {
            return;
        }

        Apply(s =>
        {
            if (!s.IgnoredNotificationApps.Contains(app, StringComparer.OrdinalIgnoreCase))
            {
                s.IgnoredNotificationApps.Add(app);
            }
        });

        IgnoredAppBox.Text = string.Empty;
    }

    private void OnClipboardSecretsToggled(object sender, RoutedEventArgs e)
        => Apply(s => s.ClipboardIgnoreSecrets = ClipboardSecretsToggle.IsOn);

    private void OnClipboardSizeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ClipboardSizeBox.SelectedIndex is int index and >= 0)
        {
            Apply(s => s.ClipboardHistoryLimit = ClipboardSizes[index]);
        }
    }

    // ------------------------------------------------------------------
    // Greffons en attente d'approbation
    // ------------------------------------------------------------------

    /// <summary>
    /// Les greffons présents mais non approuvés — ou modifiés depuis — : un
    /// bouton par greffon, qui l'approuve tel qu'il est (nom et empreinte).
    /// </summary>
    private void ShowPendingPlugins()
    {
        PendingPluginsHost.Children.Clear();

        IReadOnlyList<string> pending = PluginLoader.FindPending(
            PluginLoader.ResolveDefaultDirectory(),
            new PluginAllowlist(_settings.Current.ApprovedPlugins));

        if (pending.Count == 0)
        {
            PendingPluginsHost.Children.Add(new TextBlock
            {
                Text = Lang.T("Aucun greffon en attente.", "No pending plugins."),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF))
            });
            return;
        }

        foreach (string path in pending)
        {
            string name = Path.GetFileName(path);
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            row.Children.Add(new TextBlock
            {
                Text = name,
                FontSize = 12.5,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            var approve = new Button
            {
                Style = (Style)Application.Current.Resources["NfSecondaryButtonStyle"],
                Content = Lang.T("Autoriser", "Allow")
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(approve, Lang.T($"Autoriser le greffon {name}", $"Allow plugin {name}"));
            approve.Click += (_, _) =>
            {
                if (PluginAllowlist.Hash(path) is not { } hash)
                {
                    StatusText.Text = Lang.T($"{name} : fichier illisible.", $"{name}: unreadable file.");
                    return;
                }

                Apply(s => s.ApprovedPlugins[name] = hash);
                StatusText.Text = Lang.T($"{name} sera chargé au prochain démarrage de SpaceNotch.", $"{name} will load the next time SpaceNotch starts.");
                ShowPendingPlugins();
            };
            Grid.SetColumn(approve, 1);
            row.Children.Add(approve);

            PendingPluginsHost.Children.Add(row);
        }
    }

    // ------------------------------------------------------------------
    // Logo : la grille 3×3, cyan, pixel central blanc
    // ------------------------------------------------------------------

    private void BuildLogos()
    {
        FillLogo(BrandLogo, 4, 1);
        FillLogo(AboutLogo, 8, 2);
    }

    private static void FillLogo(Grid host, double pixel, double gap)
    {
        host.Children.Clear();
        host.RowDefinitions.Clear();
        host.ColumnDefinitions.Clear();

        for (int i = 0; i < 3; i++)
        {
            host.RowDefinitions.Add(new RowDefinition());
            host.ColumnDefinitions.Add(new ColumnDefinition());
        }

        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                bool center = row == 1 && column == 1;
                var dot = new Border
                {
                    Width = pixel,
                    Height = pixel,
                    Margin = new Thickness(gap / 2),
                    CornerRadius = new CornerRadius(pixel / 4),
                    Background = center
                        ? new SolidColorBrush(Microsoft.UI.Colors.White)
                        : new SolidColorBrush(Color.FromArgb(0xFF, 0x7F, 0xE6, 0xFF))
                };
                Grid.SetRow(dot, row);
                Grid.SetColumn(dot, column);
                host.Children.Add(dot);
            }
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

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
