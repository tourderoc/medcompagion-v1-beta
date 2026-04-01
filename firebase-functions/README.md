# Firebase Cloud Functions - Parent'aile

## Fonctions disponibles

### `onNotificationCreated`
- **Trigger**: Création d'un document dans `/notifications/{notificationId}`
- **Action**: Envoie une push notification FCM au parent concerné
- **Types supportés**: EmailReply, Quick, Info, Broadcast

### `testNotification` (HTTP)
- **URL**: `POST https://<region>-<project>.cloudfunctions.net/testNotification`
- **Body**: `{ "tokenId": "xxx", "title": "Test", "body": "Message" }`
- **Usage**: Tester l'envoi de notifications

## Prérequis

1. **Firebase CLI** installé:
   ```bash
   npm install -g firebase-tools
   ```

2. **Authentification**:
   ```bash
   firebase login
   ```

3. **Projet Firebase** avec:
   - Firestore activé
   - Cloud Messaging activé
   - Cloud Functions activé (plan Blaze requis)

## Déploiement

```bash
cd firebase-functions
npm install
firebase use <votre-project-id>
firebase deploy --only functions
```

## Structure Firestore requise

### Collection `tokens`
```
tokens/{tokenId}
  - status: "used" | "available"
  - fcmToken: string (token FCM du device)
  - childNickname: string
  - parentEmail: string
  - usedAt: timestamp
```

### Collection `notifications`
```
notifications/{notificationId}
  - type: "EmailReply" | "Quick" | "Info" | "Broadcast"
  - title: string
  - body: string
  - targetParentId: string ("all" pour broadcast)
  - tokenId: string (ID du token parent)
  - replyToMessageId: string (optionnel)
  - senderName: string
  - createdAt: timestamp
  - read: boolean
```

## Logs

Voir les logs en temps réel:
```bash
firebase functions:log --only onNotificationCreated
```

## Côté React Native (Parent'aile)

L'app doit:
1. Demander la permission notifications
2. Récupérer le FCM token
3. L'enregistrer dans Firestore lors de l'activation du token

```javascript
import messaging from '@react-native-firebase/messaging';

// Récupérer le token FCM
const fcmToken = await messaging().getToken();

// L'enregistrer dans Firestore
await firestore()
  .collection('tokens')
  .doc(tokenId)
  .update({ fcmToken: fcmToken });
```
