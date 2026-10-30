# Packaging Simple KML Editor for Windows

The packaging scripts create a self-contained Windows x64 application, a portable ZIP, and a Windows Setup installer. Recipients do not need Visual Studio or a separate .NET installation.

The current tester release is **version 1.2.0**.

It includes the current Simple KML Editor interface, placemark style editing, the Line Editor, additive pending split lines, line simplification/smoothing with topology warnings, chronological undo, tree tick preservation, splash screen, application menu, About dialog, and contextual tooltips.

## Build-computer requirements

- Windows 10 or Windows 11, 64-bit;
- .NET 8 SDK or later;
- Inno Setup 6, required for the Setup installer.

Install Inno Setup with:

```powershell
winget install --id JRSoftware.InnoSetup -e
```

## Version

The release version is read from:

```text
KmlScopedEditor\KmlScopedEditor.csproj
```

For version 1.2.0:

```xml
<Version>1.2.0</Version>
<FileVersion>1.2.0.0</FileVersion>
<AssemblyVersion>1.2.0.0</AssemblyVersion>
```

## Build the installer locally

Pull the latest `main` branch and clean previous output:

```powershell
git pull origin main
dotnet clean .\KmlScopedEditor.sln
```

Then double-click:

```text
Build-Installer.cmd
```

or run:

```powershell
powershell -ExecutionPolicy Bypass -File .\Build-Release.ps1 -Installer
```

Outputs:

```text
artifacts\publish\win-x64\KmlScopedEditor.exe
artifacts\portable\Simple-KML-Editor-Portable-1.2.0-win-x64.zip
artifacts\installer\Simple-KML-Editor-Setup-1.2.0.exe
```

## GitHub Actions tester build

The workflow:

```text
.github\workflows\windows-release.yml
```

builds the same Windows packages on a Windows runner and uploads:

- `Simple-KML-Editor-Setup-1.2.0`
- `Simple-KML-Editor-Portable-1.2.0-win-x64`

as downloadable workflow artifacts.

## Upgrading an existing installation

The installer keeps the existing permanent `AppId`:

```text
{7F5C89B1-E9E9-438F-AF80-5057DD72C1F0}
```

Do not change this value in `Installer\KmlScopedEditor.iss`. Keeping it stable allows Simple KML Editor 1.2.0 to upgrade earlier KML Scoped Editor installations instead of creating a separate application entry.

For a fresh installation, the default location is:

```text
%LOCALAPPDATA%\Programs\Simple KML Editor
```

The installer runs per-user and does not require administrator privileges. It creates a Start-menu shortcut and offers an optional desktop shortcut.

## Suggested tester checks

After installing version 1.2.0:

1. confirm the splash screen appears and the main window opens as **Simple KML Editor**;
2. open a KML/KMZ file and confirm the File/Help menus and button tooltips;
3. open **Help > About Simple KML Editor** and confirm the developer name, GitHub link and Close button are fully visible;
4. confirm tree ticking uses a normal two-click tick/untick cycle;
5. open Line Editor and test pending split lines, Apply split and Undo;
6. test **Simplify / smooth...** in Step 1 and Step 3;
7. save a KML/KMZ file and reopen it.

## Windows security warning

The application and installer are currently unsigned. Microsoft Defender SmartScreen may show an **Unknown publisher** warning after download. Public distribution should use a trusted code-signing certificate.

## CPU support

The current packages target 64-bit Intel/AMD Windows (`win-x64`). A separate `win-arm64` publish would be required for native Windows on ARM.
