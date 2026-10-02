using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Forms = System.Windows.Forms;

internal sealed partial class WatchdogWindow
{
    private int lastModeIndex;
    private string uploaderRequestPath;
    private bool publishingConfirmation;
    private readonly List<CatalogChoice> downloadChoices = new List<CatalogChoice>();
    private bool filesLoading, changingFileSelection;
    private int fileScanVersion;
    private System.Threading.CancellationTokenSource fileScanCancellation;
    private sealed class ScannedFile { public string FullPath, RelativePath; public long Length; }
    private static System.Windows.Controls.ControlTemplate fileCheckTemplate;
    private static System.Windows.Media.Geometry documentGeometry;
    private ContextMenu fileLinkMenu;
    private static System.Windows.Media.Geometry DocumentGeometry()
    {
        if (documentGeometry == null) { documentGeometry = System.Windows.Media.Geometry.Parse("M2,1 L10,1 L15,6 L15,19 L2,19 Z M10,1 L10,6 L15,6"); documentGeometry.Freeze(); }
        return documentGeometry;
    }
    private ContextMenu FileLinkMenu()
    {
        if (fileLinkMenu != null) return fileLinkMenu;
        var menu = new ContextMenu(); var copy = new MenuItem(); menu.Items.Add(copy);
        menu.Opened += delegate {
            var target = menu.PlacementTarget as TextBlock; string link;
            copy.IsEnabled = target != null && resultDownloads.TryGetValue(Convert.ToString(target.Tag),out link);
            copy.Header = L("Download-Link kopieren","Копировать ссылку скачивания","Copy download link");
        };
        copy.Click += delegate { var target = menu.PlacementTarget as TextBlock; string link; if (target != null && resultDownloads.TryGetValue(Convert.ToString(target.Tag),out link)) CopyLink(link); };
        fileLinkMenu = menu; return menu;
    }
    private void CancelFileScan()
    {
        ++fileScanVersion;
        if (fileScanCancellation != null) fileScanCancellation.Cancel();
    }
    private string FileScanMessage()
    {
        if (emptyFilesState == "loading") return L("Ordner wird gelesen… Ordner oder Modus können gewechselt werden.","Чтение папки… Можно выбрать другую папку или режим.","Reading folder… You can choose another folder or mode.");
        if (emptyFilesState == "missing") return L("Ordner nicht gefunden. Wähle einen Ordner.","Папка не найдена. Выберите папку.","Folder not found. Choose a folder.");
        if (emptyFilesState == "error") return L("Ordner konnte nicht gelesen werden. Andere Ordner wählen (Details im Hinweis).","Не удалось прочитать папку. Выберите другую (подробности в подсказке).","Cannot read folder. Choose another (details in tooltip).");
        return L("Keine Dateien in diesem Ordner.","В этой папке нет файлов.","No files in this folder.");
    }
    private void RefreshFileActions()
    {
        bool available = !uploadRunning && !filesLoading && emptyFilesState != "error";
        startButton.IsEnabled = available; checkButton.IsEnabled = available; saveButton.IsEnabled = available;
        selectAllButton.IsEnabled = available; clearButton.IsEnabled = available;
    }
    private void FinishFileScan(int version)
    {
        if (version != fileScanVersion) return;
        filesLoading = false;
        foreach (var check in fileChecks) check.IsEnabled = !uploadRunning;
        RefreshFileActions(); UpdateSelection(); QueueLayout();
    }
    private void AddScannedFiles(List<ScannedFile> files, int index, HashSet<string> remembered, bool codeMode, bool defaults, int version)
    {
        if (version != fileScanVersion) return;
        // Bound each UI callback; rendering and mode changes can run between batches.
        var clock = System.Diagnostics.Stopwatch.StartNew();
        int count = 0;
        while (index < files.Count && count++ < 16 && clock.ElapsedMilliseconds < 8) AddScannedFile(files[index++], remembered, codeMode, defaults);
        selectedCount.Text = L("Dateien werden geladen: ","Чтение списка файлов: ","Loading file list: ") + index + " / " + files.Count;
        if (index < files.Count) Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(delegate { AddScannedFiles(files,index,remembered,codeMode,defaults,version); }));
        else FinishFileScan(version);
    }
    private void RememberSelection()
    {
        if (filesLoading) return; // Never replace a saved selection with a partial scan.
        config[lastModeIndex == 0 ? "ProjectFiles" : "Files"] = fileChecks.Where(c => c.IsChecked == true).Select(c => (string)c.Tag).ToArray();
        config[lastModeIndex == 0 ? "ProjectSourceDirectory" : "SourceDirectory"] = folderPath.Text;
    }
    private static bool SensitiveFile(string path)
    {
        string name = Path.GetFileName(path).ToLowerInvariant();
        return name == ".env" || name.StartsWith(".env.") || (name.StartsWith("id_") && !name.EndsWith(".pub")) || name == ".git-credentials" || name == ".netrc" || name == ".npmrc" || name == ".pypirc" || path.Replace('\\','/').EndsWith(".docker/config.json",StringComparison.OrdinalIgnoreCase) || path.Replace('\\','/').EndsWith(".aws/config",StringComparison.OrdinalIgnoreCase) || name == "credentials.json" || name == "credentials" || name.EndsWith(".pem") || name.EndsWith(".key") || name.EndsWith(".pfx") || name.EndsWith(".p12");
    }
    private static IEnumerable<string> EnumerateProjectFiles(string rootPath, System.Threading.CancellationToken cancellation)
    {
        WindowsPathSafety.AssertNoLinks(rootPath);
        var pending = new Stack<string>(); pending.Push(rootPath);
        while(pending.Count > 0) {
            cancellation.ThrowIfCancellationRequested();
            string directory = pending.Pop();
            foreach(string file in Directory.EnumerateFiles(directory)) { cancellation.ThrowIfCancellationRequested(); if(!String.Equals(Path.GetFileName(file),".git",StringComparison.OrdinalIgnoreCase) && !WindowsPathSafety.IsUnsafeReparsePoint(file)) yield return file; }
            foreach(string child in Directory.EnumerateDirectories(directory)) { cancellation.ThrowIfCancellationRequested(); if(!String.Equals(Path.GetFileName(child), ".git", StringComparison.OrdinalIgnoreCase) && !String.Equals(Path.GetFileName(child), ".githubsync", StringComparison.OrdinalIgnoreCase) && !WindowsPathSafety.IsUnsafeReparsePoint(child)) pending.Push(child); }
        }
    }
    private void ChooseIndividualFiles()
    {
        using(var picker = new Forms.OpenFileDialog { Multiselect = true, CheckFileExists = true, Title = L("Dateien wählen", "Выбрать файлы", "Choose files") }) {
            if(picker.ShowDialog() != Forms.DialogResult.OK) return;
            string basePath = Path.GetDirectoryName(picker.FileNames[0]);
            while(picker.FileNames.Any(p => !p.StartsWith(basePath.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))) {
                basePath = Path.GetDirectoryName(basePath);
                if(String.IsNullOrEmpty(basePath)) { ShowError(L("Dateien auf demselben Laufwerk wählen.", "Выберите файлы с одного диска.", "Select files on the same drive.")); return; }
            }
            config[CodeMode ? "ProjectSourceDirectory" : "SourceDirectory"] = basePath;
            config[CodeMode ? "ProjectFiles" : "Files"] = picker.FileNames.Select(p => CodeMode ? p.Substring(basePath.TrimEnd('\\').Length + 1).Replace('\\','/') : Path.GetFileName(p)).ToArray();
            folderPath.Text = basePath; folderPath.ToolTip = basePath; LoadFiles(); ResetResult();
        }
    }
    private void ResetResult()
    {
        if (!uploadRunning) { progressPath = null; lastProgressRead = 0; }
        latestSnapshot = null; uploadedReleaseUrl = ""; resultDownloads.Clear(); downloadChoices.Clear(); confirmedFiles.Clear(); phase = "ready";
        if(openFilesButton != null) openFilesButton.Visibility = Visibility.Collapsed;
        if(copyResultButton != null) copyResultButton.Visibility = Visibility.Collapsed;
        if(publishLaterButton != null) publishLaterButton.Visibility = Visibility.Collapsed;
        if(downloadLinksButton != null) downloadLinksButton.Visibility = Visibility.Collapsed;
        currentFile="";
        if(status != null) status.Visibility=Visibility.Collapsed;
        if(progressFrame != null) UpdatePresentation();
    }
    private void CleanupUploaderRequest()
    {
        if(!String.IsNullOrEmpty(uploaderRequestPath)) try { File.Delete(uploaderRequestPath); } catch { }
        uploaderRequestPath = null;
    }
    private void WriteUploaderRequest(string operation, Dictionary<string, object> plan)
    {
        CleanupUploaderRequest();
        var request = new Dictionary<string, object> {
            {"operation",operation}, {"repository",NormalizeRepository(repository.Text)}, {"branch",codeBranch.Text},
            {"destination",repositoryDestination.Text.Trim()}, {"sourceDirectory",folderPath.Text},
            {"paths",fileChecks.Where(c => c.IsChecked == true).Select(c => (string)c.Tag).ToArray()}, {"expectedAccount",accountLogin}, {"timeoutHours",Number(timeout,"HttpTimeoutHours",1,24)}
        };
        if(plan != null) { request["baseCommit"] = StateString(plan,"baseCommit",""); request["entries"] = plan["entries"]; request["branch"] = plan["branch"]; }
        uploaderRequestPath = Path.Combine(Path.GetTempPath(), "GitHubSyncRequest-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(uploaderRequestPath,json.Serialize(request),new UTF8Encoding(false));
    }
    private void ShowCodePlan(Dictionary<string, object> snapshot)
    {
        object raw;
        if(!snapshot.TryGetValue("plan",out raw) || !(raw is Dictionary<string,object>)) return;
        var plan = (Dictionary<string,object>)raw;
        var dialog = new Window { Owner=this, Title=L("Änderungen prüfen","Проверить изменения","Review changes"), Width=700, Height=500, MinWidth=520, MinHeight=350, WindowStartupLocation=WindowStartupLocation.CenterOwner, Background=Brush(Pale), FontFamily=FontFamily };
        var body = new DockPanel { Margin=new Thickness(24) };
        var heading = Text(StateString(plan,"repository","")+" · "+StateString(plan,"branch","")+" / "+StateString(plan,"destination",""),18,Ink,true); heading.TextWrapping=TextWrapping.Wrap; DockPanel.SetDock(heading,Dock.Top); body.Children.Add(heading);
        var explanation = Text(L("Neue/geänderte Dateien werden gespeichert. Andere Dateien bleiben erhalten.","Будут добавлены новые и заменены изменённые файлы. Остальные файлы не удаляются.","New and changed files will be saved. Other files will not be deleted."),15,Muted,false); explanation.TextWrapping=TextWrapping.Wrap; explanation.Margin=new Thickness(0,12,0,12); DockPanel.SetDock(explanation,Dock.Top); body.Children.Add(explanation);
        var buttons = new UniformGrid { Columns=2, Margin=new Thickness(0,16,0,0) }; DockPanel.SetDock(buttons,Dock.Bottom); body.Children.Add(buttons);
        var cancel=MakeButton(L("Schließen","Закрыть","Close"),false,0); cancel.IsCancel=true; cancel.Margin=new Thickness(0,0,6,0); cancel.Click+=delegate { dialog.DialogResult=false; }; buttons.Children.Add(cancel);
        var send=MakeButton(L("Änderungen hochladen","Загрузить изменения","Upload changes"),true,0); send.Name="CodePlanSend"; send.IsEnabled=preparingCodeUpload; send.Margin=new Thickness(6,0,0,0); send.Click+=delegate { dialog.DialogResult=true; }; buttons.Children.Add(send);
        var list=new StackPanel(); body.Children.Add(new ScrollViewer { Style=ScrollGutterStyle(), Content=list, VerticalScrollBarVisibility=ScrollBarVisibility.Auto, HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled });
        foreach(object item in (IEnumerable)plan["entries"]) { var entry=item as Dictionary<string,object>; if(entry==null) continue; string action=StateString(entry,"action","");
            string label=action=="add" ? L("Neu","Добавится","Added") : action=="update" ? L("Ersetzen","Обновится","Updated") : L("Unverändert","Без изменений","Unchanged");
            var line=Text(label+" · "+StateString(entry,"target",""),15,Ink,false); line.TextWrapping=TextWrapping.Wrap; line.Margin=new Thickness(0,8,0,8); list.Children.Add(line);
            TextBlock statusLabel; if(fileStatusLabels.TryGetValue(StateString(entry,"name",""),out statusLabel)) statusLabel.Text=label;
        }
        dialog.Content=body; dialog.Loaded+=delegate { cancel.Focus(); };
        bool confirmed=dialog.ShowDialog()==true; bool sendRequested=preparingCodeUpload; preparingCodeUpload=false;
        if(confirmed && sendRequested) { WriteUploaderRequest("upload",plan); Launch("CodeUpload",true); }
        else { status.Text=sendRequested ? L("Abgebrochen. Keine Dateien gespeichert.","Отменено. Файлы не отправлялись.","Cancelled. No files sent.") : L("Prüfung abgeschlossen. Keine Dateien gespeichert.","Проверка завершена. Файлы не отправлялись.","Check completed. No files sent."); status.Visibility=Visibility.Visible; status.Foreground=Brush(Muted); }
    }
    private void ApplyUploaderLanguage()
    {
        modeLabel.Text=L("Ziel","Назначение","Destination");
        ((ComboBoxItem)modePicker.Items[0]).Content=L("Projektdateien","Файлы проекта","Project files");
        ((ComboBoxItem)modePicker.Items[1]).Content=L("Download-Dateien","Файлы для скачивания","Download files");
        System.Windows.Automation.AutomationProperties.SetName(modePicker,modeLabel.Text);
        publishCheck.Content=L("Nach Upload veröffentlichen","Опубликовать после загрузки","Publish after upload");
        publishCaption.Text=Convert.ToString(publishCheck.Content); publishRow.Visibility=CodeMode ? Visibility.Collapsed : Visibility.Visible;
        addFilesButton.Visibility=CodeMode ? Visibility.Visible : Visibility.Collapsed;
        releaseTargetRow.Visibility=CodeMode ? Visibility.Collapsed : Visibility.Visible; codeTargetRow.Visibility=CodeMode ? Visibility.Visible : Visibility.Collapsed; codeOptions.Visibility=CodeMode ? Visibility.Visible : Visibility.Collapsed;
        tagLabel.Text=CodeMode ? L("2. Ordner im Projekt (leer = Stamm)","2. Папка в проекте (пусто = корень)","2. Project folder (empty = root)") : L("2. Release auswählen / erstellen","2. Выбрать / создать релиз","2. Choose / create release");
        branchLabel.Text=L("Branch (leer = Standardbranch)","Ветка (пусто = основная)","Branch (empty = default)"); branchesButton.Content=L("Auswählen…","Выбрать…","Choose…");
        addFilesButton.Content=L("Einzelne Dateien wählen…","Выбрать отдельные файлы…","Choose individual files…");
        subtitleText.Text=CodeMode ? L("Dateien und Ordner direkt ins GitHub-Projekt laden","Загрузка файлов и папок в проект GitHub","Upload files and folders to a GitHub project") : L("Dateien als Release bereitstellen","Загрузка и публикация файлов для скачивания","Upload and publish downloadable release files");
        filesHint.Visibility=CodeMode ? Visibility.Visible : Visibility.Collapsed;
        if(CodeMode) filesHint.Text=L("Ordnerstruktur bleibt erhalten. .git ausgeschlossen; mögliche Zugangsdaten nicht automatisch ausgewählt. Inhalt prüfen.","Структура папок сохраняется. .git исключена; возможные секреты не выбираются автоматически. Проверьте содержимое.","Folder structure is preserved. .git is excluded; possible secrets are not selected automatically. Review contents.");
        simpleHint.Text=L("Projekt → Ziel → Dateien → Hochladen → Ergebnis öffnen","Проект → назначение → файлы → загрузить → открыть результат","Project → destination → files → upload → open result");
        startButton.Content=uploadRunning ? L("Vorgang läuft","Выполняется операция","Operation running") : CodeMode ? L("Dateien ins Projekt laden","Загрузить файлы в проект","Upload project files") : publishCheck.IsChecked==true ? L("Hochladen und veröffentlichen","Загрузить и опубликовать","Upload and publish") : L("Als Entwurf speichern","Сохранить как черновик","Save as draft");
        checkButton.ToolTip=L("Nur prüfen; GitHub wird nicht geändert.","Только проверка, без изменений на GitHub.","Read-only check; no changes to GitHub.");
        publishLaterButton.Content=L("Release veröffentlichen","Опубликовать релиз","Publish release"); copyResultButton.Content=L("Link kopieren","Копировать ссылку","Copy link");
        downloadLinksButton.Content=L("Dateilinks","Ссылки на файлы","File links");
    }
    private void UpdateResultActions(Dictionary<string,object> snapshot)
    {
        bool published=StateString(snapshot,"published","false").Equals("true",StringComparison.OrdinalIgnoreCase);
        bool terminal=phase=="completed" || phase=="checked";
        if(snapshot.ContainsKey("downloads")) { resultDownloads.Clear(); downloadChoices.Clear(); foreach(object value in (IEnumerable)snapshot["downloads"]) { var entry=value as Dictionary<string,object>; if(entry!=null) { string link=StateString(entry,"url",""); resultDownloads[StateString(entry,"localName",StateString(entry,"name",""))]=link; downloadChoices.Add(new CatalogChoice {Label=StateString(entry,"name",""),Value=link}); } } }
        if(!signingIn && snapshot.ContainsKey("account") && !String.IsNullOrEmpty(StateString(snapshot,"account",""))) accountLogin=StateString(snapshot,"account","");
        publishLaterButton.Visibility=!CodeMode && terminal && !published && snapshot.ContainsKey("plan") && snapshot["plan"] is Dictionary<string,object> && ((Dictionary<string,object>)snapshot["plan"]).ContainsKey("releaseId") ? Visibility.Visible : Visibility.Collapsed;
        copyResultButton.Visibility=!CodeMode && terminal && published && IsReleaseUrl(uploadedReleaseUrl) ? Visibility.Visible : Visibility.Collapsed;
        downloadLinksButton.Visibility=!CodeMode && terminal && published && downloadChoices.Count>0 ? Visibility.Visible : Visibility.Collapsed;
        openFilesButton.Content=CodeMode ? L("Dateien im Projekt öffnen","Открыть файлы проекта","Open project files") : published ? L("Release öffnen","Открыть релиз","Open release") : L("Entwurf öffnen","Открыть черновик","Open draft");
        if(phase=="completed") { status.Text=CodeMode ? L("Dateien im Projekt bestätigt.","Файлы подтверждены в проекте.","Files confirmed in project.") : published ? L("Release veröffentlicht. Private Projekte bleiben privat.","Релиз опубликован. Приватный проект остаётся приватным.","Release published. Private repositories remain private.") : L("Dateien im Entwurf gespeichert, nicht im Code. Noch nicht veröffentlicht.","Файлы сохранены в черновике, а не в Code. Он ещё не опубликован.","Files saved in draft, not Code. Not published yet."); string message=StateString(snapshot,"message",""); if(!String.IsNullOrEmpty(message)) status.Text+=" "+message; }
    }
    private void PublishCurrentDraft()
    {
        if(uploadRunning || latestSnapshot==null || String.IsNullOrEmpty(accountLogin)) return;
        var plan=latestSnapshot["plan"] as Dictionary<string,object>; if(plan==null) return;
        if(StateString(plan,"repository","")!=NormalizeRepository(repository.Text) || StateString(plan,"tag","")!=releaseTag.Text) { ResetResult(); return; }
        var signatures=new List<string>();
        foreach(object raw in (IEnumerable)latestSnapshot["releaseAssets"]) { var asset=raw as Dictionary<string,object>; if(asset!=null) signatures.Add(StateString(asset,"id","")+":"+StateString(asset,"size","")+":"+StateString(asset,"state","")); }
        bool confirmed;
        publishingConfirmation=true;
        try { confirmed=ConfirmUpload(L("Diesen Release jetzt veröffentlichen? Private Projekte bleiben privat.","Опубликовать этот релиз сейчас? Приватный проект останется приватным.","Publish this release now? Private repositories remain private.")+"\n\n"+repository.Text+" · "+releaseTag.Text); } finally { publishingConfirmation=false; }
        if(!confirmed) return;
        CleanupUploaderRequest(); uploaderRequestPath=Path.Combine(Path.GetTempPath(),"GitHubSyncRequest-"+Guid.NewGuid().ToString("N")+".json");
        File.WriteAllText(uploaderRequestPath,json.Serialize(new Dictionary<string,object> {{"operation","publish"},{"repository",repository.Text},{"tag",releaseTag.Text},{"releaseId",plan["releaseId"]},{"assetSignatures",signatures},{"expectedAccount",accountLogin}}),new UTF8Encoding(false));
        Launch("Publish",true);
    }
    private void CopyLink(string link)
    {
        Uri uri; string repo=NormalizeRepository(repository.Text);
        if(!Uri.TryCreate(link,UriKind.Absolute,out uri) || uri.Scheme!="https" || uri.Host!="github.com" || !uri.IsDefaultPort || !String.IsNullOrEmpty(uri.UserInfo) || !uri.AbsolutePath.StartsWith("/"+repo+"/releases/",StringComparison.OrdinalIgnoreCase)) return;
        try { Clipboard.SetText(link); } catch { ShowError(L("Zwischenablage nicht verfügbar.","Буфер обмена недоступен.","Clipboard unavailable.")); }
    }
    private void ShowDownloadLinks()
    {
        var dialog=new Window {Owner=this,Title=L("Dateilinks","Ссылки на файлы","File links"),Width=680,Height=400,MinWidth=480,MinHeight=300,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brush(Pale),FontFamily=FontFamily};
        var body=new DockPanel {Margin=new Thickness(24)};
        var close=MakeButton(L("Schließen","Закрыть","Close"),false,0); close.IsCancel=true; close.Margin=new Thickness(0,12,0,0); close.Click+=delegate {dialog.Close();}; DockPanel.SetDock(close,Dock.Bottom); body.Children.Add(close);
        var list=new StackPanel(); body.Children.Add(new ScrollViewer {Style=ScrollGutterStyle(),Content=list,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        foreach(var choice in downloadChoices) {
            var entry=choice; var row=new Grid {Margin=new Thickness(0,0,0,12)}; row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
            var name=Text(entry.Label,15,Ink,false); name.VerticalAlignment=VerticalAlignment.Center; name.ToolTip=entry.Label; row.Children.Add(name);
            var copy=MakeButton(L("Kopieren","Копировать","Copy"),false,0); copy.Name="CopyAssetLink"; copy.Height=36; copy.Padding=new Thickness(16,0,16,0); copy.Margin=new Thickness(12,0,0,0); copy.Click+=delegate {CopyLink(entry.Value);}; Grid.SetColumn(copy,1); row.Children.Add(copy); list.Children.Add(row);
        }
        dialog.Content=body; dialog.ShowDialog();
    }
}
