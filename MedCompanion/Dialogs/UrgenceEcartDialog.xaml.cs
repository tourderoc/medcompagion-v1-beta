using System.Windows;

namespace MedCompanion.Dialogs
{
    public partial class UrgenceEcartDialog : Window
    {
        public string Motif { get; private set; } = "";

        public UrgenceEcartDialog()
        {
            InitializeComponent();
            Loaded += (_, _) => MotifTextBox.Focus();
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            Motif = MotifTextBox.Text?.Trim() ?? "";
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
