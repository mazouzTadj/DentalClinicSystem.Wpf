# ============================================================
#  Publish script - dental clinic system (NurseApp + DoctorApp + ProsthetistApp)
#  شغّله من مجلد الحل (نفس مكان DentalClinicSystem.Wpf.sln)
#  عبر: Developer PowerShell for VS  أو أي PowerShell عادي بعد
#  التأكد أن dotnet SDK 10 مثبّت.
# ============================================================

$ErrorActionPreference = "Stop"

Write-Host "==> Cleaning previous publish output..." -ForegroundColor Cyan
Remove-Item -Recurse -Force "..\publish" -ErrorAction SilentlyContinue

Write-Host "==> Publishing NurseApp (self-contained, win-x64)..." -ForegroundColor Cyan
dotnet publish ".\DentalClinic.NurseApp\DentalClinic.NurseApp.csproj" `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishReadyToRun=true `
    -o "..\publish\NurseApp"

Write-Host "==> Publishing DoctorApp (self-contained, win-x64)..." -ForegroundColor Cyan
dotnet publish ".\DentalClinic.DoctorApp\DentalClinic.DoctorApp.csproj" `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishReadyToRun=true `
    -o "..\publish\DoctorApp"

Write-Host "==> Publishing ProsthetistApp (self-contained, win-x64)..." -ForegroundColor Cyan
dotnet publish ".\DentalClinic.ProsthetistApp\DentalClinic.ProsthetistApp.csproj" `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishReadyToRun=true `
    -o "..\publish\ProsthetistApp"

Write-Host "==> Done. Output folders:" -ForegroundColor Green
Write-Host "    ..\publish\NurseApp\NurseApp.exe (+ NurseApp.exe.config)"
Write-Host "    ..\publish\DoctorApp\DoctorApp.exe (+ DoctorApp.exe.config)"
Write-Host "    ..\publish\ProsthetistApp\ProsthetistApp.exe (+ ProsthetistApp.exe.config)"
Write-Host ""
Write-Host "تحقق أن كل مجلد يحتوي على ملف .exe.config (سطر الاتصال بقاعدة البيانات)." -ForegroundColor Yellow
Write-Host "الآن افتح DentalClinicSetup.iss في Inno Setup Compiler واضغط Build." -ForegroundColor Yellow