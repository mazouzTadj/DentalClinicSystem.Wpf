using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace DentalClinic.Features;

// يولّد وصفة طبية بصيغة PDF جاهزة للطباعة على ورق A5 برأسية بسيطة للعيادة
public static class PrescriptionPdfExporter
{
    static PrescriptionPdfExporter()
    {
        // نفس الترخيص المجتمعي المستخدم في مصدّر ملف المريض - الإعداد آمن للتكرار بين الملفين
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static byte[] Generate(string patientName, DateTime date, List<PrescriptionLineViewModel> lines, string? notes)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A5);
                page.Margin(28);
                page.DefaultTextStyle(x => x.FontSize(11));

                page.Header().Column(headerCol =>
                {
                    headerCol.Item().Row(row =>
                    {
                        var logoBytes = LogoLoader.TryLoadLogoBytes();
                        if (logoBytes != null)
                        {
                            row.ConstantItem(36).Image(logoBytes);
                        }

                        row.RelativeItem().PaddingLeft(logoBytes != null ? 8 : 0).Column(col =>
                        {
                            col.Item().Text("Dental Clinic").FontSize(16).SemiBold().FontColor(Colors.Blue.Darken2);
                            col.Item().Text("Prescription").FontSize(11).FontColor(Colors.Grey.Darken1);
                        });
                    });

                    headerCol.Item().PaddingTop(6).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                });

                page.Content().Column(col =>
                {
                    col.Spacing(4);

                    col.Item().PaddingTop(12).Row(row =>
                    {
                        row.RelativeItem().Text($"Patient: {patientName}").SemiBold();
                        row.RelativeItem().AlignRight().Text($"Date: {date:yyyy-MM-dd}");
                    });

                    col.Item().PaddingTop(16).Text("Rx").FontSize(13).SemiBold().FontColor(Colors.Blue.Darken2);

                    foreach (var line in lines)
                    {
                        col.Item().PaddingTop(10).Column(lineCol =>
                        {
                            lineCol.Item().Text(line.MedicationName).SemiBold().FontSize(12);

                            var detail = string.Join("   |   ",
                                new[] { line.Dosage, line.Duration }.Where(s => !string.IsNullOrWhiteSpace(s)));

                            if (!string.IsNullOrWhiteSpace(detail))
                            {
                                lineCol.Item().Text(detail).FontSize(10).FontColor(Colors.Grey.Darken2);
                            }

                            if (!string.IsNullOrWhiteSpace(line.Instructions))
                            {
                                lineCol.Item().Text(line.Instructions).FontSize(10).Italic().FontColor(Colors.Grey.Darken1);
                            }
                        });
                    }

                    if (!string.IsNullOrWhiteSpace(notes))
                    {
                        col.Item().PaddingTop(18).Text("Notes").FontSize(11).SemiBold();
                        col.Item().Text(notes).FontSize(10);
                    }

                    col.Item().PaddingTop(40).AlignRight().Column(sig =>
                    {
                        sig.Item().Text("_______________________");
                        sig.Item().AlignRight().Text("Doctor's signature").FontSize(9).FontColor(Colors.Grey.Darken1);
                    });
                });

                page.Footer().AlignCenter().Text(
                    $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}").FontSize(8).FontColor(Colors.Grey.Darken1);
            });
        });

        return document.GeneratePdf();
    }
}
