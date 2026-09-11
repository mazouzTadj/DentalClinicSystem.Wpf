using System.Windows;

namespace DentalClinic.UI;

// نافذة إشعار صغيرة غير معطِّلة (Non-modal) تظهر أسفل يمين نافذة التطبيق الرئيسية عند انقطاع
// الاتصال، وتختفي تلقائياً عند عودته - لا يوجد فيها زر إغلاق لأن المستخدم لا يحتاج التعامل معها
// إطلاقاً: MainWindow في كل تطبيق هو من يتحكم بظهورها/إخفائها بالكامل استجابة لـ
// ConnectionMonitor.StatusChanged (راجع كل MainWindow.xaml.cs). لا تُغلق التطبيق نفسه ولا تمنع
// استخدامه - Topmost فقط لتبقى مرئية، وShowActivated=false حتى لا تسرق التركيز من الشاشة الحالية.
public partial class ConnectionLostWindow : Window
{
    public ConnectionLostWindow()
    {
        InitializeComponent();
        Loaded += (s, e) => PositionBottomRightOfOwner();
    }

    private void PositionBottomRightOfOwner()
    {
        const double margin = 20;

        if (Owner != null)
        {
            Left = Owner.Left + Owner.ActualWidth - ActualWidth - margin;
            Top = Owner.Top + Owner.ActualHeight - ActualHeight - margin;
        }
        else
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - ActualWidth - margin;
            Top = workArea.Bottom - ActualHeight - margin;
        }
    }
}
