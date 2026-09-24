using System;
using System.Collections.Generic;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using SpaceNotch.Core.Motion;
using Windows.UI;

namespace SpaceNotch_App.Composition;

/// <summary>
/// Rendu de la grille hypnotique : neuf pixels lumineux et leur halo, confiés
/// au compositeur.
///
/// <para>
/// <b>Aucune image n'est calculée ici.</b> La grille est décrite dans le cœur
/// (<see cref="HypnoticField"/>) comme une fonction linéaire par morceaux ; ses
/// images clés exactes sont transmises au compositeur, qui les rejoue seul sur
/// le GPU. Pendant que la grille vit, le fil d'interface est libre ; au repos,
/// les animations sont arrêtées et les visuels masqués.
/// </para>
///
/// <para>
/// <b>Le halo est une ombre.</b> Les pixels vivent dans un <c>LayerVisual</c>
/// portant une ombre sans décalage, de la couleur des pixels : le compositeur
/// floute la forme exacte des pixels allumés, image par image. C'est le « bloom »
/// de la référence, sans shader et sans rendu supplémentaire.
/// </para>
///
/// <para>
/// Tolérante comme les autres décorations : si le compositeur refuse les
/// visuels, <see cref="TryAttach"/> renvoie <c>null</c> et l'appelant garde son
/// glyphe fixe.
/// </para>
/// </summary>
public sealed class HypnoticSurface : IDisposable
{
    /// <summary>Écart entre pixels, relatif au côté de la grille : les pixels se touchent presque.</summary>
    private const float GapRatio = 0.04f;

    /// <summary>Rayon du halo, relatif au côté de la grille.</summary>
    private const float BloomRatio = 0.55f;

    private readonly Compositor _compositor;
    private readonly FrameworkElement _host;
    private readonly LayerVisual _layer;
    private readonly DropShadow _bloom;
    private readonly CompositionColorBrush _ink;
    private readonly SpriteVisual[] _cells = new SpriteVisual[HypnoticField.CellCount];
    private readonly CompositionEasingFunction _linear;

    private HypnoticPreset _preset = HypnoticPreset.None;
    private bool _animate = true;
    private CompositionScopedBatch? _batch;
    private bool _disposed;

    private HypnoticSurface(Compositor compositor, FrameworkElement host)
    {
        _compositor = compositor;
        _host = host;
        _linear = compositor.CreateLinearEasingFunction();

        HypnoticColor warm = HypnoticField.WarmLight;
        _ink = compositor.CreateColorBrush(Color.FromArgb(0xFF, warm.R, warm.G, warm.B));

        _layer = compositor.CreateLayerVisual();
        _layer.IsVisible = false;

        _bloom = compositor.CreateDropShadow();
        _bloom.Offset = Vector3.Zero;
        _bloom.Color = _ink.Color;
        _bloom.Opacity = 0f;
        _layer.Shadow = _bloom;

        for (int i = 0; i < _cells.Length; i++)
        {
            SpriteVisual cell = compositor.CreateSpriteVisual();
            cell.Brush = _ink;
            cell.Opacity = 0f;
            _cells[i] = cell;
            _layer.Children.InsertAtTop(cell);
        }

        ElementCompositionPreview.SetElementChildVisual(host, _layer);

        _host.SizeChanged += OnHostSizeChanged;

        IsAvailable = true;
    }

    /// <summary>Vrai lorsque le compositeur a accepté les visuels.</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>Préréglage affiché.</summary>
    public HypnoticPreset Preset => _preset;

    /// <summary>
    /// Signalé à la fin d'un préréglage ponctuel — achèvement ou échec — pour que
    /// l'appelant passe à la suite.
    /// </summary>
    public event EventHandler<HypnoticPreset>? OneShotCompleted;

    /// <summary>
    /// Tente d'attacher le rendu à un élément hôte. Renvoie <c>null</c> si le
    /// compositeur refuse.
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
    /// Affiche un préréglage.
    /// </summary>
    /// <param name="preset">Préréglage ; <see cref="HypnoticPreset.None"/> arrête et masque.</param>
    /// <param name="animate">
    /// Faux sous réduction des animations : un motif fixe est posé, dans la
    /// couleur du préréglage. L'information reste, le mouvement part.
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
        // sauter la grille à son premier motif à chaque rendu.
        if (unchanged && preset != HypnoticPreset.None && HypnoticField.IsLooping(preset))
        {
            return;
        }

        Apply();
    }

    private void OnHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Les positions sont en DIPs de l'hôte : une taille nouvelle oblige à
        // redisposer la grille, ce qui n'arrive qu'au changement de palier.
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
            _layer.IsVisible = false;
            return;
        }

        float side = (float)Math.Min(_host.ActualWidth, _host.ActualHeight);

        if (side <= 0)
        {
            // L'hôte n'est pas encore disposé : SizeChanged rappellera.
            return;
        }

        Layout(side, new Vector2((float)_host.ActualWidth, (float)_host.ActualHeight));

        _layer.IsVisible = true;

        if (!_animate)
        {
            Pose(HypnoticField.StaticFrame(_preset), side);
            return;
        }

        Animate(side);
    }

    /// <summary>Dispose la grille au centre de l'hôte.</summary>
    private void Layout(float side, Vector2 host)
    {
        float gap = side * GapRatio;
        float cell = (side - (2 * gap)) / HypnoticField.GridSize;

        _layer.Size = new Vector2(side, side);
        _layer.Offset = new Vector3((host.X - side) / 2, (host.Y - side) / 2, 0);
        _bloom.BlurRadius = side * BloomRatio;

        for (int i = 0; i < _cells.Length; i++)
        {
            int column = i % HypnoticField.GridSize;
            int row = i / HypnoticField.GridSize;

            _cells[i].Size = new Vector2(cell, cell);
            _cells[i].Offset = new Vector3(column * (cell + gap), row * (cell + gap), 0);
        }
    }

    /// <summary>Pose une image fixe, sans aucune animation.</summary>
    private void Pose(HypnoticFrame frame, float side)
    {
        Color color = ToColor(frame.Color);

        _ink.Color = color;
        _bloom.Color = color;
        _bloom.Opacity = (float)frame.Bloom;

        Vector3 offset = _layer.Offset;
        _layer.Offset = new Vector3(offset.X + ((float)frame.ShakeX * side), offset.Y, 0);

        for (int i = 0; i < _cells.Length; i++)
        {
            _cells[i].Opacity = (float)frame.Cells[i];
        }
    }

    /// <summary>Transmet les images clés exactes au compositeur.</summary>
    private void Animate(float side)
    {
        IReadOnlyList<(double Progress, HypnoticFrame Frame)> keys = HypnoticField.Keyframes(_preset);
        bool loop = HypnoticField.IsLooping(_preset);
        TimeSpan duration = TimeSpan.FromSeconds(HypnoticField.PeriodSeconds(_preset));

        // La première image est posée directement : la grille est déjà en place
        // pendant l'instant qui précède le démarrage des animations.
        Pose(keys[0].Frame, side);

        CompositionScopedBatch? batch = loop ? null : _compositor.CreateScopedBatch(CompositionBatchTypes.Animation);

        ColorKeyFrameAnimation colors = _compositor.CreateColorKeyFrameAnimation();
        ScalarKeyFrameAnimation bloom = _compositor.CreateScalarKeyFrameAnimation();
        var cells = new ScalarKeyFrameAnimation[_cells.Length];

        for (int i = 0; i < cells.Length; i++)
        {
            cells[i] = _compositor.CreateScalarKeyFrameAnimation();
        }

        Vector3KeyFrameAnimation? shake = null;
        bool shakes = false;

        foreach ((double progress, HypnoticFrame frame) in keys)
        {
            shakes |= Math.Abs(frame.ShakeX) > 1e-6;
        }

        if (shakes)
        {
            shake = _compositor.CreateVector3KeyFrameAnimation();
        }

        Vector3 origin = _layer.Offset;

        foreach ((double progress, HypnoticFrame frame) in keys)
        {
            float key = (float)Math.Clamp(progress, 0, 1);

            colors.InsertKeyFrame(key, ToColor(frame.Color), _linear);
            bloom.InsertKeyFrame(key, (float)frame.Bloom, _linear);

            for (int i = 0; i < cells.Length; i++)
            {
                cells[i].InsertKeyFrame(key, (float)frame.Cells[i], _linear);
            }

            shake?.InsertKeyFrame(key, new Vector3(origin.X + ((float)frame.ShakeX * side), origin.Y, 0), _linear);
        }

        Configure(colors, duration, loop);
        Configure(bloom, duration, loop);

        _ink.StartAnimation("Color", colors);
        _bloom.StartAnimation("Color", colors);
        _bloom.StartAnimation("Opacity", bloom);

        for (int i = 0; i < cells.Length; i++)
        {
            Configure(cells[i], duration, loop);
            _cells[i].StartAnimation("Opacity", cells[i]);
        }

        if (shake is not null)
        {
            Configure(shake, duration, loop);
            _layer.StartAnimation("Offset", shake);
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

    private static void Configure(KeyFrameAnimation animation, TimeSpan duration, bool loop)
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

    private void StopAll()
    {
        // Un passage unique interrompu ne signale pas sa fin : le lot est oublié
        // avant l'arrêt.
        _batch?.Dispose();
        _batch = null;

        _ink.StopAnimation("Color");
        _bloom.StopAnimation("Color");
        _bloom.StopAnimation("Opacity");
        _layer.StopAnimation("Offset");

        foreach (SpriteVisual cell in _cells)
        {
            cell.StopAnimation("Opacity");
        }
    }

    private static Color ToColor(HypnoticColor color) => Color.FromArgb(0xFF, color.R, color.G, color.B);

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
            _layer.Shadow = null;
            _layer.Children.RemoveAll();

            foreach (SpriteVisual cell in _cells)
            {
                cell.Dispose();
            }

            _bloom.Dispose();
            _ink.Dispose();
            _linear.Dispose();
            _layer.Dispose();
        }
        catch (Exception)
        {
            // Les visuels ont pu être libérés avec leur fenêtre : ce n'est pas une erreur.
        }
    }
}
