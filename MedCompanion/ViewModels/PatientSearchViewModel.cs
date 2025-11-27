using System.Collections.ObjectModel;
using System.Windows.Input;
using MedCompanion.Commands;
using MedCompanion.Models;
using MedCompanion.Services;

namespace MedCompanion.ViewModels;

/// <summary>
/// ViewModel pour la recherche et sélection de patients
/// Gère l'autocomplete, les suggestions, et la sélection
/// </summary>
public class PatientSearchViewModel : ViewModelBase
{
    private readonly PatientIndexService _patientIndex;
    private string _searchText = string.Empty;
    private bool _isPopupOpen;
    private PatientIndexEntry? _selectedPatient;
    private int _selectedSuggestionIndex = -1;
    private bool _showCreateOption;
    private bool _showingRecentPatients;

    /// <summary>
    /// Texte de recherche saisi par l'utilisateur
    /// </summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                OnSearchTextChanged();
            }
        }
    }

    /// <summary>
    /// Indique si le popup de suggestions est ouvert
    /// </summary>
    public bool IsPopupOpen
    {
        get => _isPopupOpen;
        set => SetProperty(ref _isPopupOpen, value);
    }

    /// <summary>
    /// Patient actuellement sélectionné
    /// </summary>
    public PatientIndexEntry? SelectedPatient
    {
        get => _selectedPatient;
        set
        {
            if (SetProperty(ref _selectedPatient, value))
            {
                OnPropertyChanged(nameof(HasSelectedPatient));
                PatientSelected?.Invoke(this, value);
            }
        }
    }

    /// <summary>
    /// Indique si un patient est sélectionné
    /// </summary>
    public bool HasSelectedPatient => SelectedPatient != null;

    /// <summary>
    /// Liste des suggestions de patients
    /// </summary>
    public ObservableCollection<PatientIndexEntry> Suggestions { get; } = new();

    /// <summary>
    /// Index de la suggestion actuellement sélectionnée
    /// </summary>
    public int SelectedSuggestionIndex
    {
        get => _selectedSuggestionIndex;
        set
        {
            if (SetProperty(ref _selectedSuggestionIndex, value))
            {
                // Notifier que ValidateCommand peut maintenant s'exécuter
                (ValidateCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Indique si l'option "Créer patient" doit être affichée (aucun résultat trouvé)
    /// </summary>
    public bool ShowCreateOption
    {
        get => _showCreateOption;
        set
        {
            if (SetProperty(ref _showCreateOption, value))
            {
                // Notifier ValidateCommand de réévaluer CanExecute
                (ValidateCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Indique si on affiche les patients récents (mode initial au focus)
    /// </summary>
    public bool ShowingRecentPatients
    {
        get => _showingRecentPatients;
        set => SetProperty(ref _showingRecentPatients, value);
    }

    /// <summary>
    /// Commande pour valider la sélection
    /// </summary>
    public ICommand ValidateCommand { get; }

    /// <summary>
    /// Commande pour créer un nouveau patient
    /// </summary>
    public ICommand CreatePatientCommand { get; }

    /// <summary>
    /// Commande pour naviguer vers le bas dans les suggestions
    /// </summary>
    public ICommand NavigateDownCommand { get; }

    /// <summary>
    /// Commande pour naviguer vers le haut dans les suggestions
    /// </summary>
    public ICommand NavigateUpCommand { get; }

    /// <summary>
    /// Commande pour fermer le popup
    /// </summary>
    public ICommand ClosePopupCommand { get; }

    /// <summary>
    /// Événement déclenché quand un patient est sélectionné
    /// </summary>
    public event EventHandler<PatientIndexEntry?>? PatientSelected;

    /// <summary>
    /// Événement déclenché pour demander la création d'un nouveau patient
    /// </summary>
    public event EventHandler<string>? CreatePatientRequested;

    public PatientSearchViewModel(PatientIndexService patientIndex)
    {
        _patientIndex = patientIndex;

        ValidateCommand = new RelayCommand(
            execute: () => ValidateSelection(),
            canExecute: () => Suggestions.Count > 0 || ShowCreateOption
        );

        CreatePatientCommand = new RelayCommand(
            execute: param => CreatePatientRequested?.Invoke(this, param as string ?? string.Empty)
        );

        NavigateDownCommand = new RelayCommand(
            execute: () => NavigateDown(),
            canExecute: () => IsPopupOpen && SelectedSuggestionIndex < Suggestions.Count - 1
        );

        NavigateUpCommand = new RelayCommand(
            execute: () => NavigateUp(),
            canExecute: () => IsPopupOpen && SelectedSuggestionIndex > 0
        );

        ClosePopupCommand = new RelayCommand(
            execute: () => ClosePopup()
        );
    }

    /// <summary>
    /// Appelé quand le texte de recherche change
    /// </summary>
    private void OnSearchTextChanged()
    {
        // Nettoyage agressif du texte : supprimer caractères invisibles, espaces insécables, etc.
        var cleanedText = string.IsNullOrWhiteSpace(SearchText)
            ? string.Empty
            : System.Text.RegularExpressions.Regex.Replace(SearchText, @"[\s\u00A0\u200B\u200C\u200D\uFEFF]+", " ").Trim();

        // Si texte vide → Afficher patients récents
        if (string.IsNullOrWhiteSpace(cleanedText))
        {
            ShowRecentPatients();
            return;
        }

        // Sinon, mode recherche normale
        ShowingRecentPatients = false;

        // Moins de 3 caractères → Fermer le popup
        if (cleanedText.Length < 3)
        {
            IsPopupOpen = false;
            Suggestions.Clear();
            ShowCreateOption = false;
            return;
        }

        // Si le texte ressemble à un bloc Doctolib, extraire uniquement le nom/prénom pour la recherche
        var searchQuery = ExtractSearchQueryFromDoctolibFormat(cleanedText);

        // DEBUG: Afficher dans la console pour diagnostic
        System.Diagnostics.Debug.WriteLine($"[PatientSearch] Original: '{cleanedText}'");
        System.Diagnostics.Debug.WriteLine($"[PatientSearch] Query extracted: '{searchQuery}'");

        // Rechercher avec le texte nettoyé (ou le nom/prénom extrait)
        var results = _patientIndex.Search(searchQuery, 10);

        // Si aucun résultat et que le query contient 2 mots, essayer l'ordre inversé
        // Ex: "ABDELKADER Zakaria" → "Zakaria ABDELKADER"
        if (results.Count == 0 && searchQuery.Contains(' '))
        {
            var parts = searchQuery.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                var invertedQuery = $"{parts[1]} {parts[0]}";
                System.Diagnostics.Debug.WriteLine($"[PatientSearch] Trying inverted order: '{invertedQuery}'");
                results = _patientIndex.Search(invertedQuery, 10);
            }
        }

        // DEBUG: Afficher les résultats
        System.Diagnostics.Debug.WriteLine($"[PatientSearch] Results found: {results.Count}");

        Suggestions.Clear();
        foreach (var patient in results)
        {
            Suggestions.Add(patient);
        }

        // Si aucun résultat → Afficher option "Créer patient"
        if (Suggestions.Count == 0)
        {
            ShowCreateOption = true;
            IsPopupOpen = true; // Garder le popup ouvert
        }
        else
        {
            ShowCreateOption = false;
            IsPopupOpen = true; // Ouvrir le popup si des résultats
        }

        SelectedSuggestionIndex = -1;
        
        // IMPORTANT : Notifier ValidateCommand que Suggestions a changé
        (ValidateCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// Valide la sélection actuelle
    /// </summary>
    public void ValidateSelection()
    {
        // Si option "Créer patient" affichée
        if (ShowCreateOption)
        {
            CreatePatientRequested?.Invoke(this, SearchText);
            ClearSearch();
            return;
        }

        // Si aucune sélection mais des suggestions → Sélectionner le premier résultat
        if (SelectedSuggestionIndex < 0 && Suggestions.Count > 0)
        {
            SelectedPatient = Suggestions[0];
            ClearSearch();
            return;
        }

        // Sinon, sélectionner le patient à l'index choisi
        if (SelectedSuggestionIndex >= 0 && SelectedSuggestionIndex < Suggestions.Count)
        {
            SelectedPatient = Suggestions[SelectedSuggestionIndex];
            ClearSearch();
        }
    }

    /// <summary>
    /// Sélectionne un patient directement
    /// </summary>
    public void SelectPatient(PatientIndexEntry patient)
    {
        SelectedPatient = patient;
        ClearSearch();
    }

    /// <summary>
    /// Navigue vers le haut dans les suggestions
    /// </summary>
    public void NavigateUp()
    {
        if (SelectedSuggestionIndex > 0)
        {
            SelectedSuggestionIndex--;
        }
    }

    /// <summary>
    /// Navigue vers le bas dans les suggestions
    /// </summary>
    public void NavigateDown()
    {
        if (SelectedSuggestionIndex < Suggestions.Count - 1)
        {
            SelectedSuggestionIndex++;
        }
    }

    /// <summary>
    /// Efface la recherche et ferme le popup
    /// </summary>
    public void ClearSearch()
    {
        SearchText = string.Empty;
        IsPopupOpen = false;
        Suggestions.Clear();
        ShowCreateOption = false;
        SelectedSuggestionIndex = -1;
        
        // Notifier ValidateCommand que les conditions ont changé
        (ValidateCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// Ferme le popup sans effacer la recherche
    /// </summary>
    public void ClosePopup()
    {
        IsPopupOpen = false;
    }

    /// <summary>
    /// Affiche les 5 derniers patients consultés
    /// </summary>
    private void ShowRecentPatients()
    {
        var recentPatients = _patientIndex.GetRecentPatients()
            .Take(5)
            .ToList();

        Suggestions.Clear();
        foreach (var patient in recentPatients)
        {
            Suggestions.Add(patient);
        }

        ShowingRecentPatients = true;
        ShowCreateOption = false;
        
        if (Suggestions.Count > 0)
        {
            IsPopupOpen = true;
        }
        else
        {
            IsPopupOpen = false;
        }
    }

    /// <summary>
    /// Appelé quand la barre de recherche reçoit le focus
    /// </summary>
    public void OnSearchBoxGotFocus()
    {
        // Si texte vide, afficher les patients récents
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            ShowRecentPatients();
        }
    }

    /// <summary>
    /// Extrait le nom/prénom d'un texte qui pourrait être au format Doctolib
    /// Si ce n'est pas du Doctolib, retourne le texte tel quel
    /// </summary>
    private string ExtractSearchQueryFromDoctolibFormat(string input)
    {
        // PRIORITÉ 1: Détecter le format multi-lignes avec "né(e)"
        // Exemple: "Marianna\nné(e) MOULATOUA\nF, 02/12/2018"
        var multiLineNeePattern = System.Text.RegularExpressions.Regex.Match(
            input,
            @"^(.+?)\s+n[eé]\(e\)\s+([A-Z\s]+?)[\n\r\s]+[HFM]\s*,",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );
        if (multiLineNeePattern.Success)
        {
            var prenom = multiLineNeePattern.Groups[1].Value.Trim();
            var nom = multiLineNeePattern.Groups[2].Value.Trim();
            System.Diagnostics.Debug.WriteLine($"[PatientSearch] Format multi-lignes 'né(e)' détecté: '{prenom} {nom}'");
            return $"{prenom} {nom}";
        }

        // PRIORITÉ 2: Si le texte contient une virgule suivie d'une date (format Doctolib), extraire uniquement la première partie
        // Exemples:
        // "ABDELKADER Zakaria\nH, 23/09/2015" → "ABDELKADER Zakaria"
        // "ABDELKADER Zakaria H, 23/09/2015" → "ABDELKADER Zakaria"
        var datePattern = System.Text.RegularExpressions.Regex.Match(input, @"^(.+?)[\n\r\s]+[HFM]\s*,\s*\d{2}[/-]\d{2}[/-]\d{4}");
        if (datePattern.Success)
        {
            var extracted = datePattern.Groups[1].Value.Trim();
            System.Diagnostics.Debug.WriteLine($"[PatientSearch] Format avec date détecté: '{extracted}'");
            return extracted;
        }

        // PRIORITÉ 3: Si le texte contient un retour à la ligne, prendre uniquement la première ligne (probable nom/prénom)
        if (input.Contains('\n'))
        {
            var firstLine = input.Split('\n')[0].Trim();

            // Vérifier que la première ligne ressemble à un nom (pas une adresse, pas un sexe seul, etc.)
            if (firstLine.Length > 3 && !firstLine.StartsWith("H,") && !firstLine.StartsWith("F,") && !firstLine.StartsWith("M,"))
            {
                System.Diagnostics.Debug.WriteLine($"[PatientSearch] Première ligne extraite: '{firstLine}'");
                return firstLine;
            }
        }

        // Sinon, retourner le texte tel quel
        System.Diagnostics.Debug.WriteLine($"[PatientSearch] Aucun pattern détecté, texte retourné tel quel: '{input}'");
        return input;
    }
}
