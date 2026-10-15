using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MedCompanion.Models;
using MedCompanion.Models.Carte;
using MedCompanion.Services.Agenda;

namespace MedCompanion.Services.Carte
{
    /// <summary>
    /// Construit la carte d'un patient depuis son dossier (PLAN_CARTE_PATIENT.md, étape 1 : sans IA).
    ///
    /// Ordre de recherche fixé par le médecin : l'ADMIN du dossier bleu d'abord (patient.json,
    /// formulaire d'admission, intervenants), puis la 1ère consultation (ses blocs structurés),
    /// puis les fichiers du dossier (bilans, courriers, ordonnances). Ce qui manque reste visible,
    /// en gris : Med ne comble jamais un trou par une supposition.
    /// </summary>
    public class CartePatientService
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
        private static readonly JsonSerializerOptions JsonLecture = new() { PropertyNameCaseInsensitive = true };

        /// <summary>Ce que la carte reprend du mode Consultation, déjà calculé là-bas.</summary>
        public record Suivi(string ConsultationsTexte, string Premiere, string Derniere,
                            IReadOnlyList<(string libelle, int statut, DateTime? date)> Etapes);

        private const int MaxLignesListe = 4;

        public CartePatient Construire(string dossierPatient, Suivi suivi)
        {
            var carte = new CartePatient();
            var info = Path.Combine(dossierPatient, "info_patient");
            var patient = LireJson(Path.Combine(info, "patient.json"));
            var formulaire = LireFormulaire(Path.Combine(info, "formulaire_data.json"));
            var premiere = LirePremiereConsultation(dossierPatient);
            var completion = LireCompletion(dossierPatient);

            Centre(carte, patient, dossierPatient, completion);
            carte.Famille = Famille(carte, patient, formulaire, premiere, completion);
            carte.Motif = Motif(carte, premiere, completion);
            carte.Suivi = BlocSuivi(dossierPatient, suivi);
            carte.Bilans = Bilans(dossierPatient, info, out var bilansEnAttente);
            carte.Courriers = Courriers(dossierPatient);
            carte.Traitement = Traitement(dossierPatient);
            carte.Autour = Autour(patient, info);
            Parcours(carte, suivi);
            carte.PointsOuverts = PointsOuverts(carte, dossierPatient, suivi, bilansEnAttente);

            // Un champ déjà soumis au médecin — même laissé vide, parce que les notes n'en disaient
            // rien — n'est plus « à compléter » : sinon Med le reproposerait à chaque ouverture.
            if (completion != null)
                carte.ChampsManquants.RemoveAll(k => completion.Champs.ContainsKey(k));
            return carte;
        }

        // ── Complétion depuis les notes (étape 2) ───────────────────────────

        private static string CheminCompletion(string dossierPatient)
            => Path.Combine(dossierPatient, "info_patient", "carte.json");

        public static CompletionCarte? LireCompletion(string dossierPatient)
        {
            try
            {
                var p = CheminCompletion(dossierPatient);
                return File.Exists(p)
                    ? JsonSerializer.Deserialize<CompletionCarte>(File.ReadAllText(p, Encoding.UTF8), JsonLecture)
                    : null;
            }
            catch { return null; }
        }

        /// <summary>Fusionne ce que le médecin vient de valider avec ce qui l'avait déjà été.</summary>
        public static void EnregistrerCompletion(string dossierPatient, Dictionary<string, ChampCarte> valides)
        {
            var completion = LireCompletion(dossierPatient) ?? new CompletionCarte();
            foreach (var (cle, champ) in valides) completion.Champs[cle] = champ;
            completion.ValideLe = DateTime.Now;

            Directory.CreateDirectory(Path.Combine(dossierPatient, "info_patient"));
            File.WriteAllText(CheminCompletion(dossierPatient),
                JsonSerializer.Serialize(completion, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                }), new UTF8Encoding(false));
        }

        /// <summary>
        /// Le texte des notes à donner au modèle : les trois plus anciennes (la famille, la fratrie
        /// et le motif s'y trouvent) puis les deux plus récentes (la classe de cette année), chacune
        /// datée et sans son en-tête YAML. Borné pour rester rapide.
        /// </summary>
        public static string TexteDesNotes(string dossierPatient, int maxParNote = 3500)
        {
            var notes = Fichiers(dossierPatient, "notes", "*.md")
                .Select(f => (chemin: f, date: DateDuNom(Path.GetFileName(f)) ?? File.GetLastWriteTime(f)))
                .Where(n => !Regex.IsMatch(Path.GetFileName(n.chemin), @"_(absence|absence_excusee|rdv_deplace)\.md$"))
                .OrderBy(n => n.date).ToList();

            var choisies = notes.Take(3).Concat(notes.Skip(3).TakeLast(2)).ToList();
            var sb = new StringBuilder();
            foreach (var (chemin, date) in choisies)
            {
                string texte;
                try { texte = File.ReadAllText(chemin, Encoding.UTF8); } catch { continue; }
                texte = Regex.Replace(texte, @"^---\s*\r?\n.*?\r?\n---\s*\r?\n", "", RegexOptions.Singleline).Trim();
                if (texte.Length == 0) continue;
                sb.AppendLine($"### Note du {date:dd/MM/yyyy}");
                sb.AppendLine(Tronquer(texte, maxParNote));
                sb.AppendLine();
            }
            return sb.ToString();
        }

        // ── Centre ──────────────────────────────────────────────────────────

        /// <summary>« Parents séparés » + « (note du 05/01/2026) » : une valeur des notes dit d'où elle vient.</summary>
        private static string AvecNote(ChampCarte c) => c.Note.Length > 0 ? $"{c.Valeur} (note du {c.Note})" : c.Valeur;

        private static void Centre(CartePatient carte, JsonElement? p, string dossierPatient, CompletionCarte? completion)
        {
            var prenom = Str(p, "Prenom");
            var nom = Str(p, "Nom");
            carte.Nom = (prenom + " " + nom).Trim();
            if (carte.Nom.Length == 0) carte.Nom = Path.GetFileName(dossierPatient).Replace('_', ' ');

            if (DateTime.TryParse(Str(p, "Dob"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dob))
            {
                int age = DateTime.Today.Year - dob.Year;
                if (DateTime.Today < dob.AddYears(age)) age--;
                carte.AgeNaissance = $"{age} ans · né{(Str(p, "Sexe") == "F" ? "e" : "")} le {dob:dd/MM/yyyy}";
            }

            var classe = Str(p, "Classe");
            var ecole = Str(p, "Ecole");
            var commune = Str(p, "EcoleCommune");
            if (classe.Length == 0 && ecole.Length == 0)
            {
                // Les notes en dernier recours, datées : une classe d'une autre année se voit.
                var c = completion?.Champ("classe");
                var e = completion?.Champ("ecole");
                if (c != null || e != null)
                {
                    var note = (c ?? e)!.Note;
                    carte.Scolarite = string.Join(" — ", new[] { c?.Valeur ?? "", e?.Valeur ?? "" }.Where(s => s.Length > 0))
                                      + (note.Length > 0 ? $" (note du {note})" : "");
                    return;
                }
                carte.Scolarite = "Classe et école non renseignées";
                carte.ScolariteVide = true;
                carte.ChampsManquants.AddRange(new[] { "ecole", "classe" });
                return;
            }

            var morceaux = new List<string>();
            if (classe.Length > 0) morceaux.Add(classe);
            if (ecole.Length > 0) morceaux.Add((ecole.StartsWith("école", StringComparison.OrdinalIgnoreCase) ||
                                                ecole.StartsWith("collège", StringComparison.OrdinalIgnoreCase) ||
                                                ecole.StartsWith("lycée", StringComparison.OrdinalIgnoreCase) ? "" : "école ") + ecole
                                               + (commune.Length > 0 ? $", {commune}" : ""));
            carte.Scolarite = string.Join(" — ", morceaux);

            // Une scolarité confirmée avant la rentrée en cours est peut-être périmée.
            if (DateTime.TryParse(Str(p, "DateConfirmationScolarite"), CultureInfo.InvariantCulture,
                                  DateTimeStyles.None, out var confirmee) && confirmee < DebutAnneeScolaire())
                carte.Scolarite += " (avant cette rentrée)";
        }

        private static DateTime DebutAnneeScolaire()
        {
            var t = DateTime.Today;
            return new DateTime(t.Month >= 9 ? t.Year : t.Year - 1, 9, 1);
        }

        // ── Famille ─────────────────────────────────────────────────────────

        private static BlocCarte Famille(CartePatient carte, JsonElement? p, FormulaireData? f,
                                         Dictionary<string, string> premiere, CompletionCarte? completion)
        {
            var bloc = new BlocCarte("👪", "Famille", "admin + 1ère consult.");
            var familleTexte = premiere.GetValueOrDefault("famille", "");

            // Situation : admin, puis formulaire d'admission, puis la 1ère consultation.
            var situationAdmin = Str(p, "SituationParentale");
            string situation = situationAdmin switch
            {
                "ensemble"                 => "Parents ensemble",
                "separes_garde_principale" => "Parents séparés",
                "separes_garde_alternee"   => "Parents séparés",
                _ => (f?.SituationFamiliale ?? "") switch
                {
                    "ensemble"       => "Parents ensemble",
                    "separes"        => "Parents séparés",
                    "divorces"       => "Parents divorcés",
                    "garde_alternee" => "Parents séparés",
                    "recomposee"     => "Famille recomposée",
                    "autre"          => "Situation familiale : autre",
                    _                => ""
                }
            };
            if (situation.Length == 0)
            {
                var premiereLigne = PremiereLigne(familleTexte);
                if (Regex.IsMatch(premiereLigne, @"\b(ensemble|séparés|separes|divorcés|recompos)", RegexOptions.IgnoreCase))
                    situation = Tronquer(premiereLigne.TrimEnd('.'), 60);
            }
            if (situation.Length == 0 && completion?.Champ("situation") is { } sit)
                situation = AvecNote(sit);
            if (situation.Length > 0) bloc.Ajouter("", situation);
            else { bloc.Manque("Parents", "situation non renseignée"); carte.ChampsManquants.Add("situation"); }

            // Garde : seulement si elle a un sens.
            bool ensemble = situation.Contains("ensemble", StringComparison.OrdinalIgnoreCase);
            if (situation.Length > 0 && !ensemble)
            {
                string garde = situationAdmin == "separes_garde_alternee" || f?.SituationFamiliale == "garde_alternee"
                    ? "alternée"
                    : (f?.GardePrincipale ?? "") switch
                    {
                        "mere"    => "principale chez la mère",
                        "pere"    => "principale chez le père",
                        "parents" => "partagée entre les parents",
                        "autre"   => "autre (voir l'admin)",
                        _         => ""
                    };
                if (garde.Length == 0 && completion?.Champ("garde") is { } g) garde = AvecNote(g);
                if (garde.Length > 0) bloc.Ajouter("Garde", garde);
                else { bloc.Manque("Garde", "non renseignée"); carte.ChampsManquants.Add("garde"); }
            }

            // Les parents : la 1ère consultation dit souvent l'âge et le métier, l'admin le prénom.
            var parents = familleTexte.Replace("\r", "").Split('\n')
                .Select(l => l.Trim().TrimStart('-', '•', ' '))
                .Where(l => Regex.IsMatch(l, @"^(Père|Pere|Mère|Mere)\b", RegexOptions.IgnoreCase))
                .Select(l => Tronquer(l, 70)).ToList();
            if (parents.Count > 0)
                foreach (var l in parents.Take(2)) bloc.Ajouter("", l);
            else if (completion?.Champ("pere") != null || completion?.Champ("mere") != null)
            {
                // Les notes disent souvent l'âge et le métier, que l'admin n'a pas.
                if (completion!.Champ("pere") is { } pe) bloc.Ajouter("Père", Tronquer(AvecNote(pe), 80));
                if (completion.Champ("mere") is { } me) bloc.Ajouter("Mère", Tronquer(AvecNote(me), 80));
            }
            else
            {
                var pere = Majuscule(f?.PerePrenom ?? Str(p, "PerePrenom"));
                var mere = Majuscule(f?.MerePrenom ?? Str(p, "MerePrenom"));
                if (pere.Length > 0 || mere.Length > 0)
                    bloc.Ajouter("", string.Join(" · ", new[] { pere.Length > 0 ? $"Père {pere}" : "", mere.Length > 0 ? $"Mère {mere}" : "" }
                                                         .Where(s => s.Length > 0)));
                else carte.ChampsManquants.AddRange(new[] { "pere", "mere" });
            }

            // Fratrie : la 1ère consultation, sinon les notes validées.
            var fratrie = Regex.Replace(PremieresLignes(premiere.GetValueOrDefault("fratrie", ""), 2),
                                        @"^Fratrie\s*:\s*", "", RegexOptions.IgnoreCase);
            if (fratrie.Length == 0 && completion?.Champ("fratrie") is { } fr) fratrie = AvecNote(fr);
            if (fratrie.Length > 0) bloc.Ajouter("Fratrie", Tronquer(fratrie, 110));
            else { bloc.Manque("Fratrie", "non renseignée"); carte.ChampsManquants.Add("fratrie"); }

            // Antécédents familiaux : le formulaire d'admission.
            if (f == null) bloc.Manque("Antécédents familiaux", "non renseignés");
            else
            {
                var oui = new List<string>();
                void Si(string valeur, string libelle) { if (valeur == "oui") oui.Add(libelle); }
                Si(f.Tdah, "TDAH"); Si(f.Dyslexie, "dys"); Si(f.Tsa, "TSA"); Si(f.TroublesAnxieux, "anxiété");
                Si(f.Depression, "dépression"); Si(f.Bipolarite, "bipolarité"); Si(f.Addictions, "addictions");
                Si(f.TentativeSuicide, "TS");
                if (f.AntecedentsAutre == "oui") oui.Add(f.AntecedentsAutreLabel.Length > 0 ? f.AntecedentsAutreLabel : "autre");

                bool repondu = new[] { f.Tdah, f.Dyslexie, f.Tsa, f.TroublesAnxieux, f.Depression, f.Bipolarite, f.Addictions, f.TentativeSuicide }
                               .Any(v => v is "oui" or "non" or "nsp");
                if (oui.Count > 0) bloc.Ajouter("Antécédents familiaux", string.Join(", ", oui));
                else if (repondu) bloc.Ajouter("Antécédents familiaux", "aucun signalé");
                else bloc.Manque("Antécédents familiaux", "non renseignés");
            }
            return bloc;
        }

        // ── Motif ───────────────────────────────────────────────────────────

        private static BlocCarte Motif(CartePatient carte, Dictionary<string, string> premiere, CompletionCarte? completion)
        {
            if (premiere.Count == 0)
            {
                // Les dossiers anciens ont une 1ère consultation en texte libre : ce que Med en a
                // extrait et que le médecin a validé, sinon rien.
                var blocNotes = new BlocCarte("🎯", "Motif", "notes validées");
                if (completion?.Champ("motif") is { } mo) blocNotes.Ajouter("", Tronquer(AvecNote(mo), 110));
                else { blocNotes.Manque("", "pas de 1ère consultation structurée — à extraire des notes"); carte.ChampsManquants.Add("motif"); }
                if (completion?.Champ("oriente_par") is { } op) blocNotes.Ajouter("Orienté par", Tronquer(op.Valeur, 70));
                else carte.ChampsManquants.Add("oriente_par");
                return blocNotes;
            }

            var bloc = new BlocCarte("🎯", "Motif", "1ère consult.");

            var motif = PremiereLigne(premiere.GetValueOrDefault("motif", ""));
            if (motif.Length > 0) bloc.Ajouter("", Tronquer(motif, 90));
            else bloc.Manque("", "motif non renseigné");

            // Qui a orienté, ou le premier élément de l'histoire : souvent « Demande de l'enseignant ».
            var histoire = PremiereLigne(premiere.GetValueOrDefault("histoire_maladie", ""));
            if (histoire.Length > 0) bloc.Ajouter("", Tronquer(histoire.TrimEnd(':', ' '), 90));
            return bloc;
        }

        // ── Suivi ───────────────────────────────────────────────────────────

        private static BlocCarte BlocSuivi(string dossierPatient, Suivi suivi)
        {
            var bloc = new BlocCarte("📅", "Suivi", "dossier + agenda");
            int seances = Fichiers(dossierPatient, "cartographies", "*_cartographie.md").Count
                        + Fichiers(dossierPatient, "environnement", "*_environnement.md").Count;
            var nb = suivi.ConsultationsTexte.Length > 0 ? suivi.ConsultationsTexte : "aucune consultation";
            bloc.Ajouter("", seances > 0 ? $"{nb} + {seances} séance{(seances > 1 ? "s" : "")}" : nb);

            if (Vraie(suivi.Premiere))
                bloc.Ajouter("", Vraie(suivi.Derniere) && suivi.Derniere != suivi.Premiere
                    ? $"1ère le {suivi.Premiere} · dernière le {suivi.Derniere}"
                    : $"1ère le {suivi.Premiere}");

            var prochain = ProchainRdv(Path.GetFileName(dossierPatient));
            if (prochain.HasValue) bloc.Ajouter("Prochain RDV", prochain.Value.ToString("dddd d MMMM 'à' HH'h'mm", Fr));
            else
            {
                // L'agenda de Med ne voit que la période importée : « aucun » n'a de sens que jusque-là.
                var fin = FinAgendaConnu();
                bloc.Manque("Prochain RDV", fin.HasValue ? $"aucun à l'agenda (connu jusqu'au {fin:dd/MM})" : "aucun à l'agenda");
            }
            return bloc;
        }

        /// <summary>Dernier jour couvert par l'agenda local (import CSV ou lecture d'écran).</summary>
        private static DateTime? FinAgendaConnu()
        {
            try { return new AgendaService().ChargerEtat().PeriodeFin; }
            catch { return null; }
        }

        private static bool Vraie(string s) => !string.IsNullOrWhiteSpace(s) && s != "-";

        private static DateTime? ProchainRdv(string dossier)
        {
            try
            {
                return new AgendaService().Charger(DateTime.Today, DateTime.Today.AddMonths(6))
                    .Where(r => r.Debut > DateTime.Now
                                && string.Equals(r.Dossier, dossier, StringComparison.OrdinalIgnoreCase)
                                && r.Certitude == "sur"
                                && !(r.Statut ?? "").StartsWith("Annul", StringComparison.OrdinalIgnoreCase))
                    .Select(r => (DateTime?)r.Debut).Min();
            }
            catch { return null; }
        }

        // ── Bilans ──────────────────────────────────────────────────────────

        /// <summary>Un courrier dont l'objet contient l'un de ces mots est une demande de bilan ou d'examen.</summary>
        private static readonly Regex MotsDemande = new(
            @"\b(bilan|examen|ecg|électrocardio|electrocardio|eeg|irm|échograph|echograph|cardi|orthophon|psychomot|neuropsy|ophtalm|orl|audiogramme|polysomno|génétique|genetique)",
            RegexOptions.IgnoreCase);

        /// <summary>
        /// Les mots qui disent QUEL examen : « bilan » ou « examen » seuls ne permettent pas de
        /// reconnaître la réponse — n'importe quel bilan reçu ensuite passait pour elle (constaté
        /// le 14/10/2026 sur une demande cardiologique). « cardi » couvre cardiaque, cardiologique,
        /// cardiovasculaire.
        /// </summary>
        private static readonly Regex MotsExamen = new(
            @"(ecg|électrocardio|electrocardio|eeg|irm|échograph|echograph|cardi|orthophon|psychomot|neuropsy|ophtalm|orl|audiogramme|polysomno|génétique|genetique)",
            RegexOptions.IgnoreCase);

        private static BlocCarte Bilans(string dossierPatient, string info,
                                        out List<(DateTime date, string texte, string chemin)> enAttenteSortie)
        {
            var bloc = new BlocCarte("🧪", "Bilans", "documents + courriers + ordonnances");
            var auteurs = LireIntervenants(info)
                .Where(i => !string.IsNullOrWhiteSpace(i.SourceDocument))
                .GroupBy(i => i.SourceDocument!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Nom ?? "", StringComparer.OrdinalIgnoreCase);

            // Faits : les documents rangés en bilans ou en analyses.
            var faits = Fichiers(dossierPatient, Path.Combine("documents", "bilans"), "*.*")
                .Concat(Fichiers(dossierPatient, Path.Combine("documents", "analyses"), "*.*"))
                .Where(f => !f.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                .Select(f => (chemin: f, date: DateDuNom(Path.GetFileName(f)) ?? File.GetLastWriteTime(f)))
                .OrderByDescending(x => x.date).ToList();

            foreach (var (chemin, date) in faits.Take(MaxLignesListe))
            {
                var nom = Path.GetFileName(chemin);
                var auteur = auteurs.TryGetValue(nom, out var a) && a.Length > 0 ? $" ({Abreger(a)})" : "";
                bloc.Ajouter("", $"✓ {TypeDeBilan(nom, chemin)} — {date:MM/yyyy}{auteur}", chemin);
            }
            if (faits.Count > MaxLignesListe) bloc.Ajouter("", $"… et {faits.Count - MaxLignesListe} autre(s)");
            if (faits.Count == 0) bloc.Manque("", "aucun bilan au dossier");

            // Demandés et pas encore reçus : ordonnances de biologie, courriers de demande.
            var enAttente = new List<(DateTime date, string texte, string chemin)>();
            foreach (var bio in Fichiers(dossierPatient, "ordonnances", "BIO_*.md"))
            {
                var d = DateDuNomCompact(Path.GetFileName(bio)) ?? File.GetLastWriteTime(bio);
                bool recu = faits.Any(x => x.date.Date >= d.Date && x.chemin.Contains(Path.DirectorySeparatorChar + "analyses" + Path.DirectorySeparatorChar));
                if (!recu) enAttente.Add((d, $"Biologie — prescrite le {d:dd/MM/yyyy}", bio));
            }
            // La réponse peut être rangée n'importe où dans les documents reçus : bilan, analyse, ou
            // courrier du spécialiste (le compte rendu du cardiologue arrive en « courriers »).
            var recus = Fichiers(dossierPatient, "documents", "*.*", recursif: true)
                .Where(f => !f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                            && !f.Contains(Path.DirectorySeparatorChar + "syntheses_documents" + Path.DirectorySeparatorChar))
                .Select(f => (nom: Path.GetFileName(f).ToLowerInvariant(), date: DateDuNom(Path.GetFileName(f)) ?? File.GetLastWriteTime(f)))
                .ToList();

            foreach (var c in LireCourriers(dossierPatient).Where(c => MotsDemande.IsMatch(c.objet)))
            {
                var mots = MotsExamen.Matches(c.objet).Select(m => m.Value.ToLowerInvariant()).ToHashSet();
                // Sans mot précis, Med ne peut pas reconnaître la réponse : la demande reste affichée.
                bool recu = mots.Count > 0 && recus.Any(x => x.date.Date >= c.date.Date && mots.Any(m => x.nom.Contains(m)));
                if (!recu) enAttente.Add((c.date, $"{Tronquer(c.objet, 60)} — demandé le {c.date:dd/MM/yyyy}", c.chemin));
            }

            enAttenteSortie = enAttente;
            if (enAttente.Count == 0) bloc.Manque("Demandés en attente", "aucun");
            else
            {
                bloc.Ajouter("", "Demandés, pas encore reçus :");
                foreach (var e in enAttente.OrderByDescending(e => e.date).Take(3))
                    bloc.Ajouter("", "○ " + e.texte, e.chemin);
            }
            return bloc;
        }

        /// <summary>« 2025-08-04_bilan-neuropsychologique-Liam-CHEDRU.pdf » → « Neuropsychologique ».</summary>
        private static string TypeDeBilan(string nom, string chemin)
        {
            var sansDate = Regex.Replace(Path.GetFileNameWithoutExtension(nom), @"^\d{4}-\d{2}-\d{2}_?", "");
            var m = Regex.Match(sansDate, @"bilan-([a-zà-ÿ]+)", RegexOptions.IgnoreCase);
            if (m.Success) return Majuscule(m.Groups[1].Value);
            if (chemin.Contains(Path.DirectorySeparatorChar + "analyses" + Path.DirectorySeparatorChar))
                return "Analyses biologiques";
            var mots = sansDate.Split('-', '_', '.').Where(w => w.Length > 2).Take(3);
            return Majuscule(string.Join(" ", mots));
        }

        // ── Courriers ───────────────────────────────────────────────────────

        private record Courrier(DateTime date, string destinataire, string objet, bool brouillon, string chemin);

        private static List<Courrier> LireCourriers(string dossierPatient)
        {
            var liste = new List<Courrier>();
            var vus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var md in Fichiers(dossierPatient, "courriers", "*.md"))
            {
                vus.Add(Path.GetFileNameWithoutExtension(md));
                string texte;
                try { texte = File.ReadAllText(md, Encoding.UTF8); } catch { continue; }

                var date = DateDuNom(Path.GetFileName(md)) ?? File.GetLastWriteTime(md);
                // Le destinataire commence parfois par un pictogramme (« 👨‍⚕️ Confrère médecin »).
                var dest = Regex.Replace(
                    Regex.Match(texte, @"À l'attention de\s*:\s*(.+)", RegexOptions.IgnoreCase).Groups[1].Value,
                    @"^[^\p{L}\p{N}]+", "").Trim();
                var objet = Regex.Match(texte, @"^#*\s*Objet\s*:\s*(.+)$", RegexOptions.Multiline | RegexOptions.IgnoreCase)
                                 .Groups[1].Value.Trim().Trim('*');
                bool brouillon = Regex.IsMatch(texte, @"^status:\s*""?brouillon", RegexOptions.Multiline);
                liste.Add(new Courrier(date, dest, objet, brouillon, md));
            }

            // Les plus anciens n'existent qu'en .docx : on ne connaît que leur date.
            foreach (var docx in Fichiers(dossierPatient, "courriers", "*.docx"))
            {
                if (vus.Contains(Path.GetFileNameWithoutExtension(docx))) continue;
                var date = DateDuNom(Path.GetFileName(docx)) ?? File.GetLastWriteTime(docx);
                liste.Add(new Courrier(date, "", "", false, docx));
            }
            return liste.OrderByDescending(c => c.date).ToList();
        }

        private static BlocCarte Courriers(string dossierPatient)
        {
            var bloc = new BlocCarte("✉️", "Courriers", "courriers");
            var courriers = LireCourriers(dossierPatient);
            if (courriers.Count == 0)
            {
                bloc.Manque("", "aucun courrier fait");
                return bloc;
            }

            foreach (var c in courriers.Take(3))
            {
                var quoi = c.objet.Length > 0 ? Tronquer(c.objet, 55) : "courrier";
                var qui = c.destinataire.Length > 0 ? $" → {Tronquer(c.destinataire, 30)}" : "";
                bloc.Ajouter("", $"{c.date:dd/MM/yyyy} — {quoi}{qui}{(c.brouillon ? " (brouillon)" : "")}", c.chemin);
            }
            if (courriers.Count > 3) bloc.Ajouter("", $"… et {courriers.Count - 3} autre(s)");
            return bloc;
        }

        // ── Traitement ──────────────────────────────────────────────────────

        private static BlocCarte Traitement(string dossierPatient)
        {
            var bloc = new BlocCarte("💊", "Traitement", "ordonnances Med");
            var derniere = Fichiers(dossierPatient, "ordonnances", "MED_*.md")
                .OrderByDescending(f => DateDuNomCompact(Path.GetFileName(f)) ?? File.GetLastWriteTime(f))
                .FirstOrDefault();

            if (derniere == null)
            {
                bloc.Manque("", "aucune ordonnance dans Med");
                bloc.Manque("", "(les ordonnances sécurisées Doctolib n'y figurent pas)");
                return bloc;
            }

            string texte;
            try { texte = File.ReadAllText(derniere, Encoding.UTF8); } catch { texte = ""; }
            var lignes = texte.Replace("\r", "").Split('\n').Select(l => l.Trim()).ToList();

            // Un médicament : une ligne entièrement en gras, sauf les intitulés « **Date :** » et suivants.
            for (int i = 0; i < lignes.Count; i++)
            {
                var m = Regex.Match(lignes[i], @"^\*\*([^*:]+)\*\*$");
                if (!m.Success) continue;
                var poso = lignes.Skip(i + 1).Take(4)
                    .Select(l => Regex.Match(l, @"^\*\*Posologie\s*:\*\*\s*(.+)$", RegexOptions.IgnoreCase))
                    .FirstOrDefault(x => x.Success)?.Groups[1].Value.Trim() ?? "";
                bloc.Ajouter("", Tronquer(m.Groups[1].Value.Trim(), 50) + (poso.Length > 0 ? $" — {poso}" : ""), derniere);
                if (bloc.Lignes.Count >= MaxLignesListe) break;
            }

            var date = DateDuNomCompact(Path.GetFileName(derniere)) ?? File.GetLastWriteTime(derniere);
            if (bloc.Lignes.Count == 0) bloc.Manque("", "dernière ordonnance illisible");
            bloc.Ajouter("Ordonnance du", date.ToString("dd/MM/yyyy"), derniere);
            return bloc;
        }

        // ── Autour de l'enfant ──────────────────────────────────────────────

        private class Intervenant
        {
            public string? Nom { get; set; }
            public string? Profession { get; set; }
            public string? Ville { get; set; }
            public string? SourceDocument { get; set; }
        }

        private static List<Intervenant> LireIntervenants(string info)
        {
            try
            {
                var p = Path.Combine(info, "intervenants.json");
                if (!File.Exists(p)) return new List<Intervenant>();
                return JsonSerializer.Deserialize<List<Intervenant>>(File.ReadAllText(p, Encoding.UTF8), JsonLecture)
                       ?? new List<Intervenant>();
            }
            catch { return new List<Intervenant>(); }
        }

        private static BlocCarte Autour(JsonElement? p, string info)
        {
            var bloc = new BlocCarte("🤝", "Autour de l'enfant", "admin · intervenants");
            foreach (var i in LireIntervenants(info).Take(MaxLignesListe))
            {
                var ville = string.IsNullOrWhiteSpace(i.Ville) ? "" : $", {Tronquer(i.Ville!, 25)}";
                bloc.Ajouter("", $"{Tronquer(i.Profession ?? "Intervenant", 40)} — {Abreger(i.Nom ?? "")}{ville}");
            }

            var mt = $"{Str(p, "MedecinTraitantPrenom")} {Str(p, "MedecinTraitantNom")}".Trim();
            if (mt.Length > 0) bloc.Ajouter("Médecin traitant", "Dr " + mt);
            else bloc.Manque("Médecin traitant", "non renseigné");
            return bloc;
        }

        // ── Parcours ────────────────────────────────────────────────────────

        private static void Parcours(CartePatient carte, Suivi suivi)
        {
            // Un patient suivi avant la mise en place du parcours n'a pas de 1ère consultation
            // structurée : la frise la dit « disponible », mais lui annoncer une 1ère consultation à
            // faire après des mois de suivi serait faux (constaté le 14/10/2026, 5 consultations).
            bool premiereNonFaite = suivi.Etapes.Count > 0 && suivi.Etapes[0].statut < 3;
            if (premiereNonFaite && suivi.ConsultationsTexte.Length > 0)
            {
                carte.Parcours.Add(new EtapeParcours { Libelle = "Suivi commencé avant le parcours 3-11 ans", Etat = EtatEtape.AVenir, Derniere = true });
                return;
            }

            bool prochaineDonnee = false;
            foreach (var (libelle, statut, date) in suivi.Etapes)
            {
                // statut : 0 verrouillée, 1 disponible, 2 en cours, 3 terminée (FriseStageStatus)
                EtatEtape etat = statut switch
                {
                    3 => EtatEtape.Fait,
                    2 => EtatEtape.EnCours,
                    1 when !prochaineDonnee => EtatEtape.Prochaine,
                    _ => EtatEtape.AVenir
                };
                if (etat is EtatEtape.Prochaine or EtatEtape.EnCours) prochaineDonnee = true;
                carte.Parcours.Add(new EtapeParcours { Libelle = libelle, Etat = etat, Date = date });
            }
            if (carte.Parcours.Count > 0) carte.Parcours[^1].Derniere = true;
        }

        // ── Points ouverts (étape 4) ────────────────────────────────────────

        /// <summary>Un bilan demandé n'est « en retard » qu'après ce délai : avant, il est juste attendu.</summary>
        private const int JoursAvantRelanceBilan = 21;

        /// <summary>Une ordonnance est signalée à partir de 7 jours avant sa fin, et jusqu'à 3 mois après.</summary>
        private const int JoursAvantFinOrdonnance = 7;

        /// <summary>
        /// Ce qui attend une action, et rien d'autre (règle « ne rapporter que l'actionnable ») :
        /// chaque ligne doit pouvoir se traiter — relancer, renouveler, finir, demander, programmer.
        /// Calculé par des règles, sans IA : c'est la matière du futur rôle d'interne.
        /// </summary>
        private static BlocCarte PointsOuverts(CartePatient carte, string dossierPatient, Suivi suivi,
                                               List<(DateTime date, string texte, string chemin)> bilansEnAttente)
        {
            var bloc = new BlocCarte("⚑", "Points ouverts", "règles, sans IA");
            var aujourdhui = DateTime.Today;

            // 1. Bilans demandés et pas reçus après 3 semaines : à relancer. Pas une demande restée
            //    en brouillon — elle n'est jamais partie, c'est le point 3 qui la signale.
            var brouillons = LireCourriers(dossierPatient).Where(c => c.brouillon).ToList();
            var cheminsBrouillons = brouillons.Select(c => c.chemin).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var b in bilansEnAttente.Where(b => (aujourdhui - b.date.Date).TotalDays >= JoursAvantRelanceBilan
                                                         && !cheminsBrouillons.Contains(b.chemin))
                                             .OrderBy(b => b.date).Take(2))
                bloc.Ajouter("", $"Relancer : {Tronquer(b.texte, 70)}", b.chemin);

            // 2. Ordonnance qui arrive à sa fin, ou terminée depuis peu.
            var fin = FinDerniereOrdonnance(dossierPatient);
            if (fin.HasValue)
            {
                var (date, finTraitement, chemin) = fin.Value;
                var reste = (finTraitement.Date - aujourdhui).TotalDays;
                if (reste <= JoursAvantFinOrdonnance && reste >= -90)
                    bloc.Ajouter("", reste >= 0
                        ? $"Ordonnance du {date:dd/MM} : se termine le {finTraitement:dd/MM}"
                        : $"Ordonnance du {date:dd/MM} terminée le {finTraitement:dd/MM}", chemin);
            }

            // 3. Courriers restés en brouillon.
            foreach (var c in brouillons.Take(2))
                bloc.Ajouter("", $"Courrier du {c.date:dd/MM} resté en brouillon"
                                 + (c.objet.Length > 0 ? $" ({Tronquer(c.objet, 40)})" : ""), c.chemin);

            // 4. Ce qui est à demander à la famille : ce qu'aucune source ne dit.
            var aDemander = new List<string>();
            if (carte.ScolariteVide) aDemander.Add("classe et école");
            else if (carte.Scolarite.Contains("(avant cette rentrée)")) aDemander.Add("classe de cette rentrée");
            // La garde ne se demande qu'une fois la séparation connue : d'abord la situation.
            if (carte.Famille.Lignes.Any(l => l.Vide && l.Libelle == "Parents")) aDemander.Add("situation des parents");
            else if (carte.Famille.Lignes.Any(l => l.Vide && l.Libelle == "Garde")) aDemander.Add("mode de garde");
            if (carte.Autour.Lignes.Any(l => l.Vide && l.Libelle == "Médecin traitant")) aDemander.Add("médecin traitant");
            if (aDemander.Count > 0) bloc.Ajouter("À demander", string.Join(", ", aDemander));

            // 5. Suivi actif sans prochain rendez-vous — seulement si l'agenda voit assez loin pour
            //    l'affirmer : un enfant vu chaque mois a pu reprendre rendez-vous au-delà.
            bool sansProchain = carte.Suivi.Lignes.Any(l => l.Vide && l.Libelle == "Prochain RDV");
            bool agendaAssezLoin = FinAgendaConnu() is { } finAgenda && finAgenda >= aujourdhui.AddDays(35);
            if (sansProchain && agendaAssezLoin
                && DateTime.TryParseExact(suivi.Derniere, "dd/MM/yyyy", Fr, DateTimeStyles.None, out var derniere)
                && (aujourdhui - derniere).TotalDays <= 90)
                bloc.Ajouter("", "Pas de prochain rendez-vous à l'agenda");

            if (bloc.Lignes.Count == 0) bloc.Manque("", "rien en suspens");
            return bloc;
        }

        /// <summary>
        /// Date et fin de la dernière ordonnance Med : la date + la plus longue durée prescrite
        /// (« 1 mois », « 15 jours », « 2 semaines », parfois collés : « 1mois »).
        /// </summary>
        private static (DateTime date, DateTime fin, string chemin)? FinDerniereOrdonnance(string dossierPatient)
        {
            var derniere = Fichiers(dossierPatient, "ordonnances", "MED_*.md")
                .Select(f => (chemin: f, date: DateDuNomCompact(Path.GetFileName(f)) ?? File.GetLastWriteTime(f)))
                .OrderByDescending(x => x.date).FirstOrDefault();
            if (derniere.chemin == null) return null;

            string texte;
            try { texte = File.ReadAllText(derniere.chemin, Encoding.UTF8); } catch { return null; }

            int jours = 0;
            foreach (Match m in Regex.Matches(texte, @"\*\*Durée\s*:\*\*\s*(\d+)\s*([a-zé]+)", RegexOptions.IgnoreCase))
            {
                int n = int.Parse(m.Groups[1].Value);
                var unite = m.Groups[2].Value.ToLowerInvariant();
                int d = unite.StartsWith("mois") ? n * 30 : unite.StartsWith("sem") ? n * 7 : unite.StartsWith("j") ? n : 0;
                jours = Math.Max(jours, d);
            }
            return jours == 0 ? null : (derniere.date, derniere.date.AddDays(jours), derniere.chemin);
        }

        // ── 1ère consultation ───────────────────────────────────────────────

        /// <summary>
        /// Les blocs de l'interrogatoire de la 1ère consultation (clé → texte), lus dans le YAML de
        /// la note : ils sont déjà structurés, aucune IA n'est nécessaire pour les retrouver.
        /// </summary>
        private static Dictionary<string, string> LirePremiereConsultation(string dossierPatient)
        {
            var resultat = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var note in Fichiers(dossierPatient, "notes", "*.md").OrderBy(f => f))
            {
                string texte;
                try { texte = File.ReadAllText(note, Encoding.UTF8); } catch { continue; }
                if (!Regex.IsMatch(texte, @"^type:\s*""?consultation-premiere", RegexOptions.Multiline)) continue;

                var m = Regex.Match(texte, @"^interrogatoire_blocks_json:\s*\|\s*\r?\n\s*(\[.*\])\s*$", RegexOptions.Multiline);
                if (!m.Success) continue;
                try
                {
                    using var doc = JsonDocument.Parse(m.Groups[1].Value);
                    foreach (var b in doc.RootElement.EnumerateArray())
                    {
                        var cle = b.TryGetProperty("Key", out var k) ? k.GetString() ?? "" : "";
                        var txt = b.TryGetProperty("FreeText", out var t) ? t.GetString() ?? "" : "";
                        if (cle.Length > 0 && !string.IsNullOrWhiteSpace(txt)) resultat[cle] = txt.Trim();
                    }
                    return resultat;
                }
                catch { /* note abîmée : on essaie la suivante */ }
            }
            return resultat;
        }

        // ── Utilitaires ─────────────────────────────────────────────────────

        /// <summary>Les fichiers d'un sous-dossier, toutes années confondues (2025/notes, 2026/notes…).</summary>
        private static List<string> Fichiers(string dossierPatient, string sousDossier, string motif, bool recursif = false)
        {
            var res = new List<string>();
            try
            {
                foreach (var annee in Directory.EnumerateDirectories(dossierPatient)
                                               .Where(d => Regex.IsMatch(Path.GetFileName(d), @"^\d{4}$")))
                {
                    var dir = Path.Combine(annee, sousDossier);
                    if (Directory.Exists(dir))
                        res.AddRange(Directory.GetFiles(dir, motif, recursif ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly));
                }
            }
            catch { }
            return res;
        }

        private static JsonElement? LireJson(string chemin)
        {
            try
            {
                if (!File.Exists(chemin)) return null;
                using var doc = JsonDocument.Parse(File.ReadAllText(chemin, Encoding.UTF8));
                return doc.RootElement.Clone();
            }
            catch { return null; }
        }

        private static FormulaireData? LireFormulaire(string chemin)
        {
            try
            {
                return File.Exists(chemin)
                    ? JsonSerializer.Deserialize<FormulaireData>(File.ReadAllText(chemin, Encoding.UTF8), JsonLecture)
                    : null;
            }
            catch { return null; }
        }

        private static string Str(JsonElement? e, string nom)
            => e.HasValue && e.Value.ValueKind == JsonValueKind.Object && e.Value.TryGetProperty(nom, out var v)
               && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";

        private static DateTime? DateDuNom(string nom)
            => nom.Length >= 10 && DateTime.TryParseExact(nom[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                                          DateTimeStyles.None, out var d) ? d : null;

        /// <summary>« MED_20261009_112345.md » → 09/10/2026.</summary>
        private static DateTime? DateDuNomCompact(string nom)
        {
            var m = Regex.Match(nom, @"_(\d{8})");
            return m.Success && DateTime.TryParseExact(m.Groups[1].Value, "yyyyMMdd", CultureInfo.InvariantCulture,
                                                       DateTimeStyles.None, out var d) ? d : null;
        }

        private static string PremiereLigne(string texte)
            => texte.Replace("\r", "").Split('\n').Select(l => l.Trim().TrimStart('-', '•', ' ')).FirstOrDefault(l => l.Length > 0) ?? "";

        private static string PremieresLignes(string texte, int n)
            => string.Join(" · ", texte.Replace("\r", "").Split('\n').Select(l => l.Trim().TrimStart('-', '•', ' '))
                                       .Where(l => l.Length > 0).Take(n));

        private static string Tronquer(string s, int max)
            => s.Length <= max ? s : s[..(max - 1)].TrimEnd() + "…";

        private static string Majuscule(string s)
        {
            s = (s ?? "").Trim();
            if (s.Length == 0) return s;
            var bas = s.ToLower(Fr);
            return char.ToUpper(bas[0], Fr) + bas[1..];
        }

        /// <summary>« Éva PRÉCLOUX CARRIÈRE » → « É. Précloux Carrière ».</summary>
        private static string Abreger(string nom)
        {
            var mots = nom.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                          .Where(m => !Regex.IsMatch(m, @"^(M\.|Mme|Mlle|Dr\.?)$", RegexOptions.IgnoreCase)).ToList();
            if (mots.Count < 2) return string.Join(" ", mots.Select(Majuscule));
            var initiale = mots[0][0] + ".";
            return initiale + " " + string.Join(" ", mots.Skip(1).Select(m => string.Join("-", m.Split('-').Select(Majuscule))));
        }
    }
}
