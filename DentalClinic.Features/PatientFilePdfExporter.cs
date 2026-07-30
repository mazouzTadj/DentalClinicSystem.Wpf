using DentalClinic.Data.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace DentalClinic.Features;

// يولّد ملف PDF لسجل المريض الطبي الكامل (بيانات أساسية + كل الزيارات السابقة)
public static class PatientFilePdfExporter
{
    static PatientFilePdfExporter()
    {
        // ترخيص QuestPDF المجتمعي (Community) - مجاني للاستخدام التجاري لعيادة صغيرة كهذه
        // (حسب شروط QuestPDF: مجاني للأفراد والشركات التي إيراداتها السنوية أقل من مليون دولار)
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static byte[] Generate(Patient patient, List<MedicalSession> sessions)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Column(headerCol =>
                {
                    headerCol.Item().Row(row =>
                    {
                        var logoBytes = LogoLoader.TryLoadLogoBytes();
                        if (logoBytes != null)
                        {
                            row.ConstantItem(42).Image(logoBytes);
                        }

                        row.RelativeItem().PaddingLeft(logoBytes != null ? 10 : 0).Column(col =>
                        {
                            col.Item().Text("Dental Clinic - Patient Medical Record")
                                .FontSize(18).SemiBold().FontColor(Colors.Blue.Darken2);
                            col.Item().Text($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}")
                                .FontSize(9).FontColor(Colors.Grey.Darken1);
                        });
                    });

                    headerCol.Item().PaddingTop(8).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                });

                page.Content().Column(col =>
                {
                    col.Spacing(6);

                    // بيانات المريض الأساسية
                    col.Item().PaddingTop(14).Text(patient.FullName).FontSize(16).SemiBold();
                    col.Item().Text($"Age: {(patient.Age?.ToString() ?? "-")}   |   Gender: {patient.Gender ?? "-"}   |   Phone: {patient.PhoneNumber}");

                    if (!string.IsNullOrWhiteSpace(patient.Address))
                    {
                        col.Item().Text($"Address: {patient.Address}");
                    }

                    if (!string.IsNullOrWhiteSpace(patient.BasicMedicalNotes))
                    {
                        col.Item().Text($"Basic Medical Notes: {patient.BasicMedicalNotes}")
                            .FontColor(Colors.Red.Darken1);
                    }

                    // جدول السجل الطبي
                    col.Item().PaddingTop(16).Text("Visit History").FontSize(13).SemiBold();

                    if (sessions.Count == 0)
                    {
                        col.Item().PaddingTop(6).Text("No previous visits recorded.")
                            .FontColor(Colors.Grey.Darken1).Italic();
                    }
                    else
                    {
                        col.Item().PaddingTop(6).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(1.2f); // Date
                                columns.RelativeColumn(2f);   // Diagnosis
                                columns.RelativeColumn(2f);   // Treatment
                                columns.RelativeColumn(1.5f); // Medication
                                columns.RelativeColumn(0.9f); // Total
                                columns.RelativeColumn(0.9f); // Paid
                                columns.RelativeColumn(0.9f); // Remaining
                            });

                            table.Header(header =>
                            {
                                header.Cell().Element(HeaderCell).Text("Date");
                                header.Cell().Element(HeaderCell).Text("Diagnosis");
                                header.Cell().Element(HeaderCell).Text("Treatment");
                                header.Cell().Element(HeaderCell).Text("Medication");
                                header.Cell().Element(HeaderCell).Text("Total");
                                header.Cell().Element(HeaderCell).Text("Paid");
                                header.Cell().Element(HeaderCell).Text("Remaining");

                                static QuestPDF.Infrastructure.IContainer HeaderCell(QuestPDF.Infrastructure.IContainer c) =>
                                    c.DefaultTextStyle(x => x.SemiBold()).PaddingVertical(6)
                                     .BorderBottom(1).BorderColor(Colors.Grey.Darken1);
                            });

                            foreach (var s in sessions.OrderByDescending(x => x.SessionDateTime))
                            {
                                table.Cell().Element(BodyCell).Text(s.SessionDateTime.ToString("yyyy-MM-dd"));
                                table.Cell().Element(BodyCell).Text(s.Diagnosis ?? "-");
                                table.Cell().Element(BodyCell).Text(s.TreatmentPerformed ?? "-");
                                table.Cell().Element(BodyCell).Text(s.Medication ?? "-");
                                table.Cell().Element(BodyCell).Text(s.TotalPrice.ToString("N2"));
                                table.Cell().Element(BodyCell).Text(s.PaidAmount.ToString("N2"));
                                table.Cell().Element(BodyCell).Text(s.RemainingAmount.ToString("N2"));

                                static QuestPDF.Infrastructure.IContainer BodyCell(QuestPDF.Infrastructure.IContainer c) =>
                                    c.PaddingVertical(5).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2);
                            }
                        });
                    }
                });

                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("Page ");
                    x.CurrentPageNumber();
                    x.Span(" of ");
                    x.TotalPages();
                });
            });
        });

        return document.GeneratePdf();
    }
}
