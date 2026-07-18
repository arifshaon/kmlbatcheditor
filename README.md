# KML Scoped Editor

A Windows desktop application for safely batch-editing placemark styles in Google Earth **KML** and **KMZ** files.

KML Scoped Editor lets you select placemarks by folder, icon image, or icon variant, then change one or more style properties without deliberately changing unrelated placemark data or style settings.

The application is built with **C#**, **.NET 8**, and **WPF**.

---

## What the application does

KML Scoped Editor is designed for controlled changes across large groups of Google Earth placemarks.

You can change:

- icon size;
- icon colour;
- text or label size;
- text or label colour.

Each property is enabled separately. If a property is not selected, the application leaves that property unchanged.

For example, you can change the text colour of every placemark in a folder while preserving each placemark's existing icon image, icon size, icon colour, text size, coordinates, name, description, timestamps, and other KML content.

---

## Main features

- Opens both `.kml` and `.kmz` files.
- Displays the document, folders, subfolders, placemarks, overlays, and network links in a hierarchical tree.
- Selects placemarks by:
  - folder;
  - icon image;
  - icon variant.
- Optionally includes placemarks in nested subfolders.
- Calculates and displays the number of matching placemarks before editing.
- Changes icon size, icon colour, text size, and text colour independently or together.
- Previews the proposed changes before applying them.
- Shows style diagnostics for an individual placemark.
- Handles inline styles, shared styles, and KML `StyleMap` references.
- Clones shared styles where necessary to avoid changing placemarks outside the selected scope.
- Preserves embedded files and images when saving a KMZ package.
- Shows progress and a central loading overlay during longer operations.
- Supports cancellation where an operation can be stopped safely.
- Displays clear success, warning, and error notifications.
- Saves the edited document as a new KML or KMZ file.

---

## Selection methods

### Selected folder

Choose a folder in the tree and apply the operation to placemarks within that folder.

You can choose whether to include placemarks contained in nested subfolders.

This is useful when the KML is organised geographically, thematically, administratively, or by project.

### Icon image

Groups placemarks by the resolved icon image or icon URL.

For example, every placemark using the same `square.png` image can be selected together, even when those placemarks use different colours or sizes.

### Icon variant

Groups placemarks by the combination of:

- icon image;
- effective icon colour;
- effective icon size.

This is more precise than selecting by icon image alone. It is useful when the same icon image appears in several colours or sizes.

Text size and text colour are intentionally not part of the icon-variant definition, so a selected icon variant can be used to standardise its labels.

---

## Editable style properties

| Editor option | KML property |
|---|---|
| Icon size | `IconStyle/scale` |
| Icon colour | `IconStyle/color` |
| Text size | `LabelStyle/scale` |
| Text colour | `LabelStyle/color` |

The application changes only the options that are explicitly enabled.

### Size values

KML sizes are scale values. Common examples include:

- `0.8` — smaller than normal;
- `1` — normal scale;
- `1.5` — larger than normal.

### Colour values

Colours can be entered as:

```text
#RRGGBB
#AARRGGBB
```

Examples:

```text
#FF0000     red
#FFFFFF     white
#80FF0000   red with partial transparency
```

KML internally stores colours in `AABBGGRR` order. The application performs this conversion automatically.

---

## Safe style handling

KML placemarks can obtain their appearance from several places:

- an inline `<Style>` inside the placemark;
- a shared `<Style id="...">`;
- a `<styleUrl>` reference;
- a `<StyleMap>` containing normal and highlight styles;
- a combination of a shared style and inline overrides.

A shared style may be used by placemarks both inside and outside the selected folder or icon group. Editing that shared style directly could therefore change unrelated placemarks.

KML Scoped Editor uses scoped style handling. Where necessary, it:

1. copies the referenced style or StyleMap;
2. assigns the copy a new unique identifier;
3. changes only the enabled properties;
4. points the selected placemarks to the copied style;
5. leaves the original shared style available to unselected placemarks.

The preview reports the expected number of inline-style updates, shared-style clones, StyleMap clones, new inline styles, and unresolved references before the operation is applied.

---

## Style diagnostics

Select an individual placemark in the tree to inspect its resolved style.

The diagnostics panel displays:

- style source;
- `styleUrl`;
- normal style reference;
- highlight style reference;
- whether an inline style exists;
- icon URL;
- icon size;
- icon colour;
- text size;
- text colour.

`Not explicitly set` means the KML does not define that property at the inspected level. Google Earth may therefore use its default value.

---

## Basic workflow

1. Start **KML Scoped Editor**.
2. Select **Open KML/KMZ**.
3. Choose a `.kml` or `.kmz` file.
4. Browse the hierarchy in the tree on the left.
5. Choose a selection method:
   - Selected folder;
   - Icon image;
   - Icon variant.
6. Select the relevant folder or icon groups.
7. Select **Calculate Selection**.
8. Confirm the number of matched placemarks.
9. Enable only the properties that need to change.
10. Enter the new size or colour values.
11. Select **Preview Changes**.
12. Review the proposed changes and style operations.
13. Select **Apply Changes**.
14. Select **Save As** and save the result as a new KML or KMZ file.
15. Open the saved file in Google Earth and verify the result.

Keeping the original file and using **Save As** is strongly recommended.

---

## KML and KMZ support

### KML

A KML file is an XML document. The application loads the document, applies changes in memory, and saves the updated XML to the selected output path.

### KMZ

A KMZ file is a ZIP-based package containing a KML document and, potentially, images or other embedded resources.

When saving KMZ output, the application preserves the package resources and rebuilds the package with the updated KML document.

---

## Installation for end users

The project includes scripts for creating:

- a self-contained Windows application;
- a portable ZIP package;
- a Windows Setup executable.

A self-contained build does not require the recipient to install Visual Studio or a separate .NET runtime.

The installer:

- installs per user;
- does not require administrator privileges;
- installs by default under `%LOCALAPPDATA%\Programs\KML Scoped Editor`;
- creates a Start-menu shortcut;
- optionally creates a desktop shortcut;
- includes an uninstaller;
- uses a stable application ID so future installers can upgrade the existing installation.

The application and installer are currently unsigned. Windows Defender SmartScreen may display an **Unknown publisher** warning, particularly for a file downloaded from the internet.

---

## System requirements

### Running the packaged application

- 64-bit Windows 10 or Windows 11;
- Intel or AMD x64 processor.

The current packaging scripts target `win-x64`. A separate `win-arm64` build would be required for native Windows on ARM distribution.

### Building from source

- Windows 10 or Windows 11;
- Visual Studio 2022 with the **.NET desktop development** workload, or the .NET 8 SDK command-line tools;
- .NET 8 SDK or later;
- Inno Setup 6 only when creating `Setup.exe`.

---

## Build and run in Visual Studio

1. Clone or download this repository.
2. Open `KmlScopedEditor.sln` in Visual Studio 2022.
3. Confirm that the **.NET desktop development** workload is installed.
4. Select **Build → Build Solution**.
5. Run using **Debug → Start Without Debugging** or `Ctrl+F5`.

The project targets:

```text
.NET 8 for Windows
WPF
C#
```

---

## Build from the command line

From the repository root:

```powershell
dotnet build .\KmlScopedEditor.sln --configuration Release
```

To run the project directly:

```powershell
dotnet run --project .\KmlScopedEditor\KmlScopedEditor.csproj
```

---

## Create the portable package

Double-click:

```text
Build-Portable.cmd
```

Or run:

```powershell
powershell -ExecutionPolicy Bypass -File .\Build-Release.ps1
```

This creates a self-contained published application and portable ZIP. Inno Setup is not required.

Example outputs for version `1.0.0`:

```text
artifacts\publish\win-x64\KmlScopedEditor.exe
artifacts\portable\KML-Scoped-Editor-Portable-1.0.0-win-x64.zip
```

---

## Create the Windows installer

Install Inno Setup 6 if it is not already installed:

```powershell
winget install --id JRSoftware.InnoSetup -e
```

Then double-click:

```text
Build-Installer.cmd
```

Or run:

```powershell
powershell -ExecutionPolicy Bypass -File .\Build-Release.ps1 -Installer
```

Example installer output for version `1.0.0`:

```text
artifacts\installer\KML-Scoped-Editor-Setup-1.0.0.exe
```

See [PACKAGING.md](PACKAGING.md) for detailed packaging information.

---

## Versioning

The application version is defined in:

```text
KmlScopedEditor\KmlScopedEditor.csproj
```

Current values:

```xml
<Version>1.0.0</Version>
<FileVersion>1.0.0.0</FileVersion>
<AssemblyVersion>1.0.0.0</AssemblyVersion>
```

Update these values before building a new release.

Do not change the installer `AppId` in `Installer\KmlScopedEditor.iss`. Keeping the same AppId allows a newer installer to upgrade the existing installation instead of creating a separate application entry.

---

## Project structure

```text
kmlbatcheditor/
├── KmlScopedEditor.sln
├── KmlScopedEditor/
│   ├── Models/                 application data models
│   ├── Services/               KML/KMZ loading, selection, style and editing logic
│   ├── ViewModels/             WPF view models and commands
│   ├── App.xaml
│   ├── MainWindow.xaml
│   └── KmlScopedEditor.csproj
├── Installer/
│   └── KmlScopedEditor.iss     Inno Setup configuration
├── Build-Release.ps1           publish and packaging script
├── Build-Installer.cmd         builds portable output and Setup.exe
├── Build-Portable.cmd          builds portable output only
├── PACKAGING.md                detailed packaging guide
└── README.md
```

---

## Important limitations

- The application is a standalone Windows editor, not a Google Earth plug-in.
- It currently edits four style properties: icon size, icon colour, text size, and text colour.
- It does not currently provide multi-step undo. Keep the source file and save output under a new name.
- The current distribution target is Windows x64.
- The installer is not digitally signed.
- Very large files may require additional time and memory while parsing, resolving styles, previewing, or rebuilding KMZ output.

---

## Troubleshooting

### The WPF project template or build tools are missing

Open **Visual Studio Installer**, select **Modify**, and install the **.NET desktop development** workload.

Confirm the installed SDKs in PowerShell:

```powershell
dotnet --list-sdks
```

A version beginning with `8.0` or later is required.

### The installer build cannot find Inno Setup

Install it with:

```powershell
winget install --id JRSoftware.InnoSetup -e
```

Then close and reopen the terminal before running `Build-Installer.cmd` again.

### No placemarks are matched

- Confirm that the intended folder is selected in the tree.
- Confirm whether nested subfolders should be included.
- When selecting by icon, tick at least one icon image or icon variant.
- Select **Calculate Selection** again after changing the scope.

### A property shows “Not explicitly set”

The KML does not explicitly define that property in the resolved style. Google Earth may be using its default.

### Windows displays an Unknown publisher warning

The installer and application are currently unsigned. Verify that the file came from the expected project source before running it.

---

## Reporting problems

When reporting a problem, include:

- whether the input was KML or KMZ;
- the application version;
- the selection method used;
- the property or properties being changed;
- the exact error message;
- a small sample file when it is safe to share one.

Do not upload confidential or restricted geographic data to a public issue.

---

## Author

Developed by **Arif Shaon**.
