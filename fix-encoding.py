# -*- coding: utf-8 -*-
import codecs

# Lire le fichier
with open('MedCompanion/MainWindow.Patient.cs', 'r', encoding='utf-8-sig') as f:
    content = f.read()

# Table de correspondance des caractères mal encodés
replacements = {
    'èƒÂ©': 'é',
    'èƒÂ¨': 'è',
    'èƒÂ ': 'à',
    'èƒÂª': 'ê',
    'èƒÂ»': 'û',
    'èƒÂ´': 'ô',
    'èƒÂ®': 'î',
    'èƒÂ¯': 'ï',
    'èƒÂ§': 'ç',
    'èƒâ€°': 'É',
    'èƒË†': 'Ê',
    'èƒÅ ': 'Ê',
    'è¢â€ â€™': '→',
    'è¢Å"â€œ': '✓',
    'è¢Å"â€¦': '✅',
    'è¢ÂÅ'': '❌',
    'è¢Å¡Â è¯Â¸Â': '⚠️',
    'è¢ÂÂ³': '⏳',
    'è¢Ââ€œ': '❓',
    'è¢Å"Âè¯Â¸Â': '✏️',
    'è¢â€Â': '─',
    'è¢â‚¬Â¢': '•',
    'è°Å¸â€œâ€¹': '📋',
    'è°Å¸â€œÂ': '📁',
    'è°Å¸â€™Â¾': '💾',
    'è°Å¸â€œâ€"': '📖',
    'è°Å¸â€œâ€ž': '📄',
    'è°Å¸â€œâ€¦': '📅',
    'è°Å¸â€œÅ½': '🔎',
}

# Appliquer les remplacements
for old, new in replacements.items():
    content = content.replace(old, new)

# Sauvegarder avec UTF-8 BOM
with codecs.open('MedCompanion/MainWindow.Patient.cs', 'w', encoding='utf-8-sig') as f:
    f.write(content)

print("Fichier corrigé avec succès!")
print(f"Fichier sauvegardé en UTF-8 avec BOM")
