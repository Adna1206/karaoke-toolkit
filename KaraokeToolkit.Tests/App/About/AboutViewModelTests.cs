using KaraokeToolkit.App.Features.About;
using KaraokeToolkit.Core;

namespace KaraokeToolkit.Tests.App.About;

public class AboutViewModelTests
{
    [Fact]
    public void ShowsShippedNoticesAndNativeSourceInformationWithoutLoadingBinaries()
    {
        string folder = TestMediaFixture.NewFolder();
        foreach (string directory in new[] { "licenses", "ffmpeg", "cdrdao" })
            Directory.CreateDirectory(Path.Combine(folder, directory));
        File.WriteAllText(Path.Combine(folder, "NOTICE.txt"), "Application copyright and GPL-3.0-or-later grant");
        File.WriteAllText(Path.Combine(folder, "LICENSE"), "Complete application GPL text");
        File.WriteAllText(Path.Combine(folder, "licenses", "Avalonia.txt"), "Complete MIT notice");
        File.WriteAllText(Path.Combine(folder, "licenses", "TagLibSharp.txt"), "Full LGPL v2.1 text");
        File.WriteAllText(Path.Combine(folder, "licenses", "Velopack.txt"), "Full Velopack MIT notice");
        File.WriteAllText(Path.Combine(folder, "ffmpeg", "LICENSE.txt"), "Native build notice");
        File.WriteAllText(Path.Combine(folder, "ffmpeg", "COPYING.GPLv3"), "Full GPL v3 text");
        File.WriteAllText(Path.Combine(folder, "ffmpeg", "runtime-manifest.json"), "Source and version information");
        File.WriteAllText(Path.Combine(folder, "cdrdao", "CDRDAO.GPLv2.txt"), "Disc reader notice");
        File.WriteAllText(Path.Combine(folder, "cdrdao", "cdrdao.exe"), "not a notice");
        var model = new AboutViewModel(folder);
        Assert.Equal(ApplicationInfo.Name, model.AppName);
        Assert.Equal("Version " + ApplicationInfo.Version, model.Version);
        Assert.Contains("Application copyright and GPL-3.0-or-later grant", model.ApplicationLicenseText);
        Assert.Contains("Complete application GPL text", model.ApplicationLicenseText);
        Assert.Equal(7, model.Notices.Count);
        Assert.Equal("Complete MIT notice", model.NoticeText);
        model.SelectedNotice = model.Notices.Single(notice => notice.Name == "TagLibSharp · LGPL v2.1");
        Assert.Equal("Full LGPL v2.1 text", model.NoticeText);
        model.SelectedNotice = model.Notices.Single(notice => notice.Name == "Velopack · MIT");
        Assert.Equal("Full Velopack MIT notice", model.NoticeText);
        model.SelectedNotice = model.Notices.Single(notice => notice.Name == "FFmpeg · Licence and notices");
        Assert.Equal("Native build notice", model.NoticeText);
        model.SelectedNotice = model.Notices.Single(notice => notice.Name == "FFmpeg · GPL v3");
        Assert.Equal("Full GPL v3 text", model.NoticeText);
        model.SelectedNotice = model.Notices.Single(notice => notice.Name == "FFmpeg · Build and source information");
        Assert.Equal("Source and version information", model.NoticeText);
        model.SelectedNotice = model.Notices.Single(notice => notice.Name == "cdrdao · GPL v2 or later");
        Assert.Equal("Disc reader notice", model.NoticeText);
    }

    [Fact]
    public void MissingOrRemovedNoticesRemainReadableErrors()
    {
        string folder = TestMediaFixture.NewFolder();
        var empty = new AboutViewModel(folder);
        Assert.Contains("GPL-3.0-or-later", empty.ApplicationLicenseText);
        Assert.Contains("could not be opened", empty.ApplicationLicenseText);
        Assert.Empty(empty.Notices);
        Assert.Null(empty.SelectedNotice);
        Assert.Contains("No bundled notices", empty.NoticeText);
        Directory.CreateDirectory(Path.Combine(folder, "licenses"));
        string path = Path.Combine(folder, "licenses", "Avalonia.txt");
        File.WriteAllText(path, "MIT");
        var model = new AboutViewModel(folder);
        model.SelectedNotice = null;
        File.Delete(path);
        model.SelectedNotice = model.Notices[0];
        Assert.Contains("could not be opened", model.NoticeText);
    }
}
