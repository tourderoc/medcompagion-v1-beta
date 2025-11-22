using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MedCompanion.Models;

namespace MedCompanion.Services
{
    /// <summary>
    /// Service to manage scanned PDF form templates.
    /// Stores templates under Documents/MedCompanion/templates/scanned.
    /// </summary>
    public class TemplateLibraryService
    {
        private readonly string _templatesDirectory;

        public TemplateLibraryService()
        {
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            _templatesDirectory = Path.Combine(documentsPath, "MedCompanion", "templates", "scanned");
            EnsureDirectoryExists(_templatesDirectory);
        }

        private void EnsureDirectoryExists(string path)
        {
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
        }

        /// <summary>
        /// Imports a scanned PDF as a new template.
        /// Returns the created ScannedTemplate.
        /// </summary>
        public ScannedTemplate ImportTemplate(string pdfPath, string displayName = null)
        {
            if (!File.Exists(pdfPath))
                throw new FileNotFoundException($"Template PDF not found: {pdfPath}");

            var id = Guid.NewGuid().ToString();
            var name = displayName ?? Path.GetFileNameWithoutExtension(pdfPath);
            var destPath = Path.Combine(_templatesDirectory, $"{id}.pdf");
            File.Copy(pdfPath, destPath, overwrite: true);

            // Optional: generate a preview image (not implemented here)
            var template = new ScannedTemplate
            {
                Id = id,
                Name = name,
                FilePath = destPath,
                PreviewImagePath = null
            };
            // Persist metadata (simple JSON list)
            SaveMetadata(template);
            return template;
        }

        public List<ScannedTemplate> GetAllTemplates()
        {
            var metadataPath = Path.Combine(_templatesDirectory, "metadata.json");
            if (!File.Exists(metadataPath))
                return new List<ScannedTemplate>();
            var json = File.ReadAllText(metadataPath);
            return System.Text.Json.JsonSerializer.Deserialize<List<ScannedTemplate>>(json) ?? new List<ScannedTemplate>();
        }

        public ScannedTemplate GetTemplate(string id)
        {
            return GetAllTemplates().FirstOrDefault(t => t.Id == id);
        }

        public void DeleteTemplate(string id)
        {
            var templates = GetAllTemplates();
            var template = templates.FirstOrDefault(t => t.Id == id);
            if (template == null) return;
            if (File.Exists(template.FilePath))
                File.Delete(template.FilePath);
            if (!string.IsNullOrEmpty(template.PreviewImagePath) && File.Exists(template.PreviewImagePath))
                File.Delete(template.PreviewImagePath);
            templates.Remove(template);
            SaveAllMetadata(templates);
        }

        private void SaveMetadata(ScannedTemplate template)
        {
            var templates = GetAllTemplates();
            templates.Add(template);
            SaveAllMetadata(templates);
        }

        private void SaveAllMetadata(List<ScannedTemplate> templates)
        {
            var metadataPath = Path.Combine(_templatesDirectory, "metadata.json");
            var json = System.Text.Json.JsonSerializer.Serialize(templates, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(metadataPath, json);
        }
    }

    /// <summary>
    /// Simple model representing a scanned template.
    /// </summary>
    public class ScannedTemplate
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string? PreviewImagePath { get; set; }
    }
}
