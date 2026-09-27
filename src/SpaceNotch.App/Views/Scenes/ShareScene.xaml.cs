using System;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Localization;
using SpaceNotch.Features.Share;

namespace SpaceNotch_App.Views.Scenes;

/// <summary>
/// Partage PC → téléphone (F8) : le QR code dessiné module par module — la
/// matière pixel de la notch —, le nom du fichier, « même Wi-Fi », l'heure
/// d'expiration et « Arrêter ».
/// </summary>
public sealed partial class ShareScene : UserControl, IIslandSceneView
{
    private const double Inner = 120;

    private string? _activityId;
    private SharePayload? _shown;

    public ShareScene()
    {
        InitializeComponent();
    }

    public event EventHandler<IslandActionRequest>? ActionRequested;

    public FrameworkElement Root => this;

    public void Apply(IslandActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        _activityId = activity.Id;

        if (activity.Payload is not SharePayload payload || ReferenceEquals(payload, _shown))
        {
            return;
        }

        _shown = payload;
        HintText.Text = Lang.T("Scanne avec ton téléphone", "Scan with your phone");
        FileText.Text = payload.FileName;
        ExpiryText.Text = Lang.T(
            $"Même Wi-Fi · une fois · jusqu’à {payload.ExpiresAt.ToLocalTime().ToString("t", CultureInfo.CurrentCulture)}",
            $"Same Wi-Fi · once · until {payload.ExpiresAt.ToLocalTime().ToString("t", CultureInfo.CurrentCulture)}");
        StopButton.Content = Lang.T("Arrêter", "Stop");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(QrCard, Lang.T($"QR code de {payload.FileName}", $"QR code for {payload.FileName}"));
        QrModules.Data = Draw(payload);
    }

    /// <summary>Un rectangle par suite de modules sombres d'une rangée : peu de géométries, bords nets.</summary>
    private static GeometryGroup Draw(SharePayload payload)
    {
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        int size = payload.Size;

        if (size <= 0)
        {
            return group;
        }

        double cell = Math.Floor(Inner / size * 4) / 4;
        double offset = (Inner - (cell * size)) / 2;

        for (int r = 0; r < size; r++)
        {
            int c = 0;

            while (c < size)
            {
                if (!payload.Modules[(r * size) + c])
                {
                    c++;
                    continue;
                }

                int start = c;

                while (c < size && payload.Modules[(r * size) + c])
                {
                    c++;
                }

                group.Children.Add(new RectangleGeometry
                {
                    Rect = new global::Windows.Foundation.Rect(offset + (start * cell), offset + (r * cell), (c - start) * cell, cell)
                });
            }
        }

        return group;
    }

    private void OnStopClicked(object sender, RoutedEventArgs e)
    {
        if (_activityId is not null)
        {
            ActionRequested?.Invoke(this, new IslandActionRequest(_activityId, ShareFeature.CancelAction));
        }
    }
}
