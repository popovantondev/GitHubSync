<!-- ppk-readme:head:start -->
<a href="https://popovantondev.github.io/GitHubSync/index.html"><picture><source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/popovantondev/GitHubSync/main/docs/readme/header-de-dark.357de53f.svg"><img src="https://raw.githubusercontent.com/popovantondev/GitHubSync/main/docs/readme/header-de-light.3785adc8.svg" width="880" height="188" alt="GitHubSync — Überträgt Dateien zu GitHub und lädt Releases herunter. Windows 10/11 · x64, Release 1.5.3"></picture></a>
<!-- ppk-readme:head:end -->

# GitHubSync

[Русский](README.ru.md) · **Deutsch** · [English](README.en.md)

<!-- ppk-readme:body:start -->
<p>
<a href="https://github.com/popovantondev/GitHubSync/releases/tag/v1.5.3"><picture><source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/popovantondev/GitHubSync/main/docs/readme/btn-download-de-dark.6c155e43.svg"><img src="https://raw.githubusercontent.com/popovantondev/GitHubSync/main/docs/readme/btn-download-de-light.13fa2a9f.svg" width="171" height="36" alt="Herunterladen"></picture></a>
<a href="https://popovantondev.github.io/GitHubSync/index.html"><picture><source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/popovantondev/GitHubSync/main/docs/readme/btn-website-de-dark.f7dbce78.svg"><img src="https://raw.githubusercontent.com/popovantondev/GitHubSync/main/docs/readme/btn-website-de-light.e1b6ff70.svg" width="129" height="36" alt="Webseite"></picture></a>
<a href="https://popovantondev.github.io/GitHubSync/Guide-de.html"><picture><source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/popovantondev/GitHubSync/main/docs/readme/btn-guide-de-dark.226bcbce.svg"><img src="https://raw.githubusercontent.com/popovantondev/GitHubSync/main/docs/readme/btn-guide-de-light.f7f31317.svg" width="134" height="36" alt="Anleitung"></picture></a>
<a href="https://github.com/popovantondev/GitHubSync/issues/new/choose"><picture><source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/popovantondev/GitHubSync/main/docs/readme/btn-bug-de-dark.3329794d.svg"><img src="https://raw.githubusercontent.com/popovantondev/GitHubSync/main/docs/readme/btn-bug-de-light.c3e0d8e9.svg" width="168" height="36" alt="Fehler melden"></picture></a>
</p>

<p><a href="https://raw.githubusercontent.com/popovantondev/GitHubSync/main/docs/Ui-de-code.png"><picture><source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/popovantondev/GitHubSync/main/docs/readme/preview-de-dark.c28fdf86.webp"><img src="https://raw.githubusercontent.com/popovantondev/GitHubSync/main/docs/readme/preview-de-light.566fa841.webp" width="880" height="516" alt="Oberfläche von GitHubSync"></picture></a><br>
<sub><a href="https://raw.githubusercontent.com/popovantondev/GitHubSync/main/docs/Ui-de-code.png">Ganzer Screenshot</a></sub></p>

<picture><source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/popovantondev/GitHubSync/main/docs/readme/divider-dark.cab62eca.svg"><img src="https://raw.githubusercontent.com/popovantondev/GitHubSync/main/docs/readme/divider-light.96357132.svg" width="880" height="6" alt="Trennlinie"></picture>
<!-- ppk-readme:body:end -->

Dateien zu GitHub senden, herunterladen und fortsetzen. Hinweise auf Updates starten keine Übertragung.

**Windows 10/11 · x64 · 1.5.3 · Deutsch / Русский / English**

**[Prüfsumme SHA-256](https://github.com/popovantondev/GitHubSync/releases/download/v1.5.3/GitHubSync-1.5.3-win-x64.zip.sha256)** · **[Release und getrennte Quellarchive](https://github.com/popovantondev/GitHubSync/releases/tag/v1.5.3)** · **[Sicherheit](SECURITY.md)**

## Erste Schritte

Zuerst **Senden** oder **Herunterladen** wählen. Code enthält Projektdateien, Releases enthalten fertige Programme und Archive. Öffentliche Downloads brauchen keine Anmeldung: GitHub-Link einfügen, Dateiliste aktualisieren, Speicherordner und Dateien wählen, Änderungen bestätigen. Die vollständige Anleitung ist [offline verfügbar](docs/Guide-de.html).

Schließen blendet das Fenster in den Infobereich aus; bestätigte Übertragungen laufen weiter. Doppelklick öffnet das Fenster. Beenden erfolgt im Tray-Menü. Downloads lassen sich pausieren und nach erneuter Prüfung fortsetzen. Update-Hinweise starten keine Übertragung.

Gesamte Portable-ZIP entpacken, SHA-256 prüfen und `GitHubSync.exe` öffnen. Kein Installer oder Administratorrecht nötig. Git/Git Credential Manager sind enthalten; Windows PowerShell 5.1 und .NET Framework müssen verfügbar sein. Anmeldung einschließlich Google/SMS/2FA erfolgt im offiziellen Browser. Projekt auswählen/erstellen, Zielmodus wählen, Dateien prüfen, Upload ausdrücklich bestätigen und Ergebnis öffnen.

Echter WPF-Render mit künstlichen Dateien, Beispielkonto und neutralem Pfad; keine persönlichen Daten. Ordnerlesen läuft im Hintergrund und kann durch Ordner-/Moduswechsel abgebrochen werden.

## Zwei Ziele

- **Projektdateien:** rekursive relative Pfade, Vorschau Neu/Ersetzen/Unverändert, ein Commit in bestehenden Branch. Keine Löschungen, kein force, kein Leercommit. Geänderter/geschützter Branch stoppt den Vorgang.
- **Release-Anhänge:** Entwurf wählen/erstellen; Checkbox schaltet zwischen Entwurf speichern und Hochladen/veröffentlichen nach Bestätigung aller Anlagen. Spätere separate Veröffentlichung und Links zu Release/Dateien. Veröffentlichte Releases werden nicht verändert.

Privat bleibt privat; eine Veröffentlichung ändert die Repository-Sichtbarkeit nicht. Projekt-Erstellung ist standardmäßig privat.

## Grenzen und Datenschutz

Code-Dateien über 100 MiB ablehnen; Senden → Release-Anhänge verwenden. Kein automatisches Git LFS. `.git`/Links ausgeschlossen, mögliche Geheimnisse nicht vorausgewählt; Inhalte dennoch selbst prüfen. `.gitignore` wird nicht automatisch angewendet. Einstellungen und Fehlerlog neben der EXE können lokale Pfade, Dateinamen und Konto enthalten: nicht veröffentlichen. Tokens werden dort nicht gespeichert; GCM verwaltet Anmeldung. Git ignoriert generierte Artefakte, Runtime, Einstellungen, Logs und Schlüssel.

## Prüfung und Rechte

In Version 1.5.3 verwenden Code-Uploads das gebündelte Git statt großer base64-REST-Anfragen: ein Commit, keine Löschung anderer Dateien, kein force. Ein temporärer Branch-Snapshot verändert nicht den gewählten lokalen Ordner. Git-Objektfortschritt ist keine Dateibestätigung; Erfolg erst nach Commitprüfung auf GitHub. Das Tray-Menü wird nicht mehr jede Sekunde neu aufgebaut. Offline-Prüfungen und separate künstliche TEST-Abnahme stehen in der [Prüfung 1.5.3](docs/VERIFICATION-1.5.3.md). Andere PCs und physischer DPI-Wechsel bleiben unbestätigt.

Release-Dateien: `GitHubSync-1.5.3-win-x64.zip` und `.sha256`. [Build/Struktur](CONTRIBUTING.md), [Offline-Anleitung](docs/Guide-de.html), [MIT](LICENSE) und [Drittherstellerhinweise](THIRD_PARTY.md).
