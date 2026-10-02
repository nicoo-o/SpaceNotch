using System.Text;
using SpaceNotch.Core.Setup;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>
/// L'élément d'identité allumé ou éteint dans l'exécutable : même longueur
/// dans les deux sens, aucun octet hors de l'élément touché, et un commentaire
/// XML valide une fois éteint.
/// </summary>
public sealed class IdentityManifestTests
{
    // Tel que l'outil de manifeste l'écrit dans SpaceNotch.exe v1.13.1.
    private const string Real = "<msix xmlns=\"urn:schemas-microsoft-com:msix.v1\" publisher=\"CN=SpaceNotch\" packageName=\"SpaceNotch.Identity\" applicationId=\"SpaceNotch\"></msix>";

    private static byte[] Executable(string element)
        => Encoding.ASCII.GetBytes("MZ\0\0<assembly><asmv3:file name=\"x.dll\"></asmv3:file>" + element + "<application/></assembly>\0\0BUNDLE");

    [Fact]
    public void Les_deux_formes_ont_la_meme_longueur()
    {
        Assert.Equal(IdentityManifest.OnStart.Length, IdentityManifest.OffStart.Length);
        Assert.Equal(IdentityManifest.OnEnd.Length, IdentityManifest.OffEnd.Length);
        Assert.Equal(IdentityManifest.Sample(true).Length, IdentityManifest.Sample(false).Length);
        Assert.Equal(Real, Encoding.ASCII.GetString(IdentityManifest.Sample(true)));
    }

    [Fact]
    public void Trouve_l_element_reel_et_seulement_lui()
    {
        byte[] exe = Executable(Real);

        IdentityManifest.Element element = Assert.Single(IdentityManifest.Find(exe, exe.Length, 1000));

        Assert.True(element.On);
        Assert.Equal(Real.Length, element.Length);
        Assert.Equal(1000 + Encoding.ASCII.GetString(exe).IndexOf("<msix", StringComparison.Ordinal), element.Offset);
    }

    [Fact]
    public void Eteindre_puis_rallumer_rend_les_octets_d_origine()
    {
        byte[] exe = Executable(Real);
        byte[] original = (byte[])exe.Clone();
        IdentityManifest.Element element = Assert.Single(IdentityManifest.Find(exe, exe.Length, 0));
        Span<byte> span = exe.AsSpan((int)element.Offset, element.Length);

        Assert.True(IdentityManifest.Rewrite(span, on: false));
        string off = Encoding.ASCII.GetString(exe);
        Assert.DoesNotContain("<msix", off, StringComparison.Ordinal);
        Assert.Equal(original.Length, exe.Length);

        // Hors de l'élément, rien n'a bougé.
        Assert.True(exe.AsSpan(0, (int)element.Offset).SequenceEqual(original.AsSpan(0, (int)element.Offset)));
        int after = (int)element.Offset + element.Length;
        Assert.True(exe.AsSpan(after).SequenceEqual(original.AsSpan(after)));

        // Éteint, il est retrouvé comme tel.
        IdentityManifest.Element again = Assert.Single(IdentityManifest.Find(exe, exe.Length, 0));
        Assert.False(again.On);

        Assert.True(IdentityManifest.Rewrite(span, on: true));
        Assert.Equal(original, exe);
    }

    [Fact]
    public void Eteint_c_est_un_commentaire_XML_valide()
    {
        string off = Encoding.ASCII.GetString(IdentityManifest.Sample(false));

        Assert.StartsWith("<!--", off, StringComparison.Ordinal);
        Assert.EndsWith("-->", off, StringComparison.Ordinal);

        // Un commentaire XML ne peut pas contenir « -- » ailleurs qu'à ses bornes.
        Assert.DoesNotContain("--", off[4..^3], StringComparison.Ordinal);

        var document = new System.Xml.XmlDocument();
        document.LoadXml("<assembly>" + off + "</assembly>");
        Assert.Equal(System.Xml.XmlNodeType.Comment, document.DocumentElement!.FirstChild!.NodeType);
    }

    [Fact]
    public void Reecrire_dans_l_etat_deja_present_ne_change_rien()
    {
        byte[] on = IdentityManifest.Sample(true);
        byte[] copy = (byte[])on.Clone();

        Assert.True(IdentityManifest.Rewrite(on, on: true));
        Assert.Equal(copy, on);
    }

    [Fact]
    public void Ignore_un_autre_element_msix_et_refuse_des_octets_etrangers()
    {
        byte[] other = Encoding.ASCII.GetBytes("<msix xmlns=\"urn:autre\" a=\"b\"></msix>");
        Assert.Empty(IdentityManifest.Find(other, other.Length, 0));

        byte[] junk = Encoding.ASCII.GetBytes("<application></application>");
        Assert.False(IdentityManifest.Rewrite(junk, on: false));
    }

    [Fact]
    public void Un_element_coupe_au_bord_du_bloc_n_est_pas_retenu()
    {
        byte[] exe = Executable(Real);
        int cut = Encoding.ASCII.GetString(exe).IndexOf("></msix>", StringComparison.Ordinal);

        Assert.Empty(IdentityManifest.Find(exe, cut, 0));
    }
}

/// <summary>Le correcteur de fichier : élément à cheval sur deux blocs, aller-retour exact.</summary>
public sealed class IdentityManifestFileTests
{
    [Fact]
    public void Eteint_et_rallume_un_fichier_meme_a_cheval_sur_deux_blocs()
    {
        string path = Path.Combine(Path.GetTempPath(), $"spacenotch-{Guid.NewGuid():N}.exe");
        byte[] element = IdentityManifest.Sample(true);
        var content = new byte[(4 * 1024 * 1024) + 4096];
        new Random(7).NextBytes(content);
        int at = (4 * 1024 * 1024) - 40;
        element.CopyTo(content, at);
        File.WriteAllBytes(path, content);

        try
        {
            Assert.True(SpaceNotch.Platform.Windows.Setup.IdentityManifestFile.IsOn(path));
            Assert.True(SpaceNotch.Platform.Windows.Setup.IdentityManifestFile.Set(path, on: false));
            Assert.False(SpaceNotch.Platform.Windows.Setup.IdentityManifestFile.IsOn(path));

            byte[] off = File.ReadAllBytes(path);
            Assert.Equal(content.Length, off.Length);
            Assert.Equal(IdentityManifest.Sample(false), off.AsSpan(at, element.Length).ToArray());

            Assert.True(SpaceNotch.Platform.Windows.Setup.IdentityManifestFile.Set(path, on: true));
            Assert.Equal(content, File.ReadAllBytes(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Refuse_un_fichier_sans_element_ou_avec_deux()
    {
        string path = Path.Combine(Path.GetTempPath(), $"spacenotch-{Guid.NewGuid():N}.exe");

        try
        {
            File.WriteAllBytes(path, Encoding.ASCII.GetBytes("MZ rien ici"));
            Assert.False(SpaceNotch.Platform.Windows.Setup.IdentityManifestFile.Set(path, on: false));

            byte[] one = IdentityManifest.Sample(true);
            File.WriteAllBytes(path, [.. one, .. "  "u8.ToArray(), .. one]);
            Assert.False(SpaceNotch.Platform.Windows.Setup.IdentityManifestFile.Set(path, on: false));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
