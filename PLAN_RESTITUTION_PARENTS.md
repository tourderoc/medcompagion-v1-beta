# Plan Implémentation — Restitution aux Parents (4e phase 1ère Consultation)

*Date : 2026-05-13 — Quatrième partie de la 1ère Consultation*

---

## Contexte

Le Mode Consultation V0c inclut maintenant:
1. **Phase 1** : Interrogatoire (Anamnèse/Parents) ✅
2. **Phase 2** : Observations Cliniques (Clinique/Enfant) ✅
3. **Phase 3** : Synthèse Initiale (données pondérées) ✅
4. **Phase 4** : Restitution aux Parents (NOUVEAU) — Document 1 page à donner

La restitution doit:
- ✅ Être **belle et lisible** (infographie, flèches, couleurs)
- ✅ Générée via **LLM** (basée sur synthèse initiale)
- ✅ **1 page seulement** (points forts + difficultés + plan d'évaluation)
- ✅ Convertie en **PDF** via LibreOffice (cohérent avec app)
- ✅ **Imprimable** à donner aux parents à la fin de la consult

---

## Objectif UX/UI

### Navigation
Après synthèse initiale, afficher:
- **Tab 4** : "📋 Restitution aux Parents"
- Contient: formulaire simple + PDF preview + bouton imprimer

### Workflow Med
```
1. Remplir formulaire simple
   ├─ Points forts (pré-rempli de synthèse initiale)
   ├─ Difficultés (pré-rempli)
   └─ Nombre de séances d'évaluation (slider 1-5)

2. Générer document infographie
   ├─ LLM reçoit données structurées
   ├─ Génère HTML joli avec CSS
   ├─ Convertit HTML → PDF via LibreOffice
   └─ Affiche preview

3. Imprimer ou modifier
   ├─ Med peut ajuster si besoin
   └─ Imprimer 1 page à donner parents
```

---

## Modèles de Données

### ConsultationModels.cs — AJOUTER

```csharp
/// <summary>
/// Document de restitution aux parents
/// </summary>
public class RestitutionAuxParents : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private string _pointsForts = "";
    public string PointsForts
    {
        get => _pointsForts;
        set { _pointsForts = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PointsForts))); }
    }

    private string _difficultes = "";
    public string Difficultes
    {
        get => _difficultes;
        set { _difficultes = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Difficultes))); }
    }

    private int _nombreSeances = 2;
    public int NombreSeances
    {
        get => _nombreSeances;
        set { _nombreSeances = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NombreSeances))); }
    }

    private bool _needsCourrier = false;
    public bool NeedsCourrier
    {
        get => _needsCourrier;
        set { _needsCourrier = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NeedsCourrier))); }
    }

    private string _typeCourrier = "";
    public string TypeCourrier
    {
        get => _typeCourrier;
        set { _typeCourrier = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TypeCourrier))); }
    }

    public DateTime DateRestitution { get; set; } = DateTime.Now;
    public string? GeneratedHtmlPath { get; set; }
    public string? GeneratedPdfPath { get; set; }
}
```

---

## ViewModel

### ConsultationModeViewModel.cs — AJOUTER

```csharp
// État restitution
private RestitutionAuxParents _restitution = new();
public RestitutionAuxParents Restitution
{
    get => _restitution;
    set => SetProperty(ref _restitution, value);
}

private bool _isRestitutionMode = false;
public bool IsRestitutionMode
{
    get => _isRestitutionMode;
    set => SetProperty(ref _isRestitutionMode, value);
}

private bool _isGeneratingRestitution = false;
public bool IsGeneratingRestitution
{
    get => _isGeneratingRestitution;
    set => SetProperty(ref _isGeneratingRestitution, value);
}

private string _restitutionStatusMessage = "";
public string RestitutionStatusMessage
{
    get => _restitutionStatusMessage;
    set => SetProperty(ref _restitutionStatusMessage, value);
}

// Commandes
public ICommand SwitchToRestitutionCommand { get; }
public ICommand GenerateRestitutionCommand { get; }
public ICommand PrintRestitutionCommand { get; }

// Initialisation
SwitchToRestitutionCommand = new RelayCommand(SwitchToRestitution);
GenerateRestitutionCommand = new RelayCommand(async () => await GenerateRestitutionAsync());
PrintRestitutionCommand = new RelayCommand(PrintRestitution);

// Méthodes

private void SwitchToRestitution()
{
    IsInterrogatoireMode = false;
    IsInClinicalMode = false;
    IsSynthesisMode = false;
    IsRestitutionMode = true;
    
    // Pré-remplir depuis synthèse initiale
    InitializeRestitutionFromSynthesis();
}

private void InitializeRestitutionFromSynthesis()
{
    if (!string.IsNullOrEmpty(_synthesisContent))
    {
        // Parser synthèse pour extraire points forts et difficultés
        var (pointsForts, difficultes) = ExtractKeyPointsFromSynthesis(_synthesisContent);
        
        Restitution.PointsForts = pointsForts;
        Restitution.Difficultes = difficultes;
        Restitution.NombreSeances = 2; // Défaut
    }
}

private (string pointsForts, string difficultes) ExtractKeyPointsFromSynthesis(string synthesis)
{
    // Parser la synthèse initiale générée
    // Chercher sections "Points Forts" et "Difficultés"
    try
    {
        var lines = synthesis.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var pointsForts = "";
        var difficultes = "";
        
        bool inPointsForts = false;
        bool inDifficulties = false;
        
        foreach (var line in lines)
        {
            if (line.Contains("Forces") || line.Contains("Points Forts") || line.Contains("Positif"))
                inPointsForts = true;
            else if (line.Contains("Difficultés") || line.Contains("Problème"))
            {
                inPointsForts = false;
                inDifficulties = true;
            }
            else if (line.StartsWith("#"))
            {
                inPointsForts = false;
                inDifficulties = false;
            }
            
            if (inPointsForts && !line.StartsWith("#"))
                pointsForts += line + "\n";
            if (inDifficulties && !line.StartsWith("#"))
                difficultes += line + "\n";
        }
        
        return (pointsForts.Trim(), difficultes.Trim());
    }
    catch
    {
        return ("", "");
    }
}

private async Task GenerateRestitutionAsync()
{
    IsGeneratingRestitution = true;
    RestitutionStatusMessage = "⏳ Génération du document infographie...";

    try
    {
        // Construire le prompt pour LLM
        var prompt = BuildRestitutionPrompt();
        
        var (success, htmlContent, _) = await _llmService.ChatAsync(prompt, new(), maxTokens: 2000);
        
        if (success)
        {
            // Sauvegarder HTML temporaire
            var htmlPath = SaveTemporaryHtml(htmlContent);
            Restitution.GeneratedHtmlPath = htmlPath;
            
            // Convertir HTML → PDF via LibreOffice
            var pdfPath = await ConvertHtmlToPdfAsync(htmlPath);
            
            if (!string.IsNullOrEmpty(pdfPath) && File.Exists(pdfPath))
            {
                Restitution.GeneratedPdfPath = pdfPath;
                RestitutionStatusMessage = "✅ Document généré avec succès (prêt à imprimer)";
                
                // Afficher le PDF dans le viewer
                DisplayPdfPreview(pdfPath);
            }
            else
            {
                RestitutionStatusMessage = "❌ Erreur conversion PDF";
            }
        }
        else
        {
            RestitutionStatusMessage = "❌ Erreur génération: " + htmlContent;
        }
    }
    catch (Exception ex)
    {
        RestitutionStatusMessage = $"❌ Erreur: {ex.Message}";
    }
    finally
    {
        IsGeneratingRestitution = false;
    }
}

private string BuildRestitutionPrompt()
{
    return $@"Génère une PAGE DE RESTITUTION HTML INFOGRAPHIQUE pour les parents.

Cette page sera imprimée et donnée aux parents à la fin de la consultation.

DONNÉES À INCLURE:
- Enfant: {_currentConsultation?.Prenom} {_currentConsultation?.Nom} ({_currentConsultation?.Age} ans)
- Date: {DateTime.Now:dd/MM/yyyy}

POINTS FORTS (Ce qui va bien):
{Restitution.PointsForts}

DIFFICULTÉS CONSTATÉES:
{Restitution.Difficultes}

PLAN D'ÉVALUATION:
Nous proposons {Restitution.NombreSeances} séance(s) d'évaluation pour explorer en détail les capacités et les difficultés de votre enfant.

---

INSTRUCTIONS DE DESIGN:
✅ HTML + CSS intégré (pas JavaScript)
✅ Couleurs: 
   - Bleu (#3498DB) pour points forts
   - Orange (#E67E22) pour difficultés
   - Gris (#7F8C8D) pour plan d'évaluation
✅ Flèches simples (→ ↓) pour guider la lecture
✅ Police: Segoe UI, lisible
✅ Structure claire avec séquences
✅ 1 PAGE seulement (A4 portrait)
✅ Pas d'images, juste infographie textuelle
✅ Marges: 40px, padding: 20px

Format général:
<html>
<head>
<style>
/* CSS pour 1 page A4 */
body {{ font-family: 'Segoe UI'; max-width: 800px; margin: 40px auto; padding: 20px; }}
.header {{ text-align: center; border-bottom: 3px solid #3498DB; padding-bottom: 20px; }}
.card {{ border-left: 4px solid #color; padding: 15px; margin: 15px 0; background: #f9f9f9; }}
.strengths {{ border-left-color: #27AE60; }}
.difficulties {{ border-left-color: #E74C3C; }}
.evaluation {{ border-left-color: #3498DB; }}
.arrow {{ color: #3498DB; font-size: 20px; margin: 10px 0; text-align: center; }}
</style>
</head>
<body>
<!-- CONTENU -->
</body>
</html>

Génère maintenant le HTML complet et bien formaté.";
}

private string SaveTemporaryHtml(string htmlContent)
{
    try
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "MedCompanion_Restitution");
        Directory.CreateDirectory(tempDir);
        
        var fileName = $"restitution_{DateTime.Now:yyyyMMdd_HHmmss}.html";
        var filePath = Path.Combine(tempDir, fileName);
        
        File.WriteAllText(filePath, htmlContent, Encoding.UTF8);
        return filePath;
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"[Restitution] Erreur sauvegarde HTML: {ex.Message}");
        return "";
    }
}

private async Task<string> ConvertHtmlToPdfAsync(string htmlPath)
{
    // Utiliser HtmlToPdfService (à créer, similaire à DocxToPdfService)
    try
    {
        var htmlToPdfService = new HtmlToPdfService();
        var pdfPath = Path.ChangeExtension(htmlPath, ".pdf");
        
        var success = await Task.Run(() => htmlToPdfService.ConvertHtmlToPdf(htmlPath, pdfPath));
        
        return success ? pdfPath : "";
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"[Restitution] Erreur conversion PDF: {ex.Message}");
        return "";
    }
}

private void DisplayPdfPreview(string pdfPath)
{
    // À implémenter: afficher PDF dans viewer XAML
    // Utiliser WebView2 ou PdfViewer (si disponible)
    OnPdfReady?.Invoke(this, pdfPath);
}

private void PrintRestitution()
{
    if (!File.Exists(Restitution.GeneratedPdfPath))
    {
        RestitutionStatusMessage = "❌ Aucun document à imprimer";
        return;
    }

    try
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = Restitution.GeneratedPdfPath,
            UseShellExecute = true,
            Verb = "print"
        });
        
        RestitutionStatusMessage = "✅ Envoyé à l'imprimante";
    }
    catch (Exception ex)
    {
        RestitutionStatusMessage = $"❌ Erreur impression: {ex.Message}";
    }
}

public event EventHandler<string>? OnPdfReady;
```

---

## Service: HtmlToPdfService

### Services/HtmlToPdfService.cs — CRÉER

```csharp
using System;
using System.Diagnostics;
using System.IO;

namespace MedCompanion.Services;

/// <summary>
/// Service de conversion HTML vers PDF utilisant LibreOffice
/// </summary>
public class HtmlToPdfService
{
    private readonly string _libreOfficePath = string.Empty;

    public HtmlToPdfService()
    {
        // Chemins possibles pour LibreOffice
        var possiblePaths = new[]
        {
            @"C:\Program Files\LibreOffice\program\soffice.exe",
            @"C:\Program Files (x86)\LibreOffice\program\soffice.exe",
            @"C:\Program Files\LibreOffice 24\program\soffice.exe",
            @"C:\Program Files\LibreOffice 7\program\soffice.exe"
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
            {
                _libreOfficePath = path;
                System.Diagnostics.Debug.WriteLine($"[HtmlToPdfService] LibreOffice trouvé: {path}");
                break;
            }
        }

        if (string.IsNullOrEmpty(_libreOfficePath))
        {
            System.Diagnostics.Debug.WriteLine("[HtmlToPdfService] ⚠️ LibreOffice non trouvé");
        }
    }

    /// <summary>
    /// Convertit un fichier HTML en PDF avec LibreOffice
    /// </summary>
    public bool ConvertHtmlToPdf(string htmlPath, string pdfPath)
    {
        try
        {
            if (string.IsNullOrEmpty(_libreOfficePath))
            {
                System.Diagnostics.Debug.WriteLine("[HtmlToPdfService] LibreOffice non disponible");
                return false;
            }

            if (!File.Exists(htmlPath))
            {
                System.Diagnostics.Debug.WriteLine($"[HtmlToPdfService] Fichier HTML introuvable: {htmlPath}");
                return false;
            }

            var outputDir = Path.GetDirectoryName(pdfPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            var arguments = $"--headless --convert-to pdf --outdir \"{outputDir}\" \"{htmlPath}\"";

            System.Diagnostics.Debug.WriteLine($"[HtmlToPdfService] Conversion: {htmlPath} → {pdfPath}");

            var processInfo = new ProcessStartInfo
            {
                FileName = _libreOfficePath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using (var process = Process.Start(processInfo))
            {
                if (process == null)
                {
                    System.Diagnostics.Debug.WriteLine("[HtmlToPdfService] Impossible de démarrer LibreOffice");
                    return false;
                }

                process.WaitForExit(30000);

                if (!process.HasExited)
                {
                    process.Kill();
                    System.Diagnostics.Debug.WriteLine("[HtmlToPdfService] Timeout");
                    return false;
                }
            }

            var expectedPdfPath = Path.Combine(outputDir ?? "", Path.GetFileNameWithoutExtension(htmlPath) + ".pdf");

            if (File.Exists(expectedPdfPath))
            {
                if (expectedPdfPath != pdfPath && File.Exists(expectedPdfPath))
                {
                    if (File.Exists(pdfPath))
                        File.Delete(pdfPath);
                    File.Move(expectedPdfPath, pdfPath);
                }

                System.Diagnostics.Debug.WriteLine($"[HtmlToPdfService] ✅ Conversion réussie");
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HtmlToPdfService] Erreur: {ex.Message}");
            return false;
        }
    }
}
```

---

## XAML

### ConsultationModeControl.xaml — AJOUTER

```xaml
<!-- Tab 4: Restitution -->
<StackPanel Orientation="Horizontal" Margin="0,0,0,12" Height="40">
    <!-- Ajouter 4e tab -->
    <Button Content="📋 Restitution"
            Command="{Binding SwitchToRestitutionCommand}"
            Height="40" VerticalAlignment="Top"
            Background="#E74C3C" Foreground="White" Padding="12,0" Margin="4,0,0,0"/>
</StackPanel>

<!-- Restitution Panel -->
<Grid Visibility="{Binding IsRestitutionMode, Converter={StaticResource BoolToVis}}">
    <Grid.RowDefinitions>
        <RowDefinition Height="Auto"/>     <!-- Formulaire -->
        <RowDefinition Height="*"/>        <!-- PDF Preview -->
        <RowDefinition Height="Auto"/>     <!-- Boutons -->
    </Grid.RowDefinitions>

    <!-- Formulaire simple -->
    <Border Grid.Row="0" Background="#F8F9FA" Padding="12" Margin="0,0,0,12">
        <StackPanel>
            <TextBlock Text="📄 Formulaire Restitution" FontWeight="Bold" FontSize="14" Margin="0,0,0,12"/>
            
            <!-- Points forts -->
            <TextBlock Text="Points Forts (ce qui va bien):" FontWeight="SemiBold" Margin="0,0,0,6"/>
            <TextBox Text="{Binding Restitution.PointsForts, UpdateSourceTrigger=PropertyChanged}"
                    AcceptsReturn="True" Height="60" Padding="8" TextWrapping="Wrap" Margin="0,0,0,12"/>
            
            <!-- Difficultés -->
            <TextBlock Text="Difficultés Constatées:" FontWeight="SemiBold" Margin="0,0,0,6"/>
            <TextBox Text="{Binding Restitution.Difficultes, UpdateSourceTrigger=PropertyChanged}"
                    AcceptsReturn="True" Height="60" Padding="8" TextWrapping="Wrap" Margin="0,0,0,12"/>
            
            <!-- Nombre séances -->
            <TextBlock Text="Nombre de Séances d'Évaluation:" FontWeight="SemiBold" Margin="0,0,0,6"/>
            <Slider Value="{Binding Restitution.NombreSeances}" Minimum="1" Maximum="5" 
                   SmallChange="1" LargeChange="1" Margin="0,0,0,12"/>
            <TextBlock Text="{Binding Restitution.NombreSeances, StringFormat='{0} séance(s)'}" 
                      Foreground="#7F8C8D" FontSize="11"/>
        </StackPanel>
    </Border>

    <!-- PDF Preview -->
    <Border Grid.Row="1" Background="White" Padding="12" BorderThickness="1" BorderBrush="#DDD" Margin="0,0,0,12">
        <StackPanel>
            <TextBlock Text="📋 Aperçu Document (PDF)" FontWeight="Bold" FontSize="13" Margin="0,0,0,8"/>
            
            <!-- WebView2 pour afficher PDF -->
            <wpf:WebView2 x:Name="PdfViewer" Height="400"/>
            
            <TextBlock Text="{Binding RestitutionStatusMessage}" Foreground="#3498DB" Margin="0,8,0,0" FontSize="11"/>
        </StackPanel>
    </Border>

    <!-- Boutons action -->
    <StackPanel Grid.Row="2" Orientation="Horizontal">
        <Button Content="✨ Générer Document" 
                Command="{Binding GenerateRestitutionCommand}"
                IsEnabled="{Binding IsGeneratingRestitution, Converter={StaticResource InvertBool}}"
                Padding="12,8" Background="#27AE60" Foreground="White" Cursor="Hand" Margin="0,0,8,0"/>
        
        <Button Content="🖨️ Imprimer" 
                Command="{Binding PrintRestitutionCommand}"
                IsEnabled="{Binding Restitution.GeneratedPdfPath, Converter={StaticResource StringNotEmptyConverter}}"
                Padding="12,8" Background="#3498DB" Foreground="White" Cursor="Hand" Margin="0,0,8,0"/>

        <Button Content="← Retour Synthèse" 
                Command="{Binding SwitchToSynthesisCommand}"
                Padding="12,8" Background="#95A5A6" Foreground="White" Cursor="Hand"/>
    </StackPanel>
</Grid>
```

---

## Phases Implémentation

### Phase A — Service HtmlToPdfService (~30 min)

1. Créer HtmlToPdfService.cs (copier/adapter DocxToPdfService)
2. Tester conversion HTML → PDF via LibreOffice

### Phase B — Modèles & ViewModel (~1h)

1. Ajouter RestitutionAuxParents dans ConsultationModels.cs
2. Ajouter propriétés ViewModel
3. Implémenter GenerateRestitutionAsync() avec prompt LLM

### Phase C — XAML UI (~45 min)

1. Ajouter 4e tab navigation
2. Formulaire (TextBox points forts, difficultés, slider séances)
3. PDF viewer (WebView2)
4. Boutons action

### Phase D — Intégration & Tests (~45 min)

1. Connecter PDF viewer avec fichier généré
2. Tester impression
3. Tester flow complet

---

## Estimation Totale

| Phase | Durée |
|-------|-------|
| A | 30 min |
| B | 1h |
| C | 45 min |
| D | 45 min |
| **Total** | **~3h** |

---

## Structure Fichiers

### Par consultation: `2026/consultations/premiere_consultation_20250513_HHMMSS/`

```
├─ interrogatoire.json
├─ observations_cliniques.json
├─ synthese_initiale.md
├─ restitution/
│   ├─ restitution_brut_20250513_143000.html
│   └─ restitution_final_20250513_143000.pdf
└─ metadata.json
```

---

## Notes

- **Design infographique**: Flèches, couleurs, structure claire
- **1 page seulement**: A4 portrait, marges 40px
- **Basée sur synthèse initiale**: Extraction auto des points clés
- **Flexible**: Med peut ajuster textes avant impression
- **LibreOffice**: Cohérent avec DocxToPdfService existant
- **Imprimable**: Prêt à donner aux parents immédiatement
