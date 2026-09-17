using Microsoft.EntityFrameworkCore;
using WALLEve.Components.Industry;
using WALLEve.Models.Industry;

namespace WALLEve.Tests;

/// <summary>
/// Regression-Tests für die Industrie-Anzeige (#55).
/// Deckt die Akzeptanzkriterien ab: BPO/BPC/ME/TE/Runs korrekt dargestellt,
/// Jobstatus-Klassifikation (aktiv vs. historisch), Stale-/Alter-Markierung,
/// Character-Isolation (keine Aktivitätsdaten anderer Charaktere).
/// </summary>
public class IndustryDisplayTests
{
    private const int CharacterA = 90073315;
    private const int CharacterB = 90073316;

    private static BlueprintEntry Bpo(long itemId, int characterId = CharacterA)
        => new()
        {
            CharacterId = characterId,
            ItemId = itemId,
            TypeId = 1030,
            LocationId = 60003760,
            LocationFlag = "Hangar",
            Runs = -1,
            IsBlueprintCopy = false,
            MaterialEfficiency = 6,
            TimeEfficiency = 4,
            UpdatedAt = DateTime.UtcNow
        };

    private static BlueprintEntry Bpc(long itemId, int runs, int characterId = CharacterA)
    {
        var b = Bpo(itemId, characterId);
        b.ItemId = itemId;
        b.IsBlueprintCopy = true;
        b.Runs = runs;
        return b;
    }

    [Fact]
    public void FormatBlueprintKind_BpoOriginal_BpcKopie()
    {
        Assert.Equal("BPO (Original)", IndustryDisplay.FormatBlueprintKind(isBlueprintCopy: false));
        Assert.Equal("BPC (Kopie)", IndustryDisplay.FormatBlueprintKind(isBlueprintCopy: true));
    }

    [Fact]
    public void FormatRuns_BpoSentinel_ShowsUnlimitedNeverMinusOne()
    {
        // Runs = -1 ist der BPO-Sentinel (#49) und darf nie als "-1 Run" erscheinen.
        Assert.Equal("∞ (unbefristet)", IndustryDisplay.FormatRuns(Bpo(1000)));
    }

    [Fact]
    public void FormatRuns_Bpc_ShowsRemainingRuns()
    {
        Assert.Equal("5 Runs", IndustryDisplay.FormatRuns(Bpc(1001, runs: 5)));
        Assert.Equal("1 Run", IndustryDisplay.FormatRuns(Bpc(1002, runs: 1)));
    }

    [Fact]
    public void FormatMeTe_ShowsBothEfficiencies()
    {
        Assert.Equal("ME 6 / TE 4", IndustryDisplay.FormatMeTe(Bpo(1000)));
    }

    [Theory]
    [InlineData(0, "vor 0 min")]
    [InlineData(30, "vor 30 min")]
    [InlineData(90, "vor 1 Std.")]
    [InlineData(60 * 20, "vor 20 Std.")]
    [InlineData(60 * 24 * 3, "vor 3 Tagen")]
    [InlineData(60 * 24 * 45, "vor 1 Monaten")]
    public void FormatSyncAge_UtcAges_MinutesHoursDaysMonths(int minutesAgo, string expected)
    {
        var now = new DateTime(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);
        var updated = now.AddMinutes(-minutesAgo);
        Assert.Equal(expected, IndustryDisplay.FormatSyncAge(updated, now));
    }

    [Fact]
    public void FormatSyncAge_FutureTimestamp_ClampsToZero()
    {
        var now = new DateTime(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal("vor 0 min", IndustryDisplay.FormatSyncAge(now.AddMinutes(5), now));
    }

    [Fact]
    public void IsStale_Threshold_Differentiates()
    {
        var now = new DateTime(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(IndustryDisplay.IsStale(now.AddDays(-8), now, TimeSpan.FromDays(7)));
        Assert.False(IndustryDisplay.IsStale(now.AddDays(-6), now, TimeSpan.FromDays(7)));
    }

    [Fact]
    public void ComputeSyncState_FailedSync_PartialNeverCompleteNorValidEmpty()
    {
        var now = new DateTime(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);
        var threshold = TimeSpan.FromDays(7);

        // Frischer Bestand nach fehlgeschlagenem Sync: Partial, nie Complete.
        Assert.Equal(IndustrySyncState.Partial, IndustryDisplay.ComputeSyncState(
            hasData: true, lastSyncSucceeded: false, lastSyncAtUtc: now.AddMinutes(-5), now, threshold));

        // Kein Bestand nach fehlgeschlagenem Sync: KEIN gültiges "leer", sondern Partial.
        Assert.Equal(IndustrySyncState.Partial, IndustryDisplay.ComputeSyncState(
            hasData: false, lastSyncSucceeded: false, lastSyncAtUtc: now.AddMinutes(-5), now, threshold));

        // Auch ein alter Snapshot nach fehlgeschlagenem Sync bleibt Partial (Dominanz).
        Assert.Equal(IndustrySyncState.Partial, IndustryDisplay.ComputeSyncState(
            hasData: true, lastSyncSucceeded: false, lastSyncAtUtc: now.AddDays(-20), now, threshold));
    }

    [Fact]
    public void ComputeSyncState_SuccessfulFreshSync_Complete_EmptyIsNone()
    {
        var now = new DateTime(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);
        var threshold = TimeSpan.FromDays(7);

        Assert.Equal(IndustrySyncState.Complete, IndustryDisplay.ComputeSyncState(
            hasData: true, lastSyncSucceeded: true, lastSyncAtUtc: now.AddDays(-1), now, threshold));

        // Erfolgreicher Sync ohne Daten = gültig leer (None), nicht Partial.
        Assert.Equal(IndustrySyncState.None, IndustryDisplay.ComputeSyncState(
            hasData: false, lastSyncSucceeded: true, lastSyncAtUtc: now.AddMinutes(-5), now, threshold));
    }

    [Fact]
    public void ComputeSyncState_SuccessfulOldSync_Stale()
    {
        var now = new DateTime(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(IndustrySyncState.Stale, IndustryDisplay.ComputeSyncState(
            hasData: true, lastSyncSucceeded: true, lastSyncAtUtc: now.AddDays(-8), now, TimeSpan.FromDays(7)));
    }

    [Fact]
    public void ComputeSyncState_MixedAges_PerSectionIndependent()
    {
        // Regression Review #55: Der Stale-Zustand wird je Abschnitt bewertet.
        // Ein frischer Blueprint-Stand darf einen veralteten Job-Stand nicht
        // verdecken (früherer globaler "neuester Datenstand" über beide Bereiche).
        var now = new DateTime(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);
        var threshold = TimeSpan.FromDays(7);

        // Jobs: letzter erfolgreicher Sync vor 10 Tagen -> Stale.
        Assert.Equal(IndustrySyncState.Stale, IndustryDisplay.ComputeSyncState(
            hasData: true, lastSyncSucceeded: true, lastSyncAtUtc: now.AddDays(-10), now, threshold));

        // Blueprints: letzter erfolgreicher Sync vor 1 Tag -> Complete.
        Assert.Equal(IndustrySyncState.Complete, IndustryDisplay.ComputeSyncState(
            hasData: true, lastSyncSucceeded: true, lastSyncAtUtc: now.AddDays(-1), now, threshold));
    }

    [Fact]
    public void ComputeSyncState_NeverSynced_NoData_IsNone()
    {
        var now = new DateTime(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);

        // Nie synchronisiert und kein Bestand: neutraler Ausgangszustand (None).
        Assert.Equal(IndustrySyncState.None, IndustryDisplay.ComputeSyncState(
            hasData: false, lastSyncSucceeded: null, lastSyncAtUtc: null, now, TimeSpan.FromDays(7)));

        // Bestand ohne nachweisbaren Sync-Verlauf: mangels Fehlernachweis als vollständig ansehen.
        Assert.Equal(IndustrySyncState.Complete, IndustryDisplay.ComputeSyncState(
            hasData: true, lastSyncSucceeded: null, lastSyncAtUtc: null, now, TimeSpan.FromDays(7)));
    }

    [Fact]
    public void SyncStateAfterManualRun_ThrownOrFailedRun_PartialEvenAfterComplete()
    {
        // Regression Review #55: Ein geworfener Sync-Fehler (Exception) muss denselben
        // Zustandsweg nehmen wie Success=false. Ein Abschnitt, der zuvor Complete war,
        // darf nach dem Fehler NICHT als vollständig bestätigt weiter gerendert werden.
        var now = new DateTime(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);
        var threshold = TimeSpan.FromDays(7);

        // Fehlgeschlagener Lauf mit vorhandenem (frischem) Snapshot: Partial, nie Complete.
        Assert.Equal(IndustrySyncState.Partial, IndustryDisplay.SyncStateAfterManualRun(
            succeeded: false, hasData: true, now, threshold));

        // Fehlgeschlagener Lauf ohne Bestand: kein gültiges "leer", sondern Partial.
        Assert.Equal(IndustrySyncState.Partial, IndustryDisplay.SyncStateAfterManualRun(
            succeeded: false, hasData: false, now, threshold));
    }

    [Fact]
    public void SyncStateAfterManualRun_SuccessfulRun_CompleteOrNone()
    {
        var now = new DateTime(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);
        var threshold = TimeSpan.FromDays(7);

        // Erfolgreicher Lauf mit Bestand: frischer Sync gilt als vollständig.
        Assert.Equal(IndustrySyncState.Complete, IndustryDisplay.SyncStateAfterManualRun(
            succeeded: true, hasData: true, now, threshold));

        // Erfolgreicher Lauf ohne Daten: gültig leer (None), nicht Partial.
        Assert.Equal(IndustrySyncState.None, IndustryDisplay.SyncStateAfterManualRun(
            succeeded: true, hasData: false, now, threshold));
    }

    [Fact]
    public void ResolveSyncState_FailedBackgroundJob_ThenSuccessfulManualSync_CompleteOrNone()
    {
        // Regression Review-Runde 4: Fehlgeschlagener Background-Job → erfolgreicher
        // manueller Sync → State-Auflösung (Reload) ergibt Complete (Bestand) bzw.
        // None (leer), nicht fälschlich Partial: Der persistierte manuelle Erfolg
        // gewinnt gegen den ÄLTEREN Failed-Job.
        var now = new DateTime(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);
        var threshold = TimeSpan.FromDays(7);
        var failedJobAt = now.AddHours(-2);
        var manualSuccessAt = now.AddMinutes(-5);

        Assert.Equal(IndustrySyncState.Complete, IndustryDisplay.ResolveSyncState(
            hasData: true,
            lastTerminalAtUtc: failedJobAt,
            lastTerminalSucceeded: false,
            lastManualSyncSucceededAtUtc: manualSuccessAt,
            now, threshold));

        // Erfolgreicher manueller Sync ohne Bestand: gültig leer (None), nicht Partial.
        Assert.Equal(IndustrySyncState.None, IndustryDisplay.ResolveSyncState(
            hasData: false,
            lastTerminalAtUtc: failedJobAt,
            lastTerminalSucceeded: false,
            lastManualSyncSucceededAtUtc: manualSuccessAt,
            now, threshold));
    }

    [Fact]
    public void ResolveSyncState_ManualSuccessWithoutTerminalJob_CompleteOrNone()
    {
        // Manueller Erfolg ohne jede BackgroundJob-Historie (z. B. Erstlauf): der
        // persistierte Erfolgszeitpunkt trägt die Auflösung allein.
        var now = new DateTime(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);
        var threshold = TimeSpan.FromDays(7);

        Assert.Equal(IndustrySyncState.Complete, IndustryDisplay.ResolveSyncState(
            hasData: true, lastTerminalAtUtc: null, lastTerminalSucceeded: null,
            lastManualSyncSucceededAtUtc: now.AddMinutes(-5), now, threshold));

        Assert.Equal(IndustrySyncState.None, IndustryDisplay.ResolveSyncState(
            hasData: false, lastTerminalAtUtc: null, lastTerminalSucceeded: null,
            lastManualSyncSucceededAtUtc: now.AddMinutes(-5), now, threshold));
    }

    [Fact]
    public void ResolveSyncState_NewerFailedBackgroundJob_WinsOverOlderManualSuccess()
    {
        // Regression Guard: Ein NEUERER terminaler Background-Job (Fehlschlag) nach
        // einem älteren manuellen Erfolg ist die jüngste Evidenz → Partial. Der
        // persistierte Erfolg darf einen späteren Fehlschlag nicht überdecken.
        var now = new DateTime(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);
        var threshold = TimeSpan.FromDays(7);

        Assert.Equal(IndustrySyncState.Partial, IndustryDisplay.ResolveSyncState(
            hasData: true,
            lastTerminalAtUtc: now.AddMinutes(-5),
            lastTerminalSucceeded: false,
            lastManualSyncSucceededAtUtc: now.AddMinutes(-60),
            now, threshold));
    }

    [Fact]
    public void ResolveSyncState_NoManualSync_FallsBackToTerminalJobEvidence()
    {
        // Ohne persistierten manuellen Erfolg identisch zur bisherigen Auflösung:
        // Completed-Job → Complete, Failed-Job → Partial, keine Historie → None.
        var now = new DateTime(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);
        var threshold = TimeSpan.FromDays(7);

        Assert.Equal(IndustrySyncState.Complete, IndustryDisplay.ResolveSyncState(
            hasData: true, lastTerminalAtUtc: now.AddDays(-1), lastTerminalSucceeded: true,
            lastManualSyncSucceededAtUtc: null, now, threshold));

        Assert.Equal(IndustrySyncState.Partial, IndustryDisplay.ResolveSyncState(
            hasData: true, lastTerminalAtUtc: now.AddMinutes(-5), lastTerminalSucceeded: false,
            lastManualSyncSucceededAtUtc: null, now, threshold));

        Assert.Equal(IndustrySyncState.None, IndustryDisplay.ResolveSyncState(
            hasData: false, lastTerminalAtUtc: null, lastTerminalSucceeded: null,
            lastManualSyncSucceededAtUtc: null, now, threshold));
    }

    [Fact]
    public void MapJobStatus_KnownStatuses_GermanLabels()
    {
        Assert.Equal("Aktiv", IndustryDisplay.MapJobStatus("active"));
        Assert.Equal("Pausiert", IndustryDisplay.MapJobStatus("paused"));
        Assert.Equal("Bereit", IndustryDisplay.MapJobStatus("ready"));
        Assert.Equal("Geliefert", IndustryDisplay.MapJobStatus("delivered"));
        Assert.Equal("Fertiggestellt", IndustryDisplay.MapJobStatus("finished"));
        Assert.Equal("Storniert", IndustryDisplay.MapJobStatus("cancelled"));
        Assert.Equal("Abgelehnt", IndustryDisplay.MapJobStatus("rejected"));
    }

    [Fact]
    public void MapJobStatus_UnknownStatus_StaysVisible()
    {
        // Unbekannte ESI-Status werden nie still zu "Aktiv" oder geleert.
        Assert.Equal("Unbekannt (bogus)", IndustryDisplay.MapJobStatus("bogus"));
    }

    [Fact]
    public void IsActiveJobStatus_ActivePausedReady_HistoricalOtherwise()
    {
        Assert.True(IndustryDisplay.IsActiveJobStatus("active"));
        Assert.True(IndustryDisplay.IsActiveJobStatus("paused"));
        Assert.True(IndustryDisplay.IsActiveJobStatus("ready"));
        Assert.False(IndustryDisplay.IsActiveJobStatus("delivered"));
        Assert.False(IndustryDisplay.IsActiveJobStatus("finished"));
        Assert.False(IndustryDisplay.IsActiveJobStatus("cancelled"));
        Assert.False(IndustryDisplay.IsActiveJobStatus("rejected"));
    }

    [Fact]
    public void GetJobEndTime_CompletedDateWins_ElsePlannedEnd()
    {
        var planned = new DateTime(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc);
        var completed = new DateTime(2026, 9, 19, 8, 30, 0, DateTimeKind.Utc);

        var finished = new IndustryJobEntry { EndDate = planned, CompletedDate = completed };
        var running = new IndustryJobEntry { EndDate = planned, CompletedDate = null };

        Assert.Equal(completed, IndustryDisplay.GetJobEndTime(finished));
        Assert.Equal(planned, IndustryDisplay.GetJobEndTime(running));
    }

    [Fact]
    public async Task CharacterIsolation_JobsAndBlueprints_OnlyOwnersData()
    {
        var db = TestDb.Create();

        db.IndustryJobEntries.AddRange(
            Job(1, CharacterA, "active"),
            Job(2, CharacterB, "active"),
            Job(3, CharacterA, "delivered"));
        db.BlueprintEntries.AddRange(
            Bpo(1000, CharacterA),
            Bpo(2000, CharacterB),
            Bpc(1001, runs: 3, characterId: CharacterA));
        await db.SaveChangesAsync();

        var jobs = await IndustryDisplay.JobsForCharacter(db.IndustryJobEntries, CharacterA).ToListAsync();
        var blueprints = await IndustryDisplay.BlueprintsForCharacter(db.BlueprintEntries, CharacterA).ToListAsync();

        Assert.Equal(2, jobs.Count);
        Assert.All(jobs, j => Assert.Equal(CharacterA, j.CharacterId));
        Assert.Equal(new[] { 1, 3 }, jobs.Select(j => j.JobId).OrderBy(id => id));
        Assert.Equal(2, blueprints.Count);
        Assert.All(blueprints, b => Assert.Equal(CharacterA, b.CharacterId));
    }

    private static IndustryJobEntry Job(int jobId, int characterId, string status)
        => new()
        {
            CharacterId = characterId,
            JobId = jobId,
            ActivityId = 1,
            BlueprintId = 1000 + jobId,
            BlueprintTypeId = 1030,
            BlueprintLocationId = 60003760,
            OutputLocationId = 60003760,
            FacilityId = 60003760,
            StationId = 60003760,
            Status = status,
            StartDate = DateTime.UtcNow.AddHours(-4),
            EndDate = DateTime.UtcNow.AddHours(2),
            Runs = 1,
            Duration = 6 * 60 * 60,
            InstallerId = characterId,
            UpdatedAt = DateTime.UtcNow
        };
}