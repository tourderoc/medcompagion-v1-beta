using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MedCompanion.Models.StateMachine
{
    public class StateMachineProfile
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string Name { get; set; } = "Default Profile";

        public ObservableCollection<AvatarState> States { get; set; } = new();

        public ObservableCollection<AvatarTransition> Transitions { get; set; } = new();

        public Guid InitialStateId { get; set; }

        /// <summary>
        /// Saves the profile to a JSON file.
        /// </summary>
        public void Save(string path)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() }
            };
            var json = JsonSerializer.Serialize(this, options);
            
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(path, json);
        }

        /// <summary>
        /// Loads a profile from a JSON file.
        /// </summary>
        public static StateMachineProfile? Load(string path)
        {
            if (!File.Exists(path)) return null;

            try
            {
                var json = File.ReadAllText(path);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    Converters = { new JsonStringEnumConverter() }
                };
                return JsonSerializer.Deserialize<StateMachineProfile>(json, options);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StateMachineProfile] Error loading: {ex.Message}");
                return null;
            }
        }
    }
}
