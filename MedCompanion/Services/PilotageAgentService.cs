using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MedCompanion.Models;
using MedCompanion.Services.LLM;

namespace MedCompanion.Services
{
    public class PilotageAgentService
    {
        private readonly AppSettings _settings;

        // Dictionnaires heuristiques
        private readonly string[] _criticalKeywords = {
            "effet indésirable", "somnolence", "vomissement", "rash", "urticaire",
            "danger", "urgence", "suicide", "fugue", "violence", "menace", 
            "convulsion", "malaise", "étouffement", "allergie", "gonflement"
        };

        private readonly string[] _medicamentKeywords = {
            "médicament", "traitement", "posologie", "dose", "mg", "comprimé", "goutte",
            "sirop", "matin", "midi", "soir", "ordonnance", "renouvellement"
        };

        private readonly string[] _temporalMarkers = {
            "depuis", "hier", "avant-hier", "semaine", "mois", "jours", "jour",
            "immédiat", "tout de suite", "urgence", "ce matin", "ce soir"
        };

        public PilotageAgentService(AppSettings settings)
        {
            _settings = settings;
        }

        /// <summary>
        /// Analyse un message entrant via les deux couches (Heuristique + LLM)
        /// </summary>
        public async Task<PatientMessage> ProcessMessageAsync(PatientMessage message, string patientContext = "")
        {
            // --- Couche 1 : Heuristique ---
            AnalyzeHeuristics(message);

            // --- Couche 2 : LLM (si actif) ---
            if (_settings.IsPilotageAgentActive)
            {
                await AnalyzeWithLLMAsync(message, patientContext);
            }

            return message;
        }

        /// <summary>
        /// Couche 1 : Analyse par règles et mots-clés
        /// </summary>
        private void AnalyzeHeuristics(PatientMessage message)
        {
            string contentLower = message.Content.ToLower();

            // Mots-clés critiques
            foreach (var kw in _criticalKeywords)
            {
                if (contentLower.Contains(kw))
                {
                    message.DetectedKeywords.Add(kw);
                    message.HasCriticalKeyword = true;
                    message.Urgency = MessageUrgency.Urgent; // Urgence minimale si mot critique
                }
            }

            // Médicaments (Détection simplifiée)
            foreach (var kw in _medicamentKeywords)
            {
                if (contentLower.Contains(kw))
                {
                    message.DetectedMedicaments.Add(kw);
                }
            }

            // Marqueurs temporels
            foreach (var tm in _temporalMarkers)
            {
                if (contentLower.Contains(tm))
                {
                    message.TemporalMarkers.Add(tm);
                }
            }

            // Si critique + temporel court ("hier", "immédiat"), on monte à Critical
            if (message.HasCriticalKeyword && (contentLower.Contains("hier") || contentLower.Contains("immédiat") || contentLower.Contains("urgence")))
            {
                message.Urgency = MessageUrgency.Critical;
            }
        }

        /// <summary>
        /// Couche 2 : Utilise le LLM local Ollama pour affiner l'analyse
        /// ✅ IMPORTANT: Utilise directement OllamaLLMProvider pour garantir l'usage du modèle local
        /// </summary>
        private async Task AnalyzeWithLLMAsync(PatientMessage message, string patientContext)
        {
            try
            {
                // Vérifier que le modèle est configuré
                if (string.IsNullOrEmpty(_settings.PilotageAgentModel))
                {
                    System.Diagnostics.Debug.WriteLine("[PilotageAgent] ⚠️ Modèle non configuré - Analyse LLM ignorée");
                    return;
                }

                string systemPrompt = "Tu es un assistant expert pour un cabinet de pédopsychiatrie. Ton rôle est de trier les messages des parents et de fournir des suggestions de réponses professionnelles et empathiques.";
                string userPrompt = BuildAnalysisPrompt(message, patientContext);

                // ✅ Créer directement un provider Ollama local (comme pour l'anonymisation)
                var ollamaProvider = new OllamaLLMProvider(_settings.OllamaBaseUrl, _settings.PilotageAgentModel);

                System.Diagnostics.Debug.WriteLine($"[PilotageAgent] 🤖 Analyse via Ollama local : {_settings.PilotageAgentModel}");

                var messages = new List<(string role, string content)>
                {
                    ("user", userPrompt)
                };

                var (success, result, error) = await ollamaProvider.ChatAsync(
                    systemPrompt,
                    messages,
                    maxTokens: 1000
                );

                if (success && !string.IsNullOrEmpty(result))
                {
                    System.Diagnostics.Debug.WriteLine($"[PilotageAgent] ✅ Analyse réussie - {result.Length} caractères");
                    ParseLLMResult(message, result);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[PilotageAgent] ❌ Échec analyse : {error}");
                    message.AISummary = $"Erreur analyse: {error}";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PilotageAgent] ❌ Exception LLM : {ex.Message}");
                message.AISummary = "Erreur lors de l'analyse IA.";
            }
        }

        private string BuildAnalysisPrompt(PatientMessage message, string patientContext)
        {
            return $@"ANALYSE DE MESSAGE PATIENT
---
CONTEXTE PATIENT :
{patientContext}

MESSAGE DU PARENT :
{message.Content}

ALERTES HEURISTIQUES DÉJÀ DÉTECTÉES :
- Mots critiques : {string.Join(", ", message.DetectedKeywords)}
- Médicaments : {string.Join(", ", message.DetectedMedicaments)}
- Temps : {string.Join(", ", message.TemporalMarkers)}

MISSION :
1. Analyse le degré d'urgence médicale ou psychologique.
2. Rédige un résumé flash pour le médecin (max 2 lignes).
3. Propose un brouillon de réponse professionnel et empathique.

RÉPONDS AU FORMAT JSON :
{{
  ""urgency"": ""Urgent|Moderate|Low"",
  ""summary"": ""votre résumé ici"",
  ""suggestion"": ""votre proposition de réponse ici""
}}";
        }

        private void ParseLLMResult(PatientMessage message, string jsonResult)
        {
            try
            {
                // Un peu de nettoyage si le LLM ajoute du texte autour du JSON
                int start = jsonResult.IndexOf('{');
                int end = jsonResult.LastIndexOf('}');
                if (start >= 0 && end >= 0)
                {
                    jsonResult = jsonResult.Substring(start, end - start + 1);
                }

                var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var data = System.Text.Json.JsonSerializer.Deserialize<LLMAnalysisResult>(jsonResult, options);

                if (data != null)
                {
                    message.AISummary = data.Summary;
                    message.SuggestedResponse = data.Suggestion;
                    
                    // On ne baisse pas l'urgence si l'heuristique a trouvé du critique
                    if (message.Urgency != MessageUrgency.Critical && message.Urgency != MessageUrgency.Urgent)
                    {
                        if (data.Urgency == "Urgent") message.Urgency = MessageUrgency.Urgent;
                        else if (data.Urgency == "Moderate") message.Urgency = MessageUrgency.Moderate;
                        else message.Urgency = MessageUrgency.Low;
                    }
                }
            }
            catch
            {
                message.AISummary = "Erreur de formatage de l'analyse IA.";
            }
        }

        private class LLMAnalysisResult
        {
            public string Urgency { get; set; } = string.Empty;
            public string Summary { get; set; } = string.Empty;
            public string Suggestion { get; set; } = string.Empty;
        }
    }
}
