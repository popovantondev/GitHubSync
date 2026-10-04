# GitHubSync 1.5.3

Windows 10/11 x64 · Deutsch / Русский / English · MIT application code

## Русский

Отправляйте файлы проекта или вложения релиза на GitHub и скачивайте их обратно. Передача начинается только после подтверждения. Есть создание проекта и черновика, публикация релиза, проверка результата, прогресс и докачка скачивания.

Скачайте **GitHubSync-1.5.3-win-x64.zip**, сверьте его `.sha256`, распакуйте **папку целиком** и запустите `GitHubSync.exe`. Не запускайте EXE из ZIP и не переносите его отдельно от runtime. Установщик и права администратора не нужны. Требуются Windows PowerShell 5.1 и .NET Framework 4.6.2+ с WPF (рекомендуется 4.8). Для отправки нажмите «Войти в GitHub», завершите вход в браузере и выберите проект, назначение и файлы. Для публичного скачивания вход не обязателен. Подробная инструкция открывается кнопкой «Справка»; файл `docs/Guide-ru.html` из portable-пакета можно открыть обычным браузером без интернета.

В 1.5.3 отправка в Code использует встроенный Git вместо большого base64 REST-запроса: один подтверждённый коммит, без удаления других файлов и без force. Исправлено дёргание меню трея. Ошибка 502 проверена искусственным тестом 61 MiB + TXT/Unicode в согласованном тестовом проекте: загрузка и обратные контрольные суммы подтверждены. Это не обещание отсутствия будущих сетевых ошибок GitHub.

[Сообщить об ошибке](https://github.com/popovantondev/GitHubSync/issues/new/choose)

## Deutsch

Projektdateien und Release-Anhänge zu GitHub senden oder herunterladen, mit Bestätigung, Fortschritt und fortsetzbaren Downloads. SHA-256 prüfen, das vollständige Portable-ZIP entpacken und `GitHubSync.exe` starten; Git/GCM sind enthalten. Windows PowerShell 5.1 und .NET Framework 4.6.2+ mit WPF sind erforderlich. Kein Installer oder Administratorzugriff. Über «Bei GitHub anmelden» die Anmeldung im Browser abschließen; für öffentliche Downloads ist keine Anmeldung erforderlich. Die HTML-Anleitung ist über «Hilfe» oder offline als `docs/Guide-de.html` verfügbar.

Code-Uploads verwenden einen bestätigten Git-Commit ohne force und löschen keine anderen Projektdateien. Das Tray-Menü bleibt stabil. Dateien aus Downloads werden weder automatisch ausgeführt noch Archive automatisch entpackt.

[Problem melden](https://github.com/popovantondev/GitHubSync/issues/new/choose)

## English

Send/download project files and release attachments with explicit confirmation, progress and resumable downloads. Verify SHA-256, extract the entire portable ZIP and start `GitHubSync.exe`; Git/GCM are bundled. Requires Windows PowerShell 5.1 and .NET Framework 4.6.2+ with WPF. No installer or administrator rights. Choose “Sign in to GitHub” and complete the browser sign-in; public downloads do not require authentication. Open “Help” or `docs/Guide-en.html` for the offline HTML user guide.

Code uploads use one verified non-force Git commit, preserving other repository files. The tray popup no longer rebuilds every second. Downloads never automatically run applications or extract archives.

[Report a problem](https://github.com/popovantondev/GitHubSync/issues/new/choose)

## Files and verification

- **win-x64.zip**: application, bundled runtime, notices and offline HTML help. Use this to run GitHubSync.
- **source.zip**: reviewed application source without runtime, user settings or credentials.
- **runtime-sources.zip**: complete collected upstream source/build material and pinned packaging recipes for the bundled runtime. This is not the application or an installer.
- A **.sha256** accompanies each ZIP. Run `Get-FileHash .\GitHubSync-1.5.3-win-x64.zip -Algorithm SHA256` and compare it with the companion file.

Portable SHA-256: `461f8531784fc5cd69012a3656b5b63078bcd948bf65e350b6f1bcc0a2a25dd2`

The EXE is **unsigned**. Code uploads reject files above 100 MiB; use release attachments instead. Git LFS/history/submodules are not synchronized. Byte resume applies to downloads, not native Git pushes. Private projects remain private. Never share personal settings, logs or credentials in Issues. Other PCs, fresh interactive browser login/2FA, physical DPI changes and real network-interruption acceptance remain unverified. Source-material review is technical evidence, not a legal certificate, upstream signature audit or reproducible-build guarantee.

[Technical verification](https://github.com/popovantondev/GitHubSync/blob/v1.5.3/docs/VERIFICATION-1.5.3.md) · [Runtime source review](https://github.com/popovantondev/GitHubSync/blob/v1.5.3/docs/RUNTIME_SOURCE_REVIEW-1.5.3.md)
