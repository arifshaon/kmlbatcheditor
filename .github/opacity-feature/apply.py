from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def replace_once(path: Path, old: str, new: str, label: str) -> None:
    text = path.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise RuntimeError(
            f"{label}: expected exactly one matching block in {path}, found {count}."
        )
    path.write_text(text.replace(old, new, 1), encoding="utf-8")
    print(f"Updated {label}: {path.relative_to(ROOT)}")


batch = ROOT / "KmlScopedEditor" / "Services" / "KmlBatchEditService.cs"
preview = ROOT / "KmlScopedEditor" / "Services" / "KmlPreviewService.cs"
window = ROOT / "KmlScopedEditor" / "MainWindow.xaml"

replace_once(
    batch,
    '''        string? iconScale = null;
        string? labelScale = null;
        string? iconColor = null;
        string? labelColor = null;
''',
    '''        string? iconScale = null;
        string? labelScale = null;
        string? iconBgr = null;
        string? labelBgr = null;
        string? iconAlpha = null;
        string? labelAlpha = null;
        string? iconColorDisplay = null;
        string? labelColorDisplay = null;
        string? iconOpacityDisplay = null;
        string? labelOpacityDisplay = null;
''',
    "batch change variables",
)

replace_once(
    batch,
    '''        if (settings.ChangeIconColor &&
            !TryConvertDisplayColorToKml(
                settings.IconColorText,
                "Icon color",
                out iconColor,
                out error))
        {
            return false;
        }

        if (settings.ChangeLabelColor &&
            !TryConvertDisplayColorToKml(
                settings.LabelColorText,
                "Text color",
                out labelColor,
                out error))
        {
            return false;
        }

        changes = new StyleChangeSet
        {
            IconHref = iconHref,
            IconFileName = iconFileName,
            IconScale = iconScale,
            IconColor = iconColor,
            LabelScale = labelScale,
            LabelColor = labelColor,
            IconColorDisplay = settings.ChangeIconColor
                ? NormalizeDisplayColor(settings.IconColorText)
                : null,
            LabelColorDisplay = settings.ChangeLabelColor
                ? NormalizeDisplayColor(settings.LabelColorText)
                : null
        };
''',
    '''        if (settings.ChangeIconColor &&
            !KmlColorUtility.TryParseDisplayRgb(
                settings.IconColorText,
                "Icon color",
                out iconBgr,
                out iconColorDisplay,
                out error))
        {
            return false;
        }

        if (settings.ChangeIconOpacity &&
            !KmlColorUtility.TryParseOpacityPercent(
                settings.IconOpacityText,
                "Icon opacity",
                out iconAlpha,
                out iconOpacityDisplay,
                out error))
        {
            return false;
        }

        if (settings.ChangeLabelColor &&
            !KmlColorUtility.TryParseDisplayRgb(
                settings.LabelColorText,
                "Text color",
                out labelBgr,
                out labelColorDisplay,
                out error))
        {
            return false;
        }

        if (settings.ChangeLabelOpacity &&
            !KmlColorUtility.TryParseOpacityPercent(
                settings.LabelOpacityText,
                "Text opacity",
                out labelAlpha,
                out labelOpacityDisplay,
                out error))
        {
            return false;
        }

        changes = new StyleChangeSet
        {
            IconHref = iconHref,
            IconFileName = iconFileName,
            IconScale = iconScale,
            IconBgr = iconBgr,
            IconAlpha = iconAlpha,
            LabelScale = labelScale,
            LabelBgr = labelBgr,
            LabelAlpha = labelAlpha,
            IconColorDisplay = iconColorDisplay,
            LabelColorDisplay = labelColorDisplay,
            IconOpacityDisplay = iconOpacityDisplay,
            LabelOpacityDisplay = labelOpacityDisplay
        };
''',
    "batch validation and change set",
)

replace_once(
    batch,
    '''    private static bool TryConvertDisplayColorToKml(
        string? input,
        string propertyName,
        out string? kmlColor,
        out string error)
    {
        kmlColor = null;
        error = string.Empty;

        var value = input?.Trim().TrimStart('#');

        if (string.IsNullOrWhiteSpace(value) ||
            (value.Length != 6 && value.Length != 8) ||
            value.Any(character => !Uri.IsHexDigit(character)))
        {
            error =
                $"{propertyName} must use #RRGGBB or #AARRGGBB format.";
            return false;
        }

        value = value.ToUpperInvariant();

        var alpha = value.Length == 8 ? value[..2] : "FF";
        var rgbStart = value.Length == 8 ? 2 : 0;
        var red = value.Substring(rgbStart, 2);
        var green = value.Substring(rgbStart + 2, 2);
        var blue = value.Substring(rgbStart + 4, 2);

        // KML stores colours in AABBGGRR order.
        kmlColor = $"{alpha}{blue}{green}{red}".ToLowerInvariant();
        return true;
    }

    private static string NormalizeDisplayColor(string? input)
    {
        var value = input?.Trim().TrimStart('#').ToUpperInvariant() ?? string.Empty;
        return $"#{value}";
    }

''',
    "",
    "remove old combined colour conversion",
)

replace_once(
    batch,
    '''        if (changes.IconColor is not null)
            lines.Add($"• Icon color → {changes.IconColorDisplay}");

        if (changes.LabelScale is not null)
            lines.Add($"• Text size → {changes.LabelScale}");

        if (changes.LabelColor is not null)
            lines.Add($"• Text color → {changes.LabelColorDisplay}");
''',
    '''        if (changes.IconBgr is not null)
            lines.Add($"• Icon color → {changes.IconColorDisplay}");

        if (changes.IconAlpha is not null)
            lines.Add($"• Icon opacity → {changes.IconOpacityDisplay}");

        if (changes.LabelScale is not null)
            lines.Add($"• Text size → {changes.LabelScale}");

        if (changes.LabelBgr is not null)
            lines.Add($"• Text color → {changes.LabelColorDisplay}");

        if (changes.LabelAlpha is not null)
            lines.Add($"• Text opacity → {changes.LabelOpacityDisplay}");
''',
    "batch summary",
)

replace_once(
    batch,
    '''        public string? IconScale { get; init; }

        public string? IconColor { get; init; }

        public string? LabelScale { get; init; }

        public string? LabelColor { get; init; }

        public string? IconColorDisplay { get; init; }

        public string? LabelColorDisplay { get; init; }
''',
    '''        public string? IconScale { get; init; }

        public string? IconBgr { get; init; }

        public string? IconAlpha { get; init; }

        public string? LabelScale { get; init; }

        public string? LabelBgr { get; init; }

        public string? LabelAlpha { get; init; }

        public string? IconColorDisplay { get; init; }

        public string? LabelColorDisplay { get; init; }

        public string? IconOpacityDisplay { get; init; }

        public string? LabelOpacityDisplay { get; init; }
''',
    "style change set fields",
)

replace_once(
    batch,
    '''        if (changes.IconHref is not null ||
            changes.IconColor is not null ||
            changes.IconScale is not null)
''',
    '''        if (changes.IconHref is not null ||
            changes.IconBgr is not null ||
            changes.IconAlpha is not null ||
            changes.IconScale is not null)
''',
    "icon style condition",
)

replace_once(
    batch,
    '''            if (changes.IconColor is not null)
            {
                SetOrderedChildValue(
                    iconStyle,
                    "color",
                    changes.IconColor,
                    "color",
                    "colorMode",
                    "scale",
                    "heading",
                    "Icon",
                    "hotSpot");
            }
''',
    '''            if (changes.IconBgr is not null || changes.IconAlpha is not null)
            {
                var existingColor = iconStyle.Element(KmlNs + "color")?.Value;
                var combinedColor = KmlColorUtility.Combine(
                    existingColor,
                    changes.IconBgr,
                    changes.IconAlpha);

                SetOrderedChildValue(
                    iconStyle,
                    "color",
                    combinedColor,
                    "color",
                    "colorMode",
                    "scale",
                    "heading",
                    "Icon",
                    "hotSpot");
            }
''',
    "icon colour application",
)

replace_once(
    batch,
    '''        if (changes.LabelColor is not null ||
            changes.LabelScale is not null)
''',
    '''        if (changes.LabelBgr is not null ||
            changes.LabelAlpha is not null ||
            changes.LabelScale is not null)
''',
    "label style condition",
)

replace_once(
    batch,
    '''            if (changes.LabelColor is not null)
            {
                SetOrderedChildValue(
                    labelStyle,
                    "color",
                    changes.LabelColor,
                    "color",
                    "colorMode",
                    "scale");
            }
''',
    '''            if (changes.LabelBgr is not null || changes.LabelAlpha is not null)
            {
                var existingColor = labelStyle.Element(KmlNs + "color")?.Value;
                var combinedColor = KmlColorUtility.Combine(
                    existingColor,
                    changes.LabelBgr,
                    changes.LabelAlpha);

                SetOrderedChildValue(
                    labelStyle,
                    "color",
                    combinedColor,
                    "color",
                    "colorMode",
                    "scale");
            }
''',
    "label colour application",
)

replace_once(
    preview,
    '''        var proposedIconColor = settings.ChangeIconColor
            ? DisplayColorToKml(settings.IconColorText)
            : currentIconColor;

        var proposedLabelColor = settings.ChangeLabelColor
            ? DisplayColorToKml(settings.LabelColorText)
            : currentLabelColor;
''',
    '''        var proposedIconColor = BuildProposedColor(
            currentIconColor,
            settings.ChangeIconColor,
            settings.IconColorText,
            settings.ChangeIconOpacity,
            settings.IconOpacityText);

        var proposedLabelColor = BuildProposedColor(
            currentLabelColor,
            settings.ChangeLabelColor,
            settings.LabelColorText,
            settings.ChangeLabelOpacity,
            settings.LabelOpacityText);
''',
    "preview proposed colours",
)

replace_once(
    preview,
    '''            $"Icon colour: {KmlColorToDisplay(iconColor)}",
            $"Text size: {labelScaleText}",
            $"Text colour: {KmlColorToDisplay(labelColor)}");
''',
    '''            $"Icon colour: {KmlColorUtility.ToDisplayRgb(iconColor)}",
            $"Icon opacity: {KmlColorUtility.ToOpacityDisplay(iconColor)}",
            $"Text size: {labelScaleText}",
            $"Text colour: {KmlColorUtility.ToDisplayRgb(labelColor)}",
            $"Text opacity: {KmlColorUtility.ToOpacityDisplay(labelColor)}");
''',
    "preview details",
)

replace_once(
    preview,
    '''    private static string NormalizeKmlColor(string? value)
    {
        var color = value?.Trim().TrimStart('#');

        return color is { Length: 8 } && color.All(Uri.IsHexDigit)
            ? color.ToLowerInvariant()
            : "ffffffff";
    }

    private static string DisplayColorToKml(string? value)
    {
        var color = value?.Trim().TrimStart('#') ?? string.Empty;

        if (color.Length != 6 && color.Length != 8)
            return "ffffffff";

        var alpha = color.Length == 8 ? color[..2] : "FF";
        var rgbStart = color.Length == 8 ? 2 : 0;
        var red = color.Substring(rgbStart, 2);
        var green = color.Substring(rgbStart + 2, 2);
        var blue = color.Substring(rgbStart + 4, 2);

        return $"{alpha}{blue}{green}{red}".ToLowerInvariant();
    }
''',
    '''    private static string BuildProposedColor(
        string currentKmlColor,
        bool changeColor,
        string? displayColor,
        bool changeOpacity,
        string? opacityText)
    {
        string? bgr = null;
        string? alpha = null;

        if (changeColor)
        {
            KmlColorUtility.TryParseDisplayRgb(
                displayColor,
                "Colour",
                out bgr,
                out _,
                out _);
        }

        if (changeOpacity)
        {
            KmlColorUtility.TryParseOpacityPercent(
                opacityText,
                "Opacity",
                out alpha,
                out _,
                out _);
        }

        return KmlColorUtility.Combine(currentKmlColor, bgr, alpha);
    }

    private static string NormalizeKmlColor(string? value)
    {
        return KmlColorUtility.NormalizeKmlColor(value);
    }
''',
    "preview colour helpers",
)

replace_once(
    preview,
    '''    private static string KmlColorToDisplay(string kmlColor)
    {
        var color = NormalizeKmlColor(kmlColor).ToUpperInvariant();
        return $"#{color[..2]}{color.Substring(6, 2)}{color.Substring(4, 2)}{color.Substring(2, 2)}";
    }

''',
    "",
    "remove old preview display conversion",
)

replace_once(
    window,
    '''                                    <RowDefinition Height="Auto" />
                                    <RowDefinition Height="Auto" />
                                    <RowDefinition Height="Auto" />
                                    <RowDefinition Height="Auto" />
                                    <RowDefinition Height="Auto" />
                                </Grid.RowDefinitions>
''',
    '''                                    <RowDefinition Height="Auto" />
                                    <RowDefinition Height="Auto" />
                                    <RowDefinition Height="Auto" />
                                    <RowDefinition Height="Auto" />
                                    <RowDefinition Height="Auto" />
                                    <RowDefinition Height="Auto" />
                                    <RowDefinition Height="Auto" />
                                </Grid.RowDefinitions>
''',
    "UI row definitions",
)

replace_once(
    window,
    '''                                         ToolTip="Use #RRGGBB or #AARRGGBB" />
                                <Button Grid.Row="2"
''',
    '''                                         ToolTip="Use #RRGGBB. Existing opacity is preserved unless Change icon opacity is enabled." />
                                <Button Grid.Row="2"
''',
    "icon colour tooltip",
)

replace_once(
    window,
    '''                                <CheckBox Grid.Row="3"
                                          Grid.Column="0"
                                          Content="Change text size"
                                          IsChecked="{Binding EditSettings.ChangeLabelScale}"
                                          Margin="0,5,10,5" />
                                <TextBox Grid.Row="3"
                                         Grid.Column="1"
                                         Text="{Binding EditSettings.LabelScaleText, UpdateSourceTrigger=PropertyChanged}"
                                         IsEnabled="{Binding EditSettings.ChangeLabelScale}"
                                         Margin="0,3,0,3"
                                         Padding="5"
                                         ToolTip="KML label scale, for example 0.8, 1 or 1.5" />

                                <CheckBox Grid.Row="4"
                                          Grid.Column="0"
                                          Content="Change text colour"
                                          IsChecked="{Binding EditSettings.ChangeLabelColor}"
                                          Margin="0,5,10,5" />
                                <TextBox Grid.Row="4"
                                         Grid.Column="1"
                                         Text="{Binding EditSettings.LabelColorText, UpdateSourceTrigger=PropertyChanged}"
                                         IsEnabled="{Binding EditSettings.ChangeLabelColor}"
                                         Margin="0,3,6,3"
                                         Padding="5"
                                         ToolTip="Use #RRGGBB or #AARRGGBB" />
                                <Button Grid.Row="4"
                                         Grid.Column="2"
                                         Content="Choose..."
                                         Command="{Binding PickLabelColorCommand}"
                                         IsEnabled="{Binding EditSettings.ChangeLabelColor}"
                                         MinWidth="78"
                                         Margin="0,3,0,3" />
''',
    '''                                <CheckBox Grid.Row="3"
                                          Grid.Column="0"
                                          Content="Change icon opacity"
                                          IsChecked="{Binding EditSettings.ChangeIconOpacity}"
                                          Margin="0,5,10,5" />
                                <TextBox Grid.Row="3"
                                         Grid.Column="1"
                                         Text="{Binding EditSettings.IconOpacityText, UpdateSourceTrigger=PropertyChanged}"
                                         IsEnabled="{Binding EditSettings.ChangeIconOpacity}"
                                         Margin="0,3,6,3"
                                         Padding="5"
                                         ToolTip="Enter a percentage from 0 to 100" />
                                <TextBlock Grid.Row="3"
                                           Grid.Column="2"
                                           Text="%"
                                           VerticalAlignment="Center"
                                           Margin="6,0,0,0" />

                                <CheckBox Grid.Row="4"
                                          Grid.Column="0"
                                          Content="Change text size"
                                          IsChecked="{Binding EditSettings.ChangeLabelScale}"
                                          Margin="0,5,10,5" />
                                <TextBox Grid.Row="4"
                                         Grid.Column="1"
                                         Text="{Binding EditSettings.LabelScaleText, UpdateSourceTrigger=PropertyChanged}"
                                         IsEnabled="{Binding EditSettings.ChangeLabelScale}"
                                         Margin="0,3,0,3"
                                         Padding="5"
                                         ToolTip="KML label scale, for example 0.8, 1 or 1.5" />

                                <CheckBox Grid.Row="5"
                                          Grid.Column="0"
                                          Content="Change text colour"
                                          IsChecked="{Binding EditSettings.ChangeLabelColor}"
                                          Margin="0,5,10,5" />
                                <TextBox Grid.Row="5"
                                         Grid.Column="1"
                                         Text="{Binding EditSettings.LabelColorText, UpdateSourceTrigger=PropertyChanged}"
                                         IsEnabled="{Binding EditSettings.ChangeLabelColor}"
                                         Margin="0,3,6,3"
                                         Padding="5"
                                         ToolTip="Use #RRGGBB. Existing opacity is preserved unless Change text opacity is enabled." />
                                <Button Grid.Row="5"
                                         Grid.Column="2"
                                         Content="Choose..."
                                         Command="{Binding PickLabelColorCommand}"
                                         IsEnabled="{Binding EditSettings.ChangeLabelColor}"
                                         MinWidth="78"
                                         Margin="0,3,0,3" />

                                <CheckBox Grid.Row="6"
                                          Grid.Column="0"
                                          Content="Change text opacity"
                                          IsChecked="{Binding EditSettings.ChangeLabelOpacity}"
                                          Margin="0,5,10,5" />
                                <TextBox Grid.Row="6"
                                         Grid.Column="1"
                                         Text="{Binding EditSettings.LabelOpacityText, UpdateSourceTrigger=PropertyChanged}"
                                         IsEnabled="{Binding EditSettings.ChangeLabelOpacity}"
                                         Margin="0,3,6,3"
                                         Padding="5"
                                         ToolTip="Enter a percentage from 0 to 100" />
                                <TextBlock Grid.Row="6"
                                           Grid.Column="2"
                                           Text="%"
                                           VerticalAlignment="Center"
                                           Margin="6,0,0,0" />
''',
    "opacity controls and shifted label rows",
)

replace_once(
    window,
    '''                            <TextBlock Text="Colors may be entered as #RRGGBB or #AARRGGBB. The app converts them to KML AABBGGRR format automatically."
''',
    '''                            <TextBlock Text="Colours use #RRGGBB. Changing colour alone preserves each placemark's existing opacity. Enable the separate opacity option only when alpha should change."
''',
    "opacity guidance",
)

print("All opacity feature source transformations completed successfully.")
