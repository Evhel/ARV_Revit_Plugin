param([string]$OutputDirectory = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Data
if (-not (Test-Path -LiteralPath $OutputDirectory)) { New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null }
$sourcePath = Join-Path $PSScriptRoot 'UI_EXAMPLE.xaml'
$context = New-Object Windows.Markup.ParserContext
$context.BaseUri = [Uri]::new($sourcePath)
$stream = [IO.File]::OpenRead($sourcePath)
try { $root = [Windows.Markup.XamlReader]::Load($stream, $context) } finally { $stream.Dispose() }
$grid = $root.FindName('ViewsGrid')
$table = New-Object Data.DataTable
foreach ($column in 'Name','Kind','OnSheet') { [void]$table.Columns.Add($column, [string]) }
[void]$table.Rows.Add('АР_План 1 этажа','План','Нет')
[void]$table.Rows.Add('АР_Разрез А–А','Разрез','Нет')
[void]$table.Rows.Add('АР_Фасад 1','Фасад','Нет')
$grid.ItemsSource = $table.DefaultView
$root.Measure([Windows.Size]::new(760,704))
$root.Arrange([Windows.Rect]::new(0,0,760,704))
$root.UpdateLayout()
$root.Dispatcher.Invoke([Action]{}, [Windows.Threading.DispatcherPriority]::Render)
$root.UpdateLayout()
if ($grid.Items.Count -ne 3) { throw 'Preview must contain three sample rows.' }
$icon = $root.Child.Children[0].Children | Where-Object { $_ -is [Windows.Controls.Image] }
if ($null -eq $icon.Source -or $icon.Source.PixelWidth -ne 32) { throw 'Existing ARV icon did not load.' }
$button = $root.FindName('PlaceButton')
$point = $button.TransformToAncestor($root).Transform([Windows.Point]::new(0,0))
if ($point.Y + $button.ActualHeight -gt 704 -or $point.X + $button.ActualWidth -gt 760) { throw 'Footer action is clipped.' }
if ($button.ActualHeight -lt 32) { throw 'Action target is too small.' }
foreach ($dpi in 96,144,192) {
    $scale = $dpi / 96.0
    $image = [Windows.Media.Imaging.RenderTargetBitmap]::new([int](760*$scale),[int](704*$scale),$dpi,$dpi,[Windows.Media.PixelFormats]::Pbgra32)
    $image.Render($root)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($image))
    $name = if ($dpi -eq 96) { 'UI_REFERENCE.png' } else { 'UI_REFERENCE_'+$dpi+'dpi.png' }
    $target = [IO.File]::Create((Join-Path $OutputDirectory $name))
    try { $encoder.Save($target) } finally { $target.Dispose() }
}
'PASS: example XAML and theme load; 3 sample rows; real 32px icon; footer bounds; 96/144/192 DPI offscreen renders.'
