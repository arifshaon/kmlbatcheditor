# Packaging KML Scoped Editor for Windows

The packaging scripts create a self-contained Windows x64 application, a portable ZIP, and an optional `Setup.exe` installer. Recipients do not need Visual Studio or a separate .NET installation.

The current release is **version 1.1.0**. It includes:

- custom placemark icon selection;
- automatic icon bundling inside KMZ output;
- built-in current-versus-proposed placemark preview;
- temporary Google Earth preview packages;
- the existing folder, icon-image, and icon-variant batch-editing features.

## Folder layout

The files should be arranged as follows in the folder containing `KmlScopedEditor.sln`:

```text
KmlScopedEditor/
├── KmlScopedEditor.sln
├── KmlScopedEditor/
│   └── KmlScopedEditor.csproj
├── Installer/
│   └── KmlScopedEditor.iss
├── Build-Release.ps1
├── Build-Installer.cmd
├── Build-Portable.cmd
└── PACKAGING.md
```

## Build-computer requirements

- Windows 10 or Windows 11, 64-bit;
- .NET 8 SDK or later;
- Inno Setup 6, required only for `Setup.exe`.

Install Inno Setup from Terminal or PowerShell:

```powershell
winget install --id JRSoftware.InnoSetup -e
```

## Before building

Pull the latest `main` branch and remove previous build output:

```powershell
git pull origin main
dotnet clean .\KmlScopedEditor.sln
```

The release version is read from:

```text
KmlScopedEditor\KmlScopedEditor.csproj
```

For version 1.1.0 the project contains:

```xml
<Version>1.1.0</Version>
<FileVersion>1.1.0.0</FileVersion>
<AssemblyVersion>1.1.0.0</AssemblyVersion>
```

## Build the installer

Double-click:

```text
Build-Installer.cmd
```

Or run:

```powershell
powershell -ExecutionPolicy Bypass -File .\Build-Release.ps1 -Installer
```

The script:

1. publishes the latest application source as a self-contained Windows x64 build;
2. creates a portable ZIP;
3. passes the version from the project file to Inno Setup;
4. compiles the Windows installer.

Outputs for version 1.1.0:

```text
artifacts\publish\win-x64\KmlScopedEditor.exe
artifacts\portable\KML-Scoped-Editor-Portable-1.1.0-win-x64.zip
artifacts\installer\KML-Scoped-Editor-Setup-1.1.0.exe
```

## Build only the portable version

Double-click:

```text
Build-Portable.cmd
```

Or run:

```powershell
powershell -ExecutionPolicy Bypass -File .\Build-Release.ps1
```

This does not require Inno Setup.

## Upgrading an existing installation

Version 1.1.0 uses the same permanent installer `AppId` as version 1.0.0:

```text
{7F5C89B1-E9E9-438F-AF80-5057DD72C1F0}
```

Do not change this value in `Installer\KmlScopedEditor.iss`. Keeping the `AppId` unchanged allows the version 1.1.0 installer to upgrade the existing application in place rather than creating a second installation.

Users can normally run `KML-Scoped-Editor-Setup-1.1.0.exe` directly over version 1.0.0. The installer reuses the previous installation directory and replaces the packaged application files.

## Installation scope

The installer uses a per-user installation and does not require administrator privileges. The default location is:

```text
%LOCALAPPDATA%\Programs\KML Scoped Editor
```

It creates a Start-menu shortcut and offers an optional desktop shortcut. An uninstaller is registered in Windows Settings.

## Verification after installation

After installing version 1.1.0:

1. open KML Scoped Editor;
2. load a KML or KMZ file;
3. calculate a placemark selection;
4. confirm that **Change icon image** and **Browse** appear under **Batch Style Edit**;
5. select an icon and run **Preview Changes**;
6. confirm that the current/proposed preview appears;
7. save a KMZ and confirm that the selected icon is included in the package.

## Windows security warning

The application and installer are not digitally signed. Microsoft Defender SmartScreen may show an **Unknown publisher** warning, especially after downloading the file from the internet. Public distribution should use a trusted code-signing certificate.

## CPU support

The scripts currently target 64-bit Intel/AMD Windows (`win-x64`). Windows on ARM would require a separate `win-arm64` publish and installer build.
