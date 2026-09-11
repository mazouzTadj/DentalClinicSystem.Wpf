using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using DentalClinic.Data.Models;

namespace DentalClinic.Data.DataAccess;

// ينشئ بنية القاعدة الكاملة (13 جدول + FK + Seed لجدول Roles) تلقائياً عند أول اتصال بقاعدة
// بيانات فارغة (موجودة لكن بدون جداول - يُفترض أنها أُنشئت مسبقاً يدوياً بأمر واحد بسيط
// CREATE DATABASE من طرف من يُعِدّ السيرفر، راجع دليل_إعداد_السيرفر.md).
//
// لا يُنشئ القاعدة نفسها (CREATE DATABASE) عمداً: ذلك يتطلب صلاحيات خادم واسعة (dbcreator/sysadmin)
// لا ينبغي منحها لحساب SQL الذي يستخدمه التطبيق يومياً على كل جهاز عميل - فقط صلاحيات db_owner
// أو db_ddladmin داخل القاعدة نفسها كافية لإنشاء الجداول، وهذا نطاق أضيق وأكثر أماناً.
public static class SchemaInitializer
{
    private const string EmbeddedResourceName = "DentalClinic.Data.Schema.DentalClinicSchema.sql";

    // فحص سريع: هل جدول Users موجود؟ (كافٍ كمؤشر - إما كل الجداول موجودة أو لا شيء موجود إطلاقاً،
    // لأن الإنشاء دائماً يتم دفعة واحدة عبر CreateSchema أدناه)
    public static bool SchemaExists(DatabaseHelper db)
    {
        const string sql = "SELECT OBJECT_ID(N'dbo.Users', N'U')";
        var result = db.ExecuteScalar(sql);
        return result != null && result != DBNull.Value;
    }

    public static void CreateSchema(DatabaseHelper db)
    {
        var script = LoadEmbeddedScript();

        // كل دفعة SQL منفصلة بسطر "GO" وحيد (نفس تقسيم SSMS) - SqlCommand لا يفهم GO
        // إطلاقاً (هي فقط أمر خاص بـ sqlcmd/SSMS)، فيجب تقسيم النص وتنفيذ كل دفعة على حدة.
        var batches = Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);

        foreach (var batch in batches)
        {
            var trimmed = batch.Trim();
            if (trimmed.Length == 0) continue;
            db.ExecuteNonQuery(trimmed);
        }

        // ترقيات مخطط آمنة لقواعد البيانات الموجودة مسبقاً.
        EnsureSchemaUpgrades(db);

        // بصمة العيادة الفريدة والثابتة - أساس نظام الترخيص (راجع LicenseValidator)
        LicenseValidator.EnsureInstallationId(db);
    }

    public static void EnsureSchemaUpgrades(DatabaseHelper db)
    {
        EnsurePaymentWriteOffColumn(db);
        EnsureProstheticRoleSeeded(db);
        EnsureProstheticTables(db);
        EnsureProstheticLookupsSeeded(db);
        EnsureObsoleteProstheticPermissionKeysRemoved(db);
        EnsureProstheticPermissionsSeeded(db);
        EnsureWorkTypePriceColumn(db);
        EnsureCertificateColumn(db);
        EnsureMedicalSessionRowVersion(db);
    }

    // ===================== نظام مرمم الأسنان (Dental Prosthetist) =====================
    // كل الدوال أدناه Idempotent بالكامل (قابلة لإعادة التشغيل بلا أخطاء) ولا تحذف أي بيانات
    // أو جداول موجودة - تُنفَّذ في كل تشغيل تطبيق (Doctor أو Nurse) بلا أي أثر جانبي إن كانت
    // البنية موجودة أصلاً. راجع دليل_إعداد_السيرفر لصلاحيات db_owner/db_ddladmin اللازمة لـ ALTER/CREATE.

    // دور "Prosthetist" - يُزرَع فقط إن لم يكن موجودًا. لا يوجد أي افتراض في أي مكان آخر بالكود بأن
    // RoleID الناتج هنا (من IDENTITY) سيكون 3 بالضبط - UserRepository.RoleToId/IdToRole يبحثان عن
    // RoleID الفعلي بالاسم (RoleName = 'Prosthetist') دائمًا، فتبقى العملية صحيحة حتى لو اختلف
    // الرقم الفعلي لأي سبب (قاعدة مُعدَّلة يدويًا، ترتيب Seed مختلف، إلخ).
    private static void EnsureProstheticRoleSeeded(DatabaseHelper db)
    {
        const string sql = @"
IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE RoleName = N'Prosthetist')
BEGIN
    INSERT INTO dbo.Roles (RoleName) VALUES (N'Prosthetist');
END";
        db.ExecuteNonQuery(sql);
    }

    private static void EnsureProstheticTables(DatabaseHelper db)
    {
        // Sequence مستقل لرقم الحالة (CaseNumber) - لا علاقة له بـ PatientID ولا بـ CaseID،
        // يبدأ من 1 ويزداد دائمًا، لا يتكرر ولا يُعاد استخدامه حتى بعد حذف/أرشفة حالة.
        const string sequenceSql = @"
IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = N'ProstheticCaseNumberSeq' AND schema_id = SCHEMA_ID(N'dbo'))
BEGIN
    CREATE SEQUENCE dbo.ProstheticCaseNumberSeq AS INT START WITH 1 INCREMENT BY 1 NO CYCLE;
END";
        db.ExecuteNonQuery(sequenceSql);

        const string workTypesSql = @"
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'dbo.ProstheticWorkTypes') AND type = 'U')
BEGIN
    CREATE TABLE dbo.ProstheticWorkTypes(
        WorkTypeID INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        WorkTypeName NVARCHAR(150) NOT NULL,
        Price DECIMAL(18,2) NOT NULL CONSTRAINT DF_ProstheticWorkTypes_Price DEFAULT (0),
        IsActive BIT NOT NULL CONSTRAINT DF_ProstheticWorkTypes_IsActive DEFAULT (1),
        SortOrder INT NOT NULL CONSTRAINT DF_ProstheticWorkTypes_SortOrder DEFAULT (0)
    );
END";
        db.ExecuteNonQuery(workTypesSql);

        const string stagesSql = @"
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'dbo.ProstheticStages') AND type = 'U')
BEGIN
    CREATE TABLE dbo.ProstheticStages(
        StageID INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        StageName NVARCHAR(150) NOT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_ProstheticStages_IsActive DEFAULT (1),
        SortOrder INT NOT NULL CONSTRAINT DF_ProstheticStages_SortOrder DEFAULT (0)
    );
END";
        db.ExecuteNonQuery(stagesSql);

        const string casesSql = @"
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'dbo.ProstheticCases') AND type = 'U')
BEGIN
    CREATE TABLE dbo.ProstheticCases(
        CaseID INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        CaseNumber INT NOT NULL,
        PatientID INT NOT NULL,
        WorkTypeID INT NULL,
        IncludesUpper BIT NOT NULL CONSTRAINT DF_ProstheticCases_IncludesUpper DEFAULT (0),
        IncludesLower BIT NOT NULL CONSTRAINT DF_ProstheticCases_IncludesLower DEFAULT (0),
        ToothScope NVARCHAR(200) NULL,
        AssignedProsthetistUserID INT NULL,
        StageID INT NULL,
        CaseStatus NVARCHAR(20) NOT NULL CONSTRAINT DF_ProstheticCases_Status DEFAULT (N'Open'),
        TotalAgreedPrice DECIMAL(18,2) NOT NULL CONSTRAINT DF_ProstheticCases_Price DEFAULT (0),
        Notes NVARCHAR(1000) NULL,
        CreatedByUserID INT NOT NULL,
        CreatedAt DATETIME NOT NULL CONSTRAINT DF_ProstheticCases_CreatedAt DEFAULT (GETDATE()),
        CONSTRAINT UQ_ProstheticCases_CaseNumber UNIQUE (CaseNumber),
        CONSTRAINT CK_ProstheticCases_Status CHECK (CaseStatus IN (N'Open', N'Completed', N'Cancelled')),
        CONSTRAINT FK_ProstheticCases_Patient FOREIGN KEY (PatientID) REFERENCES dbo.Patients(PatientID),
        CONSTRAINT FK_ProstheticCases_WorkType FOREIGN KEY (WorkTypeID) REFERENCES dbo.ProstheticWorkTypes(WorkTypeID),
        CONSTRAINT FK_ProstheticCases_Stage FOREIGN KEY (StageID) REFERENCES dbo.ProstheticStages(StageID),
        CONSTRAINT FK_ProstheticCases_Prosthetist FOREIGN KEY (AssignedProsthetistUserID) REFERENCES dbo.Users(UserID),
        CONSTRAINT FK_ProstheticCases_CreatedBy FOREIGN KEY (CreatedByUserID) REFERENCES dbo.Users(UserID)
    );
    CREATE NONCLUSTERED INDEX IX_ProstheticCases_Patient ON dbo.ProstheticCases(PatientID);
    CREATE NONCLUSTERED INDEX IX_ProstheticCases_Prosthetist ON dbo.ProstheticCases(AssignedProsthetistUserID);
    CREATE NONCLUSTERED INDEX IX_ProstheticCases_Status ON dbo.ProstheticCases(CaseStatus);
END";
        db.ExecuteNonQuery(casesSql);

        const string historySql = @"
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'dbo.ProstheticCaseHistory') AND type = 'U')
BEGIN
    CREATE TABLE dbo.ProstheticCaseHistory(
        HistoryID INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        CaseID INT NOT NULL,
        ChangedByUserID INT NOT NULL,
        ChangedAt DATETIME NOT NULL CONSTRAINT DF_ProstheticCaseHistory_ChangedAt DEFAULT (GETDATE()),
        ActionType NVARCHAR(50) NOT NULL,
        OldValue NVARCHAR(500) NULL,
        NewValue NVARCHAR(500) NULL,
        Notes NVARCHAR(500) NULL,
        CONSTRAINT FK_ProstheticCaseHistory_Case FOREIGN KEY (CaseID) REFERENCES dbo.ProstheticCases(CaseID),
        CONSTRAINT FK_ProstheticCaseHistory_User FOREIGN KEY (ChangedByUserID) REFERENCES dbo.Users(UserID)
    );
    CREATE NONCLUSTERED INDEX IX_ProstheticCaseHistory_Case ON dbo.ProstheticCaseHistory(CaseID);
END";
        db.ExecuteNonQuery(historySql);

        const string sessionsSql = @"
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'dbo.ProstheticSessions') AND type = 'U')
BEGIN
    CREATE TABLE dbo.ProstheticSessions(
        ProstheticSessionID INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        CaseID INT NOT NULL,
        SessionDateTime DATETIME NOT NULL CONSTRAINT DF_ProstheticSessions_DateTime DEFAULT (GETDATE()),
        PerformedByUserID INT NOT NULL,
        Description NVARCHAR(1000) NULL,
        CreatedAt DATETIME NOT NULL CONSTRAINT DF_ProstheticSessions_CreatedAt DEFAULT (GETDATE()),
        CONSTRAINT FK_ProstheticSessions_Case FOREIGN KEY (CaseID) REFERENCES dbo.ProstheticCases(CaseID),
        CONSTRAINT FK_ProstheticSessions_User FOREIGN KEY (PerformedByUserID) REFERENCES dbo.Users(UserID)
    );
    CREATE NONCLUSTERED INDEX IX_ProstheticSessions_Case ON dbo.ProstheticSessions(CaseID);
END";
        db.ExecuteNonQuery(sessionsSql);

        const string paymentsSql = @"
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'dbo.ProstheticPayments') AND type = 'U')
BEGIN
    CREATE TABLE dbo.ProstheticPayments(
        ProstheticPaymentID INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        CaseID INT NOT NULL,
        Amount DECIMAL(18,2) NOT NULL,
        PaymentDate DATETIME NOT NULL CONSTRAINT DF_ProstheticPayments_Date DEFAULT (GETDATE()),
        ReceivedByUserID INT NOT NULL,
        Notes NVARCHAR(200) NULL,
        CONSTRAINT FK_ProstheticPayments_Case FOREIGN KEY (CaseID) REFERENCES dbo.ProstheticCases(CaseID),
        CONSTRAINT FK_ProstheticPayments_User FOREIGN KEY (ReceivedByUserID) REFERENCES dbo.Users(UserID)
    );
    CREATE NONCLUSTERED INDEX IX_ProstheticPayments_Case ON dbo.ProstheticPayments(CaseID);
END";
        db.ExecuteNonQuery(paymentsSql);
    }

    // Patch 13: بيانات افتراضية أولية لأنواع العمل والمراحل - تُدرَج فقط إن كان الجدول فارغًا
    // تمامًا (COUNT = 0)، وليس عبر IF NOT EXISTS على الجدول نفسه (الجدول أصلاً موجود دائمًا هنا
    // بحكم استدعائها بعد EnsureProstheticTables مباشرة) - فلا تتكرر البيانات عند إعادة تشغيل
    // التطبيق، ولا تُعاد إضافتها إن حذف الطبيب الرئيسي كل العناصر لاحقًا (حالة فارغة متعمَّدة تبقى
    // فارغة، وليست "خطأ" يجب إصلاحه تلقائيًا في كل تشغيل). هذه مجرد نقطة بداية قابلة للتعديل الكامل
    // من واجهة الإدارة الجديدة فور أول تشغيل - وليست قائمة طبية نهائية أو إجبارية.
    private static void EnsureProstheticLookupsSeeded(DatabaseHelper db)
    {
        const string workTypesSeedSql = @"
IF NOT EXISTS (SELECT 1 FROM dbo.ProstheticWorkTypes)
BEGIN
    INSERT INTO dbo.ProstheticWorkTypes (WorkTypeName, IsActive, SortOrder) VALUES
        (N'Crown', 1, 1),
        (N'Bridge', 1, 2),
        (N'Complete Denture', 1, 3),
        (N'Partial Denture', 1, 4),
        (N'Implant Prosthesis', 1, 5);
END";
        db.ExecuteNonQuery(workTypesSeedSql);

        const string stagesSeedSql = @"
IF NOT EXISTS (SELECT 1 FROM dbo.ProstheticStages)
BEGIN
    INSERT INTO dbo.ProstheticStages (StageName, IsActive, SortOrder) VALUES
        (N'Laboratory', 1, 1),
        (N'Try-in', 1, 2),
        (N'Adjustment', 1, 3),
        (N'Delivery', 1, 4),
        (N'Completed', 1, 5);
END";
        db.ExecuteNonQuery(stagesSeedSql);
    }

    // تنظيف آمن لأي مفتاح صلاحية "قديم" لم يعد موجودًا في التصميم الحالي (كان هذا موجودًا في
    // الباتش 1 قبل تدقيق تصميم الصلاحيات - Prosthetics.ManageCases استُبدِلت بصلاحيات ذرّية:
    // CreateCase/EditCase/DeleteCase/TransferCase). إن لم يكن أحد قد مُنِح هذا المفتاح فعليًا
    // بعد (الأرجح، لأن شاشة الصلاحيات لم تُبنَ بعد) فهذا لا يفعل شيئًا عمليًا.
    // ⚠️ لا يوجد ON DELETE CASCADE على FK_UserPermissions_Permissions في المخطط الأصلي (تحقَّق
    // من DentalClinicSchema.sql مباشرة قبل كتابة هذه الدالة) - لذلك يجب حذف صفوف UserPermissions
    // التابعة أولاً صراحةً، وإلا يفشل حذف Permissions بخطأ انتهاك القيد الخارجي.
    private static void EnsureObsoleteProstheticPermissionKeysRemoved(DatabaseHelper db)
    {
        const string sql = @"
DELETE FROM dbo.UserPermissions WHERE PermissionKey = N'Prosthetics.ManageCases';
DELETE FROM dbo.Permissions WHERE PermissionKey = N'Prosthetics.ManageCases';";
        db.ExecuteNonQuery(sql);
    }

    // يزرع مفاتيح الصلاحيات الأساسية الخاصة بالترميم في جدول Permissions (الموجود أصلاً في المخطط
    // بدون استخدام سابق) - فئة "Prosthetics" فقط، لا يمس أي صلاحية أخرى قد تُزرَع مستقبلاً لغرض آخر.
    // DisplayName هنا نص إنجليزي احتياطي فقط - الواجهة تعرض النص المترجَم عبر مفتاح Localization
    // مطابق لـ PermissionKey إن وُجد، وتقع على DisplayName هذا كـ fallback فقط.
    private static void EnsureProstheticPermissionsSeeded(DatabaseHelper db)
    {
        var definitions = new (string Key, string DisplayName, int Sort)[]
        {
            // ===== الحالة =====
            (ProstheticPermissionKeys.ViewAllCases,  "View all prosthetic cases (not only assigned)", 10),
            (ProstheticPermissionKeys.CreateCase,    "Create a new prosthetic case", 20),
            (ProstheticPermissionKeys.EditCase,      "Edit case info (work type / arch / price / notes)", 30),
            (ProstheticPermissionKeys.DeleteCase,    "Delete a prosthetic case", 40),
            (ProstheticPermissionKeys.TransferCase,  "Transfer case to another prosthetist", 50),
            (ProstheticPermissionKeys.ViewStage,     "View treatment stage", 60),
            (ProstheticPermissionKeys.EditStage,     "Change treatment stage", 70),
            (ProstheticPermissionKeys.ManageLookups, "Manage work types & treatment stages lists", 80),

            // ===== الجلسات =====
            (ProstheticPermissionKeys.AddSession,    "Add prosthetic session", 90),
            (ProstheticPermissionKeys.EditSession,   "Edit prosthetic session", 100),
            (ProstheticPermissionKeys.DeleteSession, "Delete prosthetic session", 110),

            // ===== بيانات المريض المرتبطة =====
            (ProstheticPermissionKeys.ViewPatient,     "View patient profile info", 120),
            (ProstheticPermissionKeys.EditPatientInfo, "Edit patient basic info from case file", 130),
            (ProstheticPermissionKeys.ViewTreatment,   "View patient's clinic treatments", 140),
            (ProstheticPermissionKeys.ViewDiagnosis,   "View patient's diagnoses", 150),
            (ProstheticPermissionKeys.ViewMedications, "View patient's medications", 160),

            // ===== المالية =====
            (ProstheticPermissionKeys.ViewFinance,   "View prosthetic finance dashboard", 170),
            (ProstheticPermissionKeys.ViewPayment,   "View payments within a case file", 180),
            (ProstheticPermissionKeys.AddPayment,    "Add prosthetic payment", 190),
            (ProstheticPermissionKeys.EditPayment,   "Edit prosthetic payment", 200),
            (ProstheticPermissionKeys.DeletePayment, "Delete prosthetic payment", 210),
            (ProstheticPermissionKeys.AddExpense,    "Add prosthetic expense", 220),
            (ProstheticPermissionKeys.DeleteExpense, "Delete prosthetic expense", 230),
        };

        foreach (var (key, displayName, sort) in definitions)
        {
            const string sql = @"
IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE PermissionKey = @Key)
BEGIN
    INSERT INTO dbo.Permissions (PermissionKey, Category, DisplayName, Description, SortOrder)
    VALUES (@Key, N'Prosthetics', @DisplayName, NULL, @Sort);
END";
            db.ExecuteNonQuery(sql,
                new SqlParameter("@Key", key),
                new SqlParameter("@DisplayName", displayName),
                new SqlParameter("@Sort", sort));
        }
    }

    // ترقية آمنة لقواعد البيانات المُنشأة قبل إضافة خاصية "سعر نوع العمل": تضيف عمود Price فقط
    // إن لم يكن موجوداً أصلاً (Idempotent) - لا تمس أي بيانات موجودة، والقيمة الافتراضية 0 لأي
    // نوع عمل قديم لم يُحدَّد له سعر بعد (يستطيع الطبيب الرئيسي تعبئته لاحقاً من نافذة الإدارة).
    // ترقية آمنة: تضيف عمود Certificate (نص الشهادة الطبية / العطلة المرضية المختارة لهذه الجلسة)
    // إلى MedicalSessions إن لم يكن موجوداً أصلاً - نفس نمط WriteOffAmount/RemainingAmount أعلاه
    private static void EnsureCertificateColumn(DatabaseHelper db)
    {
        const string sql = @"
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.MedicalSessions') AND name = N'Certificate')
BEGIN
    ALTER TABLE dbo.MedicalSessions
        ADD Certificate NVARCHAR(MAX) NULL;
END";
        db.ExecuteNonQuery(sql);
    }

    private static void EnsureWorkTypePriceColumn(DatabaseHelper db)
    {
        const string sql = @"
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.ProstheticWorkTypes') AND name = N'Price')
BEGIN
    ALTER TABLE dbo.ProstheticWorkTypes
        ADD Price DECIMAL(18,2) NOT NULL
            CONSTRAINT DF_ProstheticWorkTypes_Price DEFAULT (0) WITH VALUES;
END";
        db.ExecuteNonQuery(sql);
    }

    private static void EnsureMedicalSessionRowVersion(DatabaseHelper db)
    {
        const string sql = @"
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.MedicalSessions') AND name = N'RowVersion')
BEGIN
    ALTER TABLE dbo.MedicalSessions ADD RowVersion ROWVERSION NOT NULL;
END";
        db.ExecuteNonQuery(sql);
    }

    private static void EnsurePaymentWriteOffColumn(DatabaseHelper db)
    {
        // حذف دخل لا يجب أن يعيد المبلغ إلى الدين. نحتفظ بالمبلغ المحذوف كـ "شطب مالي"
        // على الجلسة، بحيث تبقى Payments هي مصدر الإيراد، بينما يبقى الرصيد المستحق صحيحاً
        // حتى بعد إضافة دفعة جديدة لاحقاً.
        const string sql = @"
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.MedicalSessions') AND name = N'WriteOffAmount')
BEGIN
    ALTER TABLE dbo.MedicalSessions
        ADD WriteOffAmount DECIMAL(10,2) NOT NULL
            CONSTRAINT DF_MedicalSessions_WriteOffAmount DEFAULT (0) WITH VALUES;
END;

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.MedicalSessions') AND name = N'RemainingAmount')
BEGIN
    ALTER TABLE dbo.MedicalSessions DROP COLUMN RemainingAmount;
END;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.MedicalSessions') AND name = N'RemainingAmount')
BEGIN
    ALTER TABLE dbo.MedicalSessions
        ADD RemainingAmount AS (TotalPrice - PaidAmount - WriteOffAmount) PERSISTED;
END;";

        db.ExecuteNonQuery(sql);
    }

    private static string LoadEmbeddedScript()
    {
        var assembly = typeof(SchemaInitializer).Assembly;
        using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new InvalidOperationException($"Embedded schema resource not found: {EmbeddedResourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
