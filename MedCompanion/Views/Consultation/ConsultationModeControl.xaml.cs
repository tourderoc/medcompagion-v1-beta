using System.Windows.Controls;
using MedCompanion.Models;
using MedCompanion.ViewModels;

namespace MedCompanion.Views.Consultation
{
    /// <summary>
    /// Mode Consultation - Interface adaptative avec dossier patient
    /// Layout: Espace de travail (67%) + Dossier patient (33%)
    /// 3 etats: FocusTravail (100% travail), Consultation (67/33), FocusDossier (100% dossier)
    /// Raccourcis: F1, F2, F3
    /// </summary>
    public partial class ConsultationModeControl : UserControl
    {
        private ConsultationModeViewModel? _viewModel;

        public ConsultationModeControl()
        {
            InitializeComponent();
            _viewModel = DataContext as ConsultationModeViewModel;
        }

        /// <summary>
        /// Charge un patient dans le mode consultation
        /// </summary>
        public void LoadPatient(PatientIndexEntry patient)
        {
            _viewModel?.LoadPatient(patient);
        }

        /// <summary>
        /// Change l'etat d'affichage
        /// </summary>
        public void SetViewState(ConsultationViewState state)
        {
            if (_viewModel != null)
            {
                _viewModel.CurrentState = state;
            }
        }

        /// <summary>
        /// Recupere le ViewModel pour injection de dependances
        /// </summary>
        public ConsultationModeViewModel? GetViewModel() => _viewModel;
    }
}
