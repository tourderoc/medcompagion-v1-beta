# Bilan — Juin 2026

> **Date :** 25 juin 2026
> **Statut :** Point de pivot — Med V2 pausé, focus merge Parent'aile

---

## Ce qu'on a fait (session 29 juin 2026 — MERGE DAY)

### Merge Parent'aile complet ✅

**7 étapes exécutées de 9h à 16h (heure locale)**

#### Bugs rencontrés et corrigés

| Bug | Cause | Fix |
|-----|-------|-----|
| Création compte → "Une erreur est survenue" | Double appel React → POST 409 | `accountStorage.ts` : fallback GET sur 409 |
| Statut token "En attente" dans MedCompanion | Pas de recompilation après merge | `dotnet build` relancé |
| Réponse médecin invisible côté parent | `.env` gitignored → Netlify gardait `USE_FIREBASE=true` | Création `.env.production` avec `VITE_FIREBASE_BRIDGE=false` |
| Token sync VPS : 2 "used" au lieu de 251 | `ON CONFLICT DO NOTHING` initial + dual-write jamais sur `main` | SQL UPSERT généré depuis Firestore → 317 tokens synchés |

#### Déployé

- `cc52cdd` — fix(bridge): fallback GET sur 409 création compte  
- `5ca517f` — fix(bridge): `.env.production` coupe Firebase en prod

#### Backup Google Drive configuré

- `rclone` installé sur VPS → remote `gdrive:` authentifié avec `nairmedcin@gmail.com`
- Script `/root/backup_to_drive.sh` : dump PostgreSQL + `.env` → `gdrive:Backups/ParentAile/YYYY-MM-DD_HH-MM/`
- Cron : tous les jours à **2h00**

#### Smoke tests validés

| Test | Résultat |
|------|----------|
| Création compte | ✅ |
| Token créé MedCompanion → Pilotage | ✅ |
| Token activé Parent'aile → statut Actif | ✅ |
| Message parent → visible MedCompanion | ✅ |
| Réponse médecin → visible parent | ✅ |
| Notification push reçue | ✅ |
| Logs VPS sans erreur | ✅ (100% 200 OK) |

#### État final VPS

- 248 tokens actifs, 49 en attente, 0 révoqué
- 25 messages (24 replied, 1 unread)
- 190 comptes Parent'aile (self-populating au login)

---

## Ce qu'on a fait (session 24-25 juin 2026)

### MedCompanion — Fixes dossier restitution (`10a9956`)

- **PDF persisté** : `GeneratedPdfPath` sauvegardé dans le `.md` après export (auparavant perdu au prochain chargement)
- **Hub refresh automatique** : event `PdfExported` déclenche `RestitutionsHub.LoadForPatientAsync` sur le thread UI
- **Dossier bleu** : section "Dossiers de Restitution" ajoutée dans le panel DOCUMENTS (côté droit, single-page + fullscreen F3) avec bouton PDF direct
- **Bouton vestigial supprimé** : Sauvegarder caché en mode idle et lecture (default `Collapsed`)

### Parent'aile — MERGE_PLAN.md mis à jour (`564d5a4` sur branche dev)

- Date du merge clarifiée : **dimanche 29 juin 2026, 9h au cabinet**
- Section "Vendredi 27 juin" ajoutée : broadcast utilisateurs depuis MedCompanion Pilotage

---

## Objectifs immédiats

| Date | Action |
|------|--------|
| **Vendredi 27 juin** | Broadcast depuis MedCompanion Pilotage → Utilisateurs → annoncer groupe de parole + mise à jour dimanche |
| **Dimanche 29 juin 9h** | ✅ Merge exécuté — VPS source de vérité |
| **Semaine du 30 juin** | Cleanup post-merge : retirer code `@FIREBASE_LEGACY` + dual-write Firebase MedCompanion |
| **Post-merge (optionnel)** | Migrer ~60 comptes Firestore manquants dans VPS accounts |

---

## Point par rapport à VISION_V2.md (Med V2)

| Fonctionnalité | Statut |
|----------------|--------|
| Toggle Console / Focus Med | ✅ Implémenté |
| Interface Focus Med (3 colonnes) | ✅ Implémenté |
| Voice STT (Handy) + TTS (Piper) | ✅ Implémenté |
| Dossiers de restitution | ✅ Implémenté (juin 2026) |
| **Mode Consultation** (dossier adaptatif F1/F2/F3) | 🔄 Conçu, **pausé** |
| **Mémoire de Med** (6 blocs contrôlés) | 🔄 Conçu, **pausé** |

**Décision 24/06/2026 :** Mode Consultation et Mémoire de Med sont bien spécifiés dans VISION_V2.md mais ne démarrent pas avant la stabilisation du merge Parent'aile. Reprendre en juillet 2026 au plus tôt.

---

## Point par rapport à VISION_V3.md (Écosystème)

| Brique | Statut |
|--------|--------|
| B1. Mode Pilotage squelette (MedCompanion) | ✅ Fait |
| B2. Gestion tokens + QR code (MedCompanion) | ✅ Fait |
| B3. Griser fonctionnalités Parent'aile | ✅ Fait (branche dev) |
| B4. Espace Patient — connexion token | ✅ Fait (branche dev) |
| B5. Espace Patient — messages | ✅ Fait (branche dev) |
| B6. Inbox messages MedCompanion | ✅ Fait |
| B7. Réponse par email | ✅ Fait |
| B8. IA tri/réponse assistée | 🔄 Partiel |
| Groupe de parole LiveKit | ✅ Fait (branche dev, ~4200 lignes) |
| **Migration Firebase → VPS** | ✅ **Fait (29 juin 2026)** |

---

## Documents liés

- [VISION_V2.md](VISION_V2.md) — Vision MedCompanion V2 (Focus Med, Mémoire, Mode Consultation)
- [VISION_V3.md](VISION_V3.md) — Vision écosystème Parent'aile + MedCompanion
- `MERGE_PLAN.md` dans le repo Parent'aile (branche dev)
