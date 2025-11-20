using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MedCompanion.Views.Patient
{
    /// <summary>
    /// UserControl pour la recherche de patients avec suggestions et création
    /// </summary>
    public partial class PatientSearchControl : UserControl
    {
        public PatientSearchControl()
        {
            InitializeComponent();

            // Intercepter le Paste (Ctrl+V) pour nettoyer le clipboard
            DataObject.AddPastingHandler(SearchBox, OnPaste);
        }

        /// <summary>
        /// Handler pour le clic sur "Créer patient" dans le popup
        /// </summary>
        private void CreatePatientBorder_Click(object sender, MouseButtonEventArgs e)
        {
            // Cette logique sera gérée par le ViewModel via Command
            // mais on garde le handler pour la compatibilité
        }

        /// <summary>
        /// Intercepte le Paste (Ctrl+V) pour nettoyer automatiquement les caractères invisibles
        /// (espaces insécables, zero-width, balises HTML, etc. venant de Doctolib)
        /// </summary>
        private void OnPaste(object sender, DataObjectPastingEventArgs e)
        {
            // Vérifier qu'on colle du texte
            if (e.DataObject.GetDataPresent(typeof(string)))
            {
                var pastedText = e.DataObject.GetData(typeof(string)) as string;

                if (!string.IsNullOrEmpty(pastedText))
                {
                    // Nettoyer agressivement les caractères invisibles
                    var cleanedText = ParsingService.CleanInvisibleCharacters(pastedText);

                    // Annuler le paste par défaut
                    e.CancelCommand();

                    // Insérer le texte nettoyé manuellement
                    var textBox = sender as TextBox;
                    if (textBox != null)
                    {
                        var caretIndex = textBox.CaretIndex;
                        var currentText = textBox.Text ?? string.Empty;

                        // Supprimer la sélection si existante
                        if (textBox.SelectionLength > 0)
                        {
                            currentText = currentText.Remove(textBox.SelectionStart, textBox.SelectionLength);
                            caretIndex = textBox.SelectionStart;
                        }

                        // Insérer le texte nettoyé
                        var newText = currentText.Insert(caretIndex, cleanedText);
                        textBox.Text = newText;

                        // Replacer le curseur après le texte collé
                        textBox.CaretIndex = caretIndex + cleanedText.Length;

                        // IMPORTANT: Forcer la mise à jour du binding pour déclencher la recherche
                        var bindingExpression = textBox.GetBindingExpression(TextBox.TextProperty);
                        bindingExpression?.UpdateSource();
                    }
                }
            }
        }
    }
}
