using System;
using System.Linq;
using System.Text.Json;
using SpaceNotch.Core.Media;
using SpaceNotch.Core.Phone;
using SpaceNotch.Core.Social;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>Vague 6d, le cœur : appels, livraisons, salle vocale Discord, paroles, Spotify.</summary>
public class Wave6dCoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 19, 20, 0, TimeSpan.FromHours(2));

    // ---- T1 : appels ----------------------------------------------------

    [Theory]
    [InlineData("Lien avec Windows", "Maman", "Appel entrant", "Maman", CallState.Ringing)]
    [InlineData("Phone Link", "Incoming call", "Alex Martin", "Alex Martin", CallState.Ringing)]
    [InlineData("Phone Link", "Missed call", "+33 6 12 34 56 78", "+33 6 12 34 56 78", CallState.Missed)]
    [InlineData("Lien avec Windows", "", "Appel entrant de Paul", "Paul", CallState.Ringing)]
    [InlineData("Lien avec Windows", "Paul", "Appel terminé", "Paul", CallState.Ended)]
    public void Calls_AreReadFromPhoneLink(string app, string title, string body, string caller, CallState state)
    {
        PhoneCall call = Assert.IsType<PhoneCall>(PhoneLink.ReadCall(app, title, body));
        Assert.Equal(caller, call.Caller);
        Assert.Equal(state, call.State);
    }

    [Fact]
    public void Calls_FromOtherApps_AreIgnored()
    {
        Assert.Null(PhoneLink.ReadCall("Teams", "Incoming call", "Camille"));
        Assert.Null(PhoneLink.ReadCall("Phone Link", "Maman", "On mange à 20 h ?"));
    }

    [Theory]
    [InlineData(42, "0:42")]
    [InlineData(725, "12:05")]
    [InlineData(3723, "1:02:03")]
    public void CallDuration_IsCompact(int seconds, string expected)
        => Assert.Equal(expected, PhoneLink.Duration(TimeSpan.FromSeconds(seconds)));

    // ---- T2 : livraisons ---------------------------------------------

    [Fact]
    public void Delivery_ReadsStepAndEta()
    {
        DeliveryUpdate food = Assert.IsType<DeliveryUpdate>(Delivery.Read("Phone Link", "Uber Eats", "Votre commande est en route, arrivée dans 12 min", Now));
        Assert.Equal("Uber Eats", food.Service);
        Assert.Equal(DeliveryKind.Food, food.Kind);
        Assert.Equal(DeliveryStep.OnTheWay, food.Step);
        Assert.Equal(Now.AddMinutes(12), food.Eta);

        DeliveryUpdate ride = Assert.IsType<DeliveryUpdate>(Delivery.Read("Uber", "Your driver is on the way", "Arriving at 7:42 PM · Toyota Prius", Now));
        Assert.Equal(DeliveryKind.Ride, ride.Kind);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 19, 42, 0, TimeSpan.FromHours(2)), ride.Eta);

        DeliveryUpdate here = Assert.IsType<DeliveryUpdate>(Delivery.Read("Deliveroo", "Deliveroo", "Ton livreur est arrivé, il est devant", Now));
        Assert.Equal(DeliveryStep.Arrived, here.Step);
        Assert.Null(here.Eta);

        DeliveryUpdate preparing = Assert.IsType<DeliveryUpdate>(Delivery.Read("Phone Link", "Deliveroo", "Le restaurant prépare ta commande", Now));
        Assert.Equal(DeliveryStep.Preparing, preparing.Step);
    }

    [Fact]
    public void Delivery_IgnoresPromotions_AndUnknownServices()
    {
        Assert.Null(Delivery.Read("Uber Eats", "Uber Eats", "-30 % sur les sushis ce soir !", Now));
        Assert.Null(Delivery.Read("Slack", "Camille", "je suis en route", Now));
        Assert.Null(Delivery.Read("Phone Link", "Bolton", "on the way", Now));
    }

    [Fact]
    public void Delivery_ClockAfterMidnight_IsTomorrow()
        => Assert.Equal(new DateTimeOffset(2026, 10, 2, 0, 10, 0, TimeSpan.FromHours(2)), Delivery.Eta("arrive à 00:10", Now.AddHours(4.5)));

    [Fact]
    public void Delivery_VehicleMovesTowardsArrival()
    {
        DateTimeOffset since = Now;
        DateTimeOffset eta = Now.AddMinutes(10);

        Assert.Equal(0.08, Delivery.Position(DeliveryStep.Preparing, null, Now, null));
        Assert.Equal(0.5, Delivery.Position(DeliveryStep.OnTheWay, eta, Now, since));
        Assert.Equal(0.71, Delivery.Position(DeliveryStep.OnTheWay, eta, Now.AddMinutes(5), since), 3);
        Assert.Equal(0.92, Delivery.Position(DeliveryStep.OnTheWay, eta, Now.AddMinutes(30), since), 3);
        Assert.Equal(1, Delivery.Position(DeliveryStep.Arrived, null, Now, since));
        Assert.Equal("Scooter", Delivery.VehicleGlyph(DeliveryKind.Food));
        Assert.Equal("Car", Delivery.VehicleGlyph(DeliveryKind.Ride));
    }

    // ---- T3 : Discord ------------------------------------------------

    [Fact]
    public void DiscordFrames_RoundTrip()
    {
        byte[] frame = DiscordRpc.Encode(DiscordOpcode.Frame, DiscordRpc.Handshake("123456789012345678"));
        Assert.True(DiscordRpc.TryReadHeader(frame, out DiscordOpcode op, out int length));
        Assert.Equal(DiscordOpcode.Frame, op);
        Assert.Equal(frame.Length - 8, length);
        Assert.Contains("\"client_id\":\"123456789012345678\"", System.Text.Encoding.UTF8.GetString(frame, 8, length));

        byte[] huge = new byte[8];
        BitConverter.GetBytes(1).CopyTo(huge, 0);
        BitConverter.GetBytes(DiscordRpc.MaxFrame + 1).CopyTo(huge, 4);
        Assert.False(DiscordRpc.TryReadHeader(huge, out _, out _));
    }

    [Fact]
    public void DiscordCommands_AreWellFormed()
    {
        using JsonDocument authorize = JsonDocument.Parse(DiscordRpc.Authorize("123456789012345678", "n1"));
        Assert.Equal("AUTHORIZE", authorize.RootElement.GetProperty("cmd").GetString());
        Assert.Contains("rpc.voice.read", authorize.RootElement.GetProperty("args").GetProperty("scopes").EnumerateArray().Select(e => e.GetString()));

        using JsonDocument mute = JsonDocument.Parse(DiscordRpc.SetMute(true, "n2"));
        Assert.True(mute.RootElement.GetProperty("args").GetProperty("mute").GetBoolean());

        using JsonDocument sub = JsonDocument.Parse(DiscordRpc.Subscribe("SPEAKING_START", "42", "n3"));
        Assert.Equal("42", sub.RootElement.GetProperty("args").GetProperty("channel_id").GetString());

        Assert.Equal("abc", DiscordRpc.ReadCode("""{"cmd":"AUTHORIZE","data":{"code":"abc"}}"""));
        Assert.Equal(("tok", "ref", 604800), DiscordRpc.ReadToken("""{"access_token":"tok","refresh_token":"ref","expires_in":604800}"""));
        Assert.True(DiscordRpc.IsClientId("123456789012345678"));
        Assert.False(DiscordRpc.IsClientId("12ab"));
    }

    [Fact]
    public void VoiceRoom_FollowsWhoSpeaks()
    {
        var room = new VoiceRoom();

        Assert.Equal(VoiceChange.Channel, room.Apply("""
            {"cmd":"GET_SELECTED_VOICE_CHANNEL","data":{"id":"42","name":"Général","voice_states":[
              {"nick":"Lucas","user":{"id":"1","username":"lucas"},"voice_state":{"mute":false,"self_mute":false}},
              {"user":{"id":"2","username":"marie","global_name":"Marie"},"voice_state":{"self_mute":true}}
            ]}}
            """));
        Assert.Equal("Général", room.ChannelName);
        Assert.Equal(["Lucas", "Marie"], room.Members.Select(m => m.Name));
        Assert.True(room.Members[1].Muted);

        Assert.Equal(VoiceChange.Members, room.Apply("""{"cmd":"DISPATCH","evt":"SPEAKING_START","data":{"user_id":"1"}}"""));
        Assert.True(room.Members[0].Speaking);
        Assert.Equal(VoiceChange.None, room.Apply("""{"cmd":"DISPATCH","evt":"SPEAKING_START","data":{"user_id":"1"}}"""));

        Assert.Equal(VoiceChange.Members, room.Apply("""{"cmd":"DISPATCH","evt":"VOICE_STATE_CREATE","data":{"user":{"id":"3","username":"tom"},"voice_state":{}}}"""));
        Assert.Equal(3, room.Members.Count);

        Assert.Equal(VoiceChange.Members, room.Apply("""{"cmd":"DISPATCH","evt":"VOICE_STATE_DELETE","data":{"user":{"id":"2"}}}"""));
        Assert.Equal(["Lucas", "tom"], room.Members.Select(m => m.Name));

        Assert.Equal(VoiceChange.SelfMute, room.Apply("""{"cmd":"DISPATCH","evt":"VOICE_SETTINGS_UPDATE","data":{"mute":true}}"""));
        Assert.True(room.SelfMuted);

        Assert.Equal(VoiceChange.ChannelSwitched, room.Apply("""{"cmd":"DISPATCH","evt":"VOICE_CHANNEL_SELECT","data":{"channel_id":null}}"""));
        Assert.Null(room.ChannelId);
        Assert.Empty(room.Members);

        Assert.Equal(VoiceChange.None, room.Apply("not json"));
    }

    // ---- T4 : paroles et Spotify -------------------------------------

    [Fact]
    public void Lrc_IsParsed_WithRepeatsAndOffset()
    {
        SyncedLyrics lyrics = Assert.IsType<SyncedLyrics>(Lyrics.Parse("""
            [ar:Daft Punk]
            [offset:+500]
            [00:10.50]One more time
            [00:14.00][00:30.00]We're gonna celebrate
            [00:20.00]
            """));

        Assert.Equal(4, lyrics.Lines.Count);
        Assert.Null(lyrics.LineAt(TimeSpan.FromSeconds(5)));
        Assert.Equal("One more time", lyrics.LineAt(TimeSpan.FromSeconds(10.1)));
        Assert.Equal("We're gonna celebrate", lyrics.LineAt(TimeSpan.FromSeconds(15)));
        Assert.Null(lyrics.LineAt(TimeSpan.FromSeconds(21)));
        Assert.Equal("We're gonna celebrate", lyrics.LineAt(TimeSpan.FromSeconds(40)));
        Assert.Null(Lyrics.Parse("plain text without stamps"));
    }

    [Fact]
    public void TheNextLine_SkipsBlanks_AndEndsWithNull()
    {
        SyncedLyrics lyrics = Assert.IsType<SyncedLyrics>(Lyrics.Parse("""
            [00:10.00]One more time
            [00:12.00]
            [00:14.00]We're gonna celebrate
            """));

        Assert.Equal("One more time", lyrics.NextLineAt(TimeSpan.FromSeconds(1)));
        Assert.Equal("We're gonna celebrate", lyrics.NextLineAt(TimeSpan.FromSeconds(11)));
        Assert.Null(lyrics.NextLineAt(TimeSpan.FromSeconds(20)));
    }

    [Fact]
    public void Spotify_AddsToTheQueue_ByTrackUri()
    {
        Assert.Equal("https://api.spotify.com/v1/me/player/queue?uri=spotify%3Atrack%3A4cOdK2wGLETKBW3PvgPWqT", SpotifyApi.AddToQueueUrl("4cOdK2wGLETKBW3PvgPWqT").AbsoluteUri);
        Assert.Contains("user-modify-playback-state", SpotifyApi.Scopes, StringComparison.Ordinal);
    }

    [Fact]
    public void LrcLib_QueryAndResponse()
    {
        Uri url = Lyrics.LrcLibQuery("Daft Punk", "One More Time - Remastered 2011", "Discovery", TimeSpan.FromSeconds(320.4))!;
        Assert.Equal("https://lrclib.net/api/get?artist_name=Daft%20Punk&track_name=One%20More%20Time&album_name=Discovery&duration=320", url.AbsoluteUri);
        Assert.Null(Lyrics.LrcLibQuery("", "x", null, TimeSpan.Zero));

        Assert.NotNull(Lyrics.FromLrcLib("""{"id":1,"instrumental":false,"syncedLyrics":"[00:01.00]Hello"}"""));
        Assert.Null(Lyrics.FromLrcLib("""{"id":1,"instrumental":true,"syncedLyrics":null}"""));
        Assert.Equal("Song", Lyrics.CleanTitle("Song (feat. Someone)"));
    }

    [Fact]
    public void Spotify_PkceAndResponses()
    {
        string verifier = SpotifyApi.CreateVerifier();
        Assert.Equal(64, verifier.Length);
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", SpotifyApi.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));

        Uri authorize = SpotifyApi.AuthorizeUrl("0123456789abcdef0123456789abcdef", "ch", "st");
        Assert.Contains("code_challenge_method=S256", authorize.Query);
        Assert.Contains("redirect_uri=http%3A%2F%2F127.0.0.1%3A43117%2Fspotify%2Fcallback", authorize.Query);

        Assert.Equal(("c0de", "st", null), SpotifyApi.ReadCallback("?code=c0de&state=st"));
        Assert.Equal("4uLU6hMCjMI75M1A2tKUQC", SpotifyApi.ReadFirstTrackId("""{"tracks":{"items":[{"id":"4uLU6hMCjMI75M1A2tKUQC"}]}}"""));
        Assert.Null(SpotifyApi.ReadFirstTrackId("""{"tracks":{"items":[]}}"""));
        Assert.True(SpotifyApi.ReadContains("[true]"));

        var queue = SpotifyApi.ReadQueue("""{"currently_playing":{},"queue":[{"name":"Aerodynamic","artists":[{"name":"Daft Punk"}]},{"name":"Digital Love","artists":[{"name":"Daft Punk"}]}]}""");
        Assert.Equal(2, queue.Count);
        Assert.Equal("Ensuite : Aerodynamic — Daft Punk", SpotifyApi.NextLine(queue, french: true));
        Assert.True(SpotifyApi.IsClientId("0123456789abcdef0123456789abcdef"));
        Assert.True(SpotifyApi.IsSpotify("Spotify.exe"));
    }
}
