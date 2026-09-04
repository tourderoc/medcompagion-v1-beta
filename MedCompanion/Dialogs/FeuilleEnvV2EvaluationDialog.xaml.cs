using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using MedCompanion.Models.Evaluations;
using MedCompanion.Services.Evaluations;

namespace MedCompanion.Dialogs
{
    /// <summary>
    /// Édition des items d'UNE feuille V2 de la cartographie de l'environnement (séance 3),
    /// depuis le bouton « Compléter » du Dossier de Restitution. Les items sont groupés par
    /// nervure et gardent leur SOURCE affichée : un item de la feuille parents et un item coté
    /// en entretien ne se corrigent pas avec la même légitimité, mais le médecin garde la main
    /// sur les deux — même liberté qu'en V1.
    ///
    /// Trois états par item — oui / non / non renseigné : une case vide n'est pas un non, et
    /// l'écraser fausserait la couleur de la nervure (qui n'existe que si elle est complète).
    /// La fiche n'est modifiée qu'à la sauvegarde ; Annuler ne touche à rien.
    /// </summary>
    public partial class FeuilleEnvV2EvaluationDialog : Window
    {
        private readonly SeanceEnvironnement _seance;
        private readonly FeuilleV2 _feuille;

        // Un enregistrement par item, dans l'ordre de la feuille : la case, l'item source.
        private readonly List<(CheckBox Case, ItemEnvironnement Item)> _lignes = new();

        public FeuilleEnvV2EvaluationDialog(SeanceEnvironnement seance, string feuilleKey)
        {
            InitializeComponent();
            _seance  = seance;
            _feuille = Array.Find(CartographieEnvironnementV2.Feuilles, f => f.Key == feuilleKey)
                       ?? CartographieEnvironnementV2.Feuilles[0];

            Title = $"🍃 {_feuille.Label}";
            TitreText.Text = $"{_feuille.Label} — {_feuille.SousTitre}";

            var qui = seance.InformateurEnv switch
            {
                "mere" => "la mère", "pere" => "le père", "autre" => "un autre adulte", _ => null
            };
            ProvenanceText.Text = $"Séance du {seance.Date:dd/MM/yyyy}"
                + (qui != null ? $" — feuille parents remplie par {qui}"
                    + (string.IsNullOrWhiteSpace(seance.InformateurEnvNom) ? "" : $" ({seance.InformateurEnvNom})") : "");

            // Réponses actuelles : mêmes conventions de lecture que LectureEnvironnementV2 —
            // les items parents par rang dans la feuille, les items médecin par leur texte.
            seance.ReponsesParent.TryGetValue(_feuille.Key, out var repsP);
            int iParent = 0;

            foreach (var nervure in _feuille.Nervures)
            {
                ItemsHost.Children.Add(new TextBlock
                {
                    Text       = nervure.IsCentrale ? $"{nervure.Label} (nervure centrale)" : nervure.Label,
                    FontSize   = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = System.Windows.Media.Brushes.DarkSlateGray,
                    Margin     = new Thickness(0, 8, 0, 3)
                });

                foreach (var item in nervure.Items)
                {
                    bool? etat;
                    if (item.Source == SourceItemEnv.Parent)
                    {
                        var v = repsP != null && iParent < repsP.Length ? repsP[iParent] : "";
                        etat = v switch { "oui" => true, "non" => false, _ => (bool?)null };
                        iParent++;
                    }
                    else
                    {
                        etat = _seance.CotationsEnv.TryGetValue(item.Texte, out var r)
                            ? r switch
                            {
                                ReponseProposition.Oui => true,
                                ReponseProposition.Non => false,
                                _                      => (bool?)null
                            }
                            : null;
                    }

                    var sourceTag = item.Source == SourceItemEnv.Parent ? "[feuille parents]" : "[entretien]";
                    var contenu = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
                    contenu.Inlines.Add(new System.Windows.Documents.Run(item.Texte));
                    contenu.Inlines.Add(new System.Windows.Documents.Run($"  {sourceTag}")
                    {
                        FontSize = 10, FontStyle = FontStyles.Italic,
                        Foreground = System.Windows.Media.Brushes.Gray
                    });

                    var cb = new CheckBox
                    {
                        IsThreeState = true,
                        IsChecked    = etat,
                        Margin       = new Thickness(8, 4, 0, 4),
                        Cursor       = System.Windows.Input.Cursors.Hand,
                        Content      = contenu
                    };
                    _lignes.Add((cb, item));
                    ItemsHost.Children.Add(cb);
                }
            }
        }

        private void Annuler_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void Sauvegarder_Click(object sender, RoutedEventArgs e)
        {
            // Réécriture des deux moitiés, dans les mêmes conventions qu'au dépouillement.
            var repsP = new List<string>();
            foreach (var (cb, item) in _lignes)
            {
                if (item.Source == SourceItemEnv.Parent)
                {
                    repsP.Add(cb.IsChecked switch { true => "oui", false => "non", _ => "vide" });
                }
                else
                {
                    _seance.CotationsEnv[item.Texte] = cb.IsChecked switch
                    {
                        true  => ReponseProposition.Oui,
                        false => ReponseProposition.Non,
                        _     => ReponseProposition.NonObservee
                    };
                }
            }
            _seance.ReponsesParent[_feuille.Key] = repsP.ToArray();
            _seance.DerniereModif = DateTime.Now;
            DialogResult = true;
        }
    }
}
