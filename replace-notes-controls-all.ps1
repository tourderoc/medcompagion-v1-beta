# Script PowerShell pour remplacer les références aux contrôles Notes dans TOUS les fichiers MainWindow

$files = @(
    "MedCompanion\MainWindow.xaml.cs",
    "MedCompanion\MainWindow.Patient.cs",
    "MedCompanion\MainWindow.Documents.cs",
    "MedCompanion\MainWindow.Formulaires.cs",
    "MedCompanion\MainWindow.LLM.cs",
    "MedCompanion\MainWindow.Ordonnances.cs"
)

Write-Host "Remplacement des références aux contrôles Notes dans tous les fichiers MainWindow..." -ForegroundColor Yellow
Write-Host ""

$totalCount = 0

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

foreach ($filePath in $files) {
    if (Test-Path $filePath) {
        Write-Host "Traitement de: $filePath" -ForegroundColor Cyan
        
        $content = Get-Content $filePath -Raw
        $fileCount = 0
        
        foreach ($replacement in $replacements) {
            $oldValue = $replacement.Old
            $newValue = $replacement.New
            
            # Compter les occurrences avant remplacement
            $matches = ([regex]::Matches($content, [regex]::Escape($oldValue))).Count
            
            if ($matches -gt 0) {
                Write-Host "  - $oldValue : $matches occurrence(s)" -ForegroundColor Gray
                $content = $content.Replace($oldValue, $newValue)
                $fileCount += $matches
            }
        }
        
        if ($fileCount -gt 0) {
            # Sauvegarder
            $content | Set-Content $filePath -Encoding UTF8
            Write-Host "  Total: $fileCount remplacement(s)" -ForegroundColor Green
            $totalCount += $fileCount
        } else {
            Write-Host "  Aucun remplacement" -ForegroundColor DarkGray
        }
        
        Write-Host ""
    }
}

Write-Host "======================================" -ForegroundColor White
Write-Host "Remplacement terminé!" -ForegroundColor Green
Write-Host "Total général: $totalCount remplacements effectués" -ForegroundColor Green
