using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using MedCompanion.Models.Evaluations;

namespace MedCompanion.Services.Evaluations
{
    /// <summary>
    /// État de recueil de la feuille parent, pour une cartographie donnée.
    /// Enregistré, jamais déduit : le dossier doit dire ce qui manque, et Med doit le savoir
    /// pour ne pas lire trois profils comme une cartographie complète.
    /// </summary>
    public enum EtatQuestionnaireParent
    {
        NonRecueilli = 0,   // feuille pas encore imprimée, ou jamais revenue
        Remis        = 1,   // remise au parent, en attente de retour
        Scanne       = 2    // revenue et lue
    }

    /// <summary>
    /// Une cartographie V2 : les 18 axes observés par le médecin, et — quand la feuille
    /// reviendra — les 5 scores du questionnaire parent.
    /// </summary>
    public class CartographieV2
    {
        public string   FilePath        { get; set; } = "";
        public string   PatientNom      { get; set; } = "";
        public DateTime Date            { get; set; } = DateTime.Today;
        public DateTime DerniereModif   { get; set; } = DateTime.Now;
        public int?     Age             { get; set; }
        public string   BandeCode       { get; set; } = "";

        /// <summary>
        /// Versée au dossier bleu ? Le médecin sauvegarde son observation quand il veut ; la
        /// carte n'apparaît dans l'onglet BILANS qu'à la fin de la séance. Le dossier ne se
        /// remplit donc pas de travaux en cours.
        /// </summary>
        public bool VerseeAuDossier { get; set; }

        public EtatQuestionnaireParent EtatQuestionnaire { get; set; } = EtatQuestionnaireParent.NonRecueilli;

        /// <summary>Valeurs 1-5 par axe, clé « profil.axe » (0 ou absent = non renseigné).</summary>
        public Dictionary<string, int> Axes { get; set; } = new();

        /// <summary>Scores 0-6 des 5 questionnaires parents, quand la feuille aura été lue.</summary>
        public Dictionary<string, int> ScoresQuestionnaire { get; set; } = new();

        /// <summary>
        /// Image de la feuille remplie, archivée dans le dossier patient. Conservée même quand
        /// la lecture aura réussi : c'est un document rempli par le parent, et c'est le seul
        /// recours si la lecture s'avère fausse.
        /// </summary>
        public string? ScanImagePath { get; set; }

        public int NbAxesRenseignes => Axes.Count(kv => kv.Value > 0);
        public bool QuestionnaireLu => ScoresQuestionnaire.Count > 0;

        /// <summary>
        /// Complète quand les deux moitiés sont là — et « là » veut dire LUE, pas seulement
        /// scannée. Une feuille archivée mais non dépouillée n'apporte encore aucun score.
        /// </summary>
        public bool EstComplete => QuestionnaireLu && NbAxesRenseignes > 0;

        public string EtatLisible
        {
            get
            {
                var gauche = $"{NbAxesRenseignes} axes observés";
                var droite = EtatQuestionnaire switch
                {
                    EtatQuestionnaireParent.Scanne when QuestionnaireLu => "questionnaire parent recueilli",
                    EtatQuestionnaireParent.Scanne                      => "feuille scannée, lecture des réponses à faire",
                    EtatQuestionnaireParent.Remis                       => "questionnaire parent remis, en attente de retour",
                    _                                                   => "questionnaire parent non recueilli"
                };
                return $"{gauche} · {droite}";
            }
        }
    }

    /// <summary>
    /// Lecture / écriture des cartographies V2, dans un fichier propre au bloc — séparé de la
    /// Phase d'évaluation, qu'on ne touche pas (cf. PLAN_CARTOGRAPHIE_ENFANT_V2.md).
    ///
    /// Emplacement : {patient}/{année}/cartographies/{yyyy-MM-dd}_cartographie.md
    ///
    /// UNE FICHE PAR SÉANCE. Sauvegarder deux fois le même jour met à jour la même fiche —
    /// sans quoi l'onglet BILANS se remplirait de doublons au fil des clics. Une nouvelle date
    /// crée une nouvelle fiche.
    ///
    /// Le format YAML + Markdown est celui du reste du dossier, et il est déjà à la bonne forme
    /// pour la suite : quand la lecture du scan existera, les 5 scores du questionnaire iront
    /// rejoindre les 18 axes dans ce même fichier.
    /// </summary>
    public class CartographieV2Service
    {
        /// <summary>
        /// Le dossier du patient est passé directement plutôt que reconstruit depuis son nom :
        /// <c>PatientIndexEntry.DirectoryPath</c> est déjà la vérité, et ça évite tout écart de
        /// convention de nommage entre l'index et PathService.
        /// </summary>
        public static string GetDirectory(string patientDirectory, int year)
            => Path.Combine(patientDirectory, year.ToString(), "cartographies");

        private static string FileNameFor(DateTime date) => $"{date:yyyy-MM-dd}_cartographie.md";

        /// <summary>
        /// Écrit la fiche du jour. Écrase la fiche du même jour si elle existe : c'est la même
        /// séance, pas une seconde cartographie.
        /// </summary>
        public (bool ok, string? path, string? error) Save(string patientDirectory, CartographieV2 c)
        {
            try
            {
                var dir = GetDirectory(patientDirectory, c.Date.Year);
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, FileNameFor(c.Date));

                c.DerniereModif = DateTime.Now;
                File.WriteAllText(path, Serialize(c), new UTF8Encoding(false));
                c.FilePath = path;
                return (true, path, null);
            }
            catch (Exception ex)
            {
                return (false, null, ex.Message);
            }
        }

        /// <summary>Charge la fiche du jour pour ce patient, ou null s'il n'y en a pas.</summary>
        public CartographieV2? LoadForDate(string patientDirectory, DateTime date)
        {
            var path = Path.Combine(GetDirectory(patientDirectory, date.Year), FileNameFor(date));
            return File.Exists(path) ? Load(path) : null;
        }

        /// <summary>
        /// Toutes les cartographies d'un patient, la plus récente d'abord. Toutes années confondues.
        /// </summary>
        public List<CartographieV2> LoadAll(string patientDirectory)
        {
            var res = new List<CartographieV2>();
            try
            {
                if (!Directory.Exists(patientDirectory)) return res;

                foreach (var yearDir in Directory.GetDirectories(patientDirectory))
                {
                    var dir = Path.Combine(yearDir, "cartographies");
                    if (!Directory.Exists(dir)) continue;
                    foreach (var f in Directory.GetFiles(dir, "*_cartographie.md"))
                    {
                        var c = Load(f);
                        if (c != null) res.Add(c);
                    }
                }
            }
            catch { /* lecture best-effort : une année illisible ne doit pas masquer les autres */ }

            return res.OrderByDescending(c => c.Date).ToList();
        }

        /// <summary>
        /// Supprime définitivement une fiche de cartographie. Utile pour retirer un essai, ou
        /// une fiche versée par erreur — la confirmation est du ressort de l'appelant.
        /// </summary>
        public bool Delete(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                File.Delete(path);
                return true;
            }
            catch { return false; }
        }

        public CartographieV2? Load(string path)
        {
            try
            {
                var text = File.ReadAllText(path, Encoding.UTF8);
                var c = new CartographieV2 { FilePath = path };

                // Le bloc YAML porte des lignes de commentaire « # … » qui séparent les axes des
                // scores : elles doivent être SAUTÉES, pas traitées comme la fin du bloc. S'arrêter
                // au premier « # » revenait à ne jamais relire un seul axe.
                var inYaml = false;
                foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
                {
                    var line = raw.Trim();

                    if (line == "---")
                    {
                        if (!inYaml) { inYaml = true; continue; }
                        break;                       // fin du bloc YAML, le corps Markdown suit
                    }
                    if (!inYaml) continue;
                    if (line.Length == 0 || line.StartsWith("#")) continue;

                    var sep = line.IndexOf(':');
                    if (sep <= 0) continue;
                    var key = line[..sep].Trim();
                    var val = line[(sep + 1)..].Trim().Trim('"');

                    switch (key)
                    {
                        case "patient":             c.PatientNom = val; break;
                        case "age":                 if (int.TryParse(val, out var a)) c.Age = a; break;
                        case "bande":               c.BandeCode = val; break;
                        case "scan_image":          c.ScanImagePath = val; break;
                        case "versee_au_dossier":   c.VerseeAuDossier = val.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                        case "questionnaire_parent":
                            c.EtatQuestionnaire = val switch
                            {
                                "scanne" => EtatQuestionnaireParent.Scanne,
                                "remis"  => EtatQuestionnaireParent.Remis,
                                _        => EtatQuestionnaireParent.NonRecueilli
                            };
                            break;
                        case "date":
                            if (DateTime.TryParseExact(val, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                                       DateTimeStyles.None, out var d)) c.Date = d;
                            break;
                        case "date_derniere_modif":
                            if (DateTime.TryParse(val, CultureInfo.InvariantCulture,
                                                  DateTimeStyles.None, out var dm)) c.DerniereModif = dm;
                            break;
                        default:
                            // Axes : « profil.axe: n ». Les scores du questionnaire : « q_axe: n ».
                            if (key.StartsWith("q_") && int.TryParse(val, out var sq))
                                c.ScoresQuestionnaire[key[2..]] = sq;
                            else if (key.Contains('.') && int.TryParse(val, out var v))
                                c.Axes[key] = v;
                            break;
                    }
                }
                return c;
            }
            catch { return null; }
        }

        private static string Serialize(CartographieV2 c)
        {
            var sb = new StringBuilder();
            sb.AppendLine("---");
            sb.AppendLine("type: cartographie_enfant");
            sb.AppendLine("version: 2");
            sb.AppendLine($"patient: \"{c.PatientNom}\"");
            sb.AppendLine($"date: {c.Date:yyyy-MM-dd}");
            sb.AppendLine($"date_derniere_modif: {c.DerniereModif:yyyy-MM-ddTHH:mm}");
            if (c.Age.HasValue) sb.AppendLine($"age: {c.Age}");
            if (!string.IsNullOrEmpty(c.BandeCode)) sb.AppendLine($"bande: {c.BandeCode}");
            sb.AppendLine($"versee_au_dossier: {(c.VerseeAuDossier ? "true" : "false")}");
            var etat = c.EtatQuestionnaire switch
            {
                EtatQuestionnaireParent.Scanne => "scanne",
                EtatQuestionnaireParent.Remis  => "remis",
                _                              => "non_recueilli"
            };
            sb.AppendLine($"questionnaire_parent: {etat}");
            if (!string.IsNullOrEmpty(c.ScanImagePath))
                sb.AppendLine($"scan_image: {c.ScanImagePath}");

            sb.AppendLine();
            sb.AppendLine("# Profils observés (médecin) — 1-5, axe absent = non observé");
            foreach (var kv in c.Axes.Where(kv => kv.Value > 0).OrderBy(kv => kv.Key))
                sb.AppendLine($"{kv.Key}: {kv.Value}");

            if (c.ScoresQuestionnaire.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("# Questionnaire parent — scores 0-6 par axe");
                foreach (var kv in c.ScoresQuestionnaire.OrderBy(kv => kv.Key))
                    sb.AppendLine($"q_{kv.Key}: {kv.Value}");
            }
            sb.AppendLine("---");
            sb.AppendLine();

            // Corps lisible : ce fichier peut être ouvert seul, sans l'application.
            sb.AppendLine($"# Cartographie de l'enfant — {c.Date:dd/MM/yyyy}");
            sb.AppendLine();
            sb.AppendLine($"_{c.EtatLisible}_");
            sb.AppendLine();

            foreach (var profil in ProfilsObservesV2.Profils)
            {
                var lignes = profil.Axes
                    .Select(ax => (ax, val: c.Axes.TryGetValue($"{profil.Key}.{ax.Key}", out var v) ? v : 0))
                    .Where(t => t.val > 0)
                    .ToList();
                if (lignes.Count == 0) continue;

                sb.AppendLine($"## {profil.Label}");
                sb.AppendLine();
                foreach (var (ax, val) in lignes)
                    sb.AppendLine($"- {ax.Label} : **{val}/5** — {(val >= 3 ? ax.Pole5 : ax.Pole1)}");
                sb.AppendLine();
            }

            return sb.ToString();
        }
    }
}
