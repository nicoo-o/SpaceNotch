using System;
using SpaceNotch.Core.Setup;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>n° 56 : signaler un problème ouvre un rapport prérempli, sans rien de personnel.</summary>
public sealed class ProblemReportTests
{
    [Fact]
    public void The_report_opens_the_bug_template_with_version_and_windows()
    {
        Uri uri = ProblemReport.NewIssue("1.4.2", new Version(10, 0, 26200, 0));

        Assert.Equal(
            "https://github.com/nicoo-o/SpaceNotch/issues/new?template=bogue.yml&version=1.4.2&windows=Windows%2011%20%28build%2026200%29",
            uri.AbsoluteUri);
    }

    [Theory]
    [InlineData(10, 0, 22631, "Windows 11 (build 22631)")]
    [InlineData(10, 0, 19045, "Windows 10 (build 19045)")]
    [InlineData(6, 3, 9600, "Windows 6.3 (build 9600)")]
    public void Windows_is_named_as_people_read_it(int major, int minor, int build, string expected)
        => Assert.Equal(expected, ProblemReport.WindowsName(new Version(major, minor, build)));

    [Fact]
    public void The_report_carries_nothing_but_the_template_the_version_and_windows()
    {
        Uri uri = ProblemReport.NewIssue("1.4.2", new Version(10, 0, 26200));

        Assert.Equal("?template=bogue.yml&version=1.4.2&windows=Windows%2011%20%28build%2026200%29", uri.Query);
        Assert.DoesNotContain(Environment.UserName, uri.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_template_exists_in_the_repository()
    {
        string root = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(SourceFile())!, "..", ".."));
        Assert.True(System.IO.File.Exists(System.IO.Path.Combine(root, ".github", "ISSUE_TEMPLATE", ProblemReport.Template)));
    }

    private static string SourceFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
}
