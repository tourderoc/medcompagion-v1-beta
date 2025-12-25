using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Input;
using MedCompanion.Commands;
using MedCompanion.Models;
using MedCompanion.Services;
using MedCompanion.Services.LLM;

namespace MedCompanion.ViewModels
{
    /// <summary>
    /// ViewModel pour la fenêtre de paramètres
    /// </summary>
    public class ParametresViewModel : ViewModelBase
    {
        private readonly SecureStorageService _secureStorage;
        private readonly WindowStateService _windowStateService;
        private readonly LLMServiceFactory? _llmFactory;
        private readonly AppSettings _settings;

        // API Keys
        private string _openAIApiKey = string.Empty;
        private string _openRouterApiKey = string.Empty;
        private bool _isPasswordVisible = false;

        // Connection Testing
        private string _connectionStatus = string.Empty;
        private bool _isTestingConnection = false;
        private bool _connectionSuccess = false;

        // Window Settings
        private string _startupPreference = "Remember";

        public ParametresViewModel(
            SecureStorageService secureStorage,
            WindowStateService windowStateService,
            LLMServiceFactory? llmFactory = null)
        {
            _secureStorage = secureStorage ?? throw new ArgumentNullException(nameof(secureStorage));
            _windowStateService = windowStateService ?? throw new ArgumentNullException(nameof(windowStateService));
            _llmFactory = llmFactory;
            _settings = AppSettings.Load(); // Load persisted settings

            // Initialize commands
            TestConnectionCommand = new RelayCommand(async _ => await TestOpenAIConnectionAsync(), _ => CanTestConnection());
            TogglePasswordVisibilityCommand = new RelayCommand(_ => TogglePasswordVisibility());
            SaveCommand = new RelayCommand(_ => SaveSettings());
            CancelCommand = new RelayCommand(_ => { });

            // Load settings
            LoadSettings();
        }

        #region Properties

        public string OpenAIApiKey
        {
            get => _openAIApiKey;
            set
            {
                if (_openAIApiKey != value)
                {
                    _openAIApiKey = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasOpenAIKey));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public string OpenRouterApiKey
        {
            get => _openRouterApiKey;
            set
            {
                if (_openRouterApiKey != value)
                {
                    _openRouterApiKey = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsPasswordVisible
        {
            get => _isPasswordVisible;
            set
            {
                if (_isPasswordVisible != value)
                {
                    _isPasswordVisible = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ConnectionStatus
        {
            get => _connectionStatus;
            set
            {
                if (_connectionStatus != value)
                {
                    _connectionStatus = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsTestingConnection
        {
            get => _isTestingConnection;
            set
            {
                if (_isTestingConnection != value)
                {
                    _isTestingConnection = value;
                    OnPropertyChanged();
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool ConnectionSuccess
        {
            get => _connectionSuccess;
            set
            {
                if (_connectionSuccess != value)
                {
                    _connectionSuccess = value;
                    OnPropertyChanged();
                }
            }
        }

        public string StartupPreference
        {
            get => _startupPreference;
            set
            {
                if (_startupPreference != value)
                {
                    _startupPreference = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool HasOpenAIKey => !string.IsNullOrWhiteSpace(OpenAIApiKey);

        public AppSettings Settings => _settings;

        #endregion

        #region Commands

        public ICommand TestConnectionCommand { get; }
        public ICommand TogglePasswordVisibilityCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        #endregion

        #region Methods

        /// <summary>
        /// Charge les paramètres depuis les services
        /// </summary>
        private void LoadSettings()
        {
            // Charger les clés API
            OpenAIApiKey = _secureStorage.GetApiKey("OpenAI") ?? string.Empty;
            OpenRouterApiKey = _secureStorage.GetApiKey("OpenRouter") ?? string.Empty;

            // Charger les préférences d'affichage
            StartupPreference = _windowStateService.GetStartupPreference();
        }

        /// <summary>
        /// Sauvegarde les paramètres dans les services
        /// </summary>
        public void SaveSettings()
        {
            // Sauvegarder les clés API
            if (!string.IsNullOrWhiteSpace(OpenAIApiKey))
                _secureStorage.SaveApiKey("OpenAI", OpenAIApiKey);
            else
                _secureStorage.DeleteApiKey("OpenAI");

            if (!string.IsNullOrWhiteSpace(OpenRouterApiKey))
                _secureStorage.SaveApiKey("OpenRouter", OpenRouterApiKey);
            else
                _secureStorage.DeleteApiKey("OpenRouter");

            // Sauvegarder les préférences d'affichage
            _windowStateService.SetStartupPreference(StartupPreference);
            
            // Sauvegarder AppSettings (incluant AnonymizationModel)
            _settings.Save();
        }

        /// <summary>
        /// Teste la connexion OpenAI avec la clé saisie
        /// </summary>
        private async Task TestOpenAIConnectionAsync()
        {
            if (string.IsNullOrWhiteSpace(OpenAIApiKey))
            {
                ConnectionStatus = "Veuillez saisir une clé API OpenAI";
                ConnectionSuccess = false;
                return;
            }

            IsTestingConnection = true;
            ConnectionStatus = "Test de connexion en cours...";
            ConnectionSuccess = false;

            try
            {
                // Créer un provider temporaire avec la clé saisie
                var tempProvider = new OpenAILLMProvider(OpenAIApiKey, "gpt-4o-mini");

                // Tester la connexion
                var (isConnected, message) = await tempProvider.CheckConnectionAsync();

                ConnectionSuccess = isConnected;
                ConnectionStatus = isConnected 
                    ? "✅ Connexion réussie !" 
                    : $"❌ Échec: {message}";
            }
            catch (Exception ex)
            {
                ConnectionSuccess = false;
                ConnectionStatus = $"❌ Erreur: {ex.Message}";
            }
            finally
            {
                IsTestingConnection = false;
            }
        }

        /// <summary>
        /// Vérifie si le test de connexion peut être effectué
        /// </summary>
        private bool CanTestConnection()
        {
            return !IsTestingConnection && !string.IsNullOrWhiteSpace(OpenAIApiKey);
        }

        /// <summary>
        /// Bascule l'affichage des mots de passe
        /// </summary>
        private void TogglePasswordVisibility()
        {
            IsPasswordVisible = !IsPasswordVisible;
        }

        #endregion
    }
}
