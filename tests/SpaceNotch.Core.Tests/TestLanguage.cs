using System.Runtime.CompilerServices;
using SpaceNotch.Core.Localization;

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
