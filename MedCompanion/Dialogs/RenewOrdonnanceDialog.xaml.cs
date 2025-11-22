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
            InfoTextBlock.Text = $"Dernière ordonnance : {medicaments.Count} médicament(s). Tous sont cochés par défaut.";

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

        // Propriétés calculées pour le binding XAML
        public string MedicamentNom => Medicament.Medicament.Denomination;

        public string PresentationLibelle => Medicament.Presentation?.Libelle ?? "Aucune présentation";

        public string Posologie => Medicament.Posologie;

        public string Duree => Medicament.Duree;

        public int Quantite => Medicament.Quantite;

        public int Renouvelable => Medicament.NombreRenouvellements;

        public SelectableMedicament(MedicamentPrescrit medicament)
        {
            Medicament = medicament;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
