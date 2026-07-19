namespace DentalClinic.Data.Helpers;

// تشفير كلمات المرور والتحقق منها باستخدام BCrypt
// لا يجب أبداً تخزين كلمة المرور كنص صريح في قاعدة البيانات
public static class PasswordHelper
{
    public static string Hash(string plainPassword) =>
        BCrypt.Net.BCrypt.HashPassword(plainPassword);

    public static bool Verify(string plainPassword, string storedHash) =>
        BCrypt.Net.BCrypt.Verify(plainPassword, storedHash);
}
