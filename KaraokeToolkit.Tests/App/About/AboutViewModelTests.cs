using KaraokeToolkit.App.Features.About;
using KaraokeToolkit.Core;

namespace KaraokeToolkit.Tests.App.About;

public class AboutViewModelTests
{
    [Fact]
    public void ShowsOnlyCanonicalLicenseTextsWithoutLoadingBinaries()
    {
        string folder = TestMediaFixture.NewFolder();
        foreach (string directory in new[] { "licenses", "licenses/application", "licenses/ffmpeg", "licenses/cdrdao", "ffmpeg", "cdrdao" })
            Directory.CreateDirectory(Path.Combine(folder, directory));
        File.WriteAllText(Path.Combine(folder, "licenses", "application", "NOTICE.txt"), "Application copyright and GPL-3.0-or-later grant");
        File.WriteAllText(Path.Combine(folder, "licenses", "application", "LICENSE"), "Complete application GPL text");
        File.WriteAllText(Path.Combine(folder, "licenses", "Avalonia.txt"), "Complete MIT notice");
        File.WriteAllText(Path.Combine(folder, "licenses", "TagLibSharp.txt"), "Full LGPL v2.1 text");
        File.WriteAllText(Path.Combine(folder, "licenses", "Velopack.txt"), "Full Velopack MIT notice");
        File.WriteAllText(Path.Combine(folder, "licenses", "FFmpeg.GPLv2.txt"), "Unneeded common native copy");
        File.WriteAllText(Path.Combine(folder, "licenses", "ffmpeg", "FFmpeg.Notices.txt"), "Native build notice");
        File.WriteAllText(Path.Combine(folder, "licenses", "ffmpeg", "FFmpeg.GPLv3.txt"), "Full GPL v3 text");
        File.WriteAllText(Path.Combine(folder, "licenses", "ffmpeg", "runtime-manifest.json"), "Source and version information");
        File.WriteAllText(Path.Combine(folder, "licenses", "cdrdao", "cdrdao.GPLv2.txt"), "Disc reader notice");
        File.WriteAllText(Path.Combine(folder, "ffmpeg", "LICENSE.txt"), "Ignore old native location");
        File.WriteAllText(Path.Combine(folder, "cdrdao", "COPYING.extra.txt"), "Ignore old reader location");
        File.WriteAllText(Path.Combine(folder, "NOTICE.txt"), "Ignore old application location");
        File.WriteAllText(Path.Combine(folder, "LICENSE"), "Ignore old application licence");
        string nested = Directory.CreateDirectory(Path.Combine(folder, "licenses", "ffmpeg", "dependency")).FullName;
        File.WriteAllText(Path.Combine(nested, "COPYING.txt"), "Nested dependency notice");
        File.WriteAllText(Path.Combine(folder, "cdrdao", "cdrdao.exe"), "not a notice");
        var model = new AboutViewModel(folder);
        Assert.Equal(ApplicationInfo.Name, model.AppName);
        Assert.Equal("Version " + ApplicationInfo.Version, model.Version);
        Assert.Contains("Application copyright and GPL-3.0-or-later grant", model.ApplicationLicenseText);
        Assert.Contains("Complete application GPL text", model.ApplicationLicenseText);
        Assert.Equal(7, model.Notices.Count);
        Assert.DoesNotContain(model.Notices, notice => notice.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
        Assert.All(model.Notices, notice => Assert.StartsWith(Path.Combine(folder, "licenses") + Path.DirectorySeparatorChar, notice.Path));
        Assert.Equal("Complete MIT notice", model.NoticeText);
        model.SelectedNotice = model.Notices.Single(notice => notice.Name == "TagLibSharp · LGPL v2.1");
        Assert.Equal("Full LGPL v2.1 text", model.NoticeText);
        model.SelectedNotice = model.Notices.Single(notice => notice.Name == "Velopack · MIT");
        Assert.Equal("Full Velopack MIT notice", model.NoticeText);
        model.SelectedNotice = model.Notices.Single(notice => notice.Name == "FFmpeg · Notices");
        Assert.Equal("Native build notice", model.NoticeText);
        model.SelectedNotice = model.Notices.Single(notice => notice.Name == "FFmpeg · GPLv3");
        Assert.Equal("Full GPL v3 text", model.NoticeText);
        model.SelectedNotice = model.Notices.Single(notice => notice.Name == "cdrdao · GPLv2");
        Assert.Equal("Disc reader notice", model.NoticeText);
        model.SelectedNotice = model.Notices.Single(notice => notice.Path == Path.Combine(nested, "COPYING.txt"));
        Assert.Equal("Nested dependency notice", model.NoticeText);
    }

    [Fact]
    public void DistinguishesDiscReaderRuntimeNoticesAndOmitsInventory()
    {
        string folder = TestMediaFixture.NewFolder();
        string licenses = Directory.CreateDirectory(Path.Combine(folder, "licenses")).FullName;
        string reader = Directory.CreateDirectory(Path.Combine(licenses, "cdrdao")).FullName;
        File.WriteAllText(Path.Combine(licenses, "README.md"), "Component inventory documentation");
        File.WriteAllText(Path.Combine(reader, "runtime-components.json"), "Source audit metadata");
        File.WriteAllText(Path.Combine(licenses, "CDRDAO.GPLv2.txt"), "Duplicate common copy");

        (string File, string Label)[] expected =
        [
            ("cdrdao.GPLv2.txt", "cdrdao · GPLv2"),
            ("MSYS2.Notices-and-Linking-Exception.txt", "cdrdao · MSYS2 · Notices and Linking Exception"),
            ("MSYS2.LGPLv3.txt", "cdrdao · MSYS2 · LGPLv3"),
            ("GCC.Runtime-Exception.txt", "cdrdao · GCC · Runtime Exception"),
            ("GCC.GPLv3.txt", "cdrdao · GCC · GPLv3"),
            ("libiconv.LGPLv2.1.txt", "cdrdao · libiconv · LGPLv2.1"),
            ("newlib.Notices.txt", "cdrdao · newlib · Notices"),
            ("Future.Component-Notice.txt", "cdrdao · Future · Component Notice")
        ];
        foreach (var notice in expected)
            File.WriteAllText(Path.Combine(reader, notice.File), notice.File);

        var model = new AboutViewModel(folder);
        Assert.Equal(expected.Length, model.Notices.Count);
        foreach (var notice in expected)
        {
            model.SelectedNotice = Assert.Single(model.Notices, entry => entry.Name == notice.Label);
            Assert.Equal(notice.File, model.NoticeText);
        }
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
