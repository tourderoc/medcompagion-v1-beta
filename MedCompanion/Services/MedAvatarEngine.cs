using System;
using System.Linq;
using MedCompanion.Models.StateMachine;

namespace MedCompanion.Services
{
    public class MedAvatarEngine
    {
        private StateMachineProfile? _currentProfile;
        private AvatarState? _currentState;

        public event EventHandler<AvatarState>? StateChanged;
        public event EventHandler<string>? MediaChanged; // Fired when media needs to change (path)

        public AvatarState? CurrentState => _currentState;

        public bool IsRunning => _currentProfile != null;

        public void LoadProfile(StateMachineProfile profile)
        {
            _currentProfile = profile;
            
            // Find initial state
            if (_currentProfile.InitialStateId != Guid.Empty)
            {
                var initial = _currentProfile.States.FirstOrDefault(s => s.Id == _currentProfile.InitialStateId);
                if (initial != null)
                {
                    SetState(initial);
                }
            }
            else if (_currentProfile.States.Any())
            {
                // Fallback to first state
                SetState(_currentProfile.States.First());
            }
        }

        public void FireTrigger(string triggerName)
        {
            if (_currentProfile == null || _currentState == null) return;

            System.Diagnostics.Debug.WriteLine($"[MedAvatarEngine] Firing Trigger: {triggerName}");

            // Find valid transition
            var transition = _currentProfile.Transitions
                .FirstOrDefault(t => t.SourceStateId == _currentState.Id && 
                                     t.Trigger.Equals(triggerName, StringComparison.OrdinalIgnoreCase));

            if (transition != null)
            {
                // Check condition (Future: Implement dynamic expression evaluator)
                if (!string.IsNullOrEmpty(transition.Condition))
                {
                    // For now, ignore conditions or implement simple check
                    // if (!Evaluate(transition.Condition)) return;
                }

                var targetState = _currentProfile.States.FirstOrDefault(s => s.Id == transition.TargetStateId);
                if (targetState != null)
                {
                    SetState(targetState);
                }
            }
            else
            {
                 System.Diagnostics.Debug.WriteLine($"[MedAvatarEngine] No transition found for trigger '{triggerName}' from state '{_currentState.Name}'");
            }
        }

        /// <summary>
        /// Force le passage à un état spécifique (sans transition)
        /// </summary>
        public void ForceState(AvatarState state)
        {
            if (_currentProfile == null) return;
            if (!_currentProfile.States.Contains(state)) return;

            SetState(state);
        }

        private void SetState(AvatarState newState)
        {
            if (_currentState == newState) return;

            System.Diagnostics.Debug.WriteLine($"[MedAvatarEngine] State Change: {_currentState?.Name ?? "null"} -> {newState.Name}");

            _currentState = newState;
            StateChanged?.Invoke(this, newState);

            // Start media
            if (newState.MediaSequence.Any())
            {
                var media = newState.MediaSequence.First();
                if (media.FileExists)
                {
                    MediaChanged?.Invoke(this, media.FilePath);
                }
            }
        }
    }
}
