using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using MedCompanion.Models;

namespace MedCompanion.Services
{
    /// <summary>
    /// Service de gestion des tokens patients pour Parent'aile
    /// Gère la création, lecture, révocation et persistence des tokens
    /// Synchronise avec Firebase pour la validation côté Parent'aile
    /// </summary>
    public class TokenService
    {
        private readonly string _pilotageDirectory;
        private readonly string _tokensFilePath;
        private TokenStorage? _cache;
        private readonly FirebaseService _firebaseService;
        private readonly VpsBridgeService _vpsBridge;

        public TokenService()
        {
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            _pilotageDirectory = Path.Combine(documentsPath, "MedCompanion", "pilotage");
            _tokensFilePath = Path.Combine(_pilotageDirectory, "tokens.json");

            _firebaseService = new FirebaseService();
            _vpsBridge = new VpsBridgeService();

            EnsureDirectoryExists();
        }

        /// <summary>
        /// Vérifie si Firebase est configuré
        /// </summary>
        public bool IsFirebaseConfigured => _firebaseService.IsConfigured;

        /// <summary>
        /// Obtient le chemin du fichier de configuration Firebase
        /// </summary>
        public string GetFirebaseConfigPath() => _firebaseService.GetConfigPath();

        /// <summary>
        /// Teste la connexion Firebase
        /// </summary>
        public Task<(bool Success, string Message)> TestFirebaseConnectionAsync()
            => _firebaseService.TestConnectionAsync();

        /// <summary>
        /// Crée le dossier pilotage s'il n'existe pas
        /// </summary>
        private void EnsureDirectoryExists()
        {
            if (!Directory.Exists(_pilotageDirectory))
            {
                Directory.CreateDirectory(_pilotageDirectory);
            }
        }

        /// <summary>
        /// Génère un nouveau token pour un patient
        /// </summary>
        /// <param name="patientId">ID du dossier patient (ex: "DUPONT_Martin")</param>
        /// <param name="patientDisplayName">Nom d'affichage (ex: "DUPONT Martin")</param>
        /// <returns>Le token créé et info Firebase</returns>
        public async Task<(PatientToken Token, bool FirebaseOk, string? FirebaseError)> CreateTokenAsync(string patientId, string patientDisplayName)
        {
            var storage = await LoadStorageAsync();

            // Vérifier si un token actif existe déjà pour ce patient
            var existingToken = storage.Tokens.FirstOrDefault(t => t.PatientId == patientId && t.Active);
            if (existingToken != null)
            {
                throw new InvalidOperationException($"Un token actif existe déjà pour le patient {patientDisplayName}. Révoquez-le d'abord.");
            }

            var token = new PatientToken
            {
                TokenId = GenerateSecureTokenId(),
                PatientId = patientId,
                PatientDisplayName = patientDisplayName,
                CreatedAt = DateTime.UtcNow,
                Active = true
            };

            storage.Tokens.Add(token);
            await SaveStorageAsync(storage);

            // VPS bridge (source de vérité)
            var (vpsOk, vpsError) = await _vpsBridge.CreateTokenAsync(token.TokenId, "medcompanion", patientId, patientDisplayName);
            if (!vpsOk)
                System.Diagnostics.Debug.WriteLine($"[TokenService] VPS create echoue: {vpsError}");

            // Firebase (dual-write, sera supprime au merge)
            var (firebaseOk, firebaseError) = await _firebaseService.WriteTokenAsync(token.TokenId, token.CreatedAt);

            return (token, vpsOk || firebaseOk, vpsOk ? null : (firebaseOk ? null : $"VPS: {vpsError} | Firebase: {firebaseError}"));
        }

        /// <summary>
        /// Récupère tous les tokens (actifs et révoqués)
        /// </summary>
        public async Task<List<PatientToken>> GetAllTokensAsync()
        {
            var storage = await LoadStorageAsync();
            return storage.Tokens.OrderByDescending(t => t.CreatedAt).ToList();
        }

        /// <summary>
        /// Récupère uniquement les tokens actifs
        /// </summary>
        public async Task<List<PatientToken>> GetActiveTokensAsync()
        {
            var storage = await LoadStorageAsync();
            return storage.Tokens.Where(t => t.Active).OrderByDescending(t => t.CreatedAt).ToList();
        }

        /// <summary>
        /// Récupère un token par son ID
        /// </summary>
        public async Task<PatientToken?> GetTokenByIdAsync(string tokenId)
        {
            var storage = await LoadStorageAsync();
            return storage.Tokens.FirstOrDefault(t => t.TokenId == tokenId);
        }

        /// <summary>
        /// Récupère le token actif d'un patient
        /// </summary>
        public async Task<PatientToken?> GetTokenByPatientIdAsync(string patientId)
        {
            var storage = await LoadStorageAsync();
            return storage.Tokens.FirstOrDefault(t => t.PatientId == patientId && t.Active);
        }

        /// <summary>
        /// Met à jour le pseudo d'un token (quand le parent active son compte)
        /// </summary>
        public async Task UpdatePseudoAsync(string tokenId, string pseudo)
        {
            var storage = await LoadStorageAsync();
            var token = storage.Tokens.FirstOrDefault(t => t.TokenId == tokenId);

            if (token == null)
                throw new InvalidOperationException("Token non trouvé");

            token.Pseudo = pseudo;
            token.LastActivity = DateTime.UtcNow;

            await SaveStorageAsync(storage);
        }

        /// <summary>
        /// Met à jour la dernière activité d'un token
        /// </summary>
        public async Task UpdateLastActivityAsync(string tokenId)
        {
            var storage = await LoadStorageAsync();
            var token = storage.Tokens.FirstOrDefault(t => t.TokenId == tokenId);

            if (token != null)
            {
                token.LastActivity = DateTime.UtcNow;
                await SaveStorageAsync(storage);
            }
        }

        /// <summary>
        /// Révoque un token (le parent ne pourra plus envoyer de messages)
        /// </summary>
        public async Task<(bool FirebaseOk, string? FirebaseError)> RevokeTokenAsync(string tokenId)
        {
            var storage = await LoadStorageAsync();
            var token = storage.Tokens.FirstOrDefault(t => t.TokenId == tokenId);

            if (token == null)
                throw new InvalidOperationException("Token non trouvé");

            token.Active = false;
            await SaveStorageAsync(storage);

            // VPS bridge
            var (vpsOk, vpsError) = await _vpsBridge.RevokeTokenAsync(tokenId);

            // Firebase (dual-write)
            var (firebaseOk, firebaseError) = await _firebaseService.UpdateTokenStatusAsync(tokenId, "revoked");

            return (vpsOk || firebaseOk, vpsOk ? null : (firebaseOk ? null : $"VPS: {vpsError} | Firebase: {firebaseError}"));
        }

        /// <summary>
        /// Supprime définitivement un token
        /// </summary>
        public async Task<(bool FirebaseOk, string? FirebaseError)> DeleteTokenAsync(string tokenId)
        {
            var storage = await LoadStorageAsync();
            var token = storage.Tokens.FirstOrDefault(t => t.TokenId == tokenId);

            if (token != null)
            {
                storage.Tokens.Remove(token);
                await SaveStorageAsync(storage);
            }

            // VPS bridge
            var (vpsOk, vpsError) = await _vpsBridge.DeleteTokenAsync(tokenId);

            // Firebase (dual-write)
            var (firebaseOk, firebaseError) = await _firebaseService.DeleteTokenAsync(tokenId);

            return (vpsOk || firebaseOk, vpsOk ? null : (firebaseOk ? null : $"VPS: {vpsError} | Firebase: {firebaseError}"));
        }

        /// <summary>
        /// Vérifie si un patient a déjà un token actif
        /// </summary>
        public async Task<bool> HasActiveTokenAsync(string patientId)
        {
            var storage = await LoadStorageAsync();
            return storage.Tokens.Any(t => t.PatientId == patientId && t.Active);
        }

        /// <summary>
        /// Synchronise les tokens locaux avec VPS + Firebase
        /// Met à jour le statut (pending → used) et le pseudo du parent
        /// </summary>
        /// <returns>Nombre de tokens mis à jour</returns>
        public async Task<int> SyncFromFirebaseAsync()
        {
            try
            {
                // Forcer le rechargement depuis le fichier pour avoir les données fraîches
                _cache = null;

                var storageForIds = await LoadStorageAsync();
                var knownIds = storageForIds.Tokens.Where(t => t.Active).Select(t => t.TokenId).ToList();
                System.Diagnostics.Debug.WriteLine($"[TokenService] Sync: {knownIds.Count} tokens actifs locaux: {string.Join(", ", knownIds)}");

                // 1. VPS bridge — source de vérité pour statuts et pseudos
                var tokenStatuses = new Dictionary<string, string>();
                var nicknames = new Dictionary<string, string>();

                var (vpsTokens, vpsErr) = await _vpsBridge.FetchAllTokensAsync();
                if (vpsErr == null)
                {
                    foreach (var kvp in vpsTokens)
                    {
                        tokenStatuses[kvp.Key] = kvp.Value.Status;
                        if (!string.IsNullOrEmpty(kvp.Value.Pseudo))
                            nicknames[kvp.Key] = kvp.Value.Pseudo;
                    }
                    System.Diagnostics.Debug.WriteLine($"[TokenService] VPS retourne {vpsTokens.Count} tokens");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[TokenService] VPS indisponible ({vpsErr}), fallback Firebase");
                }

                // 2. Compléter avec Firebase (dual-read, tokens pas encore sur VPS)
                if (_firebaseService.IsConfigured)
                {
                    var (fbStatuses, statusError) = await _firebaseService.FetchTokenStatusesAsync(knownIds);
                    if (statusError == null)
                    {
                        foreach (var kvp in fbStatuses)
                        {
                            // Token absent du VPS → on prend Firebase.
                            // Patch dual-write : si parent active depuis main (sans dual-write),
                            // VPS dit "pending" mais Firebase dit "used". On croit Firebase
                            // (état plus avancé). À retirer après le merge final.
                            if (!tokenStatuses.ContainsKey(kvp.Key))
                            {
                                tokenStatuses[kvp.Key] = kvp.Value;
                            }
                            else if (kvp.Value == "used" && tokenStatuses[kvp.Key] == "pending")
                            {
                                tokenStatuses[kvp.Key] = "used";
                            }
                        }
                    }

                    var (fbNicknames, _) = await _firebaseService.FetchParentNicknamesAsync();
                    if (fbNicknames != null)
                    {
                        foreach (var kvp in fbNicknames)
                        {
                            if (!nicknames.ContainsKey(kvp.Key))
                                nicknames[kvp.Key] = kvp.Value;
                        }
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[TokenService] Total: {tokenStatuses.Count} statuts, {nicknames.Count} pseudos");

                // 3. Mettre à jour les tokens locaux
                var storage = await LoadStorageAsync();
                int updated = 0;

                foreach (var token in storage.Tokens)
                {
                    if (!token.Active) continue;

                    if (tokenStatuses.TryGetValue(token.TokenId, out var fbStatus))
                    {
                        System.Diagnostics.Debug.WriteLine($"[TokenService] Token {token.TokenId}: fbStatus={fbStatus}, pseudo actuel={token.Pseudo ?? "null"}");
                        bool changed = false;

                        if (fbStatus == "used" && string.IsNullOrEmpty(token.Pseudo))
                        {
                            if (nicknames != null && nicknames.TryGetValue(token.TokenId, out var nickname))
                            {
                                token.Pseudo = nickname;
                                System.Diagnostics.Debug.WriteLine($"[TokenService]   → Pseudo mis à jour: {nickname}");
                            }
                            else
                            {
                                token.Pseudo = "Parent activé";
                                System.Diagnostics.Debug.WriteLine($"[TokenService]   → Pseudo fallback: Parent activé");
                            }
                            token.LastActivity = DateTime.UtcNow;
                            changed = true;
                        }

                        if (changed) updated++;
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[TokenService] Token {token.TokenId}: PAS TROUVÉ dans Firebase!");
                    }
                }

                if (updated > 0)
                {
                    await SaveStorageAsync(storage);
                }
                System.Diagnostics.Debug.WriteLine($"[TokenService] Sync terminé: {updated} token(s) mis à jour");

                return updated;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TokenService] Erreur sync Firebase: {ex.Message}");
                return 0;
            }
        }

        #region Private Methods

        /// <summary>
        /// Génère un ID de token sécurisé (12 caractères alphanumériques)
        /// </summary>
        private string GenerateSecureTokenId()
        {
            const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
            var bytes = new byte[12];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);

            var result = new char[12];
            for (int i = 0; i < 12; i++)
            {
                result[i] = chars[bytes[i] % chars.Length];
            }

            return new string(result);
        }

        /// <summary>
        /// Charge le fichier de stockage des tokens
        /// </summary>
        private async Task<TokenStorage> LoadStorageAsync()
        {
            if (_cache != null)
                return _cache;

            if (!File.Exists(_tokensFilePath))
            {
                _cache = new TokenStorage();
                return _cache;
            }

            try
            {
                var json = await File.ReadAllTextAsync(_tokensFilePath);
                _cache = JsonSerializer.Deserialize<TokenStorage>(json) ?? new TokenStorage();
                return _cache;
            }
            catch
            {
                _cache = new TokenStorage();
                return _cache;
            }
        }

        /// <summary>
        /// Sauvegarde le fichier de stockage des tokens
        /// </summary>
        private async Task SaveStorageAsync(TokenStorage storage)
        {
            storage.LastUpdated = DateTime.UtcNow;
            _cache = storage;

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            var json = JsonSerializer.Serialize(storage, options);
            await File.WriteAllTextAsync(_tokensFilePath, json);
        }

        /// <summary>
        /// Force le rechargement depuis le fichier (utile après sync Firebase)
        /// </summary>
        public void InvalidateCache()
        {
            _cache = null;
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Obtient le chemin du dossier pilotage
        /// </summary>
        public string GetPilotageDirectory() => _pilotageDirectory;

        /// <summary>
        /// Obtient le chemin du fichier tokens.json
        /// </summary>
        public string GetTokensFilePath() => _tokensFilePath;

        #endregion
    }
}
