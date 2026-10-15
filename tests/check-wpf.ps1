param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$apiPath = 'C:\Program Files\Autodesk\Revit 2022'
$script:repoPath = $RepositoryRoot
[AppDomain]::CurrentDomain.add_AssemblyResolve({
    param($sender, $eventArgs)
    $name = ([Reflection.AssemblyName]::new($eventArgs.Name)).Name
    $path = Join-Path 'C:\Program Files\Autodesk\Revit 2022' ($name + '.dll')
    if (Test-Path -LiteralPath $path) { return [Reflection.Assembly]::LoadFrom($path) }
    return $null
})
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $RepositoryRoot 'src\ViewNumberingPlugin\bin\Release\ViewNumberingPlugin.dll'))
$type = $assembly.GetType('ViewNumberingPlugin.NumberingDialog', $true)
$window = [Activator]::CreateInstance($type)
$grid = $window.Content
if ($grid.RowDefinitions.Count -ne 5) { throw 'Expected five numbering dialog rows' }
$combo = $grid.Children | Where-Object { $_ -is [Windows.Controls.ComboBox] }
$textBox = $grid.Children | Where-Object { $_ -is [Windows.Controls.TextBox] }
$combo.SelectedIndex = 2
if ($textBox.Text -cne 'а') { throw 'Lowercase default is incorrect' }
$validate = $type.GetMethod('ValidateInput', [Reflection.BindingFlags]'NonPublic,Instance')
if (-not $validate.Invoke($window, @())) { throw 'Valid lowercase number rejected' }
$combo.SelectedIndex = 0
$textBox.Text = '1'
if (-not $validate.Invoke($window, @())) { throw 'Valid start number rejected' }
$window.Content = $null
$border = New-Object Windows.Controls.Border
$border.Background = [Windows.Media.Brushes]::White
$border.Child = $grid
$border.Measure([Windows.Size]::new(380, [double]::PositiveInfinity))
$height = [math]::Ceiling($border.DesiredSize.Height)
$border.Arrange([Windows.Rect]::new(0, 0, 380, $height))
$border.UpdateLayout()
$buttonPanel = $grid.Children | Where-Object { $_ -is [Windows.Controls.StackPanel] }
$inputPoint = $textBox.TransformToAncestor($grid).Transform([Windows.Point]::new(0, 0))
$buttonsPoint = $buttonPanel.TransformToAncestor($grid).Transform([Windows.Point]::new(0, 0))
if ($buttonsPoint.Y -lt ($inputPoint.Y + $textBox.ActualHeight)) { throw 'Numbering buttons overlap input' }
$image = [Windows.Media.Imaging.RenderTargetBitmap]::new(380, [int]$height, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
$image.Render($border)
$encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($image))
$outputPath = Join-Path $RepositoryRoot 'artifacts\numbering-dialog.png'
$stream = [IO.File]::Create($outputPath)
try { $encoder.Save($stream) } finally { $stream.Dispose() }
$numberType = $assembly.GetType('ViewNumberingPlugin.NumberingType')
$commandType = $assembly.GetType('ViewNumberingPlugin.Command')
$generate = $commandType.GetMethod('GenerateNumber')
$alphabet = [string]::new($commandType.GetField('UpperCyrillic').GetValue($null))
$actual = $generate.Invoke($null, @($alphabet.Length, [Enum]::ToObject($numberType, 1)))
if ($actual -cne 'АА') { throw 'Compiled production numbering does not continue after alphabet' }
$window.Close()
'PASS: numbering WPF initialization, lowercase/digit input, five rows, no overlap, production letter continuation; PNG rendered.'
