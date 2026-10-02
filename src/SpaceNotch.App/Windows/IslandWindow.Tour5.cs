using System;
using System.Collections.Generic;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Localization;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Features.SystemHud;
using SpaceNotch.Platform.Windows.Media;

namespace SpaceNotch_App.Windows;

/// <summary>
/// Visite des animations de la vague 5 qui demandent une main : la pochette
/// sous le curseur (S2) et sa couleur (D2), les gestes (U3), l'appui (D4),
/// l'aimant (U4) et l'éclatement de la recherche (S3). La machine de tournage
/// n'a ni souris ni clavier : chaque étape joue le geste elle-même.
/// </summary>
public sealed partial class IslandWindow
{
    private IEnumerable<(string Label, Action Run)> Wave5Tour(Func<IslandActivity> music)
    {
        IslandActivity Album(string title, string artist, (byte R, byte G, byte B) from, (byte R, byte G, byte B) to, bool playing = true)
        {
            IslandActivity template = music();
            var tint = new ActivityTint(from.R, from.G, from.B);
            return new IslandActivity
            {
                CreatedAt = template.CreatedAt,
                Id = template.Id,
                FeatureId = template.FeatureId,
                SceneKey = template.SceneKey,
                Title = title,
                Subtitle = artist,
                Source = template.Source,
                IconKey = template.IconKey,
                State = template.State,
                Priority = template.Priority,
                Tint = tint,
                Actions = template.Actions,
                Payload = new MediaTrackInfo(title, artist, string.Empty, "Spotify.exe", playing, TimeSpan.FromSeconds(83), TimeSpan.FromSeconds(279), GradientArtwork(from, to), tint)
            };
        }

        yield return ("pochette · couleur et relief", () =>
        {
            TourClear();
            TourShow(Album("Good Days", "SZA", (0xFF, 0x8F, 0xA3), (0xFF, 0xB2, 0x6B)), open: true);

            // La pochette suit un curseur qui fait le tour de son coin.
            for (int i = 0; i <= 12; i++)
            {
                double t = i / 12.0 * Math.PI * 2;
                TourLater(500 + (i * 90), () => MediaSceneView.TiltArtwork(0.45 * Math.Cos(t), 0.45 * Math.Sin(t)));
            }

            TourLater(2200, () => _activityManager.PostActivity(Album("Midnight City", "M83", (0x5B, 0xE3, 0x8A), (0x7F, 0xE6, 0xFF))));
        });

        yield return ("gestes · molette et clic du milieu", () =>
        {
            TourShow(Album("Midnight City", "M83", (0x5B, 0xE3, 0x8A), (0x7F, 0xE6, 0xFF)), open: false);

            // Molette : 2 % par cran, le fader cranté s'affiche.
            int[] levels = [70, 68, 66, 64];

            for (int i = 0; i < levels.Length; i++)
            {
                int level = levels[i];
                TourLater(500 + (i * 220), () => _activityManager.PostActivity(
                    HudActivity.Build("tour.volume", TourFeature, IslandSceneCatalog.VolumeHud, "Volume", level, 100, "VolumeHigh", Lang.T("Sortie principale", "Main output"), TimeSpan.FromSeconds(30))));
            }

            // Clic du milieu : pause, sans ouvrir la notch.
            TourLater(2300, () =>
            {
                TourClear("tour.volume");
                _activityManager.PostActivity(Album("Midnight City", "M83", (0x5B, 0xE3, 0x8A), (0x7F, 0xE6, 0xFF), playing: false));
            });
        });

        yield return ("clic · la notch s'enfonce", () =>
        {
            TourLater(500, _controller.Press);
            TourLater(900, _controller.Release);
            TourLater(2000, _controller.Press);
            TourLater(2400, _controller.Release);
        });

        yield return ("aimant · le curseur approche", () =>
        {
            TourClear("tour.media");

            // Le curseur descend de l'écran vers la notch, s'y attarde, repart.
            for (int i = 0; i <= 20; i++)
            {
                double y = 160 - (i * 7.5);
                TourLater(300 + (i * 90), () => _tourCursor = (AttachCenterX(DetachDisplay()) + 30, Math.Max(12, y)));
            }

            TourLater(3500, () => _tourCursor = null);
        });

        yield return ("recherche · éclatement", () =>
        {
            _tourCursor = null;
            OpenLauncher();
            LauncherSceneView.Type("photoshop");
            TourLater(1600, LauncherSceneView.ClearWithShatter);
            TourLater(3400, _controller.RequestCollapse);
        });
    }

    /// <summary>Une pochette en dégradé diagonal, en BMP 24 bits, pour filmer sans vraie musique.</summary>
    private static byte[] GradientArtwork((byte R, byte G, byte B) from, (byte R, byte G, byte B) to)
    {
        const int Size = 64;
        const int Row = Size * 3;
        const int Pixels = Row * Size;
        byte[] bmp = new byte[54 + Pixels];

        void Int(int offset, int value) => BitConverter.GetBytes(value).CopyTo(bmp, offset);

        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        Int(2, bmp.Length);
        Int(10, 54);
        Int(14, 40);
        Int(18, Size);
        Int(22, Size);
        bmp[26] = 1;
        bmp[28] = 24;
        Int(34, Pixels);

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                double t = (x + (Size - 1 - y)) / (2.0 * (Size - 1));
                int i = 54 + (y * Row) + (x * 3);
                bmp[i] = (byte)(from.B + ((to.B - from.B) * t));
                bmp[i + 1] = (byte)(from.G + ((to.G - from.G) * t));
                bmp[i + 2] = (byte)(from.R + ((to.R - from.R) * t));
            }
        }

        return bmp;
    }
}
