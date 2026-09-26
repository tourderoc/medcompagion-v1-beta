using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MedCompanion.Services.LLM;

namespace MedCompanion.Services.Agenda
{
    /// <summary>
    /// Lecture des captures de l'agenda Doctolib par le modèle vision local.
    ///
    /// Deux vues, deux consignes : la grille de la semaine donne le squelette (jour, heure, nom),
    /// la liste du jour donne le détail (motif, date de naissance, téléphone). Savoir à l'avance
    /// ce qu'on lit vaut mieux que de le deviner — d'où deux boutons de capture distincts.
    /// </summary>
    public class AgendaLectureEcranService
    {
        private readonly LlamaCppProvider _vision = new();

        /// <summary>Une ligne lue. Date nulle en vue semaine quand la colonne n'a pas pu être datée.</summary>
        public record LigneLue(DateTime? Date, TimeSpan Heure, string Nom, string Motif,
                               DateTime? DateNaissance, string Telephone, bool Barre);

        /// <summary>
        /// Lit UNE colonne de la vue Semaine, c'est-à-dire un jour. Mesuré le 26/09/2026 : sur la
        /// grille entière, le modèle s'arrête après la première colonne ; découpée, il lit chaque
        /// jour en une douzaine de secondes, avec des noms justes.
        /// </summary>
        public Task<(bool success, List<LigneLue> lignes, string? error)> LireColonneAsync(byte[] colonne)
            => LireAsync(colonne, PromptColonne(), "colonne");

        public Task<(bool success, List<LigneLue> lignes, string? error)> LireJourAsync(byte[] capture)
            => LireAsync(capture, PromptJour(), "jour");

        /// <summary>
        /// Lit une seule ligne, déjà repérée comme barrée par l'analyse des pixels et découpée
        /// autour du trait. Le modèle ne voit pas la rature sur une capture entière ; ici, il n'a
        /// qu'à lire l'heure et le nom, sur une image agrandie trois fois.
        /// </summary>
        public async Task<(bool success, TimeSpan heure, string nom, string? error)> LireLigneRayeeAsync(byte[] bande)
        {
            var prompt = new StringBuilder();
            prompt.AppendLine("Cette image est une seule ligne d'un tableau de rendez-vous : une heure, puis");
            prompt.AppendLine("le nom d'un patient. Le texte est barré, c'est normal — lis-le quand même.");
            prompt.AppendLine("Donne l'heure au format HH:MM et le nom recopié exactement, sans la civilité.");
            prompt.AppendLine("Réponds UNIQUEMENT : {\"heure\": \"17:30\", \"nom\": \"HAMMOUCHI Anas\"}");

            var (ok, brut, erreur) = await _vision.AnalyzeImageAsync(prompt.ToString(), bande, maxTokens: 200);
            if (!ok) return (false, default, "", erreur ?? "échec du modèle");

            var texte = brut ?? "";
            int debut = texte.IndexOf('{'), fin = texte.LastIndexOf('}');
            if (debut < 0 || fin <= debut) return (false, default, "", "le modèle n'a pas rendu de JSON");

            try
            {
                using var doc = JsonDocument.Parse(texte[debut..(fin + 1)]);
                var heure = LireHeure(Texte(doc.RootElement, "heure"));
                var nom = NettoyerNom(Texte(doc.RootElement, "nom"));
                if (heure == null || nom.Length == 0) return (false, default, "", "ligne barrée illisible");
                return (true, heure.Value, nom, null);
            }
            catch (Exception ex)
            {
                return (false, default, "", $"JSON illisible : {ex.Message}");
            }
        }

        private static string PromptColonne()
        {
            var p = new StringBuilder();
            p.AppendLine("Cette image est UNE SEULE colonne de l'agenda Doctolib : une journée.");
            p.AppendLine("En haut se trouve le libellé du jour (ex. « Mar. 22 sept. »). Dessous, les rendez-vous,");
            p.AppendLine("chacun avec son heure de début et le nom du patient.");
            p.AppendLine();
            p.AppendLine("Relève toutes les lignes, de haut en bas :");
            p.AppendLine("  - \"jour\"  : le libellé écrit en haut de la colonne, recopié tel quel ;");
            p.AppendLine("  - \"heure\" : l'heure de début, format HH:MM ;");
            p.AppendLine("  - \"nom\"   : le nom du patient, recopié exactement, même s'il est tronqué ;");
            p.AppendLine("  - \"barre\" : true UNIQUEMENT si un trait horizontal traverse le texte de part en part");
            p.AppendLine("              (rendez-vous annulé) ; sinon false.");
            p.AppendLine();
            p.AppendLine("Regarde chaque ligne avant de répondre : un rendez-vous annulé garde son heure et son");
            p.AppendLine("nom, seul le trait qui le raye le distingue. Attention : une ligne écrite en gris pâle");
            p.AppendLine("est un rendez-vous déjà passé, pas un rendez-vous annulé — pour celle-là, \"barre\": false.");
            p.AppendLine();
            p.AppendLine("Deux rendez-vous peuvent partager la même heure côte à côte : relève-les tous les deux.");
            p.AppendLine("Ignore les plages « Absence » et les cases vides. N'invente rien.");
            p.AppendLine("Si l'image n'est pas une colonne d'agenda, réponds {\"lisible\": false, \"rdv\": []}.");
            p.AppendLine();
            p.AppendLine("Réponds UNIQUEMENT avec cet objet JSON :");
            p.AppendLine("""{ "lisible": true, "rdv": [ { "jour": "mar. 22", "heure": "09:00", "nom": "", "barre": false } ] }""");
            return p.ToString();
        }

        private static string PromptJour()
        {
            var p = new StringBuilder();
            p.AppendLine("Cette image est la vue LISTE d'une journée de l'agenda Doctolib (pédopsychiatrie).");
            p.AppendLine("C'est un tableau : une ligne par rendez-vous, avec des colonnes Horaire, Patient,");
            p.AppendLine("Motif de consultation, et souvent Date de naissance et Téléphone.");
            p.AppendLine("La date du jour est écrite en haut de l'écran (ex. « Samedi 26 septembre 2026 »).");
            p.AppendLine();
            p.AppendLine("Relève toutes les lignes, dans l'ordre où elles apparaissent :");
            p.AppendLine("  - \"heure\"      : colonne Horaire, format HH:MM ;");
            p.AppendLine("  - \"nom\"        : le patient, sans la civilité (M., Mme) ;");
            p.AppendLine("  - \"motif\"      : le motif de consultation, en entier ;");
            p.AppendLine("  - \"naissance\"  : la date de naissance au format JJ/MM/AAAA si la colonne existe, sinon \"\" ;");
            p.AppendLine("  - \"telephone\"  : le téléphone si la colonne existe, sinon \"\" ;");
            p.AppendLine("  - \"barre\"      : true UNIQUEMENT si un trait horizontal traverse le nom de part en");
            p.AppendLine("                   part (rendez-vous annulé) ; sinon false.");
            p.AppendLine();
            p.AppendLine("Regarde chaque ligne avant de répondre : un rendez-vous annulé garde son heure et son");
            p.AppendLine("nom, seul le trait qui le raye le distingue. Attention : les premières lignes de la");
            p.AppendLine("journée sont souvent écrites en gris pâle parce qu'elles sont déjà passées — ce n'est");
            p.AppendLine("pas une annulation, pour celles-là \"barre\": false.");
            p.AppendLine();
            p.AppendLine("Donne aussi \"date\" : la date de la journée affichée, au format JJ/MM/AAAA.");
            p.AppendLine("N'invente aucune ligne et ne complète aucun nom illisible.");
            p.AppendLine("Si l'image n'est pas une liste de rendez-vous, réponds {\"lisible\": false, \"rdv\": []}.");
            p.AppendLine();
            p.AppendLine("Réponds UNIQUEMENT avec cet objet JSON :");
            p.AppendLine("""{ "lisible": true, "date": "26/09/2026", "rdv": [ { "heure": "09:00", "nom": "", "motif": "", "naissance": "", "telephone": "", "barre": false } ] }""");
            return p.ToString();
        }

        private async Task<(bool success, List<LigneLue> lignes, string? error)> LireAsync(
            byte[] capture, string prompt, string vue)
        {
            if (capture == null || capture.Length == 0)
                return (false, new(), "Capture vide.");

            var (ok, brut, erreur) = await _vision.AnalyzeImageAsync(prompt, capture, maxTokens: 2500);
            if (!ok) return (false, new(), erreur ?? "échec du modèle");

            var texte = brut ?? "";
            int debut = texte.IndexOf('{'), fin = texte.LastIndexOf('}');
            if (debut < 0 || fin <= debut) return (false, new(), "le modèle n'a pas rendu de JSON");

            try
            {
                using var doc = JsonDocument.Parse(texte[debut..(fin + 1)]);
                var racine = doc.RootElement;

                if (racine.TryGetProperty("lisible", out var lisible) && lisible.ValueKind == JsonValueKind.False)
                    return (false, new(), $"Capture « {vue} » non reconnue par le modèle.");

                if (!racine.TryGetProperty("rdv", out var tableau) || tableau.ValueKind != JsonValueKind.Array)
                    return (false, new(), "Réponse inattendue du modèle.");

                var dateGlobale = LireDate(Texte(racine, "date"));
                var lignes = new List<LigneLue>();

                foreach (var e in tableau.EnumerateArray())
                {
                    var heure = LireHeure(Texte(e, "heure"));
                    var nom = NettoyerNom(Texte(e, "nom"));
                    if (heure == null || nom.Length == 0) continue;

                    var date = dateGlobale ?? LireJourSemaine(Texte(e, "jour"));

                    lignes.Add(new LigneLue(date, heure.Value, nom, Texte(e, "motif"),
                        LireDate(Texte(e, "naissance")), Texte(e, "telephone"),
                        e.TryGetProperty("barre", out var b) && b.ValueKind == JsonValueKind.True));
                }

                return lignes.Count == 0
                    ? (false, lignes, "Aucun rendez-vous lu sur la capture.")
                    : (true, lignes, null);
            }
            catch (Exception ex)
            {
                return (false, new(), $"JSON illisible : {ex.Message}");
            }
        }

        // ── Lecture des champs ──────────────────────────────────────────────

        private static string Texte(JsonElement e, string nom)
            => e.TryGetProperty(nom, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";

        /// <summary>Retire les civilités que Doctolib colle devant le nom.</summary>
        private static string NettoyerNom(string nom)
        {
            foreach (var civ in new[] { "M. ", "Mme ", "Mlle ", "Monsieur ", "Madame " })
                if (nom.StartsWith(civ, StringComparison.OrdinalIgnoreCase)) return nom[civ.Length..].Trim();
            return nom;
        }

        private static TimeSpan? LireHeure(string valeur)
        {
            var m = System.Text.RegularExpressions.Regex.Match(valeur, @"(\d{1,2})\s*[h:]\s*(\d{2})");
            if (!m.Success) return null;
            int h = int.Parse(m.Groups[1].Value), min = int.Parse(m.Groups[2].Value);
            return h > 23 || min > 59 ? null : new TimeSpan(h, min, 0);
        }

        private static DateTime? LireDate(string valeur)
            => DateTime.TryParseExact(valeur, new[] { "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd" },
                   CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

        /// <summary>
        /// « lun. 21 » → le 21 du mois affiché. On cherche autour d'aujourd'hui : un agenda se
        /// consulte à quelques semaines près, jamais à un an.
        /// </summary>
        private static DateTime? LireJourSemaine(string libelle)
        {
            var m = System.Text.RegularExpressions.Regex.Match(libelle, @"(\d{1,2})");
            if (!m.Success) return null;
            int jour = int.Parse(m.Groups[1].Value);
            if (jour < 1 || jour > 31) return null;

            for (int decalage = -1; decalage <= 1; decalage++)
            {
                var mois = DateTime.Today.AddMonths(decalage);
                if (jour > DateTime.DaysInMonth(mois.Year, mois.Month)) continue;
                var candidat = new DateTime(mois.Year, mois.Month, jour);
                if (Math.Abs((candidat - DateTime.Today).TotalDays) <= 21) return candidat;
            }
            return null;
        }
    }
}
