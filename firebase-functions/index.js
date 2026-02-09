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

const { onDocumentCreated } = require("firebase-functions/v2/firestore");
const { onRequest } = require("firebase-functions/v2/https");
const { setGlobalOptions } = require("firebase-functions/v2");
const admin = require("firebase-admin");

admin.initializeApp();

const db = admin.firestore();
const messaging = admin.messaging();

// Configurer la région par défaut sur europe-west1 (aligné avec le reste du projet)
setGlobalOptions({ region: "europe-west1" });

/**
 * Trigger: Quand une nouvelle notification est créée dans /notifications/{notificationId}
 * Action: Envoie une push notification FCM au(x) parent(s) concerné(s)
 */
exports.onNotificationCreated = onDocumentCreated("notifications/{notificationId}", async (event) => {
    const notification = event.data.data();
    const notificationId = event.params.notificationId;

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
 */
async function getFcmTokensForNotification(notification) {
    const fcmTokens = [];

    if (notification.type === "Broadcast" || notification.targetParentId === "all") {
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
    const tokens = fcmTokens.map(t => t.fcmToken);

    return {
        tokens: tokens,
        data: {
            notificationId: notification.id || "",
            title: notification.title || "Nouveau message",
            body: notification.body || "",
            type: notification.type || "Info",
            replyToMessageId: notification.replyToMessageId || "",
            senderName: notification.senderName || "",
            badgeCount: "1"
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
 * Fonction HTTP pour tester l'envoi de notifications
 */
exports.testNotification = onRequest(async (req, res) => {
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
