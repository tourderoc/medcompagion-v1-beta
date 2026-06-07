using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MedCompanion.Models.Evaluations;
using MedCompanion.ViewModels;

namespace MedCompanion.Views.Consultation.Evaluation
{
    public partial class EvaluationPhaseControl : UserControl
    {
        public EvaluationPhaseControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Clic sur un des 4 radio (état d'un axe). Le Tag du RadioButton porte le code 0/1/2/3.
        /// </summary>
        private void AxisState_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            if (btn.DataContext is not EvaluationAxis axis) return;
            if (btn.Tag is not string tag || !int.TryParse(tag, out var code)) return;

            axis.State = (AxisExplorationState)Math.Clamp(code, 0, 2);
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
