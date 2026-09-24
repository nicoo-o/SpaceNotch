using System;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Motion;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Platform.Windows.Windowing;
using SpaceNotch_App.Composition;
using SpaceNotch_App.Views;
using Windows.Graphics;
using WinRT.Interop;

namespace SpaceNotch_App.Windows;

/// <summary>
/// La bulle : une activité importante qui partage la notch au lieu d'attendre
/// derrière elle. Voir <see cref="SplitPresentation"/> et ADR-019.
///
/// <para>
/// Elle ne décide de rien. L'Island lui dit quoi montrer, où, et sous quelle
/// forme — accrochée au bord avec ses épaules, ou ronde derrière une notch
/// détachée. La bulle dessine, anime son apparition et signale qu'on l'a
/// touchée ; l'échange des rôles appartient à l'Island.
/// </para>
///
/// <para>
/// Les apparitions et l'échange sont des ressorts du compositeur : aucune boucle
/// sur le fil d'interface, rien ne tourne quand la bulle est posée.
/// </para>
/// </summary>
public sealed partial class BubbleWindow : Window
{
    /// <summary>Échelle d'où la bulle naît : elle sort du bord, elle n'apparaît pas en fondu.</summary>
    private const float BirthScale = 0.2f;

    /// <summary>Échelle au creux de l'échange, quand son contenu change.</summary>
    private const float SwapScale = 0.55f;

    private readonly IntPtr _hWnd;
    private readonly AppWindow _appWindow;
    private readonly IslandGeometryFactory _shape = new();

    private HypnoticSurface? _hypnotic;
    private Visual? _bodyVisual;

    private int _lastX = int.MinValue;
    private int _lastY = int.MinValue;
    private int _lastWidth = int.MinValue;
    private int _lastHeight = int.MinValue;

    private bool _windowShown;
    private bool _shown;
    private bool _floating;
    private string? _activityId;

    public BubbleWindow()
    {
        InitializeComponent();

        _hWnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hWnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        // Mêmes styles que l'Island : hors barre des tâches, hors Alt+Tab, sans
        // jamais prendre le focus à l'application au premier plan.
        WindowChrome.ApplyInteractiveSurface(_hWnd);

        _hypnotic = HypnoticSurface.TryAttach(BubbleHypnoticHost);

        Closed += (_, _) => _hypnotic?.Dispose();
    }

    /// <summary>La bulle a été touchée : l'Island échange les rôles.</summary>
    public event EventHandler<string>? Invoked;

    /// <summary>Le pointeur est sur la bulle : l'Island garde sa forme d'aperçu.</summary>
    public event EventHandler<bool>? Hovered;

    public IntPtr Handle => _hWnd;

    /// <summary>Vrai tant que la bulle est montrée, ou en train d'apparaître.</summary>
    public bool IsShown => _shown;

    /// <summary>Identifiant de l'activité que la bulle porte.</summary>
    public string? ActivityId => _activityId;

    public bool UseSpringAnimations { get; set; } = true;

    /// <summary>Teinte de la surface : la même que celle de la notch.</summary>
    public void SetSurface(Brush brush) => BubbleFill.Fill = brush;

    /// <summary>
    /// Montre une activité. Une activité nouvelle naît du bord ; un changement
    /// d'activité — l'échange — se fait par un creux et un rebond.
    /// </summary>
    /// <param name="activity">Activité à porter.</param>
    /// <param name="preset">Mouvement hypnotique de son travail, ou <c>None</c> pour son glyphe.</param>
    /// <param name="animateMotion">Faux pour figer le motif : réduction des animations ou apaisement.</param>
    public void Show(IslandActivity activity, HypnoticPreset preset, bool animateMotion)
    {
        ArgumentNullException.ThrowIfNull(activity);

        bool swap = _shown && !string.Equals(_activityId, activity.Id, StringComparison.Ordinal);
        bool birth = !_shown;

        _activityId = activity.Id;

        if (swap && UseSpringAnimations)
        {
            PlaySwap(() => ApplyContent(activity, preset, animateMotion));
        }
        else
        {
            ApplyContent(activity, preset, animateMotion);
        }

        if (!birth)
        {
            return;
        }

        _shown = true;

        if (!_windowShown)
        {
            _appWindow.Show(activateWindow: false);
            _windowShown = true;
        }

        PlayScale(from: UseSpringAnimations ? BirthScale : 1f, to: 1f, bouncy: true);
    }

    /// <summary>Retire la bulle : elle rentre dans le bord, puis sa fenêtre disparaît.</summary>
    public void HideBubble()
    {
        if (!_shown)
        {
            return;
        }

        _shown = false;
        _activityId = null;
        _hypnotic?.SetPreset(HypnoticPreset.None, animate: false);

        if (!UseSpringAnimations || EnsureBodyVisual() is not { } visual)
        {
            HideWindow();
            return;
        }

        Compositor compositor = visual.Compositor;
        ScalarKeyFrameAnimation shrink = compositor.CreateScalarKeyFrameAnimation();
        shrink.InsertKeyFrame(1f, BirthScale, compositor.CreateCubicBezierEasingFunction(new Vector2(0.4f, 0f), new Vector2(1f, 1f)));
        shrink.Duration = TimeSpan.FromMilliseconds(160);

        CompositionScopedBatch batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        visual.StartAnimation("Scale.X", shrink);
        visual.StartAnimation("Scale.Y", shrink);
        batch.End();
        batch.Completed += (_, _) =>
        {
            // Une réapparition pendant la sortie l'emporte : la fenêtre reste.
            if (!_shown)
            {
                HideWindow();
            }
        };
    }

    /// <summary>
    /// Place la bulle, en pixels physiques pour la fenêtre et en DIPs pour le
    /// tracé. Accrochée, la forme est une mini-notch à épaules ; flottante, un
    /// disque.
    /// </summary>
    public void Place(int x, int y, int widthPx, int heightPx, IslandFootprint footprint, bool floating)
    {
        if (x != _lastX || y != _lastY || widthPx != _lastWidth || heightPx != _lastHeight)
        {
            _lastX = x;
            _lastY = y;
            _lastWidth = widthPx;
            _lastHeight = heightPx;

            _appWindow.MoveAndResize(new RectInt32(x, y, widthPx, heightPx));
        }

        BubbleBody.Width = footprint.Width;
        BubbleBody.Height = footprint.Height;

        NotchGeometry geometry = NotchGeometry.Default;
        double shoulder = floating ? 0 : SplitPresentation.BubbleShoulder;
        double radius = floating
            ? footprint.Height / 2
            : IslandShape.EffectiveRadius(footprint.Width, footprint.Height, footprint.Height, shoulder);

        if (floating != _floating)
        {
            _floating = floating;
            _shape.Forget();
        }

        // Flottante, la bulle est un disque : un cercle, pas un squircle.
        double smoothing = floating ? IslandShape.Circular : geometry.Smoothing;
        Geometry? silhouette = _shape.Build(footprint, radius, smoothing, shoulder: shoulder, floating: floating);

        if (silhouette is not null)
        {
            BubbleFill.Data = silhouette;
        }

        // Le contenu se centre sous les épaules, pas sur toute la largeur : les
        // épaules appartiennent au bord de l'écran.
        BubbleContent.Margin = new Thickness(shoulder, 0, shoulder, 0);

        // L'échelle d'apparition part du bord pour une bulle accrochée — elle en
        // sort — et du centre pour une bulle flottante.
        if (EnsureBodyVisual() is { } visual)
        {
            visual.CenterPoint = new Vector3((float)(footprint.Width / 2), floating ? (float)(footprint.Height / 2) : 0f, 0f);
        }
    }

    private void ApplyContent(IslandActivity activity, HypnoticPreset preset, bool animateMotion)
    {
        bool hypnotic = _hypnotic is not null && preset != HypnoticPreset.None;

        BubbleHypnoticHost.Visibility = hypnotic ? Visibility.Visible : Visibility.Collapsed;
        BubbleGlyph.Visibility = hypnotic ? Visibility.Collapsed : Visibility.Visible;
        BubbleGlyph.Glyph = GlyphCatalog.Resolve(activity.IconKey);
        BubbleGlyph.Foreground = StatePalette.Brush(activity.State);

        _hypnotic?.SetPreset(hypnotic ? preset : HypnoticPreset.None, animateMotion);

        // La bulle n'a pas de texte visible : son nom est ce que Narrateur dit.
        string name = string.IsNullOrWhiteSpace(activity.Source)
            ? activity.Title ?? string.Empty
            : $"{activity.Title} — {activity.Source}";

        AutomationProperties.SetName(BubbleRoot, name);
        ToolTipService.SetToolTip(BubbleRoot, name);
    }

    private void PlaySwap(Action switchContent)
    {
        if (EnsureBodyVisual() is not { } visual)
        {
            switchContent();
            return;
        }

        Compositor compositor = visual.Compositor;
        ScalarKeyFrameAnimation dip = compositor.CreateScalarKeyFrameAnimation();
        dip.InsertKeyFrame(1f, SwapScale, compositor.CreateCubicBezierEasingFunction(new Vector2(0.3f, 0f), new Vector2(0.8f, 0.4f)));
        dip.Duration = TimeSpan.FromMilliseconds(120);

        CompositionScopedBatch batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        visual.StartAnimation("Scale.X", dip);
        visual.StartAnimation("Scale.Y", dip);
        batch.End();
        batch.Completed += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            switchContent();
            PlayScale(from: SwapScale, to: 1f, bouncy: true);
        });
    }

    /// <summary>
    /// Ressort du compositeur sur l'échelle. Rebondissant pour une naissance ou
    /// un retour d'échange : c'est la goutte qui prend sa forme.
    /// </summary>
    private void PlayScale(float from, float to, bool bouncy)
    {
        if (EnsureBodyVisual() is not { } visual)
        {
            return;
        }

        if (!UseSpringAnimations || Math.Abs(from - to) < 0.001f)
        {
            visual.StopAnimation("Scale.X");
            visual.StopAnimation("Scale.Y");
            visual.Scale = new Vector3(to, to, 1f);
            return;
        }

        visual.Scale = new Vector3(from, from, 1f);

        Compositor compositor = visual.Compositor;
        SpringScalarNaturalMotionAnimation spring = compositor.CreateSpringScalarAnimation();
        spring.InitialValue = from;
        spring.FinalValue = to;
        spring.DampingRatio = bouncy ? 0.5f : 0.85f;
        spring.Period = TimeSpan.FromMilliseconds(55);

        visual.StartAnimation("Scale.X", spring);
        visual.StartAnimation("Scale.Y", spring);
    }

    private Visual? EnsureBodyVisual()
    {
        if (_bodyVisual is not null)
        {
            return _bodyVisual;
        }

        try
        {
            _bodyVisual = ElementCompositionPreview.GetElementVisual(BubbleBody);
        }
        catch (Exception)
        {
            _bodyVisual = null;
        }

        return _bodyVisual;
    }

    private void HideWindow()
    {
        if (!_windowShown)
        {
            return;
        }

        _appWindow.Hide();
        _windowShown = false;
    }

    private void OnBubblePressed(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;

        if (_activityId is { } id)
        {
            Invoked?.Invoke(this, id);
        }
    }

    private void OnBubblePointerEntered(object sender, PointerRoutedEventArgs e) => Hovered?.Invoke(this, true);

    private void OnBubblePointerExited(object sender, PointerRoutedEventArgs e) => Hovered?.Invoke(this, false);
}
