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
    $content.Dispatcher.Invoke([Action]{}, [Windows.Threading.DispatcherPriority]::ApplicationIdle)
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
# Schedule catalog contains the three existing filter fields from the requested example.
$scheduleInfo=[Activator]::CreateInstance($elementAssembly.GetType('ARV.ElementViews.ScheduleInfo'));$scheduleInfo.Id=1001;$scheduleInfo.Name='Спецификация арматуры пилонов';$scheduleInfo.CanCopy=$true
$filterNames=@('ADSK_Тип элемента КЖ','ADSK_Марка изделия','МеткаОсновы_Мрк.МаркаКонстр_ФИЛЬТРОВАТЬ ПО ЭТОМУ ПАРАМЕТРУ')
for($i=0;$i -lt 3;$i++){
 $field=[Activator]::CreateInstance($elementAssembly.GetType('ARV.ElementViews.ScheduleFilterChoice'));$field.Index=$i;$field.Name=$filterNames[$i];$field.FieldName=$filterNames[$i];$field.ValueKind='String';$field.OperatorKey='Equal'
 $field.Operators.Add((New-ElementChoice 0 'Equal' 'Равно'));$field.Operators.Add((New-ElementChoice 0 'Contains' 'Содержит'));$field.Operators.Add((New-ElementChoice 0 'HasParameter' 'Параметр существует'));$scheduleInfo.Filters.Add($field)
}
$scheduleInfo.Parameters.Add((New-ElementChoice -1 '' 'Не заполнять'));$scheduleInfo.Parameters.Add((New-ElementChoice 101 'String' 'Комментарии'))
$elementCatalog.Schedules.Add($scheduleInfo)
$secondSchedule=[Activator]::CreateInstance($elementAssembly.GetType('ARV.ElementViews.ScheduleInfo'));$secondSchedule.Id=1002;$secondSchedule.Name='Ведомость деталей';$secondSchedule.CanCopy=$true;$secondSchedule.Parameters.Add((New-ElementChoice -1 '' 'Не заполнять'));$elementCatalog.Schedules.Add($secondSchedule)
$thirdSchedule=[Activator]::CreateInstance($elementAssembly.GetType('ARV.ElementViews.ScheduleInfo'));$thirdSchedule.Id=1003;$thirdSchedule.Name='Спецификация бетона';$thirdSchedule.CanCopy=$true;$thirdSchedule.Parameters.Add((New-ElementChoice -1 '' 'Не заполнять'));$elementCatalog.Schedules.Add($thirdSchedule)
$elementDialog=[Activator]::CreateInstance($elementAssembly.GetType('ARV.ElementViews.MainWindow'),[object[]]@($elementCatalog))
foreach($row in $elementDialog.Rows){$row.TemplateId=2}
$elementDialog.FindName('ParameterComboBox').SelectedIndex=1;$elementDialog.FindName('UniqueCheckBox').IsChecked=$true
$elementDialog.GetType().GetMethod('RefreshPreview',[Reflection.BindingFlags]'NonPublic,Instance').Invoke($elementDialog,@())
if ($elementDialog.FindName('CountInfo').Text -notmatch 'К обработке: 17 из 50' -or $elementDialog.Rows.Count -ne 7) {throw 'Element-view selection preview regression'}
if ($elementDialog.Rows[0].Preview -cne 'КЖ3_Арм_Пилон_Пм-1_План1') {throw 'Element-view parameter naming regression'}
$events = @('Click','SelectionChanged','TextChanged','TextBoxBase.TextChanged','PreviewTextInput','KeyDown')
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
        $window.FindName('SheetNamePreview').Text='Пример: Элемент Пм-1'
    }
    if ($case.project -eq 'ElementViews' -and $case.file -eq 'ReportWindow.xaml') {
        $table=[Data.DataTable]::new();[void]$table.Columns.Add('Element');[void]$table.Columns.Add('Reason')
        [void]$table.Rows.Add('10501 — Колонна К-1','Виды сохранены за границей листа для ручной компоновки.')
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

foreach($dpi in @(96,120,144,192)) {Render-Content $elementDialog 1180 670 $dpi (Join-Path $out ('ElementViews-compiled-'+$dpi+'.png'))}
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
function Find-VisualTextBoxes($node) {
 if($node -is [Windows.Controls.TextBox]) {Write-Output $node}
 for($child=0;$child -lt [Windows.Media.VisualTreeHelper]::GetChildrenCount($node);$child++) {Find-VisualTextBoxes ([Windows.Media.VisualTreeHelper]::GetChild($node,$child))}
}
$textCells=@(Find-VisualTextBoxes ($elementDialog.FindName('ViewsGrid')))
$titleCell=@($textCells | Where-Object {$_.DataContext -eq $elementDialog.Rows[0] -and $_.GetBindingExpression([Windows.Controls.TextBox]::TextProperty).ParentBinding.Path.Path -eq 'TitleSuffix'})[0]
if(-not $titleCell){throw 'Actual sheet-title cell not generated'}
$titleCell.Text='Разрез'
if($elementDialog.Rows[0].TitleSuffix -cne 'Разрез'){throw 'Editing title cell did not update settings'}
$detailCell=@($textCells | Where-Object {$_.DataContext -eq $elementDialog.Rows[0] -and $_.GetBindingExpression([Windows.Controls.TextBox]::TextProperty).ParentBinding.Path.Path -eq 'DetailNumber'})[0]
if(-not $detailCell){throw 'Detail number column missing'}
$detailCell.Text='2-2'
if($elementDialog.Rows[0].DetailNumber -cne '2-2'){throw 'Detail number editor binding failed'}
$elementDialog.FindName('CreateSheetCheckBox').IsChecked=$true
$elementDialog.Rows[3].DetailNumber='2-2';$badDetail=$false
try{$null=$elementDialog.BuildOptions()}catch{$badDetail=$true}
if(-not $badDetail){throw 'Duplicate sheet detail numbers accepted'}
$elementDialog.Rows[3].DetailNumber=''
if($elementDialog.BuildOptions().Views[0].DetailNumber -cne '2-2'){throw 'View request lost detail number'}
$elementDialog.FindName('UseLayoutCheckBox').IsChecked=$true
$layoutGrid=$elementDialog.FindName('LayoutGrid')
$layoutGrid.Children[0].Text='7';$layoutGrid.Children[1].Text='4';$layoutGrid.Children[2].Text='1'
$sideCell=@($textCells | Where-Object {$_.DataContext -eq $elementDialog.Rows[0] -and $_.GetBindingExpression([Windows.Controls.TextBox]::TextProperty).ParentBinding.Path.Path -eq 'SideText'})[0]
if(-not $sideCell){throw 'Actual per-view crop cell not generated'}
$sideCell.Text='50';$elementDialog.Rows[0].AboveText='60';$elementDialog.Rows[0].BelowText='70'
$elementDialog.Rows[3].SideText='300';$elementDialog.Rows[3].AboveText='400';$elementDialog.Rows[3].BelowText='500'
$elementDialog.FindName('FormatATextBox').Text='2';$elementDialog.FindName('FormatXTextBox').Text='3'
$elementDialog.FindName('SheetLeftTextBox').Text='25';$elementDialog.FindName('SheetTopTextBox').Text='30';$elementDialog.FindName('SheetGapTextBox').Text='12'
$built=$elementDialog.BuildOptions()
if($built.Views[0].SideMm -ne 50 -or $built.Views[0].AboveMm -ne 60 -or $built.Views[0].BelowMm -ne 70 -or $built.Views[1].SideMm -ne 300 -or $built.Views[1].BelowMm -ne 500){throw 'Independent view margins lost'}
if($built.FormatA -cne '2' -or $built.FormatX -cne '3'){throw 'Sheet format not passed to command'}
if($built.SheetLeftMm -ne 25 -or $built.SheetTopMm -ne 30 -or $built.SheetGapMm -ne 12){throw 'Sheet margins or gap not transferred to options'}
if($built.LayoutCells[0] -ne 7 -or $built.Views[0].TitleSuffix -cne 'Разрез' -or $built.Views[0].Number -ne 1) {throw 'Grid layout or sheet title lost in options'}
$layoutGrid.Children[1].Text='7';$duplicateRejected=$false
try{$null=$elementDialog.BuildOptions()}catch{$duplicateRejected=$true}
if(-not $duplicateRejected){throw 'Duplicate grid view accepted'}
$layoutGrid.Children[1].Text='4'
$saved=$elementDialog.CaptureSettings()
$settingsType=$elementAssembly.GetType('ARV.ElementViews.SettingsFile')
$settingsPath=Join-Path $out 'element-views-settings.json'
$saveArgs=New-Object object[] 2;$saveArgs[0]=[string]$settingsPath;$saveArgs[1]=$saved.PSObject.BaseObject
$settingsType.GetMethod('Save').Invoke($null,$saveArgs)
$loaded=$settingsType.GetMethod('Load').Invoke($null,[object[]]@([string]$settingsPath))
$elementDialog.Rows[0].Prefix='changed';$elementDialog.Rows[0].TemplateId=2;$elementDialog.Rows[0].ViewportTypeId=-1
$elementDialog.ApplySettings($loaded)
if($elementDialog.Rows[0].Prefix -cne 'КЖ3_Арм_Пилон_' -or $elementDialog.Rows[0].TemplateId -ne 4 -or $elementDialog.Rows[0].ViewportTypeId -ne 22){throw 'Settings round-trip lost names or selections'}
if($loaded.Rows[0].DetailNumber -cne '2-2' -or $elementDialog.Rows[0].DetailNumber -cne '2-2'){throw 'Detail number JSON round trip failed'}
if($loaded.Rows[0].Side -cne '50' -or $loaded.Rows[3].Below -cne '500' -or $loaded.FormatA -cne '2' -or $loaded.FormatX -cne '3'){throw 'Per-row margins/format JSON round-trip lost'}
if($loaded.SheetLeft -cne '25' -or $loaded.SheetTop -cne '30' -or $loaded.SheetGap -cne '12'){throw 'Sheet margin/gap JSON round-trip lost'}
if($loaded.LayoutCells[0] -ne 7 -or $loaded.Rows[0].TitleSuffix -cne 'Разрез' -or -not $loaded.UseLayout){throw 'Grid/title settings round-trip lost'}
$legacy=$elementDialog.CaptureSettings();$legacy.Version=1;$legacy.UseLayout=$false;$legacy.LayoutCells=$null
$legacy.Rows[0].DetailNumber=$null
$legacy.Side='110';$legacy.Above='120';$legacy.Below='130';$legacy.PlanPadding='140'
$elementDialog.ApplySettings($legacy)
if($elementDialog.Rows[0].DetailNumber){throw 'Old settings without detail number must leave it automatic'}
if($elementDialog.Rows[0].SideText -cne '140' -or $elementDialog.Rows[0].AboveText -cne '140' -or $elementDialog.Rows[3].SideText -cne '110' -or $elementDialog.Rows[3].BelowText -cne '130'){throw 'Old global margins must migrate to all corresponding rows'}
if($elementDialog.Rows[0].TemplateId -ne 4 -or $elementDialog.FindName('SheetLeftTextBox').Text -cne '20' -or $elementDialog.FindName('SheetGapTextBox').Text -cne '10'){throw 'Legacy settings must remain loadable with default sheet margins/gap'}
$elementDialog.ApplySettings($loaded)
$elementDialog.FindName('ParameterComboBox').SelectedIndex=-1;$elementDialog.FindName('ParameterComboBox').Text='Марка'
$typed=$elementDialog.BuildOptions()
if($typed.ParameterKey -cne 'I:mark'){throw 'Manually typed parameter name not resolved'}
$elementDialog.FindName('ParameterComboBox').SelectedIndex=1
$elementDialog.FindName('UniqueCheckBox').IsChecked=$false
if($elementDialog.FindName('ParameterComboBox').IsEnabled){throw 'Unchecked parameter row must be disabled'}
$elementDialog.FindName('UniqueCheckBox').IsChecked=$true
$invalidLayout=$elementDialog.CaptureSettings();$invalidLayout.LayoutCells[1]=7;$invalidLayout.Rows[0].Prefix='must not apply';$oldPrefix=$elementDialog.Rows[0].Prefix;$invalidRejected=$false
try{$elementDialog.ApplySettings($invalidLayout)}catch{$invalidRejected=$true}
if(-not $invalidRejected -or $elementDialog.Rows[0].Prefix -cne $oldPrefix){throw 'Invalid saved grid partially overwrote settings'}
$elementDialog.Rows[0].SideText='-1';$badMargin=$false
try{$null=$elementDialog.BuildOptions()}catch{$badMargin=$true}
if(-not $badMargin){throw 'Invalid row margin accepted'}
$elementDialog.Rows[0].SideText='50'
$invalidRow=$elementDialog.CaptureSettings();$invalidRow.Rows[0].Above='-1';$oldRow=$elementDialog.Rows[0].AboveText;$badMargin=$false
try{$elementDialog.ApplySettings($invalidRow)}catch{$badMargin=$true}
if(-not $badMargin -or $elementDialog.Rows[0].AboveText -cne $oldRow){throw 'Invalid saved row margin partially applied'}
$invalidMargins=$elementDialog.CaptureSettings();$invalidMargins.SheetGap='-1';$oldGap=$elementDialog.FindName('SheetGapTextBox').Text;$invalidRejected=$false
try{$elementDialog.ApplySettings($invalidMargins)}catch{$invalidRejected=$true}
if(-not $invalidRejected -or $elementDialog.FindName('SheetGapTextBox').Text -cne $oldGap){throw 'Invalid saved sheet gap partially overwrote settings'}
$elementDialog.FindName('SheetGapTextBox').Text='wrong';$invalidRejected=$false
try{$null=$elementDialog.BuildOptions()}catch{$invalidRejected=$true}
if(-not $invalidRejected){throw 'Invalid sheet gap accepted'}
$elementDialog.FindName('SheetGapTextBox').Text=$oldGap
$loaded.Rows[0].Template.Id=99999
$elementDialog.ApplySettings($loaded)
if($elementDialog.Rows[0].TemplateId -ne 4) {throw 'Saved reference must resolve by name in another project'}
$badPath=Join-Path $out 'element-views-settings-invalid.json';[IO.File]::WriteAllText($badPath,'not json')
$badRejected=$false;try{$null=$settingsType.GetMethod('Load').Invoke($null,[object[]]@([string]$badPath))}catch{$badRejected=$true}
if(-not $badRejected){throw 'Invalid settings file must be rejected'}
$loaded.Rows[0].Template.Name='Missing template';$loaded.Rows[0].Prefix='Loaded despite missing template'
$elementDialog.ApplySettings($loaded)
if($elementDialog.Rows[0].TemplateId -ne -1 -or $elementDialog.Rows[0].Prefix -cne 'Loaded despite missing template' -or $elementDialog.Rows[3].TemplateId -ne $loaded.Rows[3].Template.Id){throw 'Missing template must stay empty while other settings load'}
$loaded.Rows[0].Template.Name='КЖ_Разрезы 1:50';$loaded.Rows[0].Prefix='КЖ3_Арм_Пилон_';$elementDialog.ApplySettings($loaded)
# Missing parameter, section type, titleblock and viewport must not block independent values.
$partial=$elementDialog.CaptureSettings();$partial.Parameter.Name='Missing parameter';$partial.SectionType.Name='Missing section';$partial.TitleBlock.Name='Missing titleblock';$partial.Rows[0].Viewport.Name='Missing viewport';$partial.SheetPrefix='Partial sheet '
$elementDialog.ApplySettings($partial)
if($elementDialog.FindName('ParameterComboBox').SelectedItem -or $elementDialog.FindName('ParameterComboBox').Text -or $elementDialog.FindName('SectionTypeComboBox').SelectedItem -or $elementDialog.FindName('TitleBlockComboBox').SelectedItem -or $elementDialog.Rows[0].ViewportTypeId -ne -2 -or $elementDialog.FindName('SheetPrefixTextBox').Text -cne 'Partial sheet '){throw 'Project references must clear independently of valid settings'}
$elementDialog.ApplySettings($loaded)
$elementDialog.FindName('CreateSheetCheckBox').IsChecked=$true
$layoutGrid.Children[0].Text='1';$layoutGrid.Children[1].Text='7';$layoutGrid.Children[2].Text='';$layoutGrid.Children[3].Text='4'
$elementDialog.FindName('BodyScroll').ScrollToBottom()
$elementDialog.FindName('SettingsTabs').SelectedItem=$elementDialog.FindName('SheetsTab')
foreach($dpi in @(96,120,144,192)){Render-Content $elementDialog 1180 670 $dpi (Join-Path $out ('ElementViews-sheets-'+$dpi+'.png'))}
$elementDialog.FindName('ParameterComboBox').SelectedIndex=0
$elementDialog.GetType().GetMethod('Ok_Click',[Reflection.BindingFlags]'NonPublic,Instance').Invoke($elementDialog,@($null,[Windows.RoutedEventArgs]::new()))
if ($elementDialog.FindName('ValidationMessage').Text -notmatch 'параметр') {throw 'Unique filter without parameter must fail inline'}
$elementDialog.FindName('UniqueCheckBox').IsChecked=$false
$elementDialog.GetType().GetMethod('RefreshPreview',[Reflection.BindingFlags]'NonPublic,Instance').Invoke($elementDialog,@())
if ($elementDialog.FindName('CountInfo').Text -notmatch 'К обработке: 50 из 50') {throw 'Unchecked unique filter must retain all elements'}
# Actual schedule controls, live substring search, copy/no-copy, settings and placement cells.
$elementDialog.FindName('ParameterComboBox').SelectedIndex=1;$elementDialog.FindName('UniqueCheckBox').IsChecked=$true;$elementDialog.FindName('CreateSheetCheckBox').IsChecked=$true
$elementDialog.ScheduleRows[0].Source=$scheduleInfo;$elementDialog.ScheduleRows[0].Filter=$scheduleInfo.Filters[2];$elementDialog.ScheduleRows[0].OperatorKey='Contains';$elementDialog.ScheduleRows[0].ViewParameter=$scheduleInfo.Parameters[1]
$elementDialog.ScheduleRows[1].Source=$secondSchedule;$elementDialog.ScheduleRows[1].Copy=$false
$elementDialog.GetType().GetMethod('FillScheduleLayout_Click',[Reflection.BindingFlags]'NonPublic,Instance').Invoke($elementDialog,@($null,[Windows.RoutedEventArgs]::new()))
$built=$elementDialog.BuildOptions()
if($built.Schedules.Count -ne 2 -or $built.Schedules[0].FilterIndex -ne 2 -or $built.Schedules[0].FilterName -cne $filterNames[2] -or $built.Schedules[0].OperatorKey -cne 'Contains' -or $built.Schedules[0].ViewParameterId -ne 101){throw 'Schedule filter/view parameter settings lost'}
if($built.Schedules[1].Copy -or $built.Schedules[1].FilterIndex -ne -1 -or $built.Schedules[1].ViewParameterId -ne -1 -or $elementDialog.ScheduleRows[1].CanEdit){throw 'Reuse must not change source schedule'}
if($built.ScheduleCells[1] -ne 1 -or $built.ScheduleCells[3] -ne 2){throw 'Default schedules must stack downward from right corner'}
$elementDialog.GetType().GetMethod('RefreshPreview',[Reflection.BindingFlags]'NonPublic,Instance').Invoke($elementDialog,@())
$elementDialog.FindName('SettingsTabs').SelectedItem=$elementDialog.FindName('SchedulesTab')
$elementDialog.FindName('BodyScroll').ScrollToTop()
foreach($dpi in @(96,120,144,192)){Render-Content $elementDialog 1180 670 $dpi (Join-Path $out ('ElementViews-schedules-'+$dpi+'.png'))}
$scheduleCombos=@(Find-VisualCombos ($elementDialog.FindName('SchedulesGrid')))
$sourceCombo=@($scheduleCombos | Where-Object {$_.DataContext -eq $elementDialog.ScheduleRows[0] -and $_.IsEditable})[0]
if(-not $sourceCombo){throw 'Editable schedule selector missing'}
$sourceCombo.Text='ведом'
if(@($sourceCombo.ItemsSource).Count -ne 1 -or @($sourceCombo.ItemsSource)[0].Id -ne 1002){throw 'Typing must narrow dropdown by substring'}
$sourceCombo.Text='';$sourceCombo.SelectedItem=$scheduleInfo
$elementDialog.ScheduleRows[0].Filter=$scheduleInfo.Filters[2];$elementDialog.ScheduleRows[0].OperatorKey='Contains';$elementDialog.ScheduleRows[0].ViewParameter=$scheduleInfo.Parameters[1]
# Change the actual ComboBox selection (model-only assignments missed the old reentrant reset).
$filterControl=@($scheduleCombos | Where-Object {$_.DataContext -eq $elementDialog.ScheduleRows[0] -and $_.GetBindingExpression([Windows.Controls.ItemsControl]::ItemsSourceProperty).ParentBinding.Path.Path -eq 'Filters'})[0]
$conditionControl=@($scheduleCombos | Where-Object {$_.DataContext -eq $elementDialog.ScheduleRows[0] -and $_.GetBindingExpression([Windows.Controls.ItemsControl]::ItemsSourceProperty).ParentBinding.Path.Path -eq 'Operators'})[0]
foreach($index in @(1,2,3)){
 $filterControl.SelectedIndex=$index
 if($elementDialog.ScheduleRows[0].Filter.Index -ne ($index-1) -or $conditionControl.Items.Count -ne 3 -or $conditionControl.SelectedValue -cne 'Equal'){throw 'Choosing a filter must populate and select its native condition without resetting the field'}
 $conditionControl.SelectedValue='HasParameter'
 if($elementDialog.ScheduleRows[0].OperatorKey -cne 'HasParameter' -or $elementDialog.ScheduleRows[0].FilterValue -cne 'Не требуется'){throw 'Condition selector did not update schedule row'}
 $conditionControl.SelectedValue='Contains'
 if($elementDialog.ScheduleRows[0].OperatorKey -cne 'Contains'){throw 'Condition selector lost chosen value'}
}
# A fresh source starts with an explicit field choice; then conditions become available.
$sourceCombo.Text='';$sourceCombo.SelectedItem=$scheduleInfo
if($conditionControl.IsEnabled -or $conditionControl.Items.Count -ne 0){throw 'Condition must wait for a field choice'}
$filterControl.SelectedIndex=3
if(-not $conditionControl.IsEnabled -or $conditionControl.SelectedValue -cne 'Equal'){throw 'Fresh source condition not populated'}
$filterControl.SelectedIndex=0
if($elementDialog.ScheduleRows[0].Filter.Index -ne -1 -or $conditionControl.IsEnabled -or $elementDialog.ScheduleRows[0].FilterValue -cne '—'){throw 'Keep filters must be selectable in actual dropdown and disable condition/value'}
$filterControl.SelectedIndex=3
$conditionControl.SelectedValue='Contains';$elementDialog.ScheduleRows[0].ViewParameter=$scheduleInfo.Parameters[1]
# Last columns fill the available viewport and follow window resizing; small windows retain scrolling.
foreach($tab in @($elementDialog.FindName('SchedulesTab'),$elementDialog.FindName('SettingsTabs').Items[0])){
 $elementDialog.FindName('SettingsTabs').SelectedItem=$tab
 foreach($width in @(1180,1500,1000,1180)){
  Render-Content $elementDialog $width 670 96 (Join-Path $out 'element-column-sizing.png')
  $table=if($tab.Header -eq 'Спецификации'){$elementDialog.FindName('SchedulesGrid')}else{$elementDialog.FindName('ViewsGrid')}
  $fixed=($table.Columns | Select-Object -SkipLast 1 | Measure-Object ActualWidth -Sum).Sum
  $last=$table.Columns[$table.Columns.Count-1]
  $expected=[Math]::Max($last.MinWidth,$table.ActualWidth-2-$fixed)
  if(-not $last.Width.IsStar -or [Math]::Abs($last.ActualWidth-$expected) -gt 1){throw ('Last column does not fill '+$table.Name+' at width '+$width)}
 }
}
$elementDialog.ScheduleRows[2].Source=$thirdSchedule
$elementDialog.FindName('SettingsTabs').SelectedItem=$elementDialog.FindName('SchedulesTab')
foreach($dpi in @(96,120,144,192)){Render-Content $elementDialog 1180 670 $dpi (Join-Path $out ('ElementViews-schedules-'+$dpi+'.png'))}
$elementDialog.ScheduleRows[2].Source=$null
if($elementDialog.FindName('SectionTypeComboBox').Width -ne 255){throw 'Section type selector must be 1.5 times wider'}
$disabled=@($scheduleCombos | Where-Object {$_.DataContext -eq $elementDialog.ScheduleRows[1] -and -not $_.IsEditable -and $_.IsEnabled})
if($disabled.Count -ne 0){throw 'All fields after copy checkbox must be disabled for reused schedules'}
$scheduleSaved=$elementDialog.CaptureSettings()
$schedulePath=Join-Path $out 'element-views-schedules-settings.json'
$saveArgs=New-Object object[] 2;$saveArgs[0]=[string]$schedulePath;$saveArgs[1]=$scheduleSaved.PSObject.BaseObject
$settingsType.GetMethod('Save').Invoke($null,$saveArgs)
$roundTrip=$settingsType.GetMethod('Load').Invoke($null,[object[]]@([string]$schedulePath))
$elementDialog.ScheduleRows[0].Source=$null;$elementDialog.ApplySettings($roundTrip)
if($elementDialog.ScheduleRows[0].Source.Id -ne 1001 -or $elementDialog.ScheduleRows[0].Filter.Index -ne 2 -or $elementDialog.ScheduleRows[0].OperatorKey -cne 'Contains' -or $elementDialog.ScheduleRows[1].Copy){throw 'Schedule settings round-trip regression'}
$roundTrip.Schedules[0].Source.Id=99999;$elementDialog.ApplySettings($roundTrip)
if($elementDialog.ScheduleRows[0].Source.Id -ne 1001){throw 'Cross-project schedule source must resolve by name'}
$roundTrip.Schedules[0].Source.Name='Missing schedule';$roundTrip.SheetPrefix='Independent loaded prefix'
$elementDialog.ApplySettings($roundTrip)
if($elementDialog.ScheduleRows[0].Source -or $elementDialog.ScheduleRows[1].Source.Id -ne 1002 -or $elementDialog.FindName('SheetPrefixTextBox').Text -cne 'Independent loaded prefix' -or $elementDialog.FindName('ScheduleLayoutGrid').Children[1].Text -ne '' -or $elementDialog.FindName('ScheduleLayoutGrid').Children[3].Text -cne '2'){throw 'Missing schedule must clear only its row and schema cells'}
$roundTrip.Schedules[0].Source.Name=$scheduleInfo.Name;$roundTrip.SheetPrefix='Элемент ';$elementDialog.ApplySettings($roundTrip)
# Missing filter and writable view parameter clear independently; copy/source/name still restore.
$partialSchedules=$elementDialog.CaptureSettings();$partialSchedules.Schedules[0].FilterName='Missing filter';$partialSchedules.Schedules[0].ViewParameter.Name='Missing schedule parameter'
$elementDialog.ApplySettings($partialSchedules)
if($elementDialog.ScheduleRows[0].Source.Id -ne 1001 -or -not $elementDialog.ScheduleRows[0].Copy -or $elementDialog.ScheduleRows[0].Filter -or $elementDialog.ScheduleRows[0].ViewParameter -or $elementDialog.ScheduleRows[1].Source.Id -ne 1002){throw 'Missing schedule filter/parameter must not block independent settings'}
$blankFilter=$elementDialog.CaptureSettings();$elementDialog.ApplySettings($blankFilter)
if($elementDialog.ScheduleRows[0].Filter){throw 'Unresolved filter must not silently become Keep filters after saving settings'}
$elementDialog.ApplySettings($roundTrip)
$partialCondition=$elementDialog.CaptureSettings();$partialCondition.Schedules[0].OperatorKey='UnsupportedCondition';$elementDialog.ApplySettings($partialCondition)
if($elementDialog.ScheduleRows[0].Filter.Index -ne 2 -or $elementDialog.ScheduleRows[0].OperatorKey -or $elementDialog.ScheduleRows[0].ViewParameter.Id -ne 101){throw 'Missing filter condition must leave the condition blank and restore valid fields'}
$elementDialog.ApplySettings($roundTrip)
# Explicit copy without changing any filter, including settings persistence and mark parameter.
$elementDialog.ScheduleRows[0].Filter=$elementDialog.ScheduleRows[0].KeepFilters
$copyUnchanged=$elementDialog.BuildOptions()
if(-not $copyUnchanged.Schedules[0].Copy -or $copyUnchanged.Schedules[0].FilterIndex -ne -1 -or $copyUnchanged.Schedules[0].OperatorKey -or $copyUnchanged.Schedules[0].ViewParameterId -ne 101 -or $elementDialog.ScheduleRows[0].CanEditFilter){throw 'Copy without filter editing must still allow mark parameter'}
$keepSaved=$elementDialog.CaptureSettings();$elementDialog.ScheduleRows[0].Filter=$scheduleInfo.Filters[2];$elementDialog.ApplySettings($keepSaved)
if($elementDialog.ScheduleRows[0].Filter.Index -ne -1 -or $elementDialog.ScheduleRows[0].CanEditFilter){throw 'Keep-filter option did not survive settings restore'}
$elementDialog.ApplySettings($roundTrip)
$elementDialog.ScheduleRows[0].OperatorKey='HasParameter'
if($elementDialog.ScheduleRows[0].FilterValue -cne 'Не требуется'){throw 'Parameter-exists filter must not require a mark value'}
$elementDialog.ScheduleRows[0].OperatorKey='Contains'
$grid=$elementDialog.FindName('ScheduleLayoutGrid');$grid.Children[0].Text='2';$grid.Children[1].Text='1';$grid.Children[3].Text=''
$elementDialog.FindName('SettingsTabs').SelectedItem=$elementDialog.FindName('SheetsTab')
foreach($dpi in @(96,120,144,192)){Render-Content $elementDialog 1180 670 $dpi (Join-Path $out ('ElementViews-sheets-'+$dpi+'.png'))}
$grid.Children[3].Text='1';$badGrid=$false;try{$null=$elementDialog.BuildOptions()}catch{$badGrid=$true}
if(-not $badGrid){throw 'Duplicate schedule layout number accepted'}
$grid.Children[3].Text=''
$elementDialog.FindName('CreateSheetCheckBox').IsChecked=$false;$badSheet=$false;try{$null=$elementDialog.BuildOptions()}catch{$badSheet=$true}
if(-not $badSheet){throw 'Schedules must require a sheet'}
$elementDialog.FindName('CreateSheetCheckBox').IsChecked=$true
$elementDialog.ScheduleRows[1].Source=$scheduleInfo;$elementDialog.ScheduleRows[1].Copy=$true;$elementDialog.ScheduleRows[1].Filter=$scheduleInfo.Filters[0];$elementDialog.ScheduleRows[1].OperatorKey='Equal';$elementDialog.ScheduleRows[1].ViewParameter=$scheduleInfo.Parameters[0]
$separate=$elementDialog.BuildOptions()
if($separate.Schedules[0].FilterIndex -ne 2 -or $separate.Schedules[0].ViewParameterId -ne 101 -or $separate.Schedules[1].FilterIndex -ne 0 -or $separate.Schedules[1].ViewParameterId -ne -1){throw 'Rows using same source must keep independent filter/parameter choices'}
$enabledRows=@($elementDialog.Rows | ForEach-Object {$_.Enabled});foreach($row in $elementDialog.Rows){$row.Enabled=$false}
$onlySchedules=$elementDialog.BuildOptions()
if($onlySchedules.Views.Count -ne 0 -or $onlySchedules.Schedules.Count -ne 2){throw 'Schedules-only sheet setup rejected'}
for($i=0;$i -lt $elementDialog.Rows.Count;$i++){$elementDialog.Rows[$i].Enabled=$enabledRows[$i]}
$elementDialog.ScheduleRows[1].Copy=$false;$elementDialog.ScheduleRows[0].Copy=$false;$elementDialog.ScheduleRows[1].Source=$scheduleInfo;$badReuse=$false;try{$null=$elementDialog.BuildOptions()}catch{$badReuse=$true}
if(-not $badReuse){throw 'Same source schedule cannot be placed twice without copying'}
$oldPreset=$elementDialog.CaptureSettings();$oldPreset.Version=4
$elementDialog.ApplySettings($oldPreset)
if(@($elementDialog.ScheduleRows | Where-Object {$_.Source}).Count -ne 0 -or @($elementDialog.FindName('ScheduleLayoutGrid').Children | Where-Object {$_.Text}).Count -ne 0){throw 'Old settings must clear schedule configuration'}
$elementDialog.Close()
$creation=[Activator]::CreateInstance($elementAssembly.GetType('ARV.ElementViews.CreationResult'));$creation.Completed=17;$creation.ScheduleCopies=17;$creation.SchedulePlacements=34;$creation.Views=51;$creation.Sheets=17;$creation.Filtered=33
$elementReport=[Activator]::CreateInstance($elementAssembly.GetType('ARV.ElementViews.ReportWindow'),[object[]]@($creation))
if ($elementReport.FindName('SkippedGrid').Visibility -ne 'Collapsed' -or $elementReport.FindName('SummaryText').Text -notmatch '51' -or $elementReport.FindName('SummaryText').Text -notmatch 'Спецификаций скопировано: 17; размещено: 34') {throw 'Element-view success summary regression'}
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
