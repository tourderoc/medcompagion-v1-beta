using System.Windows;

namespace MedCompanion.Dialogs
{
    /// <summary>
    /// Dialogue personnalisé avec 3 choix : Option1, Option2, Annuler
    /// </summary>
    public partial class CustomChoiceDialog : Window
    {
        /// <summary>
        /// Énumération des choix possibles
        /// </summary>
        public enum Choice
        {
            None,
            Option1,
            Option2,
            Cancel
        }

        /// <summary>
        /// Choix sélectionné par l'utilisateur
        /// </summary>
        public Choice UserChoice { get; private set; } = Choice.None;

        /// <summary>
        /// Constructeur du dialogue
        /// </summary>
        /// <param name="title">Titre de la fenêtre</param>
        /// <param name="message">Message à afficher</param>
        /// <param name="option1Text">Texte du bouton Option 1</param>
        /// <param name="option2Text">Texte du bouton Option 2</param>
        /// <param name="cancelText">Texte du bouton Annuler</param>
        public CustomChoiceDialog(
            string title,
            string message,
            string option1Text,
            string option2Text,
            string cancelText)
        {
            InitializeComponent();
            
            Title = title;
            MessageText.Text = message;
            Option1Button.Content = option1Text;
            Option2Button.Content = option2Text;
            CancelButton.Content = cancelText;
        }

        /// <summary>
        /// Gestion du clic sur Option 1
        /// </summary>
        private void Option1_Click(object sender, RoutedEventArgs e)
        {
            UserChoice = Choice.Option1;
            DialogResult = true;
            Close();
        }

        /// <summary>
        /// Gestion du clic sur Option 2
        /// </summary>
        private void Option2_Click(object sender, RoutedEventArgs e)
        {
            UserChoice = Choice.Option2;
            DialogResult = true;
            Close();
        }

        /// <summary>
        /// Gestion du clic sur Annuler
        /// </summary>
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            UserChoice = Choice.Cancel;
            DialogResult = false;
            Close();
        }
    }
}
