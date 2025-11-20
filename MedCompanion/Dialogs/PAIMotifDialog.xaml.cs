using System.Windows;

namespace MedCompanion.Dialogs;

public partial class PAIMotifDialog : Window
{
    public string? Motif { get; private set; }
    
    public PAIMotifDialog()
    {
        InitializeComponent();
    }
    
    private void AutreTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        // Cocher automatiquement le radio "Autre" quand on clique dans la TextBox
        AutreRadio.IsChecked = true;
    }
    
    private void ValiderButton_Click(object sender, RoutedEventArgs e)
    {
        // Récupérer le motif sélectionné
        if (MedicamentRadio.IsChecked == true)
        {
            Motif = "Administration de médicament à l'école";
        }
        else if (AmenagementRadio.IsChecked == true)
        {
            Motif = "Aménagement scolaire";
        }
        else if (AutreRadio.IsChecked == true)
        {
            var autreMotif = AutreTextBox.Text.Trim();
            if (string.IsNullOrEmpty(autreMotif))
            {
                MessageBox.Show(
                    "Veuillez saisir un motif dans le champ 'Autre' ou sélectionner une autre option.",
                    "Motif requis",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return;
            }
            Motif = autreMotif;
        }
        
        DialogResult = true;
        Close();
    }
    
    private void AnnulerButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
