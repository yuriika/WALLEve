// Browser-Benachrichtigungen für Trading-Meldungen (Issue #70).
// Optionaler Seitenkanal: Jede Funktion ist fehlertolerant und wirft nie —
// verweigerte/fehlende Berechtigung darf die In-App-Meldungen nicht stören.
// Es wird keine Systemzustellung bei geschlossenem Browser behauptet
// (kein Service Worker, keine Push-Infrastruktur — bewusst kein Teil dieses Slices).

export function isSupported() {
    return typeof window !== 'undefined' && 'Notification' in window;
}

export function getPermission() {
    if (!isSupported()) return 'unsupported';
    return Notification.permission;
}

export async function requestPermission() {
    if (!isSupported()) return false;
    try {
        const result = await Notification.requestPermission();
        return result === 'granted';
    } catch {
        return false;
    }
}

export function showNotification(title, body, url) {
    if (!isSupported() || Notification.permission !== 'granted') return;
    try {
        const notification = new Notification(title, {
            body: body,
            tag: url,
            icon: '/images/eve-logo.svg',
        });
        notification.onclick = () => {
            window.focus();
            if (url) window.location.href = url;
            notification.close();
        };
    } catch {
        // Seitenkanal: Fehler hier nie auf die In-App-Meldungen durchschlagen lassen.
    }
}