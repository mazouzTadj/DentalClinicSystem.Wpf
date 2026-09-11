using System;
using System.Threading;
using Microsoft.Data.SqlClient;
using DentalClinic.Data.DataAccess;

namespace DentalClinic.UI.Connectivity;

// يراقب الاتصال بقاعدة البيانات دورياً في الخلفية (System.Threading.Timer يعمل على ThreadPool،
// وليس DispatcherTimer الذي يعمل على UI Thread) - هذا هو الفارق الجوهري الذي يحل مشكلة تجمُّد
// التطبيق بالكامل عند انقطاع الشبكة: الفحص نفسه لا يُجمِّد أي شيء أبداً مهما طالت مدته، لأنه لا
// يعمل إطلاقاً على الخيط الذي يرسم الواجهة ويستقبل نقرات الفأرة.
//
// كل تطبيق (DoctorApp/NurseApp/ProsthetistApp) يُنشئ نسخة واحدة منه بعد إنشاء DatabaseHelper
// مباشرة، ويشترك في حدث StatusChanged لتحديث أيقونة الحالة في الشريط العلوي وإظهار/إخفاء
// ConnectionLostWindow تلقائياً - راجع MainWindow في كل تطبيق.
public sealed class ConnectionMonitor : IDisposable
{
    // فحص سريع كل 5 ثوانٍ بمهلة اتصال قصيرة (3 ثوانٍ) - أسرع بكثير من المهلة الافتراضية لباقي
    // استعلامات التطبيق (راجع DatabaseHelper.ConnectTimeoutSeconds)، لأن هذا الفحص هو ما يُحدِّد
    // الحالة المعروضة للمستخدم، فيجب أن يكون سريع الاكتشاف لكلا الاتجاهين (الانقطاع والعودة).
    private const int CheckIntervalSeconds = 5;
    private const int CheckTimeoutSeconds = 3;

    private readonly DatabaseHelper _db;
    private readonly SynchronizationContext? _uiContext;
    private readonly Timer _timer;

    // Patch 16 - Fix #1: كان هذا حقلاً "volatile bool" غير كافٍ لضمان فحص-ثم-ضبط ذرّي واحد
    // (Check-and-Set): سباق نادر بين خيطين (Timer Tick + CheckNow اليدوي في نفس اللحظة تقريباً)
    // كان بإمكانه نظرياً أن يسمح لفحصين بالمرور معاً رغم القراءة الأولية لـ_checking. استُبدل
    // بحقل int + Interlocked.CompareExchange: عملية ذرّية واحدة تضمن أن فحصاً واحداً فقط ينجح في
    // الدخول في أي لحظة، بصرف النظر عن عدد الخيوط التي تحاول بدء فحص في نفس الوقت بالضبط.
    // 0 = لا يوجد فحص نشط، 1 = فحص نشط حالياً.
    private int _checking;
    private volatile bool _disposed;

    // آخر حالة معروفة - تبدأ true (متفائل) حتى ينتهي أول فحص فعلي، فلا تظهر أيقونة "منقطع" لحظة
    // فتح التطبيق قبل إتاحة الفرصة للفحص الأول لإثبات العكس
    public bool IsConnected { get; private set; } = true;

    // يُستدعى في UI Thread دائماً (عبر SynchronizationContext المُلتقَط وقت الإنشاء) - آمن التعامل
    // معه مباشرة من معالجات الحدث لتحديث عناصر واجهة WPF بلا أي Dispatcher.Invoke إضافي من المستدعي
    public event Action<bool>? StatusChanged;

    // يجب إنشاؤه من UI Thread (كما هو الحال دائماً عند إنشائه داخل constructor نافذة WPF) حتى
    // يُلتقَط SynchronizationContext الصحيح لتوصيل الحدث لاحقاً بأمان
    public ConnectionMonitor(DatabaseHelper db)
    {
        _db = db;
        _uiContext = SynchronizationContext.Current;
        _timer = new Timer(_ => CheckOnce(), null, TimeSpan.Zero, TimeSpan.FromSeconds(CheckIntervalSeconds));
    }

    private void CheckOnce()
    {
        // تفادي تراكم فحوصات متزامنة إن تأخر أحدها لأي سبب (شبكة بطيئة جداً مثلاً) - فحص واحد
        // نشط كحد أقصى في كل لحظة. Interlocked.CompareExchange ذرّي بالكامل: إن كانت القيمة
        // الحالية 0 يضبطها إلى 1 ويُعيد القيمة القديمة (0) فنُكمل؛ إن كانت أصلاً 1 (فحص آخر يعمل)
        // يُعيد 1 فنخرج فوراً دون أي عمل إضافي.
        if (_disposed) return;
        if (Interlocked.CompareExchange(ref _checking, 1, 0) != 0) return;

        try
        {
            var connected = _db.TestConnection(out _, CheckTimeoutSeconds);
            if (connected != IsConnected)
            {
                IsConnected = connected;
                Publish(connected);
            }
        }
        catch
        {
            // احتياط إضافي: أي استثناء غير متوقع من الفحص نفسه يُعامَل كانقطاع اتصال، ولا يُسمح له
            // بإسقاط الـTimer Thread أو رمي استثناء غير مُعالَج يُنهي التطبيق
            if (IsConnected)
            {
                IsConnected = false;
                Publish(false);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _checking, 0);
        }
    }

    private void Publish(bool connected)
    {
        if (_uiContext != null)
        {
            _uiContext.Post(_ => StatusChanged?.Invoke(connected), null);
        }
        else
        {
            StatusChanged?.Invoke(connected);
        }
    }

    // فحص فوري يدوي (مثلاً عند ضغط المستخدم على أيقونة الحالة لإعادة المحاولة الآن بدل انتظار
    // الدورة التالية) - لا يُعيد ضبط المؤقّت، فقط يُشغِّل فحصاً إضافياً في الخلفية بنفس الآلية الآمنة
    // (نفس حارس _checking أعلاه يمنع تشغيل فحص إضافي فوق فحص دوري يعمل بالفعل في تلك اللحظة)
    public void CheckNow()
    {
        if (_disposed) return;
        ThreadPool.QueueUserWorkItem(_ => CheckOnce());
    }

    // Patch 16 - Fix #4: يسمح لأي عملية قاعدة بيانات حقيقية (استعلام/أمر عادي نُفِّذ من أي
    // Repository في أي مكان بالمشروع) بإبلاغ ConnectionMonitor فوراً عند فشلها بسبب انقطاع اتصال
    // فعلي، بدل انتظار دورة الفحص الدوري التالية (قد تصل حتى 5 ثوانٍ). يُستخدَم من كتل catch في
    // عمليات التحديث التلقائي/الدورية في كل MainWindow (راجع Fix #3). لا يُشغِّل فحصاً جديداً ولا
    // يُنشئ أي اتصال إضافي بنفسه ابتداءً - فقط يُصنِّف الاستثناء المُعطى ويُحدِّث الحالة المنشورة
    // إن كان فعلاً عطلاً على مستوى الاتصال/الشبكة (وليس خطأ عمل/صلاحية/قيد/بناء جملة).
    public void ReportConnectionFailure(Exception? ex)
    {
        if (_disposed || ex == null) return;
        if (!IsConnectionRelated(ex)) return;

        if (IsConnected)
        {
            IsConnected = false;
            Publish(false);
        }

        // فحص فوري إضافي بمعزل عن الدورة الحالية: يُسرِّع اكتشاف عودة الاتصال أيضاً، لأنه يُعيد
        // تشغيل نفس آلية CheckOnce الآمنة (نفس حارس _checking) بدل انتظار الـTick القادم فقط
        CheckNow();
    }

    // يُصنِّف الاستثناء: هل هو فعلاً عطل على مستوى الاتصال/الشبكة/النقل (يستحق إظهار "منقطع")،
    // أم خطأ عمل عادي (بناء جملة، قيد فريد مكرر، انتهاك قيد، صلاحية) لا علاقة له بحالة الاتصال
    // إطلاقاً ويجب أن يستمر بمعالجته الحالية (رسالة خطأ للمستخدم) بلا أي تغيير لحالة الاتصال.
    private static bool IsConnectionRelated(Exception ex)
    {
        switch (ex)
        {
            case SqlException sqlEx:
                foreach (SqlError error in sqlEx.Errors)
                {
                    if (IsConnectionRelatedSqlErrorNumber(error.Number)) return true;
                }
                // فئة الخطورة (Class) >= 20 تعني عطلاً فادحاً يُنهي الاتصال نفسه من جهة SQL Server -
                // هذا دائماً عطل اتصال بحكم التعريف، بصرف النظر عن رقم الخطأ تحديداً
                return sqlEx.Class >= 20;

            // فشل فتح الاتصال نفسه أحياناً يظهر ملفوفاً بخطأ Win32 (DNS/Socket) بدل SqlException مباشرة
            case System.ComponentModel.Win32Exception:
                return true;

            case TimeoutException:
                return true;

            default:
                return false;
        }
    }

    // أرقام أخطاء SqlClient/SQL Server المعروفة كأعطال اتصال/شبكة/نقل أو انتهاء مهلة اتصال -
    // عمداً لا تشمل أخطاء بناء الجملة (مثل 102، 207) أو انتهاك القيود (547، 2627) أو الصلاحيات
    // (229، 262) حتى لا تُصنَّف خطأً كـ"انقطاع اتصال"
    private static bool IsConnectionRelatedSqlErrorNumber(int number) => number switch
    {
        -2 => true,     // Timeout expired
        -1 => true,     // Connection broken
        2 => true,      // Timeout / network-related error establishing connection
        53 => true,     // Named pipes / server not found
        64 => true,     // Connection forcibly closed
        233 => true,    // No process is on the other end of the pipe
        258 => true,    // Wait operation timed out
        10053 => true,  // Software caused connection abort
        10054 => true,  // Connection reset by peer
        10060 => true,  // Connection timed out
        11001 => true,  // Host not found
        _ => false
    };

    public void Dispose()
    {
        _disposed = true;
        _timer.Dispose();
    }
}
