/**
 * Firebase Cloud Functions pour Parent'aile
 *
 * Fonctions:
 * 1. onNotificationCreated - Envoie une push notification FCM quand une notification est créée
 *
 * Déploiement:
 *   cd firebase-functions
 *   firebase deploy --only functions
 */

const functions = require("firebase-functions");
const admin = require("firebase-admin");

admin.initializeApp();

const db = admin.firestore();
const messaging = admin.messaging();

/**
 * Trigger: Quand une nouvelle notification est créée dans /notifications/{notificationId}
 * Action: Envoie une push notification FCM au(x) parent(s) concerné(s)
 */
exports.onNotificationCreated = functions.firestore
    .document("notifications/{notificationId}")
    .onCreate(async (snapshot, context) => {
        const notification = snapshot.data();
        const notificationId = context.params.notificationId;

        console.log(`[onNotificationCreated] Nouvelle notification: ${notificationId}`);
        console.log(`[onNotificationCreated] Type: ${notification.type}, Target: ${notification.targetParentId}`);

        try {
            // Récupérer le(s) token(s) FCM à notifier
            const fcmTokens = await getFcmTokensForNotification(notification);

            if (fcmTokens.length === 0) {
                console.log("[onNotificationCreated] Aucun token FCM trouvé");
                return null;
            }

            console.log(`[onNotificationCreated] ${fcmTokens.length} token(s) FCM à notifier`);

            // Construire le message FCM
            const message = buildFcmMessage(notification, fcmTokens);

            // Envoyer la notification
            const response = await messaging.sendEachForMulticast(message);

            console.log(`[onNotificationCreated] Envoyé: ${response.successCount} succès, ${response.failureCount} échecs`);

            // Nettoyer les tokens invalides
            await cleanupInvalidTokens(fcmTokens, response);

            return { success: true, sent: response.successCount, failed: response.failureCount };

        } catch (error) {
            console.error("[onNotificationCreated] Erreur:", error);
            return { success: false, error: error.message };
        }
    });

/**
 * Récupère les tokens FCM pour une notification
 * - Pour un broadcast: tous les tokens actifs
 * - Pour une notification ciblée: le token du parent spécifique
 */
async function getFcmTokensForNotification(notification) {
    const fcmTokens = [];

    if (notification.type === "Broadcast" || notification.targetParentId === "all") {
        // Broadcast: récupérer tous les tokens actifs
        const tokensSnapshot = await db.collection("tokens")
            .where("status", "==", "used")
            .get();

        tokensSnapshot.forEach(doc => {
            const data = doc.data();
            if (data.fcmToken) {
                fcmTokens.push({
                    tokenId: doc.id,
                    fcmToken: data.fcmToken
                });
            }
        });
    } else if (notification.tokenId) {
        // Notification ciblée: récupérer le token spécifique
        const tokenDoc = await db.collection("tokens").doc(notification.tokenId).get();

        if (tokenDoc.exists) {
            const data = tokenDoc.data();
            if (data.fcmToken) {
                fcmTokens.push({
                    tokenId: tokenDoc.id,
                    fcmToken: data.fcmToken
                });
            }
        }
    }

    return fcmTokens;
}

/**
 * Construit le message FCM à envoyer
 */
function buildFcmMessage(notification, fcmTokens) {
    // Extraire uniquement les tokens FCM
    const tokens = fcmTokens.map(t => t.fcmToken);

    return {
        tokens: tokens,
        // PAS de bloc "notification" -> force le passage par le Service Worker (data-only message)
        // C'est indispensable pour le support PWA fiable et les badges
        data: {
            notificationId: notification.id || "",
            title: notification.title || "Nouveau message",
            body: notification.body || "",
            type: notification.type || "Info",
            replyToMessageId: notification.replyToMessageId || "",
            senderName: notification.senderName || "",
            badgeCount: "1" // On pourrait incrémenter dynamiquement si on stockait le compteur
        },
        android: {
            priority: "high"
        },
        apns: {
            payload: {
                aps: {
                    badge: 1,
                    sound: "default",
                    "content-available": 1
                }
            }
        }
    };
}

/**
 * Supprime les tokens FCM invalides de Firestore
 */
async function cleanupInvalidTokens(fcmTokens, response) {
    const invalidTokens = [];

    response.responses.forEach((resp, idx) => {
        if (!resp.success) {
            const errorCode = resp.error?.code;
            // Tokens invalides ou expirés
            if (errorCode === "messaging/invalid-registration-token" ||
                errorCode === "messaging/registration-token-not-registered") {
                invalidTokens.push(fcmTokens[idx]);
            }
        }
    });

    if (invalidTokens.length > 0) {
        console.log(`[cleanupInvalidTokens] ${invalidTokens.length} token(s) invalide(s) à nettoyer`);

        const batch = db.batch();
        invalidTokens.forEach(token => {
            const ref = db.collection("tokens").doc(token.tokenId);
            batch.update(ref, { fcmToken: admin.firestore.FieldValue.delete() });
        });
        await batch.commit();
    }
}

/**
 * Fonction HTTP pour tester l'envoi de notifications (optionnel)
 * URL: https://<region>-<project>.cloudfunctions.net/testNotification
 */
exports.testNotification = functions.https.onRequest(async (req, res) => {
    if (req.method !== "POST") {
        res.status(405).send("Method not allowed");
        return;
    }

    const { tokenId, title, body } = req.body;

    if (!tokenId || !title) {
        res.status(400).json({ error: "tokenId et title requis" });
        return;
    }

    try {
        // Récupérer le FCM token
        const tokenDoc = await db.collection("tokens").doc(tokenId).get();
        if (!tokenDoc.exists) {
            res.status(404).json({ error: "Token non trouvé" });
            return;
        }

        const fcmToken = tokenDoc.data().fcmToken;
        if (!fcmToken) {
            res.status(400).json({ error: "Pas de FCM token enregistré" });
            return;
        }

        // Envoyer la notification
        const message = {
            token: fcmToken,
            notification: { title, body: body || "" },
            android: { priority: "high" }
        };

        const response = await messaging.send(message);
        res.json({ success: true, messageId: response });

    } catch (error) {
        console.error("[testNotification] Erreur:", error);
        res.status(500).json({ error: error.message });
    }
});
