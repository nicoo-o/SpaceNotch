using System;
using System.Globalization;
using Microsoft.UI.Composition;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SpaceNotch.Core.Animation;
using SpaceNotch.Core.Motion;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.Setup;
using SpaceNotch.Infrastructure.Logging;
using SpaceNotch.Platform.Windows.Setup;
using SpaceNotch.Platform.Windows.System;
using SpaceNotch.Platform.Windows.Win32;
using SpaceNotch.Platform.Windows.Windowing;
using SpaceNotch_App.Animations;
using SpaceNotch_App.Composition;
using SpaceNotch_App.Windows;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using WinRT.Interop;

namespace SpaceNotch_App.Setup;

/// <summary>
/// L'installeur, et le désinstalleur : la notch elle-même.
///
/// <para>
/// Elle naît au repos en haut de l'écran principal, s'ouvre avec le ressort de
/// l'Island jusqu'à la taille de ce qu'elle a à dire, et change de hauteur à
/// chaque étape — choix, travail, fin — comme l'Island change de scène. Quand
/// tout est fait, elle se referme en notch au repos et la vraie notch prend sa
/// place au même endroit : l'installation est la première animation de
/// l'application. Voir ADR-022.
/// </para>
///
/// <para>
/// La fenêtre ne travaille pas : elle demande à <see cref="WindowsSetup"/>. Pour
/// une installation pour tous, le travail d'administrateur se fait dans un
/// processus élevé, sans fenêtre ; celle-ci n'est jamais élevée.
/// </para>
/// </summary>
public sealed partial class SetupWindow : Window
{
    /// <summary>Largeur ouverte, épaules comprises, en DIPs.</summary>
    private const double PanelWidth = 540;

    /// <summary>Hauteur maximale de la fenêtre, en DIPs : la notch n'est jamais plus haute.</summary>
    private const double WindowHeight = 460;

    /// <summary>Marge du contenu dans le corps de la notch, en DIPs.</summary>
    private const double Inset = 28;

    /// <summary>Marge sous le contenu : les grands congés du bas mangent un peu d'espace.</summary>
    private const double BottomInset = 26;

    /// <summary>Temps pendant lequel « C'est prêt » reste lisible avant la fermeture.</summary>
    private static readonly TimeSpan DoneHold = TimeSpan.FromMilliseconds(1700);

    /// <summary>
    /// Temps laissé à la vraie notch pour apparaître sous la notch de
    /// l'installeur refermée : au premier lancement, l'exécutable unique
    /// s'extrait avant de s'afficher.
    /// </summary>
    private static readonly TimeSpan HandOver = TimeSpan.FromMilliseconds(2600);

    private enum Phase
    {
        Choosing,
        Working,
        Done,
        Failed,
        Closing
    }

    private readonly SetupCommand _command;
    private readonly string _version;
    private readonly SetupText _text = SetupText.Current;
    private readonly IntPtr _hWnd;
    private readonly AppWindow _appWindow;
    private readonly IslandSpringAnimator _spring;
    private readonly HypnoticSurface? _hypnotic;
    private readonly bool _animate;
    private readonly InstalledProduct? _installed;
    private readonly InstallKind _kind;

    private Phase _phase = Phase.Choosing;
    private InstallScope _scope = InstallScope.CurrentUser;
    private IslandFootprint _target = IslandFootprint.Idle;
    private string? _launchAfterClose;
    private bool _opened;

    public SetupWindow(SetupCommand command, string version)
    {
        ArgumentNullException.ThrowIfNull(command);

        _command = command;
        _version = version;

        InitializeComponent();

        _hWnd = WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hWnd));
        _appWindow.Title = SetupIdentity.ProductName;
        AppIcon.ApplyTo(_appWindow);

        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        WindowChrome.ApplySetupSurface(_hWnd);
        PlaceAtTopOfPrimaryScreen();

        _animate = SystemVisualState.Read().UseSpringAnimations;
        _spring = new IslandSpringAnimator(SpringParameters.Default, OnFootprint, OnSpringSettled);
        _hypnotic = HypnoticSurface.TryAttach(GridHost);

        _installed = WindowsSetup.FindInstalled();
        _kind = SetupVersion.Classify(_installed?.Version, _version);
        _scope = _installed?.Options.Scope ?? InstallScope.CurrentUser;

        ContentHost.Width = PanelWidth;
        ContentPanel.Margin = new Thickness(Inset + NotchGeometry.DefaultShoulder, 22, Inset + NotchGeometry.DefaultShoulder, 0);

        ApplyTexts();
        ShowChoices();

        // Les pinceaux du thème sombre ne sont résolus qu'une fois l'arbre posé.
        RootLayout.Loaded += (_, _) => UpdateScope();

        _spring.SnapTo(IslandFootprint.Idle);

        Activated += OnActivated;
        Closed += (_, _) =>
        {
            _spring.Stop();
            _hypnotic?.Dispose();
        };
    }

    private bool IsUninstall => _command.Mode == SetupMode.Uninstall;

    // ------------------------------------------------------------------
    // Ouverture et forme
    // ------------------------------------------------------------------

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_opened)
        {
            return;
        }

        _opened = true;

        // La notch s'ouvre une fois la fenêtre à l'écran : on la voit naître
        // du repos, comme l'Island s'ouvre sur un clic.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            FitToContent();
            PrimaryButton.Focus(FocusState.Programmatic);
        });
    }

    private void PlaceAtTopOfPrimaryScreen()
    {
        // Les limites de l'écran, pas sa zone de travail : la notch est collée
        // au bord physique, comme l'Island. La fenêtre est d'abord posée sur
        // l'écran principal, puis mesurée à son échelle à lui.
        RectInt32 bounds = DisplayArea.Primary.OuterBounds;
        _appWindow.Move(new PointInt32(bounds.X + (bounds.Width / 2), bounds.Y));

        _scale = Math.Max(96, NativeMethods.GetDpiForWindow(_hWnd)) / 96.0;
        _windowX = bounds.X + ((bounds.Width - (int)Math.Ceiling(PanelWidth * _scale)) / 2);
        _windowY = bounds.Y;

        ResizeWindow(IslandFootprint.Idle.Height);
    }

    /// <summary>
    /// La fenêtre épouse la forme, au lieu d'un rectangle fixe de 460 DIP dont
    /// toute la partie vide, sous la notch, avalait les clics sur le bureau.
    /// Elle s'agrandit avant que la forme grandisse, et se resserre une fois la
    /// forme posée.
    /// </summary>
    private void ResizeWindow(double heightDip)
    {
        int width = (int)Math.Ceiling(PanelWidth * _scale);
        int height = (int)Math.Ceiling(Math.Clamp(heightDip, 1, WindowHeight) * _scale);

        if (height == _windowHeight)
        {
            return;
        }

        _windowHeight = height;
        _appWindow.MoveAndResize(new RectInt32(_windowX, _windowY, width, height));
    }

    private double _scale = 1;
    private int _windowX;
    private int _windowY;
    private int _windowHeight;

    /// <summary>Hauteur de ce qui est à dire : la notch s'y ajuste, avec son ressort.</summary>
    private void FitToContent()
    {
        ContentPanel.Measure(new Size(PanelWidth - (2 * (Inset + NotchGeometry.DefaultShoulder)), double.PositiveInfinity));

        double height = Math.Min(WindowHeight - 2, ContentPanel.Margin.Top + ContentPanel.DesiredSize.Height + BottomInset);
        AnimateTo(new IslandFootprint(PanelWidth, Math.Round(height)));
    }

    private void AnimateTo(IslandFootprint target)
    {
        _target = target;

        // Place pour le rebond du ressort, qui dépasse un peu la cible.
        double current = _spring.Current.Height;
        ResizeWindow(Math.Max(current, target.Height) * 1.08 + 4);

        if (_animate)
        {
            _spring.AnimateTo(target);
        }
        else
        {
            _spring.SnapTo(target);
            OnSpringSettled();
        }
    }

    private void OnFootprint(IslandFootprint footprint)
    {
        ShapePoint[] outline = NotchGeometry.Default.Silhouette(footprint);
        Surface.Data = IslandGeometryFactory.FromPolygons([outline], 0, 0);
        Surface.Margin = new Thickness((PanelWidth - footprint.Width) / 2, 0, 0, 0);

        // Le contenu n'existe que dans la matière : découpé à la hauteur de la
        // notch, et révélé à mesure qu'elle s'ouvre.
        ContentHost.Clip = new RectangleGeometry { Rect = new Rect(0, 0, PanelWidth, Math.Max(0, footprint.Height)) };

        double full = Math.Max(1, _target.Height - IslandFootprint.Idle.Height);
        double opened = Math.Clamp((footprint.Height - IslandFootprint.Idle.Height) / full, 0, 1);
        bool closing = _phase == Phase.Closing || _target.Height <= IslandFootprint.Idle.Height + 1;

        ContentHost.Opacity = closing
            ? Math.Clamp((footprint.Height - 120) / 120, 0, 1)
            : Smoothstep(0.55, 1.0, opened);
    }

    private void OnSpringSettled()
    {
        if (_phase != Phase.Closing)
        {
            ResizeWindow(_target.Height + 2);
            return;
        }

        if (_launchAfterClose is { } exe)
        {
            // La vraie notch naît sous celle de l'installeur, déjà au repos au
            // même endroit ; l'installeur s'efface ensuite.
            try
            {
                WindowsSetup.Launch(exe);
            }
            catch (Exception ex)
            {
                MiniLogger.Log("[SETUP] Lancement de la notch impossible", ex);
            }

            After(HandOver, FadeAndExit);
        }
        else
        {
            FadeAndExit();
        }
    }

    private void FadeAndExit()
    {
        Visual visual = ElementCompositionPreview.GetElementVisual(RootLayout);
        Compositor compositor = visual.Compositor;

        ScalarKeyFrameAnimation fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1f, 0f);
        fade.Duration = TimeSpan.FromMilliseconds(_animate ? 220 : 1);

        CompositionScopedBatch batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        visual.StartAnimation("Opacity", fade);
        batch.End();
        batch.Completed += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            Close();
            Application.Current.Exit();
        });
    }

    private void CloseNotch(string? launch = null)
    {
        _launchAfterClose = launch;
        _phase = Phase.Closing;
        _hypnotic?.SetPreset(HypnoticPreset.None, animate: false);
        AnimateTo(IslandFootprint.Idle);
    }

    // ------------------------------------------------------------------
    // Étapes
    // ------------------------------------------------------------------

    private void ApplyTexts()
    {
        ForMeTitle.Text = _text.ForMe;
        ForMeDetail.Text = _text.ForMeDetail;
        ForEveryoneTitle.Text = _text.ForEveryone;
        ForEveryoneDetail.Text = _text.ForEveryoneDetail;
        StartupCheck.Content = _text.StartWithWindows;
        DesktopCheck.Content = _text.DesktopShortcut;
        RemoveSettingsCheck.Content = _text.RemoveSettings;
        KeepsChoicesText.Text = _text.KeepsChoices;
        UninstallDetailText.Text = _text.UninstallDetail;

        AutomationProperties.SetName(ForMeButton, $"{_text.ForMe}, {_text.ForMeDetail}");
        AutomationProperties.SetName(ForEveryoneButton, $"{_text.ForEveryone}, {_text.ForEveryoneDetail}");
    }

    private void ShowChoices(string? message = null)
    {
        _phase = Phase.Choosing;

        bool fresh = !IsUninstall && _kind == InstallKind.Fresh;

        TitleText.Text = IsUninstall ? _text.UninstallTitle : SetupIdentity.ProductName;
        HeadlineText.Text = IsUninstall
            ? string.Empty
            : _text.Headline(_kind, _installed?.Version, _version);
        HeadlineText.Visibility = string.IsNullOrEmpty(HeadlineText.Text) ? Visibility.Collapsed : Visibility.Visible;

        OptionsPanel.Visibility = fresh ? Visibility.Visible : Visibility.Collapsed;
        KeepsChoicesText.Visibility = !IsUninstall && !fresh ? Visibility.Visible : Visibility.Collapsed;
        UninstallPanel.Visibility = IsUninstall && _installed is not null ? Visibility.Visible : Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Collapsed;

        MessageText.Text = message ?? (IsUninstall && _installed is null ? _text.NotInstalled : string.Empty);
        MessageText.Visibility = string.IsNullOrEmpty(MessageText.Text) ? Visibility.Collapsed : Visibility.Visible;

        if (IsUninstall && _installed is null)
        {
            SetActions(primary: _text.Close, secondary: null);
        }
        else
        {
            SetActions(
                primary: IsUninstall ? _text.Uninstall : _text.PrimaryAction(_kind),
                secondary: _text.Cancel);
        }

        UpdateScope();
        _hypnotic?.SetPreset(HypnoticPreset.Read, _animate);

        if (_opened)
        {
            FitToContent();
        }
    }

    private void ShowWorking(string step)
    {
        _phase = Phase.Working;

        OptionsPanel.Visibility = Visibility.Collapsed;
        KeepsChoicesText.Visibility = Visibility.Collapsed;
        UninstallPanel.Visibility = Visibility.Collapsed;
        MessageText.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Visible;
        ActionsPanel.Visibility = Visibility.Collapsed;

        StepText.Text = step;
        Progress.IsIndeterminate = false;
        Progress.Value = 0;

        _hypnotic?.SetPreset(IsUninstall ? HypnoticPreset.Drop : HypnoticPreset.Process, _animate);
        FitToContent();
    }

    private void ShowDone(string title, string detail)
    {
        _phase = Phase.Done;

        TitleText.Text = title;
        HeadlineText.Text = detail;
        HeadlineText.Visibility = Visibility.Visible;
        ProgressPanel.Visibility = Visibility.Collapsed;
        ActionsPanel.Visibility = Visibility.Collapsed;

        _hypnotic?.SetPreset(HypnoticPreset.Complete, _animate);
        FitToContent();
    }

    private void ShowFailed(string message)
    {
        _phase = Phase.Failed;

        TitleText.Text = _text.Failed;
        HeadlineText.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Collapsed;
        MessageText.Text = message;
        MessageText.Visibility = Visibility.Visible;
        ActionsPanel.Visibility = Visibility.Visible;
        SetActions(primary: _text.Retry, secondary: _text.Close);

        _hypnotic?.SetPreset(HypnoticPreset.Error, _animate);
        FitToContent();
    }

    private void SetActions(string primary, string? secondary)
    {
        ActionsPanel.Visibility = Visibility.Visible;
        PrimaryButton.Content = primary;
        SecondaryButton.Content = secondary;
        SecondaryButton.Visibility = secondary is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateScope()
    {
        InstallLayout layout = InstallLayout.For(_scope, WindowsSetup.Folders());
        FolderText.Text = _text.InstallFolder(layout.Directory);

        StyleChoice(ForMeButton, _scope == InstallScope.CurrentUser);
        StyleChoice(ForEveryoneButton, _scope == InstallScope.AllUsers);
    }

    /// <summary>
    /// Le choix se lit à son cadre, porté par une bordure autour du bouton et
    /// non par le bouton : les états de survol et d'appui du bouton ne peuvent
    /// donc plus l'effacer — ni le faire clignoter.
    /// </summary>
    private void StyleChoice(Button button, bool chosen)
    {
        Border frame = ReferenceEquals(button, ForMeButton) ? ForMeFrame : ForEveryoneFrame;
        Brush? stroke = chosen ? ChosenSwatch.Stroke : ChoiceSwatch.Stroke;

        if (stroke is not null)
        {
            frame.BorderBrush = stroke;
        }

        AutomationProperties.SetItemStatus(button, chosen ? "✓" : string.Empty);
    }

    // ------------------------------------------------------------------
    // Actions
    // ------------------------------------------------------------------

    private void OnForMeClick(object sender, RoutedEventArgs e)
    {
        _scope = InstallScope.CurrentUser;
        UpdateScope();
    }

    private void OnForEveryoneClick(object sender, RoutedEventArgs e)
    {
        _scope = InstallScope.AllUsers;
        UpdateScope();
    }

    private void OnSecondaryClick(object sender, RoutedEventArgs e)
    {
        if (_phase is Phase.Choosing or Phase.Failed)
        {
            CloseNotch();
        }
    }

    private async void OnPrimaryClick(object sender, RoutedEventArgs e)
    {
        switch (_phase)
        {
            case Phase.Choosing when IsUninstall && _installed is null:
                CloseNotch();
                return;

            case Phase.Choosing:
            case Phase.Failed:
                break;

            default:
                return;
        }

        try
        {
            if (IsUninstall)
            {
                await UninstallAsync().ConfigureAwait(true);
            }
            else
            {
                await InstallAsync().ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            MiniLogger.Log("[SETUP] Erreur inattendue", ex);
            ShowFailed(ex.Message);
        }
    }

    private async System.Threading.Tasks.Task InstallAsync()
    {
        InstallOptions options = _installed is not null && _kind != InstallKind.Fresh
            ? _installed.Options
            : new InstallOptions(_scope, StartupCheck.IsChecked == true, DesktopCheck.IsChecked == true);

        ShowWorking(_text.For(InstallStep.Preparing));

        var progress = new Progress<SetupProgress>(OnProgress);
        SetupOutcome outcome = await WindowsSetup.InstallAsync(options, _version, progress, Log).ConfigureAwait(true);

        switch (outcome)
        {
            case SetupOutcome.Succeeded:
                ShowDone(_text.Done, _text.DoneDetail);
                string exe = InstallLayout.For(options.Scope, WindowsSetup.Folders()).Executable;
                After(DoneHold, () => CloseNotch(launch: exe));
                break;

            case SetupOutcome.ElevationDeclined:
                ShowChoices(_text.ElevationDeclined);
                break;

            default:
                ShowFailed(string.Empty);
                break;
        }
    }

    private async System.Threading.Tasks.Task UninstallAsync()
    {
        if (_installed is null)
        {
            return;
        }

        ShowWorking(_text.Removing);
        Progress.IsIndeterminate = true;

        SetupOutcome outcome = await WindowsSetup
            .UninstallAsync(_installed, RemoveSettingsCheck.IsChecked == true, Log)
            .ConfigureAwait(true);

        switch (outcome)
        {
            case SetupOutcome.Succeeded:
                ShowDone(_text.Removed, _text.RemovedDetail);
                After(DoneHold, () => CloseNotch());
                break;

            case SetupOutcome.ElevationDeclined:
                ShowChoices(_text.ElevationDeclined);
                break;

            default:
                ShowFailed(string.Empty);
                break;
        }
    }

    private void OnProgress(SetupProgress progress)
    {
        if (_phase != Phase.Working)
        {
            return;
        }

        StepText.Text = _text.For(progress.Step);
        Progress.IsIndeterminate = progress.Indeterminate;

        if (!progress.Indeterminate)
        {
            Progress.Value = progress.Overall;
        }
    }

    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape && _phase is Phase.Choosing or Phase.Failed)
        {
            e.Handled = true;
            CloseNotch();
        }
    }

    // ------------------------------------------------------------------
    // Outils
    // ------------------------------------------------------------------

    private static void Log(string message) => MiniLogger.Log(message);

    private void After(TimeSpan delay, Action action)
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.IsRepeating = false;
        timer.Interval = delay;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            action();
        };
        timer.Start();
    }

    private static double Smoothstep(double from, double to, double x)
    {
        double t = Math.Clamp((x - from) / (to - from), 0, 1);
        return t * t * (3 - (2 * t));
    }

    internal static string Describe(SetupCommand command)
        => string.Create(CultureInfo.InvariantCulture, $"{command.Mode} {command.ToCommandLine()}");
}
