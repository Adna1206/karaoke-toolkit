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
        try
        {
            ApplicationLicenseText = File.ReadAllText(System.IO.Path.Combine(installationDirectory, "NOTICE.txt"))
                + Environment.NewLine + Environment.NewLine
                + File.ReadAllText(System.IO.Path.Combine(installationDirectory, "LICENSE"));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            ApplicationLicenseText = "Karaoke Toolkit is licensed under GPL-3.0-or-later.\n"
                + "The bundled application licence could not be opened: " + error.Message
                + "\nhttps://www.gnu.org/licenses/gpl-3.0.html";
        }
        var notices = new List<ThirdPartyNotice>();
        foreach (string folder in new[] { "licenses", "ffmpeg", "cdrdao" })
        {
            string directory = System.IO.Path.Combine(installationDirectory, folder);
            if (!Directory.Exists(directory)) continue;
            foreach (string file in Directory.EnumerateFiles(directory).OrderBy(System.IO.Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                string name = System.IO.Path.GetFileName(file);
                bool isNotice = folder == "licenses" ? name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || name == "README.md"
                    : name.StartsWith("LICENSE", StringComparison.OrdinalIgnoreCase) || name.StartsWith("COPYING", StringComparison.OrdinalIgnoreCase)
                      || (folder == "cdrdao" && name == "CDRDAO.GPLv2.txt");
                if (folder == "ffmpeg" && name == "runtime-manifest.json")
                    notices.Add(new("FFmpeg · Build and source information", file));
                else if (isNotice) notices.Add(new(folder == "licenses" ? DisplayName(name) : NativeDisplayName(folder, name), file));
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
        "README.md" => "Component inventory",
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
        string component = folder == "ffmpeg" ? "FFmpeg" : "cdrdao";
        string description = filename.ToUpperInvariant() switch
        {
            "COPYING.GPLV2" => "GPL v2",
            "COPYING.GPLV3" => "GPL v3",
            "COPYING.LGPLV2.1" => "LGPL v2.1",
            "COPYING.LGPLV3" => "LGPL v3",
            "CDRDAO.GPLV2.TXT" => "GPL v2 or later",
            // A distribution's LICENSE can contain multiple notices; do not assume a licence from its filename.
            "LICENSE" or "LICENSE.TXT" or "LICENSE.MD" or "COPYING" => "Licence and notices",
            _ => "Additional notices"
        };
        return component + " · " + description;
    }
}
