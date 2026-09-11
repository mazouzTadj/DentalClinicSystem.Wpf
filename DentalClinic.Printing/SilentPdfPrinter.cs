using System.IO;
using System.Drawing.Printing;
using PdfiumViewer;

namespace DentalClinic.Printing;

// طباعة PDF مباشرة بدون فتح أي نافذة/تطبيق خارجي (لا Process.Start ولا أي وميض على الشاشة).
// تعتمد على PdfiumViewer الذي يُحمّل صفحات الـ PDF داخلياً ويطبعها عبر PrintDocument القياسي
// في .NET - نفس آلية الطباعة التي تستخدمها أي تطبيق WinForms/WPF تقليدي مباشرة على سائق الطابعة.
//
// هذا الملف مقصود وجوده في مشروع منفصل تماماً (DentalClinic.Printing) وليس داخل
// DentalClinic.Features مباشرة: PdfiumViewer تحتاج UseWindowsForms=true لتُبنى، وتفعيل هذا
// الخيار على مشروع WPF كامل (Features) يُدخل مساحة أسماء System.Windows.Forms كـ "global usings"
// على كل الملفات، فيتصادم أي استخدام غير مؤهّل بالكامل لأنواع مشتركة مثل KeyEventArgs مع
// System.Windows.Input.KeyEventArgs في أي نافذة WPF أخرى بالمشروع. عزل WinForms في مشروع
// مستقل صغير (بدون UseWPF) يحل هذا نهائياً: Features يستدعي هذا المشروع كـ "صندوق أسود"
// عبر Print فقط، دون أن يتسرّب أي نوع من WinForms إلى ملفات WPF.
public static class SilentPdfPrinter
{
    /// <summary>
    /// يطبع بايتات PDF مباشرة على الطابعة الافتراضية، أو على طابعة محددة إن مُرر اسمها.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// إذا كانت الطابعة المطلوبة (أو الافتراضية) غير موجودة/غير صالحة.
    /// </exception>
    public static void Print(byte[] pdfBytes, string? printerName = null)
    {
        using var stream = new MemoryStream(pdfBytes);
        using var document = PdfDocument.Load(stream);

        // CutMargin: يطبع الصفحة بحجمها الكامل دون هوامش إضافية يفرضها سائق الطابعة -
        // مناسب هنا لأن تصميم الوصفة (PrescriptionPdfExporter) مضبوط أصلاً بمقاس A5 بدون هامش صفحة
        using var printDocument = document.CreatePrintDocument(PdfPrintMode.CutMargin);

        if (!string.IsNullOrWhiteSpace(printerName))
        {
            printDocument.PrinterSettings.PrinterName = printerName;
        }

        if (!printDocument.PrinterSettings.IsValid)
        {
            var name = string.IsNullOrWhiteSpace(printerName)
                ? printDocument.PrinterSettings.PrinterName
                : printerName;
            throw new InvalidOperationException($"الطابعة \"{name}\" غير متاحة أو غير مُعرّفة على هذا الجهاز.");
        }

        // PdfiumViewer لا يضبط حجم ورق مهمة الطباعة تلقائياً على حجم صفحة الـ PDF نفسه (A5 هنا) -
        // يعتمد بدلاً من ذلك على حجم الورق الافتراضي المُعرَّف لدى سائق الطابعة، وغالباً ما يكون A4.
        // النتيجة: الوصفة (المصمَّمة أصلاً بمقاس A5 كامل بدون هامش) تُطبع بحجمها الحقيقي داخل مهمة
        // طباعة أكبر مقاسها A4، فتظهر صغيرة نسبياً عند طباعتها فعلياً على ورق A5. الحل: نبحث عن
        // مقاس A5 ضمن مقاسات الورق التي يدعمها السائق ونفرضه صراحةً على المهمة قبل الطباعة.
        ApplyA5PaperSize(printDocument);

        printDocument.Print();
    }

    /// <summary>
    /// يفرض على مهمة الطباعة استخدام مقاس ورق A5 (148×210 مم) بدل الاعتماد على المقاس الافتراضي
    /// للطابعة. يُفضَّل أولاً مقاس A5 الذي يوفّره سائق الطابعة نفسه (PaperKind.A5) لأنه الأكثر توافقاً
    /// معه، وإن لم يوجد يُنشأ مقاس مخصص بنفس الأبعاد كحل احتياطي.
    /// </summary>
    private static void ApplyA5PaperSize(PrintDocument printDocument)
    {
        PaperSize? a5 = null;

        foreach (PaperSize candidate in printDocument.PrinterSettings.PaperSizes)
        {
            if (candidate.Kind == PaperKind.A5)
            {
                a5 = candidate;
                break;
            }
        }

        // احتياطي: بعض السائقين لا يُدرجون A5 ضمن PaperKind الجاهزة - ننشئ مقاساً مخصصاً بنفس أبعاد
        // A5 الحقيقية (148×210 مم ≈ 583×827 من مئة الإنش، وهي وحدة PaperSize في .NET)
        a5 ??= new PaperSize("A5", 583, 827);

        printDocument.DefaultPageSettings.PaperSize = a5;
        printDocument.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
    }

    /// <summary>
    /// أسماء الطابعات المثبّتة على الجهاز - مفيدة إن أردت لاحقاً إضافة قائمة اختيار طابعة للمستخدم
    /// بدل الاعتماد دوماً على الطابعة الافتراضية.
    /// </summary>
    public static List<string> GetInstalledPrinterNames()
    {
        var names = new List<string>();
        foreach (string name in PrinterSettings.InstalledPrinters)
        {
            names.Add(name);
        }
        return names;
    }
}