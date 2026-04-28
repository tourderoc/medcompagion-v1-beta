# Plan : Pilotage Utilisateurs V2 — séparation clinique / admin plateforme

> **À démarrer après le merge VPS stabilisé (≥1 semaine sans incident).**
> Voir [parentaile-v0/MERGE_PLAN.md](../parentaile-v0/MERGE_PLAN.md).
> Phase 0 (VPS) peut commencer en parallèle pendant la stabilisation post-merge.

---

## Contexte

Aujourd'hui l'écran **Pilotage > Tokens Parent'aile** ([PilotageControl.xaml.cs](MedCompanion/Views/Pilotage/PilotageControl.xaml.cs), 1209 lignes) est centré sur les tokens : un parent n'apparaît que s'il a un token lié à un patient suivi au cabinet.

Avec la migration VPS, la table `accounts` (PostgreSQL `account_db`) est désormais **source de vérité** pour tous les utilisateurs Parent'aile, y compris ceux **sans aucun lien cabinet** (forum vocal, mur, groupes de parole).

Le pilotage doit refléter cette réalité : l'utilisateur devient l'entité centrale, le token devient une propriété optionnelle.

---

## Décision : option C — séparation des deux espaces

Pour éviter la confusion entre **rôle médecin** (suivi patient) et **rôle admin plateforme** (modération communauté), MedCompanion expose deux vues distinctes :

### 1. Patients tokenisés (vue clinique)
- Reprend l'actuelle logique "Tokens Parent'aile"
- Renommée pour clarifier : famille suivie au cabinet, messagerie médicale
- Inchangée fonctionnellement
- Reste l'outil quotidien du psychiatre

### 2. Communauté Parent'aile (vue admin)
- **Nouvel onglet**, visuellement distinct (badge "Admin Plateforme")
- Liste tous les comptes VPS, avec ou sans token
- Stats, filtres, actions de modération
- Outil ponctuel d'administration de la plateforme

> **Pourquoi pas fusionner ?** Mélanger les deux risque de créer des confusions de rôle (un patient et un membre du forum n'ont pas le même statut), et complique la perspective où un autre médecin utiliserait MedCompanion.

---

## Décisions validées (cadrage V1)

- ❌ Pas de suppression de compte → seulement **blocage**
- ✅ Stats simples : Total / Actifs 7j / Nouveaux 7j / Bloqués
- ✅ Un seul graphique : inscriptions sur 30j
- ✅ Toute action critique : confirmation + audit log
- ✅ Petits commits successifs, pas de gros bang
- ✅ Phase 0 (VPS) peut démarrer en parallèle de la stabilisation post-merge

---

## Phase 0 — Préparation côté VPS (1-2j)

> **Démarrable en parallèle du merge.** Aucun impact UI utilisateur.

### 0.1 — Migration schéma `accounts` (parentaile-vps)

```sql
ALTER TABLE accounts ADD COLUMN status TEXT NOT NULL DEFAULT 'active';
ALTER TABLE accounts ADD COLUMN last_login_at TIMESTAMPTZ;
ALTER TABLE accounts ADD COLUMN blocked_at TIMESTAMPTZ;
ALTER TABLE accounts ADD COLUMN blocked_reason TEXT;
CREATE INDEX idx_accounts_status ON accounts(status);
CREATE INDEX idx_accounts_last_login ON accounts(last_login_at DESC);
```

**Statuts dérivés (pas de colonne dédiée) :**
- `actif` : `last_login_at >= NOW() - INTERVAL '7 days'`
- `inactif` : `last_login_at < NOW() - INTERVAL '60 days'`
- `en attente` : `last_login_at IS NULL` (compte créé mais jamais connecté)
- `bloqué` : `status = 'blocked'`

### 0.2 — Tracking last_login_at (Parent'aile React)

Une ligne dans `userContext.tsx` au login : `PUT /accounts/{uid}` avec `last_login_at = NOW()`.
Pas besoin de l'envoyer à chaque navigation, juste à l'authentification réussie.

### 0.3 — Table d'audit

```sql
CREATE TABLE admin_audit_log (
  id            SERIAL PRIMARY KEY,
  actor_email   TEXT NOT NULL,           -- qui a fait l'action (admin)
  action        TEXT NOT NULL,           -- 'block', 'unblock', 'associate_token', etc.
  target_uid    TEXT,                    -- compte concerné
  target_token  TEXT,                    -- token concerné (si applicable)
  reason        TEXT,                    -- raison libre
  metadata      JSONB,                   -- payload libre (avant/après)
  created_at    TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX idx_audit_actor ON admin_audit_log(actor_email, created_at DESC);
CREATE INDEX idx_audit_target ON admin_audit_log(target_uid, created_at DESC);
```

### 0.4 — Nouveaux endpoints VPS (préfixe `/accounts/admin/*`)

> Tous protégés par `X-Api-Key` + check email admin (header `X-Admin-Email` whitelist).

```
GET  /accounts/admin/list
       ?status=active|inactive|pending|blocked
       &has_token=true|false
       &search=<pseudo|email>
       &limit=50&offset=0
     → liste paginée avec colonnes : uid, pseudo, email, created_at,
       last_login_at, status, has_token, token_count

GET  /accounts/admin/stats
     → { total, active_7d, new_7d, new_30d, with_token, without_token,
         pending, blocked }

GET  /accounts/admin/timeseries?metric=signups&days=30
     → [ { date: '2026-04-01', count: 3 }, ... ]

GET  /accounts/admin/{uid}/detail
     → compte complet + tokens liés + counts
       (messages_sent, groupes_created, groupes_joined, signalements_recus)

PUT  /accounts/admin/{uid}/block
     Body : { reason: string, actor_email: string }
     → status = 'blocked', blocked_at = NOW(), audit log

PUT  /accounts/admin/{uid}/unblock
     Body : { actor_email: string }
     → status = 'active', blocked_at = NULL, audit log

GET  /accounts/admin/{uid}/audit
     → historique des actions admin sur ce compte
```

### 0.5 — Tests Phase 0

- [ ] Migration appliquée sur VPS sans casser les endpoints existants
- [ ] `last_login_at` se met à jour au login Parent'aile (test e2e)
- [ ] Endpoints `/accounts/admin/*` répondent avec auth admin
- [ ] Endpoints rejettent sans auth admin
- [ ] Audit log écrit correctement sur `block`/`unblock`

---

## Phase 1 — Renommage + coquille onglet Communauté (0.5j)

Dans MedCompanion :

- Renommer "Tokens Parent'aile" → **"Patients tokenisés"** (vue clinique inchangée)
- Ajouter un nouvel onglet **"Communauté Parent'aile"** dans PilotageControl
- Badge visuel "Admin Plateforme" sur le header de l'onglet (couleur distincte)
- Onglet vide pour l'instant — juste la coquille XAML + code-behind minimal
- Service C# `VpsAdminService.cs` : wrapper HttpClient sur `/accounts/admin/*`

**Commit attendu** : `feat(pilotage): coquille onglet Communauté Parent'aile`

---

## Phase 2 — Liste + filtres (1-2j)

- DataGrid alimenté par `GET /accounts/admin/list`
- Colonnes : Pseudo, Email, Créé le, Dernier login, Statut (badge couleur), Token (oui/non)
- Recherche en haut (debounce 300ms) → param `search`
- Filtres en haut (segmented buttons) :
  - Tous / Avec token / Sans token / Actifs / Inactifs / En attente / Bloqués
- Pagination : 50 par page, boutons précédent/suivant
- Refresh manuel + auto toutes les 5 min

**Commit attendu** : `feat(pilotage): liste utilisateurs VPS avec filtres et recherche`

---

## Phase 3 — Bandeau stats (0.5j)

4 cartes en haut de l'onglet, alimentées par `GET /accounts/admin/stats` :

| Carte | Valeur |
|---|---|
| Total | nombre total de comptes |
| Actifs 7j | comptes avec `last_login_at >= NOW() - 7 days` |
| Nouveaux 7j | comptes avec `created_at >= NOW() - 7 days` |
| Bloqués | comptes avec `status = 'blocked'` |

Refresh manuel + auto toutes les 5 min (même tick que la liste).

**Commit attendu** : `feat(pilotage): bandeau stats utilisateurs Parent'aile`

---

## Phase 4 — Fiche détaillée (1-2j)

Click sur une ligne → panneau latéral (ou Dialog) alimenté par `GET /accounts/admin/{uid}/detail` :

### Sections
- **Identité** : pseudo, email, UID, créé le, dernier login
- **Statut compte** : badge actif/inactif/en attente/bloqué + raison si bloqué
- **Lien cabinet** : token associé (oui/non) + statut + patient lié si dispo
- **Activité Parent'aile** :
  - Messages envoyés (count)
  - Groupes créés / rejoints (count)
  - Participation au mur (count)
  - Signalements reçus (count)
- **Actions** (boutons en bas avec niveaux de criticité visuels) :
  - 🔵 Notifier (envoie une notification VPS — réutilise infra existante)
  - 🔵 Voir historique (onglet audit)
  - 🟡 Associer un token (modal — réutilise UI existante)
  - 🟡 Révoquer le token (confirmation simple)
  - 🔴 Bloquer (confirmation avec champ "raison" obligatoire)
  - 🔴 Débloquer (confirmation simple)

### Règles
- Action critique = confirmation explicite + écriture dans `admin_audit_log`
- L'email de l'admin connecté à MedCompanion est passé dans `actor_email`
- Pas de suppression compte en V1

**Commit attendu** : `feat(pilotage): fiche détaillée utilisateur + actions admin`

---

## Phase 5 — Graphique inscriptions 30j (1j)

- Une seule courbe : inscriptions par jour sur 30 jours
- Source : `GET /accounts/admin/timeseries?metric=signups&days=30`
- Lib WPF : **LiveCharts2** (gratuit, simple, peu de deps)
- Affiché sous le bandeau stats, replié par défaut (toggle "Afficher le graphique")

**Commit attendu** : `feat(pilotage): graphique inscriptions 30 jours`

---

## Phase 6 — Audit & historique (0.5j)

- Onglet "Historique" dans la fiche détaillée → liste des actions admin sur ce compte
- Source : `GET /accounts/admin/{uid}/audit`
- Vue simple : date, action, acteur, raison

**Commit attendu** : `feat(pilotage): historique actions admin par utilisateur`

---

## Hors scope V1 (à réévaluer plus tard)

- ❌ Suppression de compte (RGPD complexe : cascade groupes/messages, droit à l'oubli)
- ❌ Statistiques avancées (cohortes, rétention, funnel)
- ❌ 2 graphiques supplémentaires (utilisateurs actifs, activation tokens) → si besoin réel
- ❌ Système de rôles admin (un seul admin pour l'instant : nairmedcin@gmail.com)
- ❌ Notifications push aux admins (alertes signalements en temps réel)
- ❌ Export CSV/Excel de la liste
- ❌ Refonte XAML profonde de PilotageControl.xaml.cs (1209 lignes) → nouveau onglet vit à côté

---

## Risques identifiés

| Risque | Mitigation |
|---|---|
| Confusion rôle médecin/admin | Séparation visuelle nette + badge "Admin Plateforme" |
| Latence chargement liste à 1000+ users | Pagination 50 + index PostgreSQL sur `status` et `last_login_at` |
| Action destructive accidentelle | Confirmations explicites + audit log + pas de suppression V1 |
| `last_login_at` non tracké rétroactivement | Comptes existants apparaissent "en attente" jusqu'à leur prochain login — comportement acceptable |
| Endpoint admin exposé sans auth forte | `X-Api-Key` + whitelist email admin obligatoire — à durcir si plusieurs admins un jour |

---

## Effort total estimé

| Phase | Effort | Cumul |
|---|---|---|
| 0 — VPS prep | 1-2j | 1-2j |
| 1 — Coquille onglet | 0.5j | 1.5-2.5j |
| 2 — Liste + filtres | 1-2j | 2.5-4.5j |
| 3 — Bandeau stats | 0.5j | 3-5j |
| 4 — Fiche détaillée | 1-2j | 4-7j |
| 5 — Graphique | 1j | 5-8j |
| 6 — Audit | 0.5j | 5.5-8.5j |

**Total : ~6-9 jours de dev, étalés sur plusieurs semaines avec petits commits.**

---

## Question ouverte (à trancher au moment de coder)

- **Auth admin côté MedCompanion** : aujourd'hui MedCompanion ne sait pas "qui est connecté" (c'est un desktop app local). Il faut décider comment l'`actor_email` est renseigné. Option simple : config locale `admin_email` dans `appsettings.json`, lue au démarrage. Suffisant tant qu'il n'y a qu'un admin.

- **Refresh temps réel** : les stats et la liste se mettent à jour toutes les 5 min. Pas besoin de SSE/WebSocket pour ça (cf [parentaile-v0/PLAN_POST_MERGE_SSE.md](../parentaile-v0/PLAN_POST_MERGE_SSE.md) — seul `session_state` justifie du temps réel).
