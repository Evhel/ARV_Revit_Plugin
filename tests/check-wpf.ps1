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
    # Only the window content is rendered; no Show/ShowDialog or Revit access.
    $content = $window.Content
    $content.Background = $window.FindResource('Arv.Background')
    $content.Measure([Windows.Size]::new($width,$height))
    $content.Arrange([Windows.Rect]::new(0,0,$width,$height))
    $content.UpdateLayout()
    $content.Dispatcher.Invoke([Action]{}, [Windows.Threading.DispatcherPriority]::Render)
    $content.Measure([Windows.Size]::new($width,$height))
    $content.Arrange([Windows.Rect]::new(0,0,$width,$height))
    $content.UpdateLayout()
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

$elementAssembly=$assemblies['ElementViews']
function New-ElementChoice($id,$key,$name) {
 $choice=[Activator]::CreateInstance($elementAssembly.GetType('ARV.ElementViews.Choice'));$choice.Id=$id;$choice.Key=$key;$choice.Name=$name;return $choice
}
function New-ElementPoint($x,$y,$z) {return [Activator]::CreateInstance($elementAssembly.GetType('ARV.ElementViews.Point3'),[object[]]@([double]$x,[double]$y,[double]$z))}
$elementCatalog=[Activator]::CreateInstance($elementAssembly.GetType('ARV.ElementViews.Catalog'))
$elementCatalog.Parameters.Add((New-ElementChoice -1 '' 'Порядковый номер (без параметра)'))
$elementCatalog.Parameters.Add((New-ElementChoice 100 'I:mark' 'Марка (экземпляр)'))
$elementCatalog.SectionTypes.Add((New-ElementChoice 1 '' 'Разрез элемента'))
$elementCatalog.Templates.Add((New-ElementChoice -1 '' 'Выберите шаблон'))
$elementCatalog.Templates.Add((New-ElementChoice 2 '' 'АР_Колонны 1:20'))
$elementCatalog.Templates.Add((New-ElementChoice 4 '' 'КЖ_Разрезы 1:50'))
$elementCatalog.TitleBlocks.Add((New-ElementChoice 3 '' 'ARV_A1: Горизонтальный'))
$elementCatalog.ViewportTypes.Add((New-ElementChoice -1 '' 'По умолчанию проекта'))
$elementCatalog.ViewportTypes.Add((New-ElementChoice 22 '' 'Без заголовка'))
for($i=0;$i -lt 50;$i++) {
 $element=[Activator]::CreateInstance($elementAssembly.GetType('ARV.ElementViews.ElementData'));$element.Id=$i+1;$element.Name='Колонна';$element.AxisX=New-ElementPoint 1 0 0;$element.AxisY=New-ElementPoint 0 1 0
 $points=[Array]::CreateInstance($elementAssembly.GetType('ARV.ElementViews.Point3'),8);$n=0;$bottom=[Math]::Floor($i/17)*10
 foreach($x in @(0,1)){foreach($y in @(0,1)){foreach($z in @($bottom,($bottom+10))){$points.SetValue((New-ElementPoint $x $y $z),$n);$n++}}}
 $element.Corners=$points;$element.Values.Add('I:mark',('К-'+(($i%17)+1)));$elementCatalog.Elements.Add($element)
}
$elementDialog=[Activator]::CreateInstance($elementAssembly.GetType('ARV.ElementViews.MainWindow'),[object[]]@($elementCatalog))
foreach($row in $elementDialog.Rows){$row.TemplateId=2}
$elementDialog.FindName('ParameterComboBox').SelectedIndex=1;$elementDialog.FindName('UniqueCheckBox').IsChecked=$true
$elementDialog.GetType().GetMethod('RefreshPreview',[Reflection.BindingFlags]'NonPublic,Instance').Invoke($elementDialog,@())
if ($elementDialog.FindName('CountInfo').Text -notmatch 'К обработке: 17 из 50' -or $elementDialog.Rows.Count -ne 7) {throw 'Element-view selection preview regression'}
if ($elementDialog.Rows[0].Preview -cne 'КЖ3_Арм_Пилон_К-1_План1') {throw 'Element-view parameter naming regression'}
$events = @('Click','SelectionChanged','TextChanged','PreviewTextInput','KeyDown')
$count = 0
foreach ($case in $cases) {
    $renderName=if($case.renderName){$case.renderName}else{$case.project}
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
    if ($case.project -eq 'OriginRelocator') {
        $window.FindName('ShiftXText').Text='165 000,0';$window.FindName('ShiftYText').Text='−2 500,0'
        $window.FindName('ModelCountText').Text='Элементов модели: 120'
        $window.FindName('RootCountText').Text='Самостоятельных объектов: 80'
        $window.FindName('ViewCountText').Text='Видов модели: 15'
        $window.FindName('AnnotationCountText').Text='Элементов оформления в видах на листах: 30'
    }
    if ($case.project -eq 'AreaReinforcementHosts') {
        $table=[Data.DataTable]::new();foreach($column in @('OldId','NewId','HostId','Status','Detail')) {[void]$table.Columns.Add($column)}
        [void]$table.Rows.Add('10501','—','20001','Пропущено','Уже имеет найденную основу.')
        [void]$table.Rows.Add('10502','—','—','Пропущено','Под нижней границей найдены несколько стен; исходное армирование сохранено.')
        $window.FindName('ResultsGrid').ItemsSource=$table.DefaultView
        $window.FindName('ResultsGrid').Height=102
        $window.FindName('SummaryText').Text='Изменено: 8. Пропущено: 2.'
    }
    if ($case.project -eq 'ElementViews' -and $case.file -eq 'MainWindow.xaml') {
        $window.FindName('ViewsGrid').ItemsSource=$elementDialog.Rows
        $window.FindName('ParameterComboBox').ItemsSource=$elementCatalog.Parameters;$window.FindName('ParameterComboBox').SelectedIndex=1
        $window.FindName('SectionTypeComboBox').ItemsSource=$elementCatalog.SectionTypes;$window.FindName('SectionTypeComboBox').SelectedIndex=0
        $window.FindName('TitleBlockComboBox').Items.Clear()
        $window.FindName('TitleBlockComboBox').ItemsSource=$elementCatalog.TitleBlocks;$window.FindName('TitleBlockComboBox').SelectedIndex=0
        $window.FindName('UniqueCheckBox').IsChecked=$true
        $window.FindName('CountInfo').Text=$elementDialog.FindName('CountInfo').Text
        $window.FindName('ContextText').Text='Выбрано элементов модели: 50. Неподходящих объектов: 0.'
        $window.FindName('SheetNamePreview').Text='Пример: Элемент К-1'
    }
    if ($case.project -eq 'ElementViews' -and $case.file -eq 'ReportWindow.xaml') {
        $table=[Data.DataTable]::new();[void]$table.Columns.Add('Element');[void]$table.Columns.Add('Reason')
        [void]$table.Rows.Add('10501 — Колонна К-1','Виды не помещаются на листе. Выберите больший формат или увеличьте знаменатель масштаба.')
        $window.FindName('SkippedGrid').ItemsSource=$table.DefaultView
        $window.FindName('SummaryText').Text='Обработано: 16. Виды: 48. Листы: 16. Пропущено: 1. Повторы исключены отбором: 33.'
    }
    $ok=$window.FindName('OkButton');$cancel=$window.FindName('CancelButton'); if ($case.report) {$cancel=$ok}
    if (-not $cancel.IsCancel -or ($case.defaultAction -ne $false -and -not $ok.IsDefault) -or ($case.defaultAction -eq $false -and ($ok.IsDefault -or -not $cancel.IsDefault))) {throw 'Keyboard defaults missing'}
    if ($ok.Background.Color.ToString() -ne '#FF57007F') {throw ('Primary color mismatch: '+$case.project)}
    foreach ($dpi in @(96,120,144,192)) {
        Render-Content $window $case.width ($case.height-40) $dpi (Join-Path $out ($renderName+'-'+$dpi+'.png'))
        $root=$window.Content
        $point=$ok.TransformToAncestor($root).Transform([Windows.Point]::new(0,0))
        if ($point.Y -lt 0 -or $point.Y+$ok.ActualHeight -gt $case.height-40 -or $point.X+$ok.ActualWidth -gt $case.width) {throw ('Footer clipped: '+$case.project)}
        $count++
    }
    # Узкое низкое окно и многострочная ошибка: кнопки должны оставаться доступны.
    $validationText=$window.FindName('ValidationMessage');$validationText.Text='Проверьте введённые значения. Длинное объяснение ошибки должно переноситься и оставлять обе кнопки доступными.';$validationText.Visibility='Visible'
    Render-Content $window 440 360 96 (Join-Path $out ($renderName+'-compact-error.png'))
    $point=$cancel.TransformToAncestor($window.Content).Transform([Windows.Point]::new(0,0))
    if ($point.Y -lt 0 -or $point.Y+$cancel.ActualHeight -gt 360 -or $point.X+$cancel.ActualWidth -gt 440) {throw ('Compact error footer clipped: '+$case.project)}
    $window.Close()
}

foreach($dpi in @(96,120,144,192)) {Render-Content $elementDialog 1000 720 $dpi (Join-Path $out ('ElementViews-compiled-'+$dpi+'.png'))}
# The public form reader must preserve the actual per-row IDs.
function Find-VisualCombos($node) {
 if($node -is [Windows.Controls.ComboBox]) {Write-Output $node}
 for($child=0;$child -lt [Windows.Media.VisualTreeHelper]::GetChildrenCount($node);$child++) {Find-VisualCombos ([Windows.Media.VisualTreeHelper]::GetChild($node,$child))}
}
$combos=@(Find-VisualCombos ($elementDialog.FindName('ViewsGrid')))
$templateCombo=@($combos | Where-Object {$_.DataContext -eq $elementDialog.Rows[0] -and @($_.ItemsSource | Where-Object {$_.Id -eq 4}).Count -gt 0})[0]
$viewportCombo=@($combos | Where-Object {$_.DataContext -eq $elementDialog.Rows[0] -and @($_.ItemsSource | Where-Object {$_.Id -eq 22}).Count -gt 0})[0]
if(-not $templateCombo -or -not $viewportCombo) {throw 'Actual per-row selectors not generated'}
$templateCombo.SelectedValue=4;$viewportCombo.SelectedValue=22
$elementDialog.Content.UpdateLayout()
$built=$elementDialog.BuildOptions()
if($built.Views[0].TemplateId -ne 4 -or $built.Views[0].ViewportTypeId -ne 22 -or $built.Views[1].TemplateId -ne 2) {throw 'Per-view template/viewport selection lost'}
$elementDialog.Rows[0].TemplateId=-1;$rejected=$false
try {$null=$elementDialog.BuildOptions()} catch {$rejected=$true}
if(-not $rejected){throw 'Enabled view without template must be rejected'}
$elementDialog.Rows[0].TemplateId=4
$saved=$elementDialog.CaptureSettings()
$settingsType=$elementAssembly.GetType('ARV.ElementViews.SettingsFile')
$settingsPath=Join-Path $out 'element-views-settings.json'
$saveArgs=New-Object object[] 2;$saveArgs[0]=[string]$settingsPath;$saveArgs[1]=$saved.PSObject.BaseObject
$settingsType.GetMethod('Save').Invoke($null,$saveArgs)
$loaded=$settingsType.GetMethod('Load').Invoke($null,[object[]]@([string]$settingsPath))
$elementDialog.Rows[0].Prefix='changed';$elementDialog.Rows[0].TemplateId=2;$elementDialog.Rows[0].ViewportTypeId=-1
$elementDialog.ApplySettings($loaded)
if($elementDialog.Rows[0].Prefix -cne 'КЖ3_Арм_Пилон_' -or $elementDialog.Rows[0].TemplateId -ne 4 -or $elementDialog.Rows[0].ViewportTypeId -ne 22){throw 'Settings round-trip lost names or selections'}
$loaded.Rows[0].Template.Id=99999
$elementDialog.ApplySettings($loaded)
if($elementDialog.Rows[0].TemplateId -ne 4) {throw 'Saved reference must resolve by name in another project'}
$badPath=Join-Path $out 'element-views-settings-invalid.json';[IO.File]::WriteAllText($badPath,'not json')
$badRejected=$false;try{$null=$settingsType.GetMethod('Load').Invoke($null,[object[]]@([string]$badPath))}catch{$badRejected=$true}
if(-not $badRejected){throw 'Invalid settings file must be rejected'}
$oldPrefix=$elementDialog.Rows[0].Prefix;$loaded.Rows[0].Template.Name='Missing template';$failed=$false
try {$elementDialog.ApplySettings($loaded)}catch{$failed=$true}
if(-not $failed -or $elementDialog.Rows[0].Prefix -cne $oldPrefix){throw 'Missing saved template must not partially overwrite the form'}
$elementDialog.FindName('CreateSheetCheckBox').IsChecked=$true
$elementDialog.FindName('BodyScroll').ScrollToBottom()
$elementDialog.FindName('SettingsTabs').SelectedIndex=1
Render-Content $elementDialog 1000 720 96 (Join-Path $out 'ElementViews-sheets-96.png')
$elementDialog.FindName('ParameterComboBox').SelectedIndex=0
$elementDialog.GetType().GetMethod('Ok_Click',[Reflection.BindingFlags]'NonPublic,Instance').Invoke($elementDialog,@($null,[Windows.RoutedEventArgs]::new()))
if ($elementDialog.FindName('ValidationMessage').Text -notmatch 'параметр') {throw 'Unique filter without parameter must fail inline'}
$elementDialog.FindName('UniqueCheckBox').IsChecked=$false
$elementDialog.GetType().GetMethod('RefreshPreview',[Reflection.BindingFlags]'NonPublic,Instance').Invoke($elementDialog,@())
if ($elementDialog.FindName('CountInfo').Text -notmatch 'К обработке: 50 из 50') {throw 'Unchecked unique filter must retain all elements'}
$elementDialog.Close()
$creation=[Activator]::CreateInstance($elementAssembly.GetType('ARV.ElementViews.CreationResult'));$creation.Completed=17;$creation.Views=51;$creation.Sheets=17;$creation.Filtered=33
$elementReport=[Activator]::CreateInstance($elementAssembly.GetType('ARV.ElementViews.ReportWindow'),[object[]]@($creation))
if ($elementReport.FindName('SkippedGrid').Visibility -ne 'Collapsed' -or $elementReport.FindName('SummaryText').Text -notmatch '51') {throw 'Element-view success summary regression'}
$elementReport.Close()
$warning=[Activator]::CreateInstance($elementAssembly.GetType('ARV.ElementViews.SkippedElement'));$warning.Element='123';$warning.Reason='Виды сохранены за границей листа.'
$creation.Warnings.Add($warning)
$elementReport=[Activator]::CreateInstance($elementAssembly.GetType('ARV.ElementViews.ReportWindow'),[object[]]@($creation))
if ($elementReport.FindName('SkippedGrid').Items.Count -ne 1 -or $elementReport.FindName('SummaryText').Text -notmatch 'Требуют внимания: 1' -or $elementReport.FindName('SummaryText').Text -notmatch 'Обработано: 17') {throw 'Retained set warning report regression'}
$elementReport.Close()
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
$relocationType = $assemblies['OriginRelocator'].GetType('Arv.OriginRelocator.RelocationWindow')
$relocation = [Activator]::CreateInstance($relocationType,[object[]]@([double]165000,[double]-2500,[int]120,[int]80,[int]15,[int]30))
if ($relocation.FindName('ShiftXText').Text -notmatch '165' -or $relocation.FindName('ViewCountText').Text -notmatch '15') {throw 'Relocation summary regression'}
$relocation.Close()
$reportAssembly=$assemblies['AreaReinforcementHosts']
$resultType=$reportAssembly.GetType('ARV.AreaReinforcementHosts.RehostResult')
$listType=[Collections.Generic.List``1].MakeGenericType($resultType)
$list=[Activator]::CreateInstance($listType)
$result=[Activator]::CreateInstance($resultType);$result.Status='Пропущено';$result.OldId='1';$result.NewId='2';$result.HostId='3';$result.Detail='Не удалось сохранить параметр «Соответствие основных слоев верха и низа». Исходное армирование оставлено без изменений.'
$list.Add($result)
$reportArgs=New-Object object[] 2;$reportArgs[0]=$list;$reportArgs[1]='Базовый уровень: «03_Этаж +6,000». Поиск стен по нижней границе контура.'
$report=$reportAssembly.GetType('ARV.AreaReinforcementHosts.ReportWindow').GetConstructors()[0].Invoke($reportArgs)
if ($report.FindName('SummaryText').Text -notmatch '1' -or $report.FindName('ResultsGrid').Items.Count -ne 1) {throw 'Area-host report regression'}
if ($report.FindName('ContextText').Text -notmatch '03_Этаж') {throw 'Base level missing in report context'}
if ($report.Width -gt 650 -or $report.Height -gt 400 -or $report.FindName('SingleResult').Visibility -ne 'Visible' -or $report.FindName('ResultsGrid').Visibility -ne 'Collapsed') {throw 'Single result must use a compact card'}
foreach ($dpi in @(96,120,144,192)) { Render-Content $report $report.Width ($report.Height-40) $dpi (Join-Path $out ('AreaReinforcementHosts-single-'+$dpi+'.png')) }
$report.Close()
$list.Add($result)
$report=$reportAssembly.GetType('ARV.AreaReinforcementHosts.ReportWindow').GetConstructors()[0].Invoke($reportArgs)
if ($report.FindName('ResultsGrid').Visibility -ne 'Visible' -or $report.Height -ge 450) {throw 'Two-row report must remain compact'}
$report.Close()
$success=[Activator]::CreateInstance($resultType);$success.Status='Изменено';$success.OldId='10';$success.NewId='11';$success.HostId='3';$success.Detail='Успешно'
$list.Add($success)
$report=$reportAssembly.GetType('ARV.AreaReinforcementHosts.ReportWindow').GetConstructors()[0].Invoke($reportArgs)
if ($report.FindName('ResultsGrid').Items.Count -ne 2 -or $report.FindName('SummaryText').Text -cne 'Изменено: 1. Пропущено: 2.') {throw 'Mixed report must list only skips and count all results'}
$report.Close()
$list.RemoveAt(0)
$report=$reportAssembly.GetType('ARV.AreaReinforcementHosts.ReportWindow').GetConstructors()[0].Invoke($reportArgs)
if ($report.FindName('SingleResult').Visibility -ne 'Visible' -or $report.FindName('SummaryText').Visibility -ne 'Visible' -or $report.FindName('SummaryText').Text -cne 'Изменено: 1. Пропущено: 1.' -or $report.FindName('ResultsGrid').Items.Count -ne 1) {throw 'Mixed report with one skip must retain totals'}
$report.Close()
$list.RemoveAt(0)
$report=$reportAssembly.GetType('ARV.AreaReinforcementHosts.ReportWindow').GetConstructors()[0].Invoke($reportArgs)
if ($report.FindName('ResultsGrid').Items.Count -ne 0 -or $report.FindName('ResultsGrid').Visibility -ne 'Collapsed' -or $report.FindName('SingleResult').Visibility -ne 'Collapsed' -or $report.FindName('SummaryText').Text -cne 'Изменено: 1. Пропущено: 0.') {throw 'All-success report must show totals without rows'}
Render-Content $report $report.Width ($report.Height-40) 96 (Join-Path $out 'AreaReinforcementHosts-success.png')
$report.Close()
"PASS: $($cases.Count) production layouts and event handlers; $count DPI renders; $($cases.Count) compact/error layouts; compiled numbering/copy/placement/relocation/element-view UI checks. Revit not launched."
