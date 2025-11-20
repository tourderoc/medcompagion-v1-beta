using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Tesseract;

namespace MedCompanion.Services
{
    /// <summary>
    /// Service OCR utilisant Tesseract pour extraire du texte depuis des images
    /// CONFIDENTIALITÉ : OCR 100% local, aucune donnée médicale envoyée vers le cloud
    /// </summary>
    public class TesseractOCRService
    {
        private readonly string _tessDataPath;

        public TesseractOCRService()
        {
            // Chemin vers les données Tesseract dans AppData (meilleur emplacement pour les données d'application)
            // Ces fichiers doivent être téléchargés depuis: https://github.com/tesseract-ocr/tessdata
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _tessDataPath = Path.Combine(appDataPath, "MedCompanion", "tessdata");

            // Créer le dossier tessdata s'il n'existe pas
            if (!Directory.Exists(_tessDataPath))
            {
                Directory.CreateDirectory(_tessDataPath);
            }
        }

        /// <summary>
        /// Obtient le chemin vers le dossier tessdata (utile pour l'utilisateur)
        /// </summary>
        public string GetTessDataPath() => _tessDataPath;

        /// <summary>
        /// Extrait le texte d'une image via OCR Tesseract
        /// </summary>
        /// <param name="imagePath">Chemin vers l'image (PNG, JPG)</param>
        /// <returns>(success, texte extrait, niveau de confiance moyen, erreur)</returns>
        public (bool success, string text, float confidence, string? error) ExtractTextFromImage(string imagePath)
        {
            try
            {
                // Vérifier que le fichier fra.traineddata existe
                var trainedDataFile = Path.Combine(_tessDataPath, "fra.traineddata");
                if (!File.Exists(trainedDataFile))
                {
                    return (false, "", 0f,
                        $"Fichier de données Tesseract manquant.\n\n" +
                        $"📥 Téléchargez 'fra.traineddata' depuis:\n" +
                        $"https://github.com/tesseract-ocr/tessdata/raw/main/fra.traineddata\n\n" +
                        $"📂 Et placez-le dans:\n" +
                        $"{_tessDataPath}\n\n" +
                        $"💡 Accès rapide: Windows+R puis tapez:\n" +
                        $"%APPDATA%\\MedCompanion\\tessdata");
                }

                using (var engine = new TesseractEngine(_tessDataPath, "fra", EngineMode.Default))
                {
                    using (var img = Pix.LoadFromFile(imagePath))
                    {
                        using (var page = engine.Process(img))
                        {
                            string text = page.GetText();
                            float confidence = page.GetMeanConfidence();

                            return (true, text, confidence, null);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return (false, "", 0f, $"Erreur OCR: {ex.Message}");
            }
        }

        /// <summary>
        /// Parse les données extraites d'une ou deux captures Doctolib
        /// </summary>
        /// <param name="text1">Texte OCR de la capture 1 (partie haute)</param>
        /// <param name="text2">Texte OCR de la capture 2 (partie basse) - optionnel</param>
        /// <returns>Dictionnaire de données parsées avec niveaux de confiance</returns>
        public Dictionary<string, DoctolibField> ParseDoctolibData(string text1, string text2 = "")
        {
            var combinedText = text1 + "\n" + text2;
            var result = new Dictionary<string, DoctolibField>();

            // === NIR (Numéro de sécurité sociale) ===
            // Format: 1 85 07 75 123 456 78 ou 185077512345678
            var nirMatch = Regex.Match(combinedText, @"(?:\d\s*){13,15}");
            if (nirMatch.Success)
            {
                var nirClean = new string(nirMatch.Value.Where(char.IsDigit).ToArray());
                if (nirClean.Length == 13 || nirClean.Length == 15)
                {
                    result["NIR"] = new DoctolibField
                    {
                        Value = nirClean,
                        Confidence = ConfidenceLevel.High,
                        Source = "OCR Tesseract"
                    };
                }
            }

            // === Adresse ===
            // Chercher patterns d'adresse (numéro + rue)
            var adresseMatch = Regex.Match(combinedText, @"(\d+[,\s]+[A-Za-zÀ-ÿ\s]+(?:rue|avenue|boulevard|chemin|allée|impasse)[^\n]{0,50})", RegexOptions.IgnoreCase);
            if (adresseMatch.Success)
            {
                result["AdresseRue"] = new DoctolibField
                {
                    Value = adresseMatch.Groups[1].Value.Trim(),
                    Confidence = ConfidenceLevel.Medium,
                    Source = "OCR Tesseract"
                };
            }

            // === Code postal + Ville ===
            // Format: 75012 Paris ou 13001 Marseille
            var cpVilleMatch = Regex.Match(combinedText, @"(\d{5})\s+([A-Za-zÀ-ÿ\s\-]+)");
            if (cpVilleMatch.Success)
            {
                result["CodePostal"] = new DoctolibField
                {
                    Value = cpVilleMatch.Groups[1].Value,
                    Confidence = ConfidenceLevel.High,
                    Source = "OCR Tesseract"
                };

                result["Ville"] = new DoctolibField
                {
                    Value = cpVilleMatch.Groups[2].Value.Trim(),
                    Confidence = ConfidenceLevel.High,
                    Source = "OCR Tesseract"
                };
            }

            // === Téléphone ===
            // Format: +33 X XX XX XX XX ou 0X XX XX XX XX
            var phoneMatch = Regex.Match(combinedText, @"(?:\+33|0)\s*[1-9](?:\s*\d{2}){4}");
            if (phoneMatch.Success)
            {
                result["Telephone"] = new DoctolibField
                {
                    Value = phoneMatch.Value,
                    Confidence = ConfidenceLevel.High,
                    Source = "OCR Tesseract"
                };
            }

            // === Email ===
            var emailMatch = Regex.Match(combinedText, @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}");
            if (emailMatch.Success)
            {
                result["Email"] = new DoctolibField
                {
                    Value = emailMatch.Value,
                    Confidence = ConfidenceLevel.High,
                    Source = "OCR Tesseract"
                };
            }

            // === Lieu de naissance ===
            // Chercher "Né(e) à" ou "Lieu de naissance"
            var lieuMatch = Regex.Match(combinedText, @"(?:Né(?:e)?\s+à|Lieu\s+de\s+naissance)\s*:?\s*([A-Za-zÀ-ÿ\s\-]+)", RegexOptions.IgnoreCase);
            if (lieuMatch.Success)
            {
                result["LieuNaissance"] = new DoctolibField
                {
                    Value = lieuMatch.Groups[1].Value.Trim(),
                    Confidence = ConfidenceLevel.Medium,
                    Source = "OCR Tesseract"
                };
            }

            return result;
        }

        /// <summary>
        /// Calcule le niveau de confiance global basé sur la confiance moyenne OCR
        /// </summary>
        public ConfidenceLevel GetConfidenceLevel(float ocrConfidence)
        {
            if (ocrConfidence >= 0.80f) return ConfidenceLevel.High;
            if (ocrConfidence >= 0.50f) return ConfidenceLevel.Medium;
            return ConfidenceLevel.Low;
        }
    }

    /// <summary>
    /// Représente un champ extrait de Doctolib avec son niveau de confiance
    /// </summary>
    public class DoctolibField
    {
        public string Value { get; set; } = string.Empty;
        public ConfidenceLevel Confidence { get; set; }
        public string Source { get; set; } = "OCR Tesseract";
    }

    /// <summary>
    /// Niveau de confiance pour le code couleur
    /// </summary>
    public enum ConfidenceLevel
    {
        Low,    // 🟥 Rouge - Incertain, vérification obligatoire
        Medium, // 🟧 Orange - À vérifier
        High    // 🟩 Vert - Confiance élevée
    }
}
