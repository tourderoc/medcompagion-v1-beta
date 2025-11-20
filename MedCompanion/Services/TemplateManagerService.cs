using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MedCompanion.Models;

namespace MedCompanion.Services
{
    /// <summary>
    /// Service pour gérer les templates de courriers (chargement, sauvegarde, CRUD)
    /// </summary>
    public class TemplateManagerService
    {
        private const string TEMPLATES_FILE = "Templates/templates.json";
        private TemplateCollection _templateCollection;
        private readonly string _templatesFilePath;

        public TemplateManagerService()
        {
            // Déterminer le chemin absolu du fichier templates.json
            var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            _templatesFilePath = Path.Combine(appDirectory, TEMPLATES_FILE);
            
            _templateCollection = new TemplateCollection();
            LoadTemplates();
        }

        /// <summary>
        /// Charge les templates depuis le fichier JSON
        /// </summary>
        public void LoadTemplates()
        {
            try
            {
                // Créer le dossier Templates s'il n'existe pas
                var templatesDir = Path.GetDirectoryName(_templatesFilePath);
                if (!Directory.Exists(templatesDir))
                {
                    Directory.CreateDirectory(templatesDir!);
                }

                // Créer le fichier s'il n'existe pas
                if (!File.Exists(_templatesFilePath))
                {
                    _templateCollection = new TemplateCollection();
                    SaveTemplates();
                    return;
                }

                var json = File.ReadAllText(_templatesFilePath);
                _templateCollection = JsonSerializer.Deserialize<TemplateCollection>(json) 
                    ?? new TemplateCollection();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TemplateManager] Erreur chargement templates: {ex.Message}");
                _templateCollection = new TemplateCollection();
            }
        }

        /// <summary>
        /// Sauvegarde les templates dans le fichier JSON
        /// </summary>
        private void SaveTemplates()
        {
            try
            {
                var options = new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                };
                var json = JsonSerializer.Serialize(_templateCollection, options);
                File.WriteAllText(_templatesFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TemplateManager] Erreur sauvegarde templates: {ex.Message}");
            }
        }

        /// <summary>
        /// Récupère tous les templates personnalisés
        /// </summary>
        public List<LetterTemplate> GetCustomTemplates()
        {
            return _templateCollection.CustomTemplates.ToList();
        }

        /// <summary>
        /// Ajoute un nouveau template personnalisé
        /// </summary>
        public (bool success, string message, string? templateId) AddTemplate(
            string name, 
            string markdown, 
            List<string> variables,
            string? description = null)
        {
            try
            {
                // Vérifier si un template avec ce nom existe déjà
                if (_templateCollection.CustomTemplates.Any(t => t.Name == name))
                {
                    return (false, "Un template avec ce nom existe déjà.", null);
                }

                var newTemplate = new LetterTemplate
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = name,
                    Markdown = markdown,
                    Variables = variables,
                    Description = description,
                    CreatedDate = DateTime.Now,
                    UsageCount = 0,
                    IsCustom = true
                };

                _templateCollection.CustomTemplates.Add(newTemplate);
                SaveTemplates();

                return (true, "Template ajouté avec succès.", newTemplate.Id);
            }
            catch (Exception ex)
            {
                return (false, $"Erreur lors de l'ajout : {ex.Message}", null);
            }
        }

        /// <summary>
        /// Met à jour un template existant
        /// </summary>
        public (bool success, string message) UpdateTemplate(
            string templateId,
            string? name = null,
            string? markdown = null,
            List<string>? variables = null,
            string? description = null)
        {
            try
            {
                var template = _templateCollection.CustomTemplates
                    .FirstOrDefault(t => t.Id == templateId);

                if (template == null)
                {
                    return (false, "Template introuvable.");
                }

                // Vérifier si le nouveau nom existe déjà (sauf pour le template actuel)
                if (name != null && name != template.Name)
                {
                    if (_templateCollection.CustomTemplates.Any(t => t.Name == name && t.Id != templateId))
                    {
                        return (false, "Un template avec ce nom existe déjà.");
                    }
                    template.Name = name;
                }

                if (markdown != null) template.Markdown = markdown;
                if (variables != null) template.Variables = variables;
                if (description != null) template.Description = description;

                SaveTemplates();
                return (true, "Template mis à jour avec succès.");
            }
            catch (Exception ex)
            {
                return (false, $"Erreur lors de la mise à jour : {ex.Message}");
            }
        }

        /// <summary>
        /// Supprime un template personnalisé
        /// </summary>
        public (bool success, string message) DeleteTemplate(string templateId)
        {
            try
            {
                var template = _templateCollection.CustomTemplates
                    .FirstOrDefault(t => t.Id == templateId);

                if (template == null)
                {
                    return (false, "Template introuvable.");
                }

                _templateCollection.CustomTemplates.Remove(template);
                SaveTemplates();

                return (true, "Template supprimé avec succès.");
            }
            catch (Exception ex)
            {
                return (false, $"Erreur lors de la suppression : {ex.Message}");
            }
        }

        /// <summary>
        /// Récupère un template par son ID
        /// </summary>
        public LetterTemplate? GetTemplateById(string templateId)
        {
            return _templateCollection.CustomTemplates
                .FirstOrDefault(t => t.Id == templateId);
        }

        /// <summary>
        /// Récupère un template par son nom
        /// </summary>
        public LetterTemplate? GetTemplateByName(string name)
        {
            return _templateCollection.CustomTemplates
                .FirstOrDefault(t => t.Name == name);
        }

        /// <summary>
        /// Incrémente le compteur d'utilisation d'un template
        /// </summary>
        public void IncrementUsageCount(string templateId)
        {
            var template = _templateCollection.CustomTemplates
                .FirstOrDefault(t => t.Id == templateId);

            if (template != null)
            {
                template.UsageCount++;
                SaveTemplates();
            }
        }

        /// <summary>
        /// Récupère le nombre total de templates personnalisés
        /// </summary>
        public int GetCustomTemplateCount()
        {
            return _templateCollection.CustomTemplates.Count;
        }

        /// <summary>
        /// Vérifie si un nom de template est disponible
        /// </summary>
        public bool IsTemplateNameAvailable(string name, string? excludeId = null)
        {
            return !_templateCollection.CustomTemplates
                .Any(t => t.Name == name && (excludeId == null || t.Id != excludeId));
        }
    }
}
