; Henkan setup, built with Inno Setup 6 by the Release workflow:
;   ISCC.exe /DAppVersion=0.0.1 /DPayload=<folder> /DThumbprint=<sha1> installer\Henkan.iss
;
; Henkan is an MSIX package, because the Explorer menu needs package identity.
; This setup is the friendly way to install it: it trusts the certificate the
; package is signed with, installs the Windows App Runtime and the package for
; the user who ran it, and registers an uninstaller that undoes all of that.
;
; <Payload> holds Henkan.msix, Henkan.cer and a Dependencies folder with the
; runtime's MSIX files, which the package build produces.

#ifndef AppVersion
  #error Pass /DAppVersion=x.y.z
#endif
#ifndef Payload
  #error Pass /DPayload=<folder with Henkan.msix, Henkan.cer and Dependencies>
#endif
#ifndef Thumbprint
  #error Pass /DThumbprint=<signing certificate SHA-1 thumbprint>
#endif

#define AppName "Henkan"
#define PackageName "MyDrift.Henkan"
#define AppUserModelId "MyDrift.Henkan_d4y38x12ddj8y!App"

[Setup]
AppId={{6C1F7A2E-3B8D-4E59-9A41-0D2F6B8C7E13}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=MyDrift
AppPublisherURL=https://github.com/MyDrift-user/henkan
AppSupportURL=https://github.com/MyDrift-user/henkan/issues
AppUpdatesURL=https://github.com/MyDrift-user/henkan/releases
DefaultDirName={autopf}\Henkan
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir=..\release
OutputBaseFilename=Henkan-{#AppVersion}-Setup
SetupIconFile=..\src\Henkan.App\Assets\Henkan.ico
UninstallDisplayIcon={app}\Henkan.ico
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
CloseApplications=force

[Messages]
WelcomeLabel2=This installs [name/ver], which converts files from the Explorer right-click menu.%n%nWindows will be asked once to trust Henkan's signing certificate.

[Files]
; The package goes last: installing it needs the certificate and the runtime
; beside it, and its AfterInstall does the installing. An error there rolls the
; whole setup back.
Source: "..\src\Henkan.App\Assets\Henkan.ico"; DestDir: "{app}"
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"
Source: "{#Payload}\Henkan.cer"; DestDir: "{tmp}"; Flags: deleteafterinstall
Source: "{#Payload}\Dependencies\*.msix"; DestDir: "{tmp}\Dependencies"; Flags: deleteafterinstall
Source: "{#Payload}\Henkan.msix"; DestDir: "{tmp}"; Flags: deleteafterinstall; AfterInstall: InstallPackage

[Run]
Filename: "explorer.exe"; Parameters: "shell:AppsFolder\{#AppUserModelId}"; Description: "Open Henkan"; Flags: postinstall nowait skipifsilent runasoriginaluser

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command ""Get-AppxPackage -AllUsers -Name '{#PackageName}' | Remove-AppxPackage -AllUsers"""; Flags: runhidden waituntilterminated; RunOnceId: "RemovePackage"
Filename: "certutil.exe"; Parameters: "-delstore TrustedPeople {#Thumbprint}"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveCertificate"

[Code]
function Run(const FileName, Params: String; AsOriginalUser: Boolean; var Code: Integer): Boolean;
begin
  if AsOriginalUser then
    Result := ExecAsOriginalUser(FileName, Params, '', SW_HIDE, ewWaitUntilTerminated, Code)
  else
    Result := Exec(FileName, Params, '', SW_HIDE, ewWaitUntilTerminated, Code);
  Result := Result and (Code = 0);
end;

procedure InstallPackage();
var
  Code: Integer;
  Script: String;
begin
  WizardForm.StatusLabel.Caption := 'Trusting the signing certificate...';
  if not Run('certutil.exe', '-f -addstore TrustedPeople "' + ExpandConstant('{tmp}\Henkan.cer') + '"', False, Code) then
    RaiseException('Windows did not accept Henkan''s signing certificate (code ' + IntToStr(Code) + ').');

  // As the user who started the setup, not the administrator account that may
  // have been used to elevate it: a package is registered per user.
  WizardForm.StatusLabel.Caption := 'Installing Henkan...';
  Script :=
    '$ErrorActionPreference = ''Stop''; ' +
    '$deps = @(Get-ChildItem ''' + ExpandConstant('{tmp}\Dependencies') + ''' -Filter *.msix | ForEach-Object FullName); ' +
    'try { Add-AppxPackage -Path ''' + ExpandConstant('{tmp}\Henkan.msix') + ''' -DependencyPath $deps -ForceApplicationShutdown -ForceUpdateFromAnyVersion } ' +
    'catch { $_ | Out-File ''' + ExpandConstant('{tmp}\install.log') + '''; exit 1 }';

  if not Run('powershell.exe', '-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "' + Script + '"', True, Code) then
    RaiseException('Henkan could not be installed (code ' + IntToStr(Code) + '). If an older test version of Henkan is installed, remove it under Settings > Apps first, then run this setup again.');
end;
