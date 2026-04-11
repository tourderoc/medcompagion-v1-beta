using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MedCompanion.Services;

namespace MedCompanion.Services
{
    /// <summary>
    /// Service chargé de synchroniser les avatars IA générés par les parents vers le PC local.
    /// Les images sont sauvegardées dans Documents/MedCompanion/Avatars/
    /// Polling toutes les 5 minutes pour les nouveaux avatars
    /// </summary>
    public class AvatarSyncService
    {
        private readonly FirebaseService _firebaseService;
        private readonly string _avatarBaseDir;
        private readonly HttpClient _httpClient;
        private CancellationTokenSource? _pollingCts;

        public event EventHandler<int>? NewAvatarsSynced;

        public AvatarSyncService(FirebaseService firebaseService)
        {
            _firebaseService = firebaseService;
            _httpClient = new HttpClient();
            
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            _avatarBaseDir = Path.Combine(documentsPath, "MedCompanion", "Avatars");
            
            if (!Directory.Exists(_avatarBaseDir))
            {
                Directory.CreateDirectory(_avatarBaseDir);
            }
        }

        /// <summary>
        /// Lance une synchronisation des avatars depuis le VPS directement
        /// </summary>
        public async Task<(int TotalSynced, int NewDownloads, string? Error)> SyncAvatarsAsync()
        {
            try
            {
                // Récupérer la liste des avatars du VPS
                var request = new HttpRequestMessage(HttpMethod.Get, "https://avatar.parentaile.fr/avatar/list");
                request.Headers.Add("X-Api-Key", "cf119d4189909886d318d30c96bc9495399ce7f2339579ce7d5779c55204f12d");

                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                    return (0, 0, $"VPS error: {response.StatusCode}");

                var json = await response.Content.ReadAsStringAsync();
                var jsonDoc = System.Text.Json.JsonDocument.Parse(json);
                var avatarsObj = jsonDoc.RootElement.GetProperty("avatars");

                int totalCount = 0;
                int newDownloads = 0;

                foreach (var prop in avatarsObj.EnumerateObject())
                {
                    var uid = prop.Name;
                    var url = prop.Value.GetString();

                    if (string.IsNullOrEmpty(url)) continue;

                    totalCount++;
                    var localPath = Path.Combine(_avatarBaseDir, $"{uid}.jpg");

                    if (!File.Exists(localPath))
                    {
                        var downloaded = await DownloadImageAsync(url, localPath);
                        if (downloaded)
                        {
                            newDownloads++;
                            System.Diagnostics.Debug.WriteLine($"[AvatarSync] Downloaded {uid}.jpg from VPS");
                        }
                    }
                }

                if (newDownloads > 0)
                {
                    NewAvatarsSynced?.Invoke(this, newDownloads);
                }

                return (totalCount, newDownloads, null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AvatarSync] Error: {ex.Message}");
                return (0, 0, ex.Message);
            }
        }

        private async Task<bool> DownloadImageAsync(string url, string localPath)
        {
            try
            {
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode) return false;

                using (var stream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = File.Create(localPath))
                {
                    await stream.CopyToAsync(fileStream);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public string GetAvatarDirectory() => _avatarBaseDir;

        /// <summary>
        /// Démarre le polling périodique (5 min) pour checker les nouveaux avatars
        /// </summary>
        public void StartPolling()
        {
            if (_pollingCts != null) return; // Déjà en cours

            _pollingCts = new CancellationTokenSource();
            var token = _pollingCts.Token;

            _ = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromMinutes(5), token);
                        if (!token.IsCancellationRequested)
                        {
                            await SyncAvatarsAsync();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[AvatarSync] Polling error: {ex.Message}");
                    }
                }
            }, token);
        }

        /// <summary>
        /// Arrête le polling
        /// </summary>
        public void StopPolling()
        {
            _pollingCts?.Cancel();
            _pollingCts?.Dispose();
            _pollingCts = null;
        }
    }
}
