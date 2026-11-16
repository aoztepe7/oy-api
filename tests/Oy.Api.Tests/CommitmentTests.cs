using Oy.Api.Domain;
using Oy.Api.Features.Commitments;

namespace Oy.Api.Tests;

public class CommitmentTests
{
    // 1×1 transparent PNG
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    [Fact]
    public void Placeholders_are_filled_per_enrollment()
    {
        var text = CommitmentRules.Render("{veli}, {cocuk} adlı çocuğumun {egitim} eğitimine katılmasına {tarih} itibarıyla izin veriyorum.",
            "Ayşe Yılmaz", "Ece Yılmaz", "Orman Okulu", new DateOnly(2026, 11, 16));
        Assert.Equal("Ayşe Yılmaz, Ece Yılmaz adlı çocuğumun Orman Okulu eğitimine katılmasına 16 Kasım 2026 itibarıyla izin veriyorum.", text);
    }

    [Fact]
    public void Hash_changes_with_the_text_but_not_with_surrounding_space()
    {
        var a = CommitmentRules.Hash("Taahhütname", "Metin");
        Assert.Equal(64, a.Length);
        Assert.Equal(a, CommitmentRules.Hash(" Taahhütname ", "Metin\n"));
        Assert.NotEqual(a, CommitmentRules.Hash("Taahhütname", "Metin."));
    }

    [Fact]
    public void Only_png_data_urls_are_accepted_as_signatures()
    {
        Assert.Equal(Png, CommitmentRules.DecodeSignature("data:image/png;base64," + Convert.ToBase64String(Png)));
        Assert.Null(CommitmentRules.DecodeSignature(null));
        Assert.Null(CommitmentRules.DecodeSignature("data:image/png;base64,bm90IGEgcG5n"));
        Assert.Null(CommitmentRules.DecodeSignature("data:image/png;base64,%%%"));
        var huge = new byte[CommitmentRules.SignatureMaxBytes + 1];
        Png.CopyTo(huge, 0);
        Assert.Null(CommitmentRules.DecodeSignature(Convert.ToBase64String(huge)));
    }

    [Fact]
    public void Signed_commitment_renders_as_pdf()
    {
        var s = new CommitmentSignature
        {
            SignedAt = new DateTime(2026, 11, 16, 9, 30, 0, DateTimeKind.Utc),
            SignedTitle = "Veli Taahhütnamesi",
            SignedBody = "Çocuğumun doğa etkinliklerine katılmasına izin veriyorum.\n\nŞartları okudum, ğüşıöç ĞÜŞİÖÇ.",
            BodyHash = CommitmentRules.Hash("Veli Taahhütnamesi", "x"),
            SignerName = "Ayşe Yılmaz",
            SignatureImage = Png,
            Channel = CommitmentChannel.Tablet,
            WitnessName = "Öğretmen Ali",
            IpAddress = "203.0.113.5",
        };
        var pdf = CommitmentPdf.Build(s, "Ece Yılmaz", "Orman Okulu");
        Assert.True(pdf.Length > 1000);
        Assert.Equal("%PDF"u8.ToArray(), pdf[..4]);
        if (Environment.GetEnvironmentVariable("OY_PDF_OUT") is { Length: > 0 } path) File.WriteAllBytes(path, pdf);

        Assert.Equal("Taahhutname-Ece-Yılmaz-Veli-Taahhütnamesi.pdf", CommitmentPdf.FileName(s, "Ece Yılmaz"));
    }
}
