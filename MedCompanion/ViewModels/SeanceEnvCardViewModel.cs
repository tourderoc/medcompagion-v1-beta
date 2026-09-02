using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using MedCompanion.Commands;
using MedCompanion.Models.Evaluations;
using MedCompanion.Services.Evaluations;

namespace MedCompanion.ViewModels
{
    /// <summary>Une réponse, telle qu'elle se lit dans le dossier : une marque, une couleur, une phrase.</summary>
    public class SeanceEnvReponseLigne
    {
        public string Texte   { get; init; } = "";
        public string Marque  { get; init; } = "";   // ✓ / ✗ / —
        public string Couleur { get; init; } = "";
        public string Source  { get; init; } = "";   // « feuille parents » ou « entretien »

        public static SeanceEnvReponseLigne De(string texte, ReponseProposition r, string? source = null)
            => new()
            {
                Texte   = texte,
                Marque  = r switch { ReponseProposition.Oui => "✓", ReponseProposition.Non => "✗", _ => "—" },
                Couleur = r switch { ReponseProposition.Oui => "#27AE60", ReponseProposition.Non => "#C0392B", _ => "#C3CEDA" },
                Source  = source ?? ""
            };
    }

    /// <summary>
    /// Une ligne dépliable du dossier bleu. Repliée, elle donne l'état ; dépliée, elle donne les
    /// réponses. Un état seul ne dit pas CE QUI accroche — c'est la raison pour laquelle la carte
    /// de la séance 2 s'est ouverte, et la même vaut ici.
    /// </summary>
    public class SeanceEnvLigneDepliable : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public string Label     { get; init; } = "";
        public string SousLabel { get; init; } = "";
        public string EtatText  { get; init; } = "";
        public string Couleur   { get; init; } = LectureEnvironnementV2.GrisIndetermine;

        public List<SeanceEnvReponseLigne> Reponses { get; init; } = new();
        public bool HasReponses => Reponses.Count > 0;

        /// <summary>Remarques libres du médecin — présentes seulement sur les axes ciblés.</summary>
        public string Remarques { get; init; } = "";
        public bool HasRemarques => !string.IsNullOrWhiteSpace(Remarques);

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value) return;
                _isExpanded = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Chevron));
            }
        }

        public string Chevron => _isExpanded ? "▾" : "▸";

        public ICommand ToggleCommand { get; }

        public SeanceEnvLigneDepliable()
            => ToggleCommand = new RelayCommand(_ => IsExpanded = !IsExpanded);
    }

    /// <summary>Une feuille dimensionnelle et ses nervures, dans le dossier bleu.</summary>
    public class SeanceEnvFeuilleBloc
    {
        public string Label     { get; init; } = "";
        public string SousTitre { get; init; } = "";
        public string Couleur   { get; init; } = "";
        public string EtatText  { get; init; } = "";

        public List<SeanceEnvLigneDepliable> Nervures { get; init; } = new();
    }

    /// <summary>
    /// Carte « Cartographie de l'environnement » de l'onglet BILANS — la feuille des parents et
    /// les items du médecin réunis, tels qu'ils ont été enregistrés.
    ///
    /// La carte montre l'état RÉEL, y compris incomplet. Une cartographie dont trois nervures ne
    /// sont pas lisibles doit se lire comme telle dans le dossier : la présenter au même titre
    /// qu'une cartographie complète ferait fonder plus tard un raisonnement sur du gris.
    /// </summary>
    public class SeanceEnvCardViewModel
    {
        public string FilePath  { get; }
        public string TitreCard { get; }
        public string EtatLigne { get; }
        public bool   EstComplete { get; }
        public string InformateurLigne { get; }

        public List<SeanceEnvFeuilleBloc> Feuilles { get; }

        public SeanceEnvCardViewModel(SeanceEnvironnement s)
        {
            FilePath = s.FilePath;

            var age = s.Age.HasValue ? $" ({s.Age} ans)" : "";
            TitreCard = $"Cartographie de l'environnement — {s.Date:dd/MM/yyyy}{age}";

            var lues = LectureEnvironnementV2.Construire(s.CotationsEnv, s.ReponsesParent);

            var lisibles = lues.Sum(f => f.NbNervuresLisibles);
            var total    = lues.Sum(f => f.NbNervures);
            var manquants = lues.Sum(f => f.NbManquants);

            EstComplete = manquants == 0;
            EtatLigne = EstComplete
                ? $"{lisibles}/{total} nervures lisibles · cartographie complète"
                : $"{lisibles}/{total} nervures lisibles · {manquants} réponse{(manquants > 1 ? "s" : "")} manquante{(manquants > 1 ? "s" : "")}";

            var qui = s.InformateurEnv switch
            {
                "mere" => "Mère", "pere" => "Père", "autre" => "Autre", _ => null
            };
            InformateurLigne = qui == null
                ? (s.HasReponsesParent ? "Rempli par : non renseigné" : "")
                : $"Rempli par : {qui}"
                  + (string.IsNullOrWhiteSpace(s.InformateurEnvNom) ? "" : $" · {s.InformateurEnvNom}");

            Feuilles = lues.Select(f => new SeanceEnvFeuilleBloc
            {
                Label     = f.Label,
                SousTitre = f.SousTitre,
                Couleur   = f.Couleur,
                EtatText  = f.EtatText,
                Nervures  = f.Nervures.Select(n => new SeanceEnvLigneDepliable
                {
                    Label    = n.Label,
                    EtatText = n.EstComplete ? $"{n.NbOui}/{n.NbTotal}" : $"{n.NbManquants} manquante(s)",
                    Couleur  = n.Couleur,
                    Reponses = n.Lignes
                        .Select(l => SeanceEnvReponseLigne.De(l.Texte, l.Reponse, l.SourceLabel))
                        .ToList()
                }).ToList()
            }).ToList();
        }
    }

    /// <summary>
    /// Carte « Évaluation ciblée » de l'onglet BILANS — un bloc par axe, avec ses constats.
    ///
    /// Carte SÉPARÉE de la cartographie, et non une section de celle-ci : les deux ne se lisent
    /// pas ensemble et n'ont pas la même fiabilité. Les fondre laisserait croire qu'un même
    /// regard les a produites.
    /// </summary>
    public class EvaluationCibleeCardViewModel
    {
        public string FilePath  { get; }
        public string TitreCard { get; }
        public string EtatLigne { get; }
        public bool   EstComplete { get; }

        public List<SeanceEnvLigneDepliable> Axes { get; }

        public EvaluationCibleeCardViewModel(SeanceEnvironnement s)
        {
            FilePath  = s.FilePath;
            TitreCard = $"Évaluation ciblée — {s.Date:dd/MM/yyyy}";

            var constats = s.Axes.Sum(a => a.Propositions.Count);
            var repondus = s.Axes.Sum(a => a.NbRepondu);

            EstComplete = constats > 0 && repondus == constats;
            EtatLigne   = $"{s.Axes.Count} axe{(s.Axes.Count > 1 ? "s" : "")} · "
                        + $"{repondus}/{constats} constats renseignés";

            Axes = s.Axes.Select(a => new SeanceEnvLigneDepliable
            {
                Label     = a.Intitule,
                SousLabel = string.IsNullOrWhiteSpace(a.Rattachement) ? "" : $"sert à trancher : {a.Rattachement}",
                EtatText  = $"{a.NbRepondu}/{a.Propositions.Count}",
                // Un axe n'a pas de couleur : ses constats ne se somment pas en un niveau. Trois
                // « oui » sur quatre ne valent pas un score — ils disent trois faits.
                Couleur   = "#EBF3FA",
                Remarques = a.Remarques,
                Reponses  = a.Propositions
                    .Select(p => SeanceEnvReponseLigne.De(p.Texte, p.Reponse))
                    .ToList()
            }).ToList();
        }
    }

    /// <summary>
    /// Bloc de synthèse de la 3ᵉ séance, côté SYNTHÈSE du dossier bleu — à la suite de la synthèse
    /// initiale et de celle de la cartographie de l'enfant.
    /// </summary>
    public class SeanceEnvSyntheseBlocViewModel
    {
        public string FilePath { get; }
        public string Titre    { get; }
        public string Texte    { get; }
        public string Qualification { get; }

        public SeanceEnvSyntheseBlocViewModel(SeanceEnvironnement s)
        {
            FilePath = s.FilePath;
            Titre    = $"Environnement & évaluation ciblée — {s.Date:dd/MM/yyyy}";
            Texte    = (s.SyntheseTexte ?? "").Trim();

            // Les fiabilités déclarées voyagent AVEC le texte. Sans elles, la synthèse se relirait
            // dans six mois avec l'assurance d'une source qu'on avait justement jugée douteuse.
            Qualification =
                $"Environnement : {FiabiliteCartographie.LabelDe(s.FiabiliteEnv).ToLowerInvariant()}"
                + $" · Évaluation ciblée : {FiabiliteCartographie.LabelDe(s.FiabiliteAxes).ToLowerInvariant()}";
        }
    }
}
