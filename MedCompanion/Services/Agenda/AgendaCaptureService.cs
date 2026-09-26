using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MedCompanion.Services.Agenda
{
    /// <summary>
    /// Les captures d'écran de l'agenda Doctolib, gardées sur disque.
    ///
    /// Elles ne sont pas jetées après lecture : si le modèle lit mal, on peut relire le même
    /// fichier avec une consigne améliorée, sans redemander au médecin de recommencer.
    /// Dossier : Documents/MedCompanion/agenda/captures/aaaa-MM-jj_HHmm_&lt;vue&gt;.png
    /// </summary>
    public class AgendaCaptureService
    {
        public const string VueSemaine = "semaine";
        public const string VueJour    = "jour";

        /// <summary>Au-delà, les captures ne servent plus à rien et encombrent la sauvegarde.</summary>
        private const int JoursConserves = 14;

        private readonly string _racine;

        public AgendaCaptureService()
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            _racine = Path.Combine(documents, "MedCompanion", "agenda", "captures");
        }

        public string Racine => _racine;

        public record Capture(string Chemin, string Vue, DateTime Prise);

        public (bool success, string? chemin, string? error) Enregistrer(byte[] png, string vue)
        {
            try
            {
                if (png == null || png.Length == 0)
                    return (false, null, "Capture vide — Doctolib est-il bien affiché dans le cadre ?");

                Directory.CreateDirectory(_racine);
                var chemin = Path.Combine(_racine, $"{DateTime.Now:yyyy-MM-dd_HHmm}_{vue}.png");
                File.WriteAllBytes(chemin, png);
                Purger();
                return (true, chemin, null);
            }
            catch (Exception ex)
            {
                return (false, null, $"Erreur d'enregistrement : {ex.Message}");
            }
        }

        /// <summary>
        /// Les captures prises aujourd'hui, la plus récente par vue. Une capture de la veille
        /// n'est jamais reprise : l'agenda a pu changer depuis.
        /// </summary>
        public List<Capture> CapturesDuJour()
        {
            if (!Directory.Exists(_racine)) return new List<Capture>();

            return Directory.GetFiles(_racine, "*.png")
                .Select(Analyser)
                .Where(c => c != null && c!.Prise.Date == DateTime.Today)
                .Select(c => c!)
                .GroupBy(c => c.Vue)
                .Select(g => g.OrderByDescending(c => c.Prise).First())
                .OrderBy(c => c.Vue)
                .ToList();
        }

        private static Capture? Analyser(string chemin)
        {
            var nom = Path.GetFileNameWithoutExtension(chemin);
            var parts = nom.Split('_');
            if (parts.Length < 3) return null;
            if (!DateTime.TryParseExact($"{parts[0]} {parts[1]}", "yyyy-MM-dd HHmm",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var prise)) return null;
            return new Capture(chemin, parts[2], prise);
        }

        private void Purger()
        {
            var limite = DateTime.Today.AddDays(-JoursConserves);
            foreach (var f in Directory.GetFiles(_racine, "*.png"))
            {
                var c = Analyser(f);
                if (c != null && c.Prise.Date < limite)
                    try { File.Delete(f); } catch { }
            }
        }
    }
}
