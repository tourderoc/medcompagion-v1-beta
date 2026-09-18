using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Input;
using MedCompanion.Commands;
using MedCompanion.Services.Restitutions;

namespace MedCompanion.ViewModels.Restitutions
{
    /// <summary>
    /// Phase 2 du service qualité — les contradictions du document avec lui-même.
    ///
    /// SON PÉRIMÈTRE, QUI EST AUSSI SA LIMITE. Ce contrôle relit le TEXTE PRODUIT, jamais le dossier
    /// patient. Il ne vérifie aucun fait : savoir si un diagnostic est juste, si une date est la
    /// bonne, si l'intervenant existe, c'est le travail du médecin et il le garde. Le contrôle ne
    /// sait qu'une chose : ce document se contredit-il, se répète-t-il, dit-il quelque chose que le
    /// lecteur ne peut pas encore comprendre. Périmètre fixé par le Dr Lassoued le 17/09/2026.
    ///
    /// CETTE COUCHE-CI EST DÉTERMINISTE, SANS MODÈLE. Elle ne demande rien à un LLM : elle compare
    /// le document à ses propres règles de construction. Donc pas de faux positif inventé, pas
    /// d'attente, pas de moteur à estampiller — c'est pour cela que <c>Moteur</c> reste vide, comme
    /// en phase 1. La couche qui confronte deux sections entre elles viendra après, avec un modèle,
    /// et elle portera le nom du moteur qui l'a produite.
    /// </summary>
    public partial class RestitutionEditorViewModel
    {
        private bool _phase2EnCours;
        public bool Phase2EnCours
        {
            get => _phase2EnCours;
            private set
            {
                if (_phase2EnCours == value) return;
                _phase2EnCours = value;
                OnPropertyChanged();
                (LancerPhase2Command as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        private string _phase2Resume = "";
        public string Phase2Resume
        {
            get => _phase2Resume;
            private set { if (_phase2Resume == value) return; _phase2Resume = value; OnPropertyChanged(); }
        }

        private ICommand? _lancerPhase2;
        public ICommand LancerPhase2Command => _lancerPhase2 ??= new RelayCommand(
            _ => LancerPhase2(), _ => !Phase2EnCours);

        /// <summary>
        /// Contrôle : un bloc cite-t-il la cartographie avant qu'elle ait été présentée au lecteur.
        ///
        /// LE DÉFAUT QU'IL ATTRAPE, observé le 18/09/2026 sur un dossier réel : le bloc « Contexte
        /// familial », pages 3-4, écrivait « la constance des scores parentaux entre les deux
        /// passations de cartographie ». La cartographie n'est présentée qu'au rang 8. Le lecteur
        /// rencontrait un outil, un score et une passation dont personne ne lui avait parlé.
        ///
        /// LA PRÉVENTION EST DANS LE PROMPT, PAS ICI. Depuis le 18/09 la consigne est posée sur les
        /// blocs concernés. Ce contrôle sert aux dossiers rédigés AVANT, et aux fois où le modèle
        /// n'obéit pas — il ne remplace pas la consigne, il en vérifie l'effet.
        ///
        /// LES TERMES NE SONT PAS RECOPIÉS ICI. Ils viennent de
        /// <see cref="RestitutionSuggesterService.TermesInterditsPour"/>, la même méthode qui
        /// construit la consigne. Recopier la liste l'aurait fait diverger au premier ajout, et le
        /// médecin aurait vu signaler un mot que rien n'interdisait — ou l'inverse.
        /// </summary>
        public void LancerPhase2()
        {
            Phase2EnCours = true;
            try
            {
                var constats = new List<ConstatQualite>();
                var examines = 0;

                foreach (var bloc in _dossier.Blocs)
                {
                    var termes = RestitutionSuggesterService.TermesInterditsPour(bloc.Key);
                    if (termes.Count == 0) continue;

                    examines++;

                    var texte = bloc.ContenuValide;
                    if (string.IsNullOrWhiteSpace(texte)) continue;

                    // Ordonnés par position DANS LE TEXTE, pas dans la liste des termes : l'extrait
                    // doit montrer là où le problème commence. Trié sur la liste, il se centrait sur
                    // « cartographie » en fin de phrase et coupait « scores parentaux » en deux.
                    var trouves = termes
                        .Select(t => (Terme: t, Passage: PremierPassage(texte, t)))
                        .Where(x => x.Passage != null)
                        .OrderBy(x => x.Passage!.Value.Position)
                        .ToList();

                    if (trouves.Count == 0) continue;

                    // UN constat par bloc, pas un par mot : le médecin corrige un bloc, il ne
                    // corrige pas un mot. Trois occurrences dans le même paragraphe feraient trois
                    // lignes pour une seule action — et un rapport qui énumère finit ignoré.
                    var mots = string.Join(", ", trouves.Select(x => $"« {x.Terme} »"));
                    var extrait = trouves[0].Passage!.Value.Extrait;

                    constats.Add(new ConstatQualite
                    {
                        Phase   = 2,
                        Gravite = GraviteConstat.Vigilance,
                        Ou      = bloc.Titre,
                        Message = bloc.Key == "restitution_1page"
                            ? $"Vocabulaire technique dans la page destinée aux parents : {mots}. " +
                              $"« …{extrait}… » — la démarche peut être évoquée, pas les scores ni les numéros de sphère."
                            : $"Cite la cartographie avant qu'elle soit présentée au lecteur : {mots}. " +
                              $"« …{extrait}… » — dites ce qui a été observé, pas où vous l'avez lu."
                    });
                }

                RemplacerConstats(2, constats);

                // Le rapport dit son périmètre : un contrôle muet est indiscernable d'un contrôle
                // qui n'a pas tourné.
                Phase2Resume = examines == 0
                    ? "Aucun bloc à contrôler."
                    : constats.Count == 0
                        ? $"{examines} blocs d'avant la cartographie relus — aucun ne la cite."
                        : $"{examines} blocs relus — {constats.Count} citent la cartographie trop tôt.";
            }
            finally
            {
                Phase2EnCours = false;
            }
        }

        /// <summary>
        /// Premier passage du terme dans le texte, rendu avec ce qui l'entoure pour que le médecin
        /// retrouve la phrase — sinon il doit relire le bloc entier pour trouver un mot.
        /// Renvoie null si le terme est absent.
        ///
        /// Comparaison insensible à la casse ET aux accents : le modèle écrit « Sphère », « sphere »
        /// ou « SPHÈRE » selon l'humeur du template, et un contrôle qui rate « sphere » sans accent
        /// ne vaut rien.
        /// </summary>
        internal static string? PremierExtrait(string texte, string terme, int marge = 38)
            => PremierPassage(texte, terme, marge)?.Extrait;

        /// <summary>
        /// Comme <see cref="PremierExtrait"/>, mais rend aussi la position du terme : c'est elle qui
        /// permet de choisir, entre plusieurs termes trouvés, celui qui apparaît le PREMIER dans le
        /// texte — donc l'extrait qui montre le début du problème.
        /// </summary>
        internal static (int Position, string Extrait)? PremierPassage(string texte, string terme, int marge = 38)
        {
            var plat  = SansAccents(texte);
            var cible = SansAccents(terme);

            var i = plat.IndexOf(cible, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;

            // Les index sont valables sur le texte d'origine : SansAccents conserve la longueur,
            // caractère pour caractère. Un remplacement qui allongerait la chaîne (« œ » → « oe »)
            // décalerait tout et l'extrait ne tomberait pas sur le bon passage.
            var debut = Math.Max(0, i - marge);
            var fin   = Math.Min(texte.Length, i + cible.Length + marge);

            // Recalé sur des mots entiers : un extrait qui commence par « …arentaux » se lit mal et
            // fait douter de ce qui est signalé. On n'avance/recule que dans la marge, jamais au
            // point de rogner le terme lui-même.
            while (debut > 0 && debut < i && !char.IsWhiteSpace(texte[debut - 1])) debut++;
            while (fin < texte.Length && fin > i + cible.Length && !char.IsWhiteSpace(texte[fin])) fin--;

            var extrait = texte.Substring(debut, fin - debut)
                               .Replace('\n', ' ').Replace('\r', ' ').Trim();

            return (i, extrait);
        }

        /// <summary>
        /// Retire les accents SANS changer la longueur : chaque caractère composé est remplacé par
        /// sa seule base. C'est ce qui permet d'utiliser l'index trouvé sur le texte aplati pour
        /// découper le texte d'origine.
        /// </summary>
        private static string SansAccents(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                var d = c.ToString().Normalize(NormalizationForm.FormD);
                var b = d.FirstOrDefault(x => CharUnicodeInfo.GetUnicodeCategory(x) != UnicodeCategory.NonSpacingMark);
                sb.Append(b == '\0' ? c : b);
            }
            return sb.ToString();
        }
    }
}
