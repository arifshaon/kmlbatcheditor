# Packaging KML Scoped Editor for Windows

The packaging scripts create a self-contained Windows x64 application, a portable ZIP, and an optional Setup.exe installer. Recipients do not need Visual Studio or a separate .NET installation.

## Folder layout

Place the files as follows in the folder containing `KmlScopedEditor.sln`:

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

- Windows 10 or Windows 11, 64-bit
- .NET 8 SDK or later
- Inno Setup 6, required only for Setup.exe

Install Inno Setup from Terminal or PowerShell:

```powershell
winget install --id JRSoftware.InnoSetup -e
```

## Build the installer

Double-click:

```text
Build-Installer.cmd
```

The script first publishes a self-contained application and creates a portable ZIP. It then compiles the installer with Inno Setup.

Outputs:

```text
artifacts\publish\win-x64\KmlScopedEditor.exe
artifacts\portable\KML-Scoped-Editor-Portable-1.0.0-win-x64.zip
artifacts\installer\KML-Scoped-Editor-Setup-1.0.0.exe
```

## Build only the portable version

Double-click:

```text
Build-Portable.cmd
```

This does not require Inno Setup.

## Versioning

The build script reads the application version from:

```text
KmlScopedEditor\KmlScopedEditor.csproj
```

For a new release, update:

```xml
<Version>1.0.0</Version>
<FileVersion>1.0.0.0</FileVersion>
<AssemblyVersion>1.0.0.0</AssemblyVersion>
```

For example, change all three to `1.1.0` / `1.1.0.0` for the next release.

Do not change the `AppId` in `Installer\KmlScopedEditor.iss`. A stable AppId allows a later installer to upgrade the existing installation instead of creating a second application.

## Installation scope

The installer uses a per-user installation and does not require administrator privileges. The default location is:

```text
%LOCALAPPDATA%\Programs\KML Scoped Editor
```

It creates a Start-menu shortcut and offers an optional desktop shortcut.

## Windows security warning

The application and installer are not digitally signed. Microsoft Defender SmartScreen may show an `Unknown publisher` warning, especially after downloading the file from the internet. Public distribution should use a trusted code-signing certificate.

## CPU support

The scripts currently target 64-bit Intel/AMD Windows (`win-x64`). Windows on ARM would require a separate `win-arm64` publish and installer build.
