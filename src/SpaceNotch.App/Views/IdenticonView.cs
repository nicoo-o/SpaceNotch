using System;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SpaceNotch.Core.Motion;

namespace SpaceNotch_App.Views;

/// <summary>
/// Identicône (A9) : pour une app sans logo, une grille 5 × 5 symétrique tirée
/// de son nom, qui pousse du centre vers les bords. Le motif vient de
/// <see cref="Identicon"/> ; la vue ne fait que le dessiner.
/// </summary>
public sealed partial class IdenticonView : Grid
{
    private readonly Rectangle[] _cells = new Rectangle[Identicon.Size * Identicon.Size];
    private string? _name;

    public IdenticonView()
    {
        IsHitTestVisible = false;
        RowSpacing = 1;
        ColumnSpacing = 1;

        for (int i = 0; i < Identicon.Size; i++)
        {
            RowDefinitions.Add(new RowDefinition());
            ColumnDefinitions.Add(new ColumnDefinition());
        }

        for (int i = 0; i < _cells.Length; i++)
        {
            var cell = new Rectangle { RadiusX = 0.5, RadiusY = 0.5 };
            SetRow(cell, i / Identicon.Size);
            SetColumn(cell, i % Identicon.Size);
            Children.Add(cell);
            _cells[i] = cell;
        }
    }

    /// <summary>Dessine le motif de <paramref name="name"/> ; le même nom ne rejoue rien.</summary>
    public void Show(string name, Brush tint, bool animate)
    {
        if (string.Equals(name, _name, StringComparison.Ordinal))
        {
            return;
        }

        _name = name;
        bool[] mask = Identicon.From(name);

        for (int i = 0; i < _cells.Length; i++)
        {
            Rectangle cell = _cells[i];
            cell.Fill = tint;
            cell.Opacity = mask[i] ? 1 : 0.08;

            if (!animate)
            {
                continue;
            }

            try
            {
                Visual visual = ElementCompositionPreview.GetElementVisual(cell);
                Compositor compositor = visual.Compositor;
                visual.CenterPoint = new System.Numerics.Vector3((float)(cell.ActualWidth / 2), (float)(cell.ActualHeight / 2), 0);
                Vector3KeyFrameAnimation grow = compositor.CreateVector3KeyFrameAnimation();
                grow.InsertKeyFrame(0f, new System.Numerics.Vector3(0.2f, 0.2f, 1));
                grow.InsertKeyFrame(1f, System.Numerics.Vector3.One, compositor.CreateCubicBezierEasingFunction(new System.Numerics.Vector2(0.3f, 1.4f), new System.Numerics.Vector2(0.5f, 1f)));
                grow.Duration = TimeSpan.FromMilliseconds(220);
                grow.DelayTime = TimeSpan.FromMilliseconds(Identicon.GrowDelay(i));
                grow.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
                visual.StartAnimation("Scale", grow);
            }
            catch (Exception)
            {
                // Sans compositeur, le motif est posé d'un coup.
            }
        }
    }
}
