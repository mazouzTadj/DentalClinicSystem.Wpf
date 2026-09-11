using System.Text;

namespace DentalClinic.UI.Localization;

// ==========================================================================================
// المشكلة:
// عند عرض نص عربي (اتجاه RTL) يحتوي على رموز "محايدة الاتجاه" مثل الأقواس () [] {} أو علامتي
// التنصيص "" أو النقطتين : أو الشرطة المائلة / ، فإن محرك الرسم في WPF/Windows لا يقوم دائماً
// بعكس (mirror) هذه الرموز بصرياً بالشكل الصحيح المتوقَّع من قارئ عربي، رغم أن النص المخزَّن في
// الكود صحيح ومتوازن منطقياً (كل "(" له ")" مقابلة). النتيجة: قوس يظهر في مكان أو باتجاه غير
// متوقَّع، خصوصاً داخل عناصر مثل CheckBox / ComboBoxItem / ItemsControl.
//
// الحل القياسي والمضمون (موصى به من معيار يونيكود نفسه - Unicode Bidirectional Algorithm،
// UAX #9): إحاطة كل رمز محايد بعلامة RLM (Right-to-Left Mark, U+200F) قبله وبعده مباشرة.
// RLM حرف "غير مرئي" تماماً (لا يُطبع أي شكل له) وظيفته الوحيدة إخبار محرك العرض: "عامل الرمز
// المجاور كأنه محاط بنص عربي RTL قوي"، فيجبره على اتخاذ نفس اتجاه/انعكاس النص العربي المحيط به
// بشكل ثابت 100% بغض النظر عن نوع الـ Control أو الخط أو إصدار .NET المستخدَم.
//
// طريقة الاستخدام:
//   1) يُطبَّق تلقائياً على كل النصوص القادمة من Strings.ar.xaml (راجع LocalizationManager) -
//      أي أن كل نص تعرضه عبر {DynamicResource} أو {StaticResource} أو LocalizationManager.T()
//      يُصحَّح تلقائياً بدون أي تعديل إضافي في كل نافذة.
//   2) لأي نص "ديناميكي" لا يأتي من ملفات الترجمة (نص من قاعدة البيانات، ملاحظات المريض، اسم
//      مُركَّب في الكود، إلخ) نادِ BidiTextFixer.Fix(text) يدوياً قبل عرضه، أو استخدم
//      ArabicBidiConverter في الـ XAML عبر Binding مباشرة.
// ==========================================================================================
public static class BidiTextFixer
{
    private const char Rlm = '\u200f';

    // ⚠️ Patch 12: عمداً بلا "{" و"}" هنا (بعكس بقية الرموز المحايدة أدناه) - التطبيق يستخدم
    // "{0}"/"{1}" حصراً كمواضع تنسيق لـstring.Format عبر LocalizationManager.T(key, args) في
    // كل نصوص *Format (مثل ProsthStats_PeriodFormat)، وليس كأقواس معروضة فعلياً كنص. إحاطة "{"
    // و"0" و"}" كل على حدة بعلامات RLM غير مرئية تكسر التطابق الحرفي الذي يعتمده string.Format
    // (يتوقّع "{0}" متتالية بلا أي حرف بينها)، فيفشل التنسيق بصمت (catch في T()) ويظهر النص الخام
    // "{0}" على الشاشة بدل القيمة الفعلية. لا يوجد أي استخدام لـ"{"/"}" كنص عادي معروض في كل
    // ملفات الترجمة الحالية (تحقّقنا: كل ظهور لهما هو "{N}" أو "{N:صيغة}" فقط).
    private const string NeutralSymbols = "()[]«»\"'/:%+*=<>-";

    /// <summary>
    /// يعيد نسخة من النص مع إحاطة كل رمز محايد بعلامات RLM غير المرئية لضمان عرضه بصرياً
    /// بالشكل الصحيح داخل سياق عربي RTL، أياً كان العنصر البصري الذي يعرضه.
    /// آمن الاستدعاء المتكرر (Idempotent إلى حد كبير) ولا يغيّر النصوص الإنجليزية/الفارغة.
    /// </summary>
    public static string Fix(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        var sb = new StringBuilder(text.Length + 8);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (NeutralSymbols.IndexOf(c) >= 0)
            {
                // نتجنّب إضافة RLM مكرَّرة إن كانت موجودة أصلاً حول نفس الرمز (نص عولج سابقاً)
                bool hasRlmBefore = sb.Length > 0 && sb[sb.Length - 1] == Rlm;
                bool hasRlmAfter = i + 1 < text.Length && text[i + 1] == Rlm;

                if (!hasRlmBefore) sb.Append(Rlm);
                sb.Append(c);
                if (!hasRlmAfter) sb.Append(Rlm);
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
