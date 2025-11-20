using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using MedCompanion.Commands;
using MedCompanion.Models;
using MedCompanion.Services;

namespace MedCompanion.ViewModels
{
    public class PromptsAnalysisViewModel : ViewModelBase
    {
        private readonly PromptConfigService _promptService;
        private readonly PromptReformulationService _reformulationService;
        private List<PromptConfig> _allPrompts;
        
        private string _selectedModuleFilter = "Tous les modules";
        private PromptConfig? _selectedPrompt;
        private string _customPromptText = "";
        private string _reformulationRequest = "";
        private bool _isReformulating = false;
        
        public PromptsAnalysisViewModel(PromptConfigService promptService, PromptReformulationService reformulationService)
        {
            _promptService = promptService;
            _reformulationService = reformulationService;
            _allPrompts = _promptService.GetAllPrompts()?.Values?.ToList() ?? new List<PromptConfig>();
            
            // IMPORTANT: Initialiser les commandes D'ABORD (avant de charger les prompts)
            // Sinon, le setter de CustomPromptText va essayer d'appeler SaveCustomCommand.RaiseCanExecuteChanged()
            // alors que SaveCustomCommand n'existe pas encore → NullReferenceException
            SaveCustomCommand = new RelayCommand(
                _ => SaveCustomPrompt(),
                _ => SelectedPrompt != null && !string.IsNullOrWhiteSpace(CustomPromptText)
            );
            
            ActivateCommand = new RelayCommand(
                _ => ToggleActivation(),
                _ => SelectedPrompt != null && !string.IsNullOrEmpty(SelectedPrompt.CustomPrompt)
            );
            
            RestoreCommand = new RelayCommand(
                _ => RestoreDefault(),
                _ => SelectedPrompt != null && !string.IsNullOrEmpty(SelectedPrompt.CustomPrompt)
            );
            
            ReformulateCommand = new RelayCommand(
                async _ => await ReformulatePromptAsync(),
                _ => SelectedPrompt != null && !string.IsNullOrWhiteSpace(ReformulationRequest) && !IsReformulating
            );
            
            PromoteCommand = new RelayCommand(
                _ => PromoteCustomToDefault(),
                _ => SelectedPrompt != null && !string.IsNullOrEmpty(SelectedPrompt.CustomPrompt) && SelectedPrompt.IsCustomActive
            );
            
            RestoreOriginalCommand = new RelayCommand(
                _ => RestoreToOriginal(),
                _ => SelectedPrompt != null
            );
            
            CloseCommand = new RelayCommand(_ => RequestClose?.Invoke());
            
            // PUIS initialiser la liste filtrée et sélectionner le premier prompt
            UpdateFilteredPrompts();
            
            // Sélectionner le premier prompt par défaut
            if (FilteredPrompts.Count > 0)
            {
                SelectedPrompt = FilteredPrompts[0];
            }
        }
        
        // Properties
        
        public ObservableCollection<PromptConfig> FilteredPrompts { get; } = new();
        
        public string SelectedModuleFilter
        {
            get => _selectedModuleFilter;
            set
            {
                if (SetProperty(ref _selectedModuleFilter, value))
                {
                    UpdateFilteredPrompts();
                }
            }
        }
        
        public PromptConfig? SelectedPrompt
        {
            get => _selectedPrompt;
            set
            {
                if (SetProperty(ref _selectedPrompt, value))
                {
                    OnSelectedPromptChanged();
                }
            }
        }
        
        public string CustomPromptText
        {
            get => _customPromptText;
            set
            {
                if (SetProperty(ref _customPromptText, value))
                {
                    // Vérification de sécurité: la Command peut ne pas encore exister pendant l'initialisation
                    SaveCustomCommand?.RaiseCanExecuteChanged();
                }
            }
        }
        
        public string PromptName => SelectedPrompt?.Name ?? "";
        public string PromptDescription => SelectedPrompt?.Description ?? "";
        public string DefaultPromptText => SelectedPrompt?.DefaultPrompt ?? "";
        
        public bool HasCustomPrompt => SelectedPrompt != null && !string.IsNullOrEmpty(SelectedPrompt.CustomPrompt);
        public bool IsCustomActive => SelectedPrompt?.IsCustomActive ?? false;
        
        public string ActivateButtonText => IsCustomActive ? "✗ Désactiver" : "✓ Activer";
        public bool NoCustomPromptInfoVisible => !HasCustomPrompt;
        
        public string ReformulationRequest
        {
            get => _reformulationRequest;
            set
            {
                if (SetProperty(ref _reformulationRequest, value))
                {
                    ReformulateCommand?.RaiseCanExecuteChanged();
                }
            }
        }
        
        public bool IsReformulating
        {
            get => _isReformulating;
            set
            {
                if (SetProperty(ref _isReformulating, value))
                {
                    ReformulateCommand?.RaiseCanExecuteChanged();
                }
            }
        }
        
        // Commands
        
        public RelayCommand SaveCustomCommand { get; }
        public RelayCommand ActivateCommand { get; }
        public RelayCommand RestoreCommand { get; }
        public RelayCommand ReformulateCommand { get; }
        public RelayCommand PromoteCommand { get; }
        public RelayCommand RestoreOriginalCommand { get; }
        public RelayCommand CloseCommand { get; }
        
        // Events
        
        public event Action? RequestClose;
        
        // Methods
        
        private void UpdateFilteredPrompts()
        {
            FilteredPrompts.Clear();
            
            var filtered = _selectedModuleFilter == "Tous les modules"
                ? _allPrompts
                : _allPrompts.Where(p => p.Module == _selectedModuleFilter).ToList();
            
            foreach (var prompt in filtered)
            {
                FilteredPrompts.Add(prompt);
            }
            
            // Re-sélectionner si le prompt actuel est toujours dans la liste
            if (SelectedPrompt != null && !FilteredPrompts.Contains(SelectedPrompt))
            {
                SelectedPrompt = FilteredPrompts.FirstOrDefault();
            }
        }
        
        private void OnSelectedPromptChanged()
        {
            if (SelectedPrompt == null)
            {
                CustomPromptText = "";
            }
            else
            {
                CustomPromptText = string.IsNullOrEmpty(SelectedPrompt.CustomPrompt)
                    ? SelectedPrompt.DefaultPrompt
                    : SelectedPrompt.CustomPrompt;
            }
            
            // Notifier les changements de propriétés dépendantes
            OnPropertyChanged(nameof(PromptName));
            OnPropertyChanged(nameof(PromptDescription));
            OnPropertyChanged(nameof(DefaultPromptText));
            OnPropertyChanged(nameof(HasCustomPrompt));
            OnPropertyChanged(nameof(IsCustomActive));
            OnPropertyChanged(nameof(ActivateButtonText));
            OnPropertyChanged(nameof(NoCustomPromptInfoVisible));
            
            // Mettre à jour l'état des commandes
            SaveCustomCommand.RaiseCanExecuteChanged();
            ActivateCommand.RaiseCanExecuteChanged();
            RestoreCommand.RaiseCanExecuteChanged();
            PromoteCommand.RaiseCanExecuteChanged();
            RestoreOriginalCommand.RaiseCanExecuteChanged();
        }
        
        private void SaveCustomPrompt()
        {
            if (SelectedPrompt == null || string.IsNullOrWhiteSpace(CustomPromptText))
                return;
            
            var customText = CustomPromptText.Trim();
            
            // Confirmation si c'est la première fois
            if (string.IsNullOrEmpty(SelectedPrompt.CustomPrompt))
            {
                var result = MessageBox.Show(
                    $"Créer une version personnalisée de '{SelectedPrompt.Name}' ?\n\n" +
                    "Vous pourrez l'activer/désactiver à tout moment.",
                    "Confirmer",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question
                );
                
                if (result != MessageBoxResult.Yes)
                    return;
            }
            
            // Sauvegarder
            var (success, message) = _promptService.UpdateCustomPrompt(SelectedPrompt.Id, customText);
            
            if (success)
            {
                MessageBox.Show("✅ Prompt personnalisé sauvegardé avec succès.", "Succès",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                
                ReloadPrompts();
            }
            else
            {
                MessageBox.Show($"❌ {message}", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void ToggleActivation()
        {
            if (SelectedPrompt == null || string.IsNullOrEmpty(SelectedPrompt.CustomPrompt))
                return;
            
            bool newState = !SelectedPrompt.IsCustomActive;
            
            // Confirmation si activation
            if (newState)
            {
                var result = MessageBox.Show(
                    $"Activer la version personnalisée de '{SelectedPrompt.Name}' ?\n\n" +
                    "Cette version sera utilisée à la place de la version par défaut pour toutes les futures interactions IA.",
                    "Confirmer l'activation",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question
                );
                
                if (result != MessageBoxResult.Yes)
                    return;
            }
            
            // Activer/Désactiver
            var (success, message) = _promptService.SetCustomPromptActive(SelectedPrompt.Id, newState);
            
            if (success)
            {
                var statusMsg = newState ? "activée" : "désactivée";
                MessageBox.Show($"✅ Version personnalisée {statusMsg}.", "Succès",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                
                ReloadPrompts();
            }
            else
            {
                MessageBox.Show($"❌ {message}", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void RestoreDefault()
        {
            if (SelectedPrompt == null)
                return;
            
            // Confirmation
            var result = MessageBox.Show(
                $"Restaurer le prompt par défaut de '{SelectedPrompt.Name}' ?\n\n" +
                "⚠️ Votre version personnalisée sera supprimée définitivement.",
                "Confirmer la restauration",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );
            
            if (result != MessageBoxResult.Yes)
                return;
            
            // Restaurer
            var (success, message) = _promptService.RestoreDefault(SelectedPrompt.Id);
            
            if (success)
            {
                MessageBox.Show("✅ Prompt restauré par défaut.", "Succès",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                
                ReloadPrompts();
            }
            else
            {
                MessageBox.Show($"❌ {message}", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void ReloadPrompts()
        {
            var selectedId = SelectedPrompt?.Id;
            
            // Recharger depuis le service
            _promptService.ReloadConfig();
            _allPrompts = _promptService.GetAllPrompts().Values.ToList();
            
            // Mettre à jour la liste filtrée
            UpdateFilteredPrompts();
            
            // Re-sélectionner l'élément
            if (!string.IsNullOrEmpty(selectedId))
            {
                SelectedPrompt = FilteredPrompts.FirstOrDefault(p => p.Id == selectedId);
            }
        }
        
        private async System.Threading.Tasks.Task ReformulatePromptAsync()
        {
            if (SelectedPrompt == null || string.IsNullOrWhiteSpace(ReformulationRequest))
                return;
            
            try
            {
                IsReformulating = true;
                
                // TOUJOURS partir du prompt par défaut pour garantir une base propre et prévisible
                var currentPrompt = SelectedPrompt.DefaultPrompt;
                
                var (success, reformulated, error) = await _reformulationService.ReformulatePromptAsync(
                    currentPrompt,
                    ReformulationRequest
                );
                
                if (success)
                {
                    // Afficher dialogue de confirmation
                    var result = MessageBox.Show(
                        $"📝 Prompt reformulé avec succès !\n\n" +
                        $"Voulez-vous remplacer le texte actuel par cette nouvelle version ?\n\n" +
                        $"Aperçu (100 premiers caractères) :\n" +
                        $"{(reformulated.Length > 100 ? reformulated.Substring(0, 100) + "..." : reformulated)}",
                        "Reformulation réussie",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question
                    );
                    
                    if (result == MessageBoxResult.Yes)
                    {
                        CustomPromptText = reformulated;
                        ReformulationRequest = ""; // Vider la demande
                        
                        MessageBox.Show(
                            "✅ Le prompt a été remplacé dans la zone de texte.\n\n" +
                            "N'oubliez pas de cliquer sur 'Sauvegarder personnalisation' pour enregistrer les modifications.",
                            "Attention",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information
                        );
                    }
                }
                else
                {
                    MessageBox.Show($"❌ Erreur lors de la reformulation :\n\n{error}", "Erreur",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            finally
            {
                IsReformulating = false;
            }
        }
        
        private void PromoteCustomToDefault()
        {
            if (SelectedPrompt == null || string.IsNullOrEmpty(SelectedPrompt.CustomPrompt))
                return;
            
            // Confirmation
            var result = MessageBox.Show(
                $"⬆️ Promouvoir la version personnalisée comme nouveau prompt par défaut ?\n\n" +
                $"Prompt : {SelectedPrompt.Name}\n\n" +
                $"Après cette action :\n" +
                $"• Votre version personnalisée deviendra le nouveau défaut\n" +
                $"• L'ancien défaut restera archivé (restauration via 'Original')\n" +
                $"• La prochaine reformulation partira de cette nouvelle base\n\n" +
                $"⚠️ Assurez-vous d'avoir bien testé cette version avant de la promouvoir.",
                "Confirmer la promotion",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );
            
            if (result != MessageBoxResult.Yes)
                return;
            
            // Promouvoir
            var (success, message) = _promptService.PromoteCustomToDefault(SelectedPrompt.Id);
            
            if (success)
            {
                MessageBox.Show(
                    "✅ Prompt promu avec succès !\n\n" +
                    "La version personnalisée est maintenant le nouveau prompt par défaut.\n" +
                    "Les prochaines reformulations partiront de cette base.",
                    "Succès",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
                
                ReloadPrompts();
            }
            else
            {
                MessageBox.Show($"❌ {message}", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void RestoreToOriginal()
        {
            if (SelectedPrompt == null)
                return;
            
            // Confirmation avec avertissement
            var result = MessageBox.Show(
                $"🏭 Restaurer le prompt original d'usine ?\n\n" +
                $"Prompt : {SelectedPrompt.Name}\n\n" +
                $"⚠️ ATTENTION - Cette action va :\n" +
                $"• Supprimer votre version personnalisée\n" +
                $"• Remplacer le défaut actuel par la version d'origine\n" +
                $"• Perdre toutes les améliorations apportées\n\n" +
                $"Cette action est IRRÉVERSIBLE.\n\n" +
                $"Êtes-vous sûr ?",
                "Confirmer la restauration d'origine",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );
            
            if (result != MessageBoxResult.Yes)
                return;
            
            // Restaurer
            var (success, message) = _promptService.RestoreToOriginal(SelectedPrompt.Id);
            
            if (success)
            {
                MessageBox.Show(
                    "✅ Prompt restauré à l'original !\n\n" +
                    "Le prompt d'usine a été restauré comme version par défaut.",
                    "Succès",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
                
                ReloadPrompts();
            }
            else
            {
                MessageBox.Show($"❌ {message}", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
