# Script pour adapter les noms de contrôles dans MainWindow.Patient.cs

$file = "MedCompanion\MainWindow.Patient.cs"
$content = Get-Content $file -Raw -Encoding UTF8

# Remplacements des contrôles du UserControl NotesControl
$replacements = @{
    'RawNoteText\.Focus\(\)' = 'NotesControlPanel.RawNoteTextBox.Focus()'
    'RawNoteText\.Text' = 'NotesControlPanel.RawNoteTextBox.Text'
    'StructuredNoteText\.Document' = 'NotesControlPanel.StructuredNoteTextBox.Document'
    'StructuredNoteText\.IsReadOnly' = 'NotesControlPanel.StructuredNoteTextBox.IsReadOnly'
    'StructuredNoteText\.Background' = 'NotesControlPanel.StructuredNoteTextBox.Background'
    'StructurerButton\.IsEnabled' = 'NotesControlPanel.StructurerBtn.IsEnabled'
    'StructurerButton\.Visibility' = 'NotesControlPanel.StructurerBtn.Visibility'
    'ValiderSauvegarderButton\.IsEnabled' = 'NotesControlPanel.ValiderSauvegarderBtn.IsEnabled'
    'ValiderSauvegarderButton\.Background' = 'NotesControlPanel.ValiderSauvegarderBtn.Background'
    'ValiderSauvegarderButton\.Visibility' = 'NotesControlPanel.ValiderSauvegarderBtn.Visibility'
    'RawNoteLabel\.Visibility' = 'NotesControlPanel.RawNoteLabelBlock.Visibility'
    'StructuredNoteLabel\.Visibility' = 'NotesControlPanel.StructuredNoteLabelBlock.Visibility'
    'FermerConsultationButton\.Visibility' = 'NotesControlPanel.FermerConsultationBtn.Visibility'
    'SynthesisPreviewText\.Document' = 'NotesControlPanel.SynthesisPreviewTextBox.Document'
    'LastSynthesisUpdateLabel\.Text' = 'NotesControlPanel.LastSynthesisUpdateTextBlock.Text'
    'GenerateSynthesisButton\.IsEnabled' = 'NotesControlPanel.GenerateSynthesisBtn.IsEnabled'
    'GenerateSynthesisButton\.Content' = 'NotesControlPanel.GenerateSynthesisBtn.Content'
}

# Appliquer les remplacements
foreach ($old in $replacements.Keys) {
    $new = $replacements[$old]
    $content = $content -replace $old, $new
}

# Sauvegarder avec UTF-8 BOM
[System.IO.File]::WriteAllText($file, $content, [System.Text.UTF8Encoding]::new($true))

Write-Host "✅ Remplacements effectués dans MainWindow.Patient.cs" -ForegroundColor Green
Write-Host "   - Tous les contrôles adaptés pour NotesControlPanel" -ForegroundColor Gray
