using System.Windows.Controls;
using MedCompanion.Services;
using MedCompanion.ViewModels;

namespace MedCompanion.Views.Consultation
{
    /// <summary>
    /// Contrôle du dossier papier avec couverture et navigation double-page
    /// </summary>
    public partial class PaperDossierControl : UserControl
    {
        public PaperDossierControl()
        {
            InitializeComponent();

            // Initialiser le service de données quand le DataContext est défini
            DataContextChanged += (s, e) =>
            {
                if (DataContext is ConsultationModeViewModel vm)
                {
                    var pathService = new PathService();
                    var dossierDataService = new DossierDataService(pathService);
                    vm.SetDossierDataService(dossierDataService);
                }
            };
        }
    }
}
