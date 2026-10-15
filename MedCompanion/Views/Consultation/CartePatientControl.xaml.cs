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
                if (e.NewValue is ViewModels.ConsultationModeViewModel vm) AppliquerDisposition(vm.IsFocusDossier);
            };
        }

        private void SurChangement(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "IsFocusDossier" && sender is ViewModels.ConsultationModeViewModel vm)
                AppliquerDisposition(vm.IsFocusDossier);
            if (e.PropertyName is "Carte" or "IsCarteVisible" or "IsFocusDossier") DemanderTrace();
        }

        private bool _pleinEcran;

        /// <summary>
        /// En page (dossier bleu) : deux colonnes, l'enfant entre la Famille et le Suivi.
        /// En plein écran (F3) : trois colonnes, l'enfant au centre exact, les huit blocs autour —
        /// la vraie carte en étoile. Les blocs ne changent pas, seulement leur place.
        /// </summary>
        private void AppliquerDisposition(bool pleinEcran)
        {
            if (_pleinEcran == pleinEcran && Page.Width > 0) return;
            _pleinEcran = pleinEcran;

            Page.Width = pleinEcran ? 1240 : 595;
            Col3.Width = pleinEcran ? new GridLength(28) : new GridLength(0);
            Col4.Width = pleinEcran ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

            void Placer(FrameworkElement el, int ligne, int colonne, int etendue = 1)
            {
                Grid.SetRow(el, ligne);
                Grid.SetColumn(el, colonne);
                Grid.SetColumnSpan(el, etendue);
            }

            if (pleinEcran)
            {
                Placer(BlocFamille, 0, 0);    Placer(BlocMotif, 0, 2);   Placer(BlocBilans, 0, 4);
                Placer(BlocSuivi, 1, 0);      Placer(Centre, 1, 2);      Placer(BlocTraitement, 1, 4);
                Placer(BlocCourriers, 2, 0);  Placer(BlocAutour, 2, 2);  Placer(BlocPoints, 2, 4);
                Placer(BlocParcours, 3, 0, 5);
                Centre.VerticalAlignment = VerticalAlignment.Center;
                Centre.Margin = new Thickness(0, 0, 0, 10);
            }
            else
            {
                Placer(BlocFamille, 0, 0);    Placer(BlocMotif, 0, 2);
                Placer(Centre, 1, 0, 3);
                Placer(BlocSuivi, 2, 0);      Placer(BlocBilans, 2, 2);
                Placer(BlocCourriers, 3, 0);  Placer(BlocTraitement, 3, 2);
                Placer(BlocAutour, 4, 0);     Placer(BlocPoints, 4, 2);
                Placer(BlocParcours, 5, 0, 3);
                Centre.VerticalAlignment = VerticalAlignment.Top;
                Centre.Margin = new Thickness(0, 4, 0, 14);
            }
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
                                                          BlocCourriers, BlocTraitement, BlocAutour, BlocPoints })
            {
                if (bloc.ActualWidth <= 0) continue;
                var haut   = bloc.TranslatePoint(new Point(bloc.ActualWidth / 2, 0), Traits);
                var bas    = bloc.TranslatePoint(new Point(bloc.ActualWidth / 2, bloc.ActualHeight), Traits);
                var gauche = bloc.TranslatePoint(new Point(0, bloc.ActualHeight / 2), Traits);
                var droite = bloc.TranslatePoint(new Point(bloc.ActualWidth, bloc.ActualHeight / 2), Traits);

                // Le bord qui fait face à l'enfant : en plein écran, les blocs de côté sont à sa hauteur.
                Point cible;
                if (bas.Y < c.Y) cible = bas;                                   // au-dessus
                else if (haut.Y > c.Y) cible = haut;                            // en dessous
                else cible = droite.X < c.X ? droite : gauche;                  // à côté

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
