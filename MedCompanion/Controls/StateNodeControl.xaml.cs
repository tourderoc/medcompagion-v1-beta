using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;
using MedCompanion.Models.StateMachine;

namespace MedCompanion.Controls
{
    public partial class StateNodeControl : UserControl
    {
        /// <summary>
        /// Événement déclenché quand on commence à drag depuis un connecteur
        /// </summary>
        public static readonly RoutedEvent ConnectorDragStartedEvent =
            EventManager.RegisterRoutedEvent("ConnectorDragStarted", RoutingStrategy.Bubble,
                typeof(ConnectorDragEventHandler), typeof(StateNodeControl));

        /// <summary>
        /// Événement déclenché quand on relâche sur un connecteur
        /// </summary>
        public static readonly RoutedEvent ConnectorDragEndedEvent =
            EventManager.RegisterRoutedEvent("ConnectorDragEnded", RoutingStrategy.Bubble,
                typeof(ConnectorDragEventHandler), typeof(StateNodeControl));

        public event ConnectorDragEventHandler ConnectorDragStarted
        {
            add { AddHandler(ConnectorDragStartedEvent, value); }
            remove { RemoveHandler(ConnectorDragStartedEvent, value); }
        }

        public event ConnectorDragEventHandler ConnectorDragEnded
        {
            add { AddHandler(ConnectorDragEndedEvent, value); }
            remove { RemoveHandler(ConnectorDragEndedEvent, value); }
        }

        public StateNodeControl()
        {
            InitializeComponent();
        }

        private void OnConnectorMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Ellipse connector && DataContext is AvatarState state)
            {
                var position = connector.Tag?.ToString() ?? "Right";
                var args = new ConnectorDragEventArgs(ConnectorDragStartedEvent, state, position);
                RaiseEvent(args);

                // Ne pas capturer ici - l'éditeur gère la capture
                e.Handled = true;
            }
        }

        private void OnConnectorMouseUp(object sender, MouseButtonEventArgs e)
        {
            // Cet événement n'est plus nécessaire car l'éditeur gère tout
            // Mais on le garde au cas où on relâche directement sur un connecteur
            if (sender is Ellipse connector && DataContext is AvatarState state)
            {
                var position = connector.Tag?.ToString() ?? "Left";
                var args = new ConnectorDragEventArgs(ConnectorDragEndedEvent, state, position);
                RaiseEvent(args);

                e.Handled = true;
            }
        }
    }

    /// <summary>
    /// Arguments pour les événements de drag des connecteurs
    /// </summary>
    public class ConnectorDragEventArgs : RoutedEventArgs
    {
        public AvatarState State { get; }
        public string ConnectorPosition { get; }

        public ConnectorDragEventArgs(RoutedEvent routedEvent, AvatarState state, string position)
            : base(routedEvent)
        {
            State = state;
            ConnectorPosition = position;
        }
    }

    public delegate void ConnectorDragEventHandler(object sender, ConnectorDragEventArgs e);
}
