using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace DentalClinic.Features;

// يولّد وصفة طبية بصيغة PDF على شكل رأسية العيادة الورقية الفعلية (بالفرنسية دائماً، بغض النظر
// عن لغة واجهة البرنامج المختارة - هذا مقصود: الوصفات الطبية في هذه العيادة تُطبع بالفرنسية دوماً)
public static class PrescriptionPdfExporter
{
    // ===== بيانات رأسية العيادة - عدّلها هنا إن تغيّر أي منها مستقبلاً =====
    private const string ClinicName = "CABINET DE CHIRURGIE DENTAIRE";
    private const string DoctorName = "Dr. DJELLOULI Zeyd AES";
    private const string DoctorTitle = "Chirurgien - dentiste";
    private const string ClinicAddress = "Haï Safsaf à côté du lycée Commandant Ferradje  Debdaba - Béchar";
    private const string ClinicMobile = "06 99 37 49 84";
    private const string ClinicPhone = "049 22 44 03";

    static PrescriptionPdfExporter()
    {
        // نفس الترخيص المجتمعي المستخدم في مصدّر ملف المريض - الإعداد آمن للتكرار بين الملفين
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static byte[] Generate(string patientName, DateTime date, List<PrescriptionLineViewModel> lines, string? notes, int? patientAge = null)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A5);
                page.Margin(0);
                page.DefaultTextStyle(x => x.FontFamily("Times New Roman").FontSize(11));

                page.Content().Padding(0).Row(pageRow =>
                {
                    // الشريط الرأسي الرفيع على الحافة اليسرى - نفس لمسة الرأسية الورقية
                    pageRow.ConstantItem(3).Background(Colors.Black);

                    pageRow.RelativeItem().Padding(22).Column(col =>
                    {
                        // ===================== الرأسية =====================
                        col.Item().AlignCenter().PaddingBottom(10)
                            .Text(ClinicName).FontSize(17).Bold();

                        col.Item().Row(row =>
                        {
                            // العمود الأيسر: أنواع الخدمات
                            row.RelativeItem(2).Column(left =>
                            {
                                left.Item().Text("Soins dentaires").FontSize(9).Bold();
                                left.Item().Text("Prothèses : fixe et amovible").FontSize(9).Bold();
                                left.Item().Text("Extractions dentaires").FontSize(9).Bold();
                                left.Item().Text("RVG...").FontSize(9).Bold();
                            });

                            // الوسط: شعار السن
                            row.ConstantItem(50).AlignCenter().AlignMiddle().Element(e =>
                            {
                                var toothLogo = LogoLoader.TryLoadOutlineToothLogoBytes();
                                if (toothLogo != null)
                                {
                                    e.Image(toothLogo);
                                }
                            });

                            // العمود الأيمن: اسم الطبيب والتاريخ
                            row.RelativeItem(2).AlignRight().Column(right =>
                            {
                                right.Item().AlignRight().Text(DoctorName).FontSize(11).Bold();
                                right.Item().AlignRight().Text(DoctorTitle).FontSize(9).Bold();
                                right.Item().AlignRight().PaddingTop(6).Text($"Date : {date:dd/MM/yyyy}").FontSize(9);
                            });
                        });

                        // سطر الاسم / اللقب / السن
                        col.Item().PaddingTop(14).Row(row =>
                        {
                            row.RelativeItem(2).Text(t =>
                            {
                                t.Span("Nom et Prénom : ").FontSize(10.5f).Bold();
                                t.Span(patientName).FontSize(10.5f);
                            });

                            row.RelativeItem(1).AlignRight().Text(t =>
                            {
                                t.Span("Âge : ").FontSize(10.5f).Bold();
                                t.Span(patientAge.HasValue ? $"{patientAge} ans" : "…… ans").FontSize(10.5f);
                            });
                        });

                        col.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Black);

                        // ===================== عنوان الوصفة =====================
                        col.Item().PaddingTop(18).AlignCenter().Text("ORDONNANCE").FontSize(22).Bold();

                        // ===================== بنود الوصفة الطبية =====================
                        col.Item().PaddingTop(24).Column(rx =>
                        {
                            rx.Spacing(4);

                            foreach (var line in lines)
                            {
                                rx.Item().PaddingBottom(10).Column(lineCol =>
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
                                rx.Item().PaddingTop(10).Text("Remarques").FontSize(10.5f).SemiBold();
                                rx.Item().Text(notes).FontSize(10);
                            }
                        });   
                    });
                });

                // ===================== تذييل: عنوان وأرقام هاتف العيادة =====================
                page.Footer().Padding(22).PaddingTop(0).Column(footer =>
                {
                    footer.Item().LineHorizontal(0.75f).LineColor(Colors.Grey.Lighten1);
                    footer.Item().PaddingTop(6).Row(row =>
                    {
                        row.RelativeItem(3).Text($"📍 {ClinicAddress}").FontSize(8.5f);
                        row.RelativeItem(1).AlignRight().Column(contact =>
                        {
                            contact.Item().AlignRight().Text($"Mobile : {ClinicMobile}").FontSize(8.5f).Bold();
                            contact.Item().AlignRight().Text($"Fixe : {ClinicPhone}").FontSize(8.5f).Bold();
                        });
                    });
                });
            });
        });

        return document.GeneratePdf();
    }
}
