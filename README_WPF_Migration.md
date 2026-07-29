# 🦷 DentalClinicSystem.Wpf

> نظام إلكتروني متكامل لإدارة عيادات الأسنان باِستخدام **WPF** و **.NET 10.0** مع دعم فصل الأدوار والمهام بين الأطباء وقسم الاستقبال (الممرضة).

---

## 📌 عن المشروع (Project Overview)

**DentalClinicSystem.Wpf** هو تطبيق سطح مكتب (Desktop Application) مُصمم خصيصاً لإدارة العيادات السنية بكفاءة عالية. يتكون النظام من تطبيقين مستقلين للمستخدمين مع الاعتماد على قاعدة بيانات **SQL Server** مركزية وطبقة بيانات مشفّرة ومحمية.

* **تطبيق الطبيب (DoctorApp):** الكشف الطبي، الملفات المرضية، الوصفات الطبية (Prescriptions)، إدارة العلاجات وأسعارها، حجز المواعيد، اللوحة المالية (دخل / مصاريف / صافي ربح)، إدارة المستخدمين، والنسخ الاحتياطي.
* **تطبيق الممرضة / الاستقبال (NurseApp):** الاستقبال، إدارة قائمة الانتظار اليومية، تسجيل المرضى الجدد، البحث عن المرضى، وتحصيل الدفعات المالية مع مؤشرات حالة الدفع.

---

## 🏗️ هيكلية الحل (Solution Architecture)

الحل مقسّم إلى **4 مشاريع** (Projects) لضمان فصل المسؤوليات:

```text
DentalClinicSystem.Wpf.sln
│
├── 📁 DentalClinic.Data              → مكتبة الوصول للبيانات (Class Library, net10.0)
│   ├── Models/                       → الكيانات + نماذج العرض (DTOs)
│   │   ├── Patient, UserAccount, VisitQueueItem
│   │   ├── MedicalSession, ToothRecord, Payment
│   │   ├── MedicationPreset
│   │   ├── FinancialModels              → RevenueSummary, OutstandingBalanceRow,
│   │   │                                   ExpenseSummary, NetProfitSummary, FinancialChartItem ...
│   │   └── SessionSearchModels          → SessionSearchCriteria / SessionSearchResult
│   ├── DataAccess/                   → Repositories عبر ADO.NET صِرف + DatabaseHelper
│   │   ├── PatientRepository, QueueRepository, SessionRepository
│   │   ├── UserRepository, PaymentRepository
│   │   ├── FinancialRepository          → إيرادات، متأخرات، مصاريف، صافي ربح، مخططات بيانية
│   │   ├── MedicationPresetRepository
│   │   └── BackupRepository             → نسخ احتياطي عبر أوامر SQL Server الأصلية
│   └── Helpers/PasswordHelper.cs     → تشفير كلمات المرور عبر BCrypt
│
├── 🎨 DentalClinic.UI                → مكتبة موارد WPF مشتركة
│   ├── Theme.xaml                    → الألوان والأنماط الموحدة
│   ├── Controls/OdontogramControl    → مخطط الأسنان التفاعلي (عنصر تحكم مخصص)
│   ├── Converters/StatusToBrushConverter
│   └── Assets/*.png                  → شعارات العيادة
│
├── 👩‍⚕️ DentalClinic.NurseApp          → تطبيق الاستقبال/الممرضة (WPF WinExe)
│   ├── LoginWindow, MainWindow       → قائمة انتظار مباشرة (تحديث كل 4 ثوانٍ) + عمود "الموعد القادم"
│   ├── AddPatientWindow              → تسجيل مريض جديد
│   ├── PatientSearchWindow           → بحث بالاسم/الهاتف + فلتر الجنس + فلتر حالة الدفع
│   └── CollectPaymentWindow          → تحصيل الدفعات (زر علوي أو زر لكل صف/مريض)
│
└── 👨‍⚕️ DentalClinic.DoctorApp         → تطبيق الطبيب والإدارة (WPF WinExe)
    ├── LoginWindow, MainWindow
    ├── PatientFileWindow             → الملف الطبي الشامل + توثيق الجلسات + تصدير PDF
    ├── PrescriptionWindow            → تحرير الوصفة الطبية + تصدير PDF
    ├── TreatmentManagementWindow     → إدارة قائمة العلاجات وأسعارها
    ├── AdvancedSearchWindow          → بحث متقدم متعدد المعايير في الجلسات
    ├── FinancialDashboardWindow      → لوحة مالية (Income / Expenses / Net Profit) + مخططات + تصدير CSV
    ├── AddExpenseWindow              → تسجيل مصروف جديد
    ├── ScheduleAppointmentDialog     → حجز موعد مستقبلي
    ├── UserManagementWindow / UserEditWindow → إدارة حسابات المستخدمين (صلاحية Admin منفصلة)
    └── BackupWindow                  → نسخ احتياطي يدوي (بالإضافة إلى نسخ تلقائي يومي صامت)
```

> ملاحظة معمارية: لا يعتمد المشروع حالياً على نمط MVVM كامل — الأسلوب المتّبع هو **Code-Behind** مباشر لكل نافذة، مع كائنات عرض بسيطة (`XxxViewModel.cs`, تطبّق `INotifyPropertyChanged`) موجودة بجانب كل نافذة لدعم ربط بيانات الجداول (Grids)، وليست طبقة MVVM كاملة بأوامر (Commands).

---

## 🗄️ قاعدة البيانات (Database)

اسم القاعدة الافتراضي: `DentalClinicDB` على SQL Server Express محلي. الوصول للبيانات بالكامل عبر **ADO.NET** خام (`SqlConnection`/`SqlCommand`) بدون Entity Framework أو Dapper.

**الجداول الرئيسية:** `Patients`, `Users`, `VisitQueue`, `MedicalSessions`, `ToothRecords`, `Payments`, `MedicationPresets`.

**جداول تُنشئ نفسها تلقائياً عند أول استخدام** (بدون سكربت ترحيل منفصل): `TreatmentPresets`.

> ⚠️ **معروف/Known Issue:** جدول `ClinicExpenses` (تستخدمه ميزة المصاريف في اللوحة المالية) **لا يُنشئ نفسه تلقائياً حالياً** ولا يوجد له سكربت ترحيل. يجب إنشاؤه يدوياً في قاعدة البيانات قبل استخدام تبويب Expenses/Net Profit، وإلا ستفشل العملية بخطأ `Invalid object name 'ClinicExpenses'`. الأعمدة المتوقعة: `ExpenseID (PK), Amount, Description, Category, ExpenseDate`.

---

## ⚙️ الإعداد والتشغيل (Setup)

1. تأكد من تثبيت **SQL Server Express** (أو أي نسخة SQL Server) و **.NET 10.0 SDK**.
2. أنشئ قاعدة بيانات باسم `DentalClinicDB` وجداولها (راجع تحذير `ClinicExpenses` أعلاه).
3. حدّث سطر الاتصال في `App.config` لكل من `DentalClinic.NurseApp` و `DentalClinic.DoctorApp`:

```xml
<connectionStrings>
  <add name="DentalClinicDB"
       connectionString="Data Source=.\SQLEXPRESS;Initial Catalog=DentalClinicDB;
                          Integrated Security=True;Encrypt=False;TrustServerCertificate=True;"
       providerName="Microsoft.Data.SqlClient" />
</connectionStrings>
<appSettings>
  <add key="BackupFolderPath" value="C:\DentalClinicBackups" />
  <add key="BackupRetainDays" value="14" />
</appSettings>
```

4. ابنِ الحل (`dotnet build`) وشغّل `DentalClinic.NurseApp` أو `DentalClinic.DoctorApp` حسب الدور.

---

## 📦 المكتبات المستخدمة (NuGet Packages)

| الحزمة | الاستخدام |
|---|---|
| `Microsoft.Data.SqlClient` | الاتصال بـ SQL Server |
| `BCrypt.Net-Next` | تشفير كلمات المرور |
| `System.Configuration.ConfigurationManager` | قراءة `App.config` |
| `QuestPDF` | توليد ملفات PDF (الملف الطبي، الوصفات الطبية) |

---

## 🔐 الصلاحيات (Roles & Permissions)

* **الدور (Role):** `Doctor` أو `Nurse` — يحدد أي تطبيق يمكن لصاحب الحساب فتحه.
* **صلاحية `IsAdmin`:** مستقلة تماماً عن الدور — تفتح شاشات إدارة المستخدمين، النسخ الاحتياطي، واللوحة المالية داخل DoctorApp بغض النظر عن الدور الأساسي.
* الفصل بين ما يراه NurseApp و DoctorApp هو فصل على مستوى **الواجهة/الكود** فقط، وليس صلاحيات قاعدة بيانات حقيقية (كلا التطبيقين يستخدمان نفس سلسلة الاتصال).

---

## 🚧 ملاحظات معروفة (Known Limitations)

* جدول `ClinicExpenses` بحاجة إنشاء يدوي (راجع القسم أعلاه).
* جدول `TreatmentPresets` يُنشأ بمنطق مكرر في مكانين من الكود (`PatientFileWindow` و `TreatmentManagementWindow`) بدل مكان مركزي واحد.
* بعض استعلامات "المرضى المديونين" (`TotalPrice > PaidAmount` على `MedicalSessions`) مكررة في أكثر من ملف بدل دالة موحدة في `PaymentRepository`.
