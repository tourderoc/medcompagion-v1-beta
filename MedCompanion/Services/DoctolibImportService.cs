using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MedCompanion.Services.LLM;

namespace MedCompanion.Services
{
    /// <summary>
    /// Service d'import Doctolib : OCR local (Tesseract) → extraction structurée (Ollama) → DoctolibImportResult
    /// 100% local, aucune donnée envoyée vers le cloud.
    /// </summary>
    public class DoctolibImportService
    {
        private readonly TesseractOCRService _ocrService;
        private readonly ILLMService _llmService;

        public DoctolibImportService(TesseractOCRService ocrService, ILLMService llmService)
        {
            _ocrService = ocrService;
            _llmService = llmService;
        }

        /// <summary>
        /// Analyse deux captures Doctolib (PNG bytes) et retourne les champs extraits.
        /// capture1 = section Infos patient, capture2 = section Médecin traitant (optionnel)
        /// </summary>
        public async Task<(bool success, DoctolibImportResult result, string? error)> ImportAsync(
            byte[] capture1,
            byte[]? capture2 = null)
        {
            // -- Étape 1 : OCR Tesseract local --
            var (ocrText1, ocrText2) = RunOcr(capture1, capture2);

            // -- Étape 2 : Regex rapides pour les champs fiables --
            var ocrFields = _ocrService.ParseDoctolibData(ocrText1, ocrText2);

            // -- Étape 3 : Extraction structurée par Ollama --
            var result = await ExtractWithOllamaAsync(ocrText1, ocrText2, ocrFields);

            return (true, result, null);
        }

        private (string text1, string text2) RunOcr(byte[] capture1, byte[]? capture2)
        {
            string text1 = "";
            string text2 = "";

            // Capture 1
            string tmp1 = Path.Combine(Path.GetTempPath(), $"doctolib_ocr1_{Guid.NewGuid():N}.png");
            try
            {
                File.WriteAllBytes(tmp1, capture1);
                var (ok, text, _, _) = _ocrService.ExtractTextFromImage(tmp1);
                if (ok) text1 = text;
            }
            finally
            {
                try { File.Delete(tmp1); } catch { /* nettoyage best-effort */ }
            }

            // Capture 2 (optionnelle)
            if (capture2 != null && capture2.Length > 0)
            {
                string tmp2 = Path.Combine(Path.GetTempPath(), $"doctolib_ocr2_{Guid.NewGuid():N}.png");
                try
                {
                    File.WriteAllBytes(tmp2, capture2);
                    var (ok, text, _, _) = _ocrService.ExtractTextFromImage(tmp2);
                    if (ok) text2 = text;
                }
                finally
                {
                    try { File.Delete(tmp2); } catch { /* nettoyage best-effort */ }
                }
            }

            return (text1, text2);
        }

        private async Task<DoctolibImportResult> ExtractWithOllamaAsync(
            string ocrText1,
            string ocrText2,
            Dictionary<string, DoctolibField> ocrFields)
        {
            var combinedOcr = ocrText1 + (string.IsNullOrWhiteSpace(ocrText2) ? "" : "\n\n--- SECTION MÉDECIN TRAITANT ---\n" + ocrText2);

            var prompt = BuildExtractionPrompt(combinedOcr);

            var (success, raw, error) = await _llmService.GenerateTextAsync(prompt, maxTokens: 800);

            DoctolibImportResult result;

            if (success && !string.IsNullOrWhiteSpace(raw))
            {
                result = ParseOllamaJson(raw);
            }
            else
            {
                // Fallback : utiliser uniquement les champs OCR regex
                result = new DoctolibImportResult();
                result.OllamaError = error ?? "Réponse vide";
            }

            // Enrichir avec les champs haute confiance du regex OCR
            MergeOcrFields(result, ocrFields);

            result.OcrText1 = ocrText1;
            result.OcrText2 = ocrText2;

            return result;
        }

        private static string BuildExtractionPrompt(string ocrText)
        {
            return $@"Tu es un assistant médical. Extrais les informations patient depuis le texte OCR ci-dessous (issu d'une capture d'écran de Doctolib).

Réponds UNIQUEMENT avec un objet JSON valide, sans markdown, sans commentaire, sans texte avant ou après.

Format JSON attendu (utilise null pour les champs manquants) :
{{
  ""prenom"": null,
  ""nom"": null,
  ""dateNaissance"": null,
  ""sexe"": null,
  ""adresseRue"": null,
  ""codePostal"": null,
  ""ville"": null,
  ""telephone"": null,
  ""email"": null,
  ""numeroSecuriteSociale"": null,
  ""lieuNaissance"": null,
  ""mtNom"": null,
  ""mtPrenom"": null,
  ""mtAdresse"": null,
  ""mtCodePostal"": null,
  ""mtVille"": null,
  ""mtTelephone"": null,
  ""mtAdeli"": null
}}

Texte OCR :
{ocrText}";
        }

        private static DoctolibImportResult ParseOllamaJson(string raw)
        {
            var result = new DoctolibImportResult();
            try
            {
                // Extraire le bloc JSON si Ollama a ajouté du texte autour
                var jsonMatch = Regex.Match(raw, @"\{[\s\S]*\}", RegexOptions.Multiline);
                if (!jsonMatch.Success) return result;

                using var doc = JsonDocument.Parse(jsonMatch.Value);
                var root = doc.RootElement;

                result.Prenom = GetString(root, "prenom");
                result.Nom = GetString(root, "nom");
                result.DateNaissance = GetString(root, "dateNaissance");
                result.Sexe = GetString(root, "sexe");
                result.AdresseRue = GetString(root, "adresseRue");
                result.CodePostal = GetString(root, "codePostal");
                result.Ville = GetString(root, "ville");
                result.Telephone = GetString(root, "telephone");
                result.Email = GetString(root, "email");
                result.NumeroSecuriteSociale = GetString(root, "numeroSecuriteSociale");
                result.LieuNaissance = GetString(root, "lieuNaissance");
                result.MTNom = GetString(root, "mtNom");
                result.MTPrenom = GetString(root, "mtPrenom");
                result.MTAdresse = GetString(root, "mtAdresse");
                result.MTCodePostal = GetString(root, "mtCodePostal");
                result.MTVille = GetString(root, "mtVille");
                result.MTTelephone = GetString(root, "mtTelephone");
                result.MTAdeli = GetString(root, "mtAdeli");
            }
            catch
            {
                result.OllamaError = "Erreur parsing JSON Ollama";
            }
            return result;
        }

        private static string? GetString(JsonElement root, string key)
        {
            if (root.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.String)
            {
                var v = el.GetString();
                return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
            }
            return null;
        }

        private static void MergeOcrFields(DoctolibImportResult result, Dictionary<string, DoctolibField> ocr)
        {
            // Les champs OCR haute confiance écrasent les résultats Ollama si ce dernier a raté
            if (ocr.TryGetValue("NIR", out var nir) && nir.Confidence == ConfidenceLevel.High)
                result.NumeroSecuriteSociale ??= nir.Value;

            if (ocr.TryGetValue("AdresseRue", out var adr))
                result.AdresseRue ??= adr.Value;

            if (ocr.TryGetValue("CodePostal", out var cp) && cp.Confidence == ConfidenceLevel.High)
                result.CodePostal ??= cp.Value;

            if (ocr.TryGetValue("Ville", out var ville) && ville.Confidence == ConfidenceLevel.High)
                result.Ville ??= ville.Value;

            if (ocr.TryGetValue("Telephone", out var tel) && tel.Confidence == ConfidenceLevel.High)
                result.Telephone ??= tel.Value;

            if (ocr.TryGetValue("Email", out var email) && email.Confidence == ConfidenceLevel.High)
                result.Email ??= email.Value;

            if (ocr.TryGetValue("LieuNaissance", out var lieu))
                result.LieuNaissance ??= lieu.Value;

            if (ocr.TryGetValue("MTAdeli", out var adeli) && adeli.Confidence == ConfidenceLevel.High)
                result.MTAdeli ??= adeli.Value;
        }
    }

    /// <summary>
    /// Résultat d'extraction Doctolib après OCR + Ollama
    /// </summary>
    public class DoctolibImportResult
    {
        // Patient
        public string? Prenom { get; set; }
        public string? Nom { get; set; }
        public string? DateNaissance { get; set; }
        public string? Sexe { get; set; }
        public string? AdresseRue { get; set; }
        public string? CodePostal { get; set; }
        public string? Ville { get; set; }
        public string? Telephone { get; set; }
        public string? Email { get; set; }
        public string? NumeroSecuriteSociale { get; set; }
        public string? LieuNaissance { get; set; }

        // Médecin traitant
        public string? MTNom { get; set; }
        public string? MTPrenom { get; set; }
        public string? MTAdresse { get; set; }
        public string? MTCodePostal { get; set; }
        public string? MTVille { get; set; }
        public string? MTTelephone { get; set; }
        public string? MTAdeli { get; set; }

        // Diagnostics
        public string? OcrText1 { get; set; }
        public string? OcrText2 { get; set; }
        public string? OllamaError { get; set; }
    }
}
