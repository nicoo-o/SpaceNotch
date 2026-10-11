using System.Linq;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Events;
using SpaceNotch.Core.Features;
using SpaceNotch.Features.Menu;
using SpaceNotch.Infrastructure.Config;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>n° 63 : historique éteint, la tuile Presse-papier propose de l'activer au lieu d'être grisée.</summary>
public sealed class ClipboardOfferTests
{
    [Fact]
    public async Task The_offer_says_what_history_keeps_and_can_be_accepted_or_declined()
    {
        var activities = new ActivityManager();
        await using var menu = new QuickMenuFeature(activities, new EventBus());

        menu.OfferClipboardHistory();
        Assert.True(menu.IsClipboardOfferShown);  // la fenêtre la retire au repli

        IslandActivity offer = Assert.Single(activities.GetActiveActivities());
        Assert.Equal(QuickMenuFeature.ClipboardOfferId, offer.Id);
        Assert.Contains("mots de passe", offer.Subtitle);
        Assert.Equal([QuickMenuFeature.ClipboardEnableAction, QuickMenuFeature.ClipboardLaterAction], offer.Actions.Select(a => a.Id));
        Assert.True(offer.Actions[0].IsPrimary);

        menu.WithdrawClipboardOffer();
        Assert.Empty(activities.GetActiveActivities());
        Assert.False(menu.IsClipboardOfferShown);
    }

    [Fact]
    public void Accepting_turns_history_on_with_a_real_depth()
    {
        // Comme l'interrupteur des Réglages : une profondeur nulle donnerait un
        // historique qui ne garde rien, donc l'air cassé.
        var settings = new AppSettings();
        Assert.False(settings.ShowClipboard);

        settings.BindFeature(FeatureKeys.Clipboard, true);
        settings.Sanitize();

        Assert.True(settings.ShowClipboard);
        Assert.True(settings.ClipboardHistoryLimit > 0);
    }
}
