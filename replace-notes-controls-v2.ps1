# Script PowerShell amélioré pour remplacer les références aux contrôles Notes

$filePath = "MedCompanion\MainWindow.xaml.cs"
$content = Get-Content $filePath -Raw

Write-Host "Remplacement des références aux contrôles Notes..." -ForegroundColor Yellow

# Compteur de remplacements
$count = 0

# Remplacements (l'ordre est important - les plus longs d'abord)
$replacements = @(
    @{Old = 'ValiderSauvegarderButton'; New = 'NotesControlPanel.ValiderSauvegarderBtn'}
    @{Old = 'FermerConsultationButton'; New = 'NotesControlPanel.FermerConsultationBtn'}
    @{Old = 'LastSynthesisUpdateLabel'; New = 'NotesControlPanel.LastSynthesisUpdateTextBlock'}
    @{Old = 'GenerateSynthesisButton'; New = 'NotesControlPanel.GenerateSynthesisBtn'}
    @{Old = 'SynthesisPreviewText'; New = 'NotesControlPanel.SynthesisPreviewTextBox'}
    @{Old = 'StructuredNoteLabel'; New = 'NotesControlPanel.StructuredNoteLabelBlock'}
    @{Old = 'StructuredNoteText'; New = 'NotesControlPanel.StructuredNoteTextBox'}
    @{Old = 'StructurerButton'; New = 'NotesControlPanel.StructurerBtn'}
    @{Old = 'RawNoteLabel'; New = 'NotesControlPanel.RawNoteLabelBlock'}
    @{Old = 'RawNoteText'; New = 'NotesControlPanel.RawNoteTextBox'}
)

foreach ($replacement in $replacements) {
    $oldValue = $replacement.Old
    $newValue = $replacement.New
    
    # Compter les occurrences avant remplacement
    $matches = ([regex]::Matches($content, [regex]::Escape($oldValue))).Count
    
    if ($matches -gt 0) {
        Write-Host "  Remplacement de '$oldValue' ($matches occurrences)" -ForegroundColor Cyan
        $content = $content.Replace($oldValue, $newValue)
        $count += $matches
    }
}

# Sauvegarder
$content | Set-Content $filePath -Encoding UTF8

Write-Host "`nRemplacement terminé!" -ForegroundColor Green
Write-Host "Total: $count remplacements effectués" -ForegroundColor Green
Write-Host "Fichier modifié: $filePath" -ForegroundColor White
