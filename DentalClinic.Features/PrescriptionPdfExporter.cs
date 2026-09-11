using System.Linq;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using DentalClinic.Printing;

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
    private const string ClinicPhone = "042 08 83 52";

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
                            row.Spacing(12);

                            // العمود الأيسر: أنواع الخدمات
                            row.RelativeItem(2).Column(left =>
                            {
                                left.Item().Text("Soins dentaires").FontSize(9).Bold();
                                left.Item().Text("Prothèses : fixe et amovible").FontSize(9).Bold();
                                left.Item().Text("Extractions dentaires").FontSize(9).Bold();
                                left.Item().Text("RVG...").FontSize(9).Bold();
                            });

                            // الوسط: شعار السن - حجم ثابت صريح (Width/Height) حتى لا يتمدد الشعار
                            // خارج مساحته المحجوزة ويتداخل مع عمود اسم الطبيب المجاور
                            row.ConstantItem(56).AlignCenter().AlignMiddle().Element(e =>
                            {
                                var toothLogo = LogoLoader.TryLoadOutlineToothLogoBytes();
                                if (toothLogo != null)
                                {
                                    e.Width(46).Image(toothLogo);
                                }
                            });

                            // العمود الأيمن: اسم الطبيب والتاريخ - وزن أكبر (3) ليتّسع لاسم الطبيب
                            // كاملاً على سطر واحد دون أن يضغط على عمود الشعار المجاور
                            // العمود الأيمن: اسم الطبيب والتاريخ - نفس وزن العمود الأيسر (2) عمداً،
                            // ليبقى الشعار في مركز الصفحة الحقيقي؛ نضبط عدم تداخل النص مع الشعار
                            // بتصغير الخط قليلاً بدل توسيع هذا العمود (كان الحل السابق يُزيح الشعار
                            // عن المركز)
                            row.RelativeItem(2).AlignRight().Column(right =>
                            {
                                right.Item().AlignRight().Text(DoctorName).FontSize(10).Bold();
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
                        // تباعد محسوب: كلما قلّ عدد الأدوية زادت المسافة بينها، لملء الفراغ الفارغ
                        // بشكل معقول بدون أي مخاطرة - جرّبنا سابقاً ExtendVertical (مكافئ flex-grow
                        // حقيقي في QuestPDF) لكنها سبّبت انقسام كل دواء لصفحة منفصلة، فتراجعنا عنها
                        // لصالح هذا الحل الحتمي المضمون دائماً بصفحة واحدة.
                        var extraSpacing = lines.Count switch
                        {
                            <= 2 => 108f,
                            3 => 75f,
                            4 => 46f,
                            5 => 22f,
                            6 => 14f,
                            _ => 6f
                        };

                        col.Item().PaddingTop(20).Column(rx =>
                        {
                            foreach (var line in lines)
                            {
                                rx.Item().PaddingBottom(extraSpacing).Column(lineCol =>
                                {
                                    // السطر الرئيسي: اسم الدواء (عريض) + عدد العلب (نفس حجم الخط، محاذى لليمين)
                                    // - نستخدم نفس نسب الأعمدة الأصلية (3 / 4 / 2) مع ترك مساحة الجرعة (4)
                                    // فارغة هنا حتى يبقى عمود عدد العلب في نفس موضعه الأصلي بالضبط
                                    lineCol.Item().Row(lineRow =>
                                    {
                                        lineRow.Spacing(10);

                                        lineRow.RelativeItem(7).ScaleToFit().Text(line.MedicationName).SemiBold().FontSize(12);

                                        if (!string.IsNullOrWhiteSpace(line.BoxCount))
                                        {
                                            lineRow.RelativeItem(2).AlignRight().Text(line.BoxCount).FontSize(12).FontColor(Colors.Black);
                                        }
                                    });

                                    // سطر الجرعة: ينزل تحت السطر الرئيسي مباشرة، لكن يبقى بنفس المحاذاة
                                    // الأفقية التي كانت عليها سابقاً (تحت عمود الجرعة القديم تقريباً) عبر
                                    // ترك نفس عرض عمود اسم الدواء (3) فارغاً قبله كمسافة بادئة
                                    if (!string.IsNullOrWhiteSpace(line.Dosage))
                                    {
                                        lineCol.Item().Row(dosageRow =>
                                        {
                                            dosageRow.Spacing(10);

                                            dosageRow.RelativeItem(2);
                                            dosageRow.RelativeItem(7).Text(line.Dosage).FontSize(10.5f).FontColor(Colors.Black);
                                        });
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

    /// <summary>
    /// يولّد الوصفة ويطبعها مباشرة بصمت تام (بدون حفظ ملف مرئي، بدون فتح أي تطبيق، بدون أي وميض)
    /// عبر SilentPdfPrinter. مرّر printerName إن أردت الطباعة على طابعة محددة غير الافتراضية.
    ///
    /// إن كانت الوصفة تحتوي على أدوية وشهادة/عطلة معاً، يُنشأ ملفا PDF منفصلان (بنفس تخطيط
    /// الصفحة بالضبط - نفس الترويسة وعنوان "ORDONNANCE" - بلا أي تغيير) بدل دمجهما في ملف واحد:
    /// ملف للأدوية وملف مستقل للشهادة/العطلة، ويُطبع كل منهما على حدة.
    /// </summary>
    public static void GenerateAndPrint(string patientName, DateTime date,
        List<PrescriptionLineViewModel> lines, string? notes, int? patientAge = null,
        string? printerName = null)
    {
        var medicationLines = lines.Where(l => !l.IsCertificate).ToList();
        var certificateLines = lines.Where(l => l.IsCertificate).ToList();

        if (medicationLines.Count > 0)
        {
            var medicationPdf = Generate(patientName, date, medicationLines, notes, patientAge);
            SilentPdfPrinter.Print(medicationPdf, printerName);
        }

        if (certificateLines.Count > 0)
        {
            // الملاحظات (Remarques) تبقى خاصة بملف الأدوية فقط، ولا تُكرَّر في ملف الشهادة/العطلة
            var certificatePdf = Generate(patientName, date, certificateLines, null, patientAge);
            SilentPdfPrinter.Print(certificatePdf, printerName);
        }
    }
}