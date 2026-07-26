# 🦷 DentalClinicSystem.Wpf

> نظام إلكتروني متكامل وإدارة عيادات الأسنان المتقدمة باِستخدام **WPF** و **.NET 10.0** مع دعم فصل الأدوار والمهام بين الأطباء وقسم الاستقبال (الممرضة).

---

## 📌 عن المشروع (Project Overview)

**DentalClinicSystem.Wpf** هو تطبيق سطح مكتب (Desktop Application) مُصمم خصيصاً لإدارة العيادات السنية بكفاءة عالية. يتكون النظام من تطبيقين مستقلين للمستخدمين مع الاعتماد على قاعدة بيانات **SQL Server** مركزية وطبقة بيانات مشفّرة ومحمية.

* **تطبيق الطبيب (DoctorApp):** يركز على الكشف الطبي، الملفات المرضية، إعداد الوصفات الطبية (Prescriptions)، اللوحة المالية، وحجز المواعيد.
* **تطبيق الممرضة / الاستقبال (NurseApp):** يركز على الاستقبال، إدارة قائمة الانتظار اليومية، تسجيل المرضى الجدد، وتحصيل الدفعات المالية.

---

## 🏗️ هيكلية الحل (Solution Architecture)

الحل مقسّم إلى **4 مشاريع** (Projects) لضمان فصل المسؤوليات ورسالة الكود Clean Architecture:

```text
DentalClinicSystem.Wpf.sln
│
├── 📁 DentalClinic.Data          → مكتبة الكيانات وقيادة البيانات (Class Library)
│   ├── DataAccess/               → Repositories صريحة باِستخدام ADO.NET
│   ├── Models/                   → الكيانات النظيفة (DTOs & ViewModels)
│   └── Helpers/                  → تشفير كلمة المرور بـ BCrypt
│
├── 🎨 DentalClinic.UI            → مكتبة الموارد المرئية والعناصر المشتركة
│   ├── Controls/                 → OdontogramControl (مخطط الأسنان التفاعلي)
│   └── Theme.xaml                → الألوان والأنماط الموحدة
│
├── 👩‍⚕️ DentalClinic.NurseApp      → تطبيق الاستقبال والممرضة (WPF WinExe)
│   ├── CollectPaymentWindow      → تحصيل الدفعات وسداد الفواتير
│   ├── AddPatientWindow          → تسجيل مريض جديد
│   └── PatientSearchWindow       → بحث متقدم بالمرضى
│
└── 👨‍⚕️ DentalClinic.DoctorApp     → تطبيق الطبيب والإدارة (WPF WinExe)
    ├── PatientFileWindow         → الملف الطبي الشامل وتوثيق الجلسات
    ├── PrescriptionWindow        → تحرير وطباعة الوصفات الطبية PDF
    ├── FinancialDashboardWindow  → لوحة الإحصائيات المباشرة والمداخيل
    └── UserManagementWindow     → إدارة المستخدمين والصلاحيات