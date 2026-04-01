using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Input;

namespace MedCompanion.Services.Voice
{
    /// <summary>
    /// Service d'intégration avec Handy (STT local)
    /// Simule le raccourci clavier configuré dans Handy pour démarrer/arrêter l'enregistrement
    /// </summary>
    public class HandyVoiceInputService : IVoiceInputService
    {
        #region P/Invoke pour simulation clavier

        [DllImport("user32.dll", SetLastError = true)]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const uint KEYEVENTF_KEYDOWN = 0x0000;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        // Codes des touches virtuelles
        private const byte VK_CONTROL = 0x11;
        private const byte VK_SHIFT = 0x10;
        private const byte VK_ALT = 0x12;

        #endregion

        private bool _isRecording;
        private string _hotkey = "Ctrl+Shift+H";

        public bool IsRecording => _isRecording;

        public bool IsAvailable => true; // On suppose que Handy est installé, pas de vérification automatique

        public string Hotkey
        {
            get => _hotkey;
            set => _hotkey = value ?? "Ctrl+Shift+H";
        }

        public event EventHandler? RecordingStarted;
        public event EventHandler? RecordingStopped;

        public async Task StartRecordingAsync()
        {
            if (_isRecording) return;

            await SimulateHotkeyAsync();
            _isRecording = true;
            RecordingStarted?.Invoke(this, EventArgs.Empty);
        }

        public async Task StopRecordingAsync()
        {
            if (!_isRecording) return;

            await SimulateHotkeyAsync();
            _isRecording = false;
            RecordingStopped?.Invoke(this, EventArgs.Empty);
        }

        public async Task ToggleRecordingAsync()
        {
            if (_isRecording)
                await StopRecordingAsync();
            else
                await StartRecordingAsync();
        }

        /// <summary>
        /// Simule la combinaison de touches configurée
        /// </summary>
        private async Task SimulateHotkeyAsync()
        {
            var (modifiers, key) = ParseHotkey(_hotkey);

            // Appuyer sur les modificateurs
            foreach (var modifier in modifiers)
            {
                keybd_event(modifier, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
            }

            // Petit délai
            await Task.Delay(50);

            // Appuyer sur la touche principale
            keybd_event(key, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
            await Task.Delay(50);
            keybd_event(key, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

            // Relâcher les modificateurs (dans l'ordre inverse)
            for (int i = modifiers.Length - 1; i >= 0; i--)
            {
                keybd_event(modifiers[i], 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            }
        }

        /// <summary>
        /// Parse un raccourci clavier (ex: "Ctrl+Shift+H") en codes de touches virtuelles
        /// </summary>
        private (byte[] modifiers, byte key) ParseHotkey(string hotkey)
        {
            var parts = hotkey.Split('+');
            var modifiers = new System.Collections.Generic.List<byte>();
            byte mainKey = 0;

            foreach (var part in parts)
            {
                var trimmed = part.Trim().ToLower();
                switch (trimmed)
                {
                    case "ctrl":
                    case "control":
                        modifiers.Add(VK_CONTROL);
                        break;
                    case "shift":
                        modifiers.Add(VK_SHIFT);
                        break;
                    case "alt":
                        modifiers.Add(VK_ALT);
                        break;
                    default:
                        // C'est la touche principale
                        mainKey = GetVirtualKeyCode(trimmed);
                        break;
                }
            }

            return (modifiers.ToArray(), mainKey);
        }

        /// <summary>
        /// Convertit une lettre/touche en code virtuel
        /// </summary>
        private byte GetVirtualKeyCode(string key)
        {
            if (string.IsNullOrEmpty(key)) return 0;

            // Lettres A-Z (0x41-0x5A)
            if (key.Length == 1 && char.IsLetter(key[0]))
            {
                return (byte)char.ToUpper(key[0]);
            }

            // Chiffres 0-9 (0x30-0x39)
            if (key.Length == 1 && char.IsDigit(key[0]))
            {
                return (byte)key[0];
            }

            // Touches spéciales
            return key.ToLower() switch
            {
                "space" => 0x20,
                "enter" => 0x0D,
                "tab" => 0x09,
                "escape" or "esc" => 0x1B,
                "f1" => 0x70,
                "f2" => 0x71,
                "f3" => 0x72,
                "f4" => 0x73,
                "f5" => 0x74,
                "f6" => 0x75,
                "f7" => 0x76,
                "f8" => 0x77,
                "f9" => 0x78,
                "f10" => 0x79,
                "f11" => 0x7A,
                "f12" => 0x7B,
                _ => 0
            };
        }
    }
}
