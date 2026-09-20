namespace NovelpiaDownloader;

public enum BonusMode { Normal, Always, Never }

public sealed record DownloadJob
{
    public string NovelNo { get; init; } = "";
    public bool SaveAsEpub { get; init; } = true;
    public bool IncludeNotice { get; init; }
    public bool RemoveBlank { get; init; }
    public bool KeepHtml { get; init; }
    public bool Compress { get; init; }
    public bool DownloadImage { get; init; }
    public bool StopOnError { get; init; }
    public bool IncludeNovelNoInName { get; init; }
    public bool IncludeChapterRangeInName { get; init; }
    public bool Vertical { get; init; }
    public bool Gothic { get; init; }
    public BonusMode BonusMode { get; init; }
    public int? From { get; init; }
    public int? To { get; init; }
    public int Retry { get; init; } = 2;
    public int ThreadNum { get; init; } = 3;
    public double Interval { get; init; } = 0.4;
    public string? FontMappingPath { get; init; }

    public string Label =>
        $"{NovelNo} · {(SaveAsEpub ? "EPUB" : "TXT")} · {(From?.ToString() ?? "처음")}~{(To?.ToString() ?? "끝")}";
}

public sealed record DownloadProgress(int Done, int Total, int Failed, string Message);
public sealed record DownloadResult(string Path, string FileName);
