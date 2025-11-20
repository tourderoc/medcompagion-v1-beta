# Script PowerShell pour remplacer les références aux contrôles Notes dans MainWindow.xaml.cs

$filePath = "MedCompanion\MainWindow.xaml.cs"
$content = Get-Content $filePath -Raw

Write-Host "Remplacement des références aux contrôles Notes..."

# Remplacements
$content = $content -replace '\bRawNoteText\b', 'NotesControlPanel.RawNoteTextBox'
$content = $content -replace '\bStructuredNoteText\b', 'NotesControlPanel.StructuredNoteTextBox'
$content = $content -replace '\bStructurerButton\b', 'NotesControlPanel.StructurerBtn'
$content = $content -replace '\bValiderSauvegarderButton\b', 'NotesControlPanel.ValiderSauvegarderBtn'
$content = $content -replace '\bRawNoteLabel\b', 'NotesControlPanel.RawNoteLabelBlock'
$content = $content -replace '\bStructuredNoteLabel\b', 'NotesControlPanel.StructuredNoteLabelBlock'
$content = $content -replace '\bFermerConsultationButton\b', 'NotesControlPanel.FermerConsultationBtn'
$content = $content -replace '\bSynthesisPreviewText\b', 'NotesControlPanel.SynthesisPreviewTextBox'
$content = $content -replace '\bLastSynthesisUpdateLabel\b', 'NotesControlPanel.LastSynthesisUpdateTextBlock'
$content = $content -replace '\bGenerateSynthesisButton\b', 'NotesControlPanel.GenerateSynthesisBtn'

# Sauvegarder
$content | Set-Content $filePath -Encoding UTF8

Write-Host "Remplacement terminé!" -ForegroundColor Green
Write-Host "Fichier modifié: $filePath"
