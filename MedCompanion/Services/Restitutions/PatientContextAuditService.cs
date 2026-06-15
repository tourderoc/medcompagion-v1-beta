using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MedCompanion.Services.LLM;

namespace MedCompanion.Services.Restitutions
{
    public class PatientContextDetails
    {
        // ── Contexte clinique 3-11 ans ──────────────────────────────────────────
        public string? Ecole { get; set; }
        public string? Classe { get; set; }
        public string? MereNom { get; set; }
        public string? MereAge { get; set; }
        public string? MereJob { get; set; }
        public string? PereNom { get; set; }
        public string? PereAge { get; set; }
        public string? PereJob { get; set; }
        public string? Fratrie { get; set; }
        public string? MarcheAge { get; set; }
        public string? LangageAcq { get; set; }
        public string? PropreteAcq { get; set; }

        // ── Vérification d'âge (tous âges) ─────────────────────────────────────
        /// <summary>Âge calculé depuis la DDN stockée dans patient.json (null si DDN absente).</summary>
        public int? AgeCalcule { get; set; }
        /// <summary>Âge mentionné pendant l'interrogatoire (extrait du bloc "age").</summary>
        public int? AgeInterrogatoire { get; set; }
        /// <summary>DDN actuellement enregistrée (format YYYY-MM-DD), pour affichage et édition.</summary>
        public string? DateNaissanceActuelle { get; set; }
        /// <summary>DDN corrigée saisie par le médecin dans le panel (format YYYY-MM-DD ou dd/MM/yyyy).</summary>
        public string? DateNaissanceCorrigee { get; set; }
        /// <summary>True si AgeCalcule ≠ AgeInterrogatoire.</summary>
        public bool HasAgeDiscrepancy { get; set; }
        /// <summary>True si aucune DDN dans le dossier.</summary>
        public bool NeedsDobEntry { get; set; }
        /// <summary>True si la section contexte complet (école, famille…) doit être affichée (3-11 ans).</summary>
        public bool ShowFullContext { get; set; }
    }

    public class PatientContextAuditService
    {
        public async Task<PatientContextDetails> ExtractContextAsync(ILLMService llmService, string noteContent)
        {
            var details = new PatientContextDetails();
            if (llmService == null || string.IsNullOrWhiteSpace(noteContent))
                return details;

            var systemPrompt = @"Tu es un assistant médical en pédopsychiatrie. 
Analyse la note clinique d'interrogatoire fournie et extrait les informations demandées sous forme de JSON brut STRICT.
Tu ne dois renvoyer AUCUN texte explicatif, AUCUN préambule, et AUCUN code de bloc Markdown (pas de ```json). Renvoyer uniquement le JSON.

Champs JSON à extraire :
{
  ""ecole"": ""Nom de l'école ou établissement (ou null si non trouvé)"",
  ""classe"": ""Classe ou niveau scolaire (ex: 6e, CM1, ou null si non trouvé)"",
  ""mereNom"": ""Prénom de la mère (ou null)"",
  ""mereAge"": ""Âge de la mère (ex: '38 ans', ou null)"",
  ""mereJob"": ""Profession ou activité de la mère (ou null)"",
  ""pereNom"": ""Prénom du père (ou null)"",
  ""pereAge"": ""Âge du père (ex: '42 ans', ou null)"",
  ""pereJob"": ""Profession ou activité du père (ou null)"",
  ""fratrie"": ""Description courte de la fratrie (ex: '2 sœurs de 8 ans', 'Enfant unique', ou null)"",
  ""marcheAge"": ""Âge de la marche (ex: '14 mois', 'dans les délais', ou null)"",
  ""langageAcq"": ""Acquisition du langage (ex: 'retard', 'dans les délais', ou null)"",
  ""propreteAcq"": ""Statut propreté diurne/nocturne (ex: 'acquise', ou null)""
}";

            var messages = new List<(string role, string content)>
            {
                ("user", noteContent)
            };

            var (success, result, _) = await llmService.ChatAsync(systemPrompt, messages, maxTokens: 800);
            if (!success || string.IsNullOrWhiteSpace(result))
                return details;

            try
            {
                // Nettoyer d'éventuels blocs de code markdown ```json ... ```
                var cleanJson = result.Trim();
                if (cleanJson.StartsWith("```"))
                {
                    var match = Regex.Match(cleanJson, @"\{.*\}", RegexOptions.Singleline);
                    if (match.Success)
                    {
                        cleanJson = match.Value;
                    }
                }

                var parsed = JsonSerializer.Deserialize<PatientContextDetails>(cleanJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (parsed != null)
                {
                    details = parsed;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PatientContextAudit] Échec désérialisation JSON: {ex.Message}. Texte brut : {result}");
            }

            return details;
        }
    }
}
