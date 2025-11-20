using System;
using System.Windows;
using MedCompanion.Models;

namespace MedCompanion.Dialogs;

public partial class OrdonnanceIDEDialog : Window
{
    public OrdonnanceIDE? Result { get; private set; }
    
    public OrdonnanceIDEDialog(string patientNom, string patientPrenom, string dateNaissance)
    {
        InitializeComponent();
        
        // Pré-remplir les informations
        DateTextBox.Text = DateTime.Now.ToString("dd/MM/yyyy");
        PatientTextBox.Text = $"{patientNom} {patientPrenom}";
        DateNaissanceTextBox.Text = dateNaissance;
    }
    
    private void ValiderButton_Click(object sender, RoutedEventArgs e)
    {
        // Validation basique
        if (string.IsNullOrWhiteSpace(SoinsTextBox.Text))
        {
            MessageBox.Show(
                "Veuillez saisir les soins prescrits.",
                "Information manquante",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
            return;
        }
        
        if (string.IsNullOrWhiteSpace(DureeTextBox.Text))
        {
            MessageBox.Show(
                "Veuillez saisir la durée de l'ordonnance.",
                "Information manquante",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
            return;
        }
        
        // Créer le résultat
        Result = new OrdonnanceIDE
        {
            DateCreation = DateTime.Now,
            Patient = PatientTextBox.Text,
            DateNaissance = DateNaissanceTextBox.Text,
            SoinsPrescrits = SoinsTextBox.Text.Trim(),
            Duree = DureeTextBox.Text.Trim(),
            Renouvelable = RenouvelableTextBox.Text.Trim()
        };
        
        DialogResult = true;
        Close();
    }
    
    private void AnnulerButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
