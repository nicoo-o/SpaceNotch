using Microsoft.UI.Xaml.Markup;
using SpaceNotch.Core.Localization;

namespace SpaceNotch_App.Views;

/// <summary>
/// Texte bilingue en XAML : <c>Text="{l:Loc Fr='Rechercher', En='Search'}"</c>.
/// Rend la phrase dans la langue de Windows (<see cref="Lang"/>).
/// </summary>
[MarkupExtensionReturnType(ReturnType = typeof(string))]
public sealed partial class Loc : MarkupExtension
{
    /// <summary>Phrase française.</summary>
    public string Fr { get; set; } = string.Empty;

    /// <summary>Phrase anglaise.</summary>
    public string En { get; set; } = string.Empty;

    protected override object ProvideValue() => Lang.T(Fr, En);
}
