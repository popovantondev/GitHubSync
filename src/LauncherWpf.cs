using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

[assembly: AssemblyTitle("GitHubSync")]
[assembly: AssemblyDescription("Send and download GitHub files with controlled updates and resumable downloads")]
[assembly: AssemblyCompany("Anton Popov")]
[assembly: AssemblyProduct("GitHubSync")]
[assembly: AssemblyVersion("1.5.2.0")]
[assembly: AssemblyFileVersion("1.5.2.0")]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.6.2")]

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Forms.Application.EnableVisualStyles();
        RenderOptions.ProcessRenderMode=System.Windows.Interop.RenderMode.SoftwareOnly;
        bool owner;
        string key = System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
        using (var mutex = new System.Threading.Mutex(true, "Local\\GitHubSync.UI." + key, out owner))
        {
            if (!owner) { try { using(var signal=System.Threading.EventWaitHandle.OpenExisting("Local\\GitHubSync.Show."+key)) signal.Set(); } catch { } return; }
            using (var signal=new System.Threading.EventWaitHandle(false,System.Threading.EventResetMode.AutoReset,"Local\\GitHubSync.Show."+key))
            {
                var app = new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
                var window = new WatchdogWindow(); window.InitializeTray();
                var wait=System.Threading.ThreadPool.RegisterWaitForSingleObject(signal,delegate {window.Dispatcher.BeginInvoke(new Action(window.RestoreWindow));},null,-1,false);
                try { app.Run(window); } finally { wait.Unregister(null); mutex.ReleaseMutex(); }
            }
        }
    }
}

internal sealed partial class WatchdogWindow : Window
{
    private static readonly Color Ink = Hex("#252B33");
    private static readonly Color Muted = Hex("#66707C");
    private static readonly Color BorderColor = Hex("#DDE0E4");
    private static readonly Color Accent = Hex("#F39712");
    private static readonly Color AccentHover = Hex("#D87B0F");
    private static readonly Color Pale = Hex("#F7F7F8");
    private const double CardRadius = 14, ControlRadius = 9, FieldHeight = 44, ScrollGutter = 12;
    private ScrollViewer contentScroll, listScroll;
    private Grid tableHeading;
    private Border progressFrame;
    private static readonly Color White = Colors.White;
    private readonly string root = AppDomain.CurrentDomain.BaseDirectory;
    private readonly JavaScriptSerializer json = new JavaScriptSerializer {MaxJsonLength=32*1024*1024};
    private Dictionary<string, object> config = new Dictionary<string, object>();
    private readonly List<CheckBox> fileChecks = new List<CheckBox>();
    private ComboBox language;
    private TextBox repository;
    private TextBox releaseTag;
    private TextBox folderPath;
    private TextBlock titleText;
    private TextBlock subtitleText;
    private TextBlock targetTitle;
    private TextBlock repoLabel;
    private TextBlock tagLabel;
    private TextBlock filesTitle;
    private TextBlock filesHint;
    private TextBlock selectedCount;
    private TextBlock status;
    private TextBlock footer;
    private TextBlock advancedLabel;
    private TextBlock retriesLabel;
    private TextBlock delayLabel;
    private TextBlock pollLabel;
    private TextBlock timeoutLabel;
    private TextBlock waitLabel;
    private Button browseButton;
    private Button selectAllButton;
    private Button clearButton;
    private Button saveButton;
    private Button checkButton;
    private Button startButton;
    private Button guideButton;
    private Button openFilesButton;
    private ComboBox modePicker;
    private CheckBox publishCheck;
    private StackPanel publishRow;
    private TextBlock publishCaption;
    private TextBox repositoryDestination, codeBranch;
    private FrameworkElement releaseTargetRow, codeTargetRow, codeOptions;
    private Button addFilesButton, publishLaterButton, copyResultButton, branchesButton;
    private Button downloadLinksButton;
    private TextBlock modeLabel, branchLabel;
    private bool preparingCodeUpload;
    private readonly Dictionary<string, string> resultDownloads = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private bool CodeMode { get { return modePicker != null && modePicker.SelectedIndex == 0; } }
    private string uploadedReleaseUrl = "";
    private Button signInButton;
    private Button projectsButton, draftsButton;
    private string catalogRequest = "";
    private string draftRequestPath;
    private TextBlock simpleHint;
    private TextBlock accountText;
    private bool signingIn;
    private string accountLogin = "";
    private ListBox fileList;
    private Expander advanced;
    private TextBox retries;
    private TextBox delay;
    private TextBox poll;
    private TextBox timeout;
    private TextBox waitPid;
    private bool uploadRunning;
    private string progressPath;
    private DispatcherTimer progressTimer;
    private TextBlock progressTitle;
    private TextBlock progressOverallText;
    private TextBlock progressFileText;
    private ProgressBar overallBar;
    private ProgressBar fileBar;
    private int lastProgressRead;
    private TextBlock folderLabel, nameHeader, sizeHeader, stateHeader;
    private readonly TextBlock[] stageLabels = new TextBlock[3];
    private readonly Border[] stageDots = new Border[3];
    private readonly Border[] stageFrames = new Border[3];
    private readonly Dictionary<string, TextBlock> fileStatusLabels = new Dictionary<string, TextBlock>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> confirmedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private string currentFile = "";
    private string phase = "ready";
    private bool checking;
    private BitmapSource brandImage;
    private Dictionary<string, object> latestSnapshot;
    private TextBlock emptyFilesLabel;
    private string emptyFilesState;

    public WatchdogWindow()
    {
        Title = "GitHubSync";
        Width = Math.Min(1080, SystemParameters.WorkArea.Width - 32);
        Height = Math.Min(1040, SystemParameters.WorkArea.Height - 32);
        MinWidth = Math.Min(900, Width);
        MinHeight = Math.Min(700, Height);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResize;
        Background = Brush(Pale);
        FontFamily = new FontFamily("Segoe UI");
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        BitmapSource windowIcon = null;
        try
        {
            using (var embedded = Assembly.GetExecutingAssembly().GetManifestResourceStream("Watchdog.Icon"))
            {
                if (embedded != null) brandImage = BitmapFrame.Create(embedded, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            }
            using (var embeddedIcon = Assembly.GetExecutingAssembly().GetManifestResourceStream("Watchdog.IconIco"))
            {
                if (embeddedIcon != null) windowIcon = ReadWindowIcon(embeddedIcon);
            }
            if (brandImage == null)
            {
                var iconFile = Path.Combine(root, "assets", "sync.png");
                if (File.Exists(iconFile)) brandImage = new BitmapImage(new Uri(iconFile));
            }
            if (windowIcon == null)
            {
                var iconFile = Path.Combine(root, "assets", "sync.ico");
                if (File.Exists(iconFile)) using (var iconStream = File.OpenRead(iconFile)) windowIcon = ReadWindowIcon(iconStream);
            }
            if (brandImage != null) brandImage.Freeze();
            if (windowIcon == null) windowIcon = brandImage;
            if (windowIcon != null) { windowIcon.Freeze(); Icon = windowIcon; }
        }
        catch { /* The system title-bar icon is optional. */ }

        ReadConfig();
        BuildView();
        language.SelectedIndex = ReadLanguage();
        ApplyLanguage();
        LoadFiles();
        progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        progressTimer.Tick += delegate { ReadProgress(); };
        progressTimer.Start();
        Closed += delegate { CancelFileScan(); progressTimer.Stop(); if (!String.IsNullOrEmpty(progressPath)) try { if (File.Exists(progressPath)) File.Delete(progressPath); } catch { } };
    }

    private static Color Hex(string value) { return (Color)ColorConverter.ConvertFromString(value); }
    private static BitmapSource ReadWindowIcon(Stream stream)
    {
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames.OrderBy(item => Math.Abs(item.PixelWidth - 32) + Math.Abs(item.PixelHeight - 32)).FirstOrDefault();
        return frame;
    }
    private static readonly Dictionary<Color,SolidColorBrush> themeBrushes = new Dictionary<Color,SolidColorBrush>();
    private static SolidColorBrush Brush(Color color)
    {
        SolidColorBrush brush;
        if (!themeBrushes.TryGetValue(color,out brush)) { brush = new SolidColorBrush(color); brush.Freeze(); themeBrushes[color] = brush; }
        return brush;
    }
    private int LanguageIndex { get { return language == null || language.SelectedIndex < 0 ? 0 : language.SelectedIndex; } }
    private string L(string de, string ru, string en) { return LanguageIndex == 1 ? ru : (LanguageIndex == 2 ? en : de); }

    private void BuildView()
    {
        var shell = new Grid { Background = Brush(Pale) };
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Content = shell;
        var rail = new Border { Background = Brush(Hex("#F2F2F3")), BorderBrush = Brush(BorderColor), BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(12, 24, 12, 20) };
        shell.Children.Add(rail);
        var railGrid = new Grid();
        railGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        railGrid.RowDefinitions.Add(new RowDefinition());
        railGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rail.Child = railGrid;
        var stages = new StackPanel();
        railGrid.Children.Add(stages);
        for (int i = 0; i < 3; i++)
        {
            var stageRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            stageDots[i] = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Background = Brush(Muted), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            stageLabels[i] = Text("", 16, Ink, false);
            stageRow.Children.Add(stageDots[i]);
            stageRow.Children.Add(stageLabels[i]);
            stageFrames[i] = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 14, 8, 14), Child = stageRow };
            stages.Children.Add(stageFrames[i]);
            if (i < 2) stages.Children.Add(new Border { Width = 1, Height = 36, Background = Brush(BorderColor), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(20, 4, 0, 4) });
        }
        var version = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var applicationName = Text("GitHubSync", 14, Muted, false);
        applicationName.TextAlignment = TextAlignment.Center;
        version.Children.Add(applicationName);
        var versionNumber = Text("v1.5.2", applicationName.FontSize - 2, Muted, false);
        versionNumber.TextAlignment = TextAlignment.Center;
        versionNumber.Margin = new Thickness(0, 2, 0, 0);
        version.Children.Add(versionNumber);
        var services = new StackPanel();
        Grid.SetRow(services, 2);
        railGrid.Children.Add(services);

        var main = new Grid { Margin = new Thickness(24) };
        Grid.SetColumn(main, 1);
        shell.Children.Add(main);
        main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 24) };
        main.Children.Add(header);
        var brand = new Grid();
        brand.ColumnDefinitions.Add(new ColumnDefinition());
        brand.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(brand);
        titleText = Text("GitHubSync", 32, Ink, true);
        titleText.VerticalAlignment = VerticalAlignment.Center;
        titleText.Margin = new Thickness(0);
        Grid.SetColumn(titleText, 0);
        brand.Children.Add(titleText);
        subtitleText = Text("", 16, Muted, false);
        subtitleText.TextWrapping = TextWrapping.Wrap;
        subtitleText.Margin = new Thickness(0, 6, 0, 0);
        header.Children.Add(subtitleText);
        var modeRow = new Grid { Width = 360, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        modeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); modeRow.ColumnDefinitions.Add(new ColumnDefinition());
        modeLabel = Text("", 15, Ink, true); modeLabel.VerticalAlignment = VerticalAlignment.Center; modeLabel.Margin = new Thickness(0, 0, 16, 0); modeRow.Children.Add(modeLabel);
        modePicker = MakeLanguagePicker(); modePicker.Width = Double.NaN; modePicker.Height = 38; modePicker.FontSize = 15;
        modePicker.Items.Add(new ComboBoxItem()); modePicker.Items.Add(new ComboBoxItem()); modePicker.SelectedIndex = StringValue("UploadMode", "release") == "code" ? 0 : 1;
        Grid.SetColumn(modePicker, 1); modeRow.Children.Add(modePicker); Grid.SetColumn(modeRow, 1); brand.Children.Add(modeRow);
        var headerActions = new StackPanel { Margin = new Thickness(0, 24, 0, 0) };
        services.Children.Add(headerActions);
        guideButton = MakeButton("", false, 0);
        guideButton.Height = 36;
        guideButton.FontSize = 14;
        guideButton.Click += delegate { OpenGuide(); };
        headerActions.Children.Add(guideButton);
        signInButton = MakeButton("", false, 0);
        signInButton.Height = 36;
        signInButton.FontSize = 14;
        signInButton.Padding = new Thickness(8, 0, 8, 0);
        signInButton.Margin = new Thickness(0, 12, 0, 0);
        signInButton.Click += delegate { if (!uploadRunning) Launch("SignIn", false); };
        headerActions.Children.Add(signInButton);
        accountText = Text("", 13, Muted, false);
        accountText.TextWrapping = TextWrapping.Wrap;
        accountText.TextAlignment = TextAlignment.Center;
        accountText.Margin = new Thickness(0, 6, 0, 0);
        headerActions.Children.Add(accountText);
        language = MakeLanguagePicker();
        language.Width = Double.NaN;
        language.Height = 36;
        language.FontSize = 14;
        language.Margin = new Thickness(0, 12, 0, 0);
        language.Items.Add("Deutsch");
        language.Items.Add("Русский");
        language.Items.Add("English");
        language.SelectionChanged += delegate { SaveLanguage(); ApplyLanguage(); };
        headerActions.Children.Add(language);
        version.Margin = new Thickness(0, 18, 0, 0);
        services.Children.Add(version);

        var scrolling = new ScrollViewer { Style = ScrollGutterStyle(), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        contentScroll = scrolling;
        scrolling.SizeChanged += delegate { QueueLayout(); };
        Loaded += delegate { QueueLayout(); };
        Grid.SetRow(scrolling, 1);
        main.Children.Add(scrolling);
        var page = new StackPanel();
        scrolling.Content = page;

        var targetCard = Card();
        page.Children.Add(targetCard);
        var targetBody = new StackPanel();
        targetCard.Child = targetBody;
        targetTitle = Text("", 20, Ink, true);
        targetTitle.Visibility = Visibility.Collapsed;
        targetBody.Children.Add(targetTitle);
        var targets = new Grid();
        targets.ColumnDefinitions.Add(new ColumnDefinition());
        targets.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        targets.ColumnDefinitions.Add(new ColumnDefinition());
        targetBody.Children.Add(targets);
        var repoPanel = new StackPanel();
        targets.Children.Add(repoPanel);
        repoLabel = Text("", 16, Ink, true);
        repoPanel.Children.Add(repoLabel);
        repository = Input();
        System.Windows.Automation.AutomationProperties.SetLabeledBy(repository, repoLabel);
        repository.Text = StringValue("Repository", "OWNER/REPOSITORY");
        if (repository.Text == "OWNER/REPOSITORY") repository.Clear();
        var repoRow = new Grid();
        repoRow.ColumnDefinitions.Add(new ColumnDefinition());
        repoRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        repoPanel.Children.Add(repoRow);
        repoRow.Children.Add(repository);
        projectsButton = MakeButton("", false, 0);
        projectsButton.Height = FieldHeight;
        projectsButton.FontSize = 14;
        projectsButton.Padding = new Thickness(12, 0, 12, 0);
        projectsButton.Margin = new Thickness(8, 6, 0, 0);
        projectsButton.Click += delegate { if (!uploadRunning) { if(downloadMode) ChooseSyncCatalog("projects"); else Launch("ListProjects", false); } };
        Grid.SetColumn(projectsButton, 1);
        repoRow.Children.Add(projectsButton);
        var tagPanel = new StackPanel();
        Grid.SetColumn(tagPanel, 2);
        targets.Children.Add(tagPanel);
        tagLabel = Text("", 16, Ink, true);
        tagPanel.Children.Add(tagLabel);
        releaseTag = Input();
        System.Windows.Automation.AutomationProperties.SetLabeledBy(releaseTag, tagLabel);
        releaseTag.Text = StringValue("ReleaseTag", "v1.0.0");
        if (String.IsNullOrWhiteSpace(repository.Text)) releaseTag.Clear();
        releaseTag.IsReadOnly = true;
        var tagRow = new Grid();
        tagRow.ColumnDefinitions.Add(new ColumnDefinition());
        tagRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        tagPanel.Children.Add(tagRow);
        tagRow.Children.Add(releaseTag);
        draftsButton = MakeButton("", false, 0);
        draftsButton.Height = FieldHeight;
        draftsButton.FontSize = 14;
        draftsButton.Padding = new Thickness(12, 0, 12, 0);
        draftsButton.Margin = new Thickness(8, 6, 0, 0);
        draftsButton.Click += delegate { if(downloadMode) ChooseSyncCatalog("releases"); else LoadDraftChoices(); };
        Grid.SetColumn(draftsButton, 1);
        tagRow.Children.Add(draftsButton);
        releaseTargetRow = tagRow;
        repositoryDestination = Input(); repositoryDestination.Text = StringValue("RepositoryPath", "");
        tagPanel.Children.Add(repositoryDestination); codeTargetRow = repositoryDestination;
        publishRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        publishCheck = FileCheck("publish", config.ContainsKey("PublishAfterUpload") ? Convert.ToBoolean(config["PublishAfterUpload"]) : !config.ContainsKey("Repository"));
        publishCaption = Text("", 15, Ink, false); publishCaption.Margin = new Thickness(8, 0, 0, 0); publishCaption.VerticalAlignment = VerticalAlignment.Center;
        publishCaption.MouseLeftButtonUp += delegate { if(!uploadRunning) publishCheck.IsChecked = publishCheck.IsChecked != true; }; publishRow.Children.Add(publishCheck); publishRow.Children.Add(publishCaption);
        System.Windows.Automation.AutomationProperties.SetLabeledBy(publishCheck, publishCaption);
        publishCheck.Checked += delegate { if (startButton != null) ApplyLanguage(); }; publishCheck.Unchecked += delegate { if (startButton != null) ApplyLanguage(); };
        targetBody.Children.Add(publishRow);
        repository.TextChanged += delegate { if (!uploadRunning) { releaseTag.Clear(); codeBranch.Clear(); remoteCatalog=null; selectedReleaseId=0; ResetResult(); } };
        folderLabel = Text("", 16, Ink, true);
        folderLabel.Margin = new Thickness(0, 16, 0, 0);
        targetBody.Children.Add(folderLabel);
        var browseRow = new Grid();
        browseRow.ColumnDefinitions.Add(new ColumnDefinition());
        browseRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        targetBody.Children.Add(browseRow);
        folderPath = Input(true);
        System.Windows.Automation.AutomationProperties.SetLabeledBy(folderPath, folderLabel);
        folderPath.IsReadOnly = true;
        folderPath.Text = SourceFolder();
        folderPath.ToolTip = folderPath.Text;
        browseRow.Children.Add(folderPath);
        browseButton = MakeButton("", false, 180);
        browseButton.Height = FieldHeight;
        browseButton.Margin = new Thickness(12, 6, 0, 0);
        browseButton.Click += delegate { ChooseFolder(); };
        Grid.SetColumn(browseButton, 1);
        browseRow.Children.Add(browseButton);

        var filesCard = Card();
        filesCard.Margin = new Thickness(0, 16, 0, 0);
        page.Children.Add(filesCard);
        var filesBody = new StackPanel();
        filesCard.Child = filesBody;
        var fileHeading = new Grid();
        fileHeading.ColumnDefinitions.Add(new ColumnDefinition());
        fileHeading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        fileHeading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        filesBody.Children.Add(fileHeading);
        filesTitle = Text("", 20, Ink, true);
        fileHeading.Children.Add(filesTitle);
        selectedCount = Text("", 14, Muted, false);
        selectedCount.VerticalAlignment = VerticalAlignment.Center;
        selectedCount.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(selectedCount, 1);
        fileHeading.Children.Add(selectedCount);
        filesHint = Text("", 14, Muted, false);
        filesHint.TextWrapping = TextWrapping.Wrap;
        filesHint.Visibility = Visibility.Collapsed;
        filesHint.Margin = new Thickness(0, 8, 0, 12);
        filesBody.Children.Add(filesHint);
        var selectionActions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 0, 0) };
        Grid.SetColumn(selectionActions, 2);
        fileHeading.Children.Add(selectionActions);
        selectAllButton = MakeButton("", false, 86);
        selectAllButton.Height = 30;
        selectAllButton.FontSize = 14;
        selectAllButton.Padding = new Thickness(12, 0, 12, 0);
        selectAllButton.Click += delegate { SetAllFiles(true); };
        selectionActions.Children.Add(selectAllButton);
        clearButton = MakeButton("", false, 86);
        clearButton.Height = 30;
        clearButton.FontSize = 14;
        clearButton.Padding = new Thickness(12, 0, 12, 0);
        clearButton.Margin = new Thickness(10, 0, 0, 0);
        clearButton.Click += delegate { SetAllFiles(false); };
        selectionActions.Children.Add(clearButton);
        var table = new Border { Margin = new Thickness(0, 14, 0, 0), Background = Brush(White), BorderBrush = Brush(BorderColor), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(1) };
        filesBody.Children.Add(table);
        var tableBody = new StackPanel();
        table.Child = tableBody;
        tableHeading = TableRow();
        tableHeading.HorizontalAlignment = HorizontalAlignment.Left;
        tableHeading.Height = 38;
        tableHeading.Background = Brush(Hex("#F3F3F4"));
        nameHeader = Text("", 14, Ink, true);
        sizeHeader = Text("", 14, Ink, true);
        sizeHeader.TextAlignment = TextAlignment.Right;
        stateHeader = Text("", 14, Ink, true);
        AddTableCell(tableHeading, nameHeader, 2);
        AddTableCell(tableHeading, sizeHeader, 3);
        AddTableCell(tableHeading, stateHeader, 4);
        tableBody.Children.Add(tableHeading);
        // Keep only visible row containers in the visual tree. A StackPanel inside
        // an outer ScrollViewer measures thousands of rows on every layout pass.
        fileList = new ListBox { BorderThickness = new Thickness(0), Padding = new Thickness(0), Background = Brushes.Transparent, IsTabStop = false };
        VirtualizingStackPanel.SetIsVirtualizing(fileList, true);
        VirtualizingStackPanel.SetVirtualizationMode(fileList, VirtualizationMode.Recycling);
        var itemsPanel = new FrameworkElementFactory(typeof(VirtualizingStackPanel));
        fileList.ItemsPanel = new ItemsPanelTemplate(itemsPanel);
        var itemStyle = new Style(typeof(ListBoxItem));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        itemStyle.Setters.Add(new Setter(UIElement.FocusableProperty, false));
        var itemTemplate = new ControlTemplate(typeof(ListBoxItem));
        itemTemplate.VisualTree = new FrameworkElementFactory(typeof(ContentPresenter));
        itemStyle.Setters.Add(new Setter(Control.TemplateProperty, itemTemplate));
        fileList.ItemContainerStyle = itemStyle;
        var rowTemplate=new DataTemplate();
        var lazyRow=new FrameworkElementFactory(typeof(ContentPresenter));
        lazyRow.SetBinding(ContentPresenter.ContentProperty,new Binding("."){Converter=new FileRowConverter(this)});
        rowTemplate.VisualTree=lazyRow;fileList.ItemTemplate=rowTemplate;
        var listTemplate = new ControlTemplate(typeof(ListBox));
        var viewport = new FrameworkElementFactory(typeof(ScrollViewer));
        viewport.Name = "PART_ScrollViewer";
        viewport.SetValue(FrameworkElement.StyleProperty, ScrollGutterStyle());
        viewport.SetValue(ScrollViewer.CanContentScrollProperty, true);
        viewport.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        viewport.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        viewport.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
        listTemplate.VisualTree = viewport;
        fileList.Template = listTemplate;
        tableBody.Children.Add(fileList);
        fileList.ApplyTemplate();
        BindFileViewport();
        listScroll.MaxHeight = 176;

        var progressCard = Card();
        progressFrame = progressCard;
        progressCard.Padding = new Thickness(14);
        progressCard.Margin = new Thickness(0, 16, 0, 0);
        Grid.SetRow(progressCard, 2);
        main.Children.Add(progressCard);
        var progressPanel = new StackPanel();
        progressCard.Child = progressPanel;
        progressTitle = Text("", 15, Ink, true);
        progressPanel.Children.Add(progressTitle);
        progressOverallText = Text("", 14, Muted, false);
        progressOverallText.Margin = new Thickness(0, 12, 0, 6);
        var progressColumns = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        progressColumns.ColumnDefinitions.Add(new ColumnDefinition());
        progressColumns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        progressColumns.ColumnDefinitions.Add(new ColumnDefinition());
        progressPanel.Children.Add(progressColumns);
        var totalColumn = new StackPanel();
        progressColumns.Children.Add(totalColumn);
        totalColumn.Children.Add(progressOverallText);
        overallBar = MakeProgressBar();
        totalColumn.Children.Add(overallBar);
        progressFileText = Text("", 14, Muted, false);
        progressFileText.Margin = new Thickness(0, 12, 0, 6);
        var fileColumn = new StackPanel();
        Grid.SetColumn(fileColumn, 2);
        progressColumns.Children.Add(fileColumn);
        fileColumn.Children.Add(progressFileText);
        fileBar = MakeProgressBar();
        fileColumn.Children.Add(fileBar);
        advanced = new Expander { Margin = new Thickness(0, 12, 0, 0), Foreground = Brush(Ink) };
        page.Children.Add(advanced);
        advancedLabel = Text("", 14, Muted, false);
        advanced.Header = advancedLabel;
        advanced.Expanded += delegate { QueueLayout(); };
        advanced.Collapsed += delegate { QueueLayout(); };
        var advancedCard = Card();
        advancedCard.Margin = new Thickness(0, 12, 0, 0);
        advanced.Content = advancedCard;
        var advancedGrid = new UniformGrid { Columns = 3 };
        var advancedBody = new StackPanel();
        advancedCard.Child = advancedBody;
        advancedBody.Children.Add(advancedGrid);
        var codeSettings = new StackPanel(); codeOptions = codeSettings; advancedBody.Children.Insert(0, codeSettings);
        branchLabel = Text("", 14, Muted, false); codeSettings.Children.Add(branchLabel);
        var branchRow = new Grid(); branchRow.ColumnDefinitions.Add(new ColumnDefinition()); branchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); codeSettings.Children.Add(branchRow);
        codeBranch = Input(); codeBranch.IsReadOnly = true; codeBranch.Text = StringValue("RepositoryBranch", ""); branchRow.Children.Add(codeBranch);
        branchesButton = MakeButton("", false, 0); branchesButton.Height = 44; branchesButton.Padding = new Thickness(12, 0, 12, 0); branchesButton.Margin = new Thickness(8, 6, 0, 0);
        branchesButton.Click += delegate { if (!uploadRunning) { if(downloadMode) ChooseSyncCatalog("branches"); else { WriteUploaderRequest("branches", null); Launch("Branches", false); } } }; Grid.SetColumn(branchesButton, 1); branchRow.Children.Add(branchesButton);
        retriesLabel = Text("", 14, Muted, false);
        delayLabel = Text("", 14, Muted, false);
        pollLabel = Text("", 14, Muted, false);
        timeoutLabel = Text("", 14, Muted, false);
        waitLabel = Text("", 14, Muted, false);
        retries = AdvancedInput(advancedGrid, retriesLabel, StringValue("MaxAttempts", "5"));
        delay = AdvancedInput(advancedGrid, delayLabel, StringValue("RetryBaseSeconds", "10"));
        poll = AdvancedInput(advancedGrid, pollLabel, StringValue("PollSeconds", "20"));
        timeout = AdvancedInput(advancedGrid, timeoutLabel, StringValue("HttpTimeoutHours", "3"));
        waitPid = AdvancedInput(advancedGrid, waitLabel, StringValue("WaitForProcessId", ""));

        var bottom = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
        Grid.SetRow(bottom, 3);
        main.Children.Add(bottom);
        var actions = new Grid();
        for (int i = 0; i < 3; i++) actions.ColumnDefinitions.Add(new ColumnDefinition());
        bottom.Children.Add(actions);
        simpleHint = Text("", 14, Muted, false);
        simpleHint.TextWrapping = TextWrapping.Wrap;
        simpleHint.Margin = new Thickness(0, 0, 0, 10);
        bottom.Children.Insert(0, simpleHint);
        var extraActions = new UniformGrid { Columns = 2, Margin = new Thickness(0, 16, 0, 0) };
        advancedBody.Children.Add(extraActions);
        saveButton = MakeButton("", false, 0);
        saveButton.Margin = new Thickness(0, 0, 8, 0);
        saveButton.Click += delegate { SaveConfig(); };
        extraActions.Children.Add(saveButton);
        checkButton = MakeButton("", false, 0);
        checkButton.Margin = new Thickness(4, 0, 4, 0);
        checkButton.Click += delegate { if(downloadMode) {RefreshRemoteFiles();return;} if (!uploadRunning && SaveConfig()) { if(CodeMode) { preparingCodeUpload=false; WriteUploaderRequest("preview",null); Launch("CodePreview",false); } else Launch("Check-Configuration.cmd",false); } };
        Grid.SetColumn(checkButton, 1);
        extraActions.Children.Add(checkButton);
        startButton = MakeButton("", true, 0);
        startButton.Margin = new Thickness(0);
        startButton.Click += delegate { if(downloadMode) StartDownload(); else StartUpload(); };
        Grid.SetColumn(startButton, 0);
        Grid.SetColumnSpan(startButton, 3);
        actions.Children.Add(startButton);
        status = Text("", 14, Muted, false);
        status.Visibility = Visibility.Collapsed;
        status.TextWrapping = TextWrapping.Wrap;
        status.Margin = new Thickness(0, 10, 0, 0);
        bottom.Children.Add(status);
        openFilesButton = MakeButton("", false, 0);
        openFilesButton.Height = 36;
        openFilesButton.HorizontalAlignment = HorizontalAlignment.Left;
        openFilesButton.Margin = new Thickness(0, 8, 0, 0);
        openFilesButton.Visibility = Visibility.Collapsed;
        openFilesButton.Click += delegate { OpenUploadedFiles(); };
        var resultActions = new WrapPanel(); bottom.Children.Add(resultActions);
        openFilesButton.Margin = new Thickness(0, 8, 8, 0); resultActions.Children.Add(openFilesButton);
        publishLaterButton = MakeButton("", true, 0); publishLaterButton.Height = 36; publishLaterButton.Margin = new Thickness(0, 8, 8, 0); publishLaterButton.Visibility = Visibility.Collapsed; resultActions.Children.Add(publishLaterButton);
        publishLaterButton.Click += delegate { PublishCurrentDraft(); };
        copyResultButton = MakeButton("", false, 0); copyResultButton.Height = 36; copyResultButton.Margin = new Thickness(0, 8, 0, 0); copyResultButton.Visibility = Visibility.Collapsed; resultActions.Children.Add(copyResultButton);
        copyResultButton.Click += delegate { CopyLink(uploadedReleaseUrl); };
        downloadLinksButton = MakeButton("", false, 0); downloadLinksButton.Height = 36; downloadLinksButton.Margin = new Thickness(8, 8, 0, 0); downloadLinksButton.Visibility = Visibility.Collapsed; resultActions.Children.Add(downloadLinksButton);
        downloadLinksButton.Click += delegate { ShowDownloadLinks(); };
        addFilesButton = MakeButton("", false, 0); addFilesButton.Height = 30; addFilesButton.FontSize = 14; addFilesButton.Padding = new Thickness(12, 0, 12, 0); addFilesButton.Margin = new Thickness(0, 8, 0, 0); addFilesButton.HorizontalAlignment = HorizontalAlignment.Left;
        addFilesButton.Click += delegate { ChooseIndividualFiles(); }; filesBody.Children.Insert(2, addFilesButton);
        footer = Text("", 14, Muted, false);
        footer.TextWrapping = TextWrapping.Wrap;
        footer.TextTrimming = TextTrimming.None;
        footer.Margin = new Thickness(0, 8, 0, 0);
        bottom.Children.Add(footer);
        lastModeIndex=modePicker.SelectedIndex;
        modePicker.SelectionChanged += delegate { if(uploadRunning || changingDirection) return; if(!downloadMode) RememberSelection(); lastModeIndex=modePicker.SelectedIndex; remoteCatalog=null; CancelFileScan(); fileChecks.Clear(); fileStatusLabels.Clear(); ResetTableItems(); if(!downloadMode) folderPath.Text=SourceFolder(); folderPath.ToolTip=folderPath.Text; ResetResult(); ApplyLanguage(); LoadFiles(); };
        BuildSyncControls(header,advancedBody,bottom);
        if(downloadMode) {folderPath.Text=StringValue("DownloadDirectory","");repositoryDestination.Text=syncPrefix;codeBranch.Text=syncBranch;}
    }

    private static Style ScrollGutterStyle()
    {
        // Default WPF ScrollViewer places Padding on its content presenter,
        // before the scrollbar. Reserve the gutter only while that bar is visible.
        var style = new Style(typeof(ScrollViewer));
        var visible = new Trigger { Property = ScrollViewer.ComputedVerticalScrollBarVisibilityProperty, Value = Visibility.Visible };
        visible.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0, 0, ScrollGutter, 0)));
        style.Triggers.Add(visible);
        return style;
    }

    private void BindFileViewport()
    {
        listScroll = (ScrollViewer)fileList.Template.FindName("PART_ScrollViewer", fileList);
        // Header columns track the row viewport, excluding both gutter and bar.
        // Rebind after replacing the virtualized list during a mode/folder change.
        tableHeading.SetBinding(FrameworkElement.WidthProperty, new Binding("ViewportWidth") { Source = listScroll });
    }

    private void QueueLayout()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(UpdateLayoutSpace));
    }

    private void UpdateLayoutSpace()
    {
        if (contentScroll == null || listScroll == null || contentScroll.ActualHeight <= 0) return;
        var page = (FrameworkElement)contentScroll.Content;
        // Measure intrinsic content, not the previous constrained viewport height.
        // Do not remove the viewport constraint: an infinite height disables
        // virtualization and realizes the complete file list during measurement.
        page.InvalidateMeasure();
        page.Measure(new Size(Math.Max(1, contentScroll.ActualWidth), Double.PositiveInfinity));
        // Give surplus height to the table, not to a second outer scrollbar.
        double reserved = page.DesiredSize.Height - listScroll.DesiredSize.Height;
        double room = Math.Max(fileChecks.Count > 3 ? 132 : 44, contentScroll.ActualHeight - reserved - 2);
        if (Math.Abs(listScroll.MaxHeight - room) > 0.5) listScroll.MaxHeight = room;
    }

    private void UpdateProgressAppearance()
    {
        bool idle = phase == "ready" || phase == "checked" || (phase == "failed" && String.IsNullOrEmpty(currentFile));
        var panel = (StackPanel)progressFrame.Child;
        panel.Children[1].Visibility = idle ? Visibility.Collapsed : Visibility.Visible;
        progressFrame.Background = idle ? Brushes.Transparent : Brush(White);
        progressFrame.BorderThickness = new Thickness(idle ? 0 : 1);
        progressFrame.Padding = new Thickness(idle ? 0 : 14);
        if (phase == "ready") progressTitle.Text = L("Bereit zum Start", "Готово к запуску", "Ready to start");
        QueueLayout();
    }

    private static Grid TableRow()
    {
        var row = new Grid { Height = 44 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        return row;
    }

    private static void AddTableCell(Grid row, FrameworkElement element, int column)
    {
        element.VerticalAlignment = VerticalAlignment.Center;
        element.Margin = new Thickness(8, 0, 8, 0);
        Grid.SetColumn(element, column);
        row.Children.Add(element);
    }

    private static Border Card()
    {
        return new Border { Background = Brush(White), BorderBrush = Brush(BorderColor), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(CardRadius), Padding = new Thickness(20) };
    }

    private static TextBlock Text(string value, double size, Color color, bool bold)
    {
        return new TextBlock { Text = value, FontSize = size, Foreground = Brush(color), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis };
    }

    private static TextBox Input(bool folder = false)
    {
        var input = new TextBox { FontSize = 15, Height = FieldHeight, Margin = new Thickness(0, 6, 0, 0), Padding = new Thickness(folder ? 40 : 12, 0, 12, 0), BorderBrush = Brush(BorderColor), BorderThickness = new Thickness(1), Background = Brush(White), Foreground = Brush(Ink), VerticalContentAlignment = VerticalAlignment.Center };
        var template = new ControlTemplate(typeof(TextBox));
        var frame = new FrameworkElementFactory(typeof(Border));
        frame.Name = "frame";
        frame.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(TextBox.BackgroundProperty));
        frame.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(TextBox.BorderBrushProperty));
        frame.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(TextBox.BorderThicknessProperty));
        frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(ControlRadius));
        var host = new FrameworkElementFactory(typeof(ScrollViewer));
        host.Name = "PART_ContentHost";
        // TextBox already pads its text view; the folder icon needs that inset only once.
        host.SetValue(FrameworkElement.MarginProperty, folder ? (object)new Thickness(0) : new TemplateBindingExtension(TextBox.PaddingProperty));
        var inner = new FrameworkElementFactory(typeof(Grid));
        inner.AppendChild(host);
        if (folder)
        {
            var glyph = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
            glyph.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M1,5 L1,17 L21,17 L21,5 L10,5 L8,2 L1,2 Z"));
            glyph.SetValue(System.Windows.Shapes.Path.StrokeProperty, Brush(Muted));
            glyph.SetValue(System.Windows.Shapes.Path.StrokeThicknessProperty, 1.5);
            glyph.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            glyph.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            glyph.SetValue(FrameworkElement.MarginProperty, new Thickness(12, 0, 0, 0));
            glyph.SetValue(UIElement.IsHitTestVisibleProperty, false);
            inner.AppendChild(glyph);
        }
        frame.AppendChild(inner);
        template.VisualTree = frame;
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(Border.BorderBrushProperty, Brush(Accent), "frame"));
        template.Triggers.Add(focus);
        input.Template = template;
        return input;
    }

    private static TextBox AdvancedInput(Panel parent, TextBlock label, string value)
    {
        var wrap = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
        parent.Children.Add(wrap);
        wrap.Children.Add(label);
        var input = Input();
        input.Text = value;
        wrap.Children.Add(input);
        return input;
    }

    private static Button MakeButton(string value, bool primary, double width)
    {
        var button = new Button { Content = value, MinWidth = width, Height = 50, Padding = new Thickness(24, 0, 24, 0), Cursor = Cursors.Hand, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Brush(Ink), Background = Brush(primary ? Accent : Hex("#ECECEE")), BorderBrush = Brush(primary ? Accent : BorderColor) };
        button.SnapsToDevicePixels = true;
        button.UseLayoutRounding = true;
        button.FocusVisualStyle = null;
        var template = new ControlTemplate(typeof(Button));
        var surface = new FrameworkElementFactory(typeof(Border));
        surface.Name = "surface";
        surface.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
        surface.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
        surface.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        surface.SetValue(Border.CornerRadiusProperty, new CornerRadius(ControlRadius));
        surface.SetValue(Border.SnapsToDevicePixelsProperty, true);
        surface.SetValue(Border.UseLayoutRoundingProperty, true);
        surface.SetValue(Border.ClipToBoundsProperty, true);
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        content.SetValue(ContentPresenter.MarginProperty, new TemplateBindingExtension(Button.PaddingProperty));
        surface.AppendChild(content);
        template.VisualTree = surface;
        var hover = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, Brush(primary ? AccentHover : Hex("#E1E2E5")), "surface"));
        template.Triggers.Add(hover);
        var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(Border.BackgroundProperty, Brush(primary ? Hex("#C9760E") : Hex("#D6D8DC")), "surface"));
        template.Triggers.Add(pressed);
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(Border.BorderBrushProperty, Brush(Accent), "surface"));
        template.Triggers.Add(focus);
        var disabled = new Trigger { Property = Button.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.65));
        template.Triggers.Add(disabled);
        button.Template = template;
        return button;
    }

    private static ComboBox MakeLanguagePicker()
    {
        var box = new ComboBox { Width = 150, Height = 48, FontSize = 16, Foreground = Brush(Ink), Background = Brush(Hex("#ECECEE")), SnapsToDevicePixels = true, UseLayoutRounding = true };
        box.FocusVisualStyle = null;
        var template = new ControlTemplate(typeof(ComboBox));
        var grid = new FrameworkElementFactory(typeof(Grid));
        var frame = new FrameworkElementFactory(typeof(Border));
        frame.Name = "surface";
        frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(ControlRadius));
        frame.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(ComboBox.BackgroundProperty));
        frame.SetValue(Border.BorderBrushProperty, Brush(BorderColor));
        frame.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        grid.AppendChild(frame);
        var selection = new FrameworkElementFactory(typeof(ContentPresenter));
        selection.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ComboBox.SelectionBoxItemProperty));
        selection.SetValue(ContentPresenter.ContentTemplateProperty, new TemplateBindingExtension(ComboBox.SelectionBoxItemTemplateProperty));
        selection.SetValue(FrameworkElement.MarginProperty, new Thickness(28, 0, 28, 0));
        selection.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        selection.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        selection.SetValue(UIElement.IsHitTestVisibleProperty, false);
        grid.AppendChild(selection);
        var toggle = new FrameworkElementFactory(typeof(ToggleButton));
        toggle.SetValue(UIElement.FocusableProperty, false);
        toggle.SetBinding(ToggleButton.IsCheckedProperty, new Binding("IsDropDownOpen") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent), Mode = BindingMode.TwoWay });
        var toggleTemplate = new ControlTemplate(typeof(ToggleButton));
        var toggleFrame = new FrameworkElementFactory(typeof(Border));
        toggleFrame.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var arrow = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
        arrow.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M 1,1 L 5,5 L 9,1"));
        arrow.SetValue(System.Windows.Shapes.Path.StrokeProperty, Brush(Muted));
        arrow.SetValue(System.Windows.Shapes.Path.StrokeThicknessProperty, 1.7);
        arrow.SetValue(System.Windows.Shapes.Path.StrokeStartLineCapProperty, PenLineCap.Round);
        arrow.SetValue(System.Windows.Shapes.Path.StrokeEndLineCapProperty, PenLineCap.Round);
        arrow.SetValue(System.Windows.Shapes.Path.StrokeLineJoinProperty, PenLineJoin.Round);
        arrow.SetValue(FrameworkElement.WidthProperty, 10.0);
        arrow.SetValue(FrameworkElement.HeightProperty, 6.0);
        arrow.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
        arrow.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        arrow.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 14, 0));
        toggleFrame.AppendChild(arrow);
        toggleTemplate.VisualTree = toggleFrame;
        toggle.SetValue(Control.TemplateProperty, toggleTemplate);
        grid.AppendChild(toggle);
        var outline = new FrameworkElementFactory(typeof(Border));
        outline.Name = "outline";
        outline.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        outline.SetValue(Border.BorderBrushProperty, Brush(BorderColor));
        outline.SetValue(Border.BorderThicknessProperty, new Thickness(0));
        outline.SetValue(Border.CornerRadiusProperty, new CornerRadius(ControlRadius));
        outline.SetValue(Border.SnapsToDevicePixelsProperty, true);
        outline.SetValue(UIElement.IsHitTestVisibleProperty, false);
        grid.AppendChild(outline);
        var popup = new FrameworkElementFactory(typeof(Popup));
        popup.Name = "PART_Popup";
        popup.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
        popup.SetValue(Popup.AllowsTransparencyProperty, true);
        popup.SetValue(Popup.PopupAnimationProperty, PopupAnimation.Fade);
        popup.SetValue(Popup.FocusableProperty, false);
        popup.SetBinding(Popup.IsOpenProperty, new Binding("IsDropDownOpen") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        var menu = new FrameworkElementFactory(typeof(Border));
        menu.SetValue(Border.BackgroundProperty, Brush(White));
        menu.SetValue(Border.BorderBrushProperty, Brush(BorderColor));
        menu.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        menu.SetValue(Border.CornerRadiusProperty, new CornerRadius(ControlRadius));
        menu.SetValue(FrameworkElement.MinWidthProperty, 150.0);
        menu.SetValue(Border.PaddingProperty, new Thickness(5));
        var items = new FrameworkElementFactory(typeof(StackPanel));
        items.SetValue(Panel.IsItemsHostProperty, true);
        menu.AppendChild(items);
        popup.AppendChild(menu);
        grid.AppendChild(popup);
        template.VisualTree = grid;
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BorderBrushProperty, Brush(Accent), "surface"));
        template.Triggers.Add(hover);
        var focused = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
        focused.Setters.Add(new Setter(Border.BorderBrushProperty, Brush(Accent), "surface"));
        template.Triggers.Add(focused);
        box.Template = template;
        var itemStyle = new Style(typeof(ComboBoxItem));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 8, 12, 8)));
        itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        var itemTemplate = new ControlTemplate(typeof(ComboBoxItem));
        var itemFrame = new FrameworkElementFactory(typeof(Border));
        itemFrame.Name = "item";
        itemFrame.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        itemFrame.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var itemContent = new FrameworkElementFactory(typeof(ContentPresenter));
        itemFrame.AppendChild(itemContent);
        itemTemplate.VisualTree = itemFrame;
        var highlighted = new Trigger { Property = ComboBoxItem.IsHighlightedProperty, Value = true };
        highlighted.Setters.Add(new Setter(Border.BackgroundProperty, Brush(Hex("#FFF0DC")), "item"));
        itemTemplate.Triggers.Add(highlighted);
        itemStyle.Setters.Add(new Setter(Control.TemplateProperty, itemTemplate));
        box.ItemContainerStyle = itemStyle;
        return box;
    }

    private static CheckBox FileCheck(string name, bool selected)
    {
        var check = new CheckBox { Tag = name, IsChecked = selected, Width = 22, Height = 22, Cursor = Cursors.Hand, ToolTip = name };
        System.Windows.Automation.AutomationProperties.SetName(check, name);
        if (fileCheckTemplate != null) { check.Template = fileCheckTemplate; return check; }
        var template = new ControlTemplate(typeof(CheckBox));
        var frame = new FrameworkElementFactory(typeof(Border));
        frame.Name = "box";
        frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
        frame.SetValue(Border.BorderBrushProperty, Brush(Hex("#AEB3BA")));
        frame.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        frame.SetValue(Border.BackgroundProperty, Brush(White));
        var tick = new FrameworkElementFactory(typeof(TextBlock));
        tick.Name = "tick";
        tick.SetValue(TextBlock.TextProperty, "✓");
        tick.SetValue(TextBlock.FontSizeProperty, 17.0);
        tick.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
        tick.SetValue(TextBlock.ForegroundProperty, Brush(White));
        tick.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        tick.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        tick.SetValue(UIElement.VisibilityProperty, Visibility.Hidden);
        frame.AppendChild(tick);
        template.VisualTree = frame;
        var active = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        active.Setters.Add(new Setter(Border.BackgroundProperty, Brush(Accent), "box"));
        active.Setters.Add(new Setter(Border.BorderBrushProperty, Brush(Accent), "box"));
        active.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "tick"));
        template.Triggers.Add(active);
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(Border.BorderBrushProperty, Brush(AccentHover), "box"));
        focus.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(2), "box"));
        template.Triggers.Add(focus);
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BorderBrushProperty, Brush(AccentHover), "box"));
        template.Triggers.Add(hover);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.55));
        template.Triggers.Add(disabled);
        fileCheckTemplate = template;
        check.Template = template;
        return check;
    }

    private void ApplyLanguage()
    {
        if (language == null || language.SelectedIndex < 0) return;
        subtitleText.Text = L("Dateien gezielt in einen GitHub-Draft-Release laden", "Загрузка выбранных файлов в черновик GitHub Release", "Upload selected files to a GitHub draft release");
        targetTitle.Text = L("Ziel des Uploads", "Куда загружать", "Upload destination");
        repoLabel.Text = L("Repository", "Репозиторий", "Repository");
        tagLabel.Text = L("Release-Tag", "Тег релиза", "Release tag");
        folderLabel.Text = L("Ordner wählen…", "Папка с файлами", "Source folder");
        nameHeader.Text = L("Dateiname", "Имя файла", "File name");
        sizeHeader.Text = L("Größe", "Размер", "Size");
        stateHeader.Text = L("Status", "Статус", "Status");
        stageLabels[0].Text = L("Bereit", "Готово", "Ready");
        stageLabels[1].Text = L("Prüfung", "Проверка", "Check");
        stageLabels[2].Text = L("Upload", "Загрузка", "Upload");
        filesTitle.Text = L("Dateien auswählen", "Выбрать файлы", "Choose files");
        progressTitle.Text = L("Upload-Fortschritt", "Прогресс загрузки", "Upload progress");
        if (lastProgressRead == 0)
        {
            progressOverallText.Text = L("Gesamt · bereit", "Общий · готово к запуску", "Overall · ready");
            progressFileText.Text = L("Aktuelle Datei · noch nicht gestartet", "Текущий файл · ещё не запущен", "Current file · not started");
        }
        filesHint.Text = L("Die Dateien bleiben im gewählten Ordner. Nichts wird in die App kopiert.", "Файлы остаются в выбранной папке. В программу ничего не копируется.", "Files stay in the selected folder. Nothing is copied into the app.");
        browseButton.Content = L("Ordner wählen…", "Выбрать папку…", "Choose folder…");
        selectAllButton.Content = L("Alle", "Все", "All");
        clearButton.Content = L("Keine", "Снять", "None");
        advancedLabel.Text = L("Erweiterte Einstellungen", "Дополнительные настройки", "Advanced settings");
        retriesLabel.Text = L("Versuche", "Попытки", "Attempts");
        delayLabel.Text = L("Pause (s)", "Пауза (с)", "Delay (s)");
        pollLabel.Text = L("Abfrage (s)", "Опрос (с)", "Poll (s)");
        timeoutLabel.Text = L("Timeout (h)", "Тайм-аут (ч)", "Timeout (h)");
        waitLabel.Text = L("Warten auf PID", "Ждать PID", "Wait for PID");
        guideButton.Content = L("Anleitung", "Справка", "Guide");
        projectsButton.Content = L("Wählen / neu…", "Выбрать / создать…", "Choose / new…");
        projectsButton.ToolTip = L("Meine GitHub-Projekte anzeigen", "Показать мои проекты GitHub", "Show my GitHub projects");
        draftsButton.Content = L("Wählen / neu…", "Выбрать / создать…", "Choose / new…");
        draftsButton.ToolTip = L("Entwurf auswählen oder erstellen", "Выбрать черновик или создать новый", "Select or create a draft");
        repoLabel.Text = L("1. GitHub-Projekt", "1. Проект GitHub", "1. GitHub project");
        tagLabel.Text = L("2. Entwurf auswählen", "2. Куда загрузить", "2. Choose destination");
        filesTitle.Text = L("3. Dateien auswählen", "3. Выбрать файлы", "3. Choose files");
        simpleHint.Text = L("Projekt → Entwurf → Dateien → Hochladen. Einstellungen werden automatisch gespeichert.", "Проект → черновик → файлы → загрузить. Настройки сохраняются автоматически.", "Project → draft → files → upload. Settings are saved automatically.");
        signInButton.Content = L("GitHub anmelden", "Войти в GitHub", "Sign in to GitHub");
        signInButton.ToolTip = L("Browser-Anmeldung; 2FA bei GitHub bestätigen.", "Вход через браузер; SMS/2FA подтвердите на GitHub.", "Browser sign-in; confirm SMS/2FA on GitHub.");
        accountText.Text = String.IsNullOrEmpty(accountLogin) ? L("Konto nicht geprüft", "Аккаунт не проверен", "Account not checked") : L("Angemeldet: ", "Вход выполнен: ", "Signed in: ") + accountLogin;
        System.Windows.Automation.AutomationProperties.SetName(language, L("Sprache", "Язык", "Language"));
        saveButton.Content = L("Speichern", "Сохранить", "Save");
        saveButton.ToolTip = L("Auswahl und Einstellungen lokal speichern; kein Upload.", "Сохраняет настройки и выбранные файлы для следующего запуска. Ничего не отправляет.", "Save settings and selected files for the next start. No upload.");
        checkButton.ToolTip = L("Draft, Zugriff und vorhandene Dateien auf GitHub prüfen; kein Upload.", "Проверяет доступ к черновику и уже загруженные файлы на GitHub. Ничего не отправляет.", "Check draft access and existing GitHub files. No upload.");
        checkButton.Content = L("Prüfen", "Проверить", "Check");
        openFilesButton.Content = L("Dateien auf GitHub öffnen", "Открыть файлы на GitHub", "Open files on GitHub");
        openFilesButton.ToolTip = L("Öffnet den Release-Entwurf im Browser. Mit dem berechtigten GitHub-Konto anmelden; keine Veröffentlichung.", "Открывает черновик релиза в браузере. Войдите в GitHub под аккаунтом с доступом. Ничего не публикует.", "Open the release draft in your browser. Sign in to GitHub with an authorized account. Does not publish anything.");
        startButton.Content = uploadRunning && !checking ? L("Upload läuft", "Идёт загрузка", "Upload running") : L("Upload starten", "Начать загрузку", "Start upload");
        checkButton.Content = uploadRunning && checking ? L("Prüfung läuft", "Идёт проверка", "Checking") : L("Prüfen", "Проверить", "Check");
        startButton.Content = uploadRunning && !checking ? L("Upload läuft", "Идёт загрузка", "Upload running") : L("Ausgewählte Dateien hochladen", "Загрузить выбранные файлы", "Upload selected files");
        footer.Text = L("Ohne Administratorrechte · Upload nacheinander · GitHub-Zugangsdaten werden nicht gespeichert", "Без прав администратора · по одному файлу · данные входа GitHub не сохраняются", "No admin rights · sequential upload · GitHub credentials are not stored");
        if (emptyFilesLabel != null)
            emptyFilesLabel.Text = FileScanMessage();
        ApplyUploaderLanguage();
        ApplySyncLanguage();
        UpdateSelection();
        if (latestSnapshot != null) ApplyProgress(latestSnapshot);
    }

    private void ReadConfig()
    {
        try { config = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(root, "config.json"), Encoding.UTF8)); }
        catch { config = new Dictionary<string, object>(); }
        if (config == null) config = new Dictionary<string, object>();
    }

    private string StringValue(string key, string fallback)
    {
        object value;
        return config.TryGetValue(key, out value) && value != null ? Convert.ToString(value) : fallback;
    }

    private string SourceFolder()
    {
        try
        {
            var value = CodeMode ? StringValue("ProjectSourceDirectory",StringValue("SourceDirectory","upload")) : StringValue("SourceDirectory","upload");
            return Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(root, value));
        }
        catch { return Path.Combine(root, "upload"); }
    }

    private void ChooseFolder()
    {
        using (var dialog = new Forms.FolderBrowserDialog())
        {
            dialog.Description = L("Ordner mit Release-Dateien wählen", "Выберите папку с файлами релиза", "Choose a folder with release files");
            if (Directory.Exists(folderPath.Text)) dialog.SelectedPath = folderPath.Text;
            if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
            folderPath.Text = dialog.SelectedPath;
            folderPath.ToolTip = dialog.SelectedPath;
            if(downloadMode) { SaveSyncPreferences(); ResetResult(); ApplyLanguage(); return; }
            if(CodeMode) { config.Remove("ProjectFiles"); config["ProjectSourceDirectory"]=dialog.SelectedPath; } else config["SourceDirectory"]=dialog.SelectedPath;
            LoadFiles(); ResetResult();
        }
    }

    private static string NormalizeRepository(string text)
    {
        string value = text.Trim();
        Uri uri;
        if (Uri.TryCreate(value, UriKind.Absolute, out uri)) {
            if (uri.Scheme != "https" || !String.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)) return "";
            var parts = uri.AbsolutePath.Trim('/').Split('/');
            if (parts.Length < 2) return "";
            value = parts[0] + "/" + parts[1];
        }
        if (value.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) value = value.Substring(0, value.Length - 4);
        return Regex.IsMatch(value, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$") && value != "OWNER/REPOSITORY" ? value : "";
    }

    private void LoadDraftChoices()
    {
        if (uploadRunning) return;
        string repo = NormalizeRepository(repository.Text);
        if (repo.Length == 0) { ShowError(L("Zuerst ein Projekt auswählen oder GitHub-Link einfügen.", "Сначала выберите проект или вставьте ссылку на него с GitHub.", "Choose a project first or paste its GitHub link.")); return; }
        repository.Text = repo;
        Launch("ListDrafts", false);
    }

    private sealed class CatalogChoice
    {
        public string Value { get; set; }
        public string Label { get; set; }
    }

    private void ShowCatalog(string kind, Dictionary<string, object> snapshot)
    {
        var choices = new List<CatalogChoice>();
        object raw;
        if (snapshot.TryGetValue("choices", out raw) && raw is IEnumerable) {
            foreach (object item in (IEnumerable)raw) {
                var entry = item as Dictionary<string, object>;
                if (entry == null) continue;
                string value = StateString(entry, "value", "");
                if (value.Length > 0) choices.Add(new CatalogChoice { Value = value, Label = StateString(entry, "label", value) });
            }
        }
        if (choices.Count == 0 && kind == "branches") { ShowError(L("Keine Branches. Projekt mit README initialisieren.", "Нет веток. Сначала создайте README в проекте.", "No branches. Initialize the project with a README.")); return; }
        if (choices.Count == 0 && kind != "projects" && kind != "drafts") {
            status.Text = kind == "projects" ? L("Keine Projekte mit Schreibzugriff gefunden. GitHub-Konto prüfen.", "Нет проектов с правом записи. Проверьте аккаунт GitHub.", "No writable projects found. Check your GitHub account.") : L("Noch kein Entwurf. Unter GitHub Releases einen Entwurf speichern und erneut auswählen.", "Черновиков пока нет. Сохраните черновик в разделе Releases на GitHub, затем выберите его здесь.", "No drafts yet. Save a draft in GitHub Releases, then select it here.");
            status.Visibility = Visibility.Visible;
            status.Foreground = Brush(Muted);
            return;
        }
        var dialog = new Window { Owner = this, Title = kind == "projects" ? L("Projekt auswählen", "Выбрать проект", "Choose project") : L("Entwurf auswählen", "Выбрать черновик", "Choose draft"), Width = 620, Height = 420, MinWidth = 420, MinHeight = 300, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Brush(Pale), FontFamily = FontFamily };
        if(kind == "branches") dialog.Title = L("Branch auswählen", "Выбрать ветку", "Choose branch");
        var body = new DockPanel { Margin = new Thickness(20) };
        bool createNew = false;
        if (kind == "drafts" || kind == "projects") {
            var create = MakeButton(kind == "projects" ? L("Neues Projekt erstellen", "Создать новый проект", "Create new project") : L("Neuen Release erstellen", "Создать новый релиз", "Create new release"), false, 0);
            create.Name = "CatalogCreate";
            create.Margin = new Thickness(0, 12, 0, 0);
            DockPanel.SetDock(create, Dock.Bottom);
            body.Children.Add(create);
            create.Click += delegate { createNew = true; dialog.DialogResult = true; };
        }
        var select = MakeButton(L("Auswählen", "Выбрать", "Select"), true, 0);
        select.Name = "CatalogSelect";
        select.IsEnabled = choices.Count > 0;
        select.Margin = new Thickness(0, 12, 0, 0);
        DockPanel.SetDock(select, Dock.Bottom);
        body.Children.Add(select);
        var list = new ListBox { ItemsSource = choices, DisplayMemberPath = "Label", FontSize = 16, BorderBrush = Brush(BorderColor), BorderThickness = new Thickness(1), Padding = new Thickness(8), SelectedIndex = 0 };
        if (choices.Count > 0) body.Children.Add(list);
        else {
            var empty = Text(kind == "projects" ? L("Keine Projekte. Unten neues Projekt erstellen.", "Проектов пока нет. Нажмите ниже «Создать новый проект».", "No projects yet. Click Create new project below.") : L("Noch kein Entwurf. Klicke unten auf „Neuen Release erstellen“.", "Черновиков пока нет. Нажмите ниже «Создать новый релиз».", "No drafts yet. Click Create new release below."), 16, Ink, false);
            empty.TextWrapping = TextWrapping.Wrap;
            body.Children.Add(empty);
        }
        dialog.Content = body;
        select.Click += delegate { if (list.SelectedItem != null) dialog.DialogResult = true; };
        list.MouseDoubleClick += delegate { if (list.SelectedItem != null) dialog.DialogResult = true; };
        if (dialog.ShowDialog() != true) return;
        if (createNew) { if (kind == "projects") ShowCreateProjectDialog(); else ShowCreateDraftDialog(); return; }
        var choice = (CatalogChoice)list.SelectedItem;
        if (kind == "projects") {
            repository.Text = choice.Value;
            releaseTag.Clear();
            if(!CodeMode) LoadDraftChoices();
        } else if(kind=="branches") { codeBranch.Text=choice.Value; ResetResult(); }
        else {
            releaseTag.Text = choice.Value;
            status.Text = L("Entwurf ausgewählt. Jetzt Dateien wählen und hochladen.", "Черновик выбран. Теперь отметьте файлы и нажмите «Загрузить».", "Draft selected. Choose files and click Upload.");
            status.Visibility = Visibility.Visible;
            status.Foreground = Brush(Muted);
        }
    }

    private void ShowCreateProjectDialog()
    {
        if (uploadRunning) return;
        if (String.IsNullOrWhiteSpace(accountLogin)) { ShowError(L("Zuerst GitHub anmelden.", "Сначала выполните вход в GitHub.", "Sign in to GitHub first.")); return; }
        var dialog = new Window { Owner = this, Title = L("Neues GitHub-Projekt", "Новый проект GitHub", "New GitHub project"), Width = 570, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Brush(Pale), FontFamily = FontFamily };
        var body = new StackPanel();
        var info = Text(L("Im angemeldeten persönlichen Konto erstellen. Ein README wird angelegt, damit Releases möglich sind. Noch keine Dateien hochgeladen.", "Создание в личном аккаунте, с которым выполнен вход. Добавится начальный README, чтобы можно было создавать релизы. Ваши файлы пока не загружаются.", "Create in the signed-in personal account. A starter README enables releases. Your files are not uploaded yet."), 15, Ink, false);
        info.Text = accountLogin + "\n" + info.Text;
        info.TextWrapping = TextWrapping.Wrap;
        body.Children.Add(info);
        var label = Text(L("Projektname (z.B. My-App)", "Имя проекта (например My-App)", "Project name (e.g. My-App)"), 16, Ink, true);
        label.Margin = new Thickness(0, 16, 0, 0); body.Children.Add(label);
        var name = Input(); name.Name = "ProjectName";
        System.Windows.Automation.AutomationProperties.SetLabeledBy(name, label); body.Children.Add(name);
        var privacyRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
        var privacy = FileCheck("", true); privacy.Name = "ProjectPrivate";
        string privacyLabel = L("Privates Projekt", "Приватный проект", "Private project");
        privacy.ToolTip = privacyLabel; System.Windows.Automation.AutomationProperties.SetName(privacy, privacyLabel);
        privacyRow.Children.Add(privacy);
        var privacyText = Text(privacyLabel, 16, Ink, false); privacyText.Margin = new Thickness(10, 0, 0, 0); privacyRow.Children.Add(privacyText);
        body.Children.Add(privacyRow);
        var warning = Text(L("Ohne Häkchen: öffentlich, für alle sichtbar. Bestehende Projekte werden nicht geändert.", "Если снять галочку — проект будет публичным, видимым всем. Существующие проекты не изменяются.", "Unchecked means public, visible to everyone. Existing projects are not changed."), 14, Muted, false);
        warning.TextWrapping = TextWrapping.Wrap; warning.Margin = new Thickness(0, 8, 0, 0); body.Children.Add(warning);
        var error = Text("", 14, Hex("#B32640"), false); error.TextWrapping = TextWrapping.Wrap; body.Children.Add(error);
        var create = MakeButton(L("Projekt auf GitHub erstellen", "Создать проект на GitHub", "Create project on GitHub"), true, 0);
        create.Name = "ProjectCreate"; create.Margin = new Thickness(0, 16, 0, 0); body.Children.Add(create);
        create.Click += delegate {
            if (!Regex.IsMatch(name.Text.Trim(), @"^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$") || name.Text.Trim().EndsWith(".git", StringComparison.OrdinalIgnoreCase)) { error.Text = L("Name: Buchstaben, Zahlen, Punkt, - oder _ (1–100), keine URL.", "Имя: латинские буквы, цифры, точка, - или _ (1–100), не ссылка.", "Name: letters, digits, dot, - or _ (1–100), not a URL."); return; }
            dialog.DialogResult = true;
        };
        dialog.Content = new Border { Background = Brush(Pale), Padding = new Thickness(24), Child = body };
        dialog.Loaded += delegate { name.Focus(); };
        if (dialog.ShowDialog() != true) return;
        try {
            draftRequestPath = Path.Combine(Path.GetTempPath(), "WatchdogCreateProject-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(draftRequestPath, json.Serialize(new Dictionary<string, object> { { "name", name.Text.Trim() }, { "private", privacy.IsChecked == true }, { "expectedAccount", accountLogin } }), new UTF8Encoding(false));
            Launch("CreateProject", false);
            if (!uploadRunning && File.Exists(draftRequestPath)) { File.Delete(draftRequestPath); draftRequestPath = null; }
        } catch (Exception ex) { ShowError(ex.Message); }
    }

    private void ShowCreateDraftDialog()
    {
        if (uploadRunning) return;
        string repo = NormalizeRepository(repository.Text);
        if (repo.Length == 0) { ShowError(L("Zuerst Projekt wählen.", "Сначала выберите проект.", "Choose a project first.")); return; }
        var dialog = new Window { Owner = this, Title = L("Neuer Entwurf", "Новый черновик релиза", "New release draft"), Width = 570, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Brush(Pale), FontFamily = FontFamily };
        var body = new StackPanel();
        var info = Text(repo + "\n" + L("Nur ein Entwurf: keine Veröffentlichung, noch kein Upload. Neuer Tag verwendet die Standard-Branch, bestehender Tag sein Ziel.", "Только черновик: без публикации и отправки файлов. Новый тег использует основную ветку проекта, существующий — свою версию.", "Draft only: no publication or file upload. A new tag uses the default branch; an existing tag keeps its target."), 15, Ink, false);
        info.TextWrapping = TextWrapping.Wrap;
        body.Children.Add(info);
        var label = Text(L("Version (z.B. v1.0.0)", "Версия (например v1.0.0)", "Version (e.g. v1.0.0)"), 16, Ink, true);
        label.Margin = new Thickness(0, 16, 0, 0);
        body.Children.Add(label);
        var tag = Input();
        tag.Name = "DraftVersion";
        System.Windows.Automation.AutomationProperties.SetLabeledBy(tag, label);
        body.Children.Add(tag);
        var titleLabel = Text(L("Titel (optional)", "Название (необязательно)", "Title (optional)"), 16, Ink, true);
        titleLabel.Margin = new Thickness(0, 16, 0, 0);
        body.Children.Add(titleLabel);
        var title = Input();
        title.Name = "DraftTitle";
        System.Windows.Automation.AutomationProperties.SetLabeledBy(title, titleLabel);
        body.Children.Add(title);
        var error = Text("", 14, Hex("#B32640"), false);
        error.TextWrapping = TextWrapping.Wrap;
        error.Margin = new Thickness(0, 8, 0, 0);
        body.Children.Add(error);
        var create = MakeButton(L("Entwurf auf GitHub erstellen", "Создать черновик на GitHub", "Create draft on GitHub"), true, 0);
        create.Name = "DraftCreate";
        create.IsDefault = true;
        create.Margin = new Thickness(0, 16, 0, 0);
        body.Children.Add(create);
        create.Click += delegate {
            string value = tag.Text.Trim();
            if (!Regex.IsMatch(value, @"^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$") || value.Contains("..") || value.EndsWith(".") || value.EndsWith(".lock")) {
                error.Text = L("Versionskennung: Buchstaben, Zahlen, Punkt, - oder _ (max. 80).", "Метка версии: латинские буквы, цифры, точка, - или _ (до 80 символов).", "Version tag: letters, digits, dot, - or _ (max. 80)."); return;
            }
            if (title.Text.Trim().Length > 120) { error.Text = L("Titel: maximal 120 Zeichen.", "Название: не больше 120 символов.", "Title: at most 120 characters."); return; }
            dialog.DialogResult = true;
        };
        dialog.Content = new Border { Background = Brush(Pale), Padding = new Thickness(24), Child = body };
        dialog.Loaded += delegate { tag.Focus(); };
        if (dialog.ShowDialog() != true) return;
        try {
            draftRequestPath = Path.Combine(Path.GetTempPath(), "WatchdogCreateDraft-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(draftRequestPath, json.Serialize(new Dictionary<string, string> { { "repository", repo }, { "tag", tag.Text.Trim() }, { "title", String.IsNullOrWhiteSpace(title.Text) ? tag.Text.Trim() : title.Text.Trim() } }), new UTF8Encoding(false));
            Launch("CreateDraft", false);
            if (!uploadRunning && File.Exists(draftRequestPath)) { File.Delete(draftRequestPath); draftRequestPath = null; }
        } catch (Exception ex) { ShowError(ex.Message); }
    }

    private void LoadFiles()
    {
        if(downloadMode) { if(changingDirection) return; RefreshRemoteFiles(); return; }
        CancelFileScan();
        int scanVersion = fileScanVersion;
        var cancellation = new System.Threading.CancellationTokenSource();
        fileScanCancellation = cancellation;
        filesLoading = true;
        fileChecks.Clear();
        fileStatusLabels.Clear();
        // A finished worker's periodic snapshot must not repaint the new mode.
        if (!uploadRunning) { progressPath = null; lastProgressRead = 0; }
        latestSnapshot = null;
        confirmedFiles.Clear();
        phase = "ready";
        currentFile = "";
        emptyFilesLabel = null;
        emptyFilesState = null;
        ResetTableItems();
        string path = folderPath.Text;
        bool codeMode = CodeMode;
        bool selectDefaults = codeMode && !config.ContainsKey("ProjectFiles");
        var remembered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        object values;
        if (config.TryGetValue(CodeMode ? "ProjectFiles" : "Files", out values))
        {
            var names = values as IEnumerable;
            if (names != null && !(values is string)) foreach (var name in names) remembered.Add(Convert.ToString(name));
        }
        emptyFilesState = "loading";
        emptyFilesLabel = EmptyLine(FileScanMessage());
        fileList.Items.Add(emptyFilesLabel);
        RefreshFileActions();
        UpdateSelection();
        System.Threading.ThreadPool.QueueUserWorkItem(delegate {
            var files = new List<ScannedFile>();
            string error = null;
            string state = null;
            try {
                cancellation.Token.ThrowIfCancellationRequested();
                if (!Directory.Exists(path)) state = "missing";
                else {
                    string prefix = Path.GetFullPath(path).TrimEnd('\\') + "\\";
                    foreach (string file in codeMode ? EnumerateProjectFiles(path, cancellation.Token) : Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly)) {
                        cancellation.Token.ThrowIfCancellationRequested();
                        var info = new FileInfo(file);
                        files.Add(new ScannedFile { FullPath = info.FullName, RelativePath = codeMode ? info.FullName.Substring(prefix.Length).Replace('\\','/') : info.Name, Length = info.Length });
                    }
                    files.Sort((left,right) => StringComparer.CurrentCultureIgnoreCase.Compare(left.RelativePath,right.RelativePath));
                    if (files.Count == 0) state = "empty";
                }
            } catch (System.OperationCanceledException) { return; }
            catch (Exception ex) { state = "error"; error = ex.Message; }
            if (cancellation.IsCancellationRequested || Dispatcher.HasShutdownStarted) return;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate {
                if (scanVersion != fileScanVersion) return;
                fileList.Items.Clear(); emptyFilesLabel = null; emptyFilesState = state;
                if (state != null) {
                    emptyFilesLabel = EmptyLine(FileScanMessage()); emptyFilesLabel.ToolTip = error;
                    fileList.Items.Add(emptyFilesLabel); FinishFileScan(scanVersion); return;
                }
                AddScannedFiles(files, 0, remembered, codeMode, selectDefaults, scanVersion);
            }));
        });
    }

    private void AddScannedFile(ScannedFile info, HashSet<string> remembered, bool codeMode, bool selectDefaults)
    {
            string relative = info.RelativePath;
            var check=FileCheck(relative,remembered.Contains(relative) || (selectDefaults && !SensitiveFile(relative)));
            check.IsEnabled = false;
            if(codeMode && SensitiveFile(relative)) check.ToolTip=L("Mögliche Zugangsdaten: Inhalt prüfen.","Возможно, учётные данные: проверьте содержимое.","Possible credentials: review contents.");
            check.Checked += delegate { if (!changingFileSelection && !filesLoading) UpdateSelection(); };
            check.Unchecked += delegate { if (!changingFileSelection && !filesLoading) UpdateSelection(); };
            var fileStatus = Text(check.IsChecked == true ? L("Bereit","Готов","Ready") : L("Nicht gewählt","Не выбран","Not selected"), 14, Muted, false);
            fileStatusLabels[relative]=fileStatus;
            fileChecks.Add(check);
            fileList.Items.Add(new FileRowModel {Info=info,Check=check,Status=fileStatus});
    }
    private sealed class FileRowModel {public ScannedFile Info;public CheckBox Check;public TextBlock Status;}
    private sealed class FileRowConverter : IValueConverter
    {
        private readonly WatchdogWindow owner;
        public FileRowConverter(WatchdogWindow window){owner=window;}
        public object Convert(object value,Type targetType,object parameter,System.Globalization.CultureInfo culture){var model=value as FileRowModel;return model==null ? value : owner.CreateFileRow(model);}
        public object ConvertBack(object value,Type targetType,object parameter,System.Globalization.CultureInfo culture){throw new NotSupportedException();}
    }
    private FrameworkElement CreateFileRow(FileRowModel model)
    {
            var row=TableRow();string relative=model.Info.RelativePath;
            var oldCheckParent=model.Check.Parent as Panel;if(oldCheckParent!=null)oldCheckParent.Children.Remove(model.Check);
            var oldStatusParent=model.Status.Parent as Panel;if(oldStatusParent!=null)oldStatusParent.Children.Remove(model.Status);
            AddTableCell(row, model.Check, 0);
            var document = new System.Windows.Shapes.Path { Width = 16, Height = 20, Data = DocumentGeometry(), Stroke = Brush(Muted), StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Round };
            AddTableCell(row, document, 1);
            document.Margin = new Thickness(4, 0, 4, 0);
            var name=Text(relative,16,Ink,false);
            name.Tag = relative; name.ContextMenu = FileLinkMenu();
            name.ToolTip = model.Info.FullPath;
            AddTableCell(row, name, 2);
            var size = Text(FormatSize(model.Info.Length), 14, Muted, false);
            size.TextAlignment = TextAlignment.Right;
            AddTableCell(row, size, 3);
            AddTableCell(row, model.Status, 4);
            return new Border { BorderBrush = Brush(Hex("#E8EAED")), BorderThickness = new Thickness(0, 0, 0, 1), Child = row };
    }

    private static TextBlock EmptyLine(string message)
    {
        return new TextBlock { Text = message, Foreground = Brush(Muted), FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(14, 12, 14, 12) };
    }

    private static string FormatSize(long size)
    {
        return size >= 1048576 ? (size / 1048576.0).ToString("N1") + " MiB" : (size / 1024.0).ToString("N0") + " KiB";
    }

    private void SetAllFiles(bool selected)
    {
        if (filesLoading) return;
        changingFileSelection = true;
        try { foreach (var item in fileChecks) item.IsChecked = selected && !(!downloadMode && CodeMode && SensitiveFile((string)item.Tag)); }
        finally { changingFileSelection = false; }
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        if (selectedCount == null) return;
        int count = fileChecks.Count(item => item.IsChecked == true);
        selectedCount.Text = L("{0} von {1} ausgewählt", "Выбрано {0} из {1}", "Selected {0} of {1}").Replace("{0}", count.ToString()).Replace("{1}", fileChecks.Count.ToString());
        UpdatePresentation();
    }

    private void UpdatePresentation()
    {
        int active = phase == "ready" ? 0 : (checking || phase == "checking" || phase == "checked" ? 1 : 2);
        for (int i = 0; i < 3; i++)
        {
            stageFrames[i].Background = i == active ? Brush(Hex("#E8E8EA")) : Brushes.Transparent;
            stageDots[i].Background = Brush(i == active ? (phase == "failed" ? Hex("#B32640") : Accent) : Hex("#92969D"));
            stageLabels[i].FontWeight = i == active ? FontWeights.SemiBold : FontWeights.Normal;
        }
        foreach (var check in fileChecks)
        {
            string name = (string)check.Tag;
            TextBlock label;
            if (!fileStatusLabels.TryGetValue(name, out label)) continue;
            bool confirmed = confirmedFiles.Contains(name);
            bool current = String.Equals(name, currentFile, StringComparison.OrdinalIgnoreCase);
            label.Text = confirmed ? L("Hochgeladen", "Загружен", "Uploaded")
                : current && phase == "failed" ? L("Fehler", "Ошибка", "Failed")
                : current && phase == "retrying" ? L("Wiederholung", "Повтор", "Retrying")
                : current && phase == "uploading" ? L("Wird geladen", "Загрузка", "Uploading")
                : phase == "checking" && check.IsChecked == true ? L("Wird geprüft", "Проверка", "Checking")
                : phase == "checked" && check.IsChecked == true ? L("Geprüft", "Проверен", "Checked")
                : check.IsChecked == true ? L("Bereit", "Готов", "Ready") : L("Nicht gewählt", "Не выбран", "Not selected");
            if(downloadMode && confirmed) label.Text=L("Heruntergeladen","Скачан","Downloaded");
            else if(downloadMode && current && phase=="downloading") label.Text=L("Download","Скачивание","Downloading");
            else if(downloadMode && current && phase=="paused") label.Text=L("Pausiert","Пауза","Paused");
            label.Foreground = Brush(confirmed ? Hex("#18785A") : current && phase == "failed" ? Hex("#B32640") : current ? AccentHover : Muted);
        }
        UpdateProgressAppearance();
    }

    private int ReadLanguage()
    {
        try
        {
            var values = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(root, "ui-settings.json"), Encoding.UTF8));
            var code = Convert.ToString(values["language"]);
            return code == "ru" ? 1 : (code == "en" ? 2 : 0);
        }
        catch { return 0; }
    }

    private void SaveLanguage()
    {
        if (language == null || language.SelectedIndex < 0) return;
        try
        {
            var codes = new[] { "de", "ru", "en" };
            File.WriteAllText(Path.Combine(root, "ui-settings.json"), json.Serialize(new Dictionary<string, string> { { "language", codes[LanguageIndex] } }), new UTF8Encoding(false));
        }
        catch { /* Read-only portable media: keep German on next launch. */ }
    }

    private int Number(TextBox field, string label, int min, int max)
    {
        int result;
        if (!int.TryParse(field.Text.Trim(), out result) || result < min || result > max)
            throw new InvalidOperationException(label + ": " + min + "–" + max);
        return result;
    }

    private bool SaveConfig()
    {
        if(downloadMode) {SaveSyncPreferences();return true;}
        if (filesLoading) return false;
        try
        {
            var repo = NormalizeRepository(repository.Text);
            var tag = releaseTag.Text.Trim();
            if (!Regex.IsMatch(repo, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$") || repo == "OWNER/REPOSITORY")
                throw new InvalidOperationException(L("In Schritt 1 ein Projekt auswählen oder GitHub-Link einfügen.", "На шаге 1 выберите проект или вставьте ссылку на него с GitHub.", "Choose a project in step 1 or paste its GitHub link."));
            if (!CodeMode && tag.Length == 0)
                throw new InvalidOperationException(L("In Schritt 2 einen Entwurf auswählen.", "На шаге 2 нажмите «Выбрать» и выберите черновик.", "Click Choose in step 2 and select a draft."));
            if (!Directory.Exists(folderPath.Text))
                throw new InvalidOperationException(L("Ordner nicht gefunden.", "Папка не найдена.", "Folder not found."));
            var selected = fileChecks.Where(item => item.IsChecked == true).Select(item => (string)item.Tag).ToArray();
            if (selected.Length == 0)
                throw new InvalidOperationException(L("Mindestens eine Datei wählen.", "Выберите хотя бы один файл.", "Select at least one file."));
            foreach (var name in selected)
                if (!File.Exists(Path.Combine(folderPath.Text, name)))
                    throw new InvalidOperationException(L("Datei fehlt: ", "Файл пропал: ", "File missing: ") + name);
            var attempts = Number(retries, "MaxAttempts", 1, 10);
            var retrySeconds = Number(delay, "RetryBaseSeconds", 1, 300);
            var pollSeconds = Number(poll, "PollSeconds", 5, 300);
            var hours = Number(timeout, "HttpTimeoutHours", 1, 24);
            object pid = null;
            if (!String.IsNullOrWhiteSpace(waitPid.Text)) pid = Number(waitPid, "WaitForProcessId", 1, int.MaxValue);

            var next = new Dictionary<string, object>(config);
            next["Repository"] = repo;
            next["ReleaseTag"] = tag;
            next[CodeMode ? "ProjectSourceDirectory" : "SourceDirectory"]=Path.GetFullPath(folderPath.Text);
            next[CodeMode ? "ProjectFiles" : "Files"]=selected;
            next["UploadMode"]=CodeMode ? "code" : "release"; next["RepositoryPath"]=repositoryDestination.Text.Trim(); next["RepositoryBranch"]=codeBranch.Text.Trim();
            next["PublishAfterUpload"]=publishCheck.IsChecked==true; next["ExpectedAccount"]=accountLogin;
            next["MaxAttempts"] = attempts;
            next["RetryBaseSeconds"] = retrySeconds;
            next["PollSeconds"] = pollSeconds;
            next["HttpTimeoutHours"] = hours;
            next["WaitForProcessId"] = pid;
            var path = Path.Combine(root, "config.json");
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, json.Serialize(next), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            config = next;
            status.Text = L("Einstellungen gespeichert.", "Настройки сохранены.", "Settings saved.");
            status.Visibility = Visibility.Visible;
            status.Foreground = Brush(Hex("#18785A"));
            return true;
        }
        catch (Exception ex) { ShowError(ex.Message); return false; }
    }

    private void StartUpload()
    {
        if(uploadRunning || !SaveConfig()) return;
        if(CodeMode) { preparingCodeUpload=true; WriteUploaderRequest("preview",null); Launch("CodePreview",false); return; }
        if(publishCheck.IsChecked==true && String.IsNullOrEmpty(accountLogin)) { ShowError(L("Zuerst anmelden.","Сначала выполните вход в GitHub.","Sign in first.")); return; }
        var chosen = fileChecks.Where(item => item.IsChecked == true).ToArray();
        long bytes = chosen.Sum(item => new FileInfo(Path.Combine(folderPath.Text, (string)item.Tag)).Length);
        var prompt = (publishCheck.IsChecked==true ? L("Nach Prüfung veröffentlichen. Private Projekte bleiben privat.","После проверки файлов релиз будет опубликован. Приватный проект останется приватным.","Publish after verification. Private projects remain private.") : L("Unveröffentlichten Entwurf speichern.","Сохранить неопубликованный черновик.","Save an unpublished draft.")) + "\n\n" + repository.Text.Trim() + " · " + releaseTag.Text.Trim() + "\n" + chosen.Length + " " + L("Dateien", "файлов", "files") + " · " + FormatSize(bytes);
        if (!ConfirmUpload(prompt)) return;
        Launch("Start-UploadWatchdog.cmd", true);
    }

    private bool ConfirmUpload(string prompt)
    {
        var dialog = new Window { Owner = this, Title = L("Upload bestätigen", "Подтвердить загрузку", "Confirm upload"), Width = 570, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Brush(Pale), FontFamily = FontFamily };
        var body = new StackPanel();
        var heading = Text(publishingConfirmation ? L("Release veröffentlichen?", "Опубликовать релиз?", "Publish release?") : L("Ausgewählte Dateien hochladen?", "Загрузить выбранные файлы?", "Upload selected files?"), 20, Ink, true);
        heading.TextWrapping = TextWrapping.Wrap; body.Children.Add(heading);
        var details = Text(prompt, 16, Ink, false); details.TextWrapping = TextWrapping.Wrap; details.Margin = new Thickness(0, 16, 0, 16); body.Children.Add(details);
        var buttons = new UniformGrid { Columns = 2 };
        var cancel = MakeButton(L("Abbrechen", "Отмена", "Cancel"), false, 0); cancel.Name = "ConfirmCancel"; cancel.IsCancel = true; cancel.Margin = new Thickness(0, 0, 6, 0);
        var confirm = MakeButton(publishingConfirmation ? L("Veröffentlichen", "Опубликовать", "Publish") : L("Hochladen", "Загрузить", "Upload"), true, 0); confirm.Name = "ConfirmSend"; confirm.Margin = new Thickness(6, 0, 0, 0);
        cancel.Click += delegate { dialog.DialogResult = false; }; confirm.Click += delegate { dialog.DialogResult = true; };
        buttons.Children.Add(cancel); buttons.Children.Add(confirm); body.Children.Add(buttons);
        dialog.Content = new Border { Background = Brush(Pale), Padding = new Thickness(24), Child = body };
        dialog.Loaded += delegate { cancel.Focus(); };
        return dialog.ShowDialog() == true;
    }

    private void Launch(string file, bool upload)
    {
        catalogRequest = file == "ListProjects" ? "projects" : file == "ListDrafts" ? "drafts" : file == "CreateDraft" ? "create" : file == "CreateProject" ? "create-project" : "";
        if(file=="Branches") catalogRequest="branches";
        signingIn = file == "SignIn";
        var path = Path.Combine(root, "Release-UploadWatchdog.ps1");
        if (!File.Exists(path)) { ShowIncompletePackage(); return; }
        try
        {
            progressPath = Path.Combine(Path.GetTempPath(), "GitHubReleaseWatchdog-" + Guid.NewGuid().ToString("N") + ".json");
            string session = progressPath;
            latestSnapshot = null;
            confirmedFiles.Clear();
            uploadedReleaseUrl = "";
            openFilesButton.Visibility = Visibility.Collapsed;
            resultDownloads.Clear(); downloadChoices.Clear(); downloadLinksButton.Visibility = Visibility.Collapsed; copyResultButton.Visibility = Visibility.Collapsed; publishLaterButton.Visibility = Visibility.Collapsed;
            currentFile = "";
            phase = upload ? "preparing" : "checking";
            checking = !upload;
            lastProgressRead = 0;
            overallBar.Value = 0;
            fileBar.Value = 0;
            overallBar.IsIndeterminate = true;
            fileBar.IsIndeterminate = true;
            var info=CreateWorkerInfo(upload,progressPath);
            if(file=="CodePreview" || file=="CodeUpload" || file=="Branches" || file=="Publish") info.Arguments=info.Arguments.Replace(" -CheckOnly","")+" -UploaderRequest \""+uploaderRequestPath+"\""+((file=="CodePreview" || file=="Branches") ? " -CheckOnly" : "");
            if (signingIn) info.Arguments += " -SignInOnly";
            if (catalogRequest == "projects") info.Arguments += " -ListRepositories";
            if (catalogRequest == "drafts") info.Arguments += " -ListDraftsFor " + repository.Text;
            if (catalogRequest == "create") info.Arguments = info.Arguments.Replace(" -CheckOnly", "") + " -CreateDraftRequest \"" + draftRequestPath + "\"";
            if (catalogRequest == "create-project") info.Arguments = info.Arguments.Replace(" -CheckOnly", "") + " -CreateProjectRequest \"" + draftRequestPath + "\"";
            var process = new Process { StartInfo = info, EnableRaisingEvents = true };
            activeWorker=process;
            // Drain both pipes without persisting raw credential/provider output.
            process.OutputDataReceived += delegate { };
            process.ErrorDataReceived += delegate { };
            SetBusy(true);
            ApplyLanguage();
            status.Text = L("Vorgang läuft im Hintergrund.", "Операция выполняется в фоне.", "Operation runs in the background.");
            status.Foreground = Brush(Muted);
            process.Exited += delegate
            {
                Dispatcher.BeginInvoke((Action)delegate
                {
                    if (progressPath != session) { process.Dispose(); return; }
                    activeWorker=null;
                    ReadProgress(); CleanupUploaderRequest();
                    if(phase=="project-planned") { var result=latestSnapshot; accountLogin=StateString(result,"account",""); latestSnapshot=null; progressPath=null; phase="ready"; SetBusy(false); ApplyLanguage(); process.Dispose(); ShowCodePlan(result); return; }
                    if (catalogRequest == "create" || catalogRequest == "create-project") {
                        try { if (!String.IsNullOrEmpty(draftRequestPath) && File.Exists(draftRequestPath)) File.Delete(draftRequestPath); } catch { }
                        draftRequestPath = null;
                    }
                    if (catalogRequest == "create-project" && phase == "project-created") {
                        repository.Text = StateString(latestSnapshot, "createdRepository", "");
                        releaseTag.Clear(); accountLogin = StateString(latestSnapshot, "account", "");
                        catalogRequest = ""; latestSnapshot = null; progressPath = null; phase = "ready";
                        SetBusy(false); ApplyLanguage();
                        status.Text=CodeMode ? L("Projekt bereit. Dateien wählen.","Проект создан и выбран. Выберите файлы.","Project ready. Choose files.") : L("Projekt erstellt. Entwurf wählen.","Проект создан и выбран. Выберите черновик.","Project created. Choose a draft.");
                        status.Visibility = Visibility.Visible; status.Foreground = Brush(Hex("#18785A"));
                        process.Dispose(); return;
                    }
                    if (catalogRequest == "create" && phase == "draft-created") {
                        releaseTag.Text = StateString(latestSnapshot, "createdTag", "");
                        accountLogin = StateString(latestSnapshot, "account", "");
                        catalogRequest = ""; latestSnapshot = null; progressPath = null; phase = "ready";
                        SetBusy(false); ApplyLanguage();
                        status.Text = L("Entwurf ausgewählt. Dateien wählen und hochladen.", "Черновик готов и выбран. Отметьте файлы и нажмите «Загрузить выбранные файлы».", "Draft ready and selected. Choose files and click Upload selected files.");
                        status.Visibility = Visibility.Visible; status.Foreground = Brush(Hex("#18785A"));
                        process.Dispose(); return;
                    }
                    if (catalogRequest.Length > 0 && phase == "catalog-ready") {
                        string kind = catalogRequest;
                        var result = latestSnapshot;
                        accountLogin = StateString(result, "account", "");
                        catalogRequest = "";
                        latestSnapshot = null;
                        progressPath = null;
                        phase = "ready";
                        SetBusy(false);
                        ApplyLanguage();
                        process.Dispose();
                        ShowCatalog(kind, result);
                        return;
                    }
                    if (signingIn) {
                        accountLogin = phase == "checked" && latestSnapshot != null ? StateString(latestSnapshot, "message", "") : "";
                        if (phase == "checked") {
                            phase = "ready";
                            latestSnapshot = null;
                            progressPath = null;
                            status.Text = L("Anmeldung bestätigt. Noch keine Dateien gesendet.", "Вход подтверждён. Файлы ещё не отправлялись.", "Sign-in confirmed. No files sent yet.");
                        }
                        signingIn = false;
                    }
                    if (phase != "completed" && phase != "checked" && phase != "failed" && !(phase == "ready" && !String.IsNullOrEmpty(accountLogin)))
                    {
                        phase = "failed";
                        status.Text = L("Prozess beendet, ohne Abschlussbestätigung.", "Процесс завершился без подтверждения результата.", "Process ended without completion confirmation.");
                        status.Foreground = Brush(Hex("#B32640"));
                        status.Visibility = Visibility.Visible;
                    }
                    SetBusy(false);
                    ApplyLanguage();
                    process.Dispose();
                });
            };
            if (!process.Start()) { process.Dispose(); throw new InvalidOperationException(L("Start fehlgeschlagen.", "Не удалось запустить процесс.", "Could not start process.")); }
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (Exception ex) { activeWorker=null; CleanupUploaderRequest(); SetBusy(false); ShowError(ex.Message); }
    }

    private ProcessStartInfo CreateWorkerInfo(bool upload, string session)
    {
        return new ProcessStartInfo {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
            Arguments = "-NoLogo -NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(root, "Release-UploadWatchdog.ps1") + "\" -ProgressFile \"" + session + "\" -Language " + new[] { "de", "ru", "en" }[LanguageIndex] + (upload ? "" : " -CheckOnly"),
            WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true
        };
    }

    private void SetBusy(bool busy)
    {
        uploadRunning = busy;
        startButton.Content = busy && !checking ? L("Upload läuft", "Идёт загрузка", "Upload running") : L("Ausgewählte Dateien hochladen", "Загрузить выбранные файлы", "Upload selected files");
        checkButton.Content = busy && checking ? L("Prüfung läuft", "Идёт проверка", "Checking") : L("Prüfen", "Проверить", "Check");
        startButton.IsEnabled = !busy;
        checkButton.IsEnabled = !busy;
        saveButton.IsEnabled = !busy;
        signInButton.IsEnabled = !busy;
        projectsButton.IsEnabled = !busy;
        draftsButton.IsEnabled = !busy;
        browseButton.IsEnabled = !busy;
        modePicker.IsEnabled=!busy; publishCheck.IsEnabled=!busy; addFilesButton.IsEnabled=!busy; branchesButton.IsEnabled=!busy; publishLaterButton.IsEnabled=!busy; repositoryDestination.IsReadOnly=busy;
        selectAllButton.IsEnabled = !busy;
        clearButton.IsEnabled = !busy;
        ApplyUploaderLanguage();
        ApplySyncLanguage();
        repository.IsReadOnly = busy;
        releaseTag.IsReadOnly = true;
        advanced.IsEnabled = !busy;
        foreach (var check in fileChecks) check.IsEnabled = !busy && !filesLoading;
        RefreshFileActions();
    }

    private static ProgressBar MakeProgressBar()
    {
        var bar = new ProgressBar { Height = 12, Minimum = 0, Maximum = 100, Value = 0, Foreground = Brush(Accent), Background = Brush(Hex("#ECECEE")), BorderThickness = new Thickness(0) };
        var template = new ControlTemplate(typeof(ProgressBar));
        var frame = new FrameworkElementFactory(typeof(Border));
        frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        frame.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        var track = new FrameworkElementFactory(typeof(Grid));
        track.Name = "PART_Track";
        var indicator = new FrameworkElementFactory(typeof(Border));
        indicator.Name = "PART_Indicator";
        indicator.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        indicator.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        indicator.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.ForegroundProperty));
        track.AppendChild(indicator);
        var activity = new FrameworkElementFactory(typeof(Border));
        activity.Name = "activity";
        activity.SetValue(FrameworkElement.WidthProperty, 80.0);
        activity.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        activity.SetValue(Border.BackgroundProperty, Brush(Accent));
        activity.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        activity.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
        track.AppendChild(activity);
        frame.AppendChild(track);
        template.VisualTree = frame;
        var unknown = new Trigger { Property = ProgressBar.IsIndeterminateProperty, Value = true };
        unknown.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed, "PART_Indicator"));
        unknown.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "activity"));
        var pulse = new DoubleAnimation(0.25, 1.0, new Duration(TimeSpan.FromSeconds(0.7))) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
        Storyboard.SetTargetName(pulse, "activity");
        Storyboard.SetTargetProperty(pulse, new PropertyPath(UIElement.OpacityProperty));
        var storyboard = new Storyboard();
        storyboard.Children.Add(pulse);
        unknown.EnterActions.Add(new BeginStoryboard { Storyboard = storyboard });
        template.Triggers.Add(unknown);
        bar.Template = template;
        return bar;
    }

    private void ReadProgress()
    {
        if (String.IsNullOrEmpty(progressPath) || !File.Exists(progressPath)) return;
        try
        {
            ApplyProgress(json.Deserialize<Dictionary<string, object>>(File.ReadAllText(progressPath, Encoding.UTF8)));
        }
        catch (IOException) { /* Atomic replacement may briefly contend with the reader. */ }
        catch (ArgumentException) { /* Ignore incomplete or malformed snapshots and retry next tick. */ }
        catch (InvalidOperationException) { }
    }

    private void ApplyProgress(Dictionary<string, object> snapshot)
    {
        if (snapshot == null) return;
        latestSnapshot = snapshot;
        if (StateString(snapshot, "state", "").StartsWith("sync-",StringComparison.Ordinal) || StateString(snapshot, "state", "") == "catalog-ready" || StateString(snapshot, "state", "") == "draft-created" || StateString(snapshot, "state", "") == "project-created" || StateString(snapshot, "state", "") == "project-planned") { phase = StateString(snapshot, "state", ""); return; }
        lastProgressRead++;
        phase = StateString(snapshot, "state", "preparing");
        string resultUrl=StateString(snapshot,"resultUrl",StateString(snapshot,"releaseUrl",""));
        if (IsReleaseUrl(resultUrl)) uploadedReleaseUrl = resultUrl;
        openFilesButton.Visibility = (phase == "completed" || phase == "checked") && IsReleaseUrl(uploadedReleaseUrl) ? Visibility.Visible : Visibility.Collapsed;
        currentFile = StateString(snapshot, "file", "");
        object names;
        if (snapshot.TryGetValue("confirmedFiles", out names))
        {
            confirmedFiles.Clear();
            var list = names as IEnumerable;
            if (list != null && !(names is string))
                foreach (var name in list) confirmedFiles.Add(Convert.ToString(name));
        }
        long totalBytes = StateLong(snapshot, "totalBytes");
        long completedBytes = StateLong(snapshot, "completedBytes");
        long fileBytes = StateLong(snapshot, "fileBytes");
        long fileSent = StateLong(snapshot, "fileSent");
        bool confirmed = confirmedFiles.Contains(currentFile);
        double overall = totalBytes > 0 ? Math.Max(0, Math.Min(100, 100.0 * (completedBytes + (confirmed ? 0 : fileSent)) / totalBytes)) : 0;
        if (phase == "completed") overall = 100;
        double current = confirmed ? 100 : fileBytes > 0 ? Math.Max(0, Math.Min(100, 100.0 * fileSent / fileBytes)) : 0;
        if (phase == "completed") current = 100;
        overallBar.Value = overall;
        fileBar.Value = current;
        bool measuring = phase == "checking" || phase == "checked" || phase == "preparing" || phase == "waiting" || phase == "retrying" || phase == "publishing" || phase == "committing" || phase == "verifying";
        overallBar.IsIndeterminate = phase != "completed" && (measuring || totalBytes <= 0);
        fileBar.IsIndeterminate = phase != "completed" && phase != "paused" && phase != "failed" && (measuring || fileBytes <= 0 || (StateString(snapshot,"mode","") == "code" && StateString(snapshot,"direction","") != "download" && !String.Equals(StateString(snapshot,"fileProgressKnown","false"),"true",StringComparison.OrdinalIgnoreCase)));
        string stage = phase == "completed" ? L("Abgeschlossen", "Завершено", "Completed")
            : phase == "publishing" ? L("Veröffentlichung", "Публикация", "Publishing")
            : phase == "committing" ? L("Commit bestätigen", "Подтверждение изменений", "Confirming commit")
            : phase == "checked" ? L("Geprüft", "Проверено", "Checked")
            : phase == "checking" ? L("Prüfung", "Проверка", "Checking")
            : phase == "failed" ? L("Fehler", "Ошибка", "Failed")
            : phase == "waiting" ? L("Warte auf Prozess", "Ожидание процесса", "Waiting for process")
            : phase == "retrying" ? L("Wiederholung", "Повтор", "Retrying")
            : phase == "preparing" ? L("Vorbereitung", "Подготовка", "Preparing")
            : L("Übertragung", "Передача", "Uploading");
        progressTitle.Text = L("Fortschritt · ", "Прогресс · ", "Progress · ") + stage;
        progressOverallText.Text = String.Format(L("Gesamt · {0:0}% · {1}/{2} Dateien", "Всего · {0:0}% · файлов {1}/{2}", "Overall · {0:0}% · {1}/{2} files"), overall, StateLong(snapshot, "filesCompleted"), StateLong(snapshot, "filesTotal"));
        progressFileText.Text = String.IsNullOrEmpty(currentFile) ? L("Aktuelle Datei · —", "Текущий файл · —", "Current file · —") : String.Format("{0} · {1:0}%", currentFile, current);
        progressFileText.ToolTip = currentFile;
        if (overallBar.IsIndeterminate) progressOverallText.Text = L("Gesamt · Dauer unbekannt", "Общий · длительность неизвестна", "Overall · duration unknown");
        if (fileBar.IsIndeterminate) progressFileText.Text = String.IsNullOrEmpty(currentFile) ? L("Aktuelle Datei · —", "Текущий файл · —", "Current file · —") : currentFile;
        status.Text = "";
        status.Foreground = Brush(Muted);
        if (phase == "retrying")
            status.Text = String.Format(L("Neuer Versuch in {0} s.", "Повтор через {0} с.", "Retrying in {0} s."), StateLong(snapshot, "retryAfterSeconds"));
        if (phase == "failed")
        {
            status.Text = L("Fehler: ", "Ошибка: ", "Failed: ") + StateString(snapshot, "message", L("Vorgang fehlgeschlagen.", "Операция не выполнена.", "Operation failed."));
            status.TextTrimming = TextTrimming.None;
            status.Foreground = Brush(Hex("#B32640"));
        }
        else if (phase == "completed" || phase == "checked")
        {
            status.Text = phase == "completed" ? L("Dateien im Release-Entwurf gespeichert, nicht im Code-Dateiverzeichnis. Nicht veröffentlicht; nur mit Zugriffsrechten sichtbar.", "Файлы сохранены в черновике релиза, а не в разделе Code. Черновик не опубликован и виден только с правами доступа.", "Files saved in the release draft, not the Code file list. The draft is unpublished and visible only with access rights.") : L("Prüfung abgeschlossen. Es wurden keine Dateien hochgeladen.", "Проверка завершена. Файлы не отправлялись.", "Check completed. No files were uploaded.");
            status.Foreground = Brush(Hex("#18785A"));
        }
        if (activeWorker==null && (phase == "completed" || phase == "checked" || phase == "failed" || phase == "paused")) SetBusy(false);
        if(StateString(snapshot,"direction","") == "download") ApplyDownloadProgress(snapshot); else UpdateResultActions(snapshot);
        status.Visibility = String.IsNullOrEmpty(status.Text) ? Visibility.Collapsed : Visibility.Visible;
        UpdatePresentation();
    }

    private static string StateString(Dictionary<string, object> state, string key, string fallback)
    {
        object value;
        return state.TryGetValue(key, out value) && value != null ? Convert.ToString(value) : fallback;
    }

    private static long StateLong(Dictionary<string, object> state, string key)
    {
        object value;
        long result;
        return state.TryGetValue(key, out value) && Int64.TryParse(Convert.ToString(value), out result) ? result : 0;
    }

    private bool IsReleaseUrl(string value)
    {
        Uri uri;
        string project = NormalizeRepository(repository.Text);
        return !String.IsNullOrEmpty(project) && Uri.TryCreate(value, UriKind.Absolute, out uri)
            && uri.Scheme == "https" && uri.Host == "github.com" && uri.IsDefaultPort
            && String.IsNullOrEmpty(uri.UserInfo) && (uri.AbsolutePath.StartsWith("/" + project + "/releases/", StringComparison.OrdinalIgnoreCase) || (CodeMode && uri.AbsolutePath.StartsWith("/" + project + "/tree/", StringComparison.OrdinalIgnoreCase)));
    }

    private void OpenUploadedFiles()
    {
        if(downloadMode) {if(Directory.Exists(localResultDirectory)) Process.Start(new ProcessStartInfo(localResultDirectory){UseShellExecute=true});return;}
        if (!IsReleaseUrl(uploadedReleaseUrl)) return;
        try { Process.Start(new ProcessStartInfo(uploadedReleaseUrl) { UseShellExecute = true }); }
        catch { ShowError(L("Browser konnte nicht geöffnet werden.", "Не удалось открыть браузер.", "Could not open the browser.")); }
    }

    private void OpenGuide()
    {
        var code = LanguageIndex == 1 ? "ru" : (LanguageIndex == 2 ? "en" : "de");
        var guide = Path.Combine(root, "docs", "Guide-" + code + ".html");
        if (!File.Exists(guide)) { ShowError(L("Anleitung fehlt.", "Инструкция не найдена.", "Guide not found.")); return; }
        try { Process.Start(new ProcessStartInfo(guide) { UseShellExecute = true }); }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void ShowError(string message)
    {
        status.Text = message;
        status.Visibility = Visibility.Visible;
        status.Foreground = Brush(Hex("#B32640"));
        MessageBox.Show(this, message, L("Hinweis", "Внимание", "Notice"), MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
