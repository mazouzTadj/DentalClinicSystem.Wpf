using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DentalClinic.UI.Localization;

namespace DentalClinic.UI.Controls;

// نقطة ملوّنة + نص صغير يعكس حالة الاتصال الحالية بالخادم، قابل للنقر لإجبار فحص فوري بدل انتظار
// الدورة التالية (5 ثوانٍ). تُحدَّث حالته من MainWindow في كل تطبيق عبر SetConnected، استجابة
// لحدث ConnectionMonitor.StatusChanged - لا يقوم هذا الـControl بأي فحص بنفسه إطلاقاً.
public partial class ConnectionStatusIndicator : UserControl
{
    private static readonly SolidColorBrush ConnectedBrush = new(Color.FromRgb(0x27, 0xAE, 0x60));
    private static readonly SolidColorBrush DisconnectedBrush = new(Color.FromRgb(0xC0, 0x39, 0x2B));

    // يُطلَق عند نقر المستخدم على المؤشر - المستدعي (MainWindow) يربطه بـConnectionMonitor.CheckNow()
    public event EventHandler? RetryRequested;

    public ConnectionStatusIndicator()
    {
        InitializeComponent();
        SetConnected(true); // حالة ابتدائية متفائلة، تُستبدَل فور أول فحص فعلي من ConnectionMonitor
    }

    public void SetConnected(bool isConnected)
    {
        if (isConnected)
        {
            StatusDot.Fill = ConnectedBrush;
            StatusText.Text = LocalizationManager.T("Conn_Connected");
            ToolTipService.SetToolTip(Root, LocalizationManager.T("Conn_ConnectedTooltip"));
        }
        else
        {
            StatusDot.Fill = DisconnectedBrush;
            StatusText.Text = LocalizationManager.T("Conn_Disconnected");
            ToolTipService.SetToolTip(Root, LocalizationManager.T("Conn_DisconnectedTooltip"));
        }
    }

    private void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
        RetryRequested?.Invoke(this, EventArgs.Empty);
}
