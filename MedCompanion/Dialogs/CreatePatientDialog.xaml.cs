using System;
using System.Globalization;
using System.Windows;
using MedCompanion.Models;

namespace MedCompanion.Dialogs
{
    public partial class CreatePatientDialog : Window
    {
        public PatientMetadata? Result { get; private set; }

        public CreatePatientDialog()
        {
            InitializeComponent();
            PrenomTextBox.Focus();
        }

        /// <summary>
        /// Constructeur avec pré-remplissage depuis un parsing Doctolib
        /// </summary>
        public CreatePatientDialog(string prenom, string nom, string? dob, string? sexe) : this()
        {
            PrenomTextBox.Text = prenom;
            NomTextBox.Text = nom;
            
            if (!string.IsNullOrEmpty(dob))
            {
                // Convertir au format JJ/MM/AAAA si nécessaire
                if (DateTime.TryParse(dob, out var date))
                {
                    DobTextBox.Text = date.ToString("dd/MM/yyyy");
                }
                else
                {
                    DobTextBox.Text = dob;
                }
            }
            
            if (sexe == "H")
            {
                SexeHRadio.IsChecked = true;
            }
            else if (sexe == "F")
            {
                SexeFRadio.IsChecked = true;
            }
        }

        private void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            var prenom = PrenomTextBox.Text.Trim();
            var nom = NomTextBox.Text.Trim();

            if (string.IsNullOrEmpty(prenom) || string.IsNullOrEmpty(nom))
            {
                MessageBox.Show("Prénom et nom sont requis.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var metadata = new PatientMetadata
            {
                Prenom = CapitalizeFirstLetter(prenom),
                Nom = nom.ToUpper()
            };

            // Date de naissance (optionnelle)
            var dobText = DobTextBox.Text.Trim();
            if (!string.IsNullOrEmpty(dobText))
            {
                if (TryParseDob(dobText, out var dob))
                {
                    metadata.Dob = dob.ToString("yyyy-MM-dd");
                }
                else
                {
                    MessageBox.Show("Format de date invalide. Utilisez JJ/MM/AAAA ou AAAA-MM-JJ", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            // Sexe (optionnel)
            if (SexeHRadio.IsChecked == true)
            {
                metadata.Sexe = "H";
            }
            else if (SexeFRadio.IsChecked == true)
            {
                metadata.Sexe = "F";
            }

            Result = metadata;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private bool TryParseDob(string input, out DateTime result)
        {
            // Essayer plusieurs formats
            string[] formats = { "dd/MM/yyyy", "yyyy-MM-dd", "dd-MM-yyyy", "d/M/yyyy" };
            
            return DateTime.TryParseExact(input, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
        }

        private string CapitalizeFirstLetter(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            var words = text.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);
            var capitalizedWords = new string[words.Length];

            for (int i = 0; i < words.Length; i++)
            {
                var word = words[i];
                if (word.Length > 0)
                {
                    capitalizedWords[i] = char.ToUpper(word[0]) + (word.Length > 1 ? word.Substring(1).ToLower() : "");
                }
            }

            if (text.Contains('-'))
            {
                return string.Join("-", capitalizedWords);
            }
            return string.Join(" ", capitalizedWords);
        }
    }
}
