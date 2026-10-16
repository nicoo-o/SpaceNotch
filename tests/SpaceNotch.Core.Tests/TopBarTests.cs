using System.Linq;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Events;
using SpaceNotch.Core.Presentation;
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
        Assert.Equal(0, TopBar.TopInset(0, 0));

        foreach (TopBarPlacement placement in new[] { TopBarPlacement.Ask, TopBarPlacement.UnderBar, TopBarPlacement.SideTab })
        {
            Assert.False(TopBar.ShouldAsk(0, 0, placement));
            Assert.False(TopBar.WantsSide(0, 0, placement));
        }
    }

    [Fact]
    public void With_a_bar_the_notch_at_the_top_never_covers_it_whatever_the_choice()
    {
        // Le décalage ne dépend que de la barre : même avec « Sur le côté », une
        // notch ramenée en haut (raccrochage, « Accrocher à ») reste dessous.
        Assert.Equal(60, TopBar.TopInset(0, 60));

        Assert.True(TopBar.ShouldAsk(0, 60, TopBarPlacement.Ask));
        Assert.False(TopBar.WantsSide(0, 60, TopBarPlacement.Ask));

        Assert.False(TopBar.ShouldAsk(0, 60, TopBarPlacement.UnderBar));
        Assert.False(TopBar.WantsSide(0, 60, TopBarPlacement.UnderBar));

        // Le choix « côté » ne décide que du bord.
        Assert.True(TopBar.WantsSide(0, 60, TopBarPlacement.SideTab));
        Assert.False(TopBar.ShouldAsk(0, 60, TopBarPlacement.SideTab));
    }

    [Fact]
    public void Under_a_bar_the_top_edge_frame_starts_at_the_bottom_of_the_bar()
    {
        // n° 96 : la goutte d'un détachement part du bas de la barre (40 DIP), pas du haut de l'écran.
        var screen = new ScreenRect(0, 0, 1707, 1067);

        ScreenRect top = TopBar.FrameBelowBar(NotchEdge.Top, screen, 40);
        Assert.Equal(40, top.Y);
        Assert.Equal(1027, top.Height);
        Assert.Equal(screen.Width, top.Width);

        // Sans barre, et sur les côtés, le repère reste l'écran.
        Assert.Equal(screen, TopBar.FrameBelowBar(NotchEdge.Top, screen, 0));
        Assert.Equal(screen, TopBar.FrameBelowBar(NotchEdge.Left, screen, 40));
        Assert.Equal(screen, TopBar.FrameBelowBar(NotchEdge.Right, screen, 40));
    }

    [Fact]
    public void A_shape_at_the_bottom_of_the_bar_lands_at_the_bar_on_screen()
    {
        // Le bas du repère local (y = 0) tombe sur le bas de la barre à l'écran.
        var screen = new ScreenRect(0, 0, 1707, 1067);
        ScreenRect frame = TopBar.FrameBelowBar(NotchEdge.Top, screen, 40);

        ScreenRect onScreen = EdgeFrame.ToScreenRect(
            NotchEdge.Top, new ScreenRect(800, 0, 100, 20), frame);

        Assert.Equal(40, onScreen.Y);
    }

    [Fact]
    public void A_pill_arrived_under_a_bar_taller_than_the_shape_reaches_the_dock()
    {
        // Relecture de #81 : barre de 48 DIP, forme au repos de 18 DIP. La pastille
        // arrivée sous la barre a son haut à y = 48 : mesuré depuis le haut de
        // l'écran (48 < 18 faux), elle n'atteignait jamais la forme.
        var screen = new ScreenRect(0, 0, 1707, 1067);
        var arrived = new ScreenRect(800, 48, 60, 18);

        Assert.True(TopBar.PillReachesDock(NotchEdge.Top, arrived, screen, 48, 18));
        Assert.False(TopBar.PillReachesDock(NotchEdge.Top, new ScreenRect(800, 200, 60, 18), screen, 48, 18));

        // Sans barre, le repère est l'écran : rien ne change.
        Assert.True(TopBar.PillReachesDock(NotchEdge.Top, new ScreenRect(800, 0, 60, 18), screen, 0, 18));
        Assert.False(TopBar.PillReachesDock(NotchEdge.Top, arrived, screen, 0, 18));
    }

    [Fact]
    public void A_pill_overshooting_while_it_docks_stays_below_the_bar()
    {
        // Mesuré à l'écran : le ressort faisait monter la pastille 4 px dans la barre.
        var screen = new ScreenRect(0, 0, 1707, 1067);
        var overshoot = new ScreenRect(800, 37, 60, 18);

        ScreenRect kept = TopBar.KeepBelowBar(NotchEdge.Top, overshoot, screen, 40);
        Assert.Equal(new ScreenRect(800, 40, 60, 18), kept);

        // Plus bas que la barre, sans barre, ou sur un côté : rien ne bouge.
        var below = new ScreenRect(800, 120, 60, 18);
        Assert.Equal(below, TopBar.KeepBelowBar(NotchEdge.Top, below, screen, 40));
        Assert.Equal(overshoot, TopBar.KeepBelowBar(NotchEdge.Top, overshoot, screen, 0));
        Assert.Equal(overshoot, TopBar.KeepBelowBar(NotchEdge.Left, overshoot, screen, 40));
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
