using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DentalClinic.UI;

// خاصية مُرفَقة (Attached Property) اختيارية بالكامل: تُفعَّل حركة دخول ناعمة (Fade + Slide-Up)
// لأي نافذة بإضافة سطر واحد فقط في XAML، بدون أي تعديل على منطق النافذة نفسها أو الكود الخلفي:
//
//     <Window ... ui:WindowFx.AnimateEntrance="True">
//
// لا تُطبَّق تلقائياً على أي نافذة قائمة حالياً في المشروع - يجب إضافتها يدوياً حيثما تريد.
public static class WindowFx
{
    public static readonly DependencyProperty AnimateEntranceProperty =
        DependencyProperty.RegisterAttached(
            "AnimateEntrance",
            typeof(bool),
            typeof(WindowFx),
            new PropertyMetadata(false, OnAnimateEntranceChanged));

    public static bool GetAnimateEntrance(DependencyObject obj) => (bool)obj.GetValue(AnimateEntranceProperty);
    public static void SetAnimateEntrance(DependencyObject obj, bool value) => obj.SetValue(AnimateEntranceProperty, value);

    private static void OnAnimateEntranceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Window window) return;
        if (e.NewValue is not bool enable || !enable) return;

        window.Loaded += (_, _) => PlayEntranceAnimation(window);
    }

    private static void PlayEntranceAnimation(Window window)
    {
        // نحتاج RenderTransform لتحريك الإزاحة الرأسية - نضيفه فقط إن لم يكن معرَّفاً أصلاً،
        // حتى لا نصطدم مع أي RenderTransform آخر تستخدمه النافذة (نادر لكن ممكن)
        if (window.RenderTransform is not TranslateTransform)
        {
            window.RenderTransform = new TranslateTransform();
        }

        window.Opacity = 0;

        var fadeIn = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        var slideUp = new DoubleAnimation
        {
            From = 14,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(260),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        window.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        window.RenderTransform.BeginAnimation(TranslateTransform.YProperty, slideUp);
    }
}
