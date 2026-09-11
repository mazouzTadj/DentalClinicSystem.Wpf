using System.Globalization;
using DentalClinic.UI.Localization;

namespace DentalClinic.Features;

// تنسيق موحَّد للمبالغ المالية عبر كل شاشات لوحة الفاينانس - يضيف رمز العملة المترجَم (دج/DZD)
// بعد الرقم، بدل ما تظهر الأرقام مجرّدة بلا أي وحدة عملة واضحة
public static class MoneyFormatter
{
    public static string Format(decimal amount)
        => $"{amount.ToString("N2", CultureInfo.InvariantCulture)} {LocalizationManager.T("Fin_CurrencySuffix")}";
}
