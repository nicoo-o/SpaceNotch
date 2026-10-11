using System.Runtime.CompilerServices;
using SpaceNotch.Core.Localization;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>
/// Les tests lisent les textes en français, quelle que soit la langue de la
/// machine qui les exécute : une CI en anglais ne doit pas changer leur verdict.
/// </summary>
internal static class TestLanguage
{
    [ModuleInitializer]
    internal static void UseFrench() => Lang.French = true;
}

/// <summary>
/// Les tests qui changent <see cref="Lang.French"/> : la langue est globale, et
/// un test qui la passait en anglais faisait lire « App » à un autre test
/// exécuté en même temps (n° 84, n° 73). Cette collection ne tourne jamais en
/// parallèle des autres ; tout test qui touche à la langue s'y range.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GlobalLanguageGroup
{
    public const string Name = "Langue globale";
}
