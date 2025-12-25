using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MedCompanion.Services
{
    /// <summary>
    /// Service d'authentification avec mot de passe et code PIN
    /// - Mot de passe requis si pas de connexion depuis 72h (3 jours)
    /// - Code PIN (4 chiffres) requis sinon
    /// - Option pour désactiver l'authentification (mode développement/test)
    /// </summary>
    public class AuthenticationService
    {
        private readonly string _authFilePath;
        private AuthData _data;

        // Durée avant de demander le mot de passe complet (72h = 3 jours)
        private static readonly TimeSpan PasswordRequiredAfter = TimeSpan.FromHours(72);

        public AuthenticationService()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MedCompanion"
            );

            Directory.CreateDirectory(appDataPath);
            _authFilePath = Path.Combine(appDataPath, "auth_settings.dat");
            _data = LoadData();
        }

        #region Propriétés publiques

        /// <summary>
        /// Indique si c'est la première utilisation (pas de credentials configurés)
        /// </summary>
        public bool IsFirstLaunch => string.IsNullOrEmpty(_data.PasswordHash);

        /// <summary>
        /// Indique si l'authentification est activée
        /// </summary>
        public bool IsAuthenticationEnabled => _data.IsEnabled;

        /// <summary>
        /// Indique si le mot de passe complet est requis (pas de connexion depuis 72h)
        /// </summary>
        public bool IsPasswordRequired
        {
            get
            {
                if (!_data.IsEnabled || IsFirstLaunch)
                    return false;

                var timeSinceLastLogin = DateTime.Now - _data.LastSuccessfulLogin;
                return timeSinceLastLogin > PasswordRequiredAfter;
            }
        }

        /// <summary>
        /// Indique si un code PIN est configuré
        /// </summary>
        public bool HasPinConfigured => !string.IsNullOrEmpty(_data.PinHash);

        /// <summary>
        /// Date de dernière connexion réussie
        /// </summary>
        public DateTime LastSuccessfulLogin => _data.LastSuccessfulLogin;

        #endregion

        #region Configuration initiale

        /// <summary>
        /// Configure les credentials lors de la première utilisation
        /// </summary>
        /// <param name="password">Mot de passe (minimum 6 caractères)</param>
        /// <param name="pin">Code PIN (4 chiffres)</param>
        /// <param name="enableAuth">Activer l'authentification</param>
        public (bool success, string? error) SetupCredentials(string password, string pin, bool enableAuth)
        {
            // Validation du mot de passe
            if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
            {
                return (false, "Le mot de passe doit contenir au moins 6 caractères.");
            }

            // Validation du PIN
            if (string.IsNullOrWhiteSpace(pin) || pin.Length != 4 || !int.TryParse(pin, out _))
            {
                return (false, "Le code PIN doit contenir exactement 4 chiffres.");
            }

            try
            {
                _data.PasswordHash = HashPassword(password);
                _data.PinHash = HashPassword(pin);
                _data.IsEnabled = enableAuth;
                _data.LastSuccessfulLogin = DateTime.Now;
                _data.CreatedAt = DateTime.Now;
                SaveData();

                System.Diagnostics.Debug.WriteLine($"[AuthService] Credentials configurés - Auth activée: {enableAuth}");
                return (true, null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AuthService] Erreur setup: {ex.Message}");
                return (false, $"Erreur lors de la configuration: {ex.Message}");
            }
        }

        /// <summary>
        /// Désactive l'authentification (pour tests/développement)
        /// </summary>
        public void DisableAuthentication()
        {
            _data.IsEnabled = false;
            SaveData();
            System.Diagnostics.Debug.WriteLine("[AuthService] Authentification désactivée");
        }

        /// <summary>
        /// Active l'authentification
        /// </summary>
        public void EnableAuthentication()
        {
            if (!IsFirstLaunch)
            {
                _data.IsEnabled = true;
                SaveData();
                System.Diagnostics.Debug.WriteLine("[AuthService] Authentification activée");
            }
        }

        /// <summary>
        /// Réinitialise tous les paramètres de sécurité (pour tests)
        /// Supprime le fichier de configuration, ce qui déclenchera l'assistant au prochain démarrage
        /// </summary>
        public void ResetAllSettings()
        {
            try
            {
                if (System.IO.File.Exists(_authFilePath))
                {
                    System.IO.File.Delete(_authFilePath);
                    System.Diagnostics.Debug.WriteLine("[AuthService] Fichier de sécurité supprimé - Reset effectué");
                }

                // Réinitialiser les données en mémoire
                _data = new AuthData();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AuthService] Erreur reset: {ex.Message}");
                throw;
            }
        }

        #endregion

        #region Vérification

        /// <summary>
        /// Vérifie le mot de passe
        /// </summary>
        public (bool success, string? error) VerifyPassword(string password)
        {
            if (string.IsNullOrEmpty(_data.PasswordHash))
            {
                return (false, "Aucun mot de passe configuré.");
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                return (false, "Veuillez entrer votre mot de passe.");
            }

            var hash = HashPassword(password);
            if (hash == _data.PasswordHash)
            {
                RecordSuccessfulLogin();
                return (true, null);
            }

            _data.FailedAttempts++;
            SaveData();
            System.Diagnostics.Debug.WriteLine($"[AuthService] Échec mot de passe - Tentative #{_data.FailedAttempts}");
            return (false, "Mot de passe incorrect.");
        }

        /// <summary>
        /// Vérifie le code PIN
        /// </summary>
        public (bool success, string? error) VerifyPin(string pin)
        {
            if (string.IsNullOrEmpty(_data.PinHash))
            {
                return (false, "Aucun code PIN configuré.");
            }

            if (string.IsNullOrWhiteSpace(pin) || pin.Length != 4)
            {
                return (false, "Code PIN invalide.");
            }

            var hash = HashPassword(pin);
            if (hash == _data.PinHash)
            {
                RecordSuccessfulLogin();
                return (true, null);
            }

            _data.FailedAttempts++;
            SaveData();
            System.Diagnostics.Debug.WriteLine($"[AuthService] Échec PIN - Tentative #{_data.FailedAttempts}");
            return (false, "Code PIN incorrect.");
        }

        private void RecordSuccessfulLogin()
        {
            _data.LastSuccessfulLogin = DateTime.Now;
            _data.FailedAttempts = 0;
            SaveData();
            System.Diagnostics.Debug.WriteLine($"[AuthService] Connexion réussie à {DateTime.Now}");
        }

        #endregion

        #region Modification des credentials

        /// <summary>
        /// Change le mot de passe (nécessite l'ancien mot de passe)
        /// </summary>
        public (bool success, string? error) ChangePassword(string currentPassword, string newPassword)
        {
            // Vérifier l'ancien mot de passe
            var (verified, _) = VerifyPassword(currentPassword);
            if (!verified)
            {
                return (false, "Le mot de passe actuel est incorrect.");
            }

            // Valider le nouveau mot de passe
            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
            {
                return (false, "Le nouveau mot de passe doit contenir au moins 6 caractères.");
            }

            _data.PasswordHash = HashPassword(newPassword);
            SaveData();
            System.Diagnostics.Debug.WriteLine("[AuthService] Mot de passe modifié");
            return (true, null);
        }

        /// <summary>
        /// Change le code PIN (nécessite le mot de passe)
        /// </summary>
        public (bool success, string? error) ChangePin(string password, string newPin)
        {
            // Vérifier le mot de passe
            var currentHash = HashPassword(password);
            if (currentHash != _data.PasswordHash)
            {
                return (false, "Mot de passe incorrect.");
            }

            // Valider le nouveau PIN
            if (string.IsNullOrWhiteSpace(newPin) || newPin.Length != 4 || !int.TryParse(newPin, out _))
            {
                return (false, "Le code PIN doit contenir exactement 4 chiffres.");
            }

            _data.PinHash = HashPassword(newPin);
            SaveData();
            System.Diagnostics.Debug.WriteLine("[AuthService] Code PIN modifié");
            return (true, null);
        }

        #endregion

        #region Hashage et stockage sécurisé

        /// <summary>
        /// Hash un mot de passe avec SHA256 + sel fixe
        /// Note: Pour une sécurité maximale, utiliser PBKDF2 avec sel aléatoire
        /// </summary>
        private static string HashPassword(string password)
        {
            // Sel fixe pour cette application (amélioration possible: sel aléatoire par utilisateur)
            const string salt = "MedCompanion_2024_SecureSalt";
            var combined = password + salt;

            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(combined);
            var hash = sha256.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }

        /// <summary>
        /// Charge les données d'authentification depuis le fichier chiffré
        /// </summary>
        private AuthData LoadData()
        {
            if (!File.Exists(_authFilePath))
                return new AuthData();

            try
            {
                var encryptedBytes = File.ReadAllBytes(_authFilePath);
                var decryptedBytes = ProtectedData.Unprotect(
                    encryptedBytes,
                    null,
                    DataProtectionScope.CurrentUser
                );

                var json = Encoding.UTF8.GetString(decryptedBytes);
                return JsonSerializer.Deserialize<AuthData>(json) ?? new AuthData();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AuthService] Erreur chargement: {ex.Message}");
                return new AuthData();
            }
        }

        /// <summary>
        /// Sauvegarde les données d'authentification de manière chiffrée (DPAPI)
        /// </summary>
        private void SaveData()
        {
            try
            {
                var json = JsonSerializer.Serialize(_data);
                var dataBytes = Encoding.UTF8.GetBytes(json);
                var encryptedBytes = ProtectedData.Protect(
                    dataBytes,
                    null,
                    DataProtectionScope.CurrentUser
                );

                File.WriteAllBytes(_authFilePath, encryptedBytes);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AuthService] Erreur sauvegarde: {ex.Message}");
                throw;
            }
        }

        #endregion

        /// <summary>
        /// Classe interne pour stocker les données d'authentification
        /// </summary>
        private class AuthData
        {
            public string? PasswordHash { get; set; }
            public string? PinHash { get; set; }
            public bool IsEnabled { get; set; } = true;
            public DateTime LastSuccessfulLogin { get; set; } = DateTime.MinValue;
            public DateTime CreatedAt { get; set; } = DateTime.MinValue;
            public int FailedAttempts { get; set; } = 0;
        }
    }
}
