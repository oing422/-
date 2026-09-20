using Android.Content;

namespace NovelpiaDownloader;

// 결과 파일을 폰의 Download/Novelpia 폴더에 저장한다.
internal static class DownloadSaver
{
    const string SubFolder = "Novelpia";

    public static string SaveToDownloads(string srcPath, string fileName, string mime)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            var resolver = Android.App.Application.Context.ContentResolver
                ?? throw new InvalidOperationException("ContentResolver 없음");
            var values = new ContentValues();
            values.Put("_display_name", fileName);
            values.Put("mime_type", mime);
            values.Put("relative_path", "Download/" + SubFolder);

            var uri = resolver.Insert(Android.Provider.MediaStore.Downloads.ExternalContentUri!, values)
                ?? throw new IOException("Download 폴더에 파일을 만들지 못했습니다.");
            using (var output = resolver.OpenOutputStream(uri)
                ?? throw new IOException("저장 스트림을 열지 못했습니다."))
            using (var input = File.OpenRead(srcPath))
            {
                input.CopyTo(output);
                output.Flush();
            }
            return "Download/" + SubFolder + "/" + fileName;
        }
        else
        {
            var root = Android.OS.Environment.GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryDownloads)?.AbsolutePath
                ?? throw new IOException("Download 폴더를 찾지 못했습니다.");
            var dir = Path.Combine(root, SubFolder);
            Directory.CreateDirectory(dir);
            var dest = Path.Combine(dir, fileName);
            File.Copy(srcPath, dest, true);
            return dest;
        }
    }
}
