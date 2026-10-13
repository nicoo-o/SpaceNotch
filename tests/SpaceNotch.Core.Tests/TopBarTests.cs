using System.Linq;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Events;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Features.Menu;
using SpaceNotch.Infrastructure.Config;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>n° 93, ADR-030 : barre des tâches en haut (StartAllBack, ExplorerPatcher).</summary>
[Collection(GlobalLanguageGroup.Name)]
public sealed class TopBarTests
{
    [Theory]
    [InlineData(0, 0, 0, false)]       // pas de barre (ou barre masquée automatiquement)
    [InlineData(0, 4, 4, false)]       // une marge de quelques pixels n'est pas une barre
    [InlineData(0, 60, 60, true)]      // 40 DIP à 150 %
    [InlineData(-1080, -1020, 60, true)] // écran secondaire au-dessus du principal
    public void A_bar_is_read_from_the_work_area(int monitorTop, int workTop, int reserved, bool present)
    {
        Assert.Equal(reserved, TopBar.Reserved(monitorTop, workTop));
        Assert.Equal(present, TopBar.IsPresent(monitorTop, workTop));
    }

    [Fact]
    public void Without_a_bar_rule_one_holds_the_notch_touches_the_edge()
    {
        foreach (TopBarPlacement placement in new[] { TopBarPlacement.Ask, TopBarPlacement.UnderBar, TopBarPlacement.SideTab })
        {
            Assert.Equal(0, TopBar.TopInset(0, 0, placement));
            Assert.False(TopBar.ShouldAsk(0, 0, placement));
            Assert.False(TopBar.WantsSide(0, 0, placement));
        }
    }

    [Fact]
    public void With_a_bar_the_notch_sits_just_below_it_until_the_user_picks_the_side()
    {
        // En attendant la réponse, sous la barre : la notch ne la recouvre jamais.
        Assert.Equal(60, TopBar.TopInset(0, 60, TopBarPlacement.Ask));
        Assert.True(TopBar.ShouldAsk(0, 60, TopBarPlacement.Ask));

        Assert.Equal(60, TopBar.TopInset(0, 60, TopBarPlacement.UnderBar));
        Assert.False(TopBar.ShouldAsk(0, 60, TopBarPlacement.UnderBar));
        Assert.False(TopBar.WantsSide(0, 60, TopBarPlacement.UnderBar));

        Assert.Equal(0, TopBar.TopInset(0, 60, TopBarPlacement.SideTab));
        Assert.True(TopBar.WantsSide(0, 60, TopBarPlacement.SideTab));
        Assert.False(TopBar.ShouldAsk(0, 60, TopBarPlacement.SideTab));
    }

    [Fact]
    public void Ask_is_the_default_and_an_unknown_value_falls_back_to_it()
    {
        Assert.Equal(TopBarPlacement.Ask, new AppSettings().TopBarPlacement);

        var settings = new AppSettings { TopBarPlacement = (TopBarPlacement)9 };
        settings.Sanitize();

        Assert.Equal(TopBarPlacement.Ask, settings.TopBarPlacement);
    }

    [Fact]
    public async Task The_question_offers_below_the_bar_first_then_the_side()
    {
        bool before = SpaceNotch.Core.Localization.Lang.French;
        SpaceNotch.Core.Localization.Lang.French = true;

        try
        {
            var activities = new ActivityManager();
            await using var menu = new QuickMenuFeature(activities, new EventBus());

            menu.OfferTopBarPlacement();

            IslandActivity offer = Assert.Single(activities.GetActiveActivities());
            Assert.Equal(QuickMenuFeature.TopBarOfferId, offer.Id);
            Assert.True(menu.IsTopBarOfferShown);
            Assert.Equal([QuickMenuFeature.TopBarUnderAction, QuickMenuFeature.TopBarSideAction], offer.Actions.Select(a => a.Id));
            Assert.True(offer.Actions[0].IsPrimary);
            Assert.Equal("Sous la barre", offer.Actions[0].Label);

            menu.WithdrawTopBarOffer();
            Assert.Empty(activities.GetActiveActivities());
            Assert.False(menu.IsTopBarOfferShown);
        }
        finally
        {
            SpaceNotch.Core.Localization.Lang.French = before;
        }
    }
}
