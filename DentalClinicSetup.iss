; ============================================================
;  Dental Clinic System - Client Installer (Inno Setup)
;  شغّل publish.ps1 أولاً، ثم افتح هذا الملف في Inno Setup Compiler
;  واضغط Build. يتيح للمستخدم عند التثبيت اختيار: تطبيق الاستقبال /
;  تطبيق الطبيب / الاثنين معاً، ويطلب عنوان سيرفر SQL ونوع المصادقة،
;  ثم يكتبهما تلقائياً داخل ملفات App.config لكل تطبيق مُثبَّت.
; ============================================================

#define MyAppName "Dental Clinic System"
#define MyAppVersion "1.1.0"
#define MyPublisher "MAZOUZ Tadjeddine"

[Setup]
AppId={{6E2F1A6C-4C7B-4C6C-9A5B-1D8F4B2E7C11}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyPublisher}
DefaultDirName={autopf}\DentalClinicSystem_WPF
DefaultGroupName=Dental Clinic System
DisableProgramGroupPage=yes
OutputDir=..\SetupOutput
OutputBaseFilename=DentalClinicSystem_Setup
Compression=lzma
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Components]
Name: "nurse";       Description: "Reception App (NurseApp) - قسم الاستقبال";              Types: full nurseonly
Name: "doctor";      Description: "Doctor App (DoctorApp) - تطبيق الطبيب";                 Types: full doctoronly
Name: "prosthetist"; Description: "Prosthetist App (ProsthetistApp) - تطبيق مرمم الأسنان"; Types: full prosthetistonly

[Types]
Name: "full";            Description: "All apps on this PC - كل التطبيقات على هذا الجهاز"
Name: "nurseonly";       Description: "Reception App only - تطبيق الاستقبال فقط"
Name: "doctoronly";      Description: "Doctor App only - تطبيق الطبيب فقط"
Name: "prosthetistonly"; Description: "Prosthetist App only - تطبيق المرمم فقط"
Name: "custom";           Description: "Custom - مخصص"; Flags: iscustom

[Files]
Source: "..\publish\NurseApp\*";       DestDir: "{app}\NurseApp";       Components: nurse;       Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\publish\DoctorApp\*";      DestDir: "{app}\DoctorApp";      Components: doctor;      Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\publish\ProsthetistApp\*"; DestDir: "{app}\ProsthetistApp"; Components: prosthetist; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Reception (NurseApp)"; Filename: "{app}\NurseApp\NurseApp.exe";  Components: nurse
Name: "{group}\Doctor (DoctorApp)";   Filename: "{app}\DoctorApp\DoctorApp.exe"; Components: doctor
Name: "{group}\Prosthetist (ProsthetistApp)"; Filename: "{app}\ProsthetistApp\ProsthetistApp.exe"; Components: prosthetist
Name: "{autodesktop}\Reception (NurseApp)"; Filename: "{app}\NurseApp\NurseApp.exe";  Components: nurse
Name: "{autodesktop}\Doctor (DoctorApp)";   Filename: "{app}\DoctorApp\DoctorApp.exe"; Components: doctor
Name: "{autodesktop}\Prosthetist (ProsthetistApp)"; Filename: "{app}\ProsthetistApp\ProsthetistApp.exe"; Components: prosthetist
Name: "{group}\Uninstall Dental Clinic System"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\NurseApp\NurseApp.exe"; Description: "Launch Reception App now"; Flags: nowait postinstall skipifsilent unchecked; Components: nurse
Filename: "{app}\DoctorApp\DoctorApp.exe"; Description: "Launch Doctor App now"; Flags: nowait postinstall skipifsilent unchecked; Components: doctor
Filename: "{app}\ProsthetistApp\ProsthetistApp.exe"; Description: "Launch Prosthetist App now"; Flags: nowait postinstall skipifsilent unchecked; Components: prosthetist

[Code]
var
  AuthPage: TInputOptionWizardPage;
  ServerPage: TInputQueryWizardPage;
  LoginPage: TInputQueryWizardPage;
  InstancesHint: TNewStaticText;
  DetectedExistingConfig: Boolean;
  AlreadyPrefilled: Boolean;

procedure DiscoverLocalSqlInstances; forward;

// ------------------------------------------------------------------
// يتحقق إن كان NurseApp.exe أو DoctorApp.exe يعملان حالياً قبل السماح
// بالتثبيت، لتفادي فشل نسخ الملفات (PublishSingleFile) في منتصف التثبيت
// ------------------------------------------------------------------
function IsProcessRunning(ExeName: String): Boolean;
var
  ResultCode: Integer;
  TmpFile: String;
  Output: AnsiString;
begin
  Result := False;
  TmpFile := ExpandConstant('{tmp}\dcs_proc_check.txt');
  Exec(ExpandConstant('{cmd}'), '/C tasklist /FI "IMAGENAME eq ' + ExeName + '" > "' + TmpFile + '"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  if FileExists(TmpFile) then
  begin
    LoadStringFromFile(TmpFile, Output);
    DeleteFile(TmpFile);
    Result := Pos(Lowercase(ExeName), Lowercase(Output)) > 0;
  end;
end;

function InitializeSetup(): Boolean;
var
  Msg: String;
begin
  Result := True;
  while IsProcessRunning('NurseApp.exe') or IsProcessRunning('DoctorApp.exe') or IsProcessRunning('ProsthetistApp.exe') do
  begin
    Msg := 'يجب إغلاق تطبيق الاستقبال (NurseApp) وتطبيق الطبيب (DoctorApp) وتطبيق المرمم (ProsthetistApp) على هذا الجهاز أولاً، ' +
      'ثم اضغط Retry للمتابعة.' + #13#10#13#10 +
      'Please close the Reception App (NurseApp), Doctor App (DoctorApp) and Prosthetist App (ProsthetistApp) on this machine, ' +
      'then click Retry to continue.';
    if MsgBox(Msg, mbError, MB_RETRYCANCEL) = IDCANCEL then
    begin
      Result := False;
      Exit;
    end;
  end;
end;

// ------------------------------------------------------------------
// يقرأ سلسلة الاتصال الحالية (إن وُجدت) من ملف config موجود مسبقاً
// ------------------------------------------------------------------
function GetConnectionStringFromConfig(ConfigFile: String): String;
var
  Content: AnsiString;
  StartMarker, EndQuote: Integer;
begin
  Result := '';
  if not FileExists(ConfigFile) then Exit;
  LoadStringFromFile(ConfigFile, Content);

  StartMarker := Pos('connectionString="', Content);
  if StartMarker = 0 then Exit;
  StartMarker := StartMarker + Length('connectionString="');

  EndQuote := Pos('"', Copy(Content, StartMarker, MaxInt));
  if EndQuote = 0 then Exit;
  EndQuote := EndQuote + StartMarker - 1;

  Result := Copy(Content, StartMarker, EndQuote - StartMarker);
  StringChangeEx(Result, '&quot;', '"', True);
  StringChangeEx(Result, '&apos;', '''', True);
  StringChangeEx(Result, '&lt;', '<', True);
  StringChangeEx(Result, '&gt;', '>', True);
  StringChangeEx(Result, '&amp;', '&', True);
end;

function ExtractField(Content, FieldName: String): String;
var
  StartP, EndP: Integer;
  Rest: AnsiString;
begin
  Result := '';
  StartP := Pos(FieldName, Content);
  if StartP = 0 then Exit;
  StartP := StartP + Length(FieldName);
  Rest := Copy(Content, StartP, MaxInt);
  EndP := Pos(';', Rest);
  if EndP = 0 then
    Result := Rest
  else
    Result := Copy(Rest, 1, EndP - 1);
end;

// يبحث عن أول config موجود من التثبيت السابق (لو كان هذا تحديثاً فوق
// نسخة سابقة) ويملأ الحقول تلقائياً بدل تركها فارغة/افتراضية
procedure PrefillFromExistingInstall;
var
  ConnStr: String;
  Candidates: array[0..5] of String;
  i: Integer;
begin
  Candidates[0] := ExpandConstant('{app}\NurseApp\NurseApp.exe.config');
  Candidates[1] := ExpandConstant('{app}\NurseApp\NurseApp.dll.config');
  Candidates[2] := ExpandConstant('{app}\DoctorApp\DoctorApp.exe.config');
  Candidates[3] := ExpandConstant('{app}\DoctorApp\DoctorApp.dll.config');
  Candidates[4] := ExpandConstant('{app}\ProsthetistApp\ProsthetistApp.exe.config');
  Candidates[5] := ExpandConstant('{app}\ProsthetistApp\ProsthetistApp.dll.config');

  ConnStr := '';
  for i := 0 to 5 do
  begin
    if ConnStr = '' then
      ConnStr := GetConnectionStringFromConfig(Candidates[i]);
  end;

  if ConnStr = '' then Exit; // لا يوجد تثبيت سابق، أو أول تثبيت من نوعه

  DetectedExistingConfig := True;

  if Pos('Integrated Security=True', ConnStr) > 0 then
  begin
    AuthPage.SelectedValueIndex := 0;
  end
  else
  begin
    AuthPage.SelectedValueIndex := 1;
    LoginPage.Values[0] := ExtractField(ConnStr, 'User ID=');
    LoginPage.Values[1] := ExtractField(ConnStr, 'Password=');
  end;

  if ExtractField(ConnStr, 'Data Source=') <> '' then
    ServerPage.Values[0] := ExtractField(ConnStr, 'Data Source=');
end;

// يحدّد مسبقاً نوع التثبيت (أي تطبيقات) بناءً على الأجهزة الموجودة فعلياً
// على هذا الحاسوب من تثبيت سابق - بأسلوب عام يغطي أي تركيبة من الثلاثة
// (وليس فقط nurse/doctor كما كان قبل إضافة ProsthetistApp)
procedure PreselectComponentsFromExistingInstall;
var
  HasNurse, HasDoctor, HasProsthetist: Boolean;
  Selected: String;
begin
  HasNurse := FileExists(ExpandConstant('{app}\NurseApp\NurseApp.exe'));
  HasDoctor := FileExists(ExpandConstant('{app}\DoctorApp\DoctorApp.exe'));
  HasProsthetist := FileExists(ExpandConstant('{app}\ProsthetistApp\ProsthetistApp.exe'));

  if not HasNurse and not HasDoctor and not HasProsthetist then
    Exit; // لا يوجد تثبيت سابق لأي تطبيق، يبقى الخيار الافتراضي full كما هو

  Selected := '';
  if HasNurse then Selected := Selected + 'nurse,';
  if HasDoctor then Selected := Selected + 'doctor,';
  if HasProsthetist then Selected := Selected + 'prosthetist,';

  if Length(Selected) > 0 then
    Delete(Selected, Length(Selected), 1); // إزالة الفاصلة الأخيرة الزائدة

  WizardSelectComponents(Selected);
end;

procedure InitializeWizard;
begin
  DetectedExistingConfig := False;
  AlreadyPrefilled := False;

  AuthPage := CreateInputOptionPage(wpSelectComponents,
    'SQL Server Authentication - نوع المصادقة',
    'How should this PC connect to the SQL Server?',
    'اختر طريقة الاتصال بسيرفر قاعدة البيانات. إن كانت الأجهزة ليست ضمن نفس Domain، ' +
    'يُفضَّل اختيار SQL Server Login بدلاً من Windows Authentication.',
    True, False);
  AuthPage.Add('Windows Authentication (Integrated Security) - مصادقة ويندوز');
  AuthPage.Add('SQL Server Login (username / password) - حساب SQL Server');
  AuthPage.SelectedValueIndex := 0;

  ServerPage := CreateInputQueryPage(AuthPage.ID,
    'SQL Server Address - عنوان سيرفر قاعدة البيانات',
    'Enter the SQL Server machine name/IP and instance',
    'مثال: 192.168.1.10\SQLEXPRESS  أو  SERVER-PC\SQLEXPRESS  ' +
    '(اتركه .\SQLEXPRESS إذا كان السيرفر هو نفس هذا الجهاز)');
  ServerPage.Add('Server\Instance:', False);
  ServerPage.Values[0] := '.\SQLEXPRESS';

  InstancesHint := TNewStaticText.Create(ServerPage);
  InstancesHint.Parent := ServerPage.Surface;
  InstancesHint.Left := 0;
  InstancesHint.Top := ScaleY(70);
  InstancesHint.Width := ServerPage.SurfaceWidth;
  InstancesHint.Height := ScaleY(42);
  InstancesHint.AutoSize := False;
  InstancesHint.Font.Color := clGray;
  DiscoverLocalSqlInstances;

  LoginPage := CreateInputQueryPage(ServerPage.ID,
    'SQL Server Login - بيانات الدخول',
    'Enter the SQL Server login credentials',
    'يُنشأ هذا الحساب مسبقاً على السيرفر من طرف مسؤول قاعدة البيانات (راجع دليل إعداد السيرفر).');
  LoginPage.Add('Username:', False);
  LoginPage.Add('Password:', True);
  // ملاحظة: لا يمكن قراءة {app} هنا لأنه غير مُهيَّأ بعد في هذه المرحلة
  // من التشغيل (يُهيَّأ فقط بعد صفحة اختيار مجلد التثبيت wpSelectDir).
  // لذلك تأجَّلت التعبئة التلقائية إلى CurPageChanged أدناه.
end;

// يبحث في سجل Windows عن SQL Server instances المحلية المسجّلة. لا يعتمد ذلك
// على خدمة SQL Browser أو الشبكة، لذلك يبقى مفيداً حتى عند إيقاف الخدمة مؤقتاً.
procedure DiscoverLocalSqlInstances;
var
  Names: TArrayOfString;
  i: Integer;
  Instances, Preferred: String;
begin
  Instances := '';
  Preferred := '';

  if not RegGetValueNames(HKLM64, 'SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL', Names) then
    RegGetValueNames(HKLM32, 'SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL', Names);

  for i := 0 to GetArrayLength(Names) - 1 do
  begin
    if Instances <> '' then Instances := Instances + ', ';
    Instances := Instances + '.\' + Names[i];
    if CompareText(Names[i], 'SQLEXPRESS') = 0 then Preferred := Names[i];
    if Preferred = '' then Preferred := Names[i];
  end;

  if Instances = '' then
    InstancesHint.Caption := 'No local SQL Server instance was detected. Enter the server name or IP manually. ' +
      'لم يتم العثور على SQL Server محلي؛ أدخل اسم الخادم أو عنوان IP يدوياً.'
  else
  begin
    InstancesHint.Caption := 'Detected local SQL Server: ' + Instances + #13#10 +
      'تم اكتشاف SQL Server محلياً. راجع العنوان ثم تابع.';
    ServerPage.Values[0] := '.\' + Preferred;
  end;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if (PageID = LoginPage.ID) and (AuthPage.SelectedValueIndex = 0) then
    Result := True;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  // أول وصول لصفحة اختيار المكوّنات: {app} مُهيَّأ الآن بأمان (بعد
  // wpSelectDir)، فهذا أول مكان آمن لقراءة/تعبئة بيانات التثبيت السابق
  if (CurPageID = wpSelectComponents) and not AlreadyPrefilled then
  begin
    AlreadyPrefilled := True;
    PrefillFromExistingInstall;
    PreselectComponentsFromExistingInstall;
  end;

  // تنبيه بسيط عند الدخول لصفحة السيرفر، لتأكيد أن القيم المعروضة هي
  // نفس إعدادات التثبيت السابق على هذا الجهاز، وليست قيماً افتراضية فارغة
  if (CurPageID = ServerPage.ID) and DetectedExistingConfig then
    ServerPage.SubCaptionLabel.Caption :=
      'تم العثور على تثبيت سابق على هذا الجهاز — الحقول أدناه مُعبّأة تلقائياً بإعداداته الحالية. ' +
      'تحقّق منها قبل المتابعة (Detected a previous install — fields below are pre-filled from it).';
end;

function BuildConnectionString(): String;
var
  Server, User, Pass: String;
begin
  Server := ServerPage.Values[0];
  if AuthPage.SelectedValueIndex = 0 then
  begin
    // Windows Authentication
    Result := 'Data Source=' + Server + ';Initial Catalog=DentalClinicDB;' +
      'Integrated Security=True;Encrypt=False;TrustServerCertificate=True;Connect Timeout=5;';
  end
  else
  begin
    // SQL Server Authentication
    User := LoginPage.Values[0];
    Pass := LoginPage.Values[1];
    Result := 'Data Source=' + Server + ';Initial Catalog=DentalClinicDB;' +
      'User ID=' + User + ';Password=' + Pass + ';Encrypt=False;TrustServerCertificate=True;Connect Timeout=5;';
  end;
end;

function XmlEscape(Value: String): String;
begin
  Result := Value;
  StringChangeEx(Result, '&', '&amp;', True);
  StringChangeEx(Result, '<', '&lt;', True);
  StringChangeEx(Result, '>', '&gt;', True);
  StringChangeEx(Result, '"', '&quot;', True);
  StringChangeEx(Result, '''', '&apos;', True);
end;

function EscapePowerShellLiteral(Value: String): String;
begin
  Result := Value;
  StringChangeEx(Result, '''', '''''', True);
  Result := '''' + Result + '''';
end;

// اختبار فعلي عبر .NET Framework الموجود مع Windows PowerShell. نختبر master
// بدل DentalClinicDB حتى يتمكن فني التثبيت من إعداد الاتصال قبل إنشاء قاعدة العيادة.
function TestSqlServerConnection(var ErrorMessage: String): Boolean;
var
  ConnectionString, Script, ScriptFile, OutputFile: String;
  ResultCode: Integer;
  Output: AnsiString;
begin
  Result := False;
  ErrorMessage := '';
  ConnectionString := BuildConnectionString();
  StringChangeEx(ConnectionString, 'Initial Catalog=DentalClinicDB;', 'Initial Catalog=master;', True);

  ScriptFile := ExpandConstant('{tmp}\dcs_test_sql.ps1');
  OutputFile := ExpandConstant('{tmp}\dcs_test_sql.txt');
  Script := '$ErrorActionPreference = ''Stop'';' + #13#10 +
    '$outputFile = ' + EscapePowerShellLiteral(OutputFile) + ';' + #13#10 +
    '$connection = New-Object System.Data.SqlClient.SqlConnection;' + #13#10 +
    '$connection.ConnectionString = ' + EscapePowerShellLiteral(ConnectionString) + ';' + #13#10 +
    'try { $connection.Open(); $command = $connection.CreateCommand(); $command.CommandText = ''SELECT 1''; ' +
    '[void]$command.ExecuteScalar(); [System.IO.File]::WriteAllText($outputFile, ''OK''); exit 0 } ' +
    'catch { [System.IO.File]::WriteAllText($outputFile, $_.Exception.Message); exit 1 } ' +
    'finally { if ($connection) { $connection.Dispose() } }';

  SaveStringToFile(ScriptFile, Script, False);
  Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ScriptFile + '"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  if FileExists(OutputFile) then LoadStringFromFile(OutputFile, Output);
  DeleteFile(ScriptFile);
  DeleteFile(OutputFile);

  Result := ResultCode = 0;
  if not Result then
  begin
    ErrorMessage := Trim(Output);
    if ErrorMessage = '' then ErrorMessage := 'The installer could not connect to SQL Server.';
  end;
end;

function ConfirmSqlConnection(): Boolean;
var
  ErrorMessage: String;
  Choice: Integer;
begin
  Result := False;
  while True do
  begin
    if TestSqlServerConnection(ErrorMessage) then
    begin
      MsgBox('SQL Server connection succeeded. تم الاتصال بسيرفر SQL بنجاح.', mbInformation, MB_OK);
      Result := True;
      Exit;
    end;

    Choice := MsgBox('Could not connect to SQL Server:' + #13#10 + ErrorMessage + #13#10#13#10 +
      'Retry: test again.  No: continue without a successful test.  Cancel: return to the settings.' + #13#10 +
      'إعادة المحاولة: اختبار جديد. لا: المتابعة دون اختبار ناجح. إلغاء: العودة إلى الإعدادات.',
      mbError, MB_YESNOCANCEL);
    if Choice = IDYES then Continue;
    if Choice = IDNO then
    begin
      Result := True;
      Exit;
    end;
    Exit;
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;

  if (CurPageID = ServerPage.ID) and (Trim(ServerPage.Values[0]) = '') then
  begin
    MsgBox('Enter a SQL Server name or IP address first. أدخل اسم أو عنوان SQL Server أولاً.', mbError, MB_OK);
    Result := False;
    Exit;
  end;

  if (CurPageID = LoginPage.ID) and ((Trim(LoginPage.Values[0]) = '') or (LoginPage.Values[1] = '')) then
  begin
    MsgBox('Enter both the SQL Server username and password. أدخل اسم المستخدم وكلمة المرور لحساب SQL Server.', mbError, MB_OK);
    Result := False;
    Exit;
  end;

  if ((CurPageID = ServerPage.ID) and (AuthPage.SelectedValueIndex = 0)) or
     ((CurPageID = LoginPage.ID) and (AuthPage.SelectedValueIndex = 1)) then
    Result := ConfirmSqlConnection;
end;

procedure UpdateConfigConnectionString(ConfigFile: String; NewConnString: String);
var
  Content: AnsiString;
  StartMarker, EndQuote: Integer;
  Prefix, Suffix: AnsiString;
  BackupFile: String;
begin
  if not FileExists(ConfigFile) then Exit;

  // نسخة احتياطية للإعدادات القديمة قبل أي تعديل (تُستبدَل في كل تشغيل
  // جديد للـ Setup، فهي تحفظ آخر نسخة سليمة قبل التثبيت الحالي فقط)
  BackupFile := ConfigFile + '.bak';
  FileCopy(ConfigFile, BackupFile, False);

  LoadStringFromFile(ConfigFile, Content);

  StartMarker := Pos('connectionString="', Content);
  if StartMarker = 0 then Exit;
  StartMarker := StartMarker + Length('connectionString="');

  EndQuote := Pos('"', Copy(Content, StartMarker, MaxInt));
  if EndQuote = 0 then Exit;
  EndQuote := EndQuote + StartMarker - 1;

  Prefix := Copy(Content, 1, StartMarker - 1);
  Suffix := Copy(Content, EndQuote, Length(Content) - EndQuote + 1);

  Content := Prefix + XmlEscape(NewConnString) + Suffix;
  SaveStringToFile(ConfigFile, Content, False);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ConnString: String;
begin
  if CurStep = ssPostInstall then
  begin
    ConnString := BuildConnectionString();
    // النشر self-contained + PublishSingleFile يسمّي ملف الإعدادات حسب الـ DLL الداخلي
    // (NurseApp.dll.config) وليس حسب الـ exe الظاهر - نستهدف الاثنين معاً بأمان (الدالة
    // تتجاهل بصمت أي مسار غير موجود عبر FileExists)، لضمان عمل الأمر بغضّ النظر عن طريقة النشر
    UpdateConfigConnectionString(ExpandConstant('{app}\NurseApp\NurseApp.dll.config'), ConnString);
    UpdateConfigConnectionString(ExpandConstant('{app}\NurseApp\NurseApp.exe.config'), ConnString);
    UpdateConfigConnectionString(ExpandConstant('{app}\DoctorApp\DoctorApp.dll.config'), ConnString);
    UpdateConfigConnectionString(ExpandConstant('{app}\DoctorApp\DoctorApp.exe.config'), ConnString);
    UpdateConfigConnectionString(ExpandConstant('{app}\ProsthetistApp\ProsthetistApp.dll.config'), ConnString);
    UpdateConfigConnectionString(ExpandConstant('{app}\ProsthetistApp\ProsthetistApp.exe.config'), ConnString);
  end;
end;
