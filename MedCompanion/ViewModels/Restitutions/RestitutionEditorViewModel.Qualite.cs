using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using MedCompanion.Commands;
using MedCompanion.Services.Restitutions;

namespace MedCompanion.ViewModels.Restitutions
{
    /// <summary>
    /// Gravité d'un constat. Elle dit ce qu'il faut faire, pas à quel point c'est grave :
    /// un <see cref="Bloquant"/> fait perdre du contenu au PDF, un <see cref="Vigilance"/>
    /// tiendra aujourd'hui mais cassera à la prochaine retouche.
    /// </summary>
    public enum GraviteConstat
    {
        Conforme,
        Vigilance,
        Bloquant
    }

    /// <summary>
    /// Un constat du service qualité. Immuable : un constat décrit ce qui a été observé à un
    /// instant, il ne se met pas à jour tout seul. Une nouvelle passe produit de nouveaux
    /// constats.
    /// </summary>
    public class ConstatQualite
    {
        /// <summary>1 = mise en page, 2 = contradictions, 3 = linguistique.</summary>
        public int Phase { get; init; }

        public GraviteConstat Gravite { get; init; }

        /// <summary>Où : « Page 15 — Synthèse globale », ou le titre du bloc concerné.</summary>
        public string Ou { get; init; } = "";

        public string Message { get; init; } = "";

        /// <summary>
        /// Moteur qui a produit le constat — vide pour la phase 1, qui ne fait que mesurer.
        /// Les phases 2 et 3 le renseignent : sans lui, comparer Qwen et Gemma ne veut rien dire.
        /// </summary>
        public string Moteur { get; init; } = "";

        public string Pastille => Gravite switch
        {
            GraviteConstat.Bloquant  => "■",
            GraviteConstat.Vigilance => "▲",
            _                        => "●"
        };

        public string Couleur => Gravite switch
        {
            GraviteConstat.Bloquant  => "#C0392B",
            GraviteConstat.Vigilance => "#D68910",
            _                        => "#16A085"
        };

        public string EtiquetteMoteur => string.IsNullOrWhiteSpace(Moteur) ? "" : $"· {Moteur}";
    }

    public partial class RestitutionEditorViewModel
    {
        // ── Le panneau qualité prend la place de l'éditeur ───────────────────
        //
        // Il ne s'ouvre pas tout seul : le médecin clique quand il juge le dossier fini.
        // L'aperçu reste à droite pendant tout le contrôle — on regarde la page dont on parle.

        private bool _modeQualite;
        public bool ModeQualite
        {
            get => _modeQualite;
            private set { if (_modeQualite == value) return; _modeQualite = value; OnPropertyChanged(); }
        }

        public ObservableCollection<ConstatQualite> Constats { get; } = new();

        // ── Choix du moteur, pour les phases 2 et 3 ──────────────────────────

        public IReadOnlyList<string> MoteursQualite { get; } = new[] { "Qwen", "Gemma" };

        private string? _moteurQualite;
        public string MoteurQualite
        {
            get => _moteurQualite ??= LireMoteurRetenu();
            set
            {
                var v = string.IsNullOrWhiteSpace(value) ? "Qwen" : value.Trim();
                if (_moteurQualite == v) return;
                _moteurQualite = v;
                EnregistrerMoteurRetenu(v);
                OnPropertyChanged();
            }
        }

        private static string LireMoteurRetenu()
        {
            try
            {
                var brut = (AppSettings.Load().QualiteMoteur ?? "").Trim();
                return brut.StartsWith("g", StringComparison.OrdinalIgnoreCase) ? "Gemma" : "Qwen";
            }
            catch { return "Qwen"; }
        }

        private static void EnregistrerMoteurRetenu(string moteur)
        {
            try
            {
                var s = AppSettings.Load();
                s.QualiteMoteur = moteur.ToLowerInvariant();
                s.Save();
            }
            catch { /* un réglage non retenu ne doit pas bloquer le contrôle */ }
        }

        // ── Mesure des pages : elle vient de l'aperçu, pas d'une estimation ──
        //
        // Posée par le code-behind de la vue, qui seul tient le WebView2. C'est le même
        // moteur que celui de l'export PDF : ce qu'il mesure est ce qui sera imprimé. Une
        // estimation en C# ne peut pas jouer ce rôle — la journée du 17/09 l'a montré deux
        // fois, l'estimation se trompait dans les deux sens.
        public Func<Task<string?>>? MesurerPagesDansApercu { get; set; }

        private bool _phase1EnCours;
        public bool Phase1EnCours
        {
            get => _phase1EnCours;
            private set
            {
                if (_phase1EnCours == value) return;
                _phase1EnCours = value;
                OnPropertyChanged();
                (LancerPhase1Command as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        private string _phase1Resume = "";
        public string Phase1Resume
        {
            get => _phase1Resume;
            private set { if (_phase1Resume == value) return; _phase1Resume = value; OnPropertyChanged(); }
        }

        public ICommand OuvrirQualiteCommand  => _ouvrirQualite  ??= new RelayCommand(_ => ModeQualite = true);
        public ICommand FermerQualiteCommand  => _fermerQualite  ??= new RelayCommand(_ => ModeQualite = false);
        public ICommand LancerPhase1Command   => _lancerPhase1   ??= new RelayCommand(
            async _ => await LancerPhase1Async(), _ => !Phase1EnCours);

        private ICommand? _ouvrirQualite;
        private ICommand? _fermerQualite;
        private ICommand? _lancerPhase1;

        /// <summary>
        /// Phase 1 — mise en page. Mesure la hauteur réelle de chaque page dans l'aperçu et
        /// signale celles qui dépasseront l'A4 à l'impression.
        ///
        /// POURQUOI CE CONTRÔLE EXISTE. À l'écran, une page est en <c>min-height: 297mm</c> :
        /// elle s'allonge et tout reste lisible. À l'impression elle passe en
        /// <c>height: 297mm; overflow: hidden</c> — Edge imprime 297 mm et coupe le reste, sans
        /// trait ni avertissement. L'aperçu montre donc du contenu que le PDF supprime, et rien
        /// ne prévient le médecin. Cette phase est le seul endroit où ça se voit.
        /// </summary>
        public async Task LancerPhase1Async()
        {
            if (MesurerPagesDansApercu == null)
            {
                RemplacerConstats(1, new[]
                {
                    new ConstatQualite
                    {
                        Phase   = 1,
                        Gravite = GraviteConstat.Vigilance,
                        Ou      = "Aperçu",
                        Message = "L'aperçu n'est pas prêt — impossible de mesurer les pages. Rouvrez le dossier."
                    }
                });
                return;
            }

            Phase1EnCours = true;
            Phase1Resume  = "Mesure des pages dans l'aperçu…";

            try
            {
                var json = await MesurerPagesDansApercu();
                var mesures = LireMesures(json);

                if (mesures.Count == 0)
                {
                    Phase1Resume = "Aucune page mesurée.";
                    RemplacerConstats(1, new[]
                    {
                        new ConstatQualite
                        {
                            Phase   = 1,
                            Gravite = GraviteConstat.Vigilance,
                            Ou      = "Aperçu",
                            Message = "La mesure n'a rien renvoyé. Laissez l'aperçu finir de s'afficher, puis relancez."
                        }
                    });
                    return;
                }

                RemplacerConstats(1, ConstruireConstatsMiseEnPage(mesures));

                var debordent = mesures.Count(m => m.Depassement > SeuilDepassementPx);
                var auBord    = mesures.Count(m => m.Depassement <= SeuilDepassementPx
                                                && !m.Fixe
                                                && m.Contenu > HauteurA4Px * 0.92);

                Phase1Resume = debordent > 0
                    ? $"{mesures.Count} pages — {debordent} coupée(s) à l'impression."
                    : auBord > 0
                        ? $"{mesures.Count} pages — aucune coupée, {auBord} à surveiller."
                        : $"{mesures.Count} pages — toutes tiennent sur une A4.";
            }
            catch (Exception ex)
            {
                Phase1Resume = "La mesure a échoué.";
                RemplacerConstats(1, new[]
                {
                    new ConstatQualite
                    {
                        Phase   = 1,
                        Gravite = GraviteConstat.Vigilance,
                        Ou      = "Aperçu",
                        Message = $"Mesure impossible : {ex.Message}"
                    }
                });
            }
            finally
            {
                Phase1EnCours = false;
            }
        }

        /// <summary>Hauteur utile d'une A4 à 96 dpi.</summary>
        private const double HauteurA4Px = 297.0 / 25.4 * 96.0;

        /// <summary>
        /// Seuil de signalement, partagé avec le repaginateur du navigateur — voir
        /// <see cref="RestitutionHtmlPreviewService.ToleranceArrondiPx"/>. Les deux DOIVENT
        /// rester égaux : signaler ce que la repagination refuse de corriger apprend au
        /// médecin à ignorer le rapport.
        /// </summary>
        private const double SeuilDepassementPx = RestitutionHtmlPreviewService.ToleranceArrondiPx;

        /// <param name="Fixe">
        /// Page dont le contenu ne dépend pas de l'enfant — la couverture (gabarit à champs) et
        /// l'annexe méthodologique (ressource figée). Sa marge n'est pas surveillée : la
        /// signaler « à 95 % » sur chaque dossier apprendrait à ignorer le rapport. Un
        /// débordement y reste signalé — ce serait un défaut du gabarit, à corriger une fois
        /// pour tous les patients.
        /// </param>
        private sealed record MesurePage(int Index, double Contenu, string Titre, bool Fixe)
        {
            public double Depassement => Contenu - HauteurA4Px;
            public double Remplissage => Contenu / HauteurA4Px * 100.0;
        }

        private static List<MesurePage> LireMesures(string? json)
        {
            var liste = new List<MesurePage>();
            if (string.IsNullOrWhiteSpace(json)) return liste;

            try
            {
                // ExecuteScriptAsync rend du JSON — et une chaîne JSON encodée quand le script
                // retourne une chaîne. On déballe si besoin.
                var brut = json.Trim();
                if (brut.StartsWith("\"", StringComparison.Ordinal))
                    brut = JsonSerializer.Deserialize<string>(brut) ?? "";

                using var doc = JsonDocument.Parse(brut);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return liste;

                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    if (el.ValueKind != JsonValueKind.Object) continue;
                    var index   = el.TryGetProperty("i", out var vi) && vi.TryGetInt32(out var i) ? i : 0;
                    var contenu = el.TryGetProperty("h", out var vh) && vh.TryGetDouble(out var h) ? h : 0;
                    var titre   = el.TryGetProperty("t", out var vt) ? (vt.GetString() ?? "") : "";
                    var fixe    = el.TryGetProperty("x", out var vx) && vx.TryGetInt32(out var x) && x == 1;
                    if (contenu > 0) liste.Add(new MesurePage(index, contenu, titre, fixe));
                }
            }
            catch { /* mesure illisible : la phase le dira */ }

            return liste;
        }

        /// <summary>
        /// Traduit les mesures en constats.
        ///
        /// CE QUE MESURE CETTE PHASE. Le document a DÉJÀ été repaginé par le script du
        /// navigateur : les cartes sont réparties sur autant d'A4 que nécessaire. Ce qui est
        /// signalé ici est donc ce que la repagination n'a PAS pu corriger — en pratique, un
        /// bloc unique trop haut pour tenir sur une page, où qu'on le mette. La seule issue
        /// est alors de raccourcir le texte.
        ///
        /// Le rapport dit aussi ce qu'il a vérifié quand tout va bien : un contrôle muet est
        /// indiscernable d'un contrôle qui n'a pas tourné.
        /// </summary>
        private static List<ConstatQualite> ConstruireConstatsMiseEnPage(List<MesurePage> mesures)
        {
            var constats = new List<ConstatQualite>();

            foreach (var m in mesures.Where(m => m.Depassement > SeuilDepassementPx)
                                     .OrderByDescending(m => m.Depassement))
            {
                var mm = m.Depassement * 25.4 / 96.0;
                constats.Add(new ConstatQualite
                {
                    Phase   = 1,
                    Gravite = GraviteConstat.Bloquant,
                    Ou      = Situer(m),
                    Message = $"Dépasse l'A4 de {mm.ToString("F0", CultureInfo.InvariantCulture)} mm "
                            + "APRÈS repagination — un bloc unique y est trop haut pour tenir sur "
                            + "une page, le découper davantage ne servirait à rien. "
                            + "Raccourcissez son texte : ce qui dépasse sera coupé du PDF."
                });
            }

            // Les pages denses ne font PLUS l'objet d'un constat par page. Avant la
            // repagination, « remplie à 99 % » annonçait une coupure à venir ; maintenant, une
            // page qui bascule est découpée automatiquement. Le signaler quatre fois par
            // dossier n'appelle aucune action — et un rapport qui énumère l'inactionnable
            // apprend à être ignoré. Une ligne récapitulative suffit.
            var denses = mesures.Where(m => m.Depassement <= SeuilDepassementPx
                                         && !m.Fixe
                                         && m.Contenu > HauteurA4Px * 0.92)
                                .ToList();

            var fixes   = mesures.Count(m => m.Fixe);
            var suivies = mesures.Count - fixes;

            var noteDenses = denses.Count == 0 ? ""
                : denses.Count == 1
                  ? " Une page est remplie à plus de 92 % : si son texte s'allonge, une page sera ajoutée automatiquement."
                  : $" {denses.Count} pages sont remplies à plus de 92 % : si leur texte s'allonge, "
                  + "des pages seront ajoutées automatiquement.";

            var noteFixes = fixes == 0 ? ""
                : fixes == 1
                  ? " La page générique (couverture ou annexe méthodologique) n'est pas surveillée : "
                  + "son contenu ne bouge pas d'un dossier à l'autre."
                  : $" Les {fixes} pages génériques (couverture, annexe méthodologique) ne sont pas "
                  + "surveillées : leur contenu ne bouge pas d'un dossier à l'autre.";

            if (constats.Count == 0)
                constats.Add(new ConstatQualite
                {
                    Phase   = 1,
                    Gravite = GraviteConstat.Conforme,
                    Ou      = $"{mesures.Count} pages",
                    Message = (suivies == 1
                                ? "La seule page qui varie selon l'enfant tient sur une A4"
                                : $"Les {suivies} pages qui varient selon l'enfant tiennent sur une A4")
                            + " — rien ne sera coupé à l'impression."
                            + noteDenses + noteFixes
                });
            else if (denses.Count > 0)
                constats.Add(new ConstatQualite
                {
                    Phase   = 1,
                    Gravite = GraviteConstat.Conforme,
                    Ou      = "Pages denses",
                    Message = noteDenses.TrimStart()
                });

            return constats;
        }

        private static string Situer(MesurePage m) =>
            string.IsNullOrWhiteSpace(m.Titre) ? $"Page {m.Index}" : $"Page {m.Index} — {m.Titre}";

        /// <summary>Remplace les constats d'une phase, sans toucher à ceux des autres.</summary>
        private void RemplacerConstats(int phase, IEnumerable<ConstatQualite> nouveaux)
        {
            for (int i = Constats.Count - 1; i >= 0; i--)
                if (Constats[i].Phase == phase) Constats.RemoveAt(i);

            foreach (var c in nouveaux) Constats.Add(c);
        }
    }
}
