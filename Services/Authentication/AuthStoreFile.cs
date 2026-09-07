using System.Text.Json;
using WALLEve.Models.Authentication;

namespace WALLEve.Services.Authentication;

/// <summary>
/// Rein serialisierbarer Auth-Container (Datei-Format v2): mehrere Chars pro Konto,
/// einer aktiv. Erkennt und migriert das alte Einzel-Zustands-Format (v1) transparent.
/// </summary>
public class AuthStoreFile
{
    /// <summary>CharacterId des aktiven Charakters.</summary>
    public int? ActiveCharacterId { get; set; }

    /// <summary>Gespeicherte Charaktere (CharacterId → AuthState).</summary>
    public Dictionary<string, EveAuthState> Characters { get; set; } = new();

    public EveAuthState? GetActive() =>
        ActiveCharacterId.HasValue && Characters.TryGetValue(ActiveCharacterId.Value.ToString(), out var state)
            ? state
            : null;

    public void Save(EveAuthState state)
    {
        Characters[state.CharacterId.ToString()] = state;
        ActiveCharacterId = state.CharacterId;
    }

    public void Remove(int characterId)
    {
        Characters.Remove(characterId.ToString());

        // Falls der entfernte aktiv war: nächsten verfügbaren aktivieren
        if (ActiveCharacterId == characterId)
        {
            var next = Characters.Values.OrderByDescending(c => c.CharacterId).FirstOrDefault();
            ActiveCharacterId = next?.CharacterId;
        }
    }

    /// <summary>Letzten deserialisierbaren Zustand zurückgeben (für eine sinnvolle Anzeige nach Logout).</summary>
    public EveAuthState? LastActive()
    {
        if (ActiveCharacterId.HasValue && Characters.TryGetValue(ActiveCharacterId.Value.ToString(), out var active))
            return active;
        return Characters.Values.OrderByDescending(c => c.CharacterId).FirstOrDefault();
    }

    public static string Serialize(AuthStoreFile store) =>
        JsonSerializer.Serialize(store, new JsonSerializerOptions { WriteIndented = false });

    /// <summary>
    /// Liest das Format v2; falls das JSON ein einzelnes EveAuthState (v1) ist,
    /// wird es in die v2-Struktur überführt (dieser Charakter wird aktiv).
    /// </summary>
    public static AuthStoreFile? Deserialize(string json)
    {
        try
        {
            // v2 zuerst
            var store = JsonSerializer.Deserialize<AuthStoreFile>(json);
            if (store != null && store.Characters.Count > 0)
            {
                // Konsistenz: ActiveCharacterId muss innerhalb der Chars liegen
                if (store.ActiveCharacterId.HasValue && !store.Characters.ContainsKey(store.ActiveCharacterId.Value.ToString()))
                {
                    store.ActiveCharacterId = store.Characters.Values.OrderByDescending(c => c.CharacterId).First().CharacterId;
                }
                return store;
            }

            // Einzelner v1-Zustand?
            var legacy = JsonSerializer.Deserialize<EveAuthState>(json);
            if (legacy != null && legacy.CharacterId > 0)
            {
                var migrated = new AuthStoreFile();
                migrated.Save(legacy);
                return migrated;
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}