# KML Batch Editor

A Windows desktop application for safely batch-editing placemark styles in Google Earth KML and KMZ files.

## Features

- Open KML and KMZ files
- Browse folders and placemarks in a hierarchical tree
- Select placemarks by folder, icon image, or icon variant
- Edit icon size, icon color, text size, and text color independently
- Preview changes before applying them
- Preserve unselected KML properties
- Handle inline styles, shared styles, and StyleMaps safely
- Preserve embedded KMZ resources when saving
- Responsive background processing with progress and cancellation

## Technology

- C#
- .NET 8
- WPF
- Windows 11+

## Build

Open `KmlScopedEditor.sln` in Visual Studio 2022 with the **.NET desktop development** workload installed. Build the solution and run the `KmlScopedEditor` project.
