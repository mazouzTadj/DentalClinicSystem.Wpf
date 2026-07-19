# الانتقال إلى WPF - دليل سريع

## هيكل الحل الجديد
- **DentalClinic.Data** — بدون أي تعديل، منسوخة كما هي من نسخة WinForms.
- **DentalClinic.UI** (جديد) — مكتبة موارد WPF مشتركة (Theme.xaml): الألوان، أنماط الأزرار المدوّرة، حاويات حقول الإدخال. كلا التطبيقين يستوردها.
- **DentalClinic.NurseApp** — أصبح الآن تطبيق WPF (أزرق).
- **DentalClinic.DoctorApp** — أصبح الآن تطبيق WPF (أخضر).

## خطوات الإعداد في Visual Studio
1. **احذف** مشروعي WinForms القديمين (`DentalClinic.NurseApp` و`DentalClinic.DoctorApp`) من الحل القديم، أو ابدأ بحل جديد فارغ.
2. انسخ محتوى هذا الأرشيف كاملاً إلى مجلد مشروعك، وافتح `DentalClinicSystem.Wpf.sln`.
3. في `App.config` بكل من `DentalClinic.NurseApp` و`DentalClinic.DoctorApp`، ضع نفس سطر الاتصال (Data Source) الذي كنت تستخدمه سابقاً في نسخة WinForms.
4. نفّذ `dotnet restore` أو ببساطة افتح الحل في Visual Studio ليعيد استرجاع الحزم تلقائياً (`Microsoft.Data.SqlClient`, `BCrypt.Net-Next`, `System.Configuration.ConfigurationManager`).
5. فعّل Multiple Startup Projects (كلا التطبيقين) واضغط F5.

## كيف تعمل شاشة الدخول الآن؟
- لا يوجد `StartupUri` في App.xaml؛ بدلاً من ذلك `App.xaml.cs` يفتح `LoginWindow` يدوياً عبر `ShowDialog()`، ولا يفتح `MainWindow` إلا بعد نجاح الدخول (`ShutdownMode` مضبوط بعناية لمنع إغلاق التطبيق تلقائياً قبل قرارنا).
- شريط العنوان الافتراضي لويندوز مُلغى (`WindowStyle="None"`) لصالح شريط علوي مخصّص بسيط (زر إغلاق فقط) + زوايا دائرية 18px + ظل ناعم (DropShadowEffect)، لتصميم عصري متكامل.
- الفائدة الإضافية: WPF يقيس كل شيء بوحدات مستقلة عن الجهاز (Device-Independent Pixels)، فمشكلة "AutoScaleMode" التي واجهتنا في WinForms **غير موجودة إطلاقاً هنا** - لا حاجة لأي إعداد تحجيم يدوي.

## الخطوة القادمة
لوحتا التحكم (Dashboards) - قائمة الانتظار الحيّة + تسجيل مريض عند الممرضة، وفتح ملف المريض عند الطبيب - بنفس تصميم البطاقات المدوّرة.
