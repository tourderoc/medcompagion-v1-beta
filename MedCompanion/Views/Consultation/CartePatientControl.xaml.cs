using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace MedCompanion.Views.Consultation
{
    /// <summary>
    /// Carte du patient (PLAN_CARTE_PATIENT.md). Le contenu vient de
    /// <see cref="ViewModels.ConsultationModeViewModel.Carte"/> ; ici, seulement les traits qui
    /// relient l'enfant à ses blocs, et l'ouverture des documents sources.
    /// </summary>
    public partial class CartePatientControl : UserControl
    {
        public CartePatientControl()
        {
            InitializeComponent();

            Plan.SizeChanged += (_, _) => DemanderTrace();
            Loaded += (_, _) => DemanderTrace();
            DataContextChanged += (_, e) =>
            {
                if (e.OldValue is INotifyPropertyChanged ancien) ancien.PropertyChanged -= SurChangement;
                if (e.NewValue is INotifyPropertyChanged nouveau) nouveau.PropertyChanged += SurChangement;
            };
        }

        private void SurChangement(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is "Carte" or "IsCarteVisible") DemanderTrace();
        }

        /// <summary>Après la mise en page : les blocs n'ont leur taille qu'une fois rendus.</summary>
        private void DemanderTrace() => Dispatcher.BeginInvoke(new Action(Tracer), DispatcherPriority.Loaded);

        /// <summary>
        /// Un trait du centre de l'enfant vers le bord le plus proche de chaque bloc. Discret : il
        /// dit l'appartenance, il ne doit pas attirer l'œil.
        /// </summary>
        private void Tracer()
        {
            Traits.Children.Clear();
            if (!IsVisible || Centre.ActualWidth <= 0) return;

            var c = Centre.TranslatePoint(new Point(Centre.ActualWidth / 2, Centre.ActualHeight / 2), Traits);
            foreach (var bloc in new FrameworkElement[] { BlocFamille, BlocMotif, BlocSuivi, BlocBilans,
                                                          BlocCourriers, BlocTraitement, BlocAutour })
            {
                if (bloc.ActualWidth <= 0) continue;
                var haut = bloc.TranslatePoint(new Point(bloc.ActualWidth / 2, 0), Traits);
                var bas  = bloc.TranslatePoint(new Point(bloc.ActualWidth / 2, bloc.ActualHeight), Traits);
                var cible = bas.Y < c.Y ? bas : haut;   // bloc au-dessus : son bord bas ; en dessous : son bord haut

                Traits.Children.Add(new Line
                {
                    X1 = c.X, Y1 = c.Y, X2 = cible.X, Y2 = cible.Y,
                    Stroke = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)),
                    StrokeThickness = 1.5
                });
            }
        }

        /// <summary>Une ligne qui a un document source l'ouvre avec l'application par défaut.</summary>
        private void Ligne_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement el || el.Tag is not string chemin || chemin.Length == 0) return;
            if (!File.Exists(chemin)) return;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(chemin) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Impossible d'ouvrir le document :\n{ex.Message}", "Carte du patient",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
