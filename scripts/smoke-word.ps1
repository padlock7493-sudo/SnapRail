$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
if (Get-Process WINWORD -ErrorAction SilentlyContinue) { throw 'Word is already running; skipped to preserve existing documents.' }
$word = $null; $document = $null
try {
    $word = New-Object -ComObject Word.Application
    $word.Visible = $false
    $word.DisplayAlerts = 0
    $document = $word.Documents.Add()
    $word.Selection.Paste()
    if ($document.InlineShapes.Count -ne 1) { throw 'Word did not receive one inline image from the clipboard.' }
    $shape = $document.InlineShapes.Item(1)
    $result = @{ word_image_paste = $true; width_points = $shape.Width; height_points = $shape.Height }
    $result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $project 'artifacts\qa-v0.5\word-results.json') -Encoding UTF8
    $result | ConvertTo-Json
} finally {
    if ($document) { $document.Close(0); [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($document) }
    if ($word) { $saveChanges = 0; $word.Quit([ref]$saveChanges); [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($word) }
}
