using SpaceNotch.Platform.Windows.Windowing;
using SpaceNotch.Platform.Windows.Win32;
using Xunit;

namespace SpaceNotch.Core.Tests;

public class ScreenChangeWatcherTests
{
    [Fact]
    public void Classify_DisplayChange_IsRecognised()
    {
        Assert.Equal(
            ScreenChangeKind.DisplayChanged,
            ScreenChangeWatcher.Classify(NativeConstants.WM_DISPLAYCHANGE, 0));
    }

    [Fact]
    public void Classify_DpiChange_IsRecognised()
    {
        Assert.Equal(
            ScreenChangeKind.DpiChanged,
            ScreenChangeWatcher.Classify(NativeConstants.WM_DPICHANGED, 0));
    }

    [Fact]
    public void Classify_WorkAreaChange_IsRecognised()
    {
        Assert.Equal(
            ScreenChangeKind.WorkAreaChanged,
            ScreenChangeWatcher.Classify(
                NativeConstants.WM_SETTINGCHANGE,
                NativeConstants.SPI_SETWORKAREA));
    }

    [Fact]
    public void Classify_VisualEffectChange_IsRecognised()
    {
        // Modifier les effets visuels doit faire réévaluer le mode de fond et
        // l'usage du ressort.
        Assert.Equal(
            ScreenChangeKind.VisualStateChanged,
            ScreenChangeWatcher.Classify(
                NativeConstants.WM_SETTINGCHANGE,
                NativeConstants.SPI_SETUIEFFECTS));
    }

    [Fact]
    public void Classify_UnrelatedMessage_IsIgnored()
    {
        // Rien à faire : le watcher ne doit pas provoquer de travail inutile.
        Assert.Equal(
            ScreenChangeKind.None,
            ScreenChangeWatcher.Classify(NativeConstants.WM_MOUSEMOVE, 0));
    }

    [Fact]
    public void HandleMessage_RaisesChangedOnlyForRelevantMessages()
    {
        using var watcher = new ScreenChangeWatcher();
        int raised = 0;

        watcher.Changed += (_, _) => raised++;

        Assert.False(watcher.HandleMessage(NativeConstants.WM_MOUSEMOVE, 0));
        Assert.Equal(0, raised);

        Assert.True(watcher.HandleMessage(NativeConstants.WM_DISPLAYCHANGE, 0));
        Assert.Equal(1, raised);
    }
}
