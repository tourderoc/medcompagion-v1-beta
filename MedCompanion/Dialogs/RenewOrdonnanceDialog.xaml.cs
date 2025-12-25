using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using MedCompanion.Models;

namespace MedCompanion.Dialogs
{
    /// <summary>
    /// Dialog pour sélectionner les médicaments à renouveler depuis la dernière ordonnance
    /// </summary>
    public partial class RenewOrdonnanceDialog : Window
    {
        public ObservableCollection<SelectableMedicament> Medicaments { get; set; }
        public List<MedicamentPrescrit> SelectedMedicaments { get; private set; }

        public RenewOrdonnanceDialog(List<MedicamentPrescrit> medicaments)
        {
            InitializeComponent();

            // Convertir les médicaments en SelectableMedicament (tous cochés par défaut)
            Medicaments = new ObservableCollection<SelectableMedicament>(
                medicaments.Select(m => new SelectableMedicament(m) { IsSelected = true })
            );

            // Lier au ListBox
            MedicamentsListBox.ItemsSource = Medicaments;

            // Afficher info
            InfoTextBlock.Text = $"Dernière ordonnance : {medicaments.Count} médicament(s). Tous sont cochés par défaut.\n" +
                                 "✏️ Vous pouvez modifier la posologie, la durée, la quantité et le nombre de renouvellements avant de valider.";

            SelectedMedicaments = new List<MedicamentPrescrit>();
        }

        private void SelectAllButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var med in Medicaments)
            {
                med.IsSelected = true;
            }
        }

        private void UnselectAllButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var med in Medicaments)
            {
                med.IsSelected = false;
            }
        }

        private void ValidateButton_Click(object sender, RoutedEventArgs e)
        {
            // Récupérer les médicaments cochés
            SelectedMedicaments = Medicaments
                .Where(m => m.IsSelected)
                .Select(m => m.Medicament)
                .ToList();

            if (SelectedMedicaments.Count == 0)
            {
                MessageBox.Show(
                    "Veuillez sélectionner au moins un médicament à renouveler.",
                    "Aucun médicament sélectionné",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return;
            }

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    /// <summary>
    /// Wrapper pour MedicamentPrescrit avec une propriété IsSelected pour le binding
    /// </summary>
    public class SelectableMedicament : INotifyPropertyChanged
    {
        private bool _isSelected;
        private string _posologie;
        private string _duree;
        private int _quantite;
        private int _nombreRenouvellements;

        public MedicamentPrescrit Medicament { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        // Propriétés en lecture seule pour l'affichage
        public string MedicamentNom => Medicament.Medicament.Denomination;
        public string PresentationLibelle => Medicament.Presentation?.Libelle ?? "Aucune présentation";

        // Propriétés éditables avec binding bidirectionnel
        public string Posologie
        {
            get => _posologie;
            set
            {
                if (_posologie != value)
                {
                    _posologie = value;
                    Medicament.Posologie = value; // Mettre à jour l'objet source
                    OnPropertyChanged(nameof(Posologie));
                }
            }
        }

        public string Duree
        {
            get => _duree;
            set
            {
                if (_duree != value)
                {
                    _duree = value;
                    Medicament.Duree = value; // Mettre à jour l'objet source
                    OnPropertyChanged(nameof(Duree));
                }
            }
        }

        public int Quantite
        {
            get => _quantite;
            set
            {
                if (_quantite != value)
                {
                    _quantite = value;
                    Medicament.Quantite = value; // Mettre à jour l'objet source
                    OnPropertyChanged(nameof(Quantite));
                }
            }
        }

        public int NombreRenouvellements
        {
            get => _nombreRenouvellements;
            set
            {
                if (_nombreRenouvellements != value)
                {
                    _nombreRenouvellements = value;
                    Medicament.NombreRenouvellements = value; // Mettre à jour l'objet source

                    // ✅ FIX: Activer le renouvellement si le nombre > 0
                    Medicament.Renouvelable = value > 0;

                    OnPropertyChanged(nameof(NombreRenouvellements));
                }
            }
        }

        public SelectableMedicament(MedicamentPrescrit medicament)
        {
            Medicament = medicament;
            // Initialiser les propriétés éditables depuis le médicament
            _posologie = medicament.Posologie;
            _duree = medicament.Duree;
            _quantite = medicament.Quantite;
            _nombreRenouvellements = medicament.NombreRenouvellements;

            // ✅ S'assurer que Renouvelable est cohérent avec NombreRenouvellements
            if (_nombreRenouvellements > 0)
            {
                Medicament.Renouvelable = true;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
