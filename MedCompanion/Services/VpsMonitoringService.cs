using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MedCompanion.Services
{
    /// <summary>
    /// Métriques brutes renvoyées par le script monitor.py du VPS
    /// </summary>
    public class VpsMetrics
    {
        public double Cpu { get; set; }
        public double Ram { get; set; }
        public double Disk { get; set; }
        public double NetworkSentMb { get; set; }
        public double NetworkRecvMb { get; set; }
        public string Uptime { get; set; } = "";
        public Dictionary<string, string> Services { get; set; } = new();
        public DateTime FetchedAt { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// Statut simplifié d'un service (vert / orange / rouge)
    /// </summary>
    public enum ServiceStatus { Active, Warning, Error, Unknown }

    /// <summary>
    /// Service qui poll les métriques du VPS via l'API monitor.py
    /// </summary>
    public class VpsMonitoringService
    {
        private readonly AppSettings _settings;
        private readonly SecureStorageService _secureStorage;
        private readonly HttpClient _http;

        private CancellationTokenSource? _pollCts;
        private VpsMetrics? _lastMetrics;
        private bool _isConnected;

        public event EventHandler<VpsMetrics>? MetricsUpdated;
        public event EventHandler<bool>? ConnectionChanged;

        private const int PollIntervalSeconds = 60;

        public VpsMonitoringService(AppSettings settings, SecureStorageService secureStorage)
        {
            _settings = settings;
            _secureStorage = secureStorage;
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        }

        public VpsMetrics? LastMetrics => _lastMetrics;
        public bool IsConnected => _isConnected;

        /// <summary>
        /// Démarre le polling automatique toutes les 60s
        /// </summary>
        public void StartPolling()
        {
            if (_pollCts != null) return;
            _pollCts = new CancellationTokenSource();
            _ = PollLoopAsync(_pollCts.Token);
        }

        /// <summary>
        /// Arrête le polling
        /// </summary>
        public void StopPolling()
        {
            _pollCts?.Cancel();
            _pollCts = null;
        }

        /// <summary>
        /// Récupère les métriques immédiatement (hors polling)
        /// </summary>
        public async Task<(bool success, VpsMetrics? metrics, string? error)> FetchNowAsync()
        {
            try
            {
                ConfigureHttpToken();
                var url = $"{_settings.VpsMonitoringUrl.TrimEnd('/')}/metrics";
                var json = await _http.GetStringAsync(url);

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var metrics = JsonSerializer.Deserialize<VpsMetrics>(json, options);
                if (metrics == null) return (false, null, "Réponse invalide du serveur");

                metrics.FetchedAt = DateTime.Now;
                _lastMetrics = metrics;
                SetConnected(true);

                return (true, metrics, null);
            }
            catch (Exception ex)
            {
                SetConnected(false);
                return (false, null, ex.Message);
            }
        }

        /// <summary>
        /// Récupère les logs d'un service (dernières N lignes)
        /// </summary>
        public async Task<(bool success, string logs, string? error)> FetchLogsAsync(string serviceName, int lines = 50)
        {
            try
            {
                ConfigureHttpToken();
                var url = $"{_settings.VpsMonitoringUrl.TrimEnd('/')}/logs/{serviceName}?lines={lines}";
                var json = await _http.GetStringAsync(url);

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var result = JsonSerializer.Deserialize<Dictionary<string, string>>(json, options);
                var logs = result?.GetValueOrDefault("logs") ?? "";
                return (true, logs, null);
            }
            catch (Exception ex)
            {
                return (false, "", ex.Message);
            }
        }

        /// <summary>
        /// Récupère l'utilisation disque et taille des logs
        /// </summary>
        public async Task<(bool success, Dictionary<string, string>? data, string? error)> FetchDiskUsageAsync()
        {
            try
            {
                ConfigureHttpToken();
                var url = $"{_settings.VpsMonitoringUrl.TrimEnd('/')}/diskusage";
                var json = await _http.GetStringAsync(url);

                // Désérialiser en JsonElement pour gérer les types mixtes (string + float)
                var doc = JsonDocument.Parse(json);
                var data = new Dictionary<string, string>();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    data[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                        ? prop.Value.GetString() ?? ""
                        : prop.Value.ToString();
                }
                return (true, data, null);
            }
            catch (Exception ex)
            {
                return (false, null, ex.Message);
            }
        }

        /// <summary>
        /// Exécute les actions de nettoyage sélectionnées
        /// </summary>
        public async Task<(bool success, Dictionary<string, string>? results, string? error)> ExecuteCleanupAsync(List<string> actions)
        {
            try
            {
                ConfigureHttpToken();
                var url = $"{_settings.VpsMonitoringUrl.TrimEnd('/')}/cleanup";
                var body = JsonSerializer.Serialize(new { actions });
                var content = new System.Net.Http.StringContent(body, System.Text.Encoding.UTF8, "application/json");
                var response = await _http.PostAsync(url, content);
                var json = await response.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var results = JsonSerializer.Deserialize<Dictionary<string, string>>(json, options);
                return (true, results, null);
            }
            catch (Exception ex)
            {
                return (false, null, ex.Message);
            }
        }

        /// <summary>
        /// Exécute une action (start/stop/restart) sur un service Linux
        /// </summary>
        public async Task<(bool success, string? error)> ExecuteServiceActionAsync(string serviceName, string action)
        {
            try
            {
                ConfigureHttpToken();
                var url = $"{_settings.VpsMonitoringUrl.TrimEnd('/')}/service/{action}/{serviceName}";
                var response = await _http.PostAsync(url, null);

                if (response.IsSuccessStatusCode)
                    return (true, null);

                var errorJson = await response.Content.ReadAsStringAsync();
                return (false, errorJson);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// Convertit "active" / "inactive" / "failed" en ServiceStatus
        /// </summary>
        public static ServiceStatus ParseStatus(string raw) => raw switch
        {
            "active" => ServiceStatus.Active,
            "activating" => ServiceStatus.Warning,
            "failed" => ServiceStatus.Error,
            _ => ServiceStatus.Unknown
        };

        // ─── Privé ────────────────────────────────────────────────

        private async Task PollLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var (success, metrics, _) = await FetchNowAsync();
                if (success && metrics != null)
                    MetricsUpdated?.Invoke(this, metrics);

                try { await Task.Delay(TimeSpan.FromSeconds(PollIntervalSeconds), ct); }
                catch (OperationCanceledException) { break; }
            }
        }

        private void SetConnected(bool connected)
        {
            if (_isConnected == connected) return;
            _isConnected = connected;
            ConnectionChanged?.Invoke(this, connected);
        }

        private void ConfigureHttpToken()
        {
            var token = _secureStorage.GetApiKey("vps_monitoring_token");
            _http.DefaultRequestHeaders.Remove("X-Token");
            if (!string.IsNullOrEmpty(token))
                _http.DefaultRequestHeaders.Add("X-Token", token);
        }
    }
}
