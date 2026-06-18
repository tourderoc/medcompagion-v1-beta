using System;
using System.IO;
using System.Text;
using System.Text.Json;
using MedCompanion.Models;

namespace MedCompanion.Services.Consultation
{
    /// <summary>
    /// Persistance du brouillon de 1ère consultation.
    /// Fichier : {patientDir}/notes/premiere_draft.json
    /// Le brouillon est supprimé quand la synthèse initiale est sauvegardée.
    /// </summary>
    public class PremiereConsultationDraftService
    {
        private static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };

        private static string DraftPath(string patientDir)
            => Path.Combine(patientDir, "notes", "premiere_draft.json");

        public bool HasDraft(string? patientDir)
            => !string.IsNullOrEmpty(patientDir) && File.Exists(DraftPath(patientDir));

        public PremiereConsultationDraft? Load(string? patientDir)
        {
            if (!HasDraft(patientDir)) return null;
            try
            {
                var json = File.ReadAllText(DraftPath(patientDir!), Encoding.UTF8);
                return JsonSerializer.Deserialize<PremiereConsultationDraft>(json);
            }
            catch { return null; }
        }

        public void Save(string? patientDir, PremiereConsultationDraft draft)
        {
            if (string.IsNullOrEmpty(patientDir)) return;
            try
            {
                draft.LastModified = DateTime.Now;
                Directory.CreateDirectory(Path.Combine(patientDir, "notes"));
                var json = JsonSerializer.Serialize(draft, _opts);
                File.WriteAllText(DraftPath(patientDir), json, Encoding.UTF8);
            }
            catch { /* best-effort */ }
        }

        public void Delete(string? patientDir)
        {
            if (string.IsNullOrEmpty(patientDir)) return;
            try
            {
                var path = DraftPath(patientDir);
                if (File.Exists(path)) File.Delete(path);
            }
            catch { /* best-effort */ }
        }
    }
}
