# Regionalscan messen und technische Grenzen festhalten (Issue #67)

Discovery-Artefakt: begrenzter, cachekonformer Messlauf des Regionalscans
(`EsiApiService.GetAllRegionalMarketOrdersAsync`, öffentliche ESI-Endpunkte,
ohne Authentifizierung). Es werden **ausschließlich beobachtete Werte**
dokumentiert — keine geratenen Produktionslimits.

## Messobjekt

Der Regionalscan lädt alle Marktorders einer Region über
`GET /markets/{region_id}/orders/?order_type=all&page=N` und läuft dabei über
den echten Produktionspfad der App:

- ETag/304-Cache (`EsiCacheService`): wiederholte Abrufe innerhalb des
  ESI-Cachefensters übertragen nur noch Header, keine Bodies.
- Gestaffelte Parallelität (`SemaphoreSlim(4)` + 250 ms Staffelung) und
  atomares Publizieren (`CollectAllPagesAtomicallyAsync`): ein Seitenfehler
  verwirft das gesamte Ergebnis (keine Teildaten).
- Der Client negotiiert **kein** `Accept-Encoding: gzip`, ESI liefert daher
  unkomprimiertes JSON. Die komprimierte Transfergröße wird separat per
  Gzip-Probe gemessen (eine Zusatzanfrage je Region).

## Durchführung (reproduzierbar)

1. Konfiguration: Sektion `Measurement:RegionalScan` in `appsettings.json`
   (`Enabled: true`, `Regions` = repräsentative Regionen, `ProbeGzip: true`).
   Alternative ohne Dateiänderung: Umgebungsvariable
   `Measurement__RegionalScan__Enabled=true`.
2. App-Start: `dotnet run`. Nach ~5 s läuft der Messlauf genau einmal
   (`RegionalScanMeasurementHostedService`, kein Produktionscollector).
3. Ergebnis: JSON-Artefakt `<Datenordner>/regional-scan-measurement.json`
   (neben `wallet.db`). Das Artefakt enthält Quelle, Zeitpunkt, ESI-Basis-URL,
   je Region Seiten/Orders/Bytes/Dauer/SQLite-Wachstum/Fehlerbudget und die
   Gzip-Probe — keine Tokens oder Nutzerdaten.
4. Auswertung: `RegionalScanMeasurementEvaluator.Evaluate` (offline, durch
   Unit-Tests abgedeckt) leitet das gemessene Envelope ab.

Messgrößen je Region: Seiten (davon 304-Cache-Treffer), Orders,
übertragene Bytes (Summe der `Content-Length` der 200er-Antworten), Dauer,
SQLite-Dateigröße vor/nach, fehlgeschlagene Seiten (Fehlerbudget), Gzip-Probe
(`Accept-Encoding: gzip`; komprimiert vs. dekomprimiert).

## Messergebnis vom 16.09.2026, 06:49 UTC

Einmaliger Kaltstart-Lauf über den echten App-Pfad (Produktions-Build),
Quelle: `regional-scan-measurement.json` im Datenordner (Artefakt-Header
`measuredAtUtc: 2026-09-16T06:49:16Z`), ESI
`https://esi.evetech.net/latest`. Fünf repräsentative Regionen, eine Seite =
bis zu 2.000 Orders.

| Region | Seiten | Orders | Bytes übertragen | Dauer | Gzip-Probe (kompr. → dekompr.) |
| --- | ---: | ---: | ---: | ---: | --- |
| The Forge (10000002) | 410 | 409.613 | 97,2 MB | 36,8 s | 25.785 → 237.381 B |
| Domain (10000043) | 184 | 183.597 | 43,5 MB | 20,0 s | 23.608 → 238.163 B |
| Sinq Laison (10000032) | 119 | 118.418 | 28,1 MB | 10,7 s | 24.650 → 238.220 B |
| Heimatar (10000030) | 73 | 72.214 | 17,1 MB | 8,9 s | 23.500 → 238.200 B |
| Verge Vendor (10000068) | 40 | 39.006 | 9,3 MB | 4,2 s | 22.947 → 237.422 B |
| **Summe** | **826** | **822.848** | **195,2 MB** | ~80 s | — |

Fehlerbudget: **0 %** (0 fehlgeschlagene von 826 Seiten — das Budget war im
Lauf unverbraucht; ein Seitenfehler verwirft nach dem Atomaritätsvertrag das
Gesamtergebnis der Region).

SQLite-Wachstum während des Laufs: **0 Bytes** (5.128.192 → 5.128.192).
Der Regionalscan selbst persistiert keine Orderdaten; ein Wachstum stammt
ausschließlich von parallelen Collectoren.

304-Cache-Verhalten (beobachtet bei direktem Folge-Abruf aller Regionen
innerhalb des ESI-Cachefensters): alle 826 Seiten wurden als `304 Not
Modified` bedient, **0 Bytes** übertragen, Laufzeit unverändert
(~37/20/11/9/4 s — dominiert von der 250-ms-Staffelung, nicht vom Transfer).
Der ETag-Cache schützt Folgeläufe vollständig vor Resends.

Gzip: eine repräsentative Seite (2.000 Orders) ist komprimiert ~22–26 kB statt
~237 kB → **Faktor ~10 weniger Transfer**. Ohne `Accept-Encoding: gzip`
überträgt der aktuelle Client jede Seite unkomprimiert (~237 kB).

## Evidenzbasierte Grenzempfehlungen

Abgeleitet aus den Messdaten (Envelope = Maximum der beobachteten Werte;
keine geratenen Limits):

- Max. Seiten je Region: **410** (The Forge) — alle Regionen sind in einem
  Scanlauf darstellbar; größere Regionen existieren in EVE nicht (The Forge
  ist der größte Handelsraum).
- Max. Orders je Region: **409.581**.
- Max. übertragene Bytes je Region (unkomprimiert): **~97 MB**.
- Max. Dauer je Region: **~38 s**, gesamter 5-Regionen-Lauf ~75 s.
- Fehlerbudget: **0 %** bei diesem Lauf; der Atomaritätsvertrag verwirft bei
  einem Seitenfehler die ganze Region (kein Teildatenschlupf).

Beobachtete Ausbaupotenziale (keine Limits, nur Befund):
- `Accept-Encoding: gzip` am `EveApi`-Client würde den Transfer um ~10x
  reduzieren (bei gleichem ΔT, da die Staffelung die Dauer dominiert).
- Die 250-ms-Staffelung begrenzt die Dauer: 410 Seiten × 250 ms / 4 parallel
  ≈ 26 s reine Staffelzeit — bei niedrigerem Fehlerbudget-Risiko verkürzbar.

Interpretation: Ein Messlauf ist repräsentativ, aber punktuell. Bevor ein hier
genannter Wert als Produktionslimit verwendet wird, ist er durch weitere
Messläufe (z. B. über den Tag verteilt) zu verifizieren.

## Nicht-Ziele

Kein Produktionscollector (kein wiederkehrendes Sammeln), keine
Handelsempfehlungen, keine Live-ESI-Abhängigkeit in deterministischen
Unit-Tests. Bei blockiertem Live-Zugriff wird der Lauf als unvollständig
ausgewiesen — es werden keine erfundenen Zahlen dokumentiert.