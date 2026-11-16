using System.Globalization;
using Oy.Api.Domain;
using Oy.Api.Features.Classroom;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Oy.Api.Features.Commitments;

/// <summary>The signed commitment as a PDF, built on request from the frozen signature record (so it never drifts).</summary>
public static class CommitmentPdf
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private const string Forest = "#2f5d43";
    private const string Muted = "#5f6b62";

    static CommitmentPdf()
    {
        // Community licence: free for organisations under USD 1M annual revenue.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static string Channel(CommitmentSignature s) => s.Channel switch
    {
        CommitmentChannel.Tablet => string.IsNullOrEmpty(s.WitnessName) ? "Okulda, tablet üzerinde" : $"Okulda, tablet üzerinde ({s.WitnessName} yanında)",
        CommitmentChannel.GuardianApp => "Veli uygulamasında, veli hesabıyla",
        CommitmentChannel.Link => "Veliye gönderilen kişisel link ile",
        _ => "—",
    };

    public static string FileName(CommitmentSignature s, string childName)
    {
        var safe = new string($"Taahhutname {childName} {s.SignedTitle}".Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        while (safe.Contains("--")) safe = safe.Replace("--", "-");
        safe = safe.Trim('-');
        return (safe.Length > 120 ? safe[..120] : safe) + ".pdf";
    }

    public static byte[] Build(CommitmentSignature s, string childName, string programTitle)
    {
        if (s.SignedAt is not { } signedAt) throw new InvalidOperationException("Not signed.");
        var local = ClassroomEndpoints.ToLocal(signedAt);

        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(2, Unit.Centimetre);
            page.DefaultTextStyle(t => t.FontSize(10.5f).LineHeight(1.4f));

            page.Header().PaddingBottom(14).Row(row =>
            {
                row.RelativeItem().Text("Okulum Yeryüzü").FontSize(12).SemiBold().FontColor(Forest);
                row.RelativeItem().AlignRight().Text($"{childName} · {programTitle}").FontSize(9).FontColor(Muted);
            });

            page.Content().Column(col =>
            {
                col.Spacing(10);
                col.Item().Text(s.SignedTitle).FontSize(17).SemiBold();
                foreach (var paragraph in s.SignedBody.Replace("\r\n", "\n").Split("\n\n"))
                    col.Item().Text(paragraph.Trim());

                col.Item().PaddingTop(18).Border(0.75f).BorderColor(Colors.Grey.Lighten1).Padding(14).Column(box =>
                {
                    box.Spacing(4);
                    box.Item().Text("Elektronik imza bilgileri").SemiBold().FontColor(Forest);
                    Line(box, "İmzalayan", s.SignerName);
                    Line(box, "Çocuk", childName);
                    Line(box, "Eğitim", programTitle);
                    Line(box, "İmza zamanı", local.ToString("d MMMM yyyy, HH:mm:ss", Turkish) + " (Türkiye saati)");
                    Line(box, "Yöntem", Channel(s));
                    if (!string.IsNullOrEmpty(s.IpAddress)) Line(box, "IP adresi", s.IpAddress);
                    if (s.SignatureImage is { Length: > 0 } image)
                    {
                        box.Item().PaddingTop(8).Text("İmza").FontSize(9).FontColor(Muted);
                        box.Item().Height(80).AlignLeft().Image(image).FitArea();
                    }
                });
            });

            page.Footer().Column(f =>
            {
                f.Item().Text($"Belge parmak izi (SHA-256): {s.BodyHash}").FontSize(7.5f).FontColor(Muted);
                f.Item().Row(r =>
                {
                    r.RelativeItem().Text("Bu belge elektronik ortamda imzalanmıştır.").FontSize(7.5f).FontColor(Muted);
                    r.AutoItem().Text(t =>
                    {
                        t.DefaultTextStyle(x => x.FontSize(7.5f).FontColor(Muted));
                        t.CurrentPageNumber();
                        t.Span(" / ");
                        t.TotalPages();
                    });
                });
            });
        })).GeneratePdf();
    }

    private static void Line(ColumnDescriptor col, string label, string value) =>
        col.Item().Text(t =>
        {
            t.Span(label + ": ").FontColor(Muted);
            t.Span(value);
        });
}
