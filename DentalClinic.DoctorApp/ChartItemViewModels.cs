namespace DentalClinic.DoctorApp;

// عمود عمودي في الرسم البياني البسيط (عدد المرضى يومياً)
public class BarChartItem
{
    public string Label { get; set; } = string.Empty;
    public string ValueText { get; set; } = string.Empty;
    public double BarHeight { get; set; }
}

// شريط أفقي في قائمة "أكثر الخدمات طلباً"
public class HorizontalBarItem
{
    public string Label { get; set; } = string.Empty;
    public string ValueText { get; set; } = string.Empty;
    public double BarWidth { get; set; }
}
