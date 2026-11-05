using KaraokeToolkit.App.Shared;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using KaraokeToolkit.Core;

namespace KaraokeToolkit.App.Features.About;

public sealed record ThirdPartyNotice(string Name, string Path);

/// <summary>Reads the notices shipped with this installation, including native tool distributions.</summary>
public partial class AboutViewModel : ViewModelBase
{
    public string AppName => ApplicationInfo.Name;
    public string Version => "Version " + ApplicationInfo.Version;
    public string ApplicationLicenseText { get; }
    public IReadOnlyList<ThirdPartyNotice> Notices { get; }
    [ObservableProperty] private ThirdPartyNotice? selectedNotice;
    [ObservableProperty] private string noticeText = "No bundled notices were found in this installation.";

    public AboutViewModel() : this(AppContext.BaseDirectory) { }

    public AboutViewModel(string installationDirectory)
    {
        string licenseDirectory = System.IO.Path.Combine(installationDirectory, "licenses");
        try
        {
            ApplicationLicenseText = File.ReadAllText(System.IO.Path.Combine(licenseDirectory, "application", "NOTICE.txt"))
                + Environment.NewLine + Environment.NewLine
                + File.ReadAllText(System.IO.Path.Combine(licenseDirectory, "application", "LICENSE"));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            ApplicationLicenseText = "Karaoke Toolkit is licensed under GPL-3.0-or-later.\n"
                + "The bundled application licence could not be opened: " + error.Message
                + "\nhttps://www.gnu.org/licenses/gpl-3.0.html";
        }
        var notices = new List<ThirdPartyNotice>();
        foreach (string folder in new[] { "", "ffmpeg", "cdrdao" })
        {
            string directory = System.IO.Path.Combine(licenseDirectory, folder);
            if (!Directory.Exists(directory)) continue;
            var search = folder == "" ? SearchOption.TopDirectoryOnly : SearchOption.AllDirectories;
            foreach (string file in Directory.EnumerateFiles(directory, "*", search).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                string name = System.IO.Path.GetFileName(file);
                // Show the bundled tool's canonical notices, not common duplicate texts.
                if (folder == "" && ((name.StartsWith("FFmpeg.", StringComparison.OrdinalIgnoreCase)
                    && Directory.Exists(System.IO.Path.Combine(licenseDirectory, "ffmpeg")))
                    || (name.Equals("CDRDAO.GPLv2.txt", StringComparison.OrdinalIgnoreCase)
                    && File.Exists(System.IO.Path.Combine(licenseDirectory, "cdrdao", "cdrdao.GPLv2.txt"))))) continue;
                if (name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                    notices.Add(new(folder == "" ? DisplayName(name)
                    : NativeDisplayName(folder, System.IO.Path.GetRelativePath(directory, file)), file));
            }
        }
        Notices = notices.AsReadOnly();
        SelectedNotice = notices.FirstOrDefault();
    }

    partial void OnSelectedNoticeChanged(ThirdPartyNotice? value)
    {
        if (value == null) return;
        try { NoticeText = File.ReadAllText(value.Path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { NoticeText = "This notice could not be opened.\n\n" + error.Message; }
    }

    private static string DisplayName(string filename) => filename switch
    {
        "Avalonia.txt" => "Avalonia · MIT",
        "CommunityToolkit.Mvvm.txt" => "CommunityToolkit.Mvvm · MIT",
        "CommunityToolkit.Mvvm.ThirdPartyNotices.txt" => "Community Toolkit · notices",
        "FFMpegCore.txt" => "FFMpegCore · MIT",
        "SharpCompress.txt" => "SharpCompress · MIT",
        "TagLibSharp.txt" => "TagLibSharp · LGPL v2.1",
        "Velopack.txt" => "Velopack · MIT",
        "ModelContextProtocol.txt" => "MCP C# SDK · Apache 2.0 / MIT notices",
        "ModelContextProtocol.Notice.txt" => "MCP C# SDK · notices",
        "Inter.OFL.txt" => "Inter font · SIL OFL 1.1",
        "ANGLE.txt" => "ANGLE · BSD-style",
        "SkiaSharp.txt" => "SkiaSharp · MIT / notices",
        "HarfBuzzSharp.txt" => "HarfBuzzSharp · MIT / notices",
        "Instances.txt" => "Instances · MIT",
        "MicroCom.Runtime.txt" => "MicroCom.Runtime · MIT",
        "Microsoft.Extensions.txt" => "Microsoft Extensions · MIT",
        "Microsoft.Extensions.ThirdPartyNotices.txt" => "Microsoft Extensions · notices",
        "Microsoft.IO.RecyclableMemoryStream.txt" => "RecyclableMemoryStream · MIT",
        "Tmds.DBus.Protocol.txt" => "Tmds.DBus.Protocol · MIT",
        "CDRDAO.GPLv2.txt" => "cdrdao · GPL v2 or later",
        "FFmpeg.LGPLv3.txt" => "FFmpeg · LGPL v3",
        "FFmpeg.GPLv3.txt" => "FFmpeg · GPL v3",
        "FFmpeg.GPLv2.txt" => "FFmpeg · GPL v2",
        _ => filename
    };

    private static string NativeDisplayName(string folder, string filename)
    {
        string origin = folder == "ffmpeg" ? "FFmpeg" : folder;
        string[] parts = System.IO.Path.ChangeExtension(filename, null)!.Split('.', 2);
        string document = string.Join(" · ", parts.Length > 1 && parts[0].Equals(origin, StringComparison.OrdinalIgnoreCase)
            ? parts.Skip(1) : parts).Replace('-', ' ');
        return origin + " · " + document;
    }
}
