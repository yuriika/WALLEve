// Hilfsaktionen der Trading-Action-Cards (Issue #66).
// Reine Seitenkanal-Helfer: sie lesen/ändern niemals Empfehlungsdaten.
// Kopieren via Clipboard-API mit Fallback für Umgebungen ohne Clipboard-Zugriff.
export async function copyText(text) {
    if (navigator.clipboard && window.isSecureContext) {
        try {
            await navigator.clipboard.writeText(text);
            return true;
        } catch {
            // Fall through to legacy path.
        }
    }

    try {
        const textArea = document.createElement('textarea');
        textArea.value = text;
        textArea.style.position = 'fixed';
        textArea.style.opacity = '0';
        document.body.appendChild(textArea);
        textArea.focus();
        textArea.select();
        const ok = document.execCommand('copy');
        document.body.removeChild(textArea);
        return ok;
    } catch {
        return false;
    }
}