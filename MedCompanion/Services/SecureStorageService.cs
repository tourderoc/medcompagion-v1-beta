using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MedCompanion.Services
{
    /// <summary>
    /// Service pour gérer le stockage sécurisé des clés API avec chiffrement DPAPI
    /// </summary>
    public class SecureStorageService
    {
        private readonly string _secureFilePath;
        private SecureData _data;

        public SecureStorageService()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MedCompanion"
            );

            Directory.CreateDirectory(appDataPath);
            _secureFilePath = Path.Combine(appDataPath, "secure_settings.dat");
            _data = LoadData();
        }

        /// <summary>
        /// Sauvegarde une clé API de manière chiffrée
        /// </summary>
        public void SaveApiKey(string provider, string apiKey)
        {
            if (string.IsNullOrWhiteSpace(provider))
                throw new ArgumentException("Le nom du provider ne peut pas être vide", nameof(provider));

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                DeleteApiKey(provider);
                return;
            }

            _data.ApiKeys[provider] = apiKey;
            SaveData();
        }

        /// <summary>
        /// Récupère une clé API déchiffrée
        /// </summary>
        public string? GetApiKey(string provider)
        {
            if (string.IsNullOrWhiteSpace(provider))
                return null;

            return _data.ApiKeys.TryGetValue(provider, out var key) ? key : null;
        }

        /// <summary>
        /// Supprime une clé API
        /// </summary>
        public void DeleteApiKey(string provider)
        {
            if (_data.ApiKeys.ContainsKey(provider))
            {
                _data.ApiKeys.Remove(provider);
                SaveData();
            }
        }

        /// <summary>
        /// Vérifie si une clé existe pour un provider
        /// </summary>
        public bool HasApiKey(string provider)
        {
            return _data.ApiKeys.ContainsKey(provider) && 
                   !string.IsNullOrWhiteSpace(_data.ApiKeys[provider]);
        }

        /// <summary>
        /// Charge les données chiffrées depuis le fichier
        /// </summary>
        private SecureData LoadData()
        {
            if (!File.Exists(_secureFilePath))
                return new SecureData();

            try
            {
                var encryptedBytes = File.ReadAllBytes(_secureFilePath);
                var decryptedBytes = ProtectedData.Unprotect(
                    encryptedBytes,
                    null,
                    DataProtectionScope.CurrentUser
                );

                var json = Encoding.UTF8.GetString(decryptedBytes);
                return JsonSerializer.Deserialize<SecureData>(json) ?? new SecureData();
            }
            catch (Exception ex)
            {
                // Si le déchiffrement échoue (fichier corrompu, changement d'utilisateur, etc.)
                // On retourne des données vides
                System.Diagnostics.Debug.WriteLine($"Erreur lors du chargement des données sécurisées: {ex.Message}");
                return new SecureData();
            }
        }

        /// <summary>
        /// Sauvegarde les données de manière chiffrée
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

                File.WriteAllBytes(_secureFilePath, encryptedBytes);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Impossible de sauvegarder les données sécurisées: {ex.Message}", 
                    ex
                );
            }
        }

        /// <summary>
        /// Classe interne pour stocker les données sécurisées
        /// </summary>
        private class SecureData
        {
            public Dictionary<string, string> ApiKeys { get; set; } = new();
        }
    }
}
