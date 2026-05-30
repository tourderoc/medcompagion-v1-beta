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
    /// CRUD des phases d'évaluation. Persistance YAML+MD dans {patient}/evaluations/.
    /// Règle : UNE SEULE évaluation active par patient à la fois (DateCloture == null).
    /// </summary>
    public class EvaluationPhaseService
    {
        private readonly PathService _pathService;

        public EvaluationPhaseService(PathService pathService)
        {
            _pathService = pathService;
        }

        /// <summary>
        /// Renvoie l'évaluation active du patient s'il en a une, sinon null.
        /// </summary>
        public EvaluationPhase? LoadActive(string patientDirectoryPath)
        {
            if (string.IsNullOrEmpty(patientDirectoryPath)) return null;
            var dir = Path.Combine(patientDirectoryPath, "evaluations");
            if (!Directory.Exists(dir)) return null;

            // Parcourir tous les .md d'évaluation, retourner le 1er sans date_cloture
            foreach (var file in Directory.GetFiles(dir, "*_evaluation_*.md").OrderByDescending(f => f))
            {
                var phase = LoadFromFile(file);
                if (phase != null && phase.IsActive) return phase;
            }
            return null;
        }

        /// <summary>
        /// Charge une évaluation depuis un fichier .md spécifique. Renvoie null si le fichier
        /// n'existe pas ou est illisible.
        /// </summary>
        public EvaluationPhase? Load(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return null;
            return LoadFromFile(filePath);
        }

        /// <summary>
        /// Renvoie toutes les évaluations du patient (actives + clôturées), triées par DateDebut.
        /// Fichiers corrompus = ignorés silencieusement.
        /// </summary>
        public List<EvaluationPhase> LoadAll(string patientDirectoryPath)
        {
            var result = new List<EvaluationPhase>();
            if (string.IsNullOrEmpty(patientDirectoryPath)) return result;
            var dir = Path.Combine(patientDirectoryPath, "evaluations");
            if (!Directory.Exists(dir)) return result;

            foreach (var file in Directory.GetFiles(dir, "*_evaluation_*.md"))
            {
                var phase = LoadFromFile(file);
                if (phase != null) result.Add(phase);
            }
            return result.OrderBy(p => p.DateDebut).ToList();
        }

        /// <summary>
        /// Crée une nouvelle évaluation pour le patient. Échoue si une est déjà active.
        /// </summary>
        public EvaluationPhase Create(string patientNomComplet, string patientDirectoryPath)
        {
            if (string.IsNullOrEmpty(patientDirectoryPath))
                throw new InvalidOperationException("DirectoryPath patient manquant.");

            var existing = LoadActive(patientDirectoryPath);
            if (existing != null)
                throw new InvalidOperationException("Une évaluation est déjà active pour ce patient.");

            var dir = Path.Combine(patientDirectoryPath, "evaluations");
            Directory.CreateDirectory(dir);

            // Numérotation incrémentale sur l'ensemble (actives + clôturées)
            var existingCount = Directory.GetFiles(dir, "*_evaluation_*.md").Length;
            var stamp         = DateTime.Now.ToString("yyyy-MM-dd");
            var fileName      = $"{stamp}_evaluation_{(existingCount + 1):000}.md";
            var path          = Path.Combine(dir, fileName);

            var phase = new EvaluationPhase
            {
                PatientNomComplet = patientNomComplet,
                FilePath          = path,
                DateDebut         = DateTime.Now,
                DateDerniereModif = DateTime.Now,
                EtapeCourante     = EvaluationStep.Preparation
            };

            Save(phase);
            return phase;
        }

        /// <summary>
        /// Réécrit le fichier d'évaluation. Met à jour DateDerniereModif.
        /// </summary>
        public void Save(EvaluationPhase phase)
        {
            phase.DateDerniereModif = DateTime.Now;
            var content = SerializeToMarkdown(phase);
            File.WriteAllText(phase.FilePath, content, Encoding.UTF8);
        }

        /// <summary>
        /// Clôture l'évaluation (DateCloture = now). Le fichier devient l'objet immuable.
        /// </summary>
        public void Close(EvaluationPhase phase)
        {
            phase.DateCloture = DateTime.Now;
            Save(phase);
        }

        /// <summary>
        /// Supprime définitivement le fichier d'évaluation. Renvoie true si le fichier existait
        /// et a été supprimé, false sinon (silencieux sur erreur).
        /// </summary>
        public bool Delete(string filePath)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return false;
                File.Delete(filePath);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EvaluationPhaseService] Delete failed {filePath}: {ex.Message}");
                return false;
            }
        }

        // ── Sérialisation ────────────────────────────────────────────────────

        private string SerializeToMarkdown(EvaluationPhase phase)
        {
            var sb = new StringBuilder();
            sb.AppendLine("---");
            sb.AppendLine("type: evaluation");
            sb.AppendLine($"patient: \"{Escape(phase.PatientNomComplet)}\"");
            sb.AppendLine($"date_debut: {phase.DateDebut.ToString("o", CultureInfo.InvariantCulture)}");
            sb.AppendLine($"date_derniere_modif: {phase.DateDerniereModif.ToString("o", CultureInfo.InvariantCulture)}");
            sb.AppendLine($"date_cloture: {(phase.DateCloture.HasValue ? phase.DateCloture.Value.ToString("o", CultureInfo.InvariantCulture) : "null")}");
            sb.AppendLine($"etape_courante: {(int)phase.EtapeCourante}");
            sb.AppendLine($"etape_1_validee: {(phase.Preparation.IsValidated ? "true" : "false")}");
            if (phase.Preparation.ValidationDate.HasValue)
                sb.AppendLine($"etape_1_validation_date: {phase.Preparation.ValidationDate.Value.ToString("o", CultureInfo.InvariantCulture)}");

            sb.AppendLine("preparation:");
            AppendList(sb, "  hypotheses_principales", phase.Preparation.HypothesesPrincipales);
            AppendList(sb, "  differentiels",          phase.Preparation.Differentiels);
            AppendList(sb, "  a_eliminer",             phase.Preparation.AEliminer);
            AppendList(sb, "  points_vigilance",       phase.Preparation.PointsVigilance);
            AppendList(sb, "  questions_cliniques",    phase.Preparation.QuestionsCliniques);

            sb.AppendLine($"etape_2_validee: {(phase.EvaluationCiblee.IsValidated ? "true" : "false")}");
            if (phase.EvaluationCiblee.ValidationDate.HasValue)
                sb.AppendLine($"etape_2_validation_date: {phase.EvaluationCiblee.ValidationDate.Value.ToString("o", CultureInfo.InvariantCulture)}");

            sb.AppendLine("evaluation_ciblee:");
            AppendAxes(sb, "  axes_principaux",    phase.EvaluationCiblee.AxesPrincipaux);
            AppendAxes(sb, "  axes_differentiels", phase.EvaluationCiblee.AxesDifferentiels);
            AppendAxes(sb, "  axes_systemiques",   phase.EvaluationCiblee.AxesSystemiques);

            sb.AppendLine($"etape_3_validee: {(phase.Synthese.IsValidated ? "true" : "false")}");
            if (phase.Synthese.ValidationDate.HasValue)
                sb.AppendLine($"etape_3_validation_date: {phase.Synthese.ValidationDate.Value.ToString("o", CultureInfo.InvariantCulture)}");

            sb.AppendLine("synthese_diagnostique:");
            AppendList(sb, "  diagnostics_retenus", phase.Synthese.DiagnosticsRetenus);
            AppendList(sb, "  elements_en_faveur",  phase.Synthese.ElementsEnFaveur);
            AppendEcartes(sb, "  diagnostics_ecartes", phase.Synthese.DiagnosticsEcartes);
            sb.AppendLine($"  certitude: {(int)phase.Synthese.Certitude}");

            // Étape 4 — Cartographie de l'enfant
            sb.AppendLine($"etape_4_validee: {(phase.CartographieEnfant.IsValidated ? "true" : "false")}");
            if (phase.CartographieEnfant.ValidationDate.HasValue)
                sb.AppendLine($"etape_4_validation_date: {phase.CartographieEnfant.ValidationDate.Value.ToString("o", CultureInfo.InvariantCulture)}");
            AppendCartographieEnfant(sb, phase.CartographieEnfant);

            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine($"# Évaluation — {phase.PatientNomComplet}");
            sb.AppendLine();
            sb.AppendLine($"Démarrée le {phase.DateDebut:dd/MM/yyyy HH:mm}.");
            if (phase.DateCloture.HasValue)
                sb.AppendLine($"Clôturée le {phase.DateCloture.Value:dd/MM/yyyy HH:mm}.");
            sb.AppendLine();
            sb.AppendLine("## Étape 1 — Préparation clinique");
            sb.AppendLine();
            AppendSectionMd(sb, "Hypothèses principales", phase.Preparation.HypothesesPrincipales);
            AppendSectionMd(sb, "Diagnostics différentiels", phase.Preparation.Differentiels);
            AppendSectionMd(sb, "Diagnostics à éliminer", phase.Preparation.AEliminer);
            AppendSectionMd(sb, "Points de vigilance", phase.Preparation.PointsVigilance);
            AppendSectionMd(sb, "Questions cliniques à résoudre", phase.Preparation.QuestionsCliniques);

            // Étape 2 lisible
            if (phase.EvaluationCiblee.AxesPrincipaux.Count > 0 ||
                phase.EvaluationCiblee.AxesDifferentiels.Count > 0 ||
                phase.EvaluationCiblee.AxesSystemiques.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## Étape 2 — Évaluation ciblée");
                sb.AppendLine();
                AppendAxesMd(sb, "Axes principaux",    phase.EvaluationCiblee.AxesPrincipaux);
                AppendAxesMd(sb, "Différentiels",      phase.EvaluationCiblee.AxesDifferentiels);
                AppendAxesMd(sb, "Facteurs systémiques", phase.EvaluationCiblee.AxesSystemiques);
            }

            // Étape 3 lisible — synthèse diagnostique
            if (phase.Synthese.DiagnosticsRetenus.Count > 0 ||
                phase.Synthese.ElementsEnFaveur.Count > 0 ||
                phase.Synthese.DiagnosticsEcartes.Count > 0 ||
                phase.Synthese.Certitude != NiveauCertitude.NonRenseigne)
            {
                sb.AppendLine();
                sb.AppendLine("## Étape 3 — Synthèse diagnostique");
                sb.AppendLine();
                AppendSectionMd(sb, "Diagnostic(s) retenu(s)", phase.Synthese.DiagnosticsRetenus);
                AppendSectionMd(sb, "Éléments cliniques en faveur", phase.Synthese.ElementsEnFaveur);
                AppendEcartesMd(sb, "Diagnostics différentiels écartés", phase.Synthese.DiagnosticsEcartes);
                var certLabel = phase.Synthese.Certitude switch
                {
                    NiveauCertitude.HypotheseAConfirmer => "Hypothèse à confirmer",
                    NiveauCertitude.Probable            => "Probable",
                    NiveauCertitude.Certain             => "Certain",
                    _                                   => "Non renseigné"
                };
                sb.AppendLine($"**Niveau de certitude :** {certLabel}");
                sb.AppendLine();
            }

            // Étape 4 lisible — Cartographie de l'enfant
            if (phase.CartographieEnfant.IsValidated || HasAnyCartographieContent(phase.CartographieEnfant))
            {
                sb.AppendLine();
                sb.AppendLine("## Étape 4 — Cartographie de l'enfant");
                sb.AppendLine();
                if (phase.CartographieEnfant.AgeAuMomentDeLaSaisie.HasValue)
                    sb.AppendLine($"_Âge à la saisie : {phase.CartographieEnfant.AgeAuMomentDeLaSaisie} ans_");
                sb.AppendLine();
                AppendChenilleSegmentMd(sb, phase.CartographieEnfant.Attachement,     phase.CartographieEnfant.AgeAuMomentDeLaSaisie);
                AppendChenilleSegmentMd(sb, phase.CartographieEnfant.Psychomotricite, phase.CartographieEnfant.AgeAuMomentDeLaSaisie);
                AppendTemperamentMd(sb, phase.CartographieEnfant.Temperament);
                AppendChenilleSegmentMd(sb, phase.CartographieEnfant.Langage,         phase.CartographieEnfant.AgeAuMomentDeLaSaisie);
                AppendChenilleSegmentMd(sb, phase.CartographieEnfant.Emotions,        phase.CartographieEnfant.AgeAuMomentDeLaSaisie);
                AppendChenilleSegmentMd(sb, phase.CartographieEnfant.Imaginaire,      phase.CartographieEnfant.AgeAuMomentDeLaSaisie);
                AppendChenilleSegmentMd(sb, phase.CartographieEnfant.Pensee,          phase.CartographieEnfant.AgeAuMomentDeLaSaisie);
            }

            return sb.ToString();
        }

        // ── Cartographie de l'enfant (étape 4) ───────────────────────────────

        private static bool HasAnyCartographieContent(CartographieEnfant carto)
            => carto.Attachement.Score > 0 || carto.Psychomotricite.Score > 0
            || carto.Langage.Score > 0      || carto.Emotions.Score > 0
            || carto.Imaginaire.Score > 0   || carto.Pensee.Score > 0
            || carto.Temperament.IsRenseigne;

        private static void AppendCartographieEnfant(StringBuilder sb, CartographieEnfant carto)
        {
            sb.AppendLine("cartographie_enfant:");
            if (carto.AgeAuMomentDeLaSaisie.HasValue)
                sb.AppendLine($"  age_au_moment: {carto.AgeAuMomentDeLaSaisie.Value}");
            AppendChenilleSegmentYaml(sb, "  attachement",     carto.Attachement);
            AppendChenilleSegmentYaml(sb, "  psychomotricite", carto.Psychomotricite);
            sb.AppendLine("  temperament:");
            sb.AppendLine($"    activite: {carto.Temperament.NiveauActivite}");
            sb.AppendLine($"    regularite: {carto.Temperament.Regularite}");
            sb.AppendLine($"    reactivite_sensorielle: {carto.Temperament.ReactiviteSensorielle}");
            sb.AppendLine($"    intensite_emotionnelle: {carto.Temperament.IntensiteEmotionnelle}");
            sb.AppendLine($"    adaptabilite: {carto.Temperament.Adaptabilite}");
            sb.AppendLine($"    temps_reaction: {carto.Temperament.TempsDeReaction}");
            AppendChenilleSegmentYaml(sb, "  langage",    carto.Langage);
            AppendChenilleSegmentYaml(sb, "  emotions",   carto.Emotions);
            AppendChenilleSegmentYaml(sb, "  imaginaire", carto.Imaginaire);
            AppendChenilleSegmentYaml(sb, "  pensee",     carto.Pensee);
        }

        private static void AppendChenilleSegmentYaml(StringBuilder sb, string key, ChenilleSegment segment)
        {
            sb.AppendLine($"{key}:");
            sb.Append("    items: [");
            for (int i = 0; i < segment.Items.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(segment.Items[i].IsChecked ? "true" : "false");
            }
            sb.AppendLine("]");
        }

        private static void AppendChenilleSegmentMd(StringBuilder sb, ChenilleSegment segment, int? age)
        {
            var niveau = CartographieScoringService.Calculer(segment.Score, age);
            sb.AppendLine($"### {segment.Label}");
            sb.AppendLine();
            sb.AppendLine($"_« {segment.PhraseBoussole} »_");
            sb.AppendLine();
            var niveauTxt = niveau.HasValue
                ? $"{NiveauEmoji(niveau.Value)} **{CartographieContent.NiveauLabel(niveau)}**"
                : "_niveau non calculé_";
            sb.AppendLine($"- Score : **{segment.Score}/6** — {niveauTxt}");
            if (niveau.HasValue)
                sb.AppendLine($"- _{CartographieContent.LectureEmotionnelle(niveau)}_");
            sb.AppendLine();
        }

        private static void AppendTemperamentMd(StringBuilder sb, TemperamentProfile t)
        {
            sb.AppendLine("### Tempérament (profil)");
            sb.AppendLine();
            sb.AppendLine("_« Il n'est pas trop ou pas assez… il est lui. »_");
            sb.AppendLine();
            sb.AppendLine($"- Niveau d'activité : **{t.NiveauActivite}/5**");
            sb.AppendLine($"- Rythme / Régularité : **{t.Regularite}/5**");
            sb.AppendLine($"- Réactivité sensorielle : **{t.ReactiviteSensorielle}/5**");
            sb.AppendLine($"- Intensité émotionnelle : **{t.IntensiteEmotionnelle}/5**");
            sb.AppendLine($"- Adaptabilité : **{t.Adaptabilite}/5**");
            sb.AppendLine($"- Temps de réaction : **{t.TempsDeReaction}/5**");
            sb.AppendLine();
        }

        private static string NiveauEmoji(NiveauSegment n) => n switch
        {
            NiveauSegment.VertFonce  => "🟢",
            NiveauSegment.VertClair  => "🟢",
            NiveauSegment.JauneClair => "🟡",
            NiveauSegment.JauneFonce => "🟡",
            NiveauSegment.RougeClair => "🔴",
            NiveauSegment.RougeFonce => "🔴",
            _                        => "⚪"
        };

        private static void AppendAxes(StringBuilder sb, string key, IEnumerable<EvaluationAxis> axes)
        {
            sb.AppendLine($"{key}:");
            foreach (var a in axes)
            {
                if (a == null || string.IsNullOrWhiteSpace(a.Label)) continue;
                sb.AppendLine($"  - label: \"{Escape(a.Label)}\"");
                sb.AppendLine($"    justification: \"{Escape(a.Justification ?? "")}\"");
                sb.AppendLine($"    state: {(int)a.State}");
                if (!string.IsNullOrWhiteSpace(a.Observation))
                    sb.AppendLine($"    observation: \"{Escape(SingleLine(a.Observation))}\"");
                if (!string.IsNullOrWhiteSpace(a.PendingMedText))
                    sb.AppendLine($"    pending_med: \"{Escape(SingleLine(a.PendingMedText))}\"");
                if (a.SuggestedQuestions.Count > 0)
                {
                    sb.AppendLine("    questions:");
                    foreach (var q in a.SuggestedQuestions)
                        if (!string.IsNullOrWhiteSpace(q?.Value))
                            sb.AppendLine($"      - \"{Escape(q.Value)}\"");
                }
                if (a.ObservationsProposees.Count > 0)
                {
                    sb.AppendLine("    observations:");
                    foreach (var o in a.ObservationsProposees)
                    {
                        if (o == null || string.IsNullOrWhiteSpace(o.Label)) continue;
                        sb.AppendLine($"      - label: \"{Escape(o.Label)}\"");
                        sb.AppendLine($"        checked: {(o.IsChecked ? "true" : "false")}");
                    }
                }
            }
        }

        private static void AppendAxesMd(StringBuilder sb, string title, IEnumerable<EvaluationAxis> axes)
        {
            sb.AppendLine($"### {title}");
            sb.AppendLine();
            var any = false;
            foreach (var a in axes)
            {
                if (a == null || string.IsNullOrWhiteSpace(a.Label)) continue;
                var stateLabel = a.State switch
                {
                    AxisExplorationState.NonAborde => "🟠 Non abordé",
                    AxisExplorationState.Partiel   => "🟡 Partiel",
                    AxisExplorationState.Evoque    => "🟢 Évoqué",
                    _                              => ""
                };
                sb.AppendLine($"- **{a.Label}** — {stateLabel}");
                if (!string.IsNullOrWhiteSpace(a.Justification))
                    sb.AppendLine($"  - _{a.Justification.Trim()}_");
                var coches = a.ObservationsProposees.Where(o => o != null && o.IsChecked).Select(o => o.Label).ToList();
                if (coches.Count > 0)
                    sb.AppendLine($"  - Observations cochées : {string.Join(", ", coches)}");
                if (!string.IsNullOrWhiteSpace(a.Observation))
                    sb.AppendLine($"  - Observation : {a.Observation.Trim()}");
                any = true;
            }
            if (!any) sb.AppendLine("_(aucun axe défini)_");
            sb.AppendLine();
        }

        private static void AppendEcartes(StringBuilder sb, string key, IEnumerable<DiagnosticEcarte> ecartes)
        {
            sb.AppendLine($"{key}:");
            foreach (var e in ecartes)
            {
                if (e == null || string.IsNullOrWhiteSpace(e.Label)) continue;
                sb.AppendLine($"  - label: \"{Escape(e.Label)}\"");
                sb.AppendLine($"    motif: \"{Escape(SingleLine(e.Motif ?? ""))}\"");
            }
        }

        private static void AppendEcartesMd(StringBuilder sb, string title, IEnumerable<DiagnosticEcarte> ecartes)
        {
            sb.AppendLine($"### {title}");
            sb.AppendLine();
            var any = false;
            foreach (var e in ecartes)
            {
                if (e == null || string.IsNullOrWhiteSpace(e.Label)) continue;
                sb.Append($"- **{e.Label.Trim()}**");
                if (!string.IsNullOrWhiteSpace(e.Motif))
                    sb.Append($" — {e.Motif.Trim()}");
                sb.AppendLine();
                any = true;
            }
            if (!any) sb.AppendLine("_(aucun différentiel écarté)_");
            sb.AppendLine();
        }

        private static string SingleLine(string s) => (s ?? "").Replace("\r", " ").Replace("\n", " ");

        private static void AppendList(StringBuilder sb, string key, IEnumerable<EditableString> items)
        {
            sb.AppendLine($"{key}:");
            foreach (var it in items)
            {
                if (it == null || string.IsNullOrWhiteSpace(it.Value)) continue;
                sb.AppendLine($"  - \"{Escape(it.Value)}\"");
            }
        }

        private static void AppendSectionMd(StringBuilder sb, string title, IEnumerable<EditableString> items)
        {
            sb.AppendLine($"### {title}");
            sb.AppendLine();
            var hasAny = false;
            foreach (var it in items)
            {
                if (it == null || string.IsNullOrWhiteSpace(it.Value)) continue;
                sb.AppendLine($"- {it.Value.Trim()}");
                hasAny = true;
            }
            if (!hasAny) sb.AppendLine("_(non renseigné)_");
            sb.AppendLine();
        }

        // ── Désérialisation ──────────────────────────────────────────────────

        private EvaluationPhase? LoadFromFile(string path)
        {
            try
            {
                var raw = File.ReadAllText(path, Encoding.UTF8);
                var yaml = ExtractYamlHeader(raw);
                if (yaml == null) return null;

                var phase = new EvaluationPhase { FilePath = path };
                phase.PatientNomComplet = GetString(yaml, "patient");
                phase.DateDebut         = GetDate(yaml,   "date_debut")         ?? DateTime.MinValue;
                phase.DateDerniereModif = GetDate(yaml,   "date_derniere_modif") ?? DateTime.MinValue;
                phase.DateCloture       = GetDate(yaml,   "date_cloture");
                phase.EtapeCourante     = (EvaluationStep)(GetInt(yaml, "etape_courante") ?? 1);

                if (GetBool(yaml, "etape_1_validee"))
                    phase.Preparation.ValidationDate = GetDate(yaml, "etape_1_validation_date") ?? DateTime.Now;

                // Sous-bloc "preparation:"
                var prep = ExtractSubBlock(yaml, "preparation:");
                if (prep != null)
                {
                    foreach (var it in GetListItems(prep, "hypotheses_principales")) phase.Preparation.HypothesesPrincipales.Add(new EditableString(it));
                    foreach (var it in GetListItems(prep, "differentiels"))          phase.Preparation.Differentiels.Add(new EditableString(it));
                    foreach (var it in GetListItems(prep, "a_eliminer"))             phase.Preparation.AEliminer.Add(new EditableString(it));
                    foreach (var it in GetListItems(prep, "points_vigilance"))       phase.Preparation.PointsVigilance.Add(new EditableString(it));
                    foreach (var it in GetListItems(prep, "questions_cliniques"))    phase.Preparation.QuestionsCliniques.Add(new EditableString(it));
                }

                // Étape 2 — validation + sous-bloc evaluation_ciblee
                if (GetBool(yaml, "etape_2_validee"))
                    phase.EvaluationCiblee.ValidationDate = GetDate(yaml, "etape_2_validation_date") ?? DateTime.Now;

                var ciblee = ExtractSubBlock(yaml, "evaluation_ciblee:");
                if (ciblee != null)
                {
                    foreach (var ax in GetAxesFromBlock(ciblee, "axes_principaux",    AxisCategory.Principal))
                        phase.EvaluationCiblee.AxesPrincipaux.Add(ax);
                    foreach (var ax in GetAxesFromBlock(ciblee, "axes_differentiels", AxisCategory.Differentiel))
                        phase.EvaluationCiblee.AxesDifferentiels.Add(ax);
                    foreach (var ax in GetAxesFromBlock(ciblee, "axes_systemiques",   AxisCategory.Systemique))
                        phase.EvaluationCiblee.AxesSystemiques.Add(ax);
                }

                // Étape 3 — Synthèse diagnostique
                if (GetBool(yaml, "etape_3_validee"))
                    phase.Synthese.ValidationDate = GetDate(yaml, "etape_3_validation_date") ?? DateTime.Now;

                var synth = ExtractSubBlock(yaml, "synthese_diagnostique:");
                if (synth != null)
                {
                    foreach (var it in GetListItems(synth, "diagnostics_retenus")) phase.Synthese.DiagnosticsRetenus.Add(new EditableString(it));
                    foreach (var it in GetListItems(synth, "elements_en_faveur"))  phase.Synthese.ElementsEnFaveur.Add(new EditableString(it));
                    foreach (var ec in GetEcartesFromBlock(synth, "diagnostics_ecartes"))
                        phase.Synthese.DiagnosticsEcartes.Add(ec);
                    var cert = GetIntInBlock(synth, "certitude") ?? 0;
                    if (cert >= 0 && cert <= 3)
                        phase.Synthese.Certitude = (NiveauCertitude)cert;
                }

                // Étape 4 — Cartographie de l'enfant (chenille universelle)
                if (GetBool(yaml, "etape_4_validee"))
                    phase.CartographieEnfant.ValidationDate = GetDate(yaml, "etape_4_validation_date") ?? DateTime.Now;

                var carto = ExtractSubBlock(yaml, "cartographie_enfant:");
                if (carto != null)
                {
                    phase.CartographieEnfant.AgeAuMomentDeLaSaisie = GetIntInBlock(carto, "age_au_moment");

                    ApplyChenilleItemsFromYaml(carto, "attachement",     phase.CartographieEnfant.Attachement);
                    ApplyChenilleItemsFromYaml(carto, "psychomotricite", phase.CartographieEnfant.Psychomotricite);
                    ApplyChenilleItemsFromYaml(carto, "langage",         phase.CartographieEnfant.Langage);
                    ApplyChenilleItemsFromYaml(carto, "emotions",        phase.CartographieEnfant.Emotions);
                    ApplyChenilleItemsFromYaml(carto, "imaginaire",      phase.CartographieEnfant.Imaginaire);
                    ApplyChenilleItemsFromYaml(carto, "pensee",          phase.CartographieEnfant.Pensee);

                    var temperament = ExtractNamedSubBlock(carto, "temperament:");
                    if (temperament != null)
                    {
                        phase.CartographieEnfant.Temperament.NiveauActivite        = GetIntInBlock(temperament, "activite")               ?? 0;
                        phase.CartographieEnfant.Temperament.Regularite            = GetIntInBlock(temperament, "regularite")             ?? 0;
                        phase.CartographieEnfant.Temperament.ReactiviteSensorielle = GetIntInBlock(temperament, "reactivite_sensorielle") ?? 0;
                        phase.CartographieEnfant.Temperament.IntensiteEmotionnelle = GetIntInBlock(temperament, "intensite_emotionnelle") ?? 0;
                        phase.CartographieEnfant.Temperament.Adaptabilite          = GetIntInBlock(temperament, "adaptabilite")           ?? 0;
                        phase.CartographieEnfant.Temperament.TempsDeReaction       = GetIntInBlock(temperament, "temps_reaction")         ?? 0;
                    }
                }

                return phase;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EvaluationPhaseService] Erreur load {path}: {ex.Message}");
                return null;
            }
        }

        // ── Parsers YAML minimalistes ────────────────────────────────────────

        private static string? ExtractYamlHeader(string raw)
        {
            if (!raw.TrimStart().StartsWith("---")) return null;
            var first = raw.IndexOf("---", StringComparison.Ordinal);
            var second = raw.IndexOf("---", first + 3, StringComparison.Ordinal);
            if (second < 0) return null;
            return raw.Substring(first + 3, second - first - 3);
        }

        private static string GetString(string yaml, string key)
        {
            var lines = yaml.Replace("\r\n", "\n").Split('\n');
            foreach (var line in lines)
            {
                var trimmed = line.TrimStart();
                if (!trimmed.StartsWith(key + ":")) continue;
                var val = trimmed.Substring(key.Length + 1).Trim();
                if (val.StartsWith("\"") && val.EndsWith("\"") && val.Length >= 2)
                    val = val.Substring(1, val.Length - 2);
                return val.Replace("\\\"", "\"").Replace("\\\\", "\\");
            }
            return "";
        }

        private static int? GetInt(string yaml, string key)
        {
            var s = GetString(yaml, key);
            return int.TryParse(s, out var n) ? n : (int?)null;
        }

        private static bool GetBool(string yaml, string key)
        {
            return GetString(yaml, key).Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        private static DateTime? GetDate(string yaml, string key)
        {
            var s = GetString(yaml, key);
            if (string.IsNullOrEmpty(s) || s == "null") return null;
            return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d) ? d : (DateTime?)null;
        }

        private static string? ExtractSubBlock(string yaml, string sectionPrefix)
        {
            var lines = yaml.Replace("\r\n", "\n").Split('\n').ToList();
            int start = lines.FindIndex(l => l.TrimEnd() == sectionPrefix);
            if (start < 0) return null;
            var sb = new StringBuilder();
            for (int i = start + 1; i < lines.Count; i++)
            {
                if (lines[i].Length > 0 && !char.IsWhiteSpace(lines[i][0])) break;
                sb.AppendLine(lines[i]);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Extrait un sous-bloc nommé (ex: "temperament:") à n'importe quelle indentation.
        /// Collecte les lignes qui suivent avec une indentation strictement supérieure.
        /// </summary>
        private static string? ExtractNamedSubBlock(string yaml, string sectionPrefix)
        {
            var lines = yaml.Replace("\r\n", "\n").Split('\n').ToList();
            int start = -1;
            int baseIndent = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                var t = lines[i].TrimStart();
                if (t.TrimEnd() == sectionPrefix)
                {
                    start = i;
                    baseIndent = lines[i].Length - t.Length;
                    break;
                }
            }
            if (start < 0) return null;

            var sb = new StringBuilder();
            for (int i = start + 1; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line.Length == 0) { sb.AppendLine(line); continue; }
                var indent = line.Length - line.TrimStart().Length;
                if (indent <= baseIndent) break;
                sb.AppendLine(line);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Applique les booléens de "items: [true, false, ...]" trouvés dans un sous-bloc
        /// nommé (ex: "attachement:") aux items du segment correspondant.
        /// Tolérant : si la clé ou la ligne items est absente, ne fait rien.
        /// </summary>
        private static void ApplyChenilleItemsFromYaml(string yaml, string segmentKey, MedCompanion.Models.Evaluations.ChenilleSegment segment)
        {
            var block = ExtractNamedSubBlock(yaml, segmentKey + ":");
            if (block == null) return;

            // Chercher la ligne items: [true, false, ...]
            var lines = block.Replace("\r\n", "\n").Split('\n');
            foreach (var line in lines)
            {
                var t = line.TrimStart();
                if (!t.StartsWith("items:")) continue;
                var rest = t.Substring("items:".Length).Trim();
                if (!rest.StartsWith("[") || !rest.EndsWith("]")) continue;
                var inner = rest.Substring(1, rest.Length - 2);
                var values = inner.Split(',');
                for (int i = 0; i < values.Length && i < segment.Items.Count; i++)
                {
                    segment.Items[i].IsChecked = values[i].Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
                }
                break;
            }
        }

        private static List<string> GetListItems(string block, string key)
        {
            var res = new List<string>();
            var lines = block.Replace("\r\n", "\n").Split('\n');
            bool inside = false;
            foreach (var line in lines)
            {
                var t = line.TrimStart();
                if (t.StartsWith(key + ":"))
                {
                    inside = true;
                    continue;
                }
                if (!inside) continue;

                // Sortie de la liste : nouvelle clé au même niveau
                var indent = line.Length - line.TrimStart().Length;
                if (t.Length > 0 && !t.StartsWith("-") && line.Length > 0 && indent <= 2)
                    break;

                if (t.StartsWith("- "))
                {
                    var val = t.Substring(2).Trim();
                    if (val.StartsWith("\"") && val.EndsWith("\"") && val.Length >= 2)
                        val = val.Substring(1, val.Length - 2);
                    val = val.Replace("\\\"", "\"").Replace("\\\\", "\\");
                    res.Add(val);
                }
            }
            return res;
        }

        private static string Escape(string s)
            => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");

        /// <summary>
        /// Parse une liste d'axes au format :
        ///   axes_principaux:
        ///     - label: "..."
        ///       justification: "..."
        ///       state: 2
        ///       observation: "..."
        ///       questions:
        ///         - "..."
        /// Reconnaît un nouvel axe à chaque ligne ""- label: ..."".
        /// </summary>
        private static List<EvaluationAxis> GetAxesFromBlock(string block, string key, AxisCategory cat)
        {
            var axes = new List<EvaluationAxis>();
            var lines = block.Replace("\r\n", "\n").Split('\n');
            bool inKey = false;
            EvaluationAxis? current = null;
            string section = "";  // "questions" | "observations" | ""
            AxisObservationItem? currentObs = null;

            foreach (var rawLine in lines)
            {
                var t = rawLine.TrimStart();

                if (!inKey)
                {
                    if (t.StartsWith(key + ":")) { inKey = true; }
                    continue;
                }

                // Sortie de la section : nouvelle clé au même niveau
                var indent = rawLine.Length - t.Length;
                if (t.Length > 0 && indent <= 2 && !t.StartsWith("-"))
                    break;

                // Nouvel axe
                if (t.StartsWith("- label:") && indent <= 4)
                {
                    if (current != null) axes.Add(current);
                    current = new EvaluationAxis { Category = cat };
                    current.Label = ExtractQuoted(t.Substring("- label:".Length).Trim());
                    section = ""; currentObs = null;
                    continue;
                }

                if (current == null) continue;

                if (t.StartsWith("justification:"))
                {
                    current.Justification = ExtractQuoted(t.Substring("justification:".Length).Trim());
                    section = ""; currentObs = null;
                }
                else if (t.StartsWith("state:"))
                {
                    if (int.TryParse(t.Substring("state:".Length).Trim(), out var n))
                    {
                        // Compatibilité ancien schéma 4 états : 3 (Consolide) → 2 (Evoque)
                        if (n == 3) n = 2;
                        current.State = (AxisExplorationState)Math.Clamp(n, 0, 2);
                    }
                    section = ""; currentObs = null;
                }
                else if (t.StartsWith("observation:"))
                {
                    current.Observation = ExtractQuoted(t.Substring("observation:".Length).Trim());
                    section = ""; currentObs = null;
                }
                else if (t.StartsWith("pending_med:"))
                {
                    current.PendingMedText = ExtractQuoted(t.Substring("pending_med:".Length).Trim());
                    section = ""; currentObs = null;
                }
                else if (t.StartsWith("questions:"))
                {
                    section = "questions"; currentObs = null;
                }
                else if (t.StartsWith("observations:"))
                {
                    section = "observations"; currentObs = null;
                }
                else if (section == "questions" && t.StartsWith("- "))
                {
                    var v = ExtractQuoted(t.Substring(2).Trim());
                    if (!string.IsNullOrEmpty(v)) current.SuggestedQuestions.Add(new EditableString(v));
                }
                else if (section == "observations" && t.StartsWith("- label:"))
                {
                    currentObs = new AxisObservationItem
                    {
                        Label = ExtractQuoted(t.Substring("- label:".Length).Trim())
                    };
                    current.ObservationsProposees.Add(currentObs);
                }
                else if (section == "observations" && currentObs != null && t.StartsWith("checked:"))
                {
                    currentObs.IsChecked = t.Substring("checked:".Length).Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
                }
            }

            if (current != null) axes.Add(current);
            return axes;
        }

        private static string ExtractQuoted(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.StartsWith("\"") && s.EndsWith("\"") && s.Length >= 2)
                s = s.Substring(1, s.Length - 2);
            return s.Replace("\\\"", "\"").Replace("\\\\", "\\");
        }

        /// <summary>
        /// Parse une liste de différentiels écartés au format :
        ///   diagnostics_ecartes:
        ///     - label: "..."
        ///       motif: "..."
        /// </summary>
        private static List<DiagnosticEcarte> GetEcartesFromBlock(string block, string key)
        {
            var res = new List<DiagnosticEcarte>();
            var lines = block.Replace("\r\n", "\n").Split('\n');
            bool inKey = false;
            DiagnosticEcarte? current = null;

            foreach (var rawLine in lines)
            {
                var t = rawLine.TrimStart();

                if (!inKey)
                {
                    if (t.StartsWith(key + ":")) { inKey = true; }
                    continue;
                }

                var indent = rawLine.Length - t.Length;
                if (t.Length > 0 && indent <= 2 && !t.StartsWith("-"))
                    break;

                if (t.StartsWith("- label:") && indent <= 4)
                {
                    if (current != null) res.Add(current);
                    current = new DiagnosticEcarte
                    {
                        Label = ExtractQuoted(t.Substring("- label:".Length).Trim())
                    };
                    continue;
                }

                if (current != null && t.StartsWith("motif:"))
                {
                    current.Motif = ExtractQuoted(t.Substring("motif:".Length).Trim());
                }
            }

            if (current != null) res.Add(current);
            return res;
        }

        /// <summary>
        /// Lit une clé simple "key: N" dans un sous-bloc indenté (≤ 4 espaces).
        /// </summary>
        private static int? GetIntInBlock(string block, string key)
        {
            var lines = block.Replace("\r\n", "\n").Split('\n');
            foreach (var line in lines)
            {
                var t = line.TrimStart();
                if (!t.StartsWith(key + ":")) continue;
                var val = t.Substring(key.Length + 1).Trim();
                if (int.TryParse(val, out var n)) return n;
            }
            return null;
        }
    }
}
