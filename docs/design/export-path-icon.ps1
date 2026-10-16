param(
 [string]$SvgPath = (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'src\ARV_Ribbon\Images\OriginToZero.svg'),
 [string]$PngPath
)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationCore,WindowsBase
if (-not $PngPath) {$PngPath=[IO.Path]::ChangeExtension($SvgPath,'.png')}
[xml]$svg=Get-Content -LiteralPath $SvgPath -Raw -Encoding UTF8
if ($svg.DocumentElement.viewBox -ne '0 0 32 32') {throw 'Expected 32px icon'}
$visual=[Windows.Media.DrawingVisual]::new();$drawing=$visual.RenderOpen()
try {
 foreach($path in $svg.DocumentElement.ChildNodes) {
  if ($path.NodeType -ne [Xml.XmlNodeType]::Element) {continue}
  if ($path.LocalName -ne 'path') {throw 'This exporter supports path-only icons'}
  $fill=$null
  if ($path.fill -ne 'none') {$fill=[Windows.Media.BrushConverter]::new().ConvertFromInvariantString($path.fill)}
  $stroke=[Windows.Media.BrushConverter]::new().ConvertFromInvariantString($path.stroke)
  $pen=[Windows.Media.Pen]::new($stroke,[double]$path.GetAttribute('stroke-width'))
  $drawing.DrawGeometry($fill,$pen,[Windows.Media.Geometry]::Parse($path.d))
 }
} finally {$drawing.Close()}
$bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new(32,32,96,96,[Windows.Media.PixelFormats]::Pbgra32)
$bitmap.Render($visual)
$encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
$stream=[IO.File]::Create($PngPath)
try {$encoder.Save($stream)} finally {$stream.Dispose()}
"Exported SVG to PNG: $PngPath"