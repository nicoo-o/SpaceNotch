using System;
using System.IO;
using SpaceNotch.Infrastructure.Config;
using Xunit;

namespace SpaceNotch.Core.Tests;

public sealed class ConfigAtomicTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "spacenotch-config-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void A_corrupted_file_falls_back_to_the_last_good_write()
    {
        var manager = new ConfigManager(_folder);

        AppSettings first = manager.Load();
        first.HideOverFullscreen = false;
        manager.Save(first);

        // Seconde écriture : la première devient la copie de secours.
        first.StartWithWindows = true;
        manager.Save(first);

        File.WriteAllText(manager.ConfigFilePath, "{ tronqué");

        AppSettings loaded = new ConfigManager(_folder).Load();

        Assert.False(loaded.HideOverFullscreen);
        Assert.False(File.Exists(manager.ConfigFilePath + ".tmp"));
    }

    [Fact]
    public void Saving_leaves_no_temporary_file()
    {
        var manager = new ConfigManager(_folder);
        manager.Save(manager.Load());
        manager.Save(manager.Load());

        Assert.True(File.Exists(manager.ConfigFilePath));
        Assert.False(File.Exists(manager.ConfigFilePath + ".tmp"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

public sealed class PluginAllowlistTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "spacenotch-plugins-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void A_plugin_is_allowed_only_as_it_was_approved()
    {
        Directory.CreateDirectory(_folder);
        string plugin = Path.Combine(_folder, "Meteo.dll");
        File.WriteAllText(plugin, "version 1");

        var approved = new System.Collections.Generic.Dictionary<string, string>
        {
            ["Meteo.dll"] = SpaceNotch.Infrastructure.Plugins.PluginAllowlist.Hash(plugin)!
        };
        var allowlist = new SpaceNotch.Infrastructure.Plugins.PluginAllowlist(approved);

        Assert.True(allowlist.IsAllowed(plugin));

        // Remplacé ou modifié : redevient « en attente ».
        File.WriteAllText(plugin, "version 2, autre code");
        Assert.False(allowlist.IsAllowed(plugin));
        Assert.Single(SpaceNotch.Infrastructure.Plugins.PluginLoader.FindPending(_folder, allowlist));

        // Jamais approuvé.
        File.WriteAllText(Path.Combine(_folder, "Inconnu.dll"), "x");
        Assert.Equal(2, SpaceNotch.Infrastructure.Plugins.PluginLoader.FindPending(_folder, allowlist).Count);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
