# Einzelne GitHub-Issues implementieren

Ein Milestone ist eine Phase, kein Auftrag für einen einzigen Durchlauf. `tracking`-Issues sind Übersichten; nur deren ausführbare Teil-Issues implementieren. Abhängigkeiten müssen nach externem Review in `dev` integriert sein. Ein offener PR genügt nicht. Milestone-Gates bleiben verbindlich.

Für ein schwächeres Modell die Nummer eines freigegebenen, kleinen Issues einsetzen und diesen Prompt verwenden:

```text
Repository: /Users/yuri/source/walleve
GitHub: yuriika/WALLEve
Aufgabe: Implementiere ausschließlich Issue #<NUMMER>.

Verbindlicher Workflow:
- Ein eigener Branch und ein PR pro ausführbarem Issue.
- Ausgangsbasis: aktuelles origin/dev; PR-Ziel ausdrücklich dev.
- Kein Merge, kein Auto-Merge, kein Schließen des Issues.
- Kein direkter Push nach dev/master, kein Force-Push.
- Nach verifiziertem PR aufhören. Danach erfolgt externes Review.

1. Voraussetzungen
Lade relevante Skills. Lies vorhandene Projektregeln, CONTRIBUTING.md,
DEVELOPMENT.md sowie das Issue, seine Kommentare, sein übergeordnetes
Tracking-Issue und die Abhängigkeiten des Milestones.
Die GitHub-Issues sind verbindlich; .brainstorming ist keine Voraussetzung.

Führe git fetch origin aus. Prüfe Arbeitsbaum und lokale/remote Branches.
Bei fremden uncommitteten Änderungen oder abweichendem lokalem dev:
nichts verwerfen, automatisch stashen oder übernehmen; Blocker melden.
Ist das ausgewählte Issue ein Tracking-Issue, nicht implementieren.
Sind Vorgänger nicht in origin/dev integriert, Blocker benennen und stoppen.
Kein stilles Stacking, Cherry-Picking oder Kopieren fremder PR-Änderungen.

2. Umfang
Verifiziere die Issue-Aussagen am Code. Ordne jedem Akzeptanzkriterium
konkrete Tests/Prüfungen zu. Keine erfundenen APIs oder Fachregeln.
Ist die Aufgabe zu breit oder eine notwendige Entscheidung ungeklärt:
konkrete Aufteilung/Klärung vorschlagen und vor Codeänderungen stoppen.
Nicht eigenmächtig nur einen Teil der Akzeptanzkriterien erledigen.

3. Implementierung
Erstelle fix/<NUMMER>-<slug> oder feat/<NUMMER>-<slug> von origin/dev.
Regression/gewünschtes Verhalten zuerst testen, danach minimale
vollständige Implementierung und gezieltes Refactoring.
Keine sachfremden Umbauten, Platzhalter oder deaktivierten Tests.
Nutzerdaten und manuelle Werte erhalten; additive Migrationen testen.
Dokumentation pflegen, README deutsch und englisch gleichwertig halten.

4. Verifikation
Führe aus:
- dotnet build
- dotnet test tests/WALLEve.Tests/WALLEve.Tests.csproj --no-restore
- git diff --check
- zusätzliche Tests und Laufzeitprüfungen aus dem Issue
Prüfe den gesamten Diff gegen origin/dev. Alle Akzeptanzkriterien erfüllen.
Keine neuen Warnungen; bestehende Warnungen konkret ausweisen.
Fehlende Prüfmöglichkeiten ehrlich nennen, niemals als bestanden ausgeben.

5. Commit/PR
Nur aufgabenbezogene Dateien stagen. Ausschließlich Issue-Branch pushen.
PR mit explizitem --base dev und --head <Issue-Branch> erstellen.
Beschreibung: Refs #<NUMMER>, Änderungen/Nicht-Ziele, Nachweis je
Akzeptanzkriterium, tatsächliche Tests, Risiken und bestehende Warnungen.
Vermerk: Kein Merge; externes Review und ausdrückliche Freigabe ausstehend.

PR zurücklesen: Base, Head, Commit, Status und Diff prüfen.
Vorhandene CI-Checks prüfen. Keine CI ist kein grüner CI-Lauf.
Nicht erfüllte Kriterien bedeuten nicht reviewfertig.
Abschluss: PR-Link, Branch, Commit, Testergebnisse und Blocker.
Kein nächstes Issue beginnen, nicht mergen und nicht schließen.
```

## Nach dem externen Review

Review-Korrekturen gehören auf denselben Issue-Branch, mit erneutem Build und Tests. Erst nach ausdrücklicher Freigabe wird integriert. Ein abhängiges Folge-Issue beginnt danach von neu gefetchtem `origin/dev`. Unabhängige Issues dürfen getrennte PRs haben; für kleinere Modelle bleibt ein Issue pro Durchlauf die Empfehlung.

`master` ist aktuell der Default-Branch. `Closes #N` im PR-Text nach `dev` schließt das Issue beim Merge nach `dev` nicht automatisch. `Refs #N` referenziert es ohne eine falsche Zusage zur Schließung. Der Maintainer entscheidet nach Integration über den Issue-Status.
