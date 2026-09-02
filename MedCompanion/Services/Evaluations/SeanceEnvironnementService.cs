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
    /// Fiche de la 3ᵉ séance : orientation diagnostique, cartographie de l'environnement,
    /// évaluation ciblée.
    ///
    /// Même forme que la fiche de cartographie de l'enfant — une fiche par séance, YAML pour les
    /// métadonnées et Markdown pour ce qui se lit. Les listes de l'orientation vivent dans le
    /// corps : ce sont des phrases, pas des métadonnées, et la fiche doit rester lisible sans
    /// l'application.
    /// </summary>
    public class SeanceEnvironnement
    {
        public string   FilePath      { get; set; } = "";
        public string   PatientNom    { get; set; } = "";
        public DateTime Date          { get; set; } = DateTime.Today;
        public DateTime DerniereModif { get; set; } = DateTime.Now;
        public int?     Age           { get; set; }

        public DateTime? DateCloture { get; set; }
        public bool EstCloturee => DateCloture.HasValue;

        // ── Orientation diagnostique ──────────────────────────────────────────
        // Ce n'est PAS un diagnostic : c'est une mise au point de l'attention, faite sur un
        // dossier volontairement incomplet, dont l'unique produit est ce que le médecin ira
        // observer pendant la séance. La synthèse, plus tard, répondra de la complétude.

        public List<string> HypothesesPrincipales { get; set; } = new();
        public List<string> Differentiels         { get; set; } = new();
        public List<string> AEliminer             { get; set; } = new();
        public List<string> PointsVigilance       { get; set; } = new();
        public List<string> QuestionsCliniques    { get; set; } = new();

        public DateTime? OrientationDate { get; set; }

        // ── Évaluation ciblée ─────────────────────────────────────────────────
        // Les axes dérivés de l'orientation, et ce que le médecin a coché en séance.

        public List<AxeCible> Axes { get; set; } = new();

        public DateTime? EvaluationDate { get; set; }

        // ── Cartographie de l'environnement — versant médecin ─────────────────
        //
        // Les 14 items que le médecin cote depuis l'entretien, indexés par leur texte. Les 22
        // autres viendront de la feuille remplie par le parent (carte 5) : c'est seulement une
        // fois les deux moitiés là que les nervures peuvent prendre une couleur.
        //
        // Un item absent du dictionnaire n'est PAS un « non » : il n'a pas été renseigné.

        public Dictionary<string, ReponseProposition> CotationsEnv { get; set; } = new();

        public DateTime? CotationEnvDate { get; set; }

        // ── Feuille parent (carte 5) ──────────────────────────────────────────
        // Les 22 réponses de la feuille remplie en salle d'attente, par clé de feuille : « oui »,
        // « non », ou vide. On garde le DÉTAIL et pas un score — c'est le détail qui sert
        // l'analyse, et le score s'en déduit quand on en a besoin.

        public Dictionary<string, string[]> ReponsesParent { get; set; } = new();

        /// <summary>Feuille scannée retenue — sans elle, « reprendre le dépouillement » n'a rien à montrer.</summary>
        public string? ScanEnvImage { get; set; }

        public string? InformateurEnv    { get; set; }
        public string? InformateurEnvNom { get; set; }

        public DateTime? ReponsesParentDate { get; set; }

        // ── Synthèse de la séance (carte 6) ───────────────────────────────────
        //
        // Deux fiabilités, une par bloc — l'environnement repose pour moitié sur une feuille
        // remplie en salle d'attente, l'évaluation ciblée sur ce que le médecin a vu lui-même.
        // Un seul poids pour les deux traiterait implicitement l'un comme l'autre.
        //
        // Elles qualifient la SOURCE, jamais la valeur : aucun compte n'est corrigé.

        public string? FiabiliteEnv   { get; set; }
        public string? FiabiliteAxes  { get; set; }
        public string? SyntheseTexte  { get; set; }
        public DateTime? SyntheseDate { get; set; }

        public bool HasSynthese => !string.IsNullOrWhiteSpace(SyntheseTexte);

        public string EtatSyntheseLisible => !HasSynthese
            ? "synthèse non rédigée"
            : $"synthèse du {SyntheseDate:dd/MM/yyyy}";

        public int NbReponsesParent
            => ReponsesParent.Sum(kv => kv.Value.Count(v => v is "oui" or "non"));

        public bool HasReponsesParent => NbReponsesParent > 0;

        public string EtatFeuilleParentLisible => !HasReponsesParent
            ? (ScanEnvImage != null ? "feuille scannée, non dépouillée" : "feuille parents non revenue")
            : $"{NbReponsesParent}/{CartographieEnvironnementV2.NbItemsParent} réponses dépouillées";

        public int NbCotationsEnv
            => CotationsEnv.Count(kv => kv.Value != ReponseProposition.NonObservee);

        public bool HasCotationEnv => NbCotationsEnv > 0;

        public string EtatCotationEnvLisible => !HasCotationEnv
            ? "vos items non cotés"
            : $"{NbCotationsEnv}/{CartographieEnvironnementV2.NbItemsMedecin} items cotés";

        public int NbAxes           => Axes.Count;
        public int NbAxesRenseignes => Axes.Count(a => a.EstRenseigne);

        public bool HasEvaluation => Axes.Count > 0;

        public string EtatEvaluationLisible => !HasEvaluation
            ? "évaluation ciblée non construite"
            : $"{NbAxesRenseignes}/{NbAxes} axes renseignés";

        public int NbOrientation =>
            HypothesesPrincipales.Count + Differentiels.Count + AEliminer.Count
            + PointsVigilance.Count + QuestionsCliniques.Count;

        public bool HasOrientation => NbOrientation > 0;

        public string EtatLisible => HasOrientation
            ? $"orientation posée — {NbOrientation} éléments"
            : "orientation non renseignée";

        /// <summary>
        /// Ce que la séance n'a pas produit, en clair, dans l'ordre des cartes.
        ///
        /// Ce n'est PAS une liste d'erreurs. Une feuille qui ne revient pas de la salle d'attente
        /// est un fait clinique, pas une négligence — c'est même le cas prévu depuis le départ. Le
        /// garde-fou nomme ce qui manque et laisse le médecin décider : bloquer la clôture
        /// l'obligerait à inventer des réponses pour pouvoir fermer la séance.
        ///
        /// Porté par la FICHE et non par les écrans : la clôture enregistre d'abord, et c'est de
        /// ce qui a été écrit que l'on doit répondre — pas de ce qui était affiché.
        /// </summary>
        public List<string> PartiesManquantes()
        {
            var m = new List<string>();

            if (!HasOrientation)
                m.Add("Orientation diagnostique — aucun élément posé");

            var constats = Axes.Sum(a => a.Propositions.Count);
            var repondus = Axes.Sum(a => a.NbRepondu);
            if (Axes.Count == 0)          m.Add("Évaluation ciblée — aucun axe construit");
            else if (repondus == 0)       m.Add("Évaluation ciblée — aucun constat renseigné");
            else if (repondus < constats) m.Add($"Évaluation ciblée — {constats - repondus} constat(s) non renseigné(s) sur {constats}");

            var attenduMed = CartographieEnvironnementV2.NbItemsMedecin;
            if (NbCotationsEnv == 0)              m.Add($"Cartographie de l'environnement — aucun de vos {attenduMed} items coté");
            else if (NbCotationsEnv < attenduMed) m.Add($"Cartographie de l'environnement — {attenduMed - NbCotationsEnv} de vos items non cotés");

            var attenduParent = CartographieEnvironnementV2.NbItemsParent;
            if (string.IsNullOrWhiteSpace(ScanEnvImage))
                m.Add("Feuille parents — non revenue de la salle d'attente");
            else if (NbReponsesParent == 0)
                m.Add("Feuille parents — scannée mais non dépouillée");
            else if (NbReponsesParent < attenduParent)
                m.Add($"Feuille parents — {attenduParent - NbReponsesParent} réponse(s) manquante(s) sur {attenduParent}");

            if (!HasSynthese)
                m.Add("Synthèse de la séance — non rédigée");

            return m;
        }
    }

    public class SeanceEnvironnementService
    {
        public static string GetDirectory(string patientDirectory, int year)
            => Path.Combine(patientDirectory, year.ToString(), "environnement");

        private static string FileNameFor(DateTime date) => $"{date:yyyy-MM-dd}_environnement.md";

        public (bool ok, string? path, string? error) Save(string patientDirectory, SeanceEnvironnement s)
        {
            try
            {
                var dir = GetDirectory(patientDirectory, s.Date.Year);
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, FileNameFor(s.Date));

                // La garde regarde l'état DÉJÀ ENREGISTRÉ, pas l'objet en mémoire : sinon elle
                // refuserait l'écriture qui pose la clôture elle-même, et la fiche ne se
                // fermerait jamais. Une fiche close sur le disque n'accepte plus rien.
                if (File.Exists(path) && Load(path)?.EstCloturee == true)
                    return (false, null, "Séance clôturée — la fiche est en lecture seule.");

                s.DerniereModif = DateTime.Now;
                File.WriteAllText(path, Serialize(s), new UTF8Encoding(false));
                s.FilePath = path;
                return (true, path, null);
            }
            catch (Exception ex) { return (false, null, ex.Message); }
        }

        public SeanceEnvironnement? LoadForDate(string patientDirectory, DateTime date)
        {
            var path = Path.Combine(GetDirectory(patientDirectory, date.Year), FileNameFor(date));
            return File.Exists(path) ? Load(path) : null;
        }

        public List<SeanceEnvironnement> LoadAll(string patientDirectory)
        {
            var res = new List<SeanceEnvironnement>();
            try
            {
                if (!Directory.Exists(patientDirectory)) return res;
                foreach (var yearDir in Directory.GetDirectories(patientDirectory))
                {
                    var dir = Path.Combine(yearDir, "environnement");
                    if (!Directory.Exists(dir)) continue;
                    foreach (var f in Directory.GetFiles(dir, "*_environnement.md"))
                    {
                        var s = Load(f);
                        if (s != null) res.Add(s);
                    }
                }
            }
            catch { /* une année illisible ne doit pas masquer les autres */ }
            return res.OrderByDescending(s => s.Date).ToList();
        }

        public SeanceEnvironnement? Load(string path)
        {
            try
            {
                var texte = File.ReadAllText(path, Encoding.UTF8);
                var s = new SeanceEnvironnement { FilePath = path };

                var inYaml = false;
                foreach (var raw in texte.Replace("\r\n", "\n").Split('\n'))
                {
                    var line = raw.Trim();
                    if (line == "---")
                    {
                        if (!inYaml) { inYaml = true; continue; }
                        break;
                    }
                    if (!inYaml || line.Length == 0 || line.StartsWith("#")) continue;

                    var sep = line.IndexOf(':');
                    if (sep <= 0) continue;
                    var key = line[..sep].Trim();
                    var val = line[(sep + 1)..].Trim().Trim('"');

                    switch (key)
                    {
                        case "patient": s.PatientNom = val; break;
                        case "age":     if (int.TryParse(val, out var a)) s.Age = a; break;
                        case "date":
                            if (DateTime.TryParseExact(val, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                                       DateTimeStyles.None, out var d)) s.Date = d;
                            break;
                        case "date_derniere_modif":
                            if (DateTime.TryParse(val, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dm))
                                s.DerniereModif = dm;
                            break;
                        case "date_cloture":
                            if (DateTime.TryParse(val, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dc))
                                s.DateCloture = dc;
                            break;
                        case "orientation_date":
                            if (DateTime.TryParse(val, CultureInfo.InvariantCulture, DateTimeStyles.None, out var od))
                                s.OrientationDate = od;
                            break;
                        case "evaluation_date":
                            if (DateTime.TryParse(val, CultureInfo.InvariantCulture, DateTimeStyles.None, out var ed))
                                s.EvaluationDate = ed;
                            break;
                        case "cotation_env_date":
                            if (DateTime.TryParse(val, CultureInfo.InvariantCulture, DateTimeStyles.None, out var cd))
                                s.CotationEnvDate = cd;
                            break;
                        case "reponses_parent_date":
                            if (DateTime.TryParse(val, CultureInfo.InvariantCulture, DateTimeStyles.None, out var rp))
                                s.ReponsesParentDate = rp;
                            break;
                        case "synthese_date":
                            if (DateTime.TryParse(val, CultureInfo.InvariantCulture, DateTimeStyles.None, out var sd))
                                s.SyntheseDate = sd;
                            break;
                        case "fiabilite_env":  s.FiabiliteEnv  = val; break;
                        case "fiabilite_axes": s.FiabiliteAxes = val; break;
                        case "scan_env_image":      s.ScanEnvImage = val; break;
                        case "informateur_env":     s.InformateurEnv = val; break;
                        case "informateur_env_nom": s.InformateurEnvNom = val; break;
                    }
                }

                s.HypothesesPrincipales = LireSection(texte, TitreHypotheses);
                s.Differentiels         = LireSection(texte, TitreDifferentiels);
                s.AEliminer             = LireSection(texte, TitreAEliminer);
                s.PointsVigilance       = LireSection(texte, TitreVigilance);
                s.QuestionsCliniques    = LireSection(texte, TitreQuestions);
                s.Axes                  = LireAxes(texte);
                s.CotationsEnv           = LireCotationsEnv(texte);
                s.ReponsesParent         = LireReponsesParent(texte);
                s.SyntheseTexte          = LireSynthese(texte);

                return s;
            }
            catch { return null; }
        }

        public bool Delete(string path)
        {
            try { if (!File.Exists(path)) return false; File.Delete(path); return true; }
            catch { return false; }
        }

        private const string TitreHypotheses    = "### Hypothèses principales";
        private const string TitreDifferentiels = "### Diagnostics différentiels";
        private const string TitreAEliminer     = "### À éliminer prudemment";
        private const string TitreVigilance     = "### Points de vigilance";
        private const string TitreQuestions     = "### Questions cliniques";

        /// <summary>
        /// Lit les puces d'une section jusqu'au prochain titre. Les listes sont écrites en
        /// Markdown plutôt qu'en YAML : ce sont des phrases cliniques, elles doivent se lire
        /// telles quelles quand on ouvre le fichier.
        /// </summary>
        private static List<string> LireSection(string texte, string titre)
        {
            var res = new List<string>();
            var i = texte.IndexOf(titre, StringComparison.Ordinal);
            if (i < 0) return res;

            foreach (var raw in texte[(i + titre.Length)..].Replace("\r\n", "\n").Split('\n'))
            {
                var l = raw.Trim();
                if (l.StartsWith("#")) break;               // section suivante
                if (!l.StartsWith("- ")) continue;
                var v = l[2..].Trim();
                if (v.Length > 0) res.Add(v);
            }
            return res;
        }

        private static string Serialize(SeanceEnvironnement s)
        {
            var sb = new StringBuilder();
            sb.AppendLine("---");
            sb.AppendLine("type: cartographie_environnement");
            sb.AppendLine("version: 1");
            sb.AppendLine($"patient: \"{s.PatientNom}\"");
            sb.AppendLine($"date: {s.Date:yyyy-MM-dd}");
            sb.AppendLine($"date_derniere_modif: {s.DerniereModif:yyyy-MM-ddTHH:mm}");
            if (s.Age.HasValue)             sb.AppendLine($"age: {s.Age}");
            if (s.OrientationDate.HasValue) sb.AppendLine($"orientation_date: {s.OrientationDate.Value:yyyy-MM-ddTHH:mm}");
            if (s.EvaluationDate.HasValue)  sb.AppendLine($"evaluation_date: {s.EvaluationDate.Value:yyyy-MM-ddTHH:mm}");
            if (s.CotationEnvDate.HasValue) sb.AppendLine($"cotation_env_date: {s.CotationEnvDate.Value:yyyy-MM-ddTHH:mm}");
            if (s.ReponsesParentDate.HasValue) sb.AppendLine($"reponses_parent_date: {s.ReponsesParentDate.Value:yyyy-MM-ddTHH:mm}");
            if (s.SyntheseDate.HasValue)       sb.AppendLine($"synthese_date: {s.SyntheseDate.Value:yyyy-MM-ddTHH:mm}");
            if (!string.IsNullOrWhiteSpace(s.FiabiliteEnv))  sb.AppendLine($"fiabilite_env: {s.FiabiliteEnv}");
            if (!string.IsNullOrWhiteSpace(s.FiabiliteAxes)) sb.AppendLine($"fiabilite_axes: {s.FiabiliteAxes}");
            if (!string.IsNullOrWhiteSpace(s.ScanEnvImage))      sb.AppendLine($"scan_env_image: \"{s.ScanEnvImage}\"");
            if (!string.IsNullOrWhiteSpace(s.InformateurEnv))    sb.AppendLine($"informateur_env: {s.InformateurEnv}");
            if (!string.IsNullOrWhiteSpace(s.InformateurEnvNom)) sb.AppendLine($"informateur_env_nom: \"{s.InformateurEnvNom}\"");
            if (s.DateCloture.HasValue)     sb.AppendLine($"date_cloture: {s.DateCloture.Value:yyyy-MM-ddTHH:mm}");
            sb.AppendLine("---");
            sb.AppendLine();

            sb.AppendLine($"# Séance 3 — Environnement & évaluation ciblée — {s.Date:dd/MM/yyyy}");
            sb.AppendLine();

            if (s.HasOrientation)
            {
                sb.AppendLine("## Orientation diagnostique");
                sb.AppendLine();
                sb.AppendLine("_Mise au point de l'attention avant la séance — ce n'est pas un diagnostic._");
                sb.AppendLine();
                Section(sb, TitreHypotheses,    s.HypothesesPrincipales);
                Section(sb, TitreDifferentiels, s.Differentiels);
                Section(sb, TitreAEliminer,     s.AEliminer);
                Section(sb, TitreVigilance,     s.PointsVigilance);
                Section(sb, TitreQuestions,     s.QuestionsCliniques);
            }

            EcrireAxes(sb, s.Axes);
            EcrireCotationsEnv(sb, s.CotationsEnv);
            EcrireReponsesParent(sb, s);
            EcrireSynthese(sb, s);

            return sb.ToString();
        }

        // ── Évaluation ciblée : écriture et relecture ─────────────────────────
        //
        // Les axes vivent dans le CORPS, en Markdown, et pas en YAML : la fiche doit se lire telle
        // quelle quand on l'ouvre hors de l'application, et une liste de constats cochés est
        // exactement ce qu'on veut pouvoir lire d'un coup d'œil.
        //
        // Trois marques, une par état — et « non observé » a la sienne. C'est le point à ne pas
        // relâcher : si un item non coché s'écrivait comme un « non », la fiche affirmerait
        // demain ce que personne n'a observé aujourd'hui.

        private const string TitreEvaluation = "## Évaluation ciblée";

        private const string MarqueOui = "- [oui] ";
        private const string MarqueNon = "- [non] ";
        private const string MarqueNo  = "- [ ] ";
        private const string PrefixeRattachement = "_Ce qu'il sert : ";

        private static void EcrireAxes(StringBuilder sb, List<AxeCible> axes)
        {
            if (axes.Count == 0) return;

            sb.AppendLine(TitreEvaluation);
            sb.AppendLine();
            sb.AppendLine("_Constats observés en séance. Une case vide signifie « non observé » — jamais « non »._");
            sb.AppendLine();

            foreach (var axe in axes)
            {
                sb.AppendLine($"### {axe.Intitule}");
                sb.AppendLine();
                if (!string.IsNullOrWhiteSpace(axe.Rattachement))
                {
                    sb.AppendLine($"{PrefixeRattachement}{axe.Rattachement.Trim()}_");
                    sb.AppendLine();
                }

                foreach (var p in axe.Propositions)
                {
                    var marque = p.Reponse switch
                    {
                        ReponseProposition.Oui => MarqueOui,
                        ReponseProposition.Non => MarqueNon,
                        _                      => MarqueNo
                    };
                    sb.AppendLine($"{marque}{p.Texte}");
                }
                sb.AppendLine();

                if (axe.HasRemarques)
                {
                    foreach (var ligne in axe.Remarques.Replace("\r\n", "\n").Split('\n'))
                        sb.AppendLine($"> {ligne}");
                    sb.AppendLine();
                }
            }
        }

        private static List<AxeCible> LireAxes(string texte)
        {
            var res = new List<AxeCible>();

            var i = texte.IndexOf(TitreEvaluation, StringComparison.Ordinal);
            if (i < 0) return res;

            AxeCible? courant = null;
            var remarques = new List<string>();

            void Cloturer()
            {
                if (courant == null) return;
                courant.Remarques = string.Join("\n", remarques).Trim();
                courant.Rebrancher();
                res.Add(courant);
                remarques.Clear();
            }

            foreach (var raw in texte[(i + TitreEvaluation.Length)..].Replace("\r\n", "\n").Split('\n'))
            {
                var l = raw.Trim();

                if (l.StartsWith("### "))
                {
                    Cloturer();
                    courant = new AxeCible { Intitule = l[4..].Trim() };
                    continue;
                }

                // Un titre de niveau 1 ou 2 ferme la section — on ne déborde pas sur la suivante.
                if (l.StartsWith("# ") || l.StartsWith("## ")) break;

                if (courant == null) continue;

                if (l.StartsWith(PrefixeRattachement))
                {
                    courant.Rattachement = l[PrefixeRattachement.Length..].TrimEnd('_').Trim();
                    continue;
                }

                // L'ordre compte : "- [ ] " ne doit pas être testé avant les deux autres, sinon un
                // "- [oui] " ne serait jamais reconnu.
                if (l.StartsWith(MarqueOui)) { courant.Ajouter(Prop(l[MarqueOui.Length..], ReponseProposition.Oui)); continue; }
                if (l.StartsWith(MarqueNon)) { courant.Ajouter(Prop(l[MarqueNon.Length..], ReponseProposition.Non)); continue; }
                if (l.StartsWith(MarqueNo))  { courant.Ajouter(Prop(l[MarqueNo.Length..],  ReponseProposition.NonObservee)); continue; }

                if (l.StartsWith("> ")) remarques.Add(l[2..]);
                else if (l == ">")      remarques.Add("");
            }

            Cloturer();
            return res;

            static PropositionObservable Prop(string texte, ReponseProposition r)
                => new() { Texte = texte.Trim(), Reponse = r };
        }

        // ── Synthèse : écriture et relecture ──────────────────────────────────

        private const string TitreSynthese = "## Synthèse de la séance";

        private static void EcrireSynthese(StringBuilder sb, SeanceEnvironnement s)
        {
            if (!s.HasSynthese) return;

            sb.AppendLine(TitreSynthese);
            sb.AppendLine();
            sb.AppendLine(s.SyntheseTexte!.Trim());
            sb.AppendLine();
        }

        /// <summary>
        /// Relit le texte de la synthèse. Il est stocké TEL QUEL dans le corps, sans balisage
        /// propre : c'est de la prose clinique, et l'encadrer d'une syntaxe la rendrait moins
        /// lisible que le fichier ne l'est aujourd'hui.
        /// </summary>
        private static string? LireSynthese(string texte)
        {
            var i = texte.IndexOf(TitreSynthese, StringComparison.Ordinal);
            if (i < 0) return null;

            var lignes = new List<string>();
            foreach (var raw in texte[(i + TitreSynthese.Length)..].Replace("\r\n", "\n").Split('\n'))
            {
                var l = raw.TrimEnd();
                if (l.TrimStart().StartsWith("## ") || l.TrimStart().StartsWith("# ")) break;
                lignes.Add(l);
            }

            var res = string.Join("\n", lignes).Trim();
            return res.Length == 0 ? null : res;
        }

        // ── Feuille parent : écriture et relecture ────────────────────────────
        //
        // Même grammaire de marques que le reste de la fiche — `- [oui]`, `- [non]`, `- [ ]` — pour
        // qu'une seule règle vaille partout : une case vide n'est jamais un « non ». Ici elle veut
        // dire que le parent n'a pas répondu à cette ligne, ce qui arrive et se lit.

        private const string TitreFeuilleParent = "## Cartographie de l'environnement — feuille parents";

        private static void EcrireReponsesParent(StringBuilder sb, SeanceEnvironnement s)
        {
            if (s.ReponsesParent.Count == 0) return;

            sb.AppendLine(TitreFeuilleParent);
            sb.AppendLine();

            var qui = s.InformateurEnv switch
            {
                "mere"  => "la mère",
                "pere"  => "le père",
                "autre" => "un autre adulte",
                _       => null
            };
            var nom = string.IsNullOrWhiteSpace(s.InformateurEnvNom) ? "" : $" ({s.InformateurEnvNom.Trim()})";
            sb.AppendLine(qui == null
                ? "_Informateur non renseigné. Une case vide signifie « non répondu » — jamais « non »._"
                : $"_Remplie par {qui}{nom}. Une case vide signifie « non répondu » — jamais « non »._");
            sb.AppendLine();

            // Parcours du CATALOGUE : la fiche garde l'ordre des feuilles, qui est ce qui la rend
            // lisible, et le texte de chaque item est réécrit à côté de la réponse.
            foreach (var feuille in CartographieEnvironnementV2.Feuilles)
            {
                if (!s.ReponsesParent.TryGetValue(feuille.Key, out var reps)) continue;
                var items = feuille.ItemsParent.ToList();
                if (items.Count == 0) continue;

                sb.AppendLine($"### {feuille.Label} — {feuille.SousTitre}");
                sb.AppendLine();
                for (int i = 0; i < items.Count; i++)
                {
                    var v = i < reps.Length ? reps[i] : "";
                    var marque = v switch
                    {
                        "oui" => MarqueOui,
                        "non" => MarqueNon,
                        _     => MarqueNo
                    };
                    sb.AppendLine($"{marque}{items[i].Texte}");
                }
                sb.AppendLine();
            }
        }

        /// <summary>
        /// Relit les réponses du parent en s'appuyant sur l'ORDRE des items dans chaque feuille du
        /// catalogue — pas sur leur texte, contrairement aux items du médecin.
        ///
        /// La raison est que la feuille papier numérote ses lignes de 1 à n : c'est la position qui
        /// fait foi côté parent, et un item reformulé garde sa place. Chaque tableau relu a donc
        /// exactement la longueur attendue par le catalogue, quitte à être complété de vides.
        /// </summary>
        private static Dictionary<string, string[]> LireReponsesParent(string texte)
        {
            var res = new Dictionary<string, string[]>();

            var i = texte.IndexOf(TitreFeuilleParent, StringComparison.Ordinal);
            if (i < 0) return res;

            var parTitre = CartographieEnvironnementV2.Feuilles
                .ToDictionary(f => $"{f.Label} — {f.SousTitre}", f => f);

            FeuilleV2? courante = null;
            var lignes = new List<string>();

            void Cloturer()
            {
                if (courante == null) return;
                var attendu = courante.ItemsParent.Count();
                var t = new string[attendu];
                for (int k = 0; k < attendu; k++) t[k] = k < lignes.Count ? lignes[k] : "";
                res[courante.Key] = t;
                lignes.Clear();
            }

            foreach (var raw in texte[(i + TitreFeuilleParent.Length)..].Replace("\r\n", "\n").Split('\n'))
            {
                var l = raw.Trim();

                if (l.StartsWith("### "))
                {
                    Cloturer();
                    parTitre.TryGetValue(l[4..].Trim(), out courante);
                    continue;
                }
                if (l.StartsWith("# ") || l.StartsWith("## ")) break;   // section suivante
                if (courante == null) continue;

                if (l.StartsWith(MarqueOui))      lignes.Add("oui");
                else if (l.StartsWith(MarqueNon)) lignes.Add("non");
                else if (l.StartsWith(MarqueNo))  lignes.Add("");
            }

            Cloturer();
            return res;
        }

        // ── Cartographie de l'environnement : écriture et relecture ───────────

        private const string TitreCotationEnv = "## Cartographie de l'environnement — versant médecin";

        private static void EcrireCotationsEnv(StringBuilder sb, Dictionary<string, ReponseProposition> cotations)
        {
            if (cotations.Count == 0) return;

            sb.AppendLine(TitreCotationEnv);
            sb.AppendLine();
            sb.AppendLine("_Cotée depuis l'entretien. Une case vide signifie « non renseigné » — jamais « non »._");
            sb.AppendLine();

            // Parcours du CATALOGUE, pas du dictionnaire : la fiche garde ainsi l'ordre des
            // feuilles et des nervures, qui est ce qui la rend lisible.
            foreach (var feuille in CartographieEnvironnementV2.Feuilles)
            {
                var lignes = new List<(string nervure, string texte, ReponseProposition r)>();
                foreach (var nervure in feuille.Nervures)
                    foreach (var item in nervure.Items.Where(i => i.Source == SourceItemEnv.Medecin))
                        if (cotations.TryGetValue(item.Texte, out var r))
                            lignes.Add((nervure.Label, item.Texte, r));

                if (lignes.Count == 0) continue;

                sb.AppendLine($"### {feuille.Label} — {feuille.SousTitre}");
                sb.AppendLine();

                string? nervureCourante = null;
                foreach (var (nervure, texte, r) in lignes)
                {
                    if (nervure != nervureCourante)
                    {
                        if (nervureCourante != null) sb.AppendLine();
                        sb.AppendLine($"**{nervure}**");
                        sb.AppendLine();
                        nervureCourante = nervure;
                    }

                    var marque = r switch
                    {
                        ReponseProposition.Oui => MarqueOui,
                        ReponseProposition.Non => MarqueNon,
                        _                      => MarqueNo
                    };
                    sb.AppendLine($"{marque}{texte}");
                }
                sb.AppendLine();
            }
        }

        /// <summary>
        /// Relit les cotations en les rapprochant du catalogue PAR LE TEXTE de l'item.
        ///
        /// Conséquence assumée : si un item est un jour reformulé, la fiche perd la réponse qui
        /// portait sur l'ancienne formulation. C'est voulu — la faire glisser silencieusement sur
        /// le nouveau libellé attribuerait au médecin une réponse à une question qu'on ne lui a
        /// pas posée.
        /// </summary>
        private static Dictionary<string, ReponseProposition> LireCotationsEnv(string texte)
        {
            var res = new Dictionary<string, ReponseProposition>();

            var i = texte.IndexOf(TitreCotationEnv, StringComparison.Ordinal);
            if (i < 0) return res;

            var connus = CartographieEnvironnementV2.Feuilles
                .SelectMany(f => f.Items)
                .Where(it => it.Source == SourceItemEnv.Medecin)
                .Select(it => it.Texte)
                .ToHashSet();

            foreach (var raw in texte[(i + TitreCotationEnv.Length)..].Replace("\r\n", "\n").Split('\n'))
            {
                var l = raw.Trim();
                if (l.StartsWith("# ") || l.StartsWith("## ")) break;   // section suivante

                ReponseProposition r;
                string corps;

                // L'ordre compte : "- [ ] " ne doit pas être testé avant les deux autres.
                if (l.StartsWith(MarqueOui))      { r = ReponseProposition.Oui;         corps = l[MarqueOui.Length..]; }
                else if (l.StartsWith(MarqueNon)) { r = ReponseProposition.Non;         corps = l[MarqueNon.Length..]; }
                else if (l.StartsWith(MarqueNo))  { r = ReponseProposition.NonObservee; corps = l[MarqueNo.Length..]; }
                else continue;

                corps = corps.Trim();
                if (connus.Contains(corps)) res[corps] = r;
            }

            return res;
        }

        private static void Section(StringBuilder sb, string titre, List<string> items)
        {
            if (items.Count == 0) return;
            sb.AppendLine(titre);
            sb.AppendLine();
            foreach (var i in items) sb.AppendLine($"- {i}");
            sb.AppendLine();
        }
    }
}
