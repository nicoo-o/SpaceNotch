using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SpaceNotch.Core.Share;
using Xunit;

namespace SpaceNotch.Core.Tests;

public sealed class ShareLinkTests
{
    [Fact]
    public async Task ShareLink_ConcurrentClaims_OnlyOneRequestSucceeds()
    {
        var link = new ShareLink("token", "photo.png", System.DateTimeOffset.UtcNow);
        string request = "GET /token/photo.png HTTP/1.1";

        Task<bool>[] claims = Enumerable.Range(0, 32)
            .Select(_ => Task.Run(() => link.TryClaim(request, System.DateTimeOffset.UtcNow)))
            .ToArray();

        bool[] results = await Task.WhenAll(claims);

        Assert.Equal(1, results.Count(result => result));
        Assert.True(link.Used);
    }
}
