using System.Windows;
using MedCompanion.Models;

namespace MedCompanion.Dialogs
{
    /// <summary>
    /// Window host for ScannedFormImportDialog
    /// </summary>
    public partial class ScannedFormImportWindow : Window
    {
        public ScannedFormImportWindow(PatientIndexEntry patient)
        {
            InitializeComponent();
            
            // Pass patient to the UserControl via DataContext
            ImportDialog.DataContext = patient;
        }
    }
}
