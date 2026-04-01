using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MedCompanion.Models;

namespace MedCompanion.Services
{
    /// <summary>
    /// Service de chargement des données pour le dossier papier
    /// Charge les "feuilles" de chaque section (intercalaire) du dossier
    /// </summary>
    public class DossierDataService
    {
        private readonly PathService _pathService;
        private static readonly CultureInfo FrenchCulture = CultureInfo.GetCultureInfo("fr-FR");

        public DossierDataService(PathService pathService)
        {
            _pathService = pathService;
        }

        /// <summary>
        /// Charge toutes les sections d'un patient
        /// </summary>
        public async Task<Dictionary<DossierTab, DossierSectionData>> LoadAllSectionsAsync(PatientIndexEntry patient)
        {
            var result = new Dictionary<DossierTab, DossierSectionData>();
            var nomComplet = patient.NomComplet;

            // Charger chaque section en parallèle
            var tasks = new List<Task<(DossierTab tab, DossierSectionData data)>>
            {
                LoadSectionAsync(nomComplet, DossierTab.Synthese),
                LoadSectionAsync(nomComplet, DossierTab.Administratif),
                LoadSectionAsync(nomComplet, DossierTab.Consultations),
                LoadSectionAsync(nomComplet, DossierTab.ProjetTherapeutique),
                LoadSectionAsync(nomComplet, DossierTab.Bilans),
                LoadSectionAsync(nomComplet, DossierTab.Documents)
            };

            var results = await Task.WhenAll(tasks);

            foreach (var (tab, data) in results)
            {
                result[tab] = data;
            }

            // Ajouter une section Couverture vide (elle affiche juste les infos patient)
            result[DossierTab.Couverture] = new DossierSectionData(DossierTab.Couverture);

            return result;
        }

        /// <summary>
        /// Charge une section spécifique
        /// </summary>
        private async Task<(DossierTab, DossierSectionData)> LoadSectionAsync(string nomComplet, DossierTab section)
        {
            var sectionData = new DossierSectionData(section);

            try
            {
                var pages = section switch
                {
                    DossierTab.Synthese => await LoadSynthesePagesAsync(nomComplet),
                    DossierTab.Administratif => await LoadAdministratifPagesAsync(nomComplet),
                    DossierTab.Consultations => await LoadConsultationsPagesAsync(nomComplet),
                    DossierTab.ProjetTherapeutique => await LoadProjetPagesAsync(nomComplet),
                    DossierTab.Bilans => await LoadBilansPagesAsync(nomComplet),
                    DossierTab.Documents => await LoadDocumentsPagesAsync(nomComplet),
                    _ => new List<DossierPageItem>()
                };

                foreach (var page in pages.OrderByDescending(p => p.Date))
                {
                    sectionData.Pages.Add(page);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DossierDataService] Erreur chargement {section}: {ex.Message}");
            }

            return (section, sectionData);
        }

        #region Loaders par section

        /// <summary>
        /// Charge les pages de synthèse
        /// </summary>
        private async Task<List<DossierPageItem>> LoadSynthesePagesAsync(string nomComplet)
        {
            var pages = new List<DossierPageItem>();
            var syntheseDir = _pathService.GetSyntheseDirectory(nomComplet);

            if (!Directory.Exists(syntheseDir))
                return pages;

            var files = Directory.GetFiles(syntheseDir, "*.md");

            foreach (var file in files)
            {
                var content = await File.ReadAllTextAsync(file);
                var fileName = Path.GetFileNameWithoutExtension(file);
                var date = ExtractDateFromFileName(fileName) ?? File.GetLastWriteTime(file);

                pages.Add(new DossierPageItem
                {
                    Title = GetSyntheseTitle(fileName),
                    Date = date,
                    FilePath = file,
                    Content = content,
                    PreviewText = GetPreview(content),
                    Section = DossierTab.Synthese,
                    DocumentType = "Synthèse",
                    IsLoaded = true
                });
            }

            return pages;
        }

        /// <summary>
        /// Charge les pages administratives (patient.json converti en page)
        /// </summary>
        private async Task<List<DossierPageItem>> LoadAdministratifPagesAsync(string nomComplet)
        {
            var pages = new List<DossierPageItem>();
            var patientJsonPath = _pathService.GetPatientJsonPath(nomComplet);

            if (File.Exists(patientJsonPath))
            {
                var content = await File.ReadAllTextAsync(patientJsonPath);
                var date = File.GetLastWriteTime(patientJsonPath);

                pages.Add(new DossierPageItem
                {
                    Title = "Informations administratives",
                    Date = date,
                    FilePath = patientJsonPath,
                    Content = FormatAdminContent(content),
                    PreviewText = "Coordonnées, contacts, informations patient",
                    Section = DossierTab.Administratif,
                    DocumentType = "Administratif",
                    IsLoaded = true
                });
            }

            return pages;
        }

        /// <summary>
        /// Charge les notes de consultation (toutes les années)
        /// </summary>
        private async Task<List<DossierPageItem>> LoadConsultationsPagesAsync(string nomComplet)
        {
            var pages = new List<DossierPageItem>();
            var notesDirs = _pathService.GetAllYearDirectories(nomComplet, "notes");

            foreach (var notesDir in notesDirs)
            {
                if (!Directory.Exists(notesDir))
                    continue;

                var files = Directory.GetFiles(notesDir, "*.md");

                foreach (var file in files)
                {
                    var content = await File.ReadAllTextAsync(file);
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    var date = ExtractDateFromNoteFileName(fileName) ?? File.GetLastWriteTime(file);

                    pages.Add(new DossierPageItem
                    {
                        Title = $"Note du {date:dd/MM/yyyy}",
                        Date = date,
                        FilePath = file,
                        Content = RemoveYamlHeader(content),
                        PreviewText = GetPreview(RemoveYamlHeader(content)),
                        Section = DossierTab.Consultations,
                        DocumentType = "Note de consultation",
                        IsLoaded = true
                    });
                }
            }

            return pages;
        }

        /// <summary>
        /// Charge les pages du projet thérapeutique
        /// </summary>
        private async Task<List<DossierPageItem>> LoadProjetPagesAsync(string nomComplet)
        {
            // Pour l'instant, on cherche un fichier projet.md dans synthese/
            var pages = new List<DossierPageItem>();
            var syntheseDir = _pathService.GetSyntheseDirectory(nomComplet);

            if (!Directory.Exists(syntheseDir))
                return pages;

            var projetFile = Path.Combine(syntheseDir, "projet_therapeutique.md");
            if (File.Exists(projetFile))
            {
                var content = await File.ReadAllTextAsync(projetFile);
                pages.Add(new DossierPageItem
                {
                    Title = "Projet Thérapeutique",
                    Date = File.GetLastWriteTime(projetFile),
                    FilePath = projetFile,
                    Content = content,
                    PreviewText = GetPreview(content),
                    Section = DossierTab.ProjetTherapeutique,
                    DocumentType = "Projet",
                    IsLoaded = true
                });
            }

            return pages;
        }

        /// <summary>
        /// Charge les bilans (documents importés de type bilan)
        /// </summary>
        private async Task<List<DossierPageItem>> LoadBilansPagesAsync(string nomComplet)
        {
            var pages = new List<DossierPageItem>();
            var years = _pathService.GetAvailableYears(nomComplet);

            foreach (var year in years)
            {
                var documentsDir = _pathService.GetDocumentsDirectory(nomComplet, year);
                var indexPath = Path.Combine(documentsDir, "documents-index.json");

                if (!File.Exists(indexPath))
                    continue;

                try
                {
                    var json = await File.ReadAllTextAsync(indexPath);
                    var documents = JsonSerializer.Deserialize<List<PatientDocumentIndex>>(json);

                    if (documents == null)
                        continue;

                    // Filtrer seulement les bilans
                    var bilans = documents.Where(d =>
                        d.Category?.Equals("bilans", StringComparison.OrdinalIgnoreCase) == true ||
                        d.Category?.Equals("bilan", StringComparison.OrdinalIgnoreCase) == true);

                    foreach (var bilan in bilans)
                    {
                        pages.Add(new DossierPageItem
                        {
                            Title = bilan.FileName ?? "Bilan",
                            Date = bilan.DateAdded,
                            FilePath = bilan.FilePath ?? "",
                            Content = bilan.Summary ?? bilan.ExtractedText ?? "Contenu non disponible",
                            PreviewText = GetPreview(bilan.Summary ?? bilan.ExtractedText ?? ""),
                            Section = DossierTab.Bilans,
                            DocumentType = "Bilan",
                            IsLoaded = true
                        });
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[DossierDataService] Erreur lecture bilans {year}: {ex.Message}");
                }
            }

            return pages;
        }

        /// <summary>
        /// Charge les documents (courriers, ordonnances, attestations)
        /// </summary>
        private async Task<List<DossierPageItem>> LoadDocumentsPagesAsync(string nomComplet)
        {
            var pages = new List<DossierPageItem>();
            var years = _pathService.GetAvailableYears(nomComplet);

            foreach (var year in years)
            {
                // Courriers
                var courriersDir = _pathService.GetCourriersDirectory(nomComplet, year);
                if (Directory.Exists(courriersDir))
                {
                    var files = Directory.GetFiles(courriersDir, "*.md");
                    foreach (var file in files)
                    {
                        var content = await File.ReadAllTextAsync(file);
                        var fileName = Path.GetFileNameWithoutExtension(file);
                        var date = ExtractDateFromFileName(fileName) ?? File.GetLastWriteTime(file);

                        pages.Add(new DossierPageItem
                        {
                            Title = $"Courrier du {date:dd/MM/yyyy}",
                            Date = date,
                            FilePath = file,
                            Content = RemoveYamlHeader(content),
                            PreviewText = GetPreview(RemoveYamlHeader(content)),
                            Section = DossierTab.Documents,
                            DocumentType = "Courrier",
                            IsLoaded = true
                        });
                    }
                }

                // Ordonnances
                var ordonnancesDir = _pathService.GetOrdonnancesDirectory(nomComplet, year);
                if (Directory.Exists(ordonnancesDir))
                {
                    var files = Directory.GetFiles(ordonnancesDir, "*.md");
                    foreach (var file in files)
                    {
                        var content = await File.ReadAllTextAsync(file);
                        var fileName = Path.GetFileNameWithoutExtension(file);
                        var date = ExtractDateFromFileName(fileName) ?? File.GetLastWriteTime(file);

                        pages.Add(new DossierPageItem
                        {
                            Title = $"Ordonnance du {date:dd/MM/yyyy}",
                            Date = date,
                            FilePath = file,
                            Content = RemoveYamlHeader(content),
                            PreviewText = GetPreview(RemoveYamlHeader(content)),
                            Section = DossierTab.Documents,
                            DocumentType = "Ordonnance",
                            IsLoaded = true
                        });
                    }
                }

                // Attestations
                var attestationsDir = _pathService.GetAttestationsDirectory(nomComplet, year);
                if (Directory.Exists(attestationsDir))
                {
                    var files = Directory.GetFiles(attestationsDir, "*.md");
                    foreach (var file in files)
                    {
                        var content = await File.ReadAllTextAsync(file);
                        var fileName = Path.GetFileNameWithoutExtension(file);
                        var date = ExtractDateFromFileName(fileName) ?? File.GetLastWriteTime(file);

                        pages.Add(new DossierPageItem
                        {
                            Title = $"Attestation du {date:dd/MM/yyyy}",
                            Date = date,
                            FilePath = file,
                            Content = RemoveYamlHeader(content),
                            PreviewText = GetPreview(RemoveYamlHeader(content)),
                            Section = DossierTab.Documents,
                            DocumentType = "Attestation",
                            IsLoaded = true
                        });
                    }
                }
            }

            return pages;
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Extrait la date d'un nom de fichier au format YYYY-MM-DD ou YYYY-MM-DD_HHmm
        /// </summary>
        private DateTime? ExtractDateFromFileName(string fileName)
        {
            // Pattern: 2025-01-30 ou 2025-01-30_1448
            var match = Regex.Match(fileName, @"(\d{4})-(\d{2})-(\d{2})(?:_(\d{2})(\d{2}))?");
            if (match.Success)
            {
                var year = int.Parse(match.Groups[1].Value);
                var month = int.Parse(match.Groups[2].Value);
                var day = int.Parse(match.Groups[3].Value);
                var hour = match.Groups[4].Success ? int.Parse(match.Groups[4].Value) : 0;
                var minute = match.Groups[5].Success ? int.Parse(match.Groups[5].Value) : 0;

                return new DateTime(year, month, day, hour, minute, 0);
            }

            return null;
        }

        /// <summary>
        /// Extrait la date d'un nom de fichier note (format: YYYY-MM-DD_HHmm_note.md)
        /// </summary>
        private DateTime? ExtractDateFromNoteFileName(string fileName)
        {
            return ExtractDateFromFileName(fileName);
        }

        /// <summary>
        /// Retourne un titre lisible pour une synthèse
        /// </summary>
        private string GetSyntheseTitle(string fileName)
        {
            if (fileName.Contains("complete"))
                return "Synthèse complète";
            if (fileName.Contains("incremental"))
                return "Mise à jour synthèse";
            if (fileName.Contains("backup"))
                return "Sauvegarde synthèse";
            return "Synthèse";
        }

        /// <summary>
        /// Retourne un aperçu du contenu (150 premiers caractères)
        /// </summary>
        private string GetPreview(string content)
        {
            if (string.IsNullOrEmpty(content))
                return "";

            // Nettoyer le markdown pour l'aperçu
            var cleaned = content
                .Replace("#", "")
                .Replace("*", "")
                .Replace("_", "")
                .Replace("`", "")
                .Replace("\r\n", " ")
                .Replace("\n", " ")
                .Trim();

            if (cleaned.Length <= 150)
                return cleaned;

            return cleaned.Substring(0, 147) + "...";
        }

        /// <summary>
        /// Supprime l'en-tête YAML d'un fichier markdown
        /// </summary>
        private string RemoveYamlHeader(string content)
        {
            if (string.IsNullOrEmpty(content))
                return content;

            // L'en-tête YAML est entre deux lignes ---
            if (!content.StartsWith("---"))
                return content;

            var endIndex = content.IndexOf("---", 3);
            if (endIndex > 0)
            {
                return content.Substring(endIndex + 3).TrimStart();
            }

            return content;
        }

        /// <summary>
        /// Formate le contenu JSON admin en texte lisible
        /// </summary>
        private string FormatAdminContent(string jsonContent)
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonContent);
                var lines = new List<string>();

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    var key = prop.Name;
                    var value = prop.Value.ValueKind == JsonValueKind.String
                        ? prop.Value.GetString()
                        : prop.Value.ToString();

                    if (!string.IsNullOrEmpty(value))
                    {
                        lines.Add($"**{FormatPropertyName(key)}**: {value}");
                    }
                }

                return string.Join("\n\n", lines);
            }
            catch
            {
                return jsonContent;
            }
        }

        /// <summary>
        /// Formate un nom de propriété JSON en français
        /// </summary>
        private string FormatPropertyName(string propName)
        {
            return propName switch
            {
                "Prenom" => "Prénom",
                "Nom" => "Nom",
                "Dob" => "Date de naissance",
                "Sexe" => "Sexe",
                "Adresse" => "Adresse",
                "Telephone" => "Téléphone",
                "Email" => "Email",
                "NumeroSecu" => "N° Sécurité sociale",
                "Medecin" => "Médecin traitant",
                "Ecole" => "École",
                "Classe" => "Classe",
                _ => propName
            };
        }

        #endregion

        /// <summary>
        /// Classe interne pour désérialiser l'index des documents
        /// </summary>
        private class PatientDocumentIndex
        {
            public string? Id { get; set; }
            public string? FileName { get; set; }
            public string? FilePath { get; set; }
            public string? Category { get; set; }
            public DateTime DateAdded { get; set; }
            public string? Summary { get; set; }
            public string? ExtractedText { get; set; }
        }
    }
}
