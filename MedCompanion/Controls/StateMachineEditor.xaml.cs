using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using MedCompanion.Models.StateMachine;
using System.Linq;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Collections.Generic;

namespace MedCompanion.Controls
{
    public partial class StateMachineEditor : UserControl
    {
        private StateMachineProfile? _profile;
        private readonly Dictionary<Guid, AvatarTransitionControl> _transitionControls = new();

        // Pour la création de transitions par drag
        private bool _isCreatingTransition;
        private AvatarState? _dragSourceState;
        private Line? _tempConnectionLine;
        private Point _dragStartPoint;

        // Transition sélectionnée
        private AvatarTransition? _selectedTransition;
        private AvatarTransitionControl? _selectedTransitionControl;

        public event EventHandler<AvatarState>? StateSelected;
        public event EventHandler<AvatarTransition>? TransitionSelected;

        public StateMachineEditor()
        {
            InitializeComponent();

            // Écouter les événements des connecteurs
            AddHandler(StateNodeControl.ConnectorDragStartedEvent, new ConnectorDragEventHandler(OnConnectorDragStarted));
            AddHandler(StateNodeControl.ConnectorDragEndedEvent, new ConnectorDragEventHandler(OnConnectorDragEnded));

            // Écouter les événements de souris au niveau du contrôle pour le drag
            MouseMove += OnEditorMouseMove;
            MouseLeftButtonUp += OnEditorMouseUp;
        }

        public void LoadProfile(StateMachineProfile profile)
        {
            _profile = profile;
            NodesItemsControl.ItemsSource = _profile.States;

            // S'abonner aux changements
            _profile.Transitions.CollectionChanged += OnTransitionsChanged;
            foreach (var state in _profile.States)
            {
                state.PropertyChanged += OnStatePropertyChanged;
            }
            _profile.States.CollectionChanged += OnStatesChanged;

            RefreshConnections();
        }

        private void OnStatesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (AvatarState state in e.NewItems)
                    state.PropertyChanged += OnStatePropertyChanged;
            }
            if (e.OldItems != null)
            {
                foreach (AvatarState state in e.OldItems)
                    state.PropertyChanged -= OnStatePropertyChanged;
            }
            RefreshConnections();
        }

        private void OnStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AvatarState.GraphX) || e.PropertyName == nameof(AvatarState.GraphY))
            {
                UpdateConnectionsForState((AvatarState)sender!);
            }
        }

        private void OnTransitionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            RefreshConnections();
        }

        #region Connections Management

        private void RefreshConnections()
        {
            // Garder la ligne temporaire si elle existe
            var tempLine = _tempConnectionLine;

            ConnectionsLayer.Children.Clear();
            _transitionControls.Clear();

            if (_profile == null) return;

            foreach (var transition in _profile.Transitions)
            {
                AddConnectionVisual(transition);
            }

            // Restaurer la ligne temporaire
            if (tempLine != null && _isCreatingTransition)
            {
                ConnectionsLayer.Children.Add(tempLine);
            }
        }

        private void AddConnectionVisual(AvatarTransition transition)
        {
            if (_profile == null) return;

            var source = _profile.States.FirstOrDefault(s => s.Id == transition.SourceStateId);
            var target = _profile.States.FirstOrDefault(s => s.Id == transition.TargetStateId);

            if (source != null && target != null)
            {
                var control = new AvatarTransitionControl
                {
                    DataContext = transition
                };

                // S'abonner aux événements
                control.DeleteRequested += OnTransitionDeleteRequested;
                control.EditRequested += OnTransitionEditRequested;
                control.SelectionRequested += OnTransitionSelectionRequested;

                UpdateConnectionPoints(control, source, target);

                ConnectionsLayer.Children.Add(control);
                _transitionControls[transition.Id] = control;
            }
        }

        private void OnTransitionSelectionRequested(object? sender, EventArgs e)
        {
            if (sender is AvatarTransitionControl control && control.DataContext is AvatarTransition transition)
            {
                SelectTransition(transition, control);
            }
        }

        private void SelectTransition(AvatarTransition transition, AvatarTransitionControl control)
        {
            // Désélectionner l'ancienne
            if (_selectedTransitionControl != null)
            {
                _selectedTransitionControl.IsSelected = false;
            }

            _selectedTransition = transition;
            _selectedTransitionControl = control;
            control.IsSelected = true;

            TransitionSelected?.Invoke(this, transition);
        }

        private void DeselectTransition()
        {
            if (_selectedTransitionControl != null)
            {
                _selectedTransitionControl.IsSelected = false;
            }
            _selectedTransition = null;
            _selectedTransitionControl = null;
        }

        private void OnTransitionDeleteRequested(object? sender, EventArgs e)
        {
            if (sender is AvatarTransitionControl control && control.DataContext is AvatarTransition transition)
            {
                var result = MessageBox.Show(
                    $"Supprimer la transition '{transition.Trigger}' ?",
                    "Confirmation",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes && _profile != null)
                {
                    _profile.Transitions.Remove(transition);
                    DeselectTransition();
                }
            }
        }

        private void OnTransitionEditRequested(object? sender, EventArgs e)
        {
            if (sender is AvatarTransitionControl control && control.DataContext is AvatarTransition transition)
            {
                EditTransitionTrigger(transition);
            }
        }

        private void EditTransitionTrigger(AvatarTransition transition)
        {
            var newTrigger = ShowInputDialog("Editer Transition", "Nom du trigger:", transition.Trigger);

            if (!string.IsNullOrWhiteSpace(newTrigger))
            {
                transition.Trigger = newTrigger;
                RefreshConnections();
            }
        }

        private void UpdateConnectionsForState(AvatarState state)
        {
            if (_profile == null) return;

            var involvedTransitions = _profile.Transitions
                .Where(t => t.SourceStateId == state.Id || t.TargetStateId == state.Id);

            foreach (var transition in involvedTransitions)
            {
                if (_transitionControls.TryGetValue(transition.Id, out var control))
                {
                    var source = _profile.States.FirstOrDefault(s => s.Id == transition.SourceStateId);
                    var target = _profile.States.FirstOrDefault(s => s.Id == transition.TargetStateId);

                    if (source != null && target != null)
                    {
                        UpdateConnectionPoints(control, source, target);
                    }
                }
            }
        }

        private void UpdateConnectionPoints(AvatarTransitionControl control, AvatarState source, AvatarState target)
        {
            double w = 150;
            double h = 80;

            // Connecter du côté droit de la source au côté gauche de la cible
            control.StartPoint = new Point(source.GraphX + w, source.GraphY + h / 2);
            control.EndPoint = new Point(target.GraphX, target.GraphY + h / 2);
        }

        #endregion

        #region Drag & Drop - Create Transition

        private void OnConnectorDragStarted(object sender, ConnectorDragEventArgs e)
        {
            if (_profile == null) return;

            _isCreatingTransition = true;
            _dragSourceState = e.State;

            // Calculer le point de départ basé sur la position du connecteur
            double w = 150;
            double h = 80;
            _dragStartPoint = e.ConnectorPosition switch
            {
                "Top" => new Point(e.State.GraphX + w / 2, e.State.GraphY),
                "Bottom" => new Point(e.State.GraphX + w / 2, e.State.GraphY + h),
                "Left" => new Point(e.State.GraphX, e.State.GraphY + h / 2),
                "Right" => new Point(e.State.GraphX + w, e.State.GraphY + h / 2),
                _ => new Point(e.State.GraphX + w, e.State.GraphY + h / 2)
            };

            // Créer une ligne temporaire
            _tempConnectionLine = new Line
            {
                X1 = _dragStartPoint.X,
                Y1 = _dragStartPoint.Y,
                X2 = _dragStartPoint.X,
                Y2 = _dragStartPoint.Y,
                Stroke = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3)),
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 4, 2 },
                IsHitTestVisible = false
            };

            ConnectionsLayer.Children.Add(_tempConnectionLine);

            // Capturer la souris au niveau du contrôle pour recevoir tous les événements
            CaptureMouse();
        }

        private void OnConnectorDragEnded(object sender, ConnectorDragEventArgs e)
        {
            if (!_isCreatingTransition || _dragSourceState == null || _profile == null)
            {
                CleanupDrag();
                return;
            }

            // Si on relâche sur un état différent, créer la transition
            if (e.State != null && e.State.Id != _dragSourceState.Id)
            {
                CreateTransition(_dragSourceState, e.State);
            }

            CleanupDrag();
        }

        private void CleanupDrag()
        {
            ReleaseMouseCapture();

            if (_tempConnectionLine != null)
            {
                ConnectionsLayer.Children.Remove(_tempConnectionLine);
                _tempConnectionLine = null;
            }

            _isCreatingTransition = false;
            _dragSourceState = null;
        }

        private void CreateTransition(AvatarState source, AvatarState target)
        {
            if (_profile == null) return;

            var trigger = ShowInputDialog("Nouvelle Transition",
                $"Creer une transition de '{source.Name}' vers '{target.Name}'.\n\nNom du trigger:",
                "NewTrigger");

            if (!string.IsNullOrWhiteSpace(trigger))
            {
                _profile.Transitions.Add(new AvatarTransition
                {
                    SourceStateId = source.Id,
                    TargetStateId = target.Id,
                    Trigger = trigger
                });
            }
        }

        #endregion

        #region Drag & Drop - Nodes

        private void OnThumbDragDelta(object sender, DragDeltaEventArgs e)
        {
            if (sender is Thumb thumb && thumb.DataContext is AvatarState state)
            {
                state.GraphX += e.HorizontalChange;
                state.GraphY += e.VerticalChange;

                if (state.GraphX < 0) state.GraphX = 0;
                if (state.GraphY < 0) state.GraphY = 0;
            }
        }

        #endregion

        #region Add/Delete States

        private void OnAddStateClick(object sender, RoutedEventArgs e)
        {
            if (_profile == null) return;

            var name = ShowInputDialog("Ajouter un Etat", "Nom du nouvel etat:", "NouvelEtat");

            if (string.IsNullOrWhiteSpace(name)) return;

            var newState = new AvatarState
            {
                Name = name,
                Description = "Description...",
                GraphX = 100 + (_profile.States.Count * 50),
                GraphY = 100 + (_profile.States.Count * 30)
            };

            _profile.States.Add(newState);

            NodesItemsControl.ItemsSource = null;
            NodesItemsControl.ItemsSource = _profile.States;
        }

        public void DeleteState(AvatarState state)
        {
            if (_profile == null) return;

            var result = MessageBox.Show(
                $"Supprimer l'etat '{state.Name}' et toutes ses transitions ?",
                "Confirmation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                var linkedTransitions = _profile.Transitions
                    .Where(t => t.SourceStateId == state.Id || t.TargetStateId == state.Id)
                    .ToList();

                foreach (var t in linkedTransitions)
                {
                    _profile.Transitions.Remove(t);
                }

                _profile.States.Remove(state);

                NodesItemsControl.ItemsSource = null;
                NodesItemsControl.ItemsSource = _profile.States;
            }
        }

        #endregion

        #region Create Transition (Menu)

        private void StartTransitionFromState(AvatarState source)
        {
            if (_profile == null) return;

            var targets = _profile.States.Where(s => s.Id != source.Id).ToList();
            if (!targets.Any())
            {
                MessageBox.Show("Aucun autre etat disponible.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var targetNames = string.Join(", ", targets.Select(t => t.Name));
            var targetName = ShowInputDialog("Nouvelle Transition",
                $"Etats disponibles: {targetNames}\n\nNom de l'etat cible:",
                targets.First().Name);

            var target = targets.FirstOrDefault(t => t.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase));
            if (target == null) return;

            CreateTransition(source, target);
        }

        #endregion

        #region Node Events

        private void OnNodeMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is AvatarState state)
            {
                DeselectTransition();
                StateSelected?.Invoke(this, state);
            }
        }

        private void OnNodeRightClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is AvatarState state)
            {
                var menu = new ContextMenu();

                var deleteItem = new MenuItem { Header = "Supprimer l'etat" };
                deleteItem.Click += (s, args) => DeleteState(state);
                menu.Items.Add(deleteItem);

                menu.Items.Add(new Separator());

                var addTransitionItem = new MenuItem { Header = "Creer une transition depuis cet etat..." };
                addTransitionItem.Click += (s, args) => StartTransitionFromState(state);
                menu.Items.Add(addTransitionItem);

                menu.IsOpen = true;
                e.Handled = true;
            }
        }

        #endregion

        #region Canvas Events

        private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
        {
            DeselectTransition();
        }

        private void OnCanvasMouseMove(object sender, MouseEventArgs e)
        {
            UpdateDragLine(e);
        }

        private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
        {
            FinalizeDrag(e);
        }

        private void OnEditorMouseMove(object sender, MouseEventArgs e)
        {
            UpdateDragLine(e);
        }

        private void OnEditorMouseUp(object sender, MouseButtonEventArgs e)
        {
            FinalizeDrag(e);
        }

        private void OnGridMouseMove(object sender, MouseEventArgs e)
        {
            UpdateDragLine(e);
        }

        private void OnGridMouseUp(object sender, MouseButtonEventArgs e)
        {
            FinalizeDrag(e);
        }

        private void UpdateDragLine(MouseEventArgs e)
        {
            if (_isCreatingTransition && _tempConnectionLine != null)
            {
                var pos = e.GetPosition(EditorGrid);
                _tempConnectionLine.X2 = pos.X;
                _tempConnectionLine.Y2 = pos.Y;
            }
        }

        private void FinalizeDrag(MouseButtonEventArgs e)
        {
            if (_isCreatingTransition && _dragSourceState != null)
            {
                // Vérifier si on est au-dessus d'un état
                var pos = e.GetPosition(EditorGrid);
                var targetState = FindStateAtPosition(pos);

                System.Diagnostics.Debug.WriteLine($"[StateMachineEditor] FinalizeDrag at ({pos.X}, {pos.Y}), target: {targetState?.Name ?? "none"}");

                if (targetState != null && targetState.Id != _dragSourceState.Id)
                {
                    CreateTransition(_dragSourceState, targetState);
                }

                CleanupDrag();
                e.Handled = true;
            }
        }

        private AvatarState? FindStateAtPosition(Point pos)
        {
            if (_profile == null) return null;

            double w = 150;
            double h = 80;

            // Ajouter une marge de tolérance
            double margin = 10;

            return _profile.States.FirstOrDefault(s =>
                pos.X >= s.GraphX - margin && pos.X <= s.GraphX + w + margin &&
                pos.Y >= s.GraphY - margin && pos.Y <= s.GraphY + h + margin);
        }

        #endregion

        #region Keyboard

        public void DeleteSelectedTransition()
        {
            if (_selectedTransition != null && _profile != null)
            {
                var result = MessageBox.Show(
                    $"Supprimer la transition '{_selectedTransition.Trigger}' ?",
                    "Confirmation",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    _profile.Transitions.Remove(_selectedTransition);
                    DeselectTransition();
                }
            }
        }

        #endregion

        #region Helpers

        private string? ShowInputDialog(string title, string prompt, string defaultValue = "")
        {
            var dialog = new Window
            {
                Title = title,
                Width = 400,
                SizeToContent = SizeToContent.Height,
                MinHeight = 150,
                MaxHeight = 300,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                ResizeMode = ResizeMode.NoResize
            };

            var grid = new Grid { Margin = new Thickness(15) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new TextBlock
            {
                Text = prompt,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(label, 0);

            var textBox = new TextBox
            {
                Text = defaultValue,
                Margin = new Thickness(0, 0, 0, 15),
                Padding = new Thickness(5)
            };
            Grid.SetRow(textBox, 1);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetRow(buttonPanel, 2);

            var okButton = new Button
            {
                Content = "OK",
                Width = 75,
                Height = 28,
                Margin = new Thickness(0, 0, 10, 0),
                IsDefault = true
            };
            okButton.Click += (s, e) => { dialog.DialogResult = true; };

            var cancelButton = new Button
            {
                Content = "Annuler",
                Width = 75,
                Height = 28,
                IsCancel = true
            };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);

            grid.Children.Add(label);
            grid.Children.Add(textBox);
            grid.Children.Add(buttonPanel);

            dialog.Content = grid;
            textBox.Focus();
            textBox.SelectAll();

            if (dialog.ShowDialog() == true)
            {
                return textBox.Text;
            }
            return null;
        }

        #endregion
    }
}
