using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace NovelpiaDownloader;

public partial class MainPage : ContentPage
{
    readonly MobileDownloader service = new();
    readonly ObservableCollection<DownloadJob> queue = new();

    public MainPage()
    {
        InitializeComponent();
        FormatPicker.SelectedIndex = 0;
        BonusPicker.SelectedIndex = 0;
        QueueView.ItemsSource = queue;
        service.Log += (_, s) => MainThread.BeginInvokeOnMainThread(() => AppendLog(s));
        LoadPrefs();
    }

    void LoadPrefs()
    {
        EmailEntry.Text = Preferences.Get("email", "");
        LoginKeyEntry.Text = Preferences.Get("loginkey", "");
        NoticeCheck.IsChecked = Preferences.Get("notice", false);
        RemoveBlankCheck.IsChecked = Preferences.Get("blank", false);
        KeepHtmlCheck.IsChecked = Preferences.Get("html", false);
        ImageCheck.IsChecked = Preferences.Get("image", true);
        CompressCheck.IsChecked = Preferences.Get("compress", true);
        StopErrorCheck.IsChecked = Preferences.Get("stoperr", false);
        NovelNoNameCheck.IsChecked = Preferences.Get("novelno", true);
        RangeNameCheck.IsChecked = Preferences.Get("range", true);
        VerticalCheck.IsChecked = Preferences.Get("vertical", false);
        GothicCheck.IsChecked = Preferences.Get("gothic", false);
        RetryEntry.Text = Preferences.Get("retry", "2");
        ThreadEntry.Text = Preferences.Get("thread", "3");
        IntervalEntry.Text = Preferences.Get("interval", "0.4");
        FontMapEntry.Text = Preferences.Get("fontmap", "");
        if (!string.IsNullOrWhiteSpace(LoginKeyEntry.Text))
            service.SetLoginKey(LoginKeyEntry.Text);
    }

    void SavePrefs()
    {
        Preferences.Set("email", EmailEntry.Text ?? "");
        Preferences.Set("loginkey", service.LoginKey ?? "");
        Preferences.Set("notice", NoticeCheck.IsChecked);
        Preferences.Set("blank", RemoveBlankCheck.IsChecked);
        Preferences.Set("html", KeepHtmlCheck.IsChecked);
        Preferences.Set("image", ImageCheck.IsChecked);
        Preferences.Set("compress", CompressCheck.IsChecked);
        Preferences.Set("stoperr", StopErrorCheck.IsChecked);
        Preferences.Set("novelno", NovelNoNameCheck.IsChecked);
        Preferences.Set("range", RangeNameCheck.IsChecked);
        Preferences.Set("vertical", VerticalCheck.IsChecked);
        Preferences.Set("gothic", GothicCheck.IsChecked);
        Preferences.Set("retry", RetryEntry.Text ?? "2");
        Preferences.Set("thread", ThreadEntry.Text ?? "3");
        Preferences.Set("interval", IntervalEntry.Text ?? "0.4");
        Preferences.Set("fontmap", FontMapEntry.Text ?? "");
    }

    async void LoginClicked(object sender, EventArgs e)
    {
        try
        {
            LoginStatus.Text = "로그인 중...";
            var ok = await service.LoginAsync(EmailEntry.Text ?? "", PasswordEntry.Text ?? "");
            LoginStatus.Text = ok ? "로그인 성공" : "로그인 실패";
            LoginKeyEntry.Text = service.LoginKey;
            SavePrefs();
        }
        catch (Exception ex) { LoginStatus.Text = "로그인 오류"; AppendLog(ex.Message); }
    }

    void LoginKeyClicked(object sender, EventArgs e)
    {
        service.SetLoginKey(LoginKeyEntry.Text ?? "");
        LoginStatus.Text = string.IsNullOrWhiteSpace(service.LoginKey) ? "LOGINKEY 없음" : "LOGINKEY 적용됨";
        SavePrefs();
    }

    async void PickFontMapClicked(object sender, EventArgs e)
    {
        try
        {
            var f = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "폰트 매핑 JSON 선택" });
            if (f != null) { FontMapEntry.Text = f.FullPath; SavePrefs(); }
        }
        catch (Exception ex) { AppendLog("파일 선택 오류: " + ex.Message); }
    }

    DownloadJob? ReadJob()
    {
        var m = Regex.Match(NovelEntry.Text ?? "", @"\d+");
        if (!m.Success) { AppendLog("소설 번호 또는 URL을 입력하세요."); return null; }
        int? fromNo = int.TryParse(FromEntry.Text, out var f) ? f : null;
        int? toNo = int.TryParse(ToEntry.Text, out var t) ? t : null;
        int retry = int.TryParse(RetryEntry.Text, out var r) ? Math.Max(0, r) : 2;
        int threads = int.TryParse(ThreadEntry.Text, out var th) ? Math.Clamp(th, 1, 12) : 3;
        double interval = double.TryParse(IntervalEntry.Text, out var it) ? Math.Max(0, it) : 0.4;
        var bm = BonusPicker.SelectedIndex switch { 1 => BonusMode.Always, 2 => BonusMode.Never, _ => BonusMode.Normal };
        SavePrefs();
        return new DownloadJob {
            NovelNo=m.Value, SaveAsEpub=FormatPicker.SelectedIndex != 1,
            IncludeNotice=NoticeCheck.IsChecked, RemoveBlank=RemoveBlankCheck.IsChecked,
            KeepHtml=KeepHtmlCheck.IsChecked, Compress=CompressCheck.IsChecked,
            DownloadImage=ImageCheck.IsChecked, StopOnError=StopErrorCheck.IsChecked,
            IncludeNovelNoInName=NovelNoNameCheck.IsChecked, IncludeChapterRangeInName=RangeNameCheck.IsChecked,
            Vertical=VerticalCheck.IsChecked, Gothic=GothicCheck.IsChecked, BonusMode=bm,
            From=fromNo, To=toNo, Retry=retry, ThreadNum=threads, Interval=interval,
            FontMappingPath=FontMapEntry.Text
        };
    }

    async void DownloadClicked(object sender, EventArgs e)
    {
        var j = ReadJob(); if (j != null) await RunJob(j);
    }

    void AddQueueClicked(object sender, EventArgs e)
    {
        var j=ReadJob(); if(j==null)return;
        if(queue.Any(x=>x==j)){AppendLog("같은 작업이 이미 목록에 있습니다.");return;}
        queue.Add(j); AppendLog("목록 추가: "+j.Label);
    }

    void RemoveQueueClicked(object sender, EventArgs e)
    {
        if(sender is Button b && b.BindingContext is DownloadJob j) queue.Remove(j);
    }

    void ClearQueueClicked(object sender, EventArgs e) => queue.Clear();

    async void RunQueueClicked(object sender, EventArgs e)
    {
        foreach(var j in queue.ToList())
        {
            if(service.IsCancellationRequested) break;
            await RunJob(j);
        }
        if(!service.IsCancellationRequested) queue.Clear();
    }

    void StopClicked(object sender, EventArgs e) => service.Cancel();

    async Task RunJob(DownloadJob j)
    {
        ToggleRunning(true);
        try
        {
            var result = await service.DownloadAsync(j, new Progress<DownloadProgress>(p =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    Progress.Progress = p.Total <= 0 ? 0 : (double)(p.Done+p.Failed)/p.Total;
                    ProgressText.Text = $"{p.Done+p.Failed}/{p.Total} · 성공 {p.Done} · 실패 {p.Failed} · {p.Message}";
                });
            }));
            if(result != null)
            {
                ProgressText.Text = "완료: " + result.FileName;
                await Share.Default.RequestAsync(new ShareFileRequest {
                    Title="다운로드 결과 저장/공유",
                    File=new ShareFile(result.Path)
                });
            }
        }
        catch(OperationCanceledException){ AppendLog("사용자가 중지했습니다."); }
        catch(Exception ex){ AppendLog("오류: "+ex.Message); await DisplayAlert("오류",ex.Message,"확인"); }
        finally { service.ResetCancellation(); ToggleRunning(false); }
    }

    void ToggleRunning(bool running)
    {
        DownloadButton.IsEnabled=!running; QueueButton.IsEnabled=!running; StopButton.IsEnabled=running;
    }

    void AppendLog(string s)
    {
        LogEditor.Text += (string.IsNullOrEmpty(LogEditor.Text) ? "" : Environment.NewLine) + s;
    }
}
