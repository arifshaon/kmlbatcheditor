from pathlib import Path
import re

root = Path(__file__).resolve().parents[2]
path = root / "KmlScopedEditor" / "MainWindow.xaml"
text = path.read_text(encoding="utf-8")

if 'Content="Change icon opacity"' in text and 'Content="Change text opacity"' in text:
    print("Opacity controls are already present in MainWindow.xaml.")
    raise SystemExit(0)

# Shift the existing text-size and text-colour controls down by one row each.
text, count = re.subn(
    r'(<CheckBox\s+Grid\.Row=")3("\s+Grid\.Column="0"\s+Content="Change text size")',
    r'\g<1>4\2',
    text,
    count=1,
)
if count != 1:
    raise RuntimeError(f"Could not locate the text-size checkbox; matches={count}.")

text, count = re.subn(
    r'(<TextBox\s+Grid\.Row=")3("\s+Grid\.Column="1"\s+Text="\{Binding EditSettings\.LabelScaleText)',
    r'\g<1>4\2',
    text,
    count=1,
)
if count != 1:
    raise RuntimeError(f"Could not locate the text-size input; matches={count}.")

text, count = re.subn(
    r'(<CheckBox\s+Grid\.Row=")4("\s+Grid\.Column="0"\s+Content="Change text colour")',
    r'\g<1>5\2',
    text,
    count=1,
)
if count != 1:
    raise RuntimeError(f"Could not locate the text-colour checkbox; matches={count}.")

text, count = re.subn(
    r'(<TextBox\s+Grid\.Row=")4("\s+Grid\.Column="1"\s+Text="\{Binding EditSettings\.LabelColorText)',
    r'\g<1>5\2',
    text,
    count=1,
)
if count != 1:
    raise RuntimeError(f"Could not locate the text-colour input; matches={count}.")

text, count = re.subn(
    r'(<Button\s+Grid\.Row=")4("\s+Grid\.Column="2"\s+Content="Choose\.\.\."\s+Command="\{Binding PickLabelColorCommand\}")',
    r'\g<1>5\2',
    text,
    count=1,
)
if count != 1:
    raise RuntimeError(f"Could not locate the text-colour button; matches={count}.")

# Update the text-colour tooltip independently from the icon tooltip.
text = text.replace(
    'ToolTip="Use #RRGGBB or #AARRGGBB" />',
    'ToolTip="Use #RRGGBB. Existing opacity is preserved unless Change text opacity is enabled." />',
    1,
)

icon_anchor = re.search(
    r'(?P<indent>\s*)<CheckBox\s+Grid\.Row="4"\s+Grid\.Column="0"\s+Content="Change text size"',
    text,
)
if not icon_anchor:
    raise RuntimeError("Could not find the shifted text-size row for icon-opacity insertion.")
indent = icon_anchor.group("indent")
icon_block = f'''{indent}<CheckBox Grid.Row="3"
{indent}          Grid.Column="0"
{indent}          Content="Change icon opacity"
{indent}          IsChecked="{{Binding EditSettings.ChangeIconOpacity}}"
{indent}          Margin="0,5,10,5" />
{indent}<TextBox Grid.Row="3"
{indent}         Grid.Column="1"
{indent}         Text="{{Binding EditSettings.IconOpacityText, UpdateSourceTrigger=PropertyChanged}}"
{indent}         IsEnabled="{{Binding EditSettings.ChangeIconOpacity}}"
{indent}         Margin="0,3,6,3"
{indent}         Padding="5"
{indent}         ToolTip="Enter a percentage from 0 to 100" />
{indent}<TextBlock Grid.Row="3"
{indent}           Grid.Column="2"
{indent}           Text="%"
{indent}           VerticalAlignment="Center"
{indent}           Margin="6,0,0,0" />

'''
text = text[:icon_anchor.start()] + icon_block + text[icon_anchor.start():]

# Insert text opacity immediately before the batch-edit grid closes.
label_button = re.search(
    r'(?P<block>(?P<indent>\s*)<Button\s+Grid\.Row="5"\s+Grid\.Column="2"\s+Content="Choose\.\.\."\s+Command="\{Binding PickLabelColorCommand\}"[\s\S]*?Margin="0,3,0,3"\s*/>)',
    text,
)
if not label_button:
    raise RuntimeError("Could not find the shifted text-colour button for text-opacity insertion.")
indent = label_button.group("indent")
label_block = f'''

{indent}<CheckBox Grid.Row="6"
{indent}          Grid.Column="0"
{indent}          Content="Change text opacity"
{indent}          IsChecked="{{Binding EditSettings.ChangeLabelOpacity}}"
{indent}          Margin="0,5,10,5" />
{indent}<TextBox Grid.Row="6"
{indent}         Grid.Column="1"
{indent}         Text="{{Binding EditSettings.LabelOpacityText, UpdateSourceTrigger=PropertyChanged}}"
{indent}         IsEnabled="{{Binding EditSettings.ChangeLabelOpacity}}"
{indent}         Margin="0,3,6,3"
{indent}         Padding="5"
{indent}         ToolTip="Enter a percentage from 0 to 100" />
{indent}<TextBlock Grid.Row="6"
{indent}           Grid.Column="2"
{indent}           Text="%"
{indent}           VerticalAlignment="Center"
{indent}           Margin="6,0,0,0" />'''
text = text[:label_button.end()] + label_block + text[label_button.end():]

text = text.replace(
    "Colors may be entered as #RRGGBB or #AARRGGBB. The app converts them to KML AABBGGRR format automatically.",
    "Colours use #RRGGBB. Changing colour alone preserves each placemark's existing opacity. Enable the separate opacity option only when alpha should change.",
    1,
)

path.write_text(text, encoding="utf-8")
print("Completed opacity controls in MainWindow.xaml.")
