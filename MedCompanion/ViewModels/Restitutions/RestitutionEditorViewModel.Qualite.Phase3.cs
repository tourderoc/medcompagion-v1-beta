using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using MedCompanion.Commands;

namespace MedCompanion.ViewModels.Restitutions
{
    /// <summary>
    /// Phase 3 du service qualité — la couche linguistique.
    ///
    /// CE QU'ELLE FAIT : rendre le texte plus français. Les tournures creuses (« le père est en
    /// couple avec la mère »), les phrases mal construites, les répétitions d'un bloc à l'autre.
    /// Rien d'autre.
    ///
    /// CE QU'ELLE NE FAIT PAS, ET NE FERA PAS. Elle ne vérifie aucun fait, ne relit pas le dossier
    /// patient, ne juge pas le contenu clinique. Périmètre fixé le 17/09/2026 : « son rôle est de
    /// voir s'il n'y a pas de contradiction dans le projet, des redondances, et enfin une couche
    /// linguiste pour rendre plus français ; la vérification des infos, c'est moi qui la fais ».
    ///
    /// ELLE PROPOSE, ELLE N'APPLIQUE JAMAIS. Chaque réécriture attend un clic. Un texte signé par un
    /// médecin et remis à des parents ne se fait pas corriger dans son dos.
    ///
    /// ET ELLE NE TOUCHE QUE DU TEXTE LIBRE. Voir <see cref="TexteLibreDuDossier"/> : les valeurs
    /// choisies dans une liste fermée (porteur, échéance, degré, statut) sont écartées avant même
    /// d'être lues. Reformuler « les parents » en « la famille » casserait les pastilles, l'annexe
    /// contacts et le tri de la feuille de route, sans que rien ne le signale.
    /// </summary>
    public partial class RestitutionEditorViewModel
    {
        private bool _phase3EnCours;
        public bool Phase3EnCours
        {
            get => _phase3EnCours;
            private set
            {
                if (_phase3EnCours == value) return;
                _phase3EnCours = value;
                OnPropertyChanged();
                (LancerPhase3Command as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        private string _phase3Resume = "";
        public string Phase3Resume
        {
            get => _phase3Resume;
            private set { if (_phase3Resume == value) return; _phase3Resume = value; OnPropertyChanged(); }
        }

        private ICommand? _lancerPhase3;
        public ICommand LancerPhase3Command => _lancerPhase3 ??= new RelayCommand(
            async _ => await LancerPhase3Async(), _ => !Phase3EnCours);

        private ICommand? _appliquerProposition;

        /// <summary>
        /// Pose la réécriture dans le dossier. Seul chemin par lequel la phase 3 modifie quoi que
        /// ce soit — et il part toujours d'un clic.
        /// </summary>
        public ICommand AppliquerPropositionCommand => _appliquerProposition ??= new RelayCommand(
            p => { if (p is ConstatQualite c) AppliquerProposition(c); },
            p => p is ConstatQualite c && c.ProposableEncore);

        // ── Couche déterministe : les tournures qu'on sait nommer ────────────

        /// <summary>
        /// Tournures repérables sans modèle, avec ce qu'il faut écrire à la place.
        ///
        /// CELLE QUI A OUVERT LE CHANTIER : « le père est en couple avec la mère ». Elle dit la
        /// situation à l'envers — on ne décrit pas un couple en rattachant un parent à l'autre — et
        /// le Dr Lassoued l'a signalée comme la seule vraie faute de langue qu'il retrouvait, surtout
        /// sur Gemma. Le prompt a été corrigé le 17/09 ; ceci rattrape les dossiers d'avant et les
        /// fois où le modèle recommence.
        ///
        /// Une entrée n'a sa place ici que si la correction est MÉCANIQUE. Tout ce qui demande de
        /// comprendre la phrase revient au modèle, plus bas.
        /// </summary>
        internal static readonly (string Motif, string Quoi, string Conseil)[] TournuresConnues =
        {
            ("est en couple avec",
             "Couple décrit par rattachement d'un parent à l'autre",
             "Écrivez « les parents vivent ensemble » ou « les parents forment un couple » : la situation se dit des deux, pas de l'un par rapport à l'autre."),

            ("il est à noter que",
             "Formule d'annonce vide",
             "Supprimez l'annonce et donnez le fait directement."),

            ("force est de constater",
             "Formule d'annonce vide",
             "Supprimez l'annonce et donnez le fait directement."),

            ("en termes de",
             "Tournure administrative",
             "Remplacez par « pour », « sur le plan de » ou une préposition simple."),

            ("au niveau du",
             "Tournure passe-partout",
             "Nommez ce dont il s'agit : « sur le plan du langage » plutôt que « au niveau du langage »."),

            ("au niveau de la",
             "Tournure passe-partout",
             "Nommez ce dont il s'agit plutôt que d'employer « au niveau de »."),

            ("de par ",
             "Locution fautive",
             "Écrivez « du fait de » ou « en raison de »."),

            ("afin de pouvoir",
             "Redondance",
             "« afin de » suffit."),

            ("comme dit précédemment",
             "Renvoi inutile dans un document court",
             "Supprimez le renvoi : le lecteur vient de le lire."),
        };

        /// <summary>
        /// Phase 3. La couche déterministe tourne d'abord et sans modèle ; la couche modèle ne part
        /// que si un moteur est disponible, et ses constats portent son nom.
        /// </summary>
        public async Task LancerPhase3Async(CancellationToken ct = default)
        {
            Phase3EnCours = true;
            Phase3Resume  = "Relecture du texte…";

            try
            {
                var segments = TexteLibreDuDossier.ARelire(_dossier);
                if (segments.Count == 0)
                {
                    RemplacerConstats(3, Array.Empty<ConstatQualite>());
                    Phase3Resume = "Aucun texte rédigé à relire.";
                    return;
                }

                var constats = new List<ConstatQualite>();
                constats.AddRange(ConstatsTournures(segments));
                constats.AddRange(ConstatsRepetitions(segments));

                var avantModele = constats.Count;
                var noteModele  = "";

                try
                {
                    var duModele = await ReecrituresDuModeleAsync(segments, ct);
                    constats.AddRange(duModele);
                    noteModele = $", {duModele.Count} proposition(s) de {MoteurQualite}";
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    // La couche déterministe a déjà produit son résultat : on le garde et on dit
                    // franchement que l'autre n'a pas tourné. Un contrôle à moitié fait qui se
                    // présente comme complet est pire qu'un contrôle qui avoue.
                    noteModele = $" — la relecture par {MoteurQualite} n'a pas abouti ({ex.Message})";
                }

                RemplacerConstats(3, constats);

                Phase3Resume = constats.Count == 0
                    ? $"{segments.Count} passages relus — rien à signaler{noteModele}."
                    : $"{segments.Count} passages relus — {avantModele} tournure(s) repérée(s){noteModele}.";
            }
            catch (OperationCanceledException)
            {
                Phase3Resume = "Relecture interrompue.";
            }
            finally
            {
                Phase3EnCours = false;
            }
        }

        private static IEnumerable<ConstatQualite> ConstatsTournures(List<SegmentTexte> segments)
        {
            foreach (var s in segments)
            {
                foreach (var (motif, quoi, conseil) in TournuresConnues)
                {
                    var passage = PremierPassage(s.Texte, motif, 46);
                    if (passage == null) continue;

                    yield return new ConstatQualite
                    {
                        Phase    = 3,
                        Gravite  = GraviteConstat.Vigilance,
                        Ou       = s.Ou,
                        BlocKey  = s.BlocKey,
                        Chemin   = s.Chemin,
                        Message  = $"{quoi} : « …{passage.Value.Extrait}… ». {conseil}"
                        // Pas de Proposition : réécrire la phrase entière demande de la comprendre.
                        // Le conseil dit quoi faire, le médecin écrit — c'est plus honnête qu'un
                        // remplacement mécanique qui produirait du français approximatif.
                    };
                }
            }
        }

        /// <summary>
        /// Répétitions d'un bloc à l'autre : une même phrase recopiée dans deux sections. Dans un
        /// même bloc, une reprise est souvent voulue (une liste reprend son intitulé) ; d'un bloc à
        /// l'autre, c'est le signe que deux sections disent la même chose.
        /// </summary>
        private static IEnumerable<ConstatQualite> ConstatsRepetitions(List<SegmentTexte> segments)
        {
            var vues = new Dictionary<string, SegmentTexte>(StringComparer.OrdinalIgnoreCase);
            var deja = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var s in segments)
            {
                foreach (var phrase in Phrases(s.Texte))
                {
                    var cle = Normaliser(phrase);
                    if (cle.Length < 60) continue;   // trop court pour qu'une reprise soit fortuite

                    if (!vues.TryGetValue(cle, out var premier))
                    {
                        vues[cle] = s;
                        continue;
                    }

                    if (premier.BlocKey == s.BlocKey) continue;   // reprise interne : souvent voulue
                    if (!deja.Add(cle)) continue;                 // déjà signalée une fois

                    var extrait = phrase.Length > 110 ? phrase[..110] + "…" : phrase;
                    yield return new ConstatQualite
                    {
                        Phase   = 3,
                        Gravite = GraviteConstat.Vigilance,
                        Ou      = s.Ou,
                        BlocKey = s.BlocKey,
                        Chemin  = s.Chemin,
                        Message = $"Phrase déjà présente dans « {premier.BlocTitre} » : « {extrait} ». "
                                + "Gardez-la là où elle porte le plus, et reformulez ou retirez l'autre."
                    };
                }
            }
        }

        internal static IEnumerable<string> Phrases(string texte)
            => texte.Split(new[] { '.', '!', '?', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Trim())
                    .Where(p => p.Length > 0);

        /// <summary>
        /// Forme comparable d'une phrase : minuscules, sans accents, ponctuation et espaces réduits.
        /// Deux phrases qui ne diffèrent que par une virgule ou une majuscule sont la même phrase.
        /// </summary>
        internal static string Normaliser(string phrase)
        {
            var sb = new StringBuilder(phrase.Length);
            var espace = false;
            foreach (var c in phrase)
            {
                if (char.IsLetterOrDigit(c)) { sb.Append(char.ToLowerInvariant(c)); espace = false; }
                else if (!espace) { sb.Append(' '); espace = true; }
            }
            return SansAccentsPublic(sb.ToString().Trim());
        }

        private static string SansAccentsPublic(string s)
        {
            var d = s.Normalize(System.Text.NormalizationForm.FormD);
            var sb = new StringBuilder(d.Length);
            foreach (var c in d)
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                    != System.Globalization.UnicodeCategory.NonSpacingMark) sb.Append(c);
            return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
        }

        // ── Couche modèle : les phrases mal construites ──────────────────────

        /// <summary>Nombre de passages envoyés au modèle en une fois — au-delà, il survole.</summary>
        internal const int PassagesParLot = 4;

        /// <summary>
        /// Demande au moteur choisi de réécrire les phrases mal construites. Il rend un JSON, jamais
        /// du texte libre : une réponse bavarde ne serait pas replaçable dans le dossier.
        ///
        /// Le modèle ne voit QUE des passages de texte libre, découpés, sans le dossier patient. Il
        /// ne peut donc rien vérifier, et c'est voulu : ce n'est pas son travail.
        /// </summary>
        private async Task<List<ConstatQualite>> ReecrituresDuModeleAsync(
            List<SegmentTexte> segments, CancellationToken ct)
        {
            var sortie = new List<ConstatQualite>();
            if (_suggesterService == null) return sortie;

            foreach (var lot in segments.Chunk(PassagesParLot))
            {
                ct.ThrowIfCancellationRequested();

                var brut = await _suggesterService.RelireLangueAsync(
                    lot.Select(s => s.Texte).ToList(), ct);

                foreach (var (index, reecrit, motif) in LireReecritures(brut))
                {
                    if (index < 0 || index >= lot.Length) continue;

                    var s = lot[index];
                    if (string.IsNullOrWhiteSpace(reecrit)) continue;
                    if (Normaliser(reecrit) == Normaliser(s.Texte)) continue;   // rien n'a changé

                    // Dernier verrou avant d'afficher une proposition : même venue du modèle, une
                    // valeur de vocabulaire fermé ne se remplace pas.
                    if (TexteLibreDuDossier.ValeursFermees.Contains(reecrit.Trim(),
                            StringComparer.OrdinalIgnoreCase)) continue;

                    sortie.Add(new ConstatQualite
                    {
                        Phase       = 3,
                        Gravite     = GraviteConstat.Vigilance,
                        Ou          = s.Ou,
                        Moteur      = MoteurQualite,
                        BlocKey     = s.BlocKey,
                        Chemin      = s.Chemin,
                        Original    = s.Texte,
                        Proposition = reecrit.Trim(),
                        Message     = string.IsNullOrWhiteSpace(motif)
                            ? "Formulation à revoir."
                            : motif.Trim()
                    });
                }
            }

            return sortie;
        }

        /// <summary>
        /// Lit la réponse du modèle. Tolérante par construction : une réponse mal formée ne doit
        /// jamais faire échouer la phase, seulement produire moins de propositions.
        /// </summary>
        internal static List<(int Index, string Reecrit, string Motif)> LireReecritures(string? brut)
        {
            var sortie = new List<(int, string, string)>();
            if (string.IsNullOrWhiteSpace(brut)) return sortie;

            var t = brut.Trim();
            if (t.StartsWith("```"))
            {
                var nl = t.IndexOf('\n');
                if (nl >= 0)
                {
                    t = t[(nl + 1)..];
                    var fin = t.LastIndexOf("```", StringComparison.Ordinal);
                    if (fin >= 0) t = t[..fin];
                }
            }

            var d = t.IndexOf('[');
            var f = t.LastIndexOf(']');
            if (d < 0 || f <= d) return sortie;

            try
            {
                using var doc = JsonDocument.Parse(t[d..(f + 1)]);
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    if (el.ValueKind != JsonValueKind.Object) continue;
                    if (!el.TryGetProperty("i", out var pi)) continue;

                    var i = pi.ValueKind == JsonValueKind.Number ? pi.GetInt32()
                          : int.TryParse(pi.GetString(), out var pv) ? pv : -1;

                    var r = el.TryGetProperty("reecrit", out var pr) ? pr.GetString() ?? "" : "";
                    var m = el.TryGetProperty("motif", out var pm) ? pm.GetString() ?? "" : "";
                    sortie.Add((i, r, m));
                }
            }
            catch (JsonException) { /* réponse inexploitable : aucune proposition, pas d'échec */ }

            return sortie;
        }

        // ── Application d'une proposition acceptée ───────────────────────────

        /// <summary>
        /// Remplace le texte d'origine par la réécriture, dans le bloc et à l'endroit d'où elle
        /// vient. Le remplacement est ANCRÉ SUR LE TEXTE D'ORIGINE : si le médecin a retouché le
        /// bloc entre-temps, l'ancre ne se retrouve plus et rien n'est modifié — plutôt que d'écraser
        /// ce qu'il vient d'écrire.
        /// </summary>
        internal void AppliquerProposition(ConstatQualite c)
        {
            if (!c.ProposableEncore) return;

            var bloc = _dossier.Blocs.FirstOrDefault(b => b.Key == c.BlocKey);
            if (bloc == null) return;

            var avant = bloc.ContenuValide ?? "";
            if (!avant.Contains(c.Original, StringComparison.Ordinal)) return;

            var i = avant.IndexOf(c.Original, StringComparison.Ordinal);
            var apres = avant[..i] + EchapperSiJson(avant, c.Proposition) + avant[(i + c.Original.Length)..];

            // Écrit par le VIEW MODEL du bloc quand il existe : son setter Contenu relit les champs
            // structurés (cartes, actions, indications) derrière le changement. Écrire dans le
            // modèle laisserait l'éditeur afficher l'ancien texte jusqu'à la réouverture du dossier.
            // Repli sur le modèle pour un bloc absent de l'éditeur — « Cadre Éducatif (ancien
            // parcours) » est dans le dossier mais n'est plus proposé à la rédaction.
            var vm = Blocs.FirstOrDefault(b => b.Model.Key == c.BlocKey);
            if (vm != null) vm.Contenu = apres;
            else bloc.ContenuValide = apres;

            c.Appliquee = true;
            (AppliquerPropositionCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        /// <summary>
        /// Dans un bloc JSON, la réécriture est posée à l'intérieur d'une chaîne : les guillemets et
        /// les antislashs qu'elle contiendrait casseraient le document. On les échappe. Sur un bloc
        /// en texte simple, il n'y a rien à échapper.
        /// </summary>
        private static string EchapperSiJson(string contenuBloc, string texte)
        {
            var t = contenuBloc.TrimStart();
            if (!t.StartsWith("{") && !t.StartsWith("[")) return texte;
            return texte.Replace("\\", "\\\\").Replace("\"", "\\\"")
                        .Replace("\r", "").Replace("\n", "\\n");
        }
    }
}
