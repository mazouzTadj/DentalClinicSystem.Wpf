using System.Data;
using Microsoft.Data.SqlClient;
using DentalClinic.Data.Models;
using DentalClinic.Data.Helpers;

namespace DentalClinic.Data.DataAccess;

public class UserRepository
{
    private readonly DatabaseHelper _db;

    public UserRepository(DatabaseHelper db)
    {
        _db = db;
        EnsureCommissionColumnExists();
        EnsureHeartbeatColumnExists();
        EnsureMainDoctorColumnExists();
        EnsureMainDoctorBackfilled();
        EnsureMainDoctorUniqueIndex();
    }

    // عمود "آخر نبضة" لميزة "متصل الآن" - migration آمنة لقواعد البيانات القديمة (نفس نمط
    // EnsureCommissionColumnExists أعلاه)
    private void EnsureHeartbeatColumnExists()
    {
        const string sql = @"
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE Name = N'LastHeartbeatAt' AND Object_ID = Object_ID(N'Users'))
            BEGIN
                ALTER TABLE Users ADD LastHeartbeatAt DATETIME NULL;
            END";
        _db.ExecuteNonQuery(sql);
    }

    // إضافة عمود نسبة العمولة المخصَّصة لكل طبيب - migration آمنة لقواعد البيانات القديمة
    // (نفس نمط EnsureClinicExpensesTableExists المستخدَم في FinancialRepository)
    private void EnsureCommissionColumnExists()
    {
        const string sql = @"
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE Name = N'CommissionPercent' AND Object_ID = Object_ID(N'Users'))
            BEGIN
                ALTER TABLE Users ADD CommissionPercent DECIMAL(5,2) NULL;
            END";
        _db.ExecuteNonQuery(sql);
    }

    // عمود "الطبيب الرئيسي" (IsMainDoctor) - يتحكم حصرياً بالدخول لتطبيق المرمم بحساب طبيب وبرؤية
    // زر فتحه من DoctorApp. منفصل تماماً عن GetPrimaryDoctorUserId (خاص بالعمولات فقط) وعن IsAdmin
    // القديم (غير مُستخدَم إطلاقاً في الكود - استُبدِل بـ PermissionsMask). Migration آمنة، لا تحذف
    // ولا تُعدِّل أي عمود/بيانات موجودة.
    private void EnsureMainDoctorColumnExists()
    {
        const string sql = @"
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE Name = N'IsMainDoctor' AND Object_ID = Object_ID(N'Users'))
            BEGIN
                ALTER TABLE Users ADD IsMainDoctor BIT NOT NULL CONSTRAINT DF_Users_IsMainDoctor DEFAULT (0);
            END";
        _db.ExecuteNonQuery(sql);
    }

    // تعبئة تلقائية عند الترقية - لكن فقط في الحالة غير الغامضة إطلاقاً: إن لم يكن أي طبيب مُعلَّماً
    // كطبيب رئيسي بعد، **ويوجد طبيب نشط واحد بالضبط يملك صلاحية ManageUsers** (أي هو فعلياً المدير
    // العام الوحيد للنظام حالياً - إشارة حقيقية لسلطته الإدارية، وليست "أول طبيب أُنشئ" كما طُلِب
    // صراحةً تفاديه). إن وُجد أكثر من طبيب كهذا أو لم يوجد أي منهم، لا تُفعَّل أي شيء تلقائياً
    // (يتطلب تحديداً يدوياً - راجع تعليق SQL في README الباتش، سطر UPDATE واحد بسيط).
    private void EnsureMainDoctorBackfilled()
    {
        const string sql = @"
            IF NOT EXISTS (SELECT 1 FROM Users WHERE IsMainDoctor = 1)
            BEGIN
                DECLARE @Candidates TABLE (UserID INT);
                INSERT INTO @Candidates (UserID)
                SELECT UserID FROM Users
                WHERE RoleID = 1 AND IsActive = 1
                  AND (PermissionsMask & @ManageUsersFlag) = @ManageUsersFlag;

                IF (SELECT COUNT(*) FROM @Candidates) = 1
                BEGIN
                    UPDATE Users SET IsMainDoctor = 1
                    WHERE UserID = (SELECT TOP 1 UserID FROM @Candidates);
                END
            END";
        _db.ExecuteNonQuery(sql, new SqlParameter("@ManageUsersFlag", (int)UserPermission.ManageUsers));
    }

    // Patch 6.2: الطبقة الأخيرة من الحماية ضد التزامن - Transaction وحدها (Read Committed الافتراضي في
    // SQL Server) لا تمنع سباقاً بين معاملتين متزامنتين تماماً (كل منهما تُلغي القديم ثم تُعيِّن مختلفاً)،
    // فتنتهي النتيجة بطبيبين رئيسيين معاً. الفهرس الفريد المُصفَّى (Filtered Unique Index) هنا يمنع ذلك
    // على مستوى قاعدة البيانات نفسها بصرف النظر عن أي تعارض تطبيقي - انظر SetAsMainDoctor لمعالجة
    // الاستثناء الناتج (SqlException رقم 2601/2627) وتحويله لرسالة واضحة بدل خطأ SQL خام.
    private void EnsureMainDoctorUniqueIndex()
    {
        // دفاع إضافي (ملاحظة مراجعة Patch 6.2): إن كانت قاعدة بيانات قديمة تحتوي فعلاً أكثر من طبيب
        // رئيسي واحد بالفعل - حالة فاسدة من قبل إدخال هذا القيد أصلاً، ولا يجب أن تحدث ضمن العمليات
        // الطبيعية بعد Patch 6.1/6.2 لأن SetAsMainDoctor تمنعها - فإن CREATE UNIQUE INDEX أدناه سيفشل
        // فوراً لأن البيانات الموجودة تخالف القيد الجديد. لذلك نُصلح أي تكرار محتمل هنا أولاً، قبل
        // إنشاء الفهرس مباشرة: نُبقي طبيباً واحداً فقط (صاحب أصغر UserID - اختيار حتمي بسيط وواضح،
        // وليس عشوائياً) ونُلغي صفة البقية.
        const string dedupeSql = @"
            IF (SELECT COUNT(*) FROM Users WHERE IsMainDoctor = 1) > 1
            BEGIN
                DECLARE @KeepUserID INT = (SELECT TOP 1 UserID FROM Users WHERE IsMainDoctor = 1 ORDER BY UserID ASC);
                UPDATE Users SET IsMainDoctor = 0 WHERE IsMainDoctor = 1 AND UserID <> @KeepUserID;
            END";
        _db.ExecuteNonQuery(dedupeSql);

        const string sql = @"
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Users_OneMainDoctor' AND object_id = OBJECT_ID(N'Users'))
            BEGIN
                CREATE UNIQUE INDEX UX_Users_OneMainDoctor ON Users(IsMainDoctor) WHERE IsMainDoctor = 1;
            END";
        _db.ExecuteNonQuery(sql);
    }

    // البحث عن مستخدم عبر اسم المستخدم (حسابات نشطة فقط) - تُستخدم لتسجيل الدخول
    public UserAccount? FindByUsername(string username)
    {
        const string sql = @"
            SELECT u.UserID, u.FullName, u.Username, u.PasswordHash, u.RoleID, r.RoleName, u.PhoneNumber, u.IsActive, u.PermissionsMask, u.CommissionPercent, u.CreatedAt, u.LastLoginAt,
                   u.LastHeartbeatAt, u.IsMainDoctor, CASE WHEN LastHeartbeatAt >= DATEADD(SECOND, -90, GETDATE()) THEN 1 ELSE 0 END AS IsOnline
            FROM Users u
            INNER JOIN Roles r ON r.RoleID = u.RoleID
            WHERE u.Username = @Username AND u.IsActive = 1";

        var table = _db.ExecuteQuery(sql, new SqlParameter("@Username", username));
        return table.Rows.Count == 0 ? null : MapRow(table.Rows[0]);
    }

    // تُستدعى دورياً (كل نصف دقيقة تقريباً) من كل تطبيق طبيب مفتوح ومسجَّل دخوله - أساس ميزة "متصل الآن"
    public void UpdateHeartbeat(int userId)
    {
        const string sql = "UPDATE Users SET LastHeartbeatAt = GETDATE() WHERE UserID = @UserID";
        _db.ExecuteNonQuery(sql, new SqlParameter("@UserID", userId));
    }

    // تسجيل الدخول: يتحقق من اسم المستخدم، كلمة المرور، وأن الدور مطابق للتطبيق المُستخدَم
    // (مثال: حساب ممرضة لا يمكنه الدخول إلى تطبيق الطبيب والعكس صحيح)
    // ملاحظة: الصلاحيات (Permissions) لا علاقة لها بهذا التحقق إطلاقاً - هي صلاحيات إضافية فوق الدور الأساسي فقط،
    // فحساب "جلولي زيد" (Permissions تتضمن ManageUsers, Role=Doctor) يسجّل دخوله لتطبيق الطبيب بشكل طبيعي تماماً كأي طبيب آخر.
    // يُبقيه للتوافق مع الاستدعاءات الحالية (DoctorApp/NurseApp) - يُفوِّض لِـ overload الجديد بمجموعة دور واحد فقط
    public UserAccount? Authenticate(string username, string password, UserRole requiredRole, out string errorMessage)
        => Authenticate(username, password, new[] { requiredRole }, out errorMessage);

    // النسخة الأعم: تقبل أكثر من دور مسموح دفعة واحدة - ضرورية لتطبيق المرمم تحديداً، الذي يجب أن
    // يقبل حسابات المرممين (Prosthetist) وأيضاً حساب الطبيب (Doctor، بصلاحية كاملة دائماً - راجع
    // البند 41 في متطلبات نظام الترميم: "الطبيب الرئيسي يجب أن يمتلك صلاحيات كاملة على النظام")
    // دون الحاجة لإنشاء حساب مرمم منفصل له.
    public UserAccount? Authenticate(string username, string password, IReadOnlyCollection<UserRole> allowedRoles, out string errorMessage)
    {
        errorMessage = string.Empty;
        var user = FindByUsername(username);

        if (user == null)
        {
            errorMessage = "Username not found or account is not active";
            return null;
        }

        if (!PasswordHelper.Verify(password, user.PasswordHash))
        {
            errorMessage = "Incorrect password";
            return null;
        }

        if (!allowedRoles.Contains(user.Role))
        {
            errorMessage = "This account is not authorized to access this application";
            return null;
        }

        UpdateLastLogin(user.UserID);
        return user;
    }

    private void UpdateLastLogin(int userId)
    {
        const string sql = "UPDATE Users SET LastLoginAt = GETDATE() WHERE UserID = @UserID";
        _db.ExecuteNonQuery(sql, new SqlParameter("@UserID", userId));
    }

    // هل يوجد أي مستخدم مسجَّل في القاعدة إطلاقاً (نشط أو غير نشط)؟
    // تُستخدم فقط عند بدء تشغيل DoctorApp لتحديد: نعرض شاشة تسجيل الدخول العادية،
    // أم شاشة "الإعداد الأول" لإنشاء أول حساب Super Admin (قاعدة بيانات جديدة فارغة تماماً
    // عند عميل جديد - راجع FirstRunSetupWindow).
    public bool AnyUsersExist()
    {
        const string sql = "SELECT COUNT(*) FROM Users";
        var result = _db.ExecuteScalar(sql);
        return result != null && Convert.ToInt32(result) > 0;
    }

    // ===================== إدارة المستخدمين (يتطلب صلاحية ManageUsers) =====================

    // كل المستخدمين بما فيهم غير النشطين - تُستخدم في شاشة إدارة المستخدمين فقط (بعكس FindByUsername)
    public List<UserAccount> GetAllUsers()
    {
        const string sql = @"
            SELECT u.UserID, u.FullName, u.Username, u.PasswordHash, u.RoleID, r.RoleName, u.PhoneNumber, u.IsActive, u.PermissionsMask, u.CommissionPercent, u.CreatedAt, u.LastLoginAt,
                   u.LastHeartbeatAt, u.IsMainDoctor, CASE WHEN LastHeartbeatAt >= DATEADD(SECOND, -90, GETDATE()) THEN 1 ELSE 0 END AS IsOnline
            FROM Users u
            INNER JOIN Roles r ON r.RoleID = u.RoleID
            ORDER BY u.FullName";

        var table = _db.ExecuteQuery(sql);
        var result = new List<UserAccount>();
        foreach (DataRow row in table.Rows)
        {
            result.Add(MapRow(row));
        }
        return result;
    }

    // إضافة مستخدم جديد (طبيب/ممرضة/مرمم، بأي مجموعة صلاحيات) - يرجع UserID الجديد
    public int AddUser(UserAccount user, string plainPassword)
    {
        var roleId = RoleToId(user.Role);
        const string sql = @"
            INSERT INTO Users (FullName, Username, PasswordHash, RoleID, PhoneNumber, PermissionsMask, IsActive)
            VALUES (@FullName, @Username, @PasswordHash, @RoleID, @PhoneNumber, @PermissionsMask, 1)";

        return _db.ExecuteInsertAndGetId(sql,
            new SqlParameter("@FullName", user.FullName),
            new SqlParameter("@Username", user.Username),
            new SqlParameter("@PasswordHash", PasswordHelper.Hash(plainPassword)),
            new SqlParameter("@RoleID", roleId),
            new SqlParameter("@PhoneNumber", (object?)user.PhoneNumber ?? DBNull.Value),
            new SqlParameter("@PermissionsMask", (int)user.Permissions));
    }

    // تعديل بيانات مستخدم موجود (بدون تغيير كلمة المرور - لذلك دالة منفصلة UpdatePassword أدناه)
    // Patch 14 (وأُصلحت في Patch 15): دفاع في العمق لسلامة "الطبيب الرئيسي".
    // ⚠️ Patch 15: النسخة الأصلية في Patch 14 كانت تقرأ IsMainDoctor عبر SELECT منفصل ثم تُقرِّر
    // ثم تُنفِّذ UPDATE منفصلاً - ثلاث خطوات غير ذرّية. نظرياً، بين القراءة والتنفيذ، عملية أخرى
    // متزامنة (SetAsMainDoctor مثلاً من مستخدم آخر في نفس اللحظة) قد تُغيِّر IsMainDoctor، فتُنفَّذ
    // هذه العملية بناءً على قراءة أصبحت قديمة (Race Condition / TOCTOU حقيقية، رصدها فحص Audit).
    // الإصلاح: الشرط نفسه أصبح جزءاً من WHERE الخاص بالـUPDATE الذرّي الوحيد أدناه - قاعدة البيانات
    // نفسها (ضمن قفل الصف التلقائي أثناء الـUPDATE) هي من تفصل الآن، وليس قرار مبني على قراءة سابقة
    // قد تصبح غير صالحة. النتيجة: يُسمح بالتحديث فقط إن كان المستخدم ليس الطبيب الرئيسي حالياً، أو
    // كان الطبيب الرئيسي والقيم الجديدة تُبقيه Doctor+Active معاً - لا حالة وسطى ممكنة إطلاقاً.
    public void UpdateUser(UserAccount user)
    {
        var roleId = RoleToId(user.Role);

        const string sql = @"
            UPDATE Users
            SET FullName = @FullName, Username = @Username, RoleID = @RoleID,
                PhoneNumber = @PhoneNumber, PermissionsMask = @PermissionsMask, IsActive = @IsActive
            WHERE UserID = @UserID
              AND (IsMainDoctor = 0 OR (@RoleID = 1 AND @IsActive = 1))";

        var rowsAffected = _db.ExecuteNonQuery(sql,
            new SqlParameter("@FullName", user.FullName),
            new SqlParameter("@Username", user.Username),
            new SqlParameter("@RoleID", roleId),
            new SqlParameter("@PhoneNumber", (object?)user.PhoneNumber ?? DBNull.Value),
            new SqlParameter("@PermissionsMask", (int)user.Permissions),
            new SqlParameter("@IsActive", user.IsActive),
            new SqlParameter("@UserID", user.UserID));

        if (rowsAffected == 0)
        {
            // العملية لم تُنفَّذ - إما المستخدم غير موجود، أو (الأرجح في هذا المسار) رفضها شرط
            // WHERE أعلاه لأنه الطبيب الرئيسي. هذا الفحص التالي تشخيصي بحت لصياغة رسالة مفهومة -
            // لا يُتَّخذ بناءً عليه أي قرار أمني (القرار الفعلي حدث فعلاً وبأمان داخل الـUPDATE
            // الذرّي أعلاه)، فلا يُعيد فتح ثغرة الـRace Condition نفسها.
            var currentIsMainDoctorResult = _db.ExecuteScalar(
                "SELECT IsMainDoctor FROM Users WHERE UserID = @UserID", new SqlParameter("@UserID", user.UserID));
            var isCurrentlyMainDoctor = currentIsMainDoctorResult != null
                && currentIsMainDoctorResult != DBNull.Value
                && Convert.ToBoolean(currentIsMainDoctorResult);

            if (isCurrentlyMainDoctor && roleId != 1)
            {
                throw new InvalidOperationException(
                    "Cannot change the main doctor's role. Transfer the main doctor status to another doctor first.");
            }
            if (isCurrentlyMainDoctor && !user.IsActive)
            {
                throw new InvalidOperationException(
                    "The main doctor cannot be deactivated. Transfer the main doctor status to another doctor first.");
            }
            // غير ذلك (نادراً): المستخدم غير موجود أصلاً - سلوك متّسق مع أي UPDATE على صف غائب في
            // بقية المشروع (عملية بلا أثر بصمت)، فلا نرمي استثناءً إضافياً هنا
        }
    }

    // تحويل الدور إلى RoleID الفعلي الموجود في قاعدة البيانات.
    // Doctor/Nurse يحتفظان بالقيم التاريخية 1/2 لأن بقية النظام يعتمد عليهما،
    // أما Prosthetist فلا نفترض أن Identity الخاص به هو 3: القواعد القديمة قد تكون
    // أضافت/حذفت أدواراً فأصبح RoleID مختلفاً. لذلك نبحث عنه بالاسم.
    private int RoleToId(UserRole role)
    {
        if (role == UserRole.Doctor) return 1;
        if (role == UserRole.Nurse) return 2;

        if (role == UserRole.Prosthetist)
        {
            const string sql = "SELECT TOP 1 RoleID FROM Roles WHERE RoleName = N'Prosthetist'";
            var result = _db.ExecuteScalar(sql);
            if (result == null || result == DBNull.Value)
                throw new InvalidOperationException("The Prosthetist role is not installed in the database. Run SchemaInitializer.EnsureSchemaUpgrades() first.");

            return Convert.ToInt32(result);
        }

        throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown user role");
    }

    private static UserRole IdToRole(int roleId, string? roleName = null)
    {
        // Prosthetist is identified by RoleName, not by assuming RoleID=3.
        if (string.Equals(roleName, "Prosthetist", StringComparison.OrdinalIgnoreCase))
            return UserRole.Prosthetist;

        return roleId switch
        {
            1 => UserRole.Doctor,
            2 => UserRole.Nurse,
            _ => throw new ArgumentOutOfRangeException(nameof(roleId), roleId, "Unknown RoleID in database")
        };
    }

    // تُستدعى فقط عند إدخال كلمة مرور جديدة صراحة أثناء التعديل (اتركها فارغة في الواجهة = لا تغيير)
    public void UpdatePassword(int userId, string newPlainPassword)
    {
        const string sql = "UPDATE Users SET PasswordHash = @Hash WHERE UserID = @UserID";
        _db.ExecuteNonQuery(sql,
            new SqlParameter("@Hash", PasswordHelper.Hash(newPlainPassword)),
            new SqlParameter("@UserID", userId));
    }

    // تعطيل/إعادة تفعيل حساب (لا حذف فعلي أبداً، حفاظاً على سجل من أنشأ كل مريض/جلسة تاريخياً)
    // Patch 15: نفس إصلاح Race Condition المطبَّق في UpdateUser بالحرف (كانت هذه الدالة أيضاً
    // محمية منذ Patch 14 عبر SELECT منفصل ثم UPDATE منفصل - غير ذرّي). الآن شرط "ليس الطبيب
    // الرئيسي" جزء من WHERE في UPDATE ذرّي واحد، فلا نافذة زمنية يمكن لعملية متزامنة استغلالها
    // بين القراءة والتنفيذ. هذه الدالة مستقلة تماماً عن UpdateUser (يستدعيها مسار مختلف: زر
    // التفعيل/التعطيل السريع في UserManagementWindow)، فتحتاج نفس الحماية بشكل منفصل تماماً.
    public void SetActive(int userId, bool isActive)
    {
        if (!isActive)
        {
            var rowsAffected = _db.ExecuteNonQuery(
                "UPDATE Users SET IsActive = 0 WHERE UserID = @UserID AND IsMainDoctor = 0",
                new SqlParameter("@UserID", userId));

            if (rowsAffected == 0)
            {
                // تشخيصي بحت بعد فشل الـUPDATE الذرّي أعلاه - لا يُتَّخذ بناءً عليه أي قرار أمني
                var currentIsMainDoctorResult = _db.ExecuteScalar(
                    "SELECT IsMainDoctor FROM Users WHERE UserID = @UserID", new SqlParameter("@UserID", userId));
                var isCurrentlyMainDoctor = currentIsMainDoctorResult != null
                    && currentIsMainDoctorResult != DBNull.Value
                    && Convert.ToBoolean(currentIsMainDoctorResult);

                if (isCurrentlyMainDoctor)
                {
                    throw new InvalidOperationException(
                        "The main doctor cannot be deactivated. Transfer the main doctor status to another doctor first.");
                }
                // غير ذلك: المستخدم غير موجود أصلاً - عملية بلا أثر بصمت، متّسقة مع بقية المشروع
            }
            return;
        }

        const string sql = "UPDATE Users SET IsActive = @IsActive WHERE UserID = @UserID";
        _db.ExecuteNonQuery(sql,
            new SqlParameter("@IsActive", isActive),
            new SqlParameter("@UserID", userId));
    }

    // تعيين/إزالة "الطبيب الرئيسي" صراحةً - الآن مُستخدَمة فعلياً من UserEditWindow (شاشة إدارة
    // المستخدمين، حصرياً لمن يملك الحق في ذلك - راجع UserEditWindow.CanEditMainDoctor)
    // يُعيِّن هذا المستخدم "الطبيب الرئيسي" الوحيد في النظام - يُلغي أي طبيب رئيسي سابق ضمن نفس
    // المعاملة (Transaction) ذرّياً. الـTransaction وحدها لا تكفي (Read Committed لا يمنع سباقاً بين
    // معاملتين متزامنتين تماماً)، لذلك تحمي أيضاً UX_Users_OneMainDoctor على مستوى قاعدة البيانات
    // (راجع EnsureMainDoctorUniqueIndex) - مستويان معاً يضمنان طبيباً رئيسياً واحداً فعلياً دائماً.
    // Patch 6.2: أصبحت أيضاً دفاعية بنفسها (لا تعتمد على الواجهة فقط) - تتحقق أن الهدف طبيب نشط
    // موجود فعلاً عبر شرط WHERE مباشر + فحص RowsAffected، وتحوِّل أي تعارض من الفهرس الفريد
    // (SqlException 2601/2627 - حالة نادرة جداً لأن UPDATE أعلاه يُنفَّذ أولاً ضمن نفس Transaction،
    // لكنها ممكنة نظرياً) إلى رسالة واضحة بدل خطأ SQL خام.
    // Patch 15: كانت هذه الدالة (منذ تصميمها الأول في Patch 6.1) بلا أي فحص لهوية "من يستدعيها" -
    // الحماية الوحيدة كانت في الواجهة (UserEditWindow._canEditMainDoctor). استدعاؤها مباشرة من
    // كود آخر كان يسمح لأي شخص بنقل صفة الطبيب الرئيسي، متجاوزاً قاعدة "الطبيب الرئيسي الحالي وحده
    // يستطيع تسليم الصفة لغيره" (نفس استثناء Bootstrap الأصلي محفوظ بلا أي تغيير في منطقه: لا
    // يوجد طبيب رئيسي إطلاقاً + المستخدم يملك ManageUsers - تماماً كما في UserEditWindow، لم يُعَد
    // تصميمه هنا، فقط نُقل نفس الشرط ليُفرَض في طبقة البيانات أيضاً).
    // ⚠️ actingUser هنا كائن موجود في الذاكرة (نفس الحال في ProstheticPermissionGuard) - إن سُحبت
    // صفة "طبيب رئيسي" منه للتو من جلسة أخرى، يبقى هذا الفحص يعتمد على الحالة التي كانت عند تسجيل
    // دخوله. هذا قرار معماري مقبول (وليس خطأً) طالما النتيجة النهائية للقاعدة تبقى صحيحة دائماً
    // (طبيب رئيسي واحد بالضبط، مضمون بالفهرس الفريد + الـTransaction أدناه بصرف النظر عن هذا)،
    // وليس ثغرة تكامل بيانات - فقط نافذة تفويض ضيقة جداً وموثَّقة صراحةً هنا.
    public void SetAsMainDoctor(int userId, UserAccount actingUser)
    {
        var isCurrentMainDoctor = actingUser.Role == UserRole.Doctor && actingUser.IsMainDoctor;
        var canBootstrap = !AnyMainDoctorExists() && actingUser.HasPermission(UserPermission.ManageUsers);

        if (!isCurrentMainDoctor && !canBootstrap)
        {
            throw new UnauthorizedAccessException(
                "Only the current main doctor can transfer the main doctor status to another doctor.");
        }

        using var conn = _db.GetConnection();
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            using (var clearCmd = new SqlCommand("UPDATE Users SET IsMainDoctor = 0 WHERE IsMainDoctor = 1", conn, tx) { CommandTimeout = DatabaseHelper.CommandTimeoutSeconds })
            {
                clearCmd.ExecuteNonQuery();
            }

            int rowsAffected;
            using (var setCmd = new SqlCommand(
                "UPDATE Users SET IsMainDoctor = 1 WHERE UserID = @UserID AND RoleID = 1 AND IsActive = 1", conn, tx) { CommandTimeout = DatabaseHelper.CommandTimeoutSeconds })
            {
                setCmd.Parameters.AddWithValue("@UserID", userId);
                rowsAffected = setCmd.ExecuteNonQuery();
            }

            if (rowsAffected == 0)
            {
                throw new InvalidOperationException(
                    "Only an active doctor account can be set as the main doctor. The selected user was not found, is not a doctor, or is not active.");
            }

            tx.Commit();
        }
        catch (SqlException sqlEx) when (sqlEx.Number == 2601 || sqlEx.Number == 2627)
        {
            tx.Rollback();
            throw new InvalidOperationException(
                "Could not set the main doctor because another main-doctor change happened at the exact same moment. Please try again.", sqlEx);
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    // إلغاء صفة "الطبيب الرئيسي" - Patch 6.2: أصبحت محظورة إن كان تنفيذها سيترك النظام بلا أي طبيب
    // رئيسي إطلاقاً، لأن القرار المُتَّفق عليه هو "طبيب رئيسي واحد دائماً" وليس صفراً بعد أول تعيين
    // (الصفر مسموح فقط قبل أول تعيين على الإطلاق - حالة البوتستراب في EnsureMainDoctorBackfilled /
    // AnyMainDoctorExists). التسليم الصحيح الوحيد هو نقل الصفة مباشرة لطبيب آخر عبر SetAsMainDoctor
    // (يُلغي القديم ويُعيِّن الجديد ذرّياً ضمن نفس Transaction، فلا تمر اللحظة أبداً بحالة صفر).
    public void ClearMainDoctor()
    {
        if (AnyMainDoctorExists())
        {
            throw new InvalidOperationException(
                "The main doctor status cannot be removed without assigning a replacement. To hand off this role, set another doctor as the main doctor instead.");
        }

        // لا يوجد طبيب رئيسي أصلاً - عملية بلا أثر عملي (لا شيء لتغييره، لكن تبقى آمنة الاستدعاء)
        _db.ExecuteNonQuery("UPDATE Users SET IsMainDoctor = 0 WHERE IsMainDoctor = 1");
    }

    // هل يوجد أي طبيب رئيسي معيَّن حالياً في النظام؟ تُستخدم لفتح "بوتستراب" التعيين الأول عندما
    // تكون الحالة غامضة (لم تنجح التعبئة التلقائية عند الترقية - راجع EnsureMainDoctorBackfilled):
    // لو لم يوجد طبيب رئيسي بعد، يُسمح لأي مستخدم يملك ManageUsers بتعيين أول طبيب رئيسي؛ بعد ذلك،
    // التعيينات اللاحقة تتطلب أن يكون المُنفِّذ نفسه الطبيب الرئيسي الحالي (راجع UserEditWindow).
    public bool AnyMainDoctorExists()
    {
        var result = _db.ExecuteScalar("SELECT COUNT(*) FROM Users WHERE IsMainDoctor = 1");
        return result != null && Convert.ToInt32(result) > 0;
    }

    // كم عدد المستخدمين النشطين الذين يملكون صلاحية ManageUsers حالياً؟
    // تُستخدم لمنع إزالة آخر مدير عام في النظام (وإلا يُقفل الجميع من شاشة الإدارة نهائياً)
    public int CountActiveSuperAdmins(int? excludeUserId = null)
    {
        var sql = "SELECT COUNT(*) FROM Users WHERE IsActive = 1 AND (PermissionsMask & @ManageUsersFlag) = @ManageUsersFlag";
        var parameters = new List<SqlParameter>
        {
            new SqlParameter("@ManageUsersFlag", (int)UserPermission.ManageUsers)
        };

        if (excludeUserId.HasValue)
        {
            sql += " AND UserID <> @ExcludeUserID";
            parameters.Add(new SqlParameter("@ExcludeUserID", excludeUserId.Value));
        }

        var result = _db.ExecuteScalar(sql, parameters.ToArray());
        return result == null ? 0 : Convert.ToInt32(result);
    }

    // هل اسم المستخدم هذا مستخدَم بالفعل من قِبل حساب آخر؟ (لإظهار رسالة واضحة بدل خطأ SQL خام)
    public bool UsernameExists(string username, int? excludeUserId = null)
    {
        var sql = "SELECT COUNT(*) FROM Users WHERE Username = @Username";
        var parameters = new List<SqlParameter> { new SqlParameter("@Username", username) };

        if (excludeUserId.HasValue)
        {
            sql += " AND UserID <> @ExcludeUserID";
            parameters.Add(new SqlParameter("@ExcludeUserID", excludeUserId.Value));
        }

        var result = _db.ExecuteScalar(sql, parameters.ToArray());
        return result != null && Convert.ToInt32(result) > 0;
    }

    private static UserAccount MapRow(DataRow row) => new UserAccount
    {
        UserID = (int)row["UserID"],
        FullName = row["FullName"].ToString()!,
        Username = row["Username"].ToString()!,
        PasswordHash = row["PasswordHash"].ToString()!,
        Role = IdToRole((int)row["RoleID"], row.Table.Columns.Contains("RoleName") ? row["RoleName"].ToString() : null),
        Permissions = (UserPermission)(int)row["PermissionsMask"],
        PhoneNumber = row["PhoneNumber"] as string,
        IsActive = (bool)row["IsActive"],
        CreatedAt = (DateTime)row["CreatedAt"],
        LastLoginAt = row["LastLoginAt"] as DateTime?,
        CommissionPercent = row.Table.Columns.Contains("CommissionPercent") && row["CommissionPercent"] != DBNull.Value
            ? Convert.ToDecimal(row["CommissionPercent"])
            : (decimal?)null,
        LastHeartbeatAt = row.Table.Columns.Contains("LastHeartbeatAt") && row["LastHeartbeatAt"] != DBNull.Value
            ? Convert.ToDateTime(row["LastHeartbeatAt"])
            : (DateTime?)null,
        IsMainDoctor = row.Table.Columns.Contains("IsMainDoctor") && (bool)row["IsMainDoctor"],
        IsOnline = row.Table.Columns.Contains("IsOnline") && Convert.ToInt32(row["IsOnline"]) == 1
    };

    // ===================== نظام تقسيم إيرادات الأطباء (Doctor Commission) =====================

    // "الطبيب الرئيسي" = أول طبيب سُجِّل في النظام (أصغر UserID بين من دورهم Doctor)، بغض النظر عمّن يملك صلاحيات إدارية اليوم
    public int? GetPrimaryDoctorUserId()
    {
        const string sql = "SELECT TOP 1 UserID FROM Users WHERE RoleID = 1 ORDER BY UserID ASC";
        var result = _db.ExecuteScalar(sql);
        return result == null || result == DBNull.Value ? null : Convert.ToInt32(result);
    }

    // كل الأطباء النشطين - تُستخدم في شاشة إعدادات العمولات لعرض كل طبيب مع نسبته
    public List<UserAccount> GetAllDoctors()
    {
        const string sql = @"
            SELECT u.UserID, u.FullName, u.Username, u.PasswordHash, u.RoleID, r.RoleName, u.PhoneNumber, u.IsActive, u.PermissionsMask, u.CommissionPercent, u.CreatedAt, u.LastLoginAt,
                   u.LastHeartbeatAt, u.IsMainDoctor, CASE WHEN LastHeartbeatAt >= DATEADD(SECOND, -90, GETDATE()) THEN 1 ELSE 0 END AS IsOnline
            FROM Users u
            INNER JOIN Roles r ON r.RoleID = u.RoleID
            WHERE u.RoleID = 1 AND u.IsActive = 1
            ORDER BY UserID ASC";

        var table = _db.ExecuteQuery(sql);
        var result = new List<UserAccount>();
        foreach (DataRow row in table.Rows)
        {
            result.Add(MapRow(row));
        }
        return result;
    }

    // كل المرممين النشطين - تُستخدم في شاشة إنشاء/نقل حالة الترميم (اختيار المرمم المسؤول)
    public List<UserAccount> GetAllProsthetists(bool activeOnly = true)
    {
        var sql = @"
            SELECT u.UserID, u.FullName, u.Username, u.PasswordHash, u.RoleID, r.RoleName, u.PhoneNumber, u.IsActive, u.PermissionsMask, u.CommissionPercent, u.CreatedAt, u.LastLoginAt,
                   u.LastHeartbeatAt, u.IsMainDoctor, CASE WHEN LastHeartbeatAt >= DATEADD(SECOND, -90, GETDATE()) THEN 1 ELSE 0 END AS IsOnline
            FROM Users u
            INNER JOIN Roles r ON r.RoleID = u.RoleID
            WHERE r.RoleName = N'Prosthetist'" + (activeOnly ? " AND u.IsActive = 1" : "") + @"
            ORDER BY u.FullName";

        var table = _db.ExecuteQuery(sql);
        var result = new List<UserAccount>();
        foreach (DataRow row in table.Rows)
        {
            result.Add(MapRow(row));
        }
        return result;
    }

    // تعيين/إزالة نسبة عمولة مخصَّصة لطبيب معيَّن (null = العودة لاستخدام النسبة العامة الافتراضية)
    public void UpdateDoctorCommissionPercent(int userId, decimal? commissionPercent)
    {
        const string sql = "UPDATE Users SET CommissionPercent = @CommissionPercent WHERE UserID = @UserID";
        _db.ExecuteNonQuery(sql,
            new SqlParameter("@CommissionPercent", (object?)commissionPercent ?? DBNull.Value),
            new SqlParameter("@UserID", userId));
    }
}
