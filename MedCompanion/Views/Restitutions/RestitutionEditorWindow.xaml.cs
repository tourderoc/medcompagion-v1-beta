using System.Windows;
using MedCompanion.ViewModels.Restitutions;

namespace MedCompanion.Views.Restitutions
{
    public partial class RestitutionEditorWindow : Window
    {
        private RestitutionEditorViewModel? _vm;

        public RestitutionEditorWindow()
        {
            InitializeComponent();
        }

        public RestitutionEditorWindow(RestitutionEditorViewModel vm) : this()
        {
            _vm = vm;
            DataContext = vm;
            Title = $"Dossier de Restitution Clinique — {vm.PatientName}";

            // Fermer la fenêtre quand le VM le demande (bouton ✕ Fermer dans l'éditeur).
            vm.RequestClose += () =>
            {
                if (Dispatcher.CheckAccess()) Close();
                else Dispatcher.Invoke(Close);
            };
        }
    }
}
