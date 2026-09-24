using System;
using System.Collections.Generic;
using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using NotchFlow.Core.Motion;
using Windows.UI;

namespace NotchFlow_App.Composition;

/// <summary>
/// Rendu du mouvement hypnotique : une source lumineuse, son halo et quelques
/// particules, confiés au compositeur.
///
/// <para>
/// <b>Aucune image n'est calculée ici.</b> Le champ est une fonction pure du
/// temps, décrite dans le cœur (<see cref="HypnoticField"/>). Cette classe en
/// échantillonne une boucle, la transforme en images clés, et le compositeur la
/// rejoue seul sur le GPU. Le fil d'interface est libre pendant que la matière
/// bouge ; au repos, les animations sont arrêtées et les visuels masqués — le
/// moteur ne coûte rien.
/// </para>
///
/// <para>
/// <b>La lumière appartient à la notch.</b> Les visuels sont attachés à un
/// élément de la notch et ne débordent que de leur halo : la matière vit dans la
/// surface et se diffuse éventuellement dans l'atmosphère, elle ne flotte jamais
/// hors de la forme.
/// </para>
///
/// <para>
/// Comme les autres décorations, la classe est tolérante : si le compositeur
/// refuse les visuels, <see cref="TryAttach"/> renvoie <c>null</c> et l'appelant
/// garde son glyphe fixe.
/// </para>
/// </summary>
public sealed class HypnoticSurface : IDisposable
{
    /// <summary>Diamètre de la source, relatif au demi-côté de l'hôte.</summary>
    private const float CoreDiameter = 1.05f;

    /// <summary>Diamètre du halo : il déborde volontairement de l'hôte, dans la surface noire.</summary>
    private const float HaloDiameter = 3.2f;

    /// <summary>Diamètre d'une particule à l'échelle 1.</summary>
    private const float MoteDiameter = 0.95f;

    /// <summary>Part de l'hôte que les particules parcourent : elles n'en touchent jamais le bord.</summary>
    private const float Travel = 0.88f;

    private static readonly string[] AnimatedProperties = ["Offset", "Scale", "Opacity"];

    private readonly Compositor _compositor;
    private readonly FrameworkElement _host;
    private readonly ContainerVisual _root;
    private readonly SpriteVisual _halo;
    private readonly SpriteVisual _core;
    private readonly SpriteVisual[] _motes = new SpriteVisual[HypnoticField.MoteCount];
    private readonly List<CompositionColorGradientStop> _tintedStops = [];
    private readonly List<CompositionObject> _owned = [];
    private readonly CompositionEasingFunction _linear;

    private HypnoticPreset _preset = HypnoticPreset.None;
    private bool _animate = true;
    private Color _tint = Color.FromArgb(0xFF, 0xFF, 0xB4, 0x6A);
    private CompositionScopedBatch? _batch;
    private bool _disposed;

    private HypnoticSurface(Compositor compositor, FrameworkElement host)
    {
        _compositor = compositor;
        _host = host;
        _linear = compositor.CreateLinearEasingFunction();

        _root = compositor.CreateContainerVisual();
        _root.IsVisible = false;

        // Ordre de dessin : le halo sous la source, les particules au-dessus —
        // elles passent devant la lumière, ce qui donne la profondeur.
        _halo = CreateGlow(Color.FromArgb(0x90, _tint.R, _tint.G, _tint.B), 0.0f);
        _core = CreateGlow(Colors.White, 0.28f);

        _root.Children.InsertAtTop(_halo);
        _root.Children.InsertAtTop(_core);

        for (int i = 0; i < _motes.Length; i++)
        {
            _motes[i] = CreateGlow(Color.FromArgb(0xFF, 0xFF, 0xF1, 0xDC), 0.18f);
            _root.Children.InsertAtTop(_motes[i]);
        }

        ElementCompositionPreview.SetElementChildVisual(host, _root);

        _host.SizeChanged += OnHostSizeChanged;

        IsAvailable = true;
    }

    /// <summary>Vrai lorsque le compositeur a accepté les visuels.</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>Préréglage affiché.</summary>
    public HypnoticPreset Preset => _preset;

    /// <summary>
    /// Signalé à la fin d'un préréglage ponctuel — achèvement ou échec — pour
    /// que l'appelant puisse passer à la suite : retour au repos, résultat.
    /// </summary>
    public event EventHandler<HypnoticPreset>? OneShotCompleted;

    /// <summary>
    /// Tente d'attacher le rendu à un élément hôte. Renvoie <c>null</c> si le
    /// compositeur refuse : une décoration ne doit jamais empêcher la notch de
    /// s'afficher.
    /// </summary>
    public static HypnoticSurface? TryAttach(FrameworkElement host)
    {
        ArgumentNullException.ThrowIfNull(host);

        try
        {
            Compositor? compositor = ElementCompositionPreview.GetElementVisual(host).Compositor;

            return compositor is null ? null : new HypnoticSurface(compositor, host);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Change la teinte de la lumière. La source reste blanche en son cœur :
    /// c'est ce qui la fait lire comme une lumière et non comme une pastille
    /// colorée.
    /// </summary>
    public void SetTint(Color tint)
    {
        if (_disposed || (tint.R == _tint.R && tint.G == _tint.G && tint.B == _tint.B))
        {
            return;
        }

        _tint = tint;

        foreach (CompositionColorGradientStop stop in _tintedStops)
        {
            stop.Color = Color.FromArgb(stop.Color.A, tint.R, tint.G, tint.B);
        }
    }

    /// <summary>
    /// Affiche un préréglage.
    /// </summary>
    /// <param name="preset">Préréglage ; <see cref="HypnoticPreset.None"/> arrête et masque.</param>
    /// <param name="animate">
    /// Faux sous réduction des animations : la composition est posée fixe, sur
    /// une image représentative. L'information reste, le mouvement part.
    /// </param>
    public void SetPreset(HypnoticPreset preset, bool animate)
    {
        if (_disposed)
        {
            return;
        }

        bool unchanged = preset == _preset && animate == _animate;

        _preset = preset;
        _animate = animate;

        // Une boucle déjà en cours n'est pas relancée : la relancer ferait
        // sauter la matière à son image de départ à chaque rendu.
        if (unchanged && preset != HypnoticPreset.None && HypnoticField.IsLooping(preset))
        {
            return;
        }

        Apply();
    }

    private void OnHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Les positions sont exprimées en DIPs de l'hôte : une taille nouvelle
        // oblige à recuire les images clés, ce qui n'arrive qu'au changement de
        // palier, jamais à chaque image.
        if (_preset != HypnoticPreset.None)
        {
            Apply();
        }
    }

    private void Apply()
    {
        StopAll();

        if (_preset == HypnoticPreset.None)
        {
            _root.IsVisible = false;
            return;
        }

        var size = new Vector2((float)_host.ActualWidth, (float)_host.ActualHeight);

        if (size.X <= 0 || size.Y <= 0)
        {
            // L'hôte n'est pas encore disposé : SizeChanged rappellera.
            return;
        }

        _root.IsVisible = true;
        _root.Size = size;

        float unit = Math.Min(size.X, size.Y) / 2;

        SetDiameter(_halo, unit * HaloDiameter);
        SetDiameter(_core, unit * CoreDiameter);

        foreach (SpriteVisual mote in _motes)
        {
            SetDiameter(mote, unit * MoteDiameter);
        }

        if (!_animate)
        {
            Pose(HypnoticField.StaticFrame(_preset), size);
            return;
        }

        Animate(size);
    }

    /// <summary>Pose une image fixe, sans aucune animation.</summary>
    private void Pose(HypnoticFrame frame, Vector2 size)
    {
        Vector2 centre = size / 2;
        Vector2 reach = centre * Travel;

        _core.Offset = new Vector3(centre.X + ((float)frame.CoreOffsetX * reach.X), centre.Y, 0);
        _core.Scale = Uniform(frame.CoreScale);
        _core.Opacity = (float)frame.CoreIntensity;

        _halo.Offset = new Vector3(centre, 0);
        _halo.Scale = Uniform(frame.HaloScale);
        _halo.Opacity = (float)frame.HaloIntensity;

        for (int i = 0; i < _motes.Length; i++)
        {
            HypnoticMote mote = frame.Motes[i];
            _motes[i].Offset = MotePosition(mote, centre, reach);
            _motes[i].Scale = Uniform(mote.Scale);
            _motes[i].Opacity = (float)mote.Intensity;
        }
    }

    /// <summary>
    /// Cuit le préréglage en images clés et les confie au compositeur.
    /// </summary>
    private void Animate(Vector2 size)
    {
        IReadOnlyList<(double Progress, HypnoticFrame Frame)> samples = HypnoticField.Sample(_preset);
        bool loop = HypnoticField.IsLooping(_preset);
        TimeSpan duration = TimeSpan.FromSeconds(HypnoticField.PeriodSeconds(_preset));

        Vector2 centre = size / 2;
        Vector2 reach = centre * Travel;

        // La première image est posée directement : pendant l'instant qui précède
        // le démarrage des animations, la matière est déjà à sa place.
        Pose(samples[0].Frame, size);

        CompositionScopedBatch? batch = loop ? null : _compositor.CreateScopedBatch(CompositionBatchTypes.Animation);

        Start(_core, samples, duration, loop,
            frame => new Vector3(centre.X + ((float)frame.CoreOffsetX * reach.X), centre.Y, 0),
            frame => Uniform(frame.CoreScale),
            frame => frame.CoreIntensity);

        Start(_halo, samples, duration, loop,
            _ => new Vector3(centre, 0),
            frame => Uniform(frame.HaloScale),
            frame => frame.HaloIntensity);

        for (int i = 0; i < _motes.Length; i++)
        {
            int index = i;

            Start(_motes[i], samples, duration, loop,
                frame => MotePosition(frame.Motes[index], centre, reach),
                frame => Uniform(frame.Motes[index].Scale),
                frame => frame.Motes[index].Intensity);
        }

        if (batch is null)
        {
            return;
        }

        HypnoticPreset finished = _preset;

        batch.End();
        batch.Completed += (_, _) =>
        {
            if (!_disposed && _batch == batch)
            {
                _batch = null;
                OneShotCompleted?.Invoke(this, finished);
            }
        };

        _batch = batch;
    }

    private void Start(
        SpriteVisual visual,
        IReadOnlyList<(double Progress, HypnoticFrame Frame)> samples,
        TimeSpan duration,
        bool loop,
        Func<HypnoticFrame, Vector3> offset,
        Func<HypnoticFrame, Vector3> scale,
        Func<HypnoticFrame, double> opacity)
    {
        Vector3KeyFrameAnimation offsets = _compositor.CreateVector3KeyFrameAnimation();
        Vector3KeyFrameAnimation scales = _compositor.CreateVector3KeyFrameAnimation();
        ScalarKeyFrameAnimation opacities = _compositor.CreateScalarKeyFrameAnimation();

        foreach ((double progress, HypnoticFrame frame) in samples)
        {
            float key = (float)progress;

            offsets.InsertKeyFrame(key, offset(frame), _linear);
            scales.InsertKeyFrame(key, scale(frame), _linear);
            opacities.InsertKeyFrame(key, (float)Math.Clamp(opacity(frame), 0, 1), _linear);
        }

        foreach (KeyFrameAnimation animation in new KeyFrameAnimation[] { offsets, scales, opacities })
        {
            animation.Duration = duration;
            animation.IterationBehavior = loop
                ? AnimationIterationBehavior.Forever
                : AnimationIterationBehavior.Count;

            if (!loop)
            {
                animation.IterationCount = 1;
            }
        }

        visual.StartAnimation("Offset", offsets);
        visual.StartAnimation("Scale", scales);
        visual.StartAnimation("Opacity", opacities);
    }

    private void StopAll()
    {
        // Un passage unique interrompu ne signale pas sa fin : le lot est oublié
        // avant l'arrêt, si bien que son achèvement n'est plus reconnu.
        _batch?.Dispose();
        _batch = null;

        foreach (SpriteVisual visual in Visuals())
        {
            foreach (string property in AnimatedProperties)
            {
                visual.StopAnimation(property);
            }
        }
    }

    private IEnumerable<SpriteVisual> Visuals()
    {
        yield return _halo;
        yield return _core;

        foreach (SpriteVisual mote in _motes)
        {
            yield return mote;
        }
    }

    private static Vector3 MotePosition(HypnoticMote mote, Vector2 centre, Vector2 reach)
        => new(centre.X + ((float)mote.X * reach.X), centre.Y + ((float)mote.Y * reach.Y), 0);

    private static Vector3 Uniform(double scale) => new((float)scale, (float)scale, 1);

    private static void SetDiameter(SpriteVisual visual, float diameter)
    {
        visual.Size = new Vector2(diameter, diameter);
        visual.CenterPoint = new Vector3(diameter / 2, diameter / 2, 0);
    }

    /// <summary>
    /// Une lumière ronde : un dégradé radial du centre vers la transparence.
    /// </summary>
    /// <param name="centre">Couleur du centre.</param>
    /// <param name="hotSpot">
    /// Part du rayon occupée par un cœur teinté avant la décroissance. Zéro pour
    /// une lumière qui décroît dès le centre — le halo.
    /// </param>
    private SpriteVisual CreateGlow(Color centre, float hotSpot)
    {
        CompositionRadialGradientBrush brush = _compositor.CreateRadialGradientBrush();
        brush.MappingMode = CompositionMappingMode.Relative;
        brush.EllipseCenter = new Vector2(0.5f, 0.5f);
        brush.EllipseRadius = new Vector2(0.5f, 0.5f);

        CompositionColorGradientStop inner = _compositor.CreateColorGradientStop(0f, centre);
        brush.ColorStops.Add(inner);

        if (hotSpot > 0)
        {
            CompositionColorGradientStop tinted = _compositor.CreateColorGradientStop(
                hotSpot,
                Color.FromArgb(0xE6, _tint.R, _tint.G, _tint.B));
            brush.ColorStops.Add(tinted);
            _tintedStops.Add(tinted);
            _owned.Add(tinted);
        }
        else
        {
            _tintedStops.Add(inner);
        }

        CompositionColorGradientStop outer = _compositor.CreateColorGradientStop(
            1f,
            Color.FromArgb(0, _tint.R, _tint.G, _tint.B));
        brush.ColorStops.Add(outer);
        _tintedStops.Add(outer);

        SpriteVisual visual = _compositor.CreateSpriteVisual();
        visual.Brush = brush;
        visual.AnchorPoint = new Vector2(0.5f, 0.5f);
        visual.Opacity = 0f;

        _owned.Add(inner);
        _owned.Add(outer);
        _owned.Add(brush);
        _owned.Add(visual);

        return visual;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        IsAvailable = false;
        _host.SizeChanged -= OnHostSizeChanged;

        try
        {
            StopAll();
            _root.Children.RemoveAll();

            foreach (CompositionObject owned in _owned)
            {
                owned.Dispose();
            }

            _batch?.Dispose();
            _linear.Dispose();
            _root.Dispose();
        }
        catch (Exception)
        {
            // Les visuels ont pu être libérés avec leur fenêtre : ce n'est pas une erreur.
        }
    }
}
