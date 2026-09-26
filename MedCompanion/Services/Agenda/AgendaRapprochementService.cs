using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MedCompanion.Services.Agenda
{
    /// <summary>
    /// Rapproche un nom lu sur l'écran Doctolib d'un dossier patient de Med.
    ///
    /// La base de référence est celle de Med — 718 dossiers le 26/09/2026 — et non l'export
    /// Doctolib de 6 481 patients : un rapprochement ne sert que s'il y a un dossier à ouvrir,
    /// et les patients dormants n'ajouteraient que des occasions de se tromper.
    ///
    /// Mesuré sur cette base le 26/09/2026 : aucun homonyme (même nom et prénom, naissances
    /// différentes). Le nom identifie donc déjà à lui seul ; la date de naissance n'est pas la
    /// clé, c'est le garde-fou — elle sert à REFUSER un rapprochement douteux, et à départager
    /// une troncature de la grille hebdomadaire (« MARANGE », « WARCKOL H »).
    /// </summary>
    public class AgendaRapprochementService
    {
        public enum Verdict
        {
            /// <summary>Nom et date de naissance concordent : le dossier est sûr.</summary>
            Sur,
            /// <summary>Le nom concorde, la date manque d'un côté : à confirmer par le médecin.</summary>
            AConfirmer,
            /// <summary>Plusieurs dossiers possibles : Med s'abstient plutôt que de choisir.</summary>
            Ambigu,
            /// <summary>Aucun dossier — nouveau patient, consultation parents, ou dossier absent.</summary>
            Aucun
        }

        public record Resultat(Verdict Verdict, string Dossier, string Raison);

        /// <summary>Un dossier de Med. Les deux ordres du nom sont indexés : quelques dossiers ont
        /// été créés en « Prénom Nom » (MATHIS_Di_Martino).</summary>
        private record Fiche(string Dossier, string Cle, string CleInverse, string ClePrenom, DateTime? Naissance);

        private readonly string _cheminLiens;
        private List<Fiche> _fiches = new();
        private Dictionary<string, string> _liens = new(StringComparer.Ordinal);

        public AgendaRapprochementService()
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            _cheminLiens = Path.Combine(documents, "MedCompanion", "agenda", "liens.json");
        }

        public int NombreDossiers => _fiches.Count;

        /// <summary>Charge la base des dossiers et les liens déjà confirmés.</summary>
        public async Task ChargerAsync()
        {
            // Le PathService n'est pas facultatif : sans lui, l'index cherche « patient.json » à la
            // racine du dossier patient au lieu de « info_patient/ », ne le trouve pas, et se rabat
            // sur le nom du dossier — donc SANS date de naissance. Tous les rapprochements
            // tombaient alors en « à confirmer » (constaté le 26/09/2026).
            var index = new PatientIndexService(new PathService());
            await index.ScanAsync();

            _fiches = index.GetAllPatients()
                .Select(p => new Fiche(p.Id, Cle($"{p.Nom} {p.Prenom}"), Cle($"{p.Prenom} {p.Nom}"),
                                       Cle(p.Prenom), LireDate(p.Dob)))
                .Where(f => f.Cle.Length > 0)
                .ToList();

            _liens = ChargerLiens();
        }

        /// <summary>
        /// Trois verdicts, jamais un seul : Med dit « pas de dossier » au lieu de forcer un lien.
        /// Un lien confirmé une fois est mémorisé — les lectures suivantes sont immédiates et
        /// insensibles aux variantes d'orthographe.
        /// </summary>
        public Resultat Resoudre(string nomLu, DateTime? naissanceLue)
        {
            var cle = Cle(nomLu);
            if (cle.Length < 3) return new Resultat(Verdict.Aucun, "", "nom trop court pour être rapproché");

            if (_liens.TryGetValue(cle, out var memorise))
                return new Resultat(Verdict.Sur, memorise, "lien déjà confirmé");

            // Nom entier d'abord ; à défaut, préfixe — la grille de la semaine tronque les noms.
            var candidats = _fiches.Where(f => f.Cle == cle || f.CleInverse == cle).ToList();
            bool parTroncature = false;
            if (candidats.Count == 0 && cle.Length >= 5)
            {
                candidats = _fiches.Where(f => f.Cle.StartsWith(cle, StringComparison.Ordinal)).ToList();
                parTroncature = candidats.Count > 0;
            }

            // Repêchage par la date de naissance : le modèle se trompe parfois d'une lettre au
            // milieu du nom (« LAMIM SCHOUAMACHER » pour SCHOUMACHER, relevé le 26/09/2026), et
            // ni le nom entier ni le préfixe ne retrouvent alors le dossier. La date est exacte,
            // et 620 des 666 dossiers datés en ont une qui leur est propre ; on exige en plus un
            // début de nom commun, pour que deux jumeaux restent ambigus plutôt que confondus.
            if (candidats.Count == 0 && naissanceLue.HasValue)
                candidats = _fiches
                    .Where(f => f.Naissance == naissanceLue.Value.Date && PrefixeCommun(f.Cle, cle) >= 4)
                    .ToList();

            if (candidats.Count == 0)
                return new Resultat(Verdict.Aucun, "", "aucun dossier de ce nom dans Med");

            if (naissanceLue.HasValue)
            {
                var concordants = candidats.Where(f => f.Naissance == naissanceLue.Value.Date).ToList();

                // Des jumeaux partagent le nom et la date : c'est le prénom qui tranche, et rien
                // d'autre (FRANKE Chloe et FRANKE Maxime, nés le 11/05/2017).
                if (concordants.Count > 1)
                {
                    var prenom = Cle(nomLu.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "");
                    var parPrenom = concordants.Where(f => f.ClePrenom == prenom && prenom.Length > 0).ToList();
                    if (parPrenom.Count == 1) concordants = parPrenom;
                }

                if (concordants.Count == 1)
                {
                    Memoriser(cle, concordants[0].Dossier);
                    return new Resultat(Verdict.Sur, concordants[0].Dossier, "nom et date de naissance concordent");
                }

                // Une date connue des deux côtés qui ne concorde avec aucun candidat : on refuse.
                if (concordants.Count == 0 && candidats.All(f => f.Naissance.HasValue))
                    return new Resultat(Verdict.Aucun, "",
                        parTroncature ? "nom tronqué et date de naissance différente"
                                      : "date de naissance différente de celle du dossier");
            }

            if (candidats.Count > 1)
                return new Resultat(Verdict.Ambigu, "",
                    $"{candidats.Count} dossiers possibles ({string.Join(", ", candidats.Take(3).Select(f => f.Dossier))})");

            // Un seul dossier, mais rien pour le confirmer : 52 dossiers n'ont pas de date de
            // naissance, et la grille de la semaine n'en donne pas.
            return new Resultat(Verdict.AConfirmer, candidats[0].Dossier,
                candidats[0].Naissance.HasValue ? "date de naissance absente de la lecture"
                                                : "dossier sans date de naissance");
        }

        // ── Mémoire des liens confirmés ─────────────────────────────────────

        private void Memoriser(string cle, string dossier)
        {
            if (_liens.TryGetValue(cle, out var deja) && deja == dossier) return;
            _liens[cle] = dossier;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_cheminLiens)!);
                File.WriteAllText(_cheminLiens,
                    JsonSerializer.Serialize(_liens, new JsonSerializerOptions { WriteIndented = true }),
                    Encoding.UTF8);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AgendaRapprochement] liens non enregistrés : {ex.Message}");
            }
        }

        private Dictionary<string, string> ChargerLiens()
        {
            try
            {
                if (!File.Exists(_cheminLiens)) return new(StringComparer.Ordinal);
                return JsonSerializer.Deserialize<Dictionary<string, string>>(
                           File.ReadAllText(_cheminLiens, Encoding.UTF8)) ?? new(StringComparer.Ordinal);
            }
            catch { return new(StringComparer.Ordinal); }
        }

        // ── Utilitaires ─────────────────────────────────────────────────────

        /// <summary>Nom réduit à ses lettres, sans accents ni casse : la forme comparable.</summary>
        private static string Cle(string nom)
            => new string((nom ?? "").Normalize(NormalizationForm.FormD)
                .Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

        /// <summary>Nombre de premières lettres communes à deux noms.</summary>
        private static int PrefixeCommun(string a, string b)
        {
            int n = Math.Min(a.Length, b.Length), i = 0;
            while (i < n && a[i] == b[i]) i++;
            return i;
        }

        private static DateTime? LireDate(string? valeur)
            => DateTime.TryParseExact(valeur, new[] { "yyyy-MM-dd", "dd/MM/yyyy" },
                   CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.Date : null;
    }
}
