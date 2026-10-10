using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using SpaceNotch.Core.Setup;
using SpaceNotch.Platform.Windows.Setup;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>
/// n° 51 : l'installeur refait la variante installée signée par la CI, octet
/// pour octet, au lieu de réécrire son propre exécutable et d'en casser la signature.
/// </summary>
public sealed class InstalledVariantTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "spacenotch-variant-" + Guid.NewGuid().ToString("N"));

    public InstalledVariantTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Theory]
    [InlineData(false, false, VariantPlan.FlipInPlace)]
    [InlineData(false, true, VariantPlan.FlipInPlace)]
    [InlineData(true, true, VariantPlan.Rebuild)]
    [InlineData(true, false, VariantPlan.KeepOff)]
    public void Un_executable_signe_n_est_jamais_reecrit_sans_la_signature_de_l_installee(bool sourceSigned, bool haveSignature, VariantPlan expected)
        => Assert.Equal(expected, InstalledVariant.Plan(sourceSigned, haveSignature));

    [Fact]
    public void La_signature_publiee_se_relit_telle_quelle()
    {
        var signature = new VariantSignature(0xA1B2C3D4, 4096, [1, 2, 3, 4, 5, 6, 7, 8]);
        VariantSignature? read = InstalledVariant.Parse(InstalledVariant.Serialize(signature));

        Assert.NotNull(read);
        Assert.Equal(signature.Checksum, read.Checksum);
        Assert.Equal(signature.CertificateOffset, read.CertificateOffset);
        Assert.Equal(signature.Certificate, read.Certificate);

        // Une variante non signée : table vide, sans place.
        Assert.NotNull(InstalledVariant.Parse(InstalledVariant.Serialize(new VariantSignature(7, 0, []))));
    }

    [Fact]
    public void Une_signature_incoherente_est_refusee()
    {
        byte[] good = InstalledVariant.Serialize(new VariantSignature(1, 4096, new byte[16]));

        Assert.Null(InstalledVariant.Parse(good.AsSpan(0, good.Length - 1)));
        Assert.Null(InstalledVariant.Parse(InstalledVariant.Serialize(new VariantSignature(1, 4100, new byte[16]))));
        Assert.Null(InstalledVariant.Parse(InstalledVariant.Serialize(new VariantSignature(1, 64, []))));

        byte[] foreign = (byte[])good.Clone();
        foreign[0] = (byte)'X';
        Assert.Null(InstalledVariant.Parse(foreign));
    }

    [Fact]
    public void L_en_tete_PE_dit_ou_ecrire()
    {
        byte[] start = MinimalHeader(pe32Plus: true);
        PeHeader? header = InstalledVariant.ReadHeader(start);

        Assert.NotNull(header);
        Assert.Equal(0x80 + 24 + 64, header.Value.ChecksumOffset);
        Assert.Equal(0x80 + 24 + 112 + 32, header.Value.SecurityDirectoryOffset);
        Assert.False(header.Value.HasSignature);

        Assert.Equal(0x80 + 24 + 96 + 32, InstalledVariant.ReadHeader(MinimalHeader(pe32Plus: false))!.Value.SecurityDirectoryOffset);
        Assert.Null(InstalledVariant.ReadHeader(new byte[512]));
    }

    [Fact]
    public void Une_vraie_dll_se_lit()
    {
        string path = typeof(InstalledVariantTests).Assembly.Location;
        byte[] start = File.ReadAllBytes(path).AsSpan(0, InstalledVariant.HeaderBytes).ToArray();

        Assert.NotNull(InstalledVariant.ReadHeader(start));
    }

    [Fact]
    public void La_variante_installee_est_refaite_octet_pour_octet()
    {
        // Deux variantes « signées » qui ne diffèrent que de l'identité et de leur
        // table des certificats, comme celles de la CI (de vrais certificats sont
        // posés par la CI : voir le travail « Variantes de l'exécutable »).
        (string downloaded, string installed) = Variants(offCertificate: 37, onCertificate: 1203);
        VariantSignature signature = InstalledVariantFile.Extract(installed);
        string copy = Path.Combine(_directory, "copie.exe");
        File.Copy(downloaded, copy);

        Assert.True(InstalledVariantFile.Rebuild(copy, signature, Sha256(installed)));
        Assert.Equal(File.ReadAllBytes(installed), File.ReadAllBytes(copy));
        Assert.True(IdentityManifestFile.IsOn(copy));
    }

    [Fact]
    public void Sans_la_bonne_empreinte_rien_n_est_garde()
    {
        (string downloaded, string installed) = Variants(offCertificate: 40, onCertificate: 48);
        string copy = Path.Combine(_directory, "copie.exe");
        File.Copy(downloaded, copy);

        Assert.False(InstalledVariantFile.Rebuild(copy, InstalledVariantFile.Extract(installed), new string('0', 64)));
    }

    [Fact]
    public void Une_signature_faite_pour_une_autre_image_est_refusee()
    {
        (string downloaded, string installed) = Variants(offCertificate: 40, onCertificate: 48);
        VariantSignature other = InstalledVariantFile.Extract(installed) with { CertificateOffset = 64 };
        string copy = Path.Combine(_directory, "copie.exe");
        File.Copy(downloaded, copy);

        Assert.False(InstalledVariantFile.Rebuild(copy, other, Sha256(installed)));
    }

    /// <summary>
    /// Dans la CI seulement (travail « Variantes de l'exécutable ») : les deux
    /// variantes de l'exécutable publié, vraiment signées, et la signature de
    /// l'installée extraite par tools/variants/Variant.ps1. Ailleurs, rien à faire.
    /// </summary>
    [Fact]
    public void Dans_la_CI_la_vraie_variante_signee_est_refaite()
    {
        if (Environment.GetEnvironmentVariable("SPACENOTCH_VARIANTS") is not { Length: > 0 } folder)
        {
            return;
        }

        string downloaded = Path.Combine(folder, "telechargee.exe");
        string installed = Path.Combine(folder, "installee.exe");
        string rebuilt = Path.Combine(folder, "refaite.exe");
        VariantSignature? published = InstalledVariant.Parse(File.ReadAllBytes(Path.Combine(folder, InstalledVariant.SignatureAsset)));

        // Le script de publication et l'application lisent la même signature.
        VariantSignature extracted = InstalledVariantFile.Extract(installed);
        Assert.NotNull(published);
        Assert.Equal(extracted.Checksum, published.Checksum);
        Assert.Equal(extracted.CertificateOffset, published.CertificateOffset);
        Assert.Equal(extracted.Certificate, published.Certificate);
        Assert.True(InstalledVariantFile.IsSigned(downloaded));

        File.Copy(downloaded, rebuilt, overwrite: true);
        Assert.True(InstalledVariantFile.Rebuild(rebuilt, published, Sha256(installed)));
    }

    /// <summary>Une vraie DLL, l'élément d'identité à la suite, puis une table des certificats à la fin.</summary>
    private (string Downloaded, string Installed) Variants(int offCertificate, int onCertificate)
    {
        byte[] image = File.ReadAllBytes(typeof(InstalledVariantTests).Assembly.Location);
        string downloaded = Path.Combine(_directory, "telechargee.exe");
        string installed = Path.Combine(_directory, "installee.exe");
        File.WriteAllBytes(downloaded, Signed(image, on: false, offCertificate, checksum: 0x1111));
        File.WriteAllBytes(installed, Signed(image, on: true, onCertificate, checksum: 0x2222));
        return (downloaded, installed);
    }

    private static byte[] Signed(byte[] image, bool on, int certificateLength, uint checksum)
    {
        byte[] body = [.. image, .. IdentityManifest.Sample(on), .. new byte[3]];
        int offset = (body.Length + 7) / 8 * 8;
        var file = new byte[offset + certificateLength];
        body.CopyTo(file, 0);
        RandomNumberGenerator.Fill(file.AsSpan(offset));

        PeHeader header = InstalledVariant.ReadHeader(file)!.Value;
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(header.ChecksumOffset), checksum);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(header.SecurityDirectoryOffset), (uint)offset);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(header.SecurityDirectoryOffset + 4), (uint)certificateLength);
        return file;
    }

    private static byte[] MinimalHeader(bool pe32Plus)
    {
        var start = new byte[512];
        start[0] = (byte)'M';
        start[1] = (byte)'Z';
        BinaryPrimitives.WriteInt32LittleEndian(start.AsSpan(0x3C), 0x80);
        "PE\0\0"u8.CopyTo(start.AsSpan(0x80));
        BinaryPrimitives.WriteUInt16LittleEndian(start.AsSpan(0x80 + 24), pe32Plus ? (ushort)0x20B : (ushort)0x10B);
        return start;
    }

    private static string Sha256(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
