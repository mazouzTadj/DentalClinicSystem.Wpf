using System.Data;
using Microsoft.Data.SqlClient;
using DentalClinic.Data.Models;

namespace DentalClinic.Data.DataAccess;

// طبقة وصول لقوائم "نوع العمل" و"مرحلة العلاج" - كلاهما قابل للتوسع بالكامل من واجهة الطبيب
// الرئيسي (إضافة/تعديل الاسم/تعطيل/إعادة تفعيل/إعادة ترتيب) بدون أي تعديل كود، تنفيذًا لمتطلب
// "لا تجعلها Hardcoded".
//
// Patch 13: إضافة عمليات الكتابة الكاملة (كانت AddWorkType/SetWorkTypeActive/AddStage/
// SetStageActive موجودة سابقًا لكن غير مُستخدَمة في أي مكان - لا واجهة إدارة كانت موجودة أصلًا).
// ⚠️ دفاع في العمق: كل دالة كتابة هنا (Add/Rename/SetActive/Move) تتحقق بنفسها عبر EnsureMainDoctor
// أن المستخدم المُنفِّذ هو الطبيب الرئيسي فعليًا (IsMainDoctor) - وليس أصغر UserID، وليس أول
// Doctor، وليس RoleID=3، وليس IsAdmin - تمامًا كما طُلب صراحةً. هذا الفحص يعمل بصرف النظر عمّا
// إذا كانت الواجهة أخفت الزر أم لا؛ استدعاء أي من هذه الدوال مباشرة من كود آخر بحساب غير الطبيب
// الرئيسي يفشل فورًا بـUnauthorizedAccessException.
public class ProstheticLookupRepository
{
    private readonly DatabaseHelper _db;

    public ProstheticLookupRepository(DatabaseHelper db)
    {
        _db = db;
    }

    private static void EnsureMainDoctor(UserAccount actingUser)
    {
        if (actingUser.Role != UserRole.Doctor || !actingUser.IsMainDoctor)
        {
            throw new UnauthorizedAccessException(
                "Only the main doctor can manage prosthetic work types and stages.");
        }
    }

    // ===================== أنواع العمل =====================

    // activeOnly=true (الافتراضي): للاستخدام العادي عند إنشاء حالة جديدة - لا تُعرَض أنواع معطَّلة.
    // activeOnly=false: تُستخدَم من (أ) نافذة الإدارة نفسها (تحتاج رؤية كل شيء لإدارته)، (ب) نافذة
    // تعديل حالة قائمة كي لا يُفقَد نوع العمل الحالي إن عُطِّل لاحقًا (راجع Patch 8 لنفس المنطق مع
    // History)، و(ج) حل الأسماء في History والإحصائيات.
    public List<ProstheticWorkType> GetWorkTypes(bool activeOnly = true)
    {
        var sql = "SELECT WorkTypeID, WorkTypeName, Price, IsActive, SortOrder FROM dbo.ProstheticWorkTypes";
        if (activeOnly) sql += " WHERE IsActive = 1";
        sql += " ORDER BY SortOrder, WorkTypeName";

        var table = _db.ExecuteQuery(sql);
        var result = new List<ProstheticWorkType>();
        foreach (DataRow row in table.Rows)
        {
            result.Add(new ProstheticWorkType
            {
                WorkTypeID = (int)row["WorkTypeID"],
                WorkTypeName = row["WorkTypeName"].ToString()!,
                Price = Convert.ToDecimal(row["Price"]),
                IsActive = (bool)row["IsActive"],
                SortOrder = (int)row["SortOrder"]
            });
        }
        return result;
    }

    // العنصر الجديد يُضاف دائمًا في نهاية الترتيب الحالي (MAX(SortOrder)+1) - يستطيع الطبيب لاحقًا
    // نقله لأعلى/أسفل يدويًا عبر MoveWorkType إن أراد ترتيبًا مختلفًا
    public int AddWorkType(string name, decimal price, UserAccount actingUser)
    {
        EnsureMainDoctor(actingUser);
        const string sql = @"
            INSERT INTO dbo.ProstheticWorkTypes (WorkTypeName, Price, IsActive, SortOrder)
            VALUES (@Name, @Price, 1, (SELECT ISNULL(MAX(SortOrder), 0) + 1 FROM dbo.ProstheticWorkTypes))";
        return _db.ExecuteInsertAndGetId(sql,
            new SqlParameter("@Name", name),
            new SqlParameter("@Price", price));
    }

    // تعديل الاسم والسعر معاً - لا تُنشئ صفًا جديدًا ولا تمس WorkTypeID، فتبقى كل الحالات (القديمة
    // والجديدة) المرتبطة بهذا المعرِّف تعرض الاسم/السعر الجديدين تلقائيًا في كل مكان (تفاصيل
    // الحالة، History، الإحصائيات) بلا أي تحديث يدوي لأي جدول آخر. تعديل السعر هنا لا يُغيِّر
    // السعر المتفَق عليه في أي حالة قائمة بالفعل (TotalAgreedPrice مخزَّن في ProstheticCases نفسه) -
    // فقط يُغيِّر القيمة التي ستُملأ تلقائياً عند اختيار هذا النوع في حالة جديدة أو حالة يُعدَّل نوع عملها.
    public void UpdateWorkType(int workTypeId, string newName, decimal price, UserAccount actingUser)
    {
        EnsureMainDoctor(actingUser);
        _db.ExecuteNonQuery(
            "UPDATE dbo.ProstheticWorkTypes SET WorkTypeName = @Name, Price = @Price WHERE WorkTypeID = @Id",
            new SqlParameter("@Name", newName),
            new SqlParameter("@Price", price),
            new SqlParameter("@Id", workTypeId));
    }

    // تعطيل/إعادة تفعيل - ما زالت متاحة (تُستخدَم داخلياً كبديل تلقائي عند رفض الحذف النهائي
    // لعنصر مستخدَم فعلياً في حالة موجودة، وأيضاً لمن يفضّل الإخفاء المؤقَّت بدل الحذف).
    public void SetWorkTypeActive(int workTypeId, bool isActive, UserAccount actingUser)
    {
        EnsureMainDoctor(actingUser);
        _db.ExecuteNonQuery(
            "UPDATE dbo.ProstheticWorkTypes SET IsActive = @IsActive WHERE WorkTypeID = @Id",
            new SqlParameter("@IsActive", isActive),
            new SqlParameter("@Id", workTypeId));
    }

    // هل نوع العمل هذا مستخدَم في حالة ترميم واحدة أو أكثر (قديمة أو حالية، بصرف النظر عن حالتها)؟
    public int CountCasesUsingWorkType(int workTypeId)
    {
        var result = _db.ExecuteScalar(
            "SELECT COUNT(*) FROM dbo.ProstheticCases WHERE WorkTypeID = @Id",
            new SqlParameter("@Id", workTypeId));
        return Convert.ToInt32(result);
    }

    // حذف نهائي حقيقي (Hard Delete) - بناءً على طلب صريح لاحق يفضّل الحذف الفعلي على التعطيل.
    // ⚠️ حماية إلزامية: يرفض الحذف فوراً (ProstheticLookupInUseException) إن كان هذا النوع
    // مستخدَماً في أي حالة موجودة، بدل ترك قيد FK_ProstheticCases_WorkType يرمي خطأ SQL خام،
    // أو أسوأ - السماح بحذفه لو كان القيد Nullable فتُفقَد المعلومة من كل الحالات القديمة بصمت.
    // الواجهة تلتقط هذا الاستثناء وتعرض رسالة تقترح "تعطيل" بدلاً من الحذف في هذه الحالة تحديداً.
    public void DeleteWorkType(int workTypeId, UserAccount actingUser)
    {
        EnsureMainDoctor(actingUser);

        var usageCount = CountCasesUsingWorkType(workTypeId);
        if (usageCount > 0)
        {
            throw new ProstheticLookupInUseException(usageCount);
        }

        _db.ExecuteNonQuery(
            "DELETE FROM dbo.ProstheticWorkTypes WHERE WorkTypeID = @Id",
            new SqlParameter("@Id", workTypeId));
    }

    // نقل عنصر خطوة واحدة لأعلى/أسفل ضمن كامل القائمة (نشطة وغير نشطة معًا - الترتيب صفة عامة
    // للعنصر بصرف النظر عن حالة التفعيل)، بمبادلة SortOrder مع الجار مباشرة ضمن Transaction واحدة.
    // إن كان العنصر أصلاً في أول/آخر القائمة، العملية تُصبح no-op آمنًا بدل أي خطأ.
    public void MoveWorkType(int workTypeId, bool moveUp, UserAccount actingUser)
    {
        EnsureMainDoctor(actingUser);
        MoveItem("dbo.ProstheticWorkTypes", "WorkTypeID", "WorkTypeName", workTypeId, moveUp);
    }

    // ===================== مراحل العلاج =====================

    public List<ProstheticStage> GetStages(bool activeOnly = true)
    {
        var sql = "SELECT StageID, StageName, IsActive, SortOrder FROM dbo.ProstheticStages";
        if (activeOnly) sql += " WHERE IsActive = 1";
        sql += " ORDER BY SortOrder, StageName";

        var table = _db.ExecuteQuery(sql);
        var result = new List<ProstheticStage>();
        foreach (DataRow row in table.Rows)
        {
            result.Add(new ProstheticStage
            {
                StageID = (int)row["StageID"],
                StageName = row["StageName"].ToString()!,
                IsActive = (bool)row["IsActive"],
                SortOrder = (int)row["SortOrder"]
            });
        }
        return result;
    }

    public int AddStage(string name, UserAccount actingUser)
    {
        EnsureMainDoctor(actingUser);
        const string sql = @"
            INSERT INTO dbo.ProstheticStages (StageName, IsActive, SortOrder)
            VALUES (@Name, 1, (SELECT ISNULL(MAX(SortOrder), 0) + 1 FROM dbo.ProstheticStages))";
        return _db.ExecuteInsertAndGetId(sql, new SqlParameter("@Name", name));
    }

    public void UpdateStage(int stageId, string newName, UserAccount actingUser)
    {
        EnsureMainDoctor(actingUser);
        _db.ExecuteNonQuery(
            "UPDATE dbo.ProstheticStages SET StageName = @Name WHERE StageID = @Id",
            new SqlParameter("@Name", newName),
            new SqlParameter("@Id", stageId));
    }

    // تعطيل/إعادة تفعيل - ما زالت متاحة كبديل تلقائي عند رفض الحذف النهائي لمرحلة مستخدَمة فعلياً.
    public void SetStageActive(int stageId, bool isActive, UserAccount actingUser)
    {
        EnsureMainDoctor(actingUser);
        _db.ExecuteNonQuery(
            "UPDATE dbo.ProstheticStages SET IsActive = @IsActive WHERE StageID = @Id",
            new SqlParameter("@IsActive", isActive),
            new SqlParameter("@Id", stageId));
    }

    public int CountCasesUsingStage(int stageId)
    {
        var result = _db.ExecuteScalar(
            "SELECT COUNT(*) FROM dbo.ProstheticCases WHERE StageID = @Id",
            new SqlParameter("@Id", stageId));
        return Convert.ToInt32(result);
    }

    // حذف نهائي حقيقي لمرحلة - نفس منطق DeleteWorkType بالحرف (راجع تعليقاته أعلاه).
    public void DeleteStage(int stageId, UserAccount actingUser)
    {
        EnsureMainDoctor(actingUser);

        var usageCount = CountCasesUsingStage(stageId);
        if (usageCount > 0)
        {
            throw new ProstheticLookupInUseException(usageCount);
        }

        _db.ExecuteNonQuery(
            "DELETE FROM dbo.ProstheticStages WHERE StageID = @Id",
            new SqlParameter("@Id", stageId));
    }

    public void MoveStage(int stageId, bool moveUp, UserAccount actingUser)
    {
        EnsureMainDoctor(actingUser);
        MoveItem("dbo.ProstheticStages", "StageID", "StageName", stageId, moveUp);
    }

    // منطق التبديل المشترك بين أنواع العمل والمراحل - نفس الجدول البنيوي (ID/Name/SortOrder) في
    // كليهما، فتُطبَّق نفس الخوارزمية بمعاملات مختلفة بدل تكرار نفس الكود مرتين
    private void MoveItem(string tableName, string idColumn, string nameColumn, int id, bool moveUp)
    {
        using var conn = _db.GetConnection();
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            var all = new List<(int Id, int SortOrder)>();
            using (var cmd = new SqlCommand(
                $"SELECT {idColumn}, SortOrder FROM {tableName} ORDER BY SortOrder, {nameColumn}", conn, tx) { CommandTimeout = DatabaseHelper.CommandTimeoutSeconds })
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read()) all.Add((reader.GetInt32(0), reader.GetInt32(1)));
            }

            var index = all.FindIndex(x => x.Id == id);
            var swapIndex = moveUp ? index - 1 : index + 1;
            if (index < 0 || swapIndex < 0 || swapIndex >= all.Count)
            {
                tx.Rollback();
                return; // العنصر غير موجود، أو في طرف القائمة أصلاً - لا شيء لفعله
            }

            var current = all[index];
            var swapWith = all[swapIndex];

            void UpdateSortOrder(int rowId, int sortOrder)
            {
                using var cmd = new SqlCommand(
                    $"UPDATE {tableName} SET SortOrder = @SortOrder WHERE {idColumn} = @Id", conn, tx) { CommandTimeout = DatabaseHelper.CommandTimeoutSeconds };
                cmd.Parameters.AddWithValue("@SortOrder", sortOrder);
                cmd.Parameters.AddWithValue("@Id", rowId);
                cmd.ExecuteNonQuery();
            }

            UpdateSortOrder(current.Id, swapWith.SortOrder);
            UpdateSortOrder(swapWith.Id, current.SortOrder);

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }
}
