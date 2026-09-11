using System.Text.Json;
using WALLEve.Models.Authentication;
using WALLEve.Services.Authentication;

namespace WALLEve.Tests;

/// <summary>
/// Tests für den Multi-Charakter-Auth-Container (File-Format v2 + v1-Migration).
/// </summary>
public class AuthStoreFileTests
{
    private static EveAuthState MakeState(int id, string name) => new()
    {
        CharacterId = id,
        CharacterName = name,
        AccessToken = $"access-{id}",
        RefreshToken = $"refresh-{id}",
        ExpiresAt = DateTime.UtcNow.AddHours(1)
    };

    [Fact]
    public void Save_UpsertsAndSetsActive()
    {
        var store = new AuthStoreFile();
        store.Save(MakeState(111, "Alpha"));
        store.Save(MakeState(222, "Beta"));

        Assert.Equal(2, store.Characters.Count);
        Assert.Equal(222, store.ActiveCharacterId);
        Assert.Equal(222, store.GetActive()!.CharacterId);

        // Erneutes Speichern desselben Chars überschreibt, keine Duplikate
        store.Save(MakeState(111, "Alpha v2"));
        Assert.Equal(2, store.Characters.Count);
        Assert.Equal("Alpha v2", store.Characters["111"].CharacterName);
    }

    [Fact]
    public void RemoveActive_PicksLastActiveAsNext()
    {
        var store = new AuthStoreFile();
        store.Save(MakeState(111, "Alpha"));
        store.Save(MakeState(222, "Beta"));

        store.Remove(222); // aktiver entfernt
        Assert.Equal(111, store.GetActive()!.CharacterId);
    }

    [Fact]
    public void SerializeDeserialize_Circular()
    {
        var store = new AuthStoreFile();
        store.Save(MakeState(111, "Alpha"));
        store.Save(MakeState(222, "Beta"));

        var json = AuthStoreFile.Serialize(store);
        var loaded = AuthStoreFile.Deserialize(json);

        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.Characters.Count);
        Assert.Equal(222, loaded.ActiveCharacterId);
        Assert.Equal("Beta", loaded.GetActive()!.CharacterName);
    }

    [Fact]
    public void Deserialize_LegacySingleState_MigratesToV2()
    {
        // v1-Format: einzelnes EveAuthState
        var legacyJson = JsonSerializer.Serialize(MakeState(333, "Legacy"));

        var loaded = AuthStoreFile.Deserialize(legacyJson);

        Assert.NotNull(loaded);
        Assert.Single(loaded!.Characters);
        Assert.Equal(333, loaded.ActiveCharacterId);
        Assert.Equal("Legacy", loaded.GetActive()!.CharacterName);
    }

    [Fact]
    public void Deserialize_InvalidJson_ReturnsNull()
    {
        Assert.Null(AuthStoreFile.Deserialize("not json at all"));
    }

    [Fact]
    public void Deserialize_V2WithUnknownActive_FallsBackToHighestId()
    {
        var store = new AuthStoreFile();
        store.Save(MakeState(111, "Alpha"));
        store.Save(MakeState(222, "Beta"));
        store.ActiveCharacterId = 999; // ungültig

        var json = AuthStoreFile.Serialize(store);
        var loaded = AuthStoreFile.Deserialize(json);

        Assert.Equal(222, loaded!.GetActive()!.CharacterId);
    }
}