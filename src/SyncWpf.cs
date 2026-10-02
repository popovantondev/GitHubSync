using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

internal sealed partial class WatchdogWindow
{
    private bool downloadMode, changingDirection, allowWindowClose, exitAfterPause, monitorBusy;
    private Button sendDirectionButton, downloadDirectionButton, remoteRefreshButton, updatesButton, pauseButton, resumeButton;
    private CheckBox monitorCheck;
    private TextBlock updatesText;
    private Dictionary<string,object> remoteCatalog, pendingDownload;
    private string syncControlFile, localResultDirectory = "", monitoredKey = "", monitorEtag = "", monitorFingerprint = "", monitorSourceId = "", lastNotified = "";
    private long selectedReleaseId;
    private readonly HashSet<string> resumeSelections = new HashSet<string>(StringComparer.Ordinal);
    private string syncBranch = "", syncPrefix = "";
    private bool observerFailed;
    private string trayFailure="";
    private Process activeWorker;
    private Forms.NotifyIcon tray;
    private Forms.ContextMenuStrip trayMenu;
    private DispatcherTimer trayAnimation, monitorTimer;
    private readonly List<System.Drawing.Icon> trayFrames = new List<System.Drawing.Icon>();
    private readonly List<BitmapSource> windowFrames = new List<BitmapSource>();
    private int trayFrame;
    private DateTime nextMonitorCheck = DateTime.MinValue;
    private static string privateStateRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GitHubSync");
    private string PrivateState { get { Directory.CreateDirectory(privateStateRoot); return privateStateRoot; } }
    private string PendingPath { get { return Path.Combine(PrivateState, "pending-download.json"); } }

    private void BuildSyncControls(StackPanel header, StackPanel advancedBody, StackPanel bottom)
    {
        downloadMode = StringValue("Direction", "upload") == "download";
        selectedReleaseId = ConfigLong("DownloadReleaseId"); syncBranch = StringValue("DownloadBranch", ""); syncPrefix = StringValue("DownloadPrefix", "");
        var direction = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        direction.ColumnDefinitions.Add(new ColumnDefinition()); direction.ColumnDefinitions.Add(new ColumnDefinition()); direction.ColumnDefinitions.Add(new ColumnDefinition());
        sendDirectionButton = MakeButton("", false, 0); sendDirectionButton.Height = 36; sendDirectionButton.Margin = new Thickness(0,0,6,0);
        downloadDirectionButton = MakeButton("", false, 0); downloadDirectionButton.Height = 36; downloadDirectionButton.Margin = new Thickness(6,0,6,0);
        remoteRefreshButton = MakeButton("", false, 0); remoteRefreshButton.Height = 36; remoteRefreshButton.Margin = new Thickness(6,0,0,0);
        Grid.SetColumn(downloadDirectionButton,1); Grid.SetColumn(remoteRefreshButton,2);
        direction.Children.Add(sendDirectionButton); direction.Children.Add(downloadDirectionButton); direction.Children.Add(remoteRefreshButton); header.Children.Add(direction);
        sendDirectionButton.Click += delegate { ChangeDirection(false); }; downloadDirectionButton.Click += delegate { ChangeDirection(true); };
        remoteRefreshButton.Click += delegate { RefreshRemoteFiles(); };
        var monitoring = new StackPanel { Margin = new Thickness(0,12,0,0) };
        monitorCheck = new CheckBox { IsChecked = StringValue("MonitorEnabled", "true").Equals("true",StringComparison.OrdinalIgnoreCase), Foreground = Brush(Ink), FontSize = 14 };
        monitorCheck.Checked += delegate { SaveSyncPreferences(); }; monitorCheck.Unchecked += delegate { SaveSyncPreferences(); };
        updatesButton = MakeButton("",false,0); updatesButton.Height = 36; updatesButton.Margin = new Thickness(0,8,0,0); updatesButton.Click += delegate { CheckRemoteUpdates(true); };
        updatesText = Text("",14,Muted,false); updatesText.TextWrapping = TextWrapping.Wrap; updatesText.Margin = new Thickness(0,8,0,8); updatesText.Visibility=Visibility.Collapsed;
        monitoring.Children.Add(monitorCheck); monitoring.Children.Add(updatesButton); advancedBody.Children.Insert(0,monitoring);bottom.Children.Insert(1,updatesText);
        var extra = new WrapPanel(); bottom.Children.Insert(2,extra);
        pauseButton = MakeButton("",false,0); pauseButton.Height = 36; pauseButton.Margin = new Thickness(0,8,8,0); pauseButton.Visibility = Visibility.Collapsed; pauseButton.Click += delegate { PauseDownload(); };
        resumeButton = MakeButton("",false,0); resumeButton.Height = 36; resumeButton.Margin = new Thickness(0,8,8,0); resumeButton.Visibility = File.Exists(PendingPath) ? Visibility.Visible : Visibility.Collapsed; resumeButton.Click += delegate { RestorePendingDownload(); };
        extra.Children.Add(pauseButton); extra.Children.Add(resumeButton);
        repositoryDestination.LostKeyboardFocus += delegate { if(downloadMode && !uploadRunning) { syncPrefix=repositoryDestination.Text.Trim(); remoteCatalog=null; } };
        monitorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        monitorTimer.Tick += delegate { if (DateTime.UtcNow >= nextMonitorCheck && monitorCheck.IsChecked == true) CheckRemoteUpdates(false); }; monitorTimer.Start();
        try { var saved=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(PrivateState,"monitor.json"),Encoding.UTF8)); monitoredKey=StateString(saved,"key","");monitorEtag=StateString(saved,"etag","");monitorFingerprint=StateString(saved,"fingerprint","");monitorSourceId=StateString(saved,"sourceId","");lastNotified=StateString(saved,"lastNotified",""); } catch {}
        Closed+=delegate {monitorTimer.Stop();PauseDownload();};
    }
    private long ConfigLong(string key) { long value; return Int64.TryParse(StringValue(key,"0"),out value) ? value : 0; }
    private void ResetTableItems()
    {
        if(fileList.Items.Count<500) {fileList.Items.Clear();return;}
        // Clearing thousands of WPF logical children synchronously stalls a mode
        // switch. Swap the virtualized viewport; the detached graph is collected.
        var parent=(StackPanel)fileList.Parent; int index=parent.Children.IndexOf(fileList); double height=listScroll.MaxHeight;
        var next=new ListBox {BorderThickness=new Thickness(0),Padding=new Thickness(0),Background=Brushes.Transparent,IsTabStop=false,Template=fileList.Template,ItemsPanel=fileList.ItemsPanel,ItemTemplate=fileList.ItemTemplate,ItemContainerStyle=fileList.ItemContainerStyle};
        VirtualizingStackPanel.SetIsVirtualizing(next,true);VirtualizingStackPanel.SetVirtualizationMode(next,VirtualizationMode.Recycling);
        parent.Children.RemoveAt(index);parent.Children.Insert(index,next);fileList=next;fileList.ApplyTemplate();
        BindFileViewport();listScroll.MaxHeight=height;
    }
    private void ChangeDirection(bool download)
    {
        if(uploadRunning || downloadMode == download) return;
        if(!downloadMode) RememberSelection(); else SaveSyncPreferences();
        downloadMode=download; remoteCatalog=null; CancelFileScan(); fileChecks.Clear(); fileStatusLabels.Clear(); ResetTableItems(); ResetResult(); localResultDirectory="";
        changingDirection=true;
        try { folderPath.Text=download ? StringValue("DownloadDirectory","") : SourceFolder(); repositoryDestination.Text=download ? syncPrefix : StringValue("RepositoryPath",""); codeBranch.Text=download ? syncBranch : StringValue("RepositoryBranch",""); }
        finally { changingDirection=false; }
        if(!download) releaseTag.Text=StringValue("ReleaseTag","");
        folderPath.ToolTip=folderPath.Text; ApplyLanguage(); LoadFiles(); SaveSyncPreferences();
    }
    private void ApplySyncLanguage()
    {
        if(sendDirectionButton == null) return;
        sendDirectionButton.Content=L("Senden","Отправить","Send"); downloadDirectionButton.Content=L("Herunterladen","Скачать","Download");
        sendDirectionButton.Background=Brush(downloadMode ? Hex("#ECECEE") : Hex("#FFE6BF")); downloadDirectionButton.Background=Brush(downloadMode ? Hex("#FFE6BF") : Hex("#ECECEE"));
        remoteRefreshButton.Content=L("Dateiliste aktualisieren","Обновить список файлов","Refresh file list"); remoteRefreshButton.Visibility=downloadMode ? Visibility.Visible : Visibility.Collapsed;
        ((ComboBoxItem)modePicker.Items[0]).Content=L("Projektdateien (Code)","Файлы проекта (Code)","Project files (Code)");
        ((ComboBoxItem)modePicker.Items[1]).Content=L("Release-Anhänge","Вложения релиза","Release attachments");
        monitorCheck.Content=L("GitHub alle 15 Minuten prüfen; Übertragung nur mit Zustimmung","Проверять GitHub каждые 15 минут; передача только с разрешения","Check GitHub every 15 minutes; transfers require approval");
        updatesButton.Content=L("Updates prüfen","Проверить обновления","Check for updates");
        pauseButton.Content=L("Download pausieren","Приостановить скачивание","Pause download"); resumeButton.Content=L("Download fortsetzen…","Продолжить скачивание…","Resume download…");
        if(downloadMode) {
            publishRow.Visibility=Visibility.Collapsed; addFilesButton.Visibility=Visibility.Collapsed;
            folderLabel.Text=L("Speicherordner auf diesem PC","Куда сохранить на этом компьютере","Save to this computer");
            projectsButton.Content=L("Auswählen…","Выбрать…","Choose…"); draftsButton.Content=L("Auswählen…","Выбрать…","Choose…");
            tagLabel.Text=CodeMode ? L("2. GitHub-Ordner (leer = Stamm)","2. Папка GitHub (пусто = корень)","2. GitHub folder (empty = root)") : L("2. Veröffentlichter Release","2. Опубликованный релиз","2. Published release");
            releaseTag.Text=selectedReleaseId == 0 ? L("Neueste stabile Version","Последняя стабильная версия","Latest stable release") : StringValue("DownloadReleaseLabel",selectedReleaseId.ToString());
            subtitleText.Text=CodeMode ? L("Dateien aus Code mit Ordnerstruktur herunterladen","Скачивание файлов проекта с сохранением папок","Download project files with their folder structure") : L("Programme und Archive aus Releases herunterladen","Скачивание программ и архивов из релизов","Download programs and archives from releases");
            filesHint.Visibility=Visibility.Visible; filesHint.Text=CodeMode ? L("Dateien aus Code; keine Git-Historie, Submodule oder Git LFS. Ganze Ordner: Pfad in Schritt 2 angeben oder passende Dateien markieren.","Файлы из Code, без истории Git, подмодулей и Git LFS. Для целой папки укажите её путь на шаге 2 или отметьте нужные файлы.","Code files, without Git history, submodules or Git LFS. Enter a folder in step 2 or select its files.") : L("Release-Anhänge; Archive werden nicht automatisch entpackt oder gestartet.","Вложения релиза: архивы не распаковываются, программы не запускаются автоматически.","Release attachments: archives are not extracted and programs are not run automatically.");
            startButton.Content=uploadRunning ? L("Download läuft","Идёт скачивание","Downloading") : L("Ausgewählte Dateien herunterladen","Скачать выбранные файлы","Download selected files");
            stageLabels[2].Text=L("Download","Скачивание","Download");
            simpleHint.Text=L("Projekt → Quelle → Dateien → Herunterladen → Ordner öffnen","Проект → источник → файлы → скачать → открыть папку","Project → source → files → download → open folder");
            openFilesButton.Content=L("Lokalen Ordner öffnen","Открыть локальную папку","Open local folder");
            publishLaterButton.Visibility=Visibility.Collapsed; copyResultButton.Visibility=Visibility.Collapsed; downloadLinksButton.Visibility=Visibility.Collapsed;
        }
        sendDirectionButton.IsEnabled=!uploadRunning; downloadDirectionButton.IsEnabled=!uploadRunning; remoteRefreshButton.IsEnabled=!uploadRunning; updatesButton.IsEnabled=!uploadRunning && !monitorBusy;
        pauseButton.Visibility=downloadMode && uploadRunning && !checking ? Visibility.Visible : Visibility.Collapsed;
        resumeButton.IsEnabled=!uploadRunning; pauseButton.IsEnabled=uploadRunning;
        if(trayMenu != null) UpdateTrayMenu();
    }
    private void SaveSyncPreferences()
    {
        if(sendDirectionButton == null) return;
        config["Direction"]=downloadMode ? "download" : "upload"; config["MonitorEnabled"]=monitorCheck.IsChecked == true;config["UploadMode"]=CodeMode ? "code" : "release";
        string project=NormalizeRepository(repository.Text); if(project.Length>0)config["Repository"]=project;
        if(downloadMode) { syncBranch=codeBranch.Text;syncPrefix=repositoryDestination.Text;config["DownloadDirectory"]=folderPath.Text; config["DownloadBranch"]=syncBranch; config["DownloadPrefix"]=syncPrefix; config["DownloadReleaseId"]=selectedReleaseId; }
        SavePreferencesOnly();
    }
    private void SavePreferencesOnly()
    {
        try { string path=Path.Combine(root,"config.json"), temp=path+".sync.tmp"; File.WriteAllText(temp,json.Serialize(config),new UTF8Encoding(false)); if(File.Exists(path)) File.Replace(temp,path,null); else File.Move(temp,path); } catch { }
    }
    private Dictionary<string,object> SyncRequest(string operation)
    {
        return new Dictionary<string,object> { {"operation",operation},{"repository",NormalizeRepository(repository.Text)},{"contentKind",CodeMode ? "code" : "release"}, {"branch",codeBranch.Text}, {"prefix",repositoryDestination.Text.Trim()}, {"releaseId",selectedReleaseId}, {"directory",folderPath.Text} };
    }
    private string SyncKey(Dictionary<string,object> request) { return StateString(request,"repository","")+"|"+StateString(request,"contentKind","")+"|"+StateString(request,"branch","")+"|"+StateString(request,"prefix","")+"|"+StateString(request,"releaseId",""); }
    private void ShowRemotePlaceholder(string message)
    {
        CancelFileScan(); filesLoading=false; fileChecks.Clear(); fileStatusLabels.Clear(); ResetTableItems(); emptyFilesLabel=null; emptyFilesState=null;
        fileList.Items.Add(EmptyLine(message)); RefreshFileActions(); UpdateSelection(); QueueLayout();
    }
    private void RefreshRemoteFiles()
    {
        if(!downloadMode || uploadRunning) return;
        var request=SyncRequest("catalog");
        if(StateString(request,"repository","").Length == 0) { ShowRemotePlaceholder(L("GitHub-Link in Schritt 1 einfügen.","Вставьте ссылку на проект GitHub на шаге 1.","Paste a GitHub project link in step 1.")); return; }
        remoteCatalog=null; ShowRemotePlaceholder(L("GitHub-Dateien werden gelesen…","Чтение файлов GitHub…","Reading GitHub files…"));
        LaunchSync(request,false,delegate(Dictionary<string,object> snapshot) {
            object raw; if(snapshot.TryGetValue("syncCatalog",out raw) && raw is Dictionary<string,object>) {
                remoteCatalog=(Dictionary<string,object>)raw; PopulateRemoteFiles(); EstablishMonitor(request,remoteCatalog); SaveSyncPreferences();
            }
        });
    }
    private void PopulateRemoteFiles()
    {
        ShowRemotePlaceholder(""); fileList.Items.Clear(); object raw;
        if(remoteCatalog == null || !remoteCatalog.TryGetValue("entries",out raw) || !(raw is IEnumerable)) return;
        var entries=((IEnumerable)raw).Cast<object>().OfType<Dictionary<string,object>>().ToList();
        int version=++fileScanVersion; filesLoading=true;
        AddRemoteBatch(entries,0,version);
    }
    private void AddRemoteBatch(List<Dictionary<string,object>> entries,int index,int version)
    {
        if(version != fileScanVersion) return;
        var clock=Stopwatch.StartNew(); int count=0;
        while(index<entries.Count && count++<16 && clock.ElapsedMilliseconds<8) {
            var entry=entries[index++]; string name=StateString(entry,"name","");
            AddScannedFile(new ScannedFile { FullPath=name, RelativePath=name, Length=StateLong(entry,"length") },resumeSelections,false,false);
        }
        if(index<entries.Count) Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(delegate { AddRemoteBatch(entries,index,version); }));
        else { resumeSelections.Clear(); if(entries.Count==0) fileList.Items.Add(EmptyLine(L("Keine Dateien oder Release-Anhänge.","Нет файлов или вложений релиза.","No files or release attachments."))); FinishFileScan(version); }
    }
    private void StartDownload()
    {
        if(uploadRunning || filesLoading) return;
        if(remoteCatalog == null) { RefreshRemoteFiles(); return; }
        if(!Directory.Exists(folderPath.Text)) { ShowError(L("Speicherordner wählen.","Выберите папку, куда сохранить файлы.","Choose a download folder.")); return; }
        string[] paths=fileChecks.Where(c=>c.IsChecked==true).Select(c=>(string)c.Tag).ToArray();
        if(paths.Length==0) { ShowError(L("Dateien markieren.","Отметьте файлы для скачивания.","Select files to download.")); return; }
        var request=SyncRequest("preview"); request["paths"]=paths; request["sourceId"]=StateString(remoteCatalog,"sourceId",""); SaveSyncPreferences();
        LaunchSync(request,false,delegate(Dictionary<string,object> snapshot) {
            object raw; if(snapshot.TryGetValue("syncCatalog",out raw) && raw is Dictionary<string,object>) ConfirmDownload((Dictionary<string,object>)raw);
        });
    }
    private void ConfirmDownload(Dictionary<string,object> plan)
    {
        var dialog=new Window { Owner=this, Title=L("Download prüfen","Подтвердить скачивание","Review download"), Width=680,Height=490,MinWidth=500,MinHeight=330,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brush(Pale),FontFamily=FontFamily };
        var body=new DockPanel { Margin=new Thickness(24) };
        var heading=Text(StateString(plan,"repository","")+" → "+StateString(plan,"directory",""),18,Ink,true); heading.TextWrapping=TextWrapping.Wrap; DockPanel.SetDock(heading,Dock.Top); body.Children.Add(heading);
        string source=StateString(plan,"contentKind","")=="code" ? StateString(plan,"branch","")+" / "+StateString(plan,"prefix","")+" · "+StateString(plan,"sourceId","") : StateString(plan,"tag",StateString(plan,"releaseId",""));
        var sourceLabel=Text(L("Quelle: ","Источник: ","Source: ")+source,14,Muted,false);sourceLabel.TextWrapping=TextWrapping.Wrap;sourceLabel.Margin=new Thickness(0,8,0,0);DockPanel.SetDock(sourceLabel,Dock.Top);body.Children.Add(sourceLabel);
        var notice=Text(L("Ersetzungen werden gesichert. Andere lokale Dateien bleiben erhalten. Abbrechen ändert nichts.","Заменяемые файлы сохранятся в резервной копии. Остальные локальные файлы не удаляются. Отмена ничего не изменит.","Replaced files are backed up. Other local files are retained. Cancel changes nothing."),15,Muted,false); notice.TextWrapping=TextWrapping.Wrap; notice.Margin=new Thickness(0,12,0,12); DockPanel.SetDock(notice,Dock.Top); body.Children.Add(notice);
        var buttons=new System.Windows.Controls.Primitives.UniformGrid { Columns=2, Margin=new Thickness(0,14,0,0) }; DockPanel.SetDock(buttons,Dock.Bottom); body.Children.Add(buttons);
        var cancel=MakeButton(L("Abbrechen","Отмена","Cancel"),false,0); cancel.Name="DownloadCancel";cancel.IsCancel=true; cancel.Margin=new Thickness(0,0,6,0); cancel.Click+=delegate {dialog.DialogResult=false;}; buttons.Children.Add(cancel);
        var confirm=MakeButton(L("Herunterladen / ersetzen","Скачать / заменить","Download / replace"),true,0); confirm.Name="DownloadConfirm"; confirm.Margin=new Thickness(6,0,0,0); confirm.Click+=delegate {dialog.DialogResult=true;}; buttons.Children.Add(confirm);
        var rows=new StackPanel(); body.Children.Add(new ScrollViewer { Style=ScrollGutterStyle(), Content=rows,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled });
        foreach(object value in (IEnumerable)plan["entries"]) {
            var entry=value as Dictionary<string,object>; if(entry==null) continue; string action=StateString(entry,"action","");
            string label=action=="same" ? L("Unverändert","Совпадает","Unchanged") : action=="update" ? L("Ersetzen","Заменится","Replace") : L("Herunterladen","Скачается","Download");
            var row=Text(label+" · "+StateString(entry,"name","")+" · "+FormatSize(StateLong(entry,"length")),15,Ink,false); row.TextWrapping=TextWrapping.Wrap; row.Margin=new Thickness(0,6,0,6); rows.Children.Add(row);
        }
        dialog.Content=body; dialog.Loaded+=delegate {cancel.Focus();}; if(dialog.ShowDialog()!=true) {status.Text=L("Abgebrochen. Keine Dateien geändert.","Отменено. Файлы не изменялись.","Cancelled. No files changed.");status.Visibility=Visibility.Visible;return;}
        pendingDownload=SyncRequest("download"); pendingDownload["plan"]=plan;
        syncControlFile=Path.Combine(PrivateState,"control-"+Guid.NewGuid().ToString("N")); pendingDownload["controlFile"]=syncControlFile;
        File.WriteAllText(PendingPath,json.Serialize(pendingDownload),new UTF8Encoding(false)); resumeButton.Visibility=Visibility.Collapsed;
        LaunchSync(pendingDownload,true,delegate(Dictionary<string,object> snapshot) {
            if(StateString(snapshot,"state","")=="completed") { if(File.Exists(PendingPath)) File.Delete(PendingPath); resumeButton.Visibility=Visibility.Collapsed; }
            else resumeButton.Visibility=Visibility.Visible;
            if(exitAfterPause) ShutdownNow();
        });
    }
    private void PauseDownload()
    {
        if(downloadMode && uploadRunning && !checking && !String.IsNullOrEmpty(syncControlFile)) { File.WriteAllText(syncControlFile,"pause"); pauseButton.IsEnabled=false; }
    }
    private void RestorePendingDownload()
    {
        if(uploadRunning || !File.Exists(PendingPath)) return;
        try {
            var request=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(PendingPath,Encoding.UTF8)); var plan=(Dictionary<string,object>)request["plan"];
            // Restore the whole source atomically. ChangeDirection would launch a
            // catalog job for the previously displayed project before these fields.
            if(!downloadMode)RememberSelection();
            downloadMode=true;CancelFileScan();fileChecks.Clear();fileStatusLabels.Clear();ResetTableItems();ResetResult();localResultDirectory="";changingDirection=true;
            try { repository.Text=StateString(plan,"repository",""); modePicker.SelectedIndex=StateString(plan,"contentKind","")=="code" ? 0 : 1; codeBranch.Text=StateString(plan,"branch",""); repositoryDestination.Text=StateString(plan,"prefix",""); folderPath.Text=StateString(plan,"directory",""); selectedReleaseId=StateLong(plan,"releaseId"); }
            finally { changingDirection=false; }
            lastModeIndex=modePicker.SelectedIndex;
            if(!CodeMode)config["DownloadReleaseLabel"]=StateString(plan,"tag",selectedReleaseId.ToString());
            folderPath.ToolTip=folderPath.Text;
            remoteCatalog=plan; foreach(object item in (IEnumerable)plan["entries"]) {var entry=item as Dictionary<string,object>;if(entry!=null)resumeSelections.Add(StateString(entry,"name",""));} ApplyLanguage(); PopulateRemoteFiles();
            SaveSyncPreferences();
            // The next click performs a fresh read-only preview and asks again about replacements.
            status.Text=L("Download erneut bestätigen. Vorhandene Teilstücke werden verwendet.","Подтвердите скачивание ещё раз. Сохранённые части будут использованы.","Confirm download again. Saved partial files will be reused."); status.Visibility=Visibility.Visible;
        } catch { ShowError(L("Gespeicherte Aufgabe nicht lesbar. Dateien erneut auswählen.","Не удалось прочитать задачу. Выберите файлы заново.","Cannot read saved task. Select files again.")); }
    }
    private void LaunchSync(Dictionary<string,object> request,bool download,Action<Dictionary<string,object>> completed)
    {
        if(uploadRunning) return;
        if(!File.Exists(Path.Combine(root,"Release-UploadWatchdog.ps1")) || !File.Exists(Path.Combine(root,"Sync-Operations.ps1"))) { ShowIncompletePackage(); return; }
        phase=download ? "preparing" : "checking"; checking=!download; latestSnapshot=null; confirmedFiles.Clear(); currentFile=""; localResultDirectory=""; progressPath=null; lastProgressRead=0;
        SetBusy(true); ApplyLanguage();
        RunSyncWorker(request,download,delegate(Dictionary<string,object> snapshot) {
            activeWorker=null; progressPath=null; latestSnapshot=null; phase=StateString(snapshot,"state","failed"); SetBusy(false);
            if(phase.StartsWith("sync-",StringComparison.Ordinal)) phase="ready";
            else ApplyProgress(snapshot);
            ApplyLanguage();
            if(StateString(snapshot,"state","")=="failed") { status.Text=L("Fehler: ","Ошибка: ","Failed: ")+StateString(snapshot,"message",""); status.Visibility=Visibility.Visible; }
            if(completed!=null) completed(snapshot);
        },true);
    }
    private void RunSyncWorker(Dictionary<string,object> request,bool download,Action<Dictionary<string,object>> completed,bool foreground)
    {
        string id=Guid.NewGuid().ToString("N"), input=Path.Combine(PrivateState,"SyncRequest-"+id+".json"), output=Path.Combine(PrivateState,"progress-"+id+".json");
        File.WriteAllText(input,json.Serialize(request),new UTF8Encoding(false));
        var info=CreateWorkerInfo(download,output); info.Arguments=info.Arguments.Replace(" -CheckOnly","")+" -SyncRequest \""+input+"\""+(download ? "" : " -CheckOnly");
        var process=new Process { StartInfo=info,EnableRaisingEvents=true }; process.OutputDataReceived+=delegate {}; process.ErrorDataReceived+=delegate {};
        if(foreground) { progressPath=output; activeWorker=process; }
        process.Exited+=delegate {
            Dictionary<string,object> snapshot;
            try { snapshot=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(output,Encoding.UTF8)); }
            catch { snapshot=new Dictionary<string,object>{{"state","failed"},{"message",L("Keine Abschlussbestätigung.","Нет подтверждения завершения.","No completion confirmation.")}}; }
            Dispatcher.BeginInvoke(new Action(delegate {
                process.Dispose(); try {File.Delete(input);File.Delete(output);} catch {}
                completed(snapshot);
            }));
        };
        try { if(!process.Start()) throw new IOException("Worker start failed"); process.BeginOutputReadLine(); process.BeginErrorReadLine(); }
        catch(Exception ex) { process.Dispose(); try{File.Delete(input);}catch{} if(foreground){activeWorker=null;SetBusy(false);} completed(new Dictionary<string,object>{{"state","failed"},{"message",ex.Message}}); }
    }
    private void ChooseSyncCatalog(string kind)
    {
        var request=SyncRequest(kind);
        LaunchSync(request,false,delegate(Dictionary<string,object> snapshot) {
            if(StateString(snapshot,"state","")!="sync-choices") return;
            var choices=new List<CatalogChoice>(); foreach(object item in (IEnumerable)snapshot["choices"]) { var value=item as Dictionary<string,object>; if(value!=null) choices.Add(new CatalogChoice{Value=StateString(value,"value",""),Label=StateString(value,"label","")}); }
            if(choices.Count==0) { status.Text=L("Keine veröffentlichten Einträge. GitHub-Link kann direkt eingefügt werden.","Нет доступных записей. Ссылку на проект можно вставить вручную.","No available entries. You can paste a project link directly."); status.Visibility=Visibility.Visible; return; }
            var dialog=new Window { Owner=this,Title=L("Quelle auswählen","Выбрать источник","Choose source"),Width=610,Height=400,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brush(Pale),FontFamily=FontFamily };
            var body=new DockPanel {Margin=new Thickness(20)}; var choose=MakeButton(L("Auswählen","Выбрать","Choose"),true,0); choose.Margin=new Thickness(0,12,0,0); DockPanel.SetDock(choose,Dock.Bottom); body.Children.Add(choose);
            var list=new ListBox {ItemsSource=choices,DisplayMemberPath="Label",SelectedIndex=0,FontSize=16}; body.Children.Add(list); dialog.Content=body; choose.Click+=delegate{dialog.DialogResult=true;};
            if(dialog.ShowDialog()!=true || list.SelectedItem==null) return; var choice=(CatalogChoice)list.SelectedItem;
            if(kind=="projects") repository.Text=choice.Value;
            else if(kind=="branches") {codeBranch.Text=choice.Value;syncBranch=choice.Value;}
            else {selectedReleaseId=Int64.Parse(choice.Value);config["DownloadReleaseLabel"]=choice.Label;}
            RefreshRemoteFiles();
        });
    }
    private void EstablishMonitor(Dictionary<string,object> request,Dictionary<string,object> catalog)
    {
        monitoredKey=SyncKey(request); monitorEtag=StateString(catalog,"etag",""); monitorFingerprint=StateString(catalog,"fingerprint",""); monitorSourceId=StateString(catalog,"sourceId",""); nextMonitorCheck=DateTime.UtcNow.AddMinutes(15);
        SaveMonitorState();
    }
    private void SaveMonitorState(){try{File.WriteAllText(Path.Combine(PrivateState,"monitor.json"),json.Serialize(new Dictionary<string,string>{{"key",monitoredKey},{"etag",monitorEtag},{"fingerprint",monitorFingerprint},{"sourceId",monitorSourceId},{"lastNotified",lastNotified}}),new UTF8Encoding(false));}catch{}}
    private void CheckRemoteUpdates(bool manual)
    {
        if(monitorBusy || uploadRunning || (!manual && monitorCheck.IsChecked!=true)) return;
        var request=SyncRequest("updates"); if(StateString(request,"repository","").Length==0) return;
        string key=SyncKey(request); if(key!=monitoredKey){monitorEtag="";monitorFingerprint="";monitorSourceId="";lastNotified="";}
        request["etag"]=monitorEtag; request["sourceId"]=monitorSourceId; monitorBusy=true; updatesButton.IsEnabled=false; nextMonitorCheck=DateTime.UtcNow.AddMinutes(15);
        RunSyncWorker(request,false,delegate(Dictionary<string,object> snapshot) {
            monitorBusy=false; updatesButton.IsEnabled=!uploadRunning;
            if(SyncKey(SyncRequest("updates"))!=key) return;
            object raw;
            if(snapshot.TryGetValue("syncCatalog",out raw) && raw is Dictionary<string,object>) {
                observerFailed=false;
                var catalog=(Dictionary<string,object>)raw;
                bool unchanged=StateString(catalog,"notModified","false").Equals("true",StringComparison.OrdinalIgnoreCase);
                string fingerprint=StateString(catalog,"fingerprint",monitorFingerprint), revision=StateString(catalog,"sourceId",monitorSourceId)+"|"+fingerprint;
                bool changed=!unchanged && monitoredKey==key && monitorFingerprint.Length>0 && fingerprint!=monitorFingerprint;
                if(!unchanged) EstablishMonitor(request,catalog);
                if(changed) {
                    updatesText.Text=L("Updates auf GitHub. Dateiliste aktualisieren und Änderungen vor dem Download prüfen.","На GitHub есть изменения. Обновите список и проверьте их перед скачиванием.","GitHub has changes. Refresh the list and review before downloading.");
                    if(lastNotified!=revision && tray!=null) {tray.ShowBalloonTip(6000,"GitHubSync",updatesText.Text,Forms.ToolTipIcon.Info);lastNotified=revision;SaveMonitorState();}
                    if(!downloadMode && manual) status.Text=updatesText.Text;
                } else if(manual) {updatesText.Text=L("Keine neuen Änderungen. Prüfung ändert keine Dateien.","Новых изменений нет. Проверка не изменяет файлы.","No new changes. Checking does not modify files.");}
            } else {
                updatesText.Text=L("Prüfung nicht möglich. Verbindung, Zugriff oder Anmeldung prüfen.","Проверка недоступна. Проверьте соединение, доступ или вход.","Check unavailable. Check connection, access or sign-in.");
                if(!observerFailed && tray!=null){tray.ShowBalloonTip(6000,"GitHubSync",updatesText.Text,Forms.ToolTipIcon.Warning);observerFailed=true;}
                nextMonitorCheck=DateTime.UtcNow.AddMinutes(30); long epoch=StateLong(snapshot,"rateReset"), seconds=StateLong(snapshot,"retryAfter");
                if(epoch>0) nextMonitorCheck=new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(epoch+5); else if(seconds>0) nextMonitorCheck=DateTime.UtcNow.AddSeconds(Math.Max(900,seconds));
            }
            updatesText.Visibility=String.IsNullOrEmpty(updatesText.Text) ? Visibility.Collapsed : Visibility.Visible;
        },false);
    }
    private void ApplyDownloadProgress(Dictionary<string,object> snapshot)
    {
        if(StateString(snapshot,"direction","")!="download") return;
        if(phase=="completed") {
            localResultDirectory=StateString(snapshot,"localDirectory",""); openFilesButton.Visibility=Directory.Exists(localResultDirectory) ? Visibility.Visible : Visibility.Collapsed;
            status.Text=StateString(snapshot,"verification","")=="hash" ? L("Dateien heruntergeladen und Prüfsummen bestätigt.","Файлы скачаны, контрольные суммы подтверждены.","Files downloaded and checksums confirmed.") : L("Dateien heruntergeladen; Größe geprüft. GitHub liefert nicht für alle Dateien eine Prüfsumme.","Файлы скачаны; размер проверен. GitHub не предоставил контрольную сумму для всех файлов.","Files downloaded; sizes checked. GitHub did not provide checksums for every file."); status.Visibility=Visibility.Visible;
        } else if(phase=="paused") { progressTitle.Text=L("Download pausiert","Скачивание приостановлено","Download paused"); status.Text=L("Teilstücke gespeichert. Fortsetzen ist nach Prüfung möglich.","Скачанные части сохранены. Можно продолжить после проверки.","Partial files saved. Resume after review."); status.Visibility=Visibility.Visible; }
        else if(phase=="verifying") progressTitle.Text=L("Prüfsumme prüfen","Проверка контрольной суммы","Verifying checksum");
        if(StateString(snapshot,"message","")=="range-restart") {status.Text=L("Server unterstützt hier kein Fortsetzen; Datei beginnt erneut.","Сервер не поддерживает докачку этого файла: скачивание начато заново.","Server does not support resuming this file; restarting it.");status.Visibility=Visibility.Visible;}
    }
    private void ShowIncompletePackage() { ShowError(L("Portable-Archiv vollständig entpacken (EXE, Worker, src und runtime). Nicht nur die EXE aus artifacts/build starten.","Распакуйте portable-архив целиком: EXE, worker, src и runtime. Не запускайте отдельную EXE из artifacts/build.","Extract the entire portable archive: EXE, worker, src and runtime. Do not run the bare EXE from artifacts/build.")); }
    internal void InitializeTray()
    {
        try {
            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Watchdog.IconIco")) if(stream!=null) trayFrames.Add(ReadTrayIcon(stream));
            for(int index=1;index<=12;index++) {
                using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Sync.Frame"+index)) {
                    if(stream==null) break;var frame=ReadWindowIcon(stream);frame.Freeze();windowFrames.Add(frame);stream.Position=0;trayFrames.Add(ReadTrayIcon(stream));
                }
            }
            if(trayFrames.Count==0) {Application.Current.ShutdownMode=ShutdownMode.OnMainWindowClose;return;}
            trayMenu=new Forms.ContextMenuStrip(); tray=new Forms.NotifyIcon {Icon=trayFrames[0],Text="GitHubSync",Visible=true,ContextMenuStrip=trayMenu}; tray.DoubleClick+=delegate {Dispatcher.BeginInvoke(new Action(RestoreWindow));}; tray.BalloonTipClicked+=delegate{Dispatcher.BeginInvoke(new Action(delegate{RestoreWindow();if(!uploadRunning)ChangeDirection(true);}));}; UpdateTrayMenu();
            trayAnimation=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(1000.0/6)}; trayAnimation.Tick+=delegate { AnimateTray(); }; trayAnimation.Start();
            Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs args) {if(!allowWindowClose){args.Cancel=true;Hide();}};
            Closed+=delegate {if(monitorTimer!=null)monitorTimer.Stop();if(trayAnimation!=null)trayAnimation.Stop();tray.Visible=false;tray.Dispose();trayMenu.Dispose();foreach(var icon in trayFrames)icon.Dispose();};
            Application.Current.SessionEnding+=delegate {allowWindowClose=true;PauseDownload();};
        } catch(Exception ex) {trayFailure=ex.Message;if(tray!=null)tray.Dispose(); tray=null; allowWindowClose=true; Application.Current.ShutdownMode=ShutdownMode.OnMainWindowClose; }
    }
    private static System.Drawing.Icon ReadTrayIcon(Stream stream)
    {
        // Let Windows select the dedicated ICO size for the tray instead of
        // downscaling the 32px WPF window bitmap. All frames have their own alpha.
        using(var icon=new System.Drawing.Icon(stream,Forms.SystemInformation.SmallIconSize)) return (System.Drawing.Icon)icon.Clone();
    }
    private void AnimateTray()
    {
        if(tray==null) return; bool active=uploadRunning && !checking;
        if(active && trayFrames.Count>1) { trayFrame=(trayFrame+1)%(trayFrames.Count-1);tray.Icon=trayFrames[trayFrame+1];if(windowFrames.Count>trayFrame)Icon=windowFrames[trayFrame]; }
        else if(tray.Icon!=trayFrames[0]) {tray.Icon=trayFrames[0];using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Watchdog.IconIco"))Icon=ReadWindowIcon(stream);}
    }
    private void UpdateTrayMenu()
    {
        if(trayMenu==null) return; trayMenu.Items.Clear();
        trayMenu.Items.Add(L("Öffnen","Открыть","Open"),null,delegate {Dispatcher.BeginInvoke(new Action(RestoreWindow));});
        trayMenu.Items.Add(L("Updates prüfen","Проверить обновления","Check updates"),null,delegate {Dispatcher.BeginInvoke(new Action(delegate {CheckRemoteUpdates(true);}));});
        if(downloadMode && uploadRunning && !checking) trayMenu.Items.Add(L("Download pausieren","Приостановить скачивание","Pause download"),null,delegate {Dispatcher.BeginInvoke(new Action(PauseDownload));});
        else if(File.Exists(PendingPath)) trayMenu.Items.Add(L("Fortsetzen…","Продолжить…","Resume…"),null,delegate {Dispatcher.BeginInvoke(new Action(delegate{RestoreWindow();RestorePendingDownload();}));});
        trayMenu.Items.Add(new Forms.ToolStripSeparator());trayMenu.Items.Add(L("Beenden","Выход","Exit"),null,delegate{Dispatcher.BeginInvoke(new Action(RequestExit));});
    }
    internal void RestoreWindow() {Show();if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;Activate();}
    private void RequestExit()
    {
        if(uploadRunning && downloadMode && !checking) {exitAfterPause=true;PauseDownload();return;}
        if(uploadRunning) {
            RestoreWindow(); if(!ConfirmExit())return;
            if(activeWorker!=null)try{activeWorker.Kill();}catch{}
        }
        ShutdownNow();
    }
    private bool ConfirmExit()
    {
        var dialog=new Window {Owner=this,Title=L("Vorgang beenden?","Прервать операцию?","Stop the operation?"),Width=580,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brush(Pale),FontFamily=FontFamily};
        var body=new StackPanel {Margin=new Thickness(24)};
        var text=Text(checking ? L("Prüfung abbrechen und Programm beenden?","Остановить проверку и выйти из программы?","Cancel the check and exit?") : L("GitHub kann bereits Dateien gespeichert haben. Vor erneutem Senden Ergebnis prüfen.","Часть файлов уже могла сохраниться на GitHub. Перед повтором проверьте результат.","GitHub may already contain some files. Check the result before retrying."),16,Ink,false);text.TextWrapping=TextWrapping.Wrap;body.Children.Add(text);
        var buttons=new System.Windows.Controls.Primitives.UniformGrid {Columns=2,Margin=new Thickness(0,20,0,0)};
        var cancel=MakeButton(L("Weiterarbeiten","Продолжить работу","Keep working"),false,0);cancel.Name="ExitCancel";cancel.IsCancel=true;cancel.Margin=new Thickness(0,0,6,0);cancel.Click+=delegate{dialog.DialogResult=false;};
        var exit=MakeButton(L("Abbrechen und beenden","Прервать и выйти","Stop and exit"),true,0);exit.Margin=new Thickness(6,0,0,0);exit.Click+=delegate{dialog.DialogResult=true;};buttons.Children.Add(cancel);buttons.Children.Add(exit);body.Children.Add(buttons);dialog.Content=body;dialog.Loaded+=delegate{cancel.Focus();};return dialog.ShowDialog()==true;
    }
    private void ShutdownNow() {allowWindowClose=true;Close();Application.Current.Shutdown();}
}
