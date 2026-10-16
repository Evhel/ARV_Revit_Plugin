param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Data
$script:root = $RepositoryRoot
[AppDomain]::CurrentDomain.add_AssemblyResolve({
    param($sender, $eventArgs)
    $name = ([Reflection.AssemblyName]::new($eventArgs.Name)).Name
    $path = Join-Path 'C:\Program Files\Autodesk\Revit 2022' ($name + '.dll')
    if (Test-Path -LiteralPath $path) { return [Reflection.Assembly]::LoadFrom($path) }
    return $null
})
$cases = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'tests\ui-windows.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$out = Join-Path $RepositoryRoot 'artifacts\ui-review'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$assemblies = @{}
foreach ($case in $cases) {
    $projectPath = Join-Path $RepositoryRoot ('src\'+$case.project)
    [xml]$project = Get-Content -LiteralPath (Get-ChildItem -LiteralPath $projectPath -Filter *.csproj).FullName
    $name = @($project.Project.PropertyGroup.AssemblyName | Where-Object {$_})[0]
    $assemblies[$case.project] = [Reflection.Assembly]::LoadFrom((Join-Path $projectPath ('bin\Release\'+$name+'.dll')))
}
function Render-Content($window, $width, $height, $dpi, $path) {
    # Без Show/ShowDialog: только содержимое окна, без системной рамки.
    $content = $window.Content
    $content.Background = $window.FindResource('Arv.Background')
    $content.Measure([Windows.Size]::new($width,$height))
    $content.Arrange([Windows.Rect]::new(0,0,$width,$height))
    $content.UpdateLayout()
    $content.Dispatcher.Invoke([Action]{}, [Windows.Threading.DispatcherPriority]::Render)
    $scale = $dpi / 96.0
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new([int]($width*$scale),[int]($height*$scale),$dpi,$dpi,[Windows.Media.PixelFormats]::Pbgra32)
    # Window фон не рисуется без HWND; фон содержимого задаём тем же ресурсом.
    $content.Background = $window.FindResource('Arv.Background')
    $bitmap.Render($content)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.File]::Create($path)
    try {$encoder.Save($stream)} finally {$stream.Dispose()}
}
$events = @('Click','SelectionChanged','TextChanged','PreviewTextInput','KeyDown')
$count = 0
foreach ($case in $cases) {
    $type = $assemblies[$case.project].GetType($case.cls,$true)
    $path = Join-Path $RepositoryRoot ('src\'+$case.project+'\'+$case.file)
    [xml]$xml = Get-Content -LiteralPath $path -Raw -Encoding UTF8
    $xml.DocumentElement.RemoveAttribute('Class','http://schemas.microsoft.com/winfx/2006/xaml')
    foreach ($node in $xml.SelectNodes('//*')) {
        foreach ($event in $events) {
            if ($node.HasAttribute($event)) {
                $method = $type.GetMethod($node.GetAttribute($event),[Reflection.BindingFlags]'Instance,Public,NonPublic')
                if (-not $method) {throw ('Missing event handler: '+$case.project+' '+$node.GetAttribute($event))}
                $node.RemoveAttribute($event)
            }
        }
    }
    $reader = [Xml.XmlNodeReader]::new($xml)
    $window = [Windows.Markup.XamlReader]::Load($reader)
    foreach ($name in @('CategoryComboBox','SubCategoryComboBox','TitleBlockComboBox','SheetsComboBox','FilterComboBox','ComboBox1','ComboBox2','ComboBox3','ComboBox4','ComboBox5','cmbWalls','cmbCeilings')) {
        $control=$window.FindName($name)
        if ($control) {
            if ($name -eq 'CategoryComboBox' -and $control.DisplayMemberPath -eq 'DisplayName') {
                $table = [Data.DataTable]::new();[void]$table.Columns.Add('DisplayName');[void]$table.Rows.Add('Стены');$control.ItemsSource=$table.DefaultView
            } elseif ($name -eq 'SheetsComboBox') {
                $table = [Data.DataTable]::new();[void]$table.Columns.Add('Name');[void]$table.Columns.Add('SheetNumber')
                [void]$table.Rows.Add('Планы и разрезы — длинное название листа для проверки отображения','АР-101')
                $control.ItemsSource=$table.DefaultView
            } else {
                [void]$control.Items.Add('АР — Стены / Кирпич / Длинное название типа или группы модели')
            }
            $control.SelectedIndex=0
            $control.IsEnabled=$true
        }
    }
    $views=$window.FindName('ViewsListBox')
    if ($views) {
        $table=[Data.DataTable]::new();[void]$table.Columns.Add('Name')
        foreach ($text in @('АР_План 1 этажа','АР_Разрез А–А','АР_Фасад 1','АР_План с длинным именем для проверки отображения и прокрутки')) {[void]$table.Rows.Add($text)}
        $views.ItemsSource=$table.DefaultView
    }
    if ($window.FindName('_cmbType')) {$window.FindName('_cmbType').SelectedIndex=0}
    $ok=$window.FindName('OkButton');$cancel=$window.FindName('CancelButton')
    if (-not $ok.IsDefault -or -not $cancel.IsCancel) {throw 'Keyboard defaults missing'}
    if ($ok.Background.Color.ToString() -ne '#FF57007F') {throw ('Primary color mismatch: '+$case.project)}
    foreach ($dpi in @(96,120,144,192)) {
        Render-Content $window $case.width ($case.height-40) $dpi (Join-Path $out ($case.project+'-'+$dpi+'.png'))
        $root=$window.Content
        $point=$ok.TransformToAncestor($root).Transform([Windows.Point]::new(0,0))
        if ($point.Y -lt 0 -or $point.Y+$ok.ActualHeight -gt $case.height-40 -or $point.X+$ok.ActualWidth -gt $case.width) {throw ('Footer clipped: '+$case.project)}
        $count++
    }
    # Узкое низкое окно и многострочная ошибка: кнопки должны оставаться доступны.
    $validationText=$window.FindName('ValidationMessage');$validationText.Text='Проверьте введённые значения. Длинное объяснение ошибки должно переноситься и оставлять обе кнопки доступными.';$validationText.Visibility='Visible'
    Render-Content $window 440 360 96 (Join-Path $out ($case.project+'-compact-error.png'))
    $point=$cancel.TransformToAncestor($window.Content).Transform([Windows.Point]::new(0,0))
    if ($point.Y -lt 0 -or $point.Y+$cancel.ActualHeight -gt 360 -or $point.X+$cancel.ActualWidth -gt 440) {throw ('Compact error footer clipped: '+$case.project)}
    $window.Close()
}
# Реальный конструктор и обработчики скомпилированного окна без обращения к документу Revit.
$type=$assemblies['ViewNumberingPlugin'].GetType('ViewNumberingPlugin.NumberingDialog',$true)
$dialog=[Activator]::CreateInstance($type)
$combo=$dialog.FindName('_cmbType');$input=$dialog.FindName('_txtStart')
$validate=$type.GetMethod('ValidateInput',[Reflection.BindingFlags]'NonPublic,Instance')
$combo.SelectedIndex=2
if ($input.Text -cne 'а' -or -not $validate.Invoke($dialog,@())) {throw 'Lowercase numbering input regression'}
$combo.SelectedIndex=0;$input.Text='0'
if ($validate.Invoke($dialog,@()) -or $dialog.FindName('ValidationMessage').Visibility -ne 'Visible') {throw 'Invalid number not shown inline'}
$input.Text='1'
if ($dialog.FindName('ValidationMessage').Visibility -ne 'Collapsed' -or -not $validate.Invoke($dialog,@())) {throw 'Corrected number not accepted'}
$command=$assemblies['ViewNumberingPlugin'].GetType('ViewNumberingPlugin.Command')
$numberType=$assemblies['ViewNumberingPlugin'].GetType('ViewNumberingPlugin.NumberingType')
$alphabet=$command.GetField('UpperCyrillic').GetValue($null)
if ($command.GetMethod('GenerateNumber').Invoke($null,@($alphabet.Length,[Enum]::ToObject($numberType,1))) -cne 'АА') {throw 'Letter continuation regression'}
$dialog.Close()
# Два настоящих окна с пустыми списками: конструкторы не читают модель.
$viewType = [Reflection.Assembly]::LoadFrom('C:\Program Files\Autodesk\Revit 2022\RevitAPI.dll').GetType('Autodesk.Revit.DB.View')
$sheetType = $viewType.Assembly.GetType('Autodesk.Revit.DB.ViewSheet')
$viewsList = [Activator]::CreateInstance([Collections.Generic.List``1].MakeGenericType($viewType))
$sheetsList = [Activator]::CreateInstance([Collections.Generic.List``1].MakeGenericType($sheetType))
$copyType = $assemblies['CopyViewsWithRename'].GetType('CopyViewsWithRename.CopyViewsWindow')
$copyArgs = New-Object object[] 1; $copyArgs[0] = $viewsList; $copy = $copyType.GetConstructors()[0].Invoke($copyArgs)
$copy.FindName('UseReplaceCheckBox').IsChecked = $true
if (-not $copy.FindName('ReplacePanel').IsEnabled -or $copy.FindName('PreviewListBox').Visibility -ne 'Visible') {throw 'Copy replacement UI regression'}
$copy.FindName('CopiesTextBox').Text = '0'
$copyType.GetMethod('OkButton_Click',[Reflection.BindingFlags]'NonPublic,Instance').Invoke($copy,@($null,[Windows.RoutedEventArgs]::new()))
if ($copy.FindName('ValidationMessage').Visibility -ne 'Visible') {throw 'Copy validation regression'}
$copy.Close()
$placeType = $assemblies['PlaceViewsOnSheet'].GetType('PlaceViewsOnSheet.MainWindow')
$placeArgs = New-Object object[] 2; $placeArgs[0] = $viewsList; $placeArgs[1] = $sheetsList; $place = $placeType.GetConstructors()[0].Invoke($placeArgs)
$placeType.GetMethod('OkButton_Click',[Reflection.BindingFlags]'NonPublic,Instance').Invoke($place,@($null,[Windows.RoutedEventArgs]::new()))
if ($place.FindName('ValidationMessage').Visibility -ne 'Visible') {throw 'Missing sheet validation regression'}
$place.Close()
"PASS: 9 production layouts, event handlers, primary color, Enter/Escape; $count DPI renders (100/125/150/200%); 9 compact/error layouts; compiled numbering/copy/placement constructors and inline validation. Revit not launched."
