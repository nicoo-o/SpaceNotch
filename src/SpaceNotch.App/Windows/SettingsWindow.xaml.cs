using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
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

    /// <summary>Redémarre la notch (notifications activées après coup, n° 12) ; faux si c'est impossible.</summary>
    public Func<bool>? RestartNotch { get; set; }

    /// <summary>Notifications activées depuis les Réglages : elles attendent le redémarrage de la notch.</summary>
    private bool _notificationsAwaitRestart;

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

    /// <summary>
    /// Raccourci « aller à la notch » (Win+Alt+N, sinon Ctrl+Alt+N), pour l'afficher :
    /// il existait sans être montré nulle part (n° 81).
    /// </summary>
    public string? FocusHotkey
    {
        get => _focusHotkey;
        set
        {
            _focusHotkey = value;
            string none = Lang.T("Aucun : Win+Alt+N et Ctrl+Alt+N sont pris par d'autres applications", "None: Win+Alt+N and Ctrl+Alt+N are taken by other apps");
            ShowKeys(FocusHotkeyCaps, value, none);
            AutomationProperties.SetName(FocusHotkeyHost, Lang.T("Aller à la notch : ", "Go to the notch: ") + (string.IsNullOrWhiteSpace(value) ? none : value));
            AutomationProperties.SetHelpText(FocusHotkeyHost, Lang.T(
                "Donne le clavier à la notch depuis n’importe quelle application : flèches pour parcourir, Échap pour revenir.",
                "Moves the keyboard to the notch from any app: arrows to browse, Esc to go back."));
        }
    }

    private string? _focusHotkey;

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
        UpdateNotificationCard();
        AnnotateCards();

        // Après l'indexation : le texte d'attente (« Aucun : … sont pris ») n'est
        // pas le sujet de la carte, et faisait sortir la recherche sur « aucun » (n° 85).
        ShowHotkey();

        // Réglages avancés masqués tant qu'on ne les demande pas (n° 23).
        SetCardsVisible(all: true, string.Empty);

        string version = AppVersion();
        VersionText.Text = $"Version {version}";
        NavVersionText.Text = $"SpaceNotch {version}";

        // Minuteur à usage unique : il se désarme après l'écriture. Aucune
        // vérification périodique n'existe donc, même fenêtre ouverte.
        _persistTimer = DispatcherQueue.CreateTimer();
        _persistTimer.IsRepeating = false;
        _persistTimer.Interval = PersistDelay;
        _persistTimer.Tick += SpaceNotch_App.Diagnostics.Guard.Tick((_, _) =>
        {
            _persistTimer.Stop();
            _settings.Persist();
        });

        PathText.Text = _settings.ConfigFilePath;

        // L'aperçu suit exactement le même signal que l'Island : une seule source
        // de vérité, donc aucune possibilité d'afficher autre chose que le réel.
        _settings.Changed += OnSettingsChanged;
        SpaceNotch_App.UI.SystemTheme.Changed += OnSystemThemeChanged;

        try
        {
            _accessibility = new global::Windows.UI.ViewManagement.AccessibilitySettings();
            _accessibility.HighContrastChanged += OnHighContrastChanged;
        }
        catch (Exception)
        {
            // Sans l'événement, le changement de couleurs du thème (SystemTheme) suffit.
            _accessibility = null;
        }

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
        // Bornée à la zone de travail de l'écran principal, où elle s'ouvre : sur
        // 1366 × 768 à 125 %, le bas de la fenêtre sortait de l'écran (n° 29).
        double scale = GetDpiForWindow(handle) / 96.0;
        RectInt32 area = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        (int width, int height) = WindowFit.Fit((int)(920 * scale), (int)(660 * scale), area.Width, area.Height, (int)(16 * scale));
        appWindow.Resize(new SizeInt32(width, height));

        _appWindow = appWindow;
        _darkBackground = RootLayout.Background;
        ApplyChrome();

        // La fenêtre de réglages se place au centre de l'écran principal : les
        // réglages se lisent, ils ne doivent pas aller chercher l'utilisateur.
        appWindow.Move(new PointInt32(
            area.X + ((area.Width - appWindow.Size.Width) / 2),
            area.Y + ((area.Height - appWindow.Size.Height) / 2)));
    }

    /// <summary>
    /// Fond et barre de titre, selon le contraste élevé. Barre de titre noire,
    /// comme la fenêtre : la barre blanche par défaut coupait l'OLED en deux.
    /// Contraste élevé (phase C) : ni fond noir imposé ni barre de titre peinte —
    /// les contrôles prennent les couleurs du système, et un fond noir forcé
    /// donnait du texte noir sur noir en contraste élevé clair. Relue à chaque
    /// changement de contraste : choisie à l'ouverture seulement, elle gardait le
    /// fond noir jusqu'à ce qu'on rouvre les Réglages (n° 90).
    /// </summary>
    private void ApplyChrome()
    {
        if (_appWindow is null || _closed)
        {
            return;
        }

        bool highContrast = IsHighContrast();

        // En contraste élevé, le pinceau du système est relu à chaque fois : passer
        // d'un thème de contraste à un autre (Aquatique → Désert) ne change pas
        // l'état, mais change la couleur du fond.
        if (highContrast)
        {
            RootLayout.RequestedTheme = ElementTheme.Default;
            RootLayout.Background = Application.Current.Resources.TryGetValue("SystemColorWindowColorBrush", out object? brush) && brush is Brush window
                ? window
                : null;
        }

        if (highContrast == _chromeHighContrast)
        {
            return;
        }

        _chromeHighContrast = highContrast;

        if (!highContrast)
        {
            RootLayout.RequestedTheme = ElementTheme.Dark;
            RootLayout.Background = _darkBackground;
        }

        if (!AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        // Sans couleur (null), la barre de titre reprend celles du système.
        Color? black = highContrast ? null : Color.FromArgb(0xFF, 0x05, 0x05, 0x06);
        Color? ink = highContrast ? null : Color.FromArgb(0xFF, 0xE8, 0xE8, 0xEA);
        AppWindowTitleBar bar = _appWindow.TitleBar;
        bar.BackgroundColor = black;
        bar.InactiveBackgroundColor = black;
        bar.ForegroundColor = ink;
        bar.ButtonBackgroundColor = black;
        bar.ButtonInactiveBackgroundColor = black;
        bar.ButtonForegroundColor = ink;
        bar.ButtonHoverBackgroundColor = highContrast ? null : Color.FromArgb(0xFF, 0x1A, 0x1B, 0x1F);
        bar.ButtonHoverForegroundColor = ink;
    }

    private AppWindow? _appWindow;

    /// <summary>Le fond noir de la fenêtre (XAML), rendu en sortant du contraste élevé.</summary>
    private Brush? _darkBackground;

    /// <summary>Contraste élevé au dernier habillage ; null avant le premier.</summary>
    private bool? _chromeHighContrast;

    /// <summary>Fenêtre fermée : un changement de thème encore en file n'habille plus rien.</summary>
    private bool _closed;

    /// <summary>
    /// Signale l'entrée et la sortie du contraste élevé ; gardée en champ, sans
    /// quoi l'abonnement disparaîtrait avec l'objet.
    /// </summary>
    private global::Windows.UI.ViewManagement.AccessibilitySettings? _accessibility;

    private void OnHighContrastChanged(global::Windows.UI.ViewManagement.AccessibilitySettings sender, object args) => OnSystemThemeChanged();

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
            Border card = SettingsCard(glyph, title, description, toggle);

            // Services pour développeurs : avec les réglages avancés (n° 75, n° 23).
            if (feature.Id is FeatureKeys.Discord or FeatureKeys.Channel)
            {
                SpaceNotch_App.UI.SettingsCard.SetAdvanced(card, true);
            }

            FeaturesHost.Children.Add(card);
        }
    }

    private static (string Glyph, string Title, string Description) DescribeFeature(IIslandFeature feature) => feature.Id switch
    {
        FeatureKeys.Media => ("Music", Lang.T("Musique et vidéos", "Music and video"), Lang.T("Pochette, titre, lecture — Spotify, navigateur, Apple Music…", "Artwork, title, playback — Spotify, browser, Apple Music…")),
        FeatureKeys.Notifications => ("Notification", "Notifications", Lang.T("Un aperçu dans la notch, puis elles se rangent.", "A glimpse in the notch, then they tidy themselves away.")),
        FeatureKeys.Clipboard => ("Clipboard", Lang.T("Presse-papier", "Clipboard"), Lang.T("Les derniers éléments copiés, épinglables.", "Your latest copied items, pinnable.")),
        FeatureKeys.Bluetooth => ("Bluetooth", "Bluetooth", Lang.T("Connexion, déconnexion et batterie de tes appareils.", "Connections, disconnections and battery of your devices.")),
        FeatureKeys.VolumeHud => ("VolumeHigh", "Volume", Lang.T("Remplace l’indicateur de volume de Windows.", "Replaces the Windows volume indicator.")),
        FeatureKeys.Pomodoro => ("Timer", "Focus", Lang.T("Le minuteur « Focus 25 min » : la session finit avant ta prochaine réunion, puis une pause.", "The “Focus 25 min” timer: the session ends before your next meeting, then a break.")),
        FeatureKeys.Discord => ("Headphones", "Discord", Lang.T("Ta salle vocale Discord dans la notch. Demande une application développeur Discord.", "Your Discord voice channel in the notch. Needs a Discord developer app.")),
        FeatureKeys.Channel => ("Agent", Lang.T("Canal local", "Local channel"), Lang.T("Laisse tes agents IA (Claude Code) et tes scripts montrer où ils en sont. Rien ne sort de ton PC.", "Lets your AI agents (Claude Code) and scripts show their progress. Nothing leaves your PC.")),
        FeatureKeys.FileShelf => ("Folder", Lang.T("Étagère", "Shelf"), Lang.T("Dépose des fichiers sur la notch, reprends-les plus tard.", "Drop files on the notch, pick them up later.")),
        FeatureKeys.Downloads => ("Download", Lang.T("Téléchargements", "Downloads"), Lang.T("La progression de ce que tu télécharges.", "The progress of what you download.")),
        FeatureKeys.Charge => ("Bolt", Lang.T("Charge", "Charging"), Lang.T("La batterie qui se remplit quand tu branches le chargeur.", "The battery filling up when you plug in the charger.")),
        FeatureKeys.Monitor => ("Cpu", Lang.T("Moniteur système", "System monitor"), Lang.T("Une alerte quand le processeur sature, avec le coupable.", "An alert when the processor is maxed out, with the culprit.")),
        FeatureKeys.Meeting => ("Calendar", Lang.T("Prochain rendez-vous", "Next meeting"), Lang.T("Un compte à rebours cinq minutes avant, puis « Rejoindre ».", "A countdown five minutes before, then “Join”.")),
        FeatureKeys.Privacy => ("Camera", Lang.T("Caméra et micro", "Camera and mic"), Lang.T("Un point quand une application les utilise.", "A dot when an app is using them.")),
        _ => ("Launcher", feature.DisplayName, string.Empty)
    };

    /// <summary>Carte OLED construite en code, identique à celles du XAML.</summary>
    private Border SettingsCard(string glyph, string title, string description, FrameworkElement control)
    {
        var grid = new Grid { ColumnSpacing = 14 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new GlyphView
        {
            Key = glyph,
            Size = 16,
            VerticalAlignment = VerticalAlignment.Center
        };
        Ink(icon, GlyphView.TintProperty, "NfSettingsGlyphBrush");
        grid.Children.Add(icon);

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

        // L'ordre suit l'énumération AssistantSource.
        AssistantSourceBox.ItemsSource = new[] { Lang.T("Aucun", "None"), Lang.T("Windows (sur l’appareil)", "Windows (on device)"), "Claude" };

        DensityBox.ItemsSource = new[] { Lang.T("Compacte", "Compact"), Lang.T("Confortable", "Comfortable"), Lang.T("Aérée", "Airy") };
        // Pas d'« Automatiques » (n° 52) : rien ne s'installe sans l'utilisateur
        // tant que les mises à jour ne sont pas signées.
        UpdateModeBox.ItemsSource = new[] { Lang.T("Désactivées", "Off"), Lang.T("Me prévenir", "Ask me") };
        PixelIntensityBox.ItemsSource = new[] { Lang.T("Calme", "Calm"), Lang.T("Normal", "Normal"), Lang.T("Expressif", "Expressive") };
        TopBarBox.ItemsSource = new[] { Lang.T("Me demander", "Ask me"), Lang.T("Sous la barre", "Below the bar"), Lang.T("Sur le côté", "On the side") };
        CutoutBox.ItemsSource = new[] { Lang.T("Aucune", "None"), Lang.T("Ordinaire (200 px)", "Standard (200 px)"), Lang.T("Personnalisée", "Custom") };

        // L'ordre suit l'énumération MotionStyle : l'index sélectionné en est la valeur.
        MotionStyleBox.ItemsSource = new[] { Lang.T("Calme", "Calm"), Lang.T("Naturel", "Natural"), Lang.T("Dynamique", "Lively"), Lang.T("Personnalisé", "Custom") };

        WebSearchBox.ItemsSource = WebEngines.Select(e => e.Label).ToArray();
        HotkeyBox.ItemsSource = new[] { Lang.T("Automatique", "Automatic") }
            .Concat(SpaceNotch.Platform.Windows.Launcher.GlobalHotkey.Choices.Select(c => c.Label))
            .ToArray();
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
            CutoutBox.SelectedIndex = CutoutIndex(settings.CutoutMode);
            UpdateModeBox.SelectedIndex = (int)settings.UpdateMode;
            UpdateDescription.Text = UpdateModeDescription(settings.UpdateMode);
            CutoutWidthSlider.Value = SpaceNotch.Core.Presentation.CameraCutout.WidthFor(true, settings.CutoutWidth);

            DensityBox.SelectedIndex = (int)settings.Density;
            RadiusSlider.Value = settings.CornerRadiusBottom;
            ExpandedRadiusSlider.Value = settings.CornerRadiusExpanded;
            ShoulderSlider.Value = settings.ShoulderRadius;
            SmoothingToggle.IsOn = settings.CornerSmoothing > IslandShape.Circular;

            MotionStyleBox.SelectedIndex = (int)settings.MotionStyle;
            ResponseSlider.Value = settings.SpringResponseSeconds;
            BounceSlider.Value = settings.SpringBounce;

            BouncyToggle.IsOn = settings.AllowBouncyAnimations;
            BounceSlider.IsEnabled = settings.AllowBouncyAnimations;
            HypnoticToggle.IsOn = settings.AllowHypnoticMotion;
            RainToggle.IsOn = settings.ShowBinaryRain;
            HoverToggle.IsOn = settings.HoverToPreview;
            HoverExpandToggle.IsOn = settings.HoverToExpand;
            DetachToggle.IsOn = settings.AllowDetach;
            EdgeBox.SelectedIndex = (int)settings.DockEdge;
            SideEdgesToggle.IsOn = settings.AllowSideEdges;
            TopBarBox.SelectedIndex = (int)settings.TopBarPlacement;
            SurfaceTintBox.SelectedIndex = (int)settings.SurfaceTint;
            CustomColorBox.Text = settings.CustomSurfaceColor;
            OpacitySlider.Value = settings.SurfaceOpacity * 100;
            SideShoulderSlider.Value = settings.SideShoulderRadius;
            FloatingRadiusSlider.Value = settings.FloatingRadius;
            ShadowSlider.Value = settings.FloatingShadowOpacity * 100;
            OutlineToggle.IsOn = settings.ShowOutline;
            SoundsToggle.IsOn = settings.PlaySounds;
            WeatherCityBox.Text = settings.WeatherCity;
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
            PixelToggle.IsOn = settings.ShowPixel;
            PixelIntensityBox.SelectedIndex = (int)settings.PixelIntensity;
            PixelIntensityDescription.Text = PixelIntensityText(settings.PixelIntensity);
            AdvancedToggle.IsOn = settings.ShowAdvancedSettings;
            UpdateAgentHooksButton();
            AssistantSourceBox.SelectedIndex = (int)settings.AssistantSource;
            ClaudeModelBox.Text = settings.ClaudeModel;
            UpdateAssistantStatus(settings.AssistantSource);
            CameraMirrorToggle.IsOn = settings.CameraMirror;
            LyricsToggle.IsOn = settings.ShowLyrics;
            SpotifyIdBox.Text = settings.SpotifyClientId;
            DiscordIdBox.Text = settings.DiscordClientId;
            UpdateConnections();
            DiagnosticsToggle.IsOn = settings.EnableDiagnostics;
            CompositionToggle.IsOn = settings.UseCompositionAtmosphere;
            ClipboardSecretsToggle.IsOn = settings.ClipboardIgnoreSecrets;
            WebSearchBox.SelectedIndex = Math.Max(0, Array.FindIndex(WebEngines, e => e.Key == settings.WebSearchEngine));
            HotkeyBox.SelectedIndex = HotkeyIndex(settings.LauncherHotkey);

            int size = Array.FindIndex(ClipboardSizes, n => n >= settings.ClipboardHistoryLimit);
            ClipboardSizeBox.SelectedIndex = size < 0 ? ClipboardSizes.Length - 1 : size;

            ShowIgnoredApps(settings.IgnoredNotificationApps);

            string executable = StartupExecutable();
            StartupToggle.IsOn = executable.Length > 0
                && StartupRegistration.IsEnabled(StartupRegistration.BuildCommand(executable));

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

        // Barre en haut, bord et côtés changent aussi depuis la notch (carte, languette) :
        // la page Notch suit (n° 93).
        bool loading = _loading;
        _loading = true;

        try
        {
            TopBarBox.SelectedIndex = (int)settings.TopBarPlacement;
            SideEdgesToggle.IsOn = settings.AllowSideEdges;
            EdgeBox.SelectedIndex = (int)settings.DockEdge;
            EdgeBox.IsEnabled = settings.AllowSideEdges;
        }
        finally
        {
            _loading = loading;
        }
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
        CutoutWidthRow.Visibility = settings.CutoutMode == CameraCutoutMode.Custom ? Visibility.Visible : Visibility.Collapsed;
        CutoutWidthValue.Text = $"{SpaceNotch.Core.Presentation.CameraCutout.WidthFor(true, settings.CutoutWidth).ToString("0", CultureInfo.InvariantCulture)} px";

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

    private void OnTopBarChanged(object sender, SelectionChangedEventArgs e)
        => Apply(s => s.TopBarPlacement = (SpaceNotch.Core.Scenes.TopBarPlacement)Math.Clamp(TopBarBox.SelectedIndex, 0, 2));

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

    /// <summary>La ville de la météo s'enregistre quand on quitte le champ ou qu'on valide.</summary>
    private void OnWeatherCityCommitted(object sender, RoutedEventArgs e)
        => Apply(s => s.WeatherCity = WeatherCityBox.Text.Trim());

    private void OnWeatherCityKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == global::Windows.System.VirtualKey.Enter)
        {
            OnWeatherCityCommitted(sender, e);
            e.Handled = true;
        }
    }

    private void OnSoundsToggled(object sender, RoutedEventArgs e)
        => Apply(s => s.PlaySounds = SoundsToggle.IsOn);

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

        bool light = settings.UsesLightAppearance(SpaceNotch_App.UI.SystemTheme.IsLight);
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
        => Apply(s =>
        {
            s.CutoutMode = CutoutBox.SelectedIndex switch
            {
                1 => CameraCutoutMode.Center,
                2 => CameraCutoutMode.Custom,
                _ => CameraCutoutMode.None
            };

            // « Personnalisée » écrit la largeur que le curseur affiche : sans
            // largeur, Sanitize la ramène à « Aucune » (piège de la v1.16).
            if (s.CutoutMode == CameraCutoutMode.Custom && s.CutoutWidth <= 0)
            {
                s.CutoutWidth = SpaceNotch.Core.Presentation.CameraCutout.DefaultWidth;
            }
        });

    private void OnUpdateModeChanged(object sender, SelectionChangedEventArgs e)
    {
        var mode = (SpaceNotch.Core.Update.UpdateMode)Math.Clamp(UpdateModeBox.SelectedIndex, 0, 1);
        UpdateDescription.Text = UpdateModeDescription(mode);
        Apply(s => s.UpdateMode = mode);
    }

    /// <summary>Ce que fait le mode choisi, en une phrase (« Me prévenir » par défaut, n° 77).</summary>
    private static string UpdateModeDescription(SpaceNotch.Core.Update.UpdateMode mode) => mode switch
    {
        SpaceNotch.Core.Update.UpdateMode.Off => Lang.T("Aucune vérification : tu installes les nouvelles versions toi-même.", "No checks: you install new versions yourself."),
        _ => Lang.T("La notch te propose chaque nouvelle version ; rien ne s’installe sans ton accord.", "The notch offers each new version; nothing installs without your OK."),
    };

    private void OnCutoutWidthChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        => ApplyContinuous(s => s.CutoutWidth = e.NewValue);

    /// <summary>Position dans la liste : Aucune, Ordinaire, Personnalisée.</summary>
    private static int CutoutIndex(CameraCutoutMode mode) => mode switch
    {
        CameraCutoutMode.None => 0,
        CameraCutoutMode.Custom => 2,
        _ => 1
    };

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
    {
        // Rebond éteint : le ressort est amorti au point critique, l'amortissement
        // choisi n'agit plus (n° 91).
        BounceSlider.IsEnabled = BouncyToggle.IsOn;
        Apply(s => s.AllowBouncyAnimations = BouncyToggle.IsOn);
    }


    private void OnClockToggled(object sender, RoutedEventArgs e)
        => Apply(s => s.ShowClockAtRest = ClockToggle.IsOn);

    private void OnPixelToggled(object sender, RoutedEventArgs e)
        => Apply(s => s.ShowPixel = PixelToggle.IsOn);

    private void OnPixelIntensityChanged(object sender, SelectionChangedEventArgs e)
    {
        var intensity = (PixelIntensity)Math.Clamp(PixelIntensityBox.SelectedIndex, 0, 2);
        PixelIntensityDescription.Text = PixelIntensityText(intensity);

        // Le Narrateur lit la description du choix en cours, pas celle de l'ouverture.
        AutomationProperties.SetHelpText(PixelIntensityBox, PixelIntensityDescription.Text);
        Apply(s => s.PixelIntensity = intensity);
    }

    private static string PixelIntensityText(PixelIntensity intensity) => intensity switch
    {
        PixelIntensity.Calm => Lang.T("Immobile, il garde ses humeurs. Comme quand Windows réduit les animations.", "Still, keeps his moods. Like when Windows reduces animations."),
        PixelIntensity.Expressive => Lang.T("Plus vivant : il regarde autour de lui quand tu ne bouges pas, et bâille plus souvent le soir.", "Livelier: looks around when you sit still, and yawns more often in the evening."),
        _ => Lang.T("Il suit le curseur, cligne, jette un coup d’œil et bâille le soir.", "Follows the pointer, blinks, glances around and yawns in the evening.")
    };

    private void OnAdvancedToggled(object sender, RoutedEventArgs e)
    {
        Apply(s => s.ShowAdvancedSettings = AdvancedToggle.IsOn);

        // Hors recherche, les cartes avancées paraissent ou disparaissent aussitôt.
        if (SearchSettingsBox.Text.Trim().Length == 0)
        {
            SetCardsVisible(all: true, string.Empty);
        }
    }


    // ---- Agents IA (I4) : les hooks de Claude Code ---------------------------

    private static string ClaudeSettingsPath
        => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

    private static string? ReadClaudeSettings()
    {
        try
        {
            return System.IO.File.Exists(ClaudeSettingsPath) ? System.IO.File.ReadAllText(ClaudeSettingsPath) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void UpdateAgentHooksButton()
    {
        bool installed = SpaceNotch.Core.Channel.ClaudeHook.IsInstalled(ReadClaudeSettings());
        AgentHooksButton.Content = installed ? Lang.T("Retirer", "Remove") : Lang.T("Installer", "Install");
    }

    /// <summary>
    /// Installe ou retire les hooks. Le fichier d'origine est d'abord copié à
    /// côté (<c>settings.json.spacenotch.bak</c>) ; les autres hooks sont gardés.
    /// Rien n'est touché si le fichier existant n'est pas un JSON lisible.
    /// </summary>
    private void OnAgentHooksClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            string? current = ReadClaudeSettings();
            bool installed = SpaceNotch.Core.Channel.ClaudeHook.IsInstalled(current);
            string path = ClaudeSettingsPath;
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);

            // Barres obliques : le hook est lancé par bash (Git Bash) comme par cmd.
            string executable = (Environment.ProcessPath ?? "SpaceNotch.exe").Replace('\\', '/');

            // Calculé d'abord : un fichier illisible lève ici, avant toute écriture.
            string next = installed
                ? SpaceNotch.Core.Channel.ClaudeHook.Uninstall(current!)
                : SpaceNotch.Core.Channel.ClaudeHook.Install(current, executable);

            if (current is not null)
            {
                System.IO.File.Copy(path, path + ".spacenotch.bak", overwrite: true);
            }

            string temporary = path + ".spacenotch.tmp";
            System.IO.File.WriteAllText(temporary, next);
            System.IO.File.Move(temporary, path, overwrite: true);
        }
        catch (Exception ex)
        {
            SpaceNotch.Infrastructure.Logging.MiniLogger.Log("[HOOKS] Modification impossible", ex);
            AgentHooksDescription.Text = Lang.T("Impossible de modifier ~/.claude/settings.json : ", "Could not change ~/.claude/settings.json: ") + ex.Message;
        }

        UpdateAgentHooksButton();
    }

    // ---- Assistant (I1 à I3) : le modèle et la clé ----------------------------

    private void OnAssistantSourceChanged(object sender, SelectionChangedEventArgs e)
    {
        var source = (SpaceNotch.Core.Assistant.AssistantSource)Math.Max(0, AssistantSourceBox.SelectedIndex);
        UpdateAssistantStatus(source);
        Apply(s => s.AssistantSource = source);
    }

    /// <summary>Ce que le choix donnera vraiment sur ce PC : prêt, à télécharger, indisponible, clé manquante.</summary>
    private void UpdateAssistantStatus(SpaceNotch.Core.Assistant.AssistantSource source)
    {
        bool hasKey = SpaceNotch_App.Assistant.AssistantKeys.HasKey;
        ClaudeKeyButton.Content = hasKey ? Lang.T("Retirer", "Remove") : Lang.T("Enregistrer", "Save");
        ClaudeKeyBox.IsEnabled = !hasKey;
        ClaudeKeyText.Text = hasKey
            ? Lang.T("Une clé est enregistrée dans le coffre de Windows.", "A key is stored in the Windows vault.")
            : Lang.T("Gardée dans le coffre de Windows, jamais dans les réglages.", "Kept in the Windows vault, never in the settings file.");

        string? status = source switch
        {
            SpaceNotch.Core.Assistant.AssistantSource.Local => SpaceNotch_App.Assistant.WindowsLocalModel.State() switch
            {
                Microsoft.Windows.AI.AIFeatureReadyState.Ready => Lang.T("Phi Silica est prêt sur ce PC : rien ne quitte la machine.", "Phi Silica is ready on this PC: nothing leaves the machine."),
                Microsoft.Windows.AI.AIFeatureReadyState.NotReady => Lang.T("Phi Silica sera préparé par Windows à la première demande.", "Windows will prepare Phi Silica on first use."),
                _ => Lang.T("Ce PC n’a pas de modèle Windows (PC Copilot+ requis) : les règles locales répondent.", "This PC has no Windows model (Copilot+ PC required): local rules answer.")
            },
            SpaceNotch.Core.Assistant.AssistantSource.Claude => hasKey
                ? Lang.T("Claude répond avec ta clé. Seul le texte concerné est envoyé, jamais un mot de passe.", "Claude answers with your key. Only the text at hand is sent, never a password.")
                : Lang.T("Ajoute ta clé Claude ci-dessous pour l’activer.", "Add your Claude key below to turn it on."),
            _ => null
        };

        AssistantStatusText.Text = status ?? Lang.T(
            "Sans modèle, la notch résume et comprend les rappels avec ses propres règles. Windows : sur l’appareil, rien ne sort. Claude : avec ta clé, seul le texte concerné est envoyé.",
            "Without a model, the notch summarizes and understands reminders with its own rules. Windows: on the device, nothing leaves. Claude: with your key, only the text at hand is sent.");
    }

    private void OnClaudeKeyClicked(object sender, RoutedEventArgs e)
    {
        if (SpaceNotch_App.Assistant.AssistantKeys.HasKey)
        {
            SpaceNotch_App.Assistant.AssistantKeys.Remove();
        }
        else if (!string.IsNullOrWhiteSpace(ClaudeKeyBox.Password))
        {
            SpaceNotch_App.Assistant.AssistantKeys.Save(ClaudeKeyBox.Password.Trim());
        }

        ClaudeKeyBox.Password = string.Empty;
        var source = (SpaceNotch.Core.Assistant.AssistantSource)Math.Max(0, AssistantSourceBox.SelectedIndex);
        UpdateAssistantStatus(source);

        // Ré-appliqué pour que la notch prenne ou lâche le modèle tout de suite.
        Apply(s => s.AssistantSource = source);
    }

    private void OnClaudeModelCommitted(object sender, RoutedEventArgs e)
    {
        string model = ClaudeModelBox.Text.Trim();
        Apply(s => s.ClaudeModel = model);
    }

    // ---- Téléphone, salons et musique (vague 6d) -----------------------------

    private void OnCameraMirrorToggled(object sender, RoutedEventArgs e)
        => Apply(s => s.CameraMirror = CameraMirrorToggle.IsOn);

    private void OnLyricsToggled(object sender, RoutedEventArgs e)
        => Apply(s => s.ShowLyrics = LyricsToggle.IsOn);

    private void OnSpotifyIdCommitted(object sender, RoutedEventArgs e)
    {
        string id = SpotifyIdBox.Text.Trim();
        Apply(s => s.SpotifyClientId = id);
        UpdateConnections();
    }

    private void OnDiscordIdCommitted(object sender, RoutedEventArgs e)
    {
        string id = DiscordIdBox.Text.Trim();
        Apply(s => s.DiscordClientId = id);
        UpdateConnections();
    }

    private async void OnSpotifyClicked(object sender, RoutedEventArgs e)
    {
        if (SpaceNotch_App.Media.SpotifyClient.IsConnected)
        {
            SpaceNotch_App.Media.SpotifyClient.Disconnect();
            UpdateConnections();
            return;
        }

        string id = SpotifyIdBox.Text.Trim();

        if (!SpaceNotch.Core.Media.SpotifyApi.IsClientId(id))
        {
            SpotifyText.Text = Lang.T("Identifiant invalide : 32 caractères hexadécimaux.", "Invalid client ID: 32 hexadecimal characters.");
            return;
        }

        Apply(s => s.SpotifyClientId = id);
        SpotifyButton.IsEnabled = false;
        SpotifyText.Text = Lang.T("Accepte dans le navigateur…", "Accept in the browser…");
        bool ok;

        // async void : une exception ici fermait la notch (n° 33). Le bouton
        // revient, et l'échec se lit dans la carte.
        try
        {
            ok = await new SpaceNotch_App.Media.SpotifyClient(() => id).ConnectAsync();
        }
        catch (Exception ex)
        {
            MiniLogger.Log("[RÉGLAGES] Connexion à Spotify impossible", ex);
            ok = false;
        }

        SpotifyButton.IsEnabled = true;
        UpdateConnections();

        if (!ok)
        {
            SpotifyText.Text = Lang.T("Connexion refusée ou expirée.", "Connection refused or timed out.");
        }
    }

    private void OnDiscordClicked(object sender, RoutedEventArgs e)
    {
        bool connected = DiscordLinkConnected();

        if (connected)
        {
            SpaceNotch_App.Assistant.SecretVault.Remove(SpaceNotch_App.Phone.DiscordLink.TokenResource);
            SpaceNotch_App.Assistant.SecretVault.Remove(SpaceNotch_App.Phone.DiscordLink.SecretResource);
            SpaceNotch_App.Phone.DiscordLink.Client?.Stop();
            UpdateConnections();
            return;
        }

        string id = DiscordIdBox.Text.Trim();

        if (!SpaceNotch.Core.Social.DiscordRpc.IsClientId(id))
        {
            DiscordText.Text = Lang.T("Identifiant invalide : un nombre de 17 à 20 chiffres.", "Invalid client ID: a 17 to 20 digit number.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(DiscordSecretBox.Password))
        {
            SpaceNotch_App.Assistant.SecretVault.Save(SpaceNotch_App.Phone.DiscordLink.SecretResource, DiscordSecretBox.Password.Trim());
        }

        DiscordSecretBox.Password = string.Empty;

        if (!SpaceNotch_App.Phone.DiscordLink.HasSecret)
        {
            DiscordText.Text = Lang.T("Colle aussi le secret de l’application.", "Also paste the app secret.");
            return;
        }

        // Relier Discord, c'est vouloir sa salle vocale dans la notch : éteinte par
        // défaut (n° 75), elle s'allume par l'interrupteur, qui l'enregistre.
        if (_featureToggles.TryGetValue(FeatureKeys.Discord, out ToggleSwitch? discordToggle) && !discordToggle.IsOn)
        {
            discordToggle.IsOn = true;
        }

        Apply(s => s.DiscordClientId = id);
        SpaceNotch_App.Phone.DiscordLink.Client?.Authorize();
        DiscordText.Text = Lang.T("Accepte dans la fenêtre de Discord…", "Accept in the Discord window…");
        DiscordButton.Content = Lang.T("Déconnecter", "Disconnect");
    }

    private static bool DiscordLinkConnected()
        => SpaceNotch_App.Assistant.SecretVault.Read(SpaceNotch_App.Phone.DiscordLink.TokenResource) is not null;

    private void UpdateConnections()
    {
        bool spotify = SpaceNotch_App.Media.SpotifyClient.IsConnected;
        SpotifyButton.Content = spotify ? Lang.T("Déconnecter", "Disconnect") : Lang.T("Connecter", "Connect");

        if (spotify)
        {
            SpotifyText.Text = Lang.T("Connecté : « J’aime » et la file d’attente apparaissent pendant la lecture.", "Connected: “Like” and the queue show while playing.");
        }

        bool discord = DiscordLinkConnected();
        DiscordButton.Content = discord ? Lang.T("Déconnecter", "Disconnect") : Lang.T("Connecter", "Connect");

        if (discord)
        {
            DiscordText.Text = SpaceNotch_App.Phone.DiscordLink.Client?.State == SpaceNotch.Platform.Windows.Discord.DiscordLinkState.NotRunning
                ? Lang.T("Autorisé. Discord n’est pas lancé.", "Authorized. Discord isn’t running.")
                : Lang.T("Connecté : ton salon vocal apparaît dans la notch.", "Connected: your voice channel shows in the notch.");
        }
    }

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
    /// L'exécutable à inscrire : celui de l'installation s'il y en a une et
    /// qu'elle est toujours là. Ouverts depuis une copie de développement, les
    /// Réglages inscrivaient cette copie à la place de la notch installée, dont
    /// la clé de démarrage est partagée (n° 66) ; sans installation (portable),
    /// ou si son dossier a disparu, l'exécutable courant.
    /// </summary>
    private static string StartupExecutable()
        => SpaceNotch.Platform.Windows.Setup.WindowsSetup.FindInstalled() is { } installed && File.Exists(installed.Executable)
            ? installed.Executable
            : Environment.ProcessPath ?? string.Empty;

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

        string executable = StartupExecutable();

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
            StartupToggle.IsOn = StartupRegistration.IsEnabled(StartupRegistration.BuildCommand(executable));
            _loading = false;

            return;
        }

        // Rallumé ici après une désactivation dans le Gestionnaire des tâches :
        // sans lever celle-ci, l'inscription restait sans effet (n° 65).
        if (enabled)
        {
            StartupRegistration.ClearWindowsDisable();

            // La marque de Windows n'a pas pu être levée : l'interrupteur ne
            // prétend pas l'avoir fait.
            if (!StartupRegistration.IsEnabled(StartupRegistration.BuildCommand(executable)))
            {
                StatusText.Text = Lang.T(
                    "Windows garde le démarrage désactivé : rallume SpaceNotch dans le Gestionnaire des tâches, onglet Démarrage.",
                    "Windows keeps startup disabled: turn SpaceNotch back on in Task Manager, Startup tab.");
                _loading = true;
                StartupToggle.IsOn = false;
                _loading = false;
                return;
            }
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

    /// <summary>Le dossier du journal (n° 56) : il est dans %LocalAppData%, pas à côté de la configuration.</summary>
    private void OnOpenLogClicked(object sender, RoutedEventArgs e)
        => OpenFolder(Path.GetDirectoryName(MiniLogger.LogPath));

    /// <summary>
    /// Signaler un problème (n° 56) : la page d'un nouveau rapport s'ouvre dans le
    /// navigateur, version et Windows remplis. Rien ne part sans l'utilisateur.
    /// </summary>
    private async void OnReportProblemClicked(object sender, RoutedEventArgs e)
    {
        // Un double clic ouvrait deux rapports.
        ReportProblemButton.IsEnabled = false;

        try
        {
            if (!await global::Windows.System.Launcher.LaunchUriAsync(SpaceNotch.Core.Setup.ProblemReport.NewIssue(AppVersion(), Environment.OSVersion.Version)))
            {
                StatusText.Text = Lang.T("Impossible d’ouvrir le navigateur.", "Could not open the browser.");
            }
        }
        catch (Exception ex)
        {
            MiniLogger.Log("[RÉGLAGES] Ouverture du rapport de problème impossible", ex);
            StatusText.Text = Lang.T("Impossible d’ouvrir le navigateur.", "Could not open the browser.");
        }
        finally
        {
            ReportProblemButton.IsEnabled = true;
        }
    }

    /// <summary>La version lue dans À propos, et envoyée telle quelle dans un rapport (n° 56).</summary>
    private static string AppVersion()
        => typeof(SettingsWindow).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : string.Empty;

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
        bool advanced = _settings.Current.ShowAdvancedSettings;

        foreach (StackPanel page in Pages)
        {
            int inPage = 0;

            // Un titre de groupe dont toutes les cartes sont avancées (« Réglage fin »)
            // disparaît avec elles plutôt que de rester seul (n° 23).
            TextBlock? group = null;
            bool groupShown = false;

            foreach (UIElement child in AllChildren(page))
            {
                switch (child)
                {
                    case Border { Tag: string text } card:
                        bool match = all
                            ? advanced || !SpaceNotch_App.UI.SettingsCard.GetAdvanced(card)
                            : SpaceNotch.Core.Accessibility.SettingsSearch.Matches(
                                _cardIndex.TryGetValue(card, out string? index) ? index : SpaceNotch.Core.Accessibility.SettingsSearch.Index([text]),
                                query);
                        card.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
                        inPage += match ? 1 : 0;
                        groupShown |= match;
                        break;

                    case TextBlock label when all && IsGroupTitle(label):
                        CloseGroup(group, groupShown);
                        group = label;
                        groupShown = false;
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
            CloseGroup(group, groupShown);

            if (!all)
            {
                page.Visibility = inPage > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        return found;
    }

    private static bool IsGroupTitle(TextBlock label)
        => label.Style == (Style)Application.Current.Resources["NfSettingsGroupStyle"];

    private static void CloseGroup(TextBlock? group, bool shown)
    {
        if (group is not null)
        {
            group.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // ---- Accessibilité des cartes (phase C) ------------------------------

    /// <summary>Texte de recherche de chaque carte : son étiquette française et ce qu'elle affiche.</summary>
    private readonly Dictionary<Border, string> _cardIndex = [];

    /// <summary>
    /// Donne un nom à chaque contrôle d'une carte : le titre de la carte, et sa
    /// description en aide. Sans cela, le Narrateur disait « Interrupteur,
    /// désactivé » sans dire lequel. Un bouton qui porte son propre texte le
    /// garde (n° 58 : « Retirer » était lu « Clé Claude ») ; le titre de la carte
    /// devient son aide. Indexe aussi la carte pour la recherche, dans les deux
    /// langues.
    /// </summary>
    private void AnnotateCards()
    {
        foreach (StackPanel page in Pages)
        {
            foreach (UIElement child in AllChildren(page))
            {
                if (child is not Border { Tag: string tag } card)
                {
                    continue;
                }

                var texts = new List<TextBlock>();
                var controls = new List<FrameworkElement>();
                Collect(card.Child, texts, controls);

                string? title = texts.FirstOrDefault()?.Text;
                string? description = texts.Skip(1).FirstOrDefault()?.Text;

                foreach (FrameworkElement control in controls)
                {
                    // Un bouton à texte dit déjà ce qu'il fait : la carte dit sur quoi, et
                    // sa description ce qu'il en coûte (« Rétablir » efface des réglages).
                    var button = control as Microsoft.UI.Xaml.Controls.Primitives.ButtonBase;
                    string? own = button is null ? null : ContentText(button);
                    string? help = own is null
                        ? description
                        : string.Join(". ", new[] { title, description }.Where(t => !string.IsNullOrEmpty(t)));

                    // WinUI ne tire pas toujours le nom d'un contenu composé (glyphe + texte) :
                    // ce texte-là est posé explicitement ; un texte simple reste implicite,
                    // pour suivre « Installer » ⇄ « Retirer ».
                    string? name = own is null ? title : button!.Content is Panel ? own : null;

                    if (!string.IsNullOrEmpty(name) && string.IsNullOrEmpty(AutomationProperties.GetName(control)))
                    {
                        AutomationProperties.SetName(control, name);
                    }

                    if (!string.IsNullOrEmpty(help) && string.IsNullOrEmpty(AutomationProperties.GetHelpText(control)))
                    {
                        AutomationProperties.SetHelpText(control, help);
                    }
                }

                _cardIndex[card] = SpaceNotch.Core.Accessibility.SettingsSearch.Index([tag, .. texts.Select(t => t.Text)]);
            }
        }
    }

    /// <summary>Le texte qu'un bouton affiche, s'il en a un : c'est son nom pour le Narrateur.</summary>
    private static string? ContentText(Microsoft.UI.Xaml.Controls.Primitives.ButtonBase button) => button.Content switch
    {
        string text when !string.IsNullOrWhiteSpace(text) => text,
        TextBlock { Text: { Length: > 0 } text } => text,
        Panel panel => panel.Children.OfType<TextBlock>().Select(t => t.Text).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)),
        _ => null
    };

    /// <summary>Textes et contrôles d'une carte, dans l'ordre de lecture.</summary>
    private static void Collect(UIElement? element, List<TextBlock> texts, List<FrameworkElement> controls)
    {
        switch (element)
        {
            case null:
                return;

            case TextBlock text when !string.IsNullOrWhiteSpace(text.Text):
                texts.Add(text);
                return;

            case ToggleSwitch or ComboBox or Slider or TextBox or PasswordBox or Microsoft.UI.Xaml.Controls.Primitives.ButtonBase or NumberBox:
                controls.Add((FrameworkElement)element);
                return;

            case Border border:
                Collect(border.Child, texts, controls);
                return;

            case Panel panel:
                foreach (UIElement inner in panel.Children)
                {
                    Collect(inner, texts, controls);
                }

                return;
        }
    }

    private static bool IsHighContrast()
    {
        try
        {
            return new global::Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;
        }
        catch (Exception)
        {
            return false;
        }
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

    /// <summary>Position dans la liste : 0 pour « Automatique », puis l'ordre de GlobalHotkey.Choices.</summary>
    private static int HotkeyIndex(string? key)
    {
        int index = SpaceNotch.Platform.Windows.Launcher.GlobalHotkey.Choices.ToList().FindIndex(c => c.Key == key);
        return index < 0 ? 0 : index + 1;
    }

    private void OnHotkeyChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HotkeyBox.SelectedIndex is int index and >= 0)
        {
            string key = index == 0
                ? SpaceNotch.Platform.Windows.Launcher.GlobalHotkey.Auto
                : SpaceNotch.Platform.Windows.Launcher.GlobalHotkey.Choices[index - 1].Key;
            Apply(s => s.LauncherHotkey = key);
        }
    }

    /// <summary>Le raccourci réellement actif, sous le choix : un choix pris par une autre application cède la place au suivant.</summary>
    private void ShowHotkey()
        => ShowKeys(HotkeyCaps, _searchHotkey, Lang.T("Aucun : tous les raccourcis proposés sont pris par d'autres applications", "None: every offered shortcut is taken by other apps"));

    /// <summary>
    /// Un raccourci en touches dessinées, ou la raison de son absence. Le Narrateur
    /// lit le raccourci d'une traite (« Actif : Win+Alt+N »), pas touche par touche.
    /// </summary>
    private static void ShowKeys(StackPanel caps, string? hotkey, string none)
    {
        caps.Children.Clear();

        if (string.IsNullOrWhiteSpace(hotkey))
        {
            caps.Children.Add(new TextBlock { Text = none, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
            return;
        }

        // Sans couleur imposée : le texte hérite de celle de la carte, qui suit le
        // thème (un blanc fixe disparaissait en contraste élevé clair).
        var active = new TextBlock { Text = Lang.T("Actif :", "Active:"), FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(active, Lang.T("Actif : ", "Active: ") + hotkey);
        caps.Children.Add(active);

        foreach (string key in hotkey.Split('+'))
        {
            var label = new TextBlock { Text = key, Style = (Style)Application.Current.Resources["NfKeyCapTextStyle"] };
            AutomationProperties.SetAccessibilityView(label, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
            caps.Children.Add(new Border
            {
                Style = (Style)Application.Current.Resources["NfKeyCapStyle"],
                VerticalAlignment = VerticalAlignment.Center,
                Child = label
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

    /// <summary>
    /// Une encre des Réglages, lue dans le thème (n° 57) : en contraste élevé,
    /// la couleur du système au lieu d'un blanc translucide écrit en dur.
    /// </summary>
    private static Brush Token(string key) => (Brush)Application.Current.Resources[key];

    /// <summary>
    /// Les encres posées en code, relues quand le thème de Windows change : le
    /// XAML suit un passage en contraste élevé, elles non (relecture du n° 57).
    /// </summary>
    private readonly List<(DependencyObject Target, DependencyProperty Property, string Key)> _inks = [];

    private void Ink(DependencyObject target, DependencyProperty property, string key)
    {
        target.SetValue(property, Token(key));
        _inks.Add((target, property, key));
    }

    private void OnSystemThemeChanged()
        => DispatcherQueue.TryEnqueueSafely(() =>
        {
            foreach ((DependencyObject target, DependencyProperty property, string key) in _inks)
            {
                target.SetValue(property, Token(key));
            }

            UpdateNotificationCard();
            ShowPendingPlugins();

            // En dernier : une barre de titre refusée ne prive pas les encres de leur mise à jour.
            ApplyChrome();
        });

    private void UpdateNotificationCard()
    {
        var access = SpaceNotch.Platform.Windows.Notifications.WindowsNotificationListener.GetAccess();
        Brush green = Token("NfSettingsPositiveBrush");
        Brush dim = Token("NfSettingsNavGlyphBrush");

        (string Status, Brush Brush, string? Button) card = access switch
        {
            SpaceNotch.Platform.Windows.Notifications.NotificationAccess.Allowed => (Lang.T("● Autorisé", "● Allowed"), green, null),
            SpaceNotch.Platform.Windows.Notifications.NotificationAccess.Denied => (Lang.T("Bloqué par Windows", "Blocked by Windows"), dim, Lang.T("Ouvrir les paramètres", "Open Settings")),
            SpaceNotch.Platform.Windows.Notifications.NotificationAccess.NotAsked => (string.Empty, dim, Lang.T("Autoriser", "Allow")),
            _ when _notificationsAwaitRestart => (Lang.T("Prêtes au redémarrage", "Ready after a restart"), dim, Lang.T("Redémarrer la notch", "Restart the notch")),

            // Installée sans les notifications Windows (n° 12) : elles s'activent
            // ici, au lieu de demander de réinstaller — sauf si l'exécutable
            // installé a son identité éteinte (variante manquante) : redémarrer
            // n'y changerait rien.
            _ when InstalledWithoutNotifications() is { } product && !SpaceNotch.Platform.Windows.Setup.IdentityManifestFile.IsOn(InstalledExecutable(product))
                => (Lang.T("Réinstalle SpaceNotch pour les activer", "Reinstall SpaceNotch to turn them on"), dim, null),
            _ when InstalledWithoutNotifications() is not null => (Lang.T("Pas activées à l'installation", "Not turned on at install"), dim, Lang.T("Activer", "Turn on")),
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
        // async void : une exception ici fermait la notch (n° 33).
        try
        {
            var access = SpaceNotch.Platform.Windows.Notifications.WindowsNotificationListener.GetAccess();

            if (access == SpaceNotch.Platform.Windows.Notifications.NotificationAccess.Unavailable)
            {
                if (_notificationsAwaitRestart)
                {
                    if (RestartNotch?.Invoke() != true)
                    {
                        StatusText.Text = Lang.T("Redémarrage impossible : quitte la notch et relance-la depuis le menu Démarrer.", "Couldn't restart: quit the notch and start it again from the Start menu.");
                    }

                    return;
                }

                if (InstalledWithoutNotifications() is { } product)
                {
                    NotificationButton.IsEnabled = false;
                    _notificationsAwaitRestart = await SpaceNotch.Platform.Windows.Setup.WindowsSetup.EnableNotificationsAsync(product, MiniLogger.Log);
                    NotificationButton.IsEnabled = true;

                    if (!_notificationsAwaitRestart)
                    {
                        StatusText.Text = Lang.T("Les notifications n'ont pas pu être activées : le détail est dans le journal.", "Notifications couldn't be turned on: details are in the log.");
                    }
                }

                UpdateNotificationCard();
                return;
            }

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
        }
        catch (Exception ex)
        {
            MiniLogger.Log("[RÉGLAGES] Accès aux notifications impossible à demander", ex);
        }

        UpdateNotificationCard();
    }

    /// <summary>
    /// L'installation, si elle est là et que ce processus n'a pas d'identité
    /// (notifications non choisies à l'installation) ; sinon <c>null</c>. Une
    /// version portable n'a rien à activer.
    /// </summary>
    private static SpaceNotch.Core.Setup.InstalledProduct? InstalledWithoutNotifications()
        => !SpaceNotch.Platform.Windows.Setup.IdentityPackage.HasIdentity
            && SpaceNotch.Platform.Windows.Setup.WindowsSetup.FindInstalled() is { } product
            && File.Exists(InstalledExecutable(product))

            // Seulement dans la notch installée elle-même : une copie de
            // développement ou une version portable à côté ne doit pas
            // l'activer pour une autre (relecture de #98).
            && string.Equals(Environment.ProcessPath, InstalledExecutable(product), StringComparison.OrdinalIgnoreCase)
                ? product
                : null;

    /// <summary>L'exécutable de l'installation, tel que SpaceNotch le place — pas tel que le dit le registre.</summary>
    private static string InstalledExecutable(SpaceNotch.Core.Setup.InstalledProduct product)
        => SpaceNotch.Core.Setup.InstallLayout.For(product.Options.Scope, SpaceNotch.Platform.Windows.Setup.WindowsSetup.Folders()).Executable;

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
                Foreground = Token("NfSettingsDescriptionBrush")
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
                Foreground = Token("NfTextPrimaryBrush"),
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
        _closed = true;
        _settings.Changed -= OnSettingsChanged;
        SpaceNotch_App.UI.SystemTheme.Changed -= OnSystemThemeChanged;

        if (_accessibility is not null)
        {
            _accessibility.HighContrastChanged -= OnHighContrastChanged;
        }

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
