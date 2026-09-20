using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NovelpiaDownloader;

public sealed class MobileDownloader
{
    readonly HttpClient http;
    CancellationTokenSource cts = new();
    public event EventHandler<string>? Log;
    public string LoginKey { get; private set; } = CreateLoginKey();
    public bool IsCancellationRequested => cts.IsCancellationRequested;

    public MobileDownloader()
    {
        http = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All });
        http.Timeout = TimeSpan.FromSeconds(35);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 Chrome/120 Mobile Safari/537.36");
    }

    public void SetLoginKey(string key) { if(!string.IsNullOrWhiteSpace(key)) LoginKey=key.Trim(); }
    public void Cancel() => cts.Cancel();
    public void ResetCancellation() { cts.Dispose(); cts=new CancellationTokenSource(); }

    static string CreateLoginKey()
    {
        const string chars="0123456789abcdef";
        string Part()=>new string(Enumerable.Range(0,32).Select(_=>chars[Random.Shared.Next(chars.Length)]).ToArray());
        return Part()+"_"+Part();
    }

    public async Task<bool> LoginAsync(string email,string password)
    {
        using var req = NewRequest(HttpMethod.Post,"https://novelpia.com/proc/login","https://novelpia.com/");
        req.Content=new FormUrlEncodedContent(new Dictionary<string,string>{{"redirectrurl",""},{"email",email},{"wd",password}});
        using var resp=await http.SendAsync(req,cts.Token);
        var body=await resp.Content.ReadAsStringAsync(cts.Token);
        var ok=body.Contains("감사합니다",StringComparison.Ordinal);
        Log?.Invoke(this, ok ? "로그인 성공" : "로그인 실패");
        return ok;
    }

    public async Task<DownloadResult?> DownloadAsync(DownloadJob job,IProgress<DownloadProgress>? progress=null)
    {
        var token=cts.Token;
        token.ThrowIfCancellationRequested();
        var map=new FontMapping(job.FontMappingPath);
        var novelHtml=(job.SaveAsEpub||job.IncludeNotice)
            ? await GetAsync($"https://novelpia.com/novel/{job.NovelNo}",token) : "";

        string title=Extract(novelHtml,@"productName = '(.+?)';");
        if(string.IsNullOrWhiteSpace(title)) title="Novelpia_"+job.NovelNo;
        string author=WebUtility.HtmlDecode(Extract(novelHtml,@"<meta[^>]+name=[""']author[""'][^>]+content=[""']([^""']+)[""']"));
        string safeTitle=SafeFile(title);
        var suffix=new StringBuilder();
        if(job.IncludeNovelNoInName) suffix.Append("_").Append(job.NovelNo);
        if(job.IncludeChapterRangeInName && (job.From.HasValue||job.To.HasValue))
            suffix.Append($"_{job.From?.ToString()??"start"}-{job.To?.ToString()??"end"}");
        var ext=job.SaveAsEpub?".epub":".txt";
        string fileName=safeTitle+suffix+ext;

        var work=Path.Combine(FileSystem.Current.AppDataDirectory,"work",job.NovelNo+"_"+DateTime.Now.ToString("yyyyMMddHHmmss"));
        Directory.CreateDirectory(work);
        var output=Path.Combine(FileSystem.Current.AppDataDirectory,"downloads");
        Directory.CreateDirectory(output);
        var outPath=Path.Combine(output,fileName);

        try
        {
            var chapters=new List<Chapter>();
            if(job.IncludeNotice && !string.IsNullOrEmpty(novelHtml))
            {
                var table=Regex.Match(novelHtml,@"<table[^>]*class=""[^""]*notice_table[^""]*""[^>]*>(.+?)</table>",RegexOptions.Singleline);
                if(table.Success)
                foreach(Match m in Regex.Matches(table.Groups[1].Value,@"location='/viewer/(\d+)';""[^>]*><b>(.+?)</b>",RegexOptions.Singleline))
                    chapters.Add(new Chapter(m.Groups[1].Value,$"[공지] {WebUtility.HtmlDecode(Strip(m.Groups[2].Value))}",-1,false,true));
            }

            var all=new List<Chapter>();
            var seen=new HashSet<string>();
            for(int page=0;page<500;page++)
            {
                token.ThrowIfCancellationRequested();
                var body=await PostAsync("https://novelpia.com/proc/episode_list",
                    $"novel_no={Uri.EscapeDataString(job.NovelNo)}&sort=DOWN&page={page}","https://novelpia.com/",token);
                var ms=Regex.Matches(body,@"id=""bookmark_(\d+)""></i>(.+?)</b>.+?>(EP\.(\d+)|BONUS)<",RegexOptions.Singleline);
                if(ms.Count==0) break;
                bool any=false;
                foreach(Match m in ms)
                {
                    if(!seen.Add(m.Groups[1].Value)) continue;
                    any=true;
                    bool bonus=m.Groups[3].Value=="BONUS";
                    int ep=bonus?0:int.Parse(m.Groups[4].Value);
                    all.Add(new Chapter(m.Groups[1].Value,WebUtility.HtmlDecode(Strip(m.Groups[2].Value)),ep,bonus,false));
                }
                Log?.Invoke(this,$"목록 확인: {all.Count}개");
                if(!any) break;
            }

            int maxEp=all.Where(x=>!x.Bonus).Select(x=>x.Ep).DefaultIfEmpty(0).Max();
            int running=0,lastEp=0,tailBonus=0;
            foreach(var ch in all)
            {
                if(ch.Bonus)
                {
                    if(job.BonusMode==BonusMode.Never) continue;
                    if(job.BonusMode==BonusMode.Normal)
                    {
                        bool tail=maxEp>0 && running>=maxEp;
                        if(tail)
                        {
                            int virtualNo=maxEp+(++tailBonus);
                            if(job.From.HasValue&&virtualNo<job.From.Value) continue;
                            if(job.To.HasValue&&virtualNo>job.To.Value) continue;
                        }
                        else
                        {
                            if(job.From.HasValue&&lastEp<job.From.Value) continue;
                            if(job.To.HasValue&&lastEp>job.To.Value) continue;
                        }
                    }
                }
                else
                {
                    running=Math.Max(running,ch.Ep); lastEp=ch.Ep;
                    if(job.From.HasValue&&ch.Ep<job.From.Value) continue;
                    if(job.To.HasValue&&ch.Ep>job.To.Value) continue;
                }
                chapters.Add(ch);
            }
            if(chapters.Count==0) throw new InvalidOperationException("선택 조건에 맞는 화가 없습니다.");

            var results=new ConcurrentDictionary<int,RenderedChapter>();
            int done=0,failed=0;
            for(int start=0;start<chapters.Count;start+=job.ThreadNum)
            {
                token.ThrowIfCancellationRequested();
                var batch=chapters.Skip(start).Take(job.ThreadNum).Select((ch,off)=>Task.Run(async()=>{
                    int idx=start+off;
                    try
                    {
                        var json=await WithRetry(()=>PostAsync($"https://novelpia.com/proc/viewer_data/{ch.Id}",null,"https://novelpia.com/",token),job.Retry,token);
                        if(string.IsNullOrWhiteSpace(json)||json.Contains("본인인증")) throw new Exception("본문 응답 오류");
                        var rendered=await RenderChapter(ch,json,job,map,work,idx,token);
                        results[idx]=rendered;
                        var d=Interlocked.Increment(ref done);
                        progress?.Report(new DownloadProgress(d,chapters.Count,Volatile.Read(ref failed),ch.Title));
                        Log?.Invoke(this,$"✓ {ch.Title}");
                    }
                    catch(Exception ex) when(ex is not OperationCanceledException)
                    {
                        var f=Interlocked.Increment(ref failed);
                        progress?.Report(new DownloadProgress(Volatile.Read(ref done),chapters.Count,f,ch.Title));
                        Log?.Invoke(this,$"✗ {ch.Title} ({ex.Message})");
                        if(job.StopOnError) cts.Cancel();
                    }
                },token)).ToArray();
                await Task.WhenAll(batch);
                if(job.Interval>0 && start+job.ThreadNum<chapters.Count)
                    await Task.Delay(TimeSpan.FromSeconds(job.Interval),token);
            }
            token.ThrowIfCancellationRequested();

            var ordered=results.OrderBy(x=>x.Key).Select(x=>x.Value).ToList();
            if(job.SaveAsEpub)
                await BuildEpub(outPath,job,title,author,novelHtml,ordered,work,token);
            else
                await BuildTxt(outPath,ordered,job,map,token);

            Log?.Invoke(this,"완료: "+fileName);
            return new DownloadResult(outPath,fileName);
        }
        finally
        {
            try { if(Directory.Exists(work)) Directory.Delete(work,true); } catch {}
        }
    }

    async Task<RenderedChapter> RenderChapter(Chapter ch,string json,DownloadJob job,FontMapping map,string work,int idx,CancellationToken token)
    {
        using var doc=JsonDocument.Parse(json);
        if(!doc.RootElement.TryGetProperty("s",out var arr)) throw new Exception("본문 형식 오류");
        var html=new StringBuilder();
        var plain=new StringBuilder();
        var images=new List<ImageAsset>();
        html.Append(EpubTemplate.chapter).Append("<h1>").Append(WebUtility.HtmlEncode(ch.Title)).Append("</h1>\n<p>&nbsp;</p>\n");
        var carry=new List<string>();
        int imageNo=0;
        foreach(var item in arr.EnumerateArray())
        {
            token.ThrowIfCancellationRequested();
            if(!item.TryGetProperty("text",out var te)) continue;
            var raw=te.GetString()??"";
            if(raw.Contains("cover-wrapper",StringComparison.OrdinalIgnoreCase)) continue;
            var im=Regex.Match(raw,@"<img.+?src=""(.+?)"".+?>",RegexOptions.IgnoreCase);
            if(im.Success)
            {
                if(job.DownloadImage)
                {
                    imageNo++;
                    var bytes=await DownloadBytes(im.Groups[1].Value,job.Retry,token);
                    var (ext,mime)=DetectImageType(bytes);
                    var asset=new ImageAsset($"{idx+1}_{imageNo}",ext,mime,bytes);
                    images.Add(asset);
                    html.Append($"<p><img alt=\"{imageNo}\" src=\"../Images/{asset.Name}.{ext}\" width=\"100%\"/></p>\n");
                }
                else if(!job.RemoveBlank) html.Append("<p>&#160;</p>\n");
                continue;
            }

            var cleaned=CleanText(raw,job.KeepHtml,ref carry);
            cleaned=map.DecodeText(cleaned);
            if(string.IsNullOrEmpty(cleaned))
            {
                if(!job.RemoveBlank){ html.Append("<p>&#160;</p>\n"); plain.AppendLine(); }
                continue;
            }
            html.Append("<p>").Append(cleaned).Append("</p>\n");
            var p=job.KeepHtml?Regex.Replace(cleaned,@"<[^>]+>",""):cleaned;
            plain.AppendLine(WebUtility.HtmlDecode(p));
        }
        html.Append("</body>\n</html>\n");
        return new RenderedChapter(ch.Title,html.ToString(),plain.ToString(),images);
    }

    async Task BuildTxt(string path,List<RenderedChapter> chapters,DownloadJob job,FontMapping map,CancellationToken token)
    {
        await using var fs=new FileStream(path,FileMode.Create,FileAccess.Write);
        await using var sw=new StreamWriter(fs,new UTF8Encoding(false));
        foreach(var c in chapters)
        {
            token.ThrowIfCancellationRequested();
            await sw.WriteLineAsync(c.Title);
            await sw.WriteLineAsync();
            await sw.WriteAsync(c.Plain);
            await sw.WriteLineAsync();
        }
    }

    async Task BuildEpub(string path,DownloadJob job,string title,string author,string novelHtml,List<RenderedChapter> chapters,string work,CancellationToken token)
    {
        byte[]? cover=null; string coverExt="jpg",coverMime="image/jpeg";
        if(job.DownloadImage)
        {
            var url=Extract(novelHtml,@"(?:href|src)=""(//images\.novelpia\.com/imagebox/cover/.+?\.file)""");
            if(!string.IsNullOrEmpty(url))
            {
                try { cover=await DownloadBytes(url,job.Retry,token); (coverExt,coverMime)=DetectImageType(cover); } catch {}
            }
        }

        var titleEnc=WebUtility.HtmlEncode(title);
        var authorEnc=WebUtility.HtmlEncode(author);
        var now=DateTimeOffset.Now;
        var ncx=new StringBuilder(EpubTemplate.toc);
        ncx.Append("<text>").Append(titleEnc).Append("</text>\n</docTitle>\n<navMap>\n");
        for(int i=0;i<chapters.Count;i++)
            ncx.Append($"<navPoint id=\"navPoint-{i+1}\" playOrder=\"{i+1}\"><navLabel><text>{WebUtility.HtmlEncode(chapters[i].Title)}</text></navLabel><content src=\"Text/chapter{i+1}.html\" /></navPoint>\n");
        ncx.Append("</navMap>\n</ncx>\n");

        var opf=new StringBuilder(EpubTemplate.content1);
        opf.Append($"<dc:identifier id=\"BookId\" opf:scheme=\"NovelpiaNovelNo\">{job.NovelNo}</dc:identifier>\n");
        opf.Append("<dc:title>").Append(titleEnc).Append("</dc:title>\n<dc:language>ko</dc:language>\n");
        if(!string.IsNullOrEmpty(author)) opf.Append("<dc:creator opf:role=\"aut\">").Append(authorEnc).Append("</dc:creator>\n");
        opf.Append($"<dc:date>{DateTime.UtcNow:yyyy-MM-dd}</dc:date>\n");
        if(cover!=null) opf.Append("<meta name=\"cover\" content=\"cover-img\"/>\n");
        opf.Append(EpubTemplate.content2_head);
        if(cover!=null)
        {
            opf.Append("<item id=\"cover.html\" href=\"Text/cover.html\" media-type=\"application/xhtml+xml\"/>\n");
            opf.Append($"<item id=\"cover-img\" href=\"Images/cover.{coverExt}\" media-type=\"{coverMime}\"/>\n");
        }
        for(int i=0;i<chapters.Count;i++)
        {
            opf.Append($"<item id=\"chapter{i+1}\" href=\"Text/chapter{i+1}.html\" media-type=\"application/xhtml+xml\"/>\n");
            foreach(var im in chapters[i].Images)
                opf.Append($"<item id=\"img-{im.Name}\" href=\"Images/{im.Name}.{im.Ext}\" media-type=\"{im.Mime}\"/>\n");
        }
        opf.Append("</manifest>\n<spine toc=\"ncx\">\n");
        if(cover!=null) opf.Append("<itemref idref=\"cover.html\"/>\n");
        for(int i=0;i<chapters.Count;i++) opf.Append($"<itemref idref=\"chapter{i+1}\"/>\n");
        opf.Append("</spine>\n");
        if(cover!=null) opf.Append("<guide><reference type=\"cover\" title=\"Cover\" href=\"Text/cover.html\"/></guide>\n");
        opf.Append("</package>\n");

        if(File.Exists(path)) File.Delete(path);
        await using var fs=new FileStream(path,FileMode.Create,FileAccess.Write);
        using var zip=new EpubZipWriter(fs,job.Compress,leaveOpen:true);
        zip.AddEntry("mimetype","application/epub+zip",now);
        zip.AddEntry("META-INF/container.xml",EpubTemplate.container,now);
        zip.AddEntry("OEBPS/Styles/sgc-toc.css",EpubTemplate.sgctoc,now);
        zip.AddEntry("OEBPS/Styles/Stylesheet.css",EpubTemplate.Stylesheet(job.Vertical,job.Gothic),now);
        if(cover!=null)
        {
            zip.AddEntry("OEBPS/Text/cover.html",EpubTemplate.cover.Replace("__COVER_EXT__",coverExt),now);
            zip.AddEntry($"OEBPS/Images/cover.{coverExt}",cover,now);
        }
        for(int i=0;i<chapters.Count;i++)
        {
            zip.AddEntry($"OEBPS/Text/chapter{i+1}.html",chapters[i].Html,now);
            foreach(var im in chapters[i].Images) zip.AddEntry($"OEBPS/Images/{im.Name}.{im.Ext}",im.Bytes,now);
        }
        zip.AddEntry("OEBPS/toc.ncx",ncx.ToString(),now);
        zip.AddEntry("OEBPS/content.opf",opf.ToString(),now);
        zip.Finish();
    }

    static string CleanText(string text,bool keepHtml,ref List<string> carry)
    {
        text=Regex.Replace(text,@"<p style='height: 0px; width: 0px;.+?>.*?</p>","");
        if(!keepHtml) text=Regex.Replace(text,@"</?[^>]+>","");
        else
        {
            var sb=new StringBuilder();
            var stack=new List<string>(carry);
            foreach(var open in carry) sb.Append('<').Append(open).Append('>');
            int pos=0;
            foreach(Match m in Regex.Matches(text,@"<(/?)([a-zA-Z][a-zA-Z0-9]*)([^>]*)>"))
            {
                sb.Append(text.Substring(pos,m.Index-pos));
                var name=m.Groups[2].Value.ToLowerInvariant();
                bool close=m.Groups[1].Value=="/";
                bool self=m.Value.EndsWith("/>")||name is "img" or "br" or "hr";
                if(self) sb.Append(m.Value);
                else if(close)
                {
                    int ix=-1; for(int k=stack.Count-1;k>=0;k--) if(TagName(stack[k])==name){ix=k;break;}
                    if(ix>=0){ for(int k=stack.Count-1;k>=ix;k--) sb.Append("</").Append(TagName(stack[k])).Append('>'); stack.RemoveRange(ix,stack.Count-ix); }
                }
                else { stack.Add(name+m.Groups[3].Value); sb.Append(m.Value); }
                pos=m.Index+m.Length;
            }
            sb.Append(text.Substring(pos));
            for(int k=stack.Count-1;k>=0;k--) sb.Append("</").Append(TagName(stack[k])).Append('>');
            carry=stack; text=sb.ToString();
        }
        return text.Replace("\n","").Replace("\r","");
    }

    static string TagName(string s){int i=s.IndexOf(' ');return i>0?s[..i]:s;}

    async Task<T> WithRetry<T>(Func<Task<T>> action,int retry,CancellationToken token)
    {
        Exception? last=null;
        for(int i=0;i<=retry;i++)
        {
            token.ThrowIfCancellationRequested();
            try{return await action();}
            catch(Exception ex) when(ex is not OperationCanceledException){last=ex;if(i<retry)await Task.Delay(350,token);}
        }
        throw last??new Exception("요청 실패");
    }

    async Task<byte[]> DownloadBytes(string url,int retry,CancellationToken token)
    {
        if(url.StartsWith("//")) url="https:"+url;
        return await WithRetry(async()=>{
            using var req=NewRequest(HttpMethod.Get,url,null);
            using var resp=await http.SendAsync(req,HttpCompletionOption.ResponseHeadersRead,token);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsByteArrayAsync(token);
        },retry,token);
    }

    async Task<string> GetAsync(string url,CancellationToken token)
    {
        using var req=NewRequest(HttpMethod.Get,url,null);
        using var resp=await http.SendAsync(req,token); resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(token);
    }

    async Task<string> PostAsync(string url,string? body,string? referer,CancellationToken token)
    {
        using var req=NewRequest(HttpMethod.Post,url,referer);
        req.Content=body==null?new StringContent(""):new StringContent(body,Encoding.UTF8,"application/x-www-form-urlencoded");
        using var resp=await http.SendAsync(req,token); resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(token);
    }

    HttpRequestMessage NewRequest(HttpMethod method,string url,string? referer)
    {
        var r=new HttpRequestMessage(method,url);
        r.Headers.TryAddWithoutValidation("Cookie",$"LOGINKEY={LoginKey};");
        if(!string.IsNullOrWhiteSpace(referer)) r.Headers.Referrer=new Uri(referer);
        return r;
    }

    static string Extract(string s,string pattern)
    {
        if(string.IsNullOrEmpty(s)) return "";
        var m=Regex.Match(s,pattern,RegexOptions.Singleline|RegexOptions.IgnoreCase);
        return m.Success?m.Groups[1].Value:"";
    }
    static string Strip(string s)=>Regex.Replace(s,@"<[^>]+>","");
    static string SafeFile(string s)=>Regex.Replace(WebUtility.HtmlDecode(s),@"[\\/:*?""<>|]","_").Trim();

    static (string ext,string mime) DetectImageType(byte[] b)
    {
        if(b.Length>=8&&b[0]==0x89&&b[1]==0x50&&b[2]==0x4E&&b[3]==0x47)return("png","image/png");
        if(b.Length>=3&&b[0]==0xFF&&b[1]==0xD8&&b[2]==0xFF)return("jpg","image/jpeg");
        if(b.Length>=6&&b[0]==0x47&&b[1]==0x49&&b[2]==0x46)return("gif","image/gif");
        if(b.Length>=12&&b[0]==0x52&&b[1]==0x49&&b[2]==0x46&&b[3]==0x46&&b[8]==0x57&&b[9]==0x45&&b[10]==0x42&&b[11]==0x50)return("webp","image/webp");
        if(b.Length>=2&&b[0]==0x42&&b[1]==0x4D)return("bmp","image/bmp");
        return("jpg","image/jpeg");
    }

    sealed record Chapter(string Id,string Title,int Ep,bool Bonus,bool Notice);
    sealed record ImageAsset(string Name,string Ext,string Mime,byte[] Bytes);
    sealed record RenderedChapter(string Title,string Html,string Plain,List<ImageAsset> Images);
}
