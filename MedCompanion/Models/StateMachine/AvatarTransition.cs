using System;

namespace MedCompanion.Models.StateMachine
{
    public class AvatarTransition
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// The ID of the state where this transition starts.
        /// </summary>
        public Guid SourceStateId { get; set; }

        /// <summary>
        /// The ID of the state where this transition leads.
        /// </summary>
        public Guid TargetStateId { get; set; }

        /// <summary>
        /// The event name that triggers this transition (e.g., "StartSpeaking", "OnMediaEnd").
        /// </summary>
        public string Trigger { get; set; } = "";

        /// <summary>
        /// Optional condition expression (e.g., "Context == 'Consultation'").
        /// Currently stored as a string, evaluated by the engine.
        /// </summary>
        public string? Condition { get; set; }
    }
}
