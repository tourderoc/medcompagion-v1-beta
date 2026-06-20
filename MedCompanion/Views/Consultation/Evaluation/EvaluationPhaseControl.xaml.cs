using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MedCompanion.Views.Consultation.Evaluation
{
    public partial class EvaluationPhaseControl : UserControl
    {
        public EvaluationPhaseControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Convertit le scroll molette en scroll horizontal sur la frise des étapes
        /// (lecture seule). Sinon la molette est captée par le ScrollViewer vertical parent
        /// et la barre des étapes ne défile pas.
        /// </summary>
        private void EtapesNav_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer sv)
            {
                sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta);
                e.Handled = true;
            }
        }
    }
}
