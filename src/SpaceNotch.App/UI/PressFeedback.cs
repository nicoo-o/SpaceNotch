using System;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;

namespace SpaceNotch_App.UI;

/// <summary>
/// Retour d'appui des contrôles OLED : l'élément se tasse à 0,96 sur un ressort
/// rapide (~120 ms) et revient avec un léger rebond (amortissement 0,7). Porté
/// par la composition, il ne touche ni la mise en page ni les couleurs — le
/// « flash blanc » venait justement d'un changement de fond à l'appui.
///
/// Sous réduction des animations, rien n'est joué : l'encre de survol suffit.
/// </summary>
public static class PressFeedback
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(PressFeedback), new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty ScaleProperty = DependencyProperty.RegisterAttached(
        "Scale", typeof(double), typeof(PressFeedback), new PropertyMetadata(0.96));

    private static readonly PointerEventHandler Pressed = OnPressed;
    private static readonly PointerEventHandler Released = OnReleased;

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static double GetScale(DependencyObject element) => (double)element.GetValue(ScaleProperty);

    public static void SetScale(DependencyObject element, double value) => element.SetValue(ScaleProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        // Les boutons marquent l'appui comme traité : il faut écouter aussi les
        // événements déjà traités, sinon rien n'arrive jamais ici.
        element.RemoveHandler(UIElement.PointerPressedEvent, Pressed);
        element.RemoveHandler(UIElement.PointerReleasedEvent, Released);
        element.RemoveHandler(UIElement.PointerCaptureLostEvent, Released);
        element.RemoveHandler(UIElement.PointerExitedEvent, Released);
        element.RemoveHandler(UIElement.PointerCanceledEvent, Released);

        if (e.NewValue is true)
        {
            element.AddHandler(UIElement.PointerPressedEvent, Pressed, handledEventsToo: true);
            element.AddHandler(UIElement.PointerReleasedEvent, Released, handledEventsToo: true);
            element.AddHandler(UIElement.PointerCaptureLostEvent, Released, handledEventsToo: true);
            element.AddHandler(UIElement.PointerExitedEvent, Released, handledEventsToo: true);
            element.AddHandler(UIElement.PointerCanceledEvent, Released, handledEventsToo: true);
        }
    }

    private static void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            Animate(element, (float)GetScale(element), dampingRatio: 1f, periodMs: 30);
        }
    }

    private static void OnReleased(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            Animate(element, 1f, dampingRatio: 0.7f, periodMs: 45);
        }
    }

    /// <summary>
    /// Ressort de composition sur l'échelle, centré. La période se règle en
    /// millisecondes : ~30 ms donnent un tassement perçu en ~120 ms.
    /// </summary>
    private static void Animate(FrameworkElement element, float target, float dampingRatio, int periodMs)
    {
        if (!MotionSettings.AnimationsEnabled)
        {
            return;
        }

        try
        {
            Visual visual = ElementCompositionPreview.GetElementVisual(element);
            Compositor compositor = visual.Compositor;

            visual.CenterPoint = new Vector3((float)element.ActualWidth / 2f, (float)element.ActualHeight / 2f, 0f);

            // Déjà au repos et rien à faire : on n'arme pas d'animation inutile
            // (la sortie du pointeur arrive aussi sans appui préalable).
            if (target >= 1f && visual.Scale == Vector3.One)
            {
                return;
            }

            SpringVector3NaturalMotionAnimation spring = compositor.CreateSpringVector3Animation();
            spring.FinalValue = new Vector3(target, target, 1f);
            spring.DampingRatio = dampingRatio;
            spring.Period = TimeSpan.FromMilliseconds(periodMs);
            spring.StopBehavior = AnimationStopBehavior.SetToFinalValue;

            visual.StartAnimation("Scale", spring);
        }
        catch (Exception)
        {
            // Visuel détaché pendant l'animation : sans conséquence.
        }
    }
}
