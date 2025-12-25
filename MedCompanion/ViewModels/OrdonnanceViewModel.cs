using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Input;
using MedCompanion.Commands;
using MedCompanion.Models;
using MedCompanion.Services;

namespace MedCompanion.ViewModels;

/// <summary>
/// ViewModel pour la gestion des ordonnances (onglet Ordonnances)
/// </summary>
public class OrdonnanceViewModel : ViewModelBase
{
    private readonly OrdonnanceService _ordonnanceService;
    private string? _currentPatientName;
    private PatientMetadata? _selectedPatient;
    
    private ObservableCollection<OrdonnanceItem> _ordonnances;
    private OrdonnanceItem? _selectedOrdonnance;
    private string _previewMarkdown;
    private string _ordonnancesCount;
    
    public OrdonnanceViewModel(OrdonnanceService ordonnanceService)
    {
        _ordonnanceService = ordonnanceService;
        _ordonnances = new ObservableCollection<OrdonnanceItem>();
        _previewMarkdown = string.Empty;
        _ordonnancesCount = "0 ordonnances";
        
        // Initialiser les commandes
        GenerateIDECommand = new RelayCommand(_ => OnGenerateIDE(), _ => CanGenerateIDE());
        DeleteCommand = new RelayCommand(_ => OnDelete(), _ => CanDelete());
        OpenDocxCommand = new RelayCommand(_ => OnOpenDocx(), _ => CanOpenDocx());
    }
    
    #region Properties
    
    /// <summary>
    /// Patient actuellement sélectionné
    /// </summary>
    public PatientMetadata? SelectedPatient
    {
        get => _selectedPatient;
        set
        {
            if (SetProperty(ref _selectedPatient, value))
            {
                _currentPatientName = value?.NomComplet;
            }
        }
    }
    
    /// <summary>
    /// Liste des ordonnances
    /// </summary>
    public ObservableCollection<OrdonnanceItem> Ordonnances
    {
        get => _ordonnances;
        set => SetProperty(ref _ordonnances, value);
    }
    
    /// <summary>
    /// Ordonnance sélectionnée
    /// </summary>
    public OrdonnanceItem? SelectedOrdonnance
    {
        get => _selectedOrdonnance;
        set
        {
            if (SetProperty(ref _selectedOrdonnance, value))
            {
                OnSelectedOrdonnanceChanged();
            }
        }
    }
    
    /// <summary>
    /// Contenu Markdown pour la preview
    /// </summary>
    public string PreviewMarkdown
    {
        get => _previewMarkdown;
        set => SetProperty(ref _previewMarkdown, value);
    }
    
    /// <summary>
    /// Compteur d'ordonnances (ex: "3 ordonnances")
    /// </summary>
    public string OrdonnancesCount
    {
        get => _ordonnancesCount;
        set => SetProperty(ref _ordonnancesCount, value);
    }
    
    /// <summary>
    /// Indique si une ordonnance est sélectionnée
    /// </summary>
    public bool IsOrdonnanceSelected => SelectedOrdonnance != null;
    
    /// <summary>
    /// Indique si le DOCX est disponible pour l'ordonnance sélectionnée
    /// </summary>
    public bool IsDocxAvailable => SelectedOrdonnance?.DocxPath != null && File.Exists(SelectedOrdonnance.DocxPath);
    
    #endregion
    
    #region Commands
    
    public ICommand GenerateIDECommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand OpenDocxCommand { get; }
    
    #endregion
    
    #region Events
    
    /// <summary>
    /// Événement déclenché quand l'utilisateur demande à générer une ordonnance IDE
    /// (MainWindow devra ouvrir le dialogue OrdonnanceIDEDialog)
    /// </summary>
    public event EventHandler? GenerateIDERequested;
    
    /// <summary>
    /// Événement déclenché après une action réussie (création/suppression)
    /// </summary>
    public event EventHandler<string>? ActionCompleted;
    
    #endregion
    
    #region Public Methods
    
    /// <summary>
    /// Charge les ordonnances pour un patient donné
    /// </summary>
    public void LoadOrdonnances(string patientName)
    {
        _currentPatientName = patientName;
        
        try
        {
            var ordonnancesList = _ordonnanceService.GetOrdonnances(patientName);
            
            Ordonnances.Clear();
            
            foreach (var (date, type, preview, mdPath, docxPath) in ordonnancesList)
            {
                Ordonnances.Add(new OrdonnanceItem
                {
                    Date = date,
                    Type = type,
                    Preview = preview,
                    MdPath = mdPath,
                    DocxPath = docxPath
                });
            }
            
            UpdateOrdonnancesCount();
            
            // Désélectionner
            SelectedOrdonnance = null;
            PreviewMarkdown = string.Empty;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[OrdonnanceViewModel] Erreur LoadOrdonnances: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Charge les ordonnances du patient actuellement sélectionné
    /// </summary>
    public void LoadOrdonnances()
    {
        // Utiliser SelectedPatient directement pour plus de robustesse
        var patientName = _selectedPatient?.NomComplet ?? _currentPatientName;

        if (!string.IsNullOrEmpty(patientName))
        {
            Debug.WriteLine($"[OrdonnanceViewModel] 📋 LoadOrdonnances pour patient: {patientName}");
            LoadOrdonnances(patientName);
        }
        else
        {
            Debug.WriteLine("[OrdonnanceViewModel] ⚠️ LoadOrdonnances: Aucun patient sélectionné");
            Debug.WriteLine($"  _currentPatientName = '{_currentPatientName ?? "NULL"}'");
            Debug.WriteLine($"  _selectedPatient = {(_selectedPatient == null ? "NULL" : _selectedPatient.NomComplet)}");
        }
    }
    
    /// <summary>
    /// Sauvegarde une ordonnance IDE
    /// </summary>
    public (bool success, string message, string? mdPath, string? docxPath, string? pdfPath) SaveOrdonnanceIDE(OrdonnanceIDE ordonnance)
    {
        // Utiliser SelectedPatient directement pour plus de robustesse
        var patientName = _selectedPatient?.NomComplet ?? _currentPatientName;

        if (string.IsNullOrEmpty(patientName))
        {
            Debug.WriteLine("[OrdonnanceViewModel] ❌ ERREUR SaveOrdonnanceIDE: Aucun patient sélectionné");
            Debug.WriteLine($"  _currentPatientName = '{_currentPatientName ?? "NULL"}'");
            Debug.WriteLine($"  _selectedPatient = {(_selectedPatient == null ? "NULL" : _selectedPatient.NomComplet)}");
            return (false, "Aucun patient sélectionné", null, null, null);
        }

        Debug.WriteLine($"[OrdonnanceViewModel] 💾 SaveOrdonnanceIDE pour patient: {patientName}");
        var result = _ordonnanceService.SaveOrdonnanceIDE(patientName, ordonnance);
        Debug.WriteLine($"[OrdonnanceViewModel] Résultat sauvegarde: success={result.success}, message={result.message}");

        return result;
    }

    /// <summary>
    /// Sauvegarde une ordonnance de biologie
    /// </summary>
    public (bool success, string message, string? mdPath, string? docxPath, string? pdfPath) SaveOrdonnanceBiologie(OrdonnanceBiologie ordonnance)
    {
        // Utiliser SelectedPatient directement pour plus de robustesse
        var patientName = _selectedPatient?.NomComplet ?? _currentPatientName;

        if (string.IsNullOrEmpty(patientName))
        {
            Debug.WriteLine("[OrdonnanceViewModel] ❌ ERREUR SaveOrdonnanceBiologie: Aucun patient sélectionné");
            Debug.WriteLine($"  _currentPatientName = '{_currentPatientName ?? "NULL"}'");
            Debug.WriteLine($"  _selectedPatient = {(_selectedPatient == null ? "NULL" : _selectedPatient.NomComplet)}");
            return (false, "Aucun patient sélectionné", null, null, null);
        }

        Debug.WriteLine($"[OrdonnanceViewModel] 💾 SaveOrdonnanceBiologie pour patient: {patientName}");
        var result = _ordonnanceService.SaveOrdonnanceBiologie(patientName, ordonnance);
        Debug.WriteLine($"[OrdonnanceViewModel] Résultat sauvegarde: success={result.success}, message={result.message}");

        return result;
    }

    /// <summary>
    /// Supprime une ordonnance par son chemin
    /// </summary>
    public (bool success, string message) DeleteOrdonnance(string mdPath)
    {
        return _ordonnanceService.DeleteOrdonnance(mdPath);
    }
    
    /// <summary>
    /// Supprime l'ordonnance sélectionnée
    /// </summary>
    public void DeleteSelectedOrdonnance()
    {
        if (SelectedOrdonnance == null) return;
        
        var (success, message) = _ordonnanceService.DeleteOrdonnance(SelectedOrdonnance.MdPath);
        
        if (success)
        {
            Ordonnances.Remove(SelectedOrdonnance);
            SelectedOrdonnance = null;
            UpdateOrdonnancesCount();
            
            ActionCompleted?.Invoke(this, message);
        }
        else
        {
            ActionCompleted?.Invoke(this, $"❌ {message}");
        }
    }
    
    /// <summary>
    /// Recharge les ordonnances du patient courant
    /// </summary>
    public void RefreshOrdonnances()
    {
        if (!string.IsNullOrEmpty(_currentPatientName))
        {
            LoadOrdonnances(_currentPatientName);
        }
    }
    
    #endregion
    
    #region Private Methods
    
    private void OnSelectedOrdonnanceChanged()
    {
        // Charger le contenu de l'ordonnance sélectionnée
        if (SelectedOrdonnance != null && File.Exists(SelectedOrdonnance.MdPath))
        {
            try
            {
                PreviewMarkdown = File.ReadAllText(SelectedOrdonnance.MdPath);
            }
            catch (Exception ex)
            {
                PreviewMarkdown = $"Erreur lors de la lecture: {ex.Message}";
            }
        }
        else
        {
            PreviewMarkdown = string.Empty;
        }
        
        // Notifier les changements de propriétés dérivées
        OnPropertyChanged(nameof(IsOrdonnanceSelected));
        OnPropertyChanged(nameof(IsDocxAvailable));
    }
    
    private void UpdateOrdonnancesCount()
    {
        var count = Ordonnances.Count;
        OrdonnancesCount = count switch
        {
            0 => "0 ordonnances",
            1 => "1 ordonnance",
            _ => $"{count} ordonnances"
        };
    }
    
    private bool CanGenerateIDE()
    {
        return !string.IsNullOrEmpty(_currentPatientName);
    }
    
    private void OnGenerateIDE()
    {
        // Déclencher l'événement pour que MainWindow ouvre le dialogue
        GenerateIDERequested?.Invoke(this, EventArgs.Empty);
    }
    
    private bool CanDelete()
    {
        return IsOrdonnanceSelected;
    }
    
    private void OnDelete()
    {
        DeleteSelectedOrdonnance();
    }
    
    private bool CanOpenDocx()
    {
        return IsDocxAvailable;
    }
    
    private void OnOpenDocx()
    {
        if (SelectedOrdonnance?.DocxPath != null && File.Exists(SelectedOrdonnance.DocxPath))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = SelectedOrdonnance.DocxPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                ActionCompleted?.Invoke(this, $"❌ Erreur ouverture DOCX: {ex.Message}");
            }
        }
    }
    
    #endregion
}

/// <summary>
/// Représente une ordonnance dans la liste
/// </summary>
public class OrdonnanceItem
{
    public DateTime Date { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Preview { get; set; } = string.Empty;
    public string MdPath { get; set; } = string.Empty;
    public string? DocxPath { get; set; }
}
