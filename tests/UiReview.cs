using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

internal static class UiReview
{
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly string[] Files = { "app.exe", "README-with-a-long-filename-for-layout-review.md", "changelog.txt" };
    private static object window;
    private static Type type;
    private static int checks;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            RenderOptions.ProcessRenderMode=System.Windows.Interop.RenderMode.SoftwareOnly;
            var assembly = Assembly.LoadFrom(args[0]);
            type = assembly.GetType("WatchdogWindow", true);
            type.GetField("privateStateRoot",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"private-state"));
            window = Activator.CreateInstance(type, true);
            var view = (Window)window;
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            WaitFiles();
            var output = args[1];
            Assert(view.Width == Math.Min(1080, SystemParameters.WorkArea.Width - 32), "Default width matches reference and fits work area");
            Assert(view.Height == Math.Min(1040, SystemParameters.WorkArea.Height - 32), "Taller default height matches reference and fits work area");
            Directory.CreateDirectory(output);
            Assert(view.Icon != null, "Embedded window icon");
            var windowIcon = (BitmapSource)view.Icon;
            Assert(windowIcon.PixelWidth == 32 && windowIcon.PixelHeight == 32, "Taskbar/window chrome uses the dedicated 32px ICO frame");
            Assert(!Find<Image>((DependencyObject)view.Content).Any(), "No large decorative header icon");
            Assert(((List<CheckBox>)Field("fileChecks")).Count == 3, "Synthetic file table");
            Assert(((Button)Field("signInButton")).Height == 36, "Sign-in uses compact sidebar button");
            var accountField = view.GetType().GetField("accountLogin", BindingFlags.NonPublic | BindingFlags.Instance);
            accountField.SetValue(view, "synthetic-account");
            view.GetType().GetMethod("ApplyLanguage", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(view, null);
            Assert(((TextBlock)Field("accountText")).Text.Contains("synthetic-account"), "Verified account shown without token");
            accountField.SetValue(view, "");
            view.GetType().GetMethod("ApplyLanguage", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(view, null);
            Layout(view, 1064, 1000);
            var pathInput = (TextBox)Field("folderPath");
            Assert(((ScrollViewer)pathInput.Template.FindName("PART_ContentHost", pathInput)).Margin.Left == 0, "Folder path has no duplicate text inset");
            Assert(((ScrollViewer)Field("contentScroll")).ScrollableHeight < 1, "Three files in idle mode need no outer scrolling");
            Assert(((Border)Field("progressFrame")).ActualHeight < 40, "Idle progress is compact");
            ((TextBox)Field("folderPath")).Text = @"C:\Releases\" + new string('a', 220) + @"\Build";
            ((TextBox)Field("folderPath")).ToolTip = ((TextBox)Field("folderPath")).Text;
            Layout(view, 1064, 1000);
            Assert(((Button)Field("browseButton")).ActualWidth >= 180, "Long path cannot displace folder button");
            Assert(((TextBox)Field("folderPath")).Text.Length > 220, "Long path value is not truncated in data");
            ((TextBox)Field("folderPath")).Text = @"C:\Releases\Build";
            ((TextBox)Field("folderPath")).ToolTip = @"C:\Releases\Build";
            Assert(((ComboBox)Field("language")).SelectedIndex == 0, "German first launch");
            var normalize = type.GetMethod("NormalizeRepository", BindingFlags.Static | BindingFlags.NonPublic);
            Assert((string)normalize.Invoke(null, new object[] { "https://github.com/owner/repo/releases" }) == "owner/repo", "GitHub URL accepted without tag guessing");
            Assert((string)normalize.Invoke(null, new object[] { "owner/repo.git" }) == "owner/repo", "Git suffix normalized");
            Assert((string)normalize.Invoke(null, new object[] { "https://wrong.test/owner/repo" }) == "", "Other hosts rejected");
            Assert(((TextBox)Field("releaseTag")).IsReadOnly, "Draft is selected rather than guessed");
            ((TextBlock)Field("status")).Visibility = Visibility.Collapsed;
            Apply("checking", "", 0, 0, new string[0]);
            Assert(((ProgressBar)Field("overallBar")).IsIndeterminate, "Unknown check duration uses activity indicator");
            Assert(!((TextBlock)Field("progressOverallText")).Text.Contains("%"), "Unknown duration has no invented percentage");
            Assert(((TextBlock)Field("fileStatusLabels", Files[0])).Text == "Wird geprüft", "Check state");
            Apply("checked", "", 0, 0, new string[0]);
            Assert(((TextBlock)Field("fileStatusLabels", Files[0])).Text == "Geprüft", "Checked state");
            Apply("preparing", "", 0, 0, new string[0]);
            Apply("uploading", Files[0], 100, 0, new string[0]);
            Assert(((TextBlock)Field("fileStatusLabels", Files[0])).Text == "Wird geladen", "100% sent does not imply GitHub confirmation");
            Apply("uploading", Files[0], 0, 100, new[] { Files[0] });
            Assert(Math.Abs(((ProgressBar)Field("overallBar")).Value - 100.0 / 3) < .01, "Confirmed bytes counted once");
            Assert(((ProgressBar)Field("fileBar")).Value == 100, "Confirmed current file complete");
            Assert(((TextBlock)Field("fileStatusLabels", Files[0])).Text == "Hochgeladen", "Confirmed row");
            Apply("retrying", Files[1], 0, 100, new[] { Files[0] });
            Assert(((TextBlock)Field("fileStatusLabels", Files[1])).Text == "Wiederholung", "Retry row");
            Apply("waiting", "", 0, 100, new[] { Files[0] });
            Apply("failed", Files[1], 0, 100, new[] { Files[0] });
            Assert(((TextBlock)Field("fileStatusLabels", Files[1])).Text == "Fehler", "Failure row");
            var codeBytes = new Dictionary<string,object>{{"state","uploading"},{"mode","code"},{"direction","upload"},{"file",Files[0]},{"fileBytes",100},{"fileSent",50},{"totalBytes",300},{"completedBytes",0},{"confirmedFiles",new string[0]},{"fileProgressKnown",true}};
            type.GetMethod("ApplyProgress",Private).Invoke(window,new object[]{codeBytes});
            Assert(!((ProgressBar)Field("fileBar")).IsIndeterminate && ((ProgressBar)Field("fileBar")).Value==50,"Code streaming shows actual byte progress");
            Apply("failed",Files[0],0,0,new string[0]);
            Assert(!((ProgressBar)Field("fileBar")).IsIndeterminate,"Failed transfer stops activity animation");
            Apply("completed", "", 0, 300, Files);
            Assert(((Button)Field("openFilesButton")).Visibility == Visibility.Collapsed, "No guessed draft URL after completion");
            var releaseSnapshot = new Dictionary<string, object> { {"state", "completed"}, {"releaseUrl", "https://github.com/example/release-test/releases/tag/untagged-fixture"} };
            type.GetMethod("ApplyProgress", Private).Invoke(window, new object[] { releaseSnapshot });
            Assert(((Button)Field("openFilesButton")).Visibility == Visibility.Visible, "Completed upload links to exact API draft URL");
            Assert(((TextBlock)Field("status")).Text.Contains("nicht im Code"), "Completion explains release assets versus Code files");
            Apply("checked", "", 0, 300, Files);
            Assert(((Button)Field("openFilesButton")).Visibility == Visibility.Visible, "Read-only check also opens existing files without reupload");
            foreach (string invalid in new[] { "http://github.com/example/release-test/releases/tag/x", "https://evil.test/example/release-test/releases/tag/x", "https://github.com/other/repo/releases/tag/x", "https://github.com/example/release-test", "https://user@github.com/example/release-test/releases/tag/x" })
                Assert(!(bool)type.GetMethod("IsReleaseUrl", Private).Invoke(window, new object[] { invalid }), "Reject invalid or unrelated release URL: " + invalid);
            Apply("failed", "", 0, 0, new string[0]);
            Assert(((Button)Field("openFilesButton")).Visibility == Visibility.Collapsed, "Failure does not advertise success link");
            Apply("completed", "", 0, 300, Files);
            Assert(((ProgressBar)Field("overallBar")).Value == 100, "Completed aggregate");
            Assert(((ProgressBar)Field("fileBar")).Value == 100, "Completed file bar remains full");
            var language = (ComboBox)Field("language");
            language.SelectedIndex = 1;
            Layout(view, 1064, 1000);
            Render(view, Path.Combine(output, "ru-tall-completed.png"), 1);
            Assert(((ScrollViewer)Field("contentScroll")).ScrollableHeight < 1, "Tall reference window fits completed transfer without outer scrolling: " + ((ScrollViewer)Field("contentScroll")).ScrollableHeight + " viewport=" + ((ScrollViewer)Field("contentScroll")).ActualHeight + " list=" + ((ScrollViewer)Field("listScroll")).ActualHeight);
            Assert(((Button)Field("openFilesButton")).Content.ToString() == "Открыть черновик", "Result link uses selected language");
            Render(view, Path.Combine(output, "ru-tall-completed.png"), 1);
            foreach (int lang in new[] { 0, 1, 2 })
            {
                language.SelectedIndex = lang;
                foreach (double scale in new[] { 1.0, 1.25, 1.5, 2.0 })
                {
                    // Equivalent logical work area, rendered at each pixel density.
                    double width = Math.Min(1080, 1920 / scale - 32);
                    double height = Math.Min(1040, 1080 / scale - 80);
                    view.MinWidth = Math.Min(900, width);
                    view.MinHeight = Math.Min(700, height);
                    view.Width = width;
                    view.Height = height;
                    Apply("uploading", Files[1], 50, 100, new[] { Files[0] });
                    Layout(view, width - 16, height - 40);
                    Find<ScrollViewer>((DependencyObject)view.Content).First().ScrollToTop();
                    Layout(view, width - 16, height - 40);
                    AssertActionLayout();
                    if (scale == 1)
                    {
                        Assert(((ScrollViewer)Field("contentScroll")).ScrollableHeight < 1, "Active progress fits without outer scrolling");
                        var bar = (ProgressBar)Field("fileBar");
                        var frame = (Border)Field("progressFrame");
                        var position = bar.TransformToAncestor(frame).Transform(new Point());
                        Assert(position.Y >= 0 && position.Y + bar.ActualHeight <= frame.ActualHeight, "Current-file progress stays fully visible");
                        Assert(frame.Parent != ((ScrollViewer)Field("contentScroll")).Content && Grid.GetRow(frame) == 2, "Progress pinned outside page scrolling");
                    }
                    Render(view, Path.Combine(output, new[] { "de", "ru", "en" }[lang] + "-" + (int)(scale * 100) + ".png"), scale);
                }
                view.Width = 1100;
                view.Height = 1040;
                Apply("ready", "", 0, 0, new string[0]);
                Layout(view, 1064, 1000);
                Assert(((ScrollViewer)Field("contentScroll")).ScrollableHeight < 1, "Ready layout fits in each language");
                Render(view, Path.Combine(output, new[] { "de", "ru", "en" }[lang] + "-ready.png"), 1);
                Apply("uploading", Files[1], 50, 100, new[] { Files[0] });
                Layout(view, 1064, 1000);
                Find<ScrollViewer>((DependencyObject)view.Content).First().ScrollToBottom();
                Layout(view, 1064, 1000);
                Render(view, Path.Combine(output, new[] { "de", "ru", "en" }[lang] + "-progress.png"), 1);
            }
            language.SelectedIndex = 0;
            view.Width = 1100;
            view.Height = 1040;
            Layout(view, 1064, 1000);
            AssertButtonFrames();
            var versionNumber = Find<TextBlock>((DependencyObject)view.Content).Single(item => item.Text == "v1.5.1");
            var versionBlock = (StackPanel)versionNumber.Parent;
            var applicationName = versionBlock.Children.OfType<TextBlock>().Single(item => item.Text == "GitHubSync");
            Assert(versionBlock.HorizontalAlignment == HorizontalAlignment.Center && versionBlock.Orientation == Orientation.Vertical && applicationName.TextAlignment == TextAlignment.Center && versionNumber.TextAlignment == TextAlignment.Center, "App name and separate version line centered in sidebar");
            Assert(versionNumber.FontSize == applicationName.FontSize - 2, "Version is two points smaller than app name");
            var versionPosition = versionNumber.TransformToAncestor(versionBlock).Transform(new Point());
            var namePosition = applicationName.TransformToAncestor(versionBlock).Transform(new Point());
            Assert(versionPosition.Y >= namePosition.Y + applicationName.ActualHeight, "Version sits below app name without overlap");
            Assert(Math.Abs((versionPosition.X + versionNumber.ActualWidth / 2) - (namePosition.X + applicationName.ActualWidth / 2)) < .5, "App name and version share the same horizontal center");
            var pickerFrame = (Border)language.Template.FindName("surface", language);
            Assert(pickerFrame != null && pickerFrame.BorderThickness.Left == 1 && pickerFrame.BorderThickness.Right == 1 && pickerFrame.BorderThickness.Top == 1 && pickerFrame.BorderThickness.Bottom == 1, "Language picker has a complete outline");
            Assert((Border)language.Template.FindName("outline", language) != null, "Language picker redraws outline above its click surface");
            Assert(language.ActualWidth <= ((Grid)view.Content).ColumnDefinitions[0].ActualWidth - 24, "Language picker fits inside the sidebar with both edge margins");
            Assert(Find<ContentPresenter>(language).Any(item => item.HorizontalAlignment == HorizontalAlignment.Center && item.Margin.Left == item.Margin.Right), "Selected language is centered independently of the arrow");
            var chevron = Find<System.Windows.Shapes.Path>(language).First();
            Assert(chevron.VerticalAlignment == VerticalAlignment.Center && chevron.Margin.Bottom == 0, "Language chevron aligns to the label's vertical centerline");
            var scrolling = Find<ScrollViewer>((DependencyObject)view.Content).First();
            scrolling.ScrollToBottom();
            Layout(view, 1064, 1000);
            Render(view, Path.Combine(output, "de-progress.png"), 1);
            ((TextBox)Field("folderPath")).Text = args[2];
            Invoke("LoadFiles");
            language.SelectedIndex = 1;
            Assert(((TextBlock)Field("emptyFilesLabel")).Text == "В этой папке нет файлов.", "Empty-file message follows Russian language selection");
            language.SelectedIndex = 2;
            Assert(((TextBlock)Field("emptyFilesLabel")).Text == "No files in this folder.", "Empty-file message follows English language selection");
            language.SelectedIndex = 0;
            Assert(((TextBlock)Field("emptyFilesLabel")).Text == "Keine Dateien in diesem Ordner.", "Empty-file message follows German language selection");
            var folder = (TextBox)Field("folderPath");
            var fixture = Path.GetDirectoryName(args[2]);
            foreach (string scenario in new[] { "one", "many" })
            {
                folder.Text = Path.Combine(fixture, scenario);
                Invoke("LoadFiles");
                Layout(view, 1064, 1000);
                Assert(((ScrollViewer)Field("contentScroll")).ScrollableHeight < 1, "No outer scrollbar for " + scenario);
                Assert(((List<CheckBox>)Field("fileChecks")).Count == (scenario == "one" ? 1 : 30), "Table contains " + scenario + " fixture");
                if (scenario == "many") {
                    Assert(((ScrollViewer)Field("listScroll")).ScrollableHeight > 0, "Long list scrolls inside table");
                    for (int refresh = 0; refresh < 3; refresh++) {
                        Invoke("LoadFiles");
                        Invoke("UpdateLayoutSpace");
                        Layout(view, view.Width - 16, view.Height - 40);
                        Assert(((ScrollViewer)Field("listScroll")).MaxHeight >= 132, "Repeated folder reload keeps at least three rows");
                    }
                }
                Render(view, Path.Combine(output, scenario + ".png"), 1);
            }
            folder.Text = Path.Combine(fixture, "upload");
            Invoke("LoadFiles");
            Invoke("SetAllFiles", false);
            Assert(((List<CheckBox>)Field("fileChecks")).All(check => check.IsChecked == false), "Clear preserves selection behavior");
            Invoke("SetAllFiles", true);
            Assert((bool)Invoke("SaveConfig"), "Settings save in isolated fixture");
            language.SelectedIndex = 1;
            Assert((int)Invoke("ReadLanguage") == 1, "Language preference round-trip");
            var worker = (System.Diagnostics.ProcessStartInfo)Invoke("CreateWorkerInfo", false, Path.Combine(fixture, "worker-status.json"));
            Assert(!worker.UseShellExecute && worker.CreateNoWindow && worker.WindowStyle == System.Diagnostics.ProcessWindowStyle.Hidden, "GUI worker has no console window");
            Assert(worker.RedirectStandardOutput && worker.RedirectStandardError, "Both worker output streams are drained");
            Assert(worker.Arguments.Contains("-Language ru") && worker.Arguments.Contains("-CheckOnly") && !worker.Arguments.Contains("/c"), "Direct worker receives UI language and check-only mode");
            foreach (bool upload in new[] { false, true })
            {
                Invoke("Launch", "unused.cmd", upload);
                var deadline = DateTime.UtcNow.AddSeconds(20);
                while ((bool)Field("uploadRunning") && DateTime.UtcNow < deadline)
                {
                    Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(delegate { }));
                    System.Threading.Thread.Sleep(20);
                }
                Assert(!(bool)Field("uploadRunning"), "Synthetic worker exits without pipe deadlock");
                Assert((string)Field("phase") == (upload ? "failed" : "checked"), "Synthetic worker result reaches UI");
                if (upload) Assert(((TextBlock)Field("status")).Text.Contains("Искусственная ошибка."), "Error is visible in Russian UI without console");
            }
            Invoke("Launch", "SignIn", false);
            var signInDeadline = DateTime.UtcNow.AddSeconds(20);
            while ((bool)Field("uploadRunning") && DateTime.UtcNow < signInDeadline) {
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(delegate { }));
                System.Threading.Thread.Sleep(20);
            }
            // Drain the exit callback, not just the final snapshot timer.
            for (int tick = 0; tick < 25; tick++) {
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(delegate { }));
                System.Threading.Thread.Sleep(20);
            }
            Assert((string)Field("phase") == "ready", "Successful sign-in stays ready, not failed");
            Assert(((TextBlock)Field("accountText")).Text.Contains("synthetic-account"), "Sign-in worker verifies account in UI");
            // Real WPF modal selections with an offline worker fixture. No GitHub requests.
            view.Show();
            int selectedDialogs = 0;
            var selector = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            selector.Tick += delegate {
                var picker = application.Windows.Cast<Window>().FirstOrDefault(item => item != view && item.IsVisible);
                if (picker == null || picker.Tag != null) return;
                var select = Find<Button>((DependencyObject)picker.Content).Single(item => item.Name == "CatalogSelect");
                picker.Tag = "selected";
                selectedDialogs++;
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate { select.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }));
            };
            selector.Start();
            Invoke("Launch", "ListProjects", false);
            var catalogDeadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < catalogDeadline && (selectedDialogs < 2 || (bool)Field("uploadRunning"))) {
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(delegate { }));
                System.Threading.Thread.Sleep(20);
            }
            selector.Stop();
            Assert(selectedDialogs == 2, "Project selection automatically offers draft picker; dialogs=" + selectedDialogs + "; phase=" + Field("phase") + "; status=" + ((TextBlock)Field("status")).Text);
            Assert(((TextBox)Field("repository")).Text == "example/catalog-project" && ((TextBox)Field("releaseTag")).Text == "v-catalog-draft", "Picker values reach real target fields");
            Assert((string)Field("phase") == "ready" && !(bool)Field("uploadRunning"), "Read-only catalog completion restores usable UI");
            int createDialogs = 0;
            var creator = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            creator.Tick += delegate {
                var picker = application.Windows.Cast<Window>().FirstOrDefault(item => item != view && item.IsVisible);
                if (picker == null || picker.Tag != null) return;
                picker.Tag = "selected";
                var controls = Find<Button>((DependencyObject)picker.Content).ToArray();
                var create = controls.FirstOrDefault(item => item.Name == "CatalogCreate");
                if (create == null) {
                    var inputs = Find<TextBox>((DependencyObject)picker.Content).ToArray();
                    inputs.Single(item => item.Name == "DraftVersion").Text = "v-new-draft";
                    inputs.Single(item => item.Name == "DraftTitle").Text = "Тестовый черновик";
                    create = controls.Single(item => item.Name == "DraftCreate");
                } else {
                    Assert(!controls.Single(item => item.Name == "CatalogSelect").IsEnabled, "Empty catalog cannot select a nonexistent draft");
                }
                createDialogs++;
                var action = create;
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate { action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }));
            };
            creator.Start();
            Invoke("ShowCatalog", "drafts", new Dictionary<string, object> { { "choices", new object[0] } });
            var createDeadline = DateTime.UtcNow.AddSeconds(20);
            while ((bool)Field("uploadRunning") && DateTime.UtcNow < createDeadline) {
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(delegate { }));
                System.Threading.Thread.Sleep(20);
            }
            creator.Stop();
            Assert(createDialogs == 2, "Empty catalog offers create form with explicit submit");
            Assert(((TextBox)Field("releaseTag")).Text == "v-new-draft" && (string)Field("phase") == "ready", "Confirmed new draft is selected automatically");
            Assert(Field("draftRequestPath") == null, "Temporary creation request is removed after completion");
            foreach (int lang in new[] { 0, 1, 2 }) {
                language.SelectedIndex = lang;
                var cancel = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                cancel.Tick += delegate {
                    var popup = application.Windows.Cast<Window>().FirstOrDefault(item => item != view && item.IsVisible);
                    if (popup == null) return;
                    cancel.Stop();
                    var button = Find<Button>((DependencyObject)popup.Content).Single(item => item.Name == "DraftCreate");
                    var formatted = new FormattedText(Convert.ToString(button.Content), System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(button.FontFamily, button.FontStyle, button.FontWeight, button.FontStretch), button.FontSize, Brushes.Black);
                    Assert(button.ActualWidth >= formatted.Width + 48, "Create action label fits with padding in each language");
                    foreach (double scale in new[] { 1.0, 1.25, 1.5, 2.0 }) Render(popup, Path.Combine(output, new[] { "de", "ru", "en" }[lang] + "-create-" + (int)(scale * 100) + ".png"), scale);
                    popup.Close();
                };
                cancel.Start();
                Invoke("ShowCreateDraftDialog");
                cancel.Stop();
                Assert(!(bool)Field("uploadRunning") && Field("draftRequestPath") == null && ((TextBox)Field("releaseTag")).Text == "v-new-draft", "Cancelling creation does not start a worker or change target");
            }
            int projectDialogs = 0;
            var projectCreator = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            projectCreator.Tick += delegate {
                var popup = application.Windows.Cast<Window>().FirstOrDefault(item => item != view && item.IsVisible);
                if (popup == null || popup.Tag != null) return;
                popup.Tag = "selected";
                var buttons = Find<Button>((DependencyObject)popup.Content).ToArray();
                var action = buttons.FirstOrDefault(item => item.Name == "CatalogCreate");
                if (action == null) {
                    Find<TextBox>((DependencyObject)popup.Content).Single(item => item.Name == "ProjectName").Text = "New-Project";
                    Assert(Find<CheckBox>((DependencyObject)popup.Content).Single(item => item.Name == "ProjectPrivate").IsChecked == true, "Project is private by default");
                    action = buttons.Single(item => item.Name == "ProjectCreate");
                }
                projectDialogs++;
                var selected = action;
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate { selected.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }));
            };
            projectCreator.Start();
            Invoke("ShowCatalog", "projects", new Dictionary<string, object> { { "choices", new object[0] } });
            var projectDeadline = DateTime.UtcNow.AddSeconds(20);
            while ((bool)Field("uploadRunning") && DateTime.UtcNow < projectDeadline) {
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(delegate { }));
                System.Threading.Thread.Sleep(20);
            }
            projectCreator.Stop();
            Assert(projectDialogs == 2, "Empty projects offer creation dialog");
            Assert(((TextBox)Field("repository")).Text == "synthetic-account/New-Project" && ((TextBox)Field("releaseTag")).Text == "", "Created project selected and old release cleared");
            Assert(!(bool)Field("uploadRunning") && Field("draftRequestPath") == null, "Project creation finishes and cleans temporary request");
            foreach (int lang in new[] { 0, 1, 2 }) {
                language.SelectedIndex = lang;
                var cancel = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                cancel.Tick += delegate {
                    var popup = application.Windows.Cast<Window>().FirstOrDefault(item => item != view && item.IsVisible);
                    if (popup == null) return;
                    cancel.Stop();
                    foreach (double scale in new[] { 1.0, 1.25, 1.5, 2.0 }) Render(popup, Path.Combine(output, new[] { "de", "ru", "en" }[lang] + "-project-" + (int)(scale * 100) + ".png"), scale);
                    popup.Close();
                };
                cancel.Start(); Invoke("ShowCreateProjectDialog"); cancel.Stop();
                Assert(!(bool)Field("uploadRunning") && Field("draftRequestPath") == null, "Cancelled project form does not start a worker");
            }
            foreach (int lang in new[] { 0, 1, 2 }) {
                language.SelectedIndex = lang;
                var cancel = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                cancel.Tick += delegate {
                    var popup = application.Windows.Cast<Window>().FirstOrDefault(item => item != view && item.IsVisible);
                    if (popup == null) return;
                    cancel.Stop();
                    var buttons = Find<Button>((DependencyObject)popup.Content).ToArray();
                    Assert(buttons.Length == 2 && buttons.Any(item => item.Name == "ConfirmCancel") && buttons.Any(item => item.Name == "ConfirmSend"), "Localized WPF confirm/cancel actions");
                    Render(popup, Path.Combine(output, new[] { "de", "ru", "en" }[lang] + "-confirm.png"), 1);
                    popup.Close();
                };
                cancel.Start();
                Assert(!(bool)Invoke("ConfirmUpload", "example/repo · v-test\n" + new[] { "2 Dateien", "2 файла", "2 files" }[lang]), "Closing confirmation does not approve upload");
                cancel.Stop();
            }
            var modes=(ComboBox)Field("modePicker");
            Assert(modes.Items.Count==2 && !((CheckBox)Field("publishCheck")).IsChecked.Value, "Legacy config retains draft-only behavior");
            var originalReleaseFiles=((List<CheckBox>)Field("fileChecks")).Where(c=>c.IsChecked==true).Select(c=>(string)c.Tag).ToArray();
            ((Dictionary<string,object>)Field("config"))["ProjectSourceDirectory"]=Path.Combine(Path.GetDirectoryName(args[2]),"code");
            modes.SelectedIndex=0;
            WaitFiles();
            var codeChecks=(List<CheckBox>)Field("fileChecks");
            Assert(codeChecks.Count==4 && !codeChecks.Any(c=>((string)c.Tag).Contains(".git/")), "Recursive scan excludes .git");
            Assert(codeChecks.Single(c=>(string)c.Tag==".env").IsChecked==false, "Possible secret starts unchecked");
            Assert(codeChecks.Any(c=>(string)c.Tag=="docs/инструкция.txt" && c.IsChecked==true), "Nested Unicode selection preserved");
            Invoke("SetAllFiles",true);
            Assert(codeChecks.Single(c=>(string)c.Tag==".env").IsChecked==false, "Select all does not silently select secrets");
            Assert(((FrameworkElement)Field("releaseTargetRow")).Visibility==Visibility.Collapsed && ((FrameworkElement)Field("codeTargetRow")).Visibility==Visibility.Visible, "Mode-specific target controls");
            Assert(((CheckBox)Field("publishCheck")).IsEnabled && ((StackPanel)Field("publishRow")).Visibility==Visibility.Collapsed, "Publication absent from Code mode");
            foreach(int lang in new[]{0,1,2}) {
                language.SelectedIndex=lang;
                foreach(double scale in new[]{1.0,1.25,1.5,2.0}) { Layout(view,1064,1000); AssertActionLayout(); Render(view,Path.Combine(output,new[]{"de","ru","en"}[lang]+"-code-"+(int)(scale*100)+".png"),scale); }
            }
            language.SelectedIndex=1;
            ((TextBox)Field("repository")).Text="example/code-project";
            type.GetField("accountLogin",Private).SetValue(window,"synthetic-account");
            bool sawPreview=false;
            var codeTimer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(70) };
            codeTimer.Tick+=delegate { var popup=application.Windows.Cast<Window>().FirstOrDefault(w=>w!=view && w.IsVisible); if(popup==null) return; var send=Find<Button>((DependencyObject)popup.Content).FirstOrDefault(b=>b.Name=="CodePlanSend"); if(send==null)return; codeTimer.Stop(); sawPreview=true; Assert(send.IsEnabled,"Code write requires explicit review approval"); Assert(Find<TextBlock>((DependencyObject)popup.Content).Any(t=>t.Text.Contains("Обновится")),"Replacement shown in review"); Render(popup,Path.Combine(output,"ru-code-review.png"),1); send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); };
            codeTimer.Start(); Invoke("StartUpload");
            var codeDeadline=DateTime.UtcNow.AddSeconds(20);
            while((bool)Field("uploadRunning") && DateTime.UtcNow<codeDeadline) { Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background,new Action(delegate{})); System.Threading.Thread.Sleep(20); }
            codeTimer.Stop();
            Assert(sawPreview && (string)Field("phase")=="completed","Code preview -> explicit confirmation -> background upload");
            Assert(Field("uploaderRequestPath")==null,"Temporary Code request cleaned up");
            Assert(((Button)Field("openFilesButton")).Visibility==Visibility.Visible && ((Button)Field("copyResultButton")).Visibility==Visibility.Collapsed,"Code result opens project, not release");
            Assert(((IEnumerable)((Dictionary<string,object>)Field("config"))["Files"]).Cast<string>().SequenceEqual(originalReleaseFiles),"Code settings preserve release selection");
            Layout(view,1064,1000); Render(view,Path.Combine(output,"ru-code-completed.png"),1);
            bool readOnlyReview=false; var reviewTimer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(70)};
            reviewTimer.Tick+=delegate {var popup=application.Windows.Cast<Window>().FirstOrDefault(w=>w!=view && w.IsVisible); if(popup==null)return; var send=Find<Button>((DependencyObject)popup.Content).FirstOrDefault(b=>b.Name=="CodePlanSend"); if(send==null)return; reviewTimer.Stop(); readOnlyReview=true; Assert(!send.IsEnabled,"Check review cannot trigger a write"); popup.Close();};
            type.GetField("preparingCodeUpload",Private).SetValue(window,false); Invoke("WriteUploaderRequest","preview",null); reviewTimer.Start(); Invoke("Launch","CodePreview",false);
            var reviewDeadline=DateTime.UtcNow.AddSeconds(20); while((bool)Field("uploadRunning") && DateTime.UtcNow<reviewDeadline) {Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background,new Action(delegate{})); System.Threading.Thread.Sleep(20);}
            reviewTimer.Stop(); Assert(readOnlyReview && (string)Field("phase")=="ready" && Field("uploaderRequestPath")==null,"Read-only review ends without upload and cleans request");
            modes.SelectedIndex=1;
            WaitFiles();
            ((TextBox)Field("repository")).Text="example/release-test"; ((TextBox)Field("releaseTag")).Text="fixture";
            var draftSnapshot=new Dictionary<string,object>{{"state","completed"},{"releaseUrl","https://github.com/example/release-test/releases/tag/fixture"},{"account","synthetic-account"},{"plan",new Dictionary<string,object>{{"repository","example/release-test"},{"tag","fixture"},{"releaseId",1}}},{"releaseAssets",new object[]{new Dictionary<string,object>{{"id",4},{"size",20},{"state","uploaded"}}}}};
            type.GetMethod("ApplyProgress",Private).Invoke(window,new object[]{draftSnapshot});
            Assert(((Button)Field("publishLaterButton")).Visibility==Visibility.Visible,"Draft can be published separately");
            var publishTimer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(70) };
            publishTimer.Tick+=delegate { var popup=application.Windows.Cast<Window>().FirstOrDefault(w=>w!=view && w.IsVisible); if(popup==null)return; var send=Find<Button>((DependencyObject)popup.Content).FirstOrDefault(b=>b.Name=="ConfirmSend"); if(send==null)return; publishTimer.Stop(); Assert(send.Content.ToString()=="Опубликовать","Publication action is explicitly named"); send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); };
            publishTimer.Start(); Invoke("PublishCurrentDraft");
            var publishDeadline=DateTime.UtcNow.AddSeconds(20);
            while((bool)Field("uploadRunning") && DateTime.UtcNow<publishDeadline) { Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background,new Action(delegate{})); System.Threading.Thread.Sleep(20); }
            publishTimer.Stop();
            Assert((string)Field("phase")=="completed" && ((Button)Field("copyResultButton")).Visibility==Visibility.Visible,"Published result exposes link copy");
            Assert(((Button)Field("publishLaterButton")).Visibility==Visibility.Collapsed && ((Dictionary<string,string>)Field("resultDownloads")).Count==1,"Published release cannot be published twice; asset link returned");
            Assert(Field("uploaderRequestPath")==null,"Publication request cleaned up");
            var linksTimer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(70)};
            linksTimer.Tick+=delegate {var popup=application.Windows.Cast<Window>().FirstOrDefault(w=>w!=view && w.IsVisible); if(popup==null)return; linksTimer.Stop(); Assert(Find<Button>((DependencyObject)popup.Content).Count(b=>b.Name=="CopyAssetLink")==1,"Every published attachment has a copy action"); Render(popup,Path.Combine(output,"ru-file-links.png"),1); popup.Close();};
            linksTimer.Start(); Invoke("ShowDownloadLinks"); linksTimer.Stop();
            ((CheckBox)Field("publishCheck")).IsChecked=true; Assert(((Button)Field("startButton")).Content.ToString()=="Загрузить и опубликовать","Publish checkbox names combined action");
            ((CheckBox)Field("publishCheck")).IsChecked=false; Assert(((Button)Field("startButton")).Content.ToString()=="Сохранить как черновик","Draft checkbox names non-public action");
            Layout(view,1064,1000); Render(view,Path.Combine(output,"ru-published.png"),1);
            Invoke("ResetResult");
            // Regression: the original mode switch recursively scanned Downloads
            // and created/measured thousands of rows synchronously on the UI thread.
            var settings = (Dictionary<string,object>)Field("config");
            settings["ProjectSourceDirectory"] = Path.Combine(Path.GetDirectoryName(args[2]),"large");
            settings["ProjectFiles"] = new[] { "nested/file-00001.txt" };
            var switchClock = System.Diagnostics.Stopwatch.StartNew();
            modes.SelectedIndex=0;
            switchClock.Stop();
            Assert(switchClock.ElapsedMilliseconds < 500,"Large-folder mode switch returns immediately: " + switchClock.ElapsedMilliseconds + "ms");
            Assert((bool)Field("filesLoading") && !((Button)Field("startButton")).IsEnabled,"Partial list cannot be uploaded");
            Assert(!(bool)Invoke("SaveConfig"),"Partial list cannot replace persisted selection");
            // Cancel even after row construction has begun, preserving saved selection.
            var batchDeadline = DateTime.UtcNow.AddSeconds(10);
            while (((List<CheckBox>)Field("fileChecks")).Count == 0 && DateTime.UtcNow < batchDeadline) { Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background,new Action(delegate{})); System.Threading.Thread.Sleep(5); }
            Assert(((List<CheckBox>)Field("fileChecks")).Count > 0 && (bool)Field("filesLoading"),"Large scan is assembled in cancellable UI batches");
            modes.SelectedIndex=1; WaitFiles();
            Assert(((IEnumerable)settings["ProjectFiles"]).Cast<string>().SequenceEqual(new[]{"nested/file-00001.txt"}),"Switch during scan preserves saved Code selection");
            Assert(((List<CheckBox>)Field("fileChecks")).Count==3,"Cancelled scan cannot overwrite new mode list");
            modes.SelectedIndex=0;
            int heartbeats=0; long maxHeartbeatGap=0; var heartbeatClock=System.Diagnostics.Stopwatch.StartNew();
            var heartbeat = new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(20)};
            heartbeat.Tick += delegate { maxHeartbeatGap=Math.Max(maxHeartbeatGap,heartbeatClock.ElapsedMilliseconds); heartbeatClock.Restart(); heartbeats++; };
            heartbeat.Start(); WaitFiles(); heartbeat.Stop();
            Assert(((List<CheckBox>)Field("fileChecks")).Count==6000,"Large recursive list is complete, not silently truncated");
            Assert((string)Field("phase")=="ready" && Field("progressPath")==null,"Previous worker snapshot cannot repaint the new mode");
            Assert(heartbeats>5 && maxHeartbeatGap<1000,"Dispatcher remains responsive during large list: "+heartbeats+" ticks, maximum gap "+maxHeartbeatGap+"ms");
            Console.WriteLine("6000-file scan: " + heartbeats + " UI heartbeats; maximum timer gap " + maxHeartbeatGap + "ms.");
            Layout(view,1064,1000);
            var largeList=(ListBox)Field("fileList");
            Assert(largeList.ItemContainerGenerator.ContainerFromIndex(5999)==null,"Off-screen rows are not realized during layout");
            switchClock.Restart(); Invoke("SetAllFiles",true); switchClock.Stop();
            Assert(((List<CheckBox>)Field("fileChecks")).All(c=>c.IsChecked==true) && switchClock.ElapsedMilliseconds<2000,"Bulk selection runs once, not one table update per file");
            Assert(((TextBlock)Field("selectedCount")).Text.Contains("6000 из 6000"),"Large-list selection counter updates immediately");
            Layout(view,1064,1000);
            Render(view,Path.Combine(output,"ru-large-list.png"),1);
            ((ScrollViewer)Field("listScroll")).ScrollToEnd(); Layout(view,1064,1000);
            Assert(largeList.ItemContainerGenerator.ContainerFromIndex(5999)!=null,"Last row remains reachable by internal scrolling");
            switchClock.Restart(); modes.SelectedIndex=1; switchClock.Stop();
            Assert(switchClock.ElapsedMilliseconds<500,"Switch away from completed large list remains responsive: "+switchClock.ElapsedMilliseconds+"ms");
            Console.WriteLine("Mode switch away from 6000 rows: " + switchClock.ElapsedMilliseconds + "ms.");
            WaitFiles();
            // A failed scan leaves no partial, uploadable table and is recoverable.
            var missingFolder=(TextBox)Field("folderPath"); string previousFolder=missingFolder.Text;
            missingFolder.Text=Path.Combine(Path.GetDirectoryName(args[2]),"missing-fixture"); Invoke("LoadFiles");
            Assert(((List<CheckBox>)Field("fileChecks")).Count==0 && (string)Field("emptyFilesState")=="missing","Missing folder is handled after background scan");
            missingFolder.Text=previousFolder; Invoke("LoadFiles");
            view.Hide();
            Layout(view, 1084, 780);
            Assert(((ScrollViewer)Field("contentScroll")).ScrollableHeight < 1, "Signed-in idle page fits without outer scroll");
            ((Expander)Field("advanced")).IsExpanded = true;
            Layout(view, 884, 660);
            AssertActionLayout();
            Assert(((ComboBox)Field("language")).Items.Count == 3, "Three languages");
            ((Expander)Field("advanced")).IsExpanded=false;
            ((TextBox)Field("repository")).Text="example/release-test";
            Invoke("ChangeDirection",true); WaitSyncJob();
            Assert((bool)Field("downloadMode") && ((List<CheckBox>)Field("fileChecks")).Count==1,"Download loads remote catalog, not local folder contents");
            Assert(((StackPanel)Field("publishRow")).Visibility==Visibility.Collapsed,"Download cannot publish");
            ((TextBox)Field("folderPath")).Text=args[2];
            for(int languageIndex=0;languageIndex<3;languageIndex++) {
                ((ComboBox)Field("language")).SelectedIndex=languageIndex;
                for(int mode=0;mode<2;mode++) {
                    modes.SelectedIndex=mode;WaitSyncJob(); Layout(view,1064,1000);
                    Assert(((Button)Field("startButton")).Content.ToString().Contains(languageIndex==1 ? "Скачать" : languageIndex==2 ? "Download" : "herunterladen"),"Primary action identifies downloading");
                    Assert(((ScrollViewer)Field("contentScroll")).ScrollableHeight<1,"Download source/list fit without outer scroll");
                    Assert(((TextBlock)Field("accountText")).TextAlignment==TextAlignment.Center,"Account remains centered in download mode");
                    Render(view,Path.Combine(output,new[]{"de","ru","en"}[languageIndex]+"-download-"+mode+".png"),1);
                }
            }
            ((ComboBox)Field("language")).SelectedIndex=1;((TextBox)Field("folderPath")).Text=args[2];
            Invoke("SetAllFiles",true);
            bool cancelled=false;var cancelDownloadTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(60)};
            cancelDownloadTimer.Tick+=delegate {var popup=application.Windows.Cast<Window>().FirstOrDefault(w=>w!=view && w.IsVisible);if(popup==null)return;var button=Find<Button>((DependencyObject)popup.Content).FirstOrDefault(b=>b.Name=="DownloadCancel");if(button==null)return;cancelDownloadTimer.Stop();cancelled=true;button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));};
            cancelDownloadTimer.Start();Invoke("StartDownload");WaitSyncJob();cancelDownloadTimer.Stop();
            Assert(cancelled && ((TextBlock)Field("status")).Text.Contains("Отменено"),"Cancel preview never starts download");
            Assert(!File.Exists(Path.Combine(args[2],"docs","инструкция.txt")),"Cancelled download writes no target file");
            var downloadSnapshot=new Dictionary<string,object>{{"state","completed"},{"direction","download"},{"mode","release"},{"localDirectory",args[2]},{"verification","size"},{"filesTotal",1},{"filesCompleted",1},{"confirmedFiles",new[]{"docs/инструкция.txt"}}};
            type.GetMethod("ApplyProgress",Private).Invoke(window,new object[]{downloadSnapshot});
            Assert(((Button)Field("openFilesButton")).Visibility==Visibility.Visible && ((TextBlock)Field("status")).Text.Contains("не предоставил"),"Local result and honest size-only verification");
            Assert(((Button)Field("publishLaterButton")).Visibility==Visibility.Collapsed,"No release mutation actions after download");
            type.GetField("monitorFingerprint",Private).SetValue(window,"old-synthetic-revision");
            Invoke("CheckRemoteUpdates",true);
            var monitorDeadline=DateTime.UtcNow.AddSeconds(20);
            while((bool)Field("monitorBusy") && DateTime.UtcNow<monitorDeadline){Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background,new Action(delegate{}));System.Threading.Thread.Sleep(20);}
            Assert(!(bool)Field("monitorBusy"),"Manual observer completes in background");
            Assert(((TextBlock)Field("updatesText")).Text.Contains("изменения") && ((TextBlock)Field("updatesText")).Visibility==Visibility.Visible,"Remote changes show pinned notification");
            Assert(!(bool)Field("uploadRunning") && !File.Exists(Path.Combine(args[2],"docs","инструкция.txt")),"Observer never starts a transfer");
            Assert(!File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"private-state","pending-download.json")),"Observation creates no approved transfer task");
            Invoke("SaveSyncPreferences");
            var syncSaved=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"config.json")));
            Assert((string)syncSaved["Repository"]=="example/release-test" && (string)syncSaved["Direction"]=="download","Download preferences retain repository and direction across restart");
            var savedResumePlan=new Dictionary<string,object>((Dictionary<string,object>)Field("remoteCatalog"));savedResumePlan["directory"]=args[2];
            Invoke("ChangeDirection",false);WaitFiles();((TextBox)Field("repository")).Text="example/other-project";
            string pendingFixture=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"private-state","pending-download.json");
            File.WriteAllText(pendingFixture,new JavaScriptSerializer().Serialize(new Dictionary<string,object>{{"plan",savedResumePlan}}));
            Invoke("RestorePendingDownload");WaitFiles();
            Assert((bool)Field("downloadMode") && ((TextBox)Field("repository")).Text=="example/release-test","Resume restores the approved project, not the previously displayed one");
            Assert(!(bool)Field("uploadRunning") && ((List<CheckBox>)Field("fileChecks")).Count==1 && ((List<CheckBox>)Field("fileChecks"))[0].IsChecked==true,"Resume restores selection without launching a catalog or download");
            File.Delete(pendingFixture);
            Invoke("ChangeDirection",false);WaitFiles();
            view.Show();type.GetMethod("InitializeTray",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(window,null);
            Assert(Field("tray")!=null,"Tray is initialized with embedded icon: "+Field("trayFailure"));
            Assert(((IList)Field("trayFrames")).Count==13 && ((IList)Field("windowFrames")).Count==12,"Static icon and cached animation frames");
            type.GetField("uploadRunning",Private).SetValue(window,true);type.GetField("checking",Private).SetValue(window,false);Invoke("AnimateTray");var animated=view.Icon;Invoke("AnimateTray");Assert(view.Icon!=animated,"Window icon advances with tray animation");
            type.GetField("uploadRunning",Private).SetValue(window,false);Invoke("AnimateTray");
            bool exitCancelled=false;type.GetField("uploadRunning",Private).SetValue(window,true);
            var exitCancelTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(50)};
            exitCancelTimer.Tick+=delegate{var popup=application.Windows.Cast<Window>().FirstOrDefault(w=>w!=view&&w.IsVisible);if(popup==null)return;var cancel=Find<Button>((DependencyObject)popup.Content).FirstOrDefault(b=>b.Name=="ExitCancel");if(cancel==null)return;exitCancelTimer.Stop();exitCancelled=cancel.Content.ToString()=="Продолжить работу";cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));};
            exitCancelTimer.Start();Invoke("RequestExit");exitCancelTimer.Stop();type.GetField("uploadRunning",Private).SetValue(window,false);
            Assert(exitCancelled && !((bool)Field("allowWindowClose")),"Exit cancellation uses localized themed controls and keeps process alive");
            view.Close();Assert(!view.IsVisible && !type.GetField("allowWindowClose",Private).GetValue(window).Equals(true),"Close hides window instead of ending process");
            type.GetMethod("RestoreWindow",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(window,null);Assert(view.IsVisible,"Tray restores the same window");
            type.GetField("allowWindowClose",Private).SetValue(window,true);
            string fixtureConfig=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"config.json"); string savedFixtureConfig=File.ReadAllText(fixtureConfig);
            try {File.WriteAllText(fixtureConfig,new JavaScriptSerializer().Serialize(new Dictionary<string,object>{{"Repository","OWNER/REPOSITORY"},{"UploadMode","release"},{"PublishAfterUpload",true},{"SourceDirectory","upload"},{"Files",new string[0]}})); var fresh=(Window)Activator.CreateInstance(type,true); Assert(((CheckBox)type.GetField("publishCheck",Private).GetValue(fresh)).IsChecked==true,"Fresh configuration enables publication, still requiring confirmation"); fresh.Close();} finally {File.WriteAllText(fixtureConfig,savedFixtureConfig);}
            view.Close();
            Console.WriteLine("UI review passed: " + checks + " assertions, 12 language/density renders and fixture renders.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static object Field(string name)
    {
        return type.GetField(name, Private).GetValue(window);
    }
    private static void WaitSyncJob()
    {
        var deadline=DateTime.UtcNow.AddSeconds(25);
        while(((bool)Field("uploadRunning") || (bool)Field("filesLoading")) && DateTime.UtcNow<deadline) {Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background,new Action(delegate{}));System.Threading.Thread.Sleep(20);}
        Assert(!(bool)Field("uploadRunning") && !(bool)Field("filesLoading"),"Remote worker and batched scan completed");
    }

    private static object Field(string name, string key)
    {
        return ((Dictionary<string, TextBlock>)Field(name))[key];
    }

    private static object Invoke(string method, params object[] args)
    {
        var result = type.GetMethod(method, Private).Invoke(window, args);
        if (method == "LoadFiles") WaitFiles();
        return result;
    }

    private static void WaitFiles()
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while ((bool)Field("filesLoading") && DateTime.UtcNow < deadline) {
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background,new Action(delegate{}));
            System.Threading.Thread.Sleep(5);
        }
        Assert(!(bool)Field("filesLoading"), "Background file scan completes");
    }

    private static void Apply(string phase, string file, long sent, long completed, string[] confirmed)
    {
        var snapshot = new Dictionary<string, object> {
            { "state", phase }, { "file", file }, { "fileBytes", 100L }, { "fileSent", sent },
            { "totalBytes", 300L }, { "completedBytes", completed },
            { "filesTotal", 3 }, { "filesCompleted", confirmed.Length }, { "confirmedFiles", confirmed },
            { "retryAfterSeconds", 10 }, { "message", "" }
        };
        var statusFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "synthetic-progress.json");
        File.WriteAllText(statusFile, new JavaScriptSerializer().Serialize(snapshot), new System.Text.UTF8Encoding(false));
        type.GetField("progressPath", Private).SetValue(window, statusFile);
        Invoke("ReadProgress");
    }

    private static void Layout(Window view, double width, double height)
    {
        var content = (FrameworkElement)view.Content;
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Loaded, new Action(delegate { }));
        Invoke("UpdateLayoutSpace");
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();
    }

    private static void AssertActionLayout()
    {
        var buttons = new[] { (Button)Field("startButton") };
        foreach (var button in buttons)
        {
            var formatted = new FormattedText(Convert.ToString(button.Content), System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(button.FontFamily, button.FontStyle, button.FontWeight, button.FontStretch), button.FontSize, Brushes.Black);
            Assert(button.ActualWidth >= formatted.Width + 48, "Button text has side padding: " + button.Content);
            Assert(button.ActualHeight >= 48, "Button has agreed height");
        }
        Assert(Grid.GetColumnSpan(buttons[0]) == 3, "One full-width primary upload action");
        Assert(((Expander)Field("advanced")).Content != null && Grid.GetColumnSpan((Button)Field("saveButton")) == 1, "Technical actions retained separately from primary action");
        foreach (var button in new[] { (Button)Field("guideButton"), (Button)Field("browseButton"), (Button)Field("selectAllButton"), (Button)Field("clearButton"), (Button)Field("projectsButton"), (Button)Field("draftsButton") })
        {
            if(button==(Button)Field("draftsButton") && ((ComboBox)Field("modePicker")).SelectedIndex==0) continue;
            var formatted = new FormattedText(Convert.ToString(button.Content), System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(button.FontFamily, button.FontStyle, button.FontWeight, button.FontStretch), button.FontSize, Brushes.Black);
            Assert(button.ActualWidth >= formatted.Width + button.Padding.Left + button.Padding.Right, "Auxiliary button label fits: " + button.Content);
        }
    }

    private static void AssertButtonFrames()
    {
        foreach (var button in new[] { (Button)Field("guideButton"), (Button)Field("browseButton"), (Button)Field("selectAllButton"), (Button)Field("clearButton"), (Button)Field("saveButton"), (Button)Field("checkButton"), (Button)Field("startButton") })
        {
            button.ApplyTemplate();
            var surface = (Border)button.Template.FindName("surface", button);
            Assert(surface != null, "Button has shared framed surface: " + button.Content);
            Assert(surface.BorderThickness.Left > 0 && surface.BorderThickness.Right > 0 && surface.BorderThickness.Top > 0 && surface.BorderThickness.Bottom > 0, "Button border visible on all edges: " + button.Content);
            Assert(surface.CornerRadius.TopLeft == surface.CornerRadius.TopRight && surface.CornerRadius.TopLeft == surface.CornerRadius.BottomLeft && surface.CornerRadius.TopLeft == surface.CornerRadius.BottomRight, "Button corners use a consistent radius: " + button.Content);
            Assert(button.SnapsToDevicePixels && button.UseLayoutRounding, "Button uses pixel-aligned rendering: " + button.Content);
        }
    }

    private static void Render(Window view, string path, double scale)
    {
        var sourcePath = (TextBox)Field("folderPath");
        string actualPath = sourcePath.Text;
        object actualTooltip = sourcePath.ToolTip;
        // Public renders must not disclose the Windows profile/temporary path.
        if (view == (Window)window) { sourcePath.Text = "C:\\Projects\\Demo"; sourcePath.ToolTip = sourcePath.Text; }
        try {
        var visual = (FrameworkElement)view.Content;
        visual.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth * scale), (int)Math.Ceiling(visual.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(path)) encoder.Save(stream);
        } finally { sourcePath.Text = actualPath; sourcePath.ToolTip = actualTooltip; }
    }

    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T) yield return (T)child;
            foreach (var descendant in Find<T>(child)) yield return descendant;
        }
    }

    private static void Assert(bool condition, string message)
    {
        checks++;
        if (!condition) throw new InvalidOperationException(message);
    }
}
