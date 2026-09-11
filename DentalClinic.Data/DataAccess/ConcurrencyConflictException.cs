namespace DentalClinic.Data.DataAccess;

// لا نحفظ فوق تعديل مستخدم آخر بصمت.
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException()
        : base("This session was changed by another user. Reload it before saving your changes.")
    {
    }
}
