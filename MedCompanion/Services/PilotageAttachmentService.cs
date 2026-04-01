using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MedCompanion.Models;

namespace MedCompanion.Services
{
    /// <summary>
    /// Service de gestion des pièces jointes pour le mode Pilotage
    /// Stocke les documents en attente d'envoi par patient
    /// </summary>
    public class PilotageAttachmentService
    {
        private readonly PathService _pathService;
        private readonly string _attachmentsDir;
        private readonly string _indexFile;
        private List<PilotageAttachment> _attachments = new();

        public PilotageAttachmentService(PathService pathService)
        {
            _pathService = pathService;
            // Documents/MedCompanion/patients -> Documents/MedCompanion/pilotage
            var basePatientsDir = _pathService.GetBasePatientsDirectory();
            var medCompanionDir = Path.GetDirectoryName(basePatientsDir) ?? basePatientsDir;
            _attachmentsDir = Path.Combine(medCompanionDir, "pilotage", "pending_attachments");
            _indexFile = Path.Combine(_attachmentsDir, "attachments_index.json");

            // Créer le dossier si nécessaire
            if (!Directory.Exists(_attachmentsDir))
            {
                Directory.CreateDirectory(_attachmentsDir);
            }

            // Charger l'index existant
            LoadIndex();
        }

        /// <summary>
        /// Ajoute un document à la file d'attente pour un patient
        /// </summary>
        /// <param name="sourceFilePath">Chemin du fichier source (PDF)</param>
        /// <param name="patientId">ID du patient (format NOM_Prenom)</param>
        /// <param name="documentType">Type de document (attestation, courrier, ordonnance, autre)</param>
        /// <returns>Le PilotageAttachment créé</returns>
        public async Task<PilotageAttachment> AddAttachmentAsync(string sourceFilePath, string patientId, string documentType = "autre")
        {
            if (!File.Exists(sourceFilePath))
                throw new FileNotFoundException($"Fichier non trouvé: {sourceFilePath}");

            // Créer le dossier du patient
            var patientDir = Path.Combine(_attachmentsDir, patientId);
            if (!Directory.Exists(patientDir))
            {
                Directory.CreateDirectory(patientDir);
            }

            // Copier le fichier dans le dossier de staging
            var fileName = Path.GetFileName(sourceFilePath);
            var destPath = Path.Combine(patientDir, fileName);

            // Si le fichier existe déjà, ajouter un timestamp
            if (File.Exists(destPath))
            {
                var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                var ext = Path.GetExtension(fileName);
                var timestamp = DateTime.Now.ToString("HHmmss");
                destPath = Path.Combine(patientDir, $"{nameWithoutExt}_{timestamp}{ext}");
            }

            await Task.Run(() => File.Copy(sourceFilePath, destPath, overwrite: false));

            var attachment = new PilotageAttachment
            {
                FilePath = destPath,
                PatientId = patientId,
                DocumentType = documentType,
                AddedAt = DateTime.Now,
                IsSelected = true
            };

            _attachments.Add(attachment);
            SaveIndex();

            System.Diagnostics.Debug.WriteLine($"[PilotageAttachment] ✅ Ajouté: {fileName} pour {patientId}");

            return attachment;
        }

        /// <summary>
        /// Récupère les pièces jointes en attente pour un patient
        /// </summary>
        public List<PilotageAttachment> GetAttachmentsForPatient(string patientId)
        {
            // Nettoyer les attachments dont les fichiers n'existent plus
            _attachments.RemoveAll(a => !File.Exists(a.FilePath));

            return _attachments
                .Where(a => a.PatientId.Equals(patientId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(a => a.AddedAt)
                .ToList();
        }

        /// <summary>
        /// Récupère toutes les pièces jointes en attente
        /// </summary>
        public List<PilotageAttachment> GetAllPendingAttachments()
        {
            // Nettoyer les attachments dont les fichiers n'existent plus
            _attachments.RemoveAll(a => !File.Exists(a.FilePath));
            return _attachments.OrderByDescending(a => a.AddedAt).ToList();
        }

        /// <summary>
        /// Retire une pièce jointe de la file d'attente
        /// </summary>
        public void RemoveAttachment(string filePath)
        {
            var attachment = _attachments.FirstOrDefault(a => a.FilePath == filePath);
            if (attachment != null)
            {
                _attachments.Remove(attachment);

                // Supprimer le fichier physique
                try
                {
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[PilotageAttachment] ⚠️ Erreur suppression fichier: {ex.Message}");
                }

                SaveIndex();
                System.Diagnostics.Debug.WriteLine($"[PilotageAttachment] 🗑️ Retiré: {Path.GetFileName(filePath)}");
            }
        }

        /// <summary>
        /// Vide les pièces jointes d'un patient après envoi réussi
        /// </summary>
        public void ClearPatientAttachments(string patientId)
        {
            var toRemove = _attachments.Where(a => a.PatientId.Equals(patientId, StringComparison.OrdinalIgnoreCase)).ToList();

            foreach (var attachment in toRemove)
            {
                try
                {
                    if (File.Exists(attachment.FilePath))
                    {
                        File.Delete(attachment.FilePath);
                    }
                }
                catch { }
            }

            _attachments.RemoveAll(a => a.PatientId.Equals(patientId, StringComparison.OrdinalIgnoreCase));
            SaveIndex();

            System.Diagnostics.Debug.WriteLine($"[PilotageAttachment] 🧹 Vidé pour: {patientId}");
        }

        /// <summary>
        /// Ajoute un fichier manuellement (via le bouton "Ajouter un fichier")
        /// </summary>
        public async Task<PilotageAttachment?> AddManualFileAsync(string patientId)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Documents PDF|*.pdf|Tous les fichiers|*.*",
                Title = "Sélectionner un document à joindre"
            };

            if (dialog.ShowDialog() == true)
            {
                return await AddAttachmentAsync(dialog.FileName, patientId, "autre");
            }

            return null;
        }

        /// <summary>
        /// Charge l'index des pièces jointes depuis le fichier JSON
        /// </summary>
        private void LoadIndex()
        {
            try
            {
                if (File.Exists(_indexFile))
                {
                    var json = File.ReadAllText(_indexFile);
                    var loaded = JsonSerializer.Deserialize<List<PilotageAttachment>>(json);
                    _attachments = loaded ?? new List<PilotageAttachment>();

                    // Nettoyer les fichiers qui n'existent plus
                    _attachments.RemoveAll(a => !File.Exists(a.FilePath));

                    System.Diagnostics.Debug.WriteLine($"[PilotageAttachment] 📂 Index chargé: {_attachments.Count} fichiers");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PilotageAttachment] ⚠️ Erreur chargement index: {ex.Message}");
                _attachments = new List<PilotageAttachment>();
            }
        }

        /// <summary>
        /// Sauvegarde l'index des pièces jointes dans le fichier JSON
        /// </summary>
        private void SaveIndex()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(_attachments, options);
                File.WriteAllText(_indexFile, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PilotageAttachment] ⚠️ Erreur sauvegarde index: {ex.Message}");
            }
        }

        /// <summary>
        /// Obtient les fichiers sélectionnés pour un patient
        /// </summary>
        public List<string> GetSelectedFilePaths(string patientId)
        {
            return _attachments
                .Where(a => a.PatientId.Equals(patientId, StringComparison.OrdinalIgnoreCase) && a.IsSelected)
                .Select(a => a.FilePath)
                .Where(File.Exists)
                .ToList();
        }
    }
}
