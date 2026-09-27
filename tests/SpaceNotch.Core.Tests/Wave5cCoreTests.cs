using System;
using System.Linq;
using SpaceNotch.Core.Calendar;
using SpaceNotch.Core.Motion;
using SpaceNotch.Core.Share;
using SpaceNotch.Core.Weather;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>Vague 5c : rendez-vous, météo, partage.</summary>
public class Wave5cCoreTests
{
    [Theory]
    [InlineData("Rejoindre : https://teams.microsoft.com/l/meetup-join/19%3ameeting_abc/0?context=x", "Teams")]
    [InlineData("<https://meet.google.com/abc-defg-hij>", "Meet")]
    [InlineData("Salle 3 · https://acme.zoom.us/j/123456789?pwd=xyz.", "Zoom")]
    [InlineData("https://acme.webex.com/meet/jdoe", "Webex")]
    public void MeetingLinks_AreFound(string text, string service)
    {
        Uri? link = MeetingLink.Find(null, "", text);
        Assert.NotNull(link);
        Assert.Equal(service, MeetingLink.ServiceOf(link));
        Assert.DoesNotContain(">", link.ToString());
    }

    [Theory]
    [InlineData("Voir https://example.com/agenda")]
    [InlineData("http://meet.google.com/abc-defg-hij")]
    [InlineData("https://teams.microsoft.com/")]
    [InlineData("")]
    public void OrdinaryLinks_AreNotMeetings(string text)
        => Assert.Null(MeetingLink.Find(text));

    [Fact]
    public void Countdown_FollowsTheMeeting()
    {
        var start = new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero);
        DateTimeOffset end = start.AddMinutes(30);

        Assert.Equal(MeetingPhase.None, MeetingCountdown.Phase(start, end, start.AddMinutes(-6)));
        Assert.Equal(MeetingPhase.Soon, MeetingCountdown.Phase(start, end, start.AddMinutes(-4)));
        Assert.Equal(MeetingPhase.Now, MeetingCountdown.Phase(start, end, start.AddMinutes(3)));
        Assert.Equal(MeetingPhase.None, MeetingCountdown.Phase(start, end, start.AddMinutes(11)));

        Assert.Equal(0.8, MeetingCountdown.Remaining(start, start.AddMinutes(-4)), 3);
        Assert.Equal("dans 4 min", MeetingCountdown.Label(start, start.AddMinutes(-3.5), french: true));
        Assert.Equal("in 30 s", MeetingCountdown.Label(start, start.AddSeconds(-30), french: false));
        Assert.Equal("4 min", MeetingCountdown.TimeLeft(start, start.AddMinutes(-3.5)));
        Assert.Equal("30 s", MeetingCountdown.TimeLeft(start, start.AddSeconds(-30)));
        Assert.Equal(start.AddMinutes(-5), MeetingCountdown.NextChange(start, end, start.AddMinutes(-20)));
        Assert.Equal(start, MeetingCountdown.NextChange(start, end, start.AddMinutes(-2)));
        Assert.Null(MeetingCountdown.NextChange(start, end, start.AddHours(1)));
    }

    [Theory]
    [InlineData(0, true, "WeatherSun")]
    [InlineData(0, false, "Moon")]
    [InlineData(3, true, "WeatherCloud")]
    [InlineData(45, true, "WeatherFog")]
    [InlineData(61, true, "WeatherRain")]
    [InlineData(81, true, "WeatherRain")]
    [InlineData(73, true, "WeatherSnow")]
    [InlineData(95, true, "WeatherStorm")]
    public void WeatherCodes_MapToPixelIcons(int code, bool day, string icon)
    {
        Assert.Equal(icon, WeatherCodes.IconFor(code, day));
        Assert.NotNull(PixelGlyphs.Resolve(icon));
    }

    [Fact]
    public void OpenMeteo_Responses_AreRead()
    {
        WeatherReport? report = WeatherCodes.ParseForecast("""{"latitude":48.86,"current":{"time":"2026-09-28T10:00","temperature_2m":14.6,"weather_code":61,"is_day":1}}""");
        Assert.NotNull(report);
        Assert.Equal(61, report.Code);
        Assert.Equal("WeatherRain", report.IconKey);
        Assert.StartsWith("15", report.Temperature);

        WeatherPlace? place = WeatherCodes.ParsePlace("""{"results":[{"name":"Lyon","latitude":45.75,"longitude":4.85}]}""");
        Assert.Equal("Lyon", place?.Name);
        Assert.Null(WeatherCodes.ParsePlace("""{"generationtime_ms":0.2}"""));
        Assert.Null(WeatherCodes.ParseForecast("not json"));

        Assert.Contains("latitude=45.75", WeatherCodes.ForecastUri(45.75, 4.85).ToString());
        Assert.Contains("name=Saint-%C3%89tienne", WeatherCodes.GeocodingUri("Saint-Étienne", true).AbsoluteUri);
    }

    [Fact]
    public void Rain_ReallyFalls()
    {
        Assert.Equal(4, WeatherAnimation.Length("WeatherRain"));
        Assert.Equal(0, WeatherAnimation.Length("WeatherSun"));

        bool[] a = WeatherAnimation.Frame("WeatherRain", 0), b = WeatherAnimation.Frame("WeatherRain", 1);
        Assert.False(a.SequenceEqual(b));

        // La goutte de la colonne 1 descend d'une rangée d'une image à l'autre.
        int RowOf(bool[] f) => Enumerable.Range(3, 4).First(r => f[(r * 7) + 1]);
        Assert.Equal((RowOf(a) - 3 + 1) % 4, RowOf(b) - 3);
    }

    [Fact]
    public void ShareLinks_AreSingleUse_AndExpire()
    {
        var now = DateTimeOffset.UnixEpoch;
        string token = ShareLink.TokenFrom(Enumerable.Range(1, 16).Select(i => (byte)i).ToArray());
        var link = new ShareLink(token, "photo été.jpg", now);

        Assert.DoesNotContain('/', token);
        Assert.Equal($"http://192.168.1.20:8421/{token}/photo%20%C3%A9t%C3%A9.jpg", link.Url("192.168.1.20", 8421));

        Assert.True(link.Accepts($"GET /{token}/photo.jpg HTTP/1.1", now.AddMinutes(1)));
        Assert.False(link.Accepts("GET /autre/photo.jpg HTTP/1.1", now));
        Assert.False(link.Accepts($"POST /{token}/x HTTP/1.1", now));
        Assert.False(link.Accepts($"GET /{token}/x HTTP/1.1", now.AddMinutes(11)));

        link.MarkUsed();
        Assert.False(link.Accepts($"GET /{token}/photo.jpg HTTP/1.1", now));

        string headers = ShareLink.ResponseHeaders("photo été.jpg", 1234);
        Assert.Contains("Content-Length: 1234", headers);
        Assert.Contains("filename=\"photo _t_.jpg\"", headers);
        Assert.EndsWith("\r\n\r\n", headers);
    }
}
