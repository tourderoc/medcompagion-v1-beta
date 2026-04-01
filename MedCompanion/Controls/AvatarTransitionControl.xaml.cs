using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MedCompanion.Models.StateMachine;

namespace MedCompanion.Controls
{
    public partial class AvatarTransitionControl : UserControl
    {
        public static readonly DependencyProperty StartPointProperty =
            DependencyProperty.Register(nameof(StartPoint), typeof(Point), typeof(AvatarTransitionControl),
                new PropertyMetadata(new Point(0, 0), OnPointsChanged));

        public static readonly DependencyProperty EndPointProperty =
            DependencyProperty.Register(nameof(EndPoint), typeof(Point), typeof(AvatarTransitionControl),
                new PropertyMetadata(new Point(0, 0), OnPointsChanged));

        public Point StartPoint
        {
            get => (Point)GetValue(StartPointProperty);
            set => SetValue(StartPointProperty, value);
        }

        public Point EndPoint
        {
            get => (Point)GetValue(EndPointProperty);
            set => SetValue(EndPointProperty, value);
        }

        /// <summary>
        /// Événement déclenché quand on veut supprimer cette transition
        /// </summary>
        public event EventHandler? DeleteRequested;

        /// <summary>
        /// Événement déclenché quand on veut éditer le trigger
        /// </summary>
        public event EventHandler? EditRequested;

        /// <summary>
        /// Événement déclenché quand on clique sur la transition pour la sélectionner
        /// </summary>
        public event EventHandler? SelectionRequested;

        private bool _isHighlighted;
        private bool _isSelected;

        /// <summary>
        /// Indique si cette transition est sélectionnée
        /// </summary>
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                UpdateSelectionVisual();
            }
        }

        public AvatarTransitionControl()
        {
            InitializeComponent();
        }

        private static void OnPointsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (AvatarTransitionControl)d;
            control.UpdateGeometry();
        }

        private void UpdateGeometry()
        {
            var start = StartPoint;
            var end = EndPoint;

            // Calculer les points de contrôle Bezier
            double dist = Math.Abs(end.X - start.X);
            double controlDist = 50 + (dist * 0.2);

            var p1 = new Point(start.X + controlDist, start.Y);
            var p2 = new Point(end.X - controlDist, end.Y);

            // Créer la géométrie
            var geometry = new PathGeometry();
            var figure = new PathFigure { StartPoint = start };
            var segment = new BezierSegment(p1, p2, end, true);
            figure.Segments.Add(segment);
            geometry.Figures.Add(figure);

            ConnectionPath.Data = geometry;
            HitArea.Data = geometry;

            // Calculer le point milieu pour le label
            double t = 0.5;
            double midX = Math.Pow(1 - t, 3) * start.X +
                          3 * Math.Pow(1 - t, 2) * t * p1.X +
                          3 * (1 - t) * Math.Pow(t, 2) * p2.X +
                          Math.Pow(t, 3) * end.X;

            double midY = Math.Pow(1 - t, 3) * start.Y +
                          3 * Math.Pow(1 - t, 2) * t * p1.Y +
                          3 * (1 - t) * Math.Pow(t, 2) * p2.Y +
                          Math.Pow(t, 3) * end.Y;

            LabelTransform.X = midX - 30;
            LabelTransform.Y = midY - 12;

            // Positionner et orienter la flèche
            UpdateArrow(end, p2);
        }

        private void UpdateArrow(Point end, Point controlPoint)
        {
            // Calculer l'angle de la tangente à la fin de la courbe
            double angle = Math.Atan2(end.Y - controlPoint.Y, end.X - controlPoint.X) * 180 / Math.PI;

            ArrowRotation.Angle = angle;
            ArrowTranslation.X = end.X - 10;
            ArrowTranslation.Y = end.Y - 6;
        }

        private void SetHighlight(bool highlight)
        {
            _isHighlighted = highlight;
            UpdateVisualState();
        }

        private void UpdateSelectionVisual()
        {
            UpdateVisualState();
        }

        private void UpdateVisualState()
        {
            string color;
            double thickness;

            if (_isSelected)
            {
                color = "#E91E63"; // Rose pour sélectionné
                thickness = 3;
            }
            else if (_isHighlighted)
            {
                color = "#2196F3"; // Bleu pour survolé
                thickness = 3;
            }
            else
            {
                color = "#888"; // Gris par défaut
                thickness = 2;
            }

            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));

            ConnectionPath.Stroke = brush;
            ArrowHead.Fill = brush;
            ConnectionPath.StrokeThickness = thickness;

            if (_isSelected || _isHighlighted)
            {
                LabelBorder.BorderBrush = brush;
                LabelBorder.Background = _isSelected
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FCE4EC"))
                    : new SolidColorBrush(Colors.White);
            }
            else
            {
                LabelBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DDD"));
                LabelBorder.Background = new SolidColorBrush(Colors.White);
            }
        }

        private void OnMouseEnter(object sender, MouseEventArgs e)
        {
            SetHighlight(true);
        }

        private void OnMouseLeave(object sender, MouseEventArgs e)
        {
            SetHighlight(false);
        }

        private void OnLabelMouseEnter(object sender, MouseEventArgs e)
        {
            SetHighlight(true);
        }

        private void OnLabelMouseLeave(object sender, MouseEventArgs e)
        {
            SetHighlight(false);
        }

        private void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            DeleteRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnRenameClick(object sender, RoutedEventArgs e)
        {
            EditRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnLabelClick(object sender, MouseButtonEventArgs e)
        {
            // Sélectionner la transition quand on clique sur le label
            SelectionRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }

        private void OnHitAreaClick(object sender, MouseButtonEventArgs e)
        {
            SelectionRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }
}
