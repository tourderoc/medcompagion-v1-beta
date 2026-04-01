using System.IO;
using System.Text.Json;
using MedCompanion.Models;

namespace MedCompanion.Services
{
    /// <summary>
    /// Service pour gérer la mémoire persistante de Med
    /// Charge/Sauvegarde depuis med_memory.json dans le dossier MedCompanion
    /// </summary>
    public class MedMemoryService
    {
        private readonly string _memoryPath;
        private MedMemory _memory;

        public MedMemoryService()
        {
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var basePath = Path.Combine(documentsPath, "MedCompanion");
            _memoryPath = Path.Combine(basePath, "med_memory.json");
            _memory = LoadOrCreateDefault();
        }

        /// <summary>
        /// Mémoire actuelle de Med
        /// </summary>
        public MedMemory Memory => _memory;

        /// <summary>
        /// Récupère tous les blocs mémoire
        /// </summary>
        public List<MedMemoryBlock> GetAllBlocks()
        {
            return _memory.Blocks.OrderBy(b => b.DisplayOrder).ToList();
        }

        /// <summary>
        /// Récupère un bloc par son ID
        /// </summary>
        public MedMemoryBlock? GetBlock(string blockId)
        {
            return _memory.GetBlock(blockId);
        }

        /// <summary>
        /// Met à jour le contenu d'un bloc
        /// </summary>
        public void UpdateBlockContent(string blockId, string content)
        {
            var block = _memory.GetBlock(blockId);
            if (block != null)
            {
                block.Content = content;
                block.UpdatedAt = DateTime.Now;
                _memory.UpdatedAt = DateTime.Now;
                System.Diagnostics.Debug.WriteLine($"[MedMemoryService] Bloc '{blockId}' mis à jour");
            }
        }

        /// <summary>
        /// Sauvegarde la mémoire dans le fichier JSON
        /// </summary>
        public void Save()
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                var json = JsonSerializer.Serialize(_memory, options);

                // S'assurer que le dossier existe
                var directory = Path.GetDirectoryName(_memoryPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(_memoryPath, json, System.Text.Encoding.UTF8);
                System.Diagnostics.Debug.WriteLine($"[MedMemoryService] Mémoire sauvegardée dans {_memoryPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MedMemoryService] Erreur sauvegarde : {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Recharge la mémoire depuis le fichier
        /// </summary>
        public void Reload()
        {
            _memory = LoadOrCreateDefault();
        }

        /// <summary>
        /// Génère le contexte mémoire pour le prompt de Med
        /// </summary>
        public string GetContextForPrompt()
        {
            return _memory.ToContextString();
        }

        /// <summary>
        /// Charge la mémoire ou crée celle par défaut
        /// </summary>
        private MedMemory LoadOrCreateDefault()
        {
            try
            {
                if (File.Exists(_memoryPath))
                {
                    var json = File.ReadAllText(_memoryPath, System.Text.Encoding.UTF8);
                    var memory = JsonSerializer.Deserialize<MedMemory>(json);
                    if (memory != null && memory.Blocks.Count > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[MedMemoryService] Mémoire chargée : {memory.Blocks.Count} bloc(s)");
                        return memory;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MedMemoryService] Erreur chargement : {ex.Message}");
            }

            // Créer la mémoire par défaut
            System.Diagnostics.Debug.WriteLine("[MedMemoryService] Création de la mémoire par défaut");
            var defaultMemory = MedMemory.CreateDefault();

            // Sauvegarder immédiatement
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                var json = JsonSerializer.Serialize(defaultMemory, options);

                var directory = Path.GetDirectoryName(_memoryPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(_memoryPath, json, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MedMemoryService] Erreur création fichier par défaut : {ex.Message}");
            }

            return defaultMemory;
        }

        /// <summary>
        /// Efface tout le contenu de la mémoire (reset)
        /// </summary>
        public void ClearAllContent()
        {
            foreach (var block in _memory.Blocks)
            {
                block.Content = string.Empty;
                block.UpdatedAt = DateTime.Now;
            }
            _memory.UpdatedAt = DateTime.Now;
            Save();
            System.Diagnostics.Debug.WriteLine("[MedMemoryService] Mémoire effacée");
        }
    }
}
