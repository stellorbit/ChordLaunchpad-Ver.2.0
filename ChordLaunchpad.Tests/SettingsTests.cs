using System;
using System.IO;
using Windows.System;
using Xunit;
using ChordLaunchpad.Core.Models;

namespace ChordLaunchpad.Tests;

public class SettingsTests
{
    [Fact]
    public void DefaultSettings_ShouldHaveExpectedDefaults()
    {
        var settings = new AppSettings();
        Assert.Equal(ZoomShortcutStyle.ProTools, settings.ZoomStyle);
        Assert.True(settings.EnableNumpadZoom);
        Assert.True(settings.EnableWheelZoom);
        Assert.Equal(VirtualKey.T, settings.CustomZoomInKey);
        Assert.Equal("ja-JP", settings.AppLanguage);
        Assert.True(settings.EnableAutoSave);
        Assert.Equal(5, settings.AutoSaveIntervalMinutes);
        Assert.Equal(10, settings.AutoSaveMaxBackups);
        Assert.False(settings.UseDefaultProjectDirectory);
        Assert.Contains("ChordLaunchpad Projects", settings.DefaultProjectDirectory);
    }

    [Fact]
    public void AppSettings_Language_Serialization_PreservesSelectedLanguage()
    {
        var settings = new AppSettings
        {
            AppLanguage = "en-US"
        };

        var json = System.Text.Json.JsonSerializer.Serialize(settings);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(deserialized);
        Assert.Equal("en-US", deserialized.AppLanguage);
    }

    [Fact]
    public void LocalizationService_ShouldSwitchLanguagesAndFireEvent()
    {
        bool eventFired = false;
        void OnLanguageChanged() => eventFired = true;

        ChordLaunchpad.Core.LocalizationService.LanguageChanged += OnLanguageChanged;

        try
        {
            ChordLaunchpad.Core.LocalizationService.CurrentLanguage = ChordLaunchpad.Core.LocalizationService.LanguageJapanese;
            Assert.False(ChordLaunchpad.Core.LocalizationService.IsEnglish);
            Assert.Equal("ファイル", ChordLaunchpad.Core.LocalizationService.Get("Menu_File"));
            Assert.Equal("プロジェクトを保存", ChordLaunchpad.Core.LocalizationService.Get("Menu_File_Save"));

            eventFired = false;
            ChordLaunchpad.Core.LocalizationService.CurrentLanguage = ChordLaunchpad.Core.LocalizationService.LanguageEnglish;
            Assert.True(eventFired);
            Assert.True(ChordLaunchpad.Core.LocalizationService.IsEnglish);
            Assert.Equal("File", ChordLaunchpad.Core.LocalizationService.Get("Menu_File"));
            Assert.Equal("Save Project", ChordLaunchpad.Core.LocalizationService.Get("Menu_File_Save"));
        }
        finally
        {
            ChordLaunchpad.Core.LocalizationService.LanguageChanged -= OnLanguageChanged;
            ChordLaunchpad.Core.LocalizationService.CurrentLanguage = ChordLaunchpad.Core.LocalizationService.LanguageJapanese;
        }
    }

    [Theory]
    [InlineData(ZoomShortcutStyle.ProTools)]
    [InlineData(ZoomShortcutStyle.Cubase)]
    [InlineData(ZoomShortcutStyle.StudioOne)]
    [InlineData(ZoomShortcutStyle.PremiereResolve)]
    [InlineData(ZoomShortcutStyle.AvidMediaComposer)]
    [InlineData(ZoomShortcutStyle.Custom)]
    public void AppSettings_Serialization_PreservesAllStyles(ZoomShortcutStyle style)
    {
        var settings = new AppSettings
        {
            ZoomStyle = style,
            EnableNumpadZoom = false,
            CustomZoomInKey = VirtualKey.H,
            CustomZoomInCtrl = true,
            CustomZoomOutKey = VirtualKey.G,
            CustomZoomOutShift = true,
            EnableWheelZoom = false,
            EnableAutoSave = true,
            AutoSaveIntervalMinutes = 3,
            AutoSaveMaxBackups = 15,
            UseDefaultProjectDirectory = true,
            DefaultProjectDirectory = @"C:\Music\MyProjects"
        };

        var json = System.Text.Json.JsonSerializer.Serialize(settings);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(style, deserialized.ZoomStyle);
        Assert.False(deserialized.EnableNumpadZoom);
        Assert.Equal(VirtualKey.H, deserialized.CustomZoomInKey);
        Assert.True(deserialized.CustomZoomInCtrl);
        Assert.Equal(VirtualKey.G, deserialized.CustomZoomOutKey);
        Assert.True(deserialized.CustomZoomOutShift);
        Assert.False(deserialized.EnableWheelZoom);
        Assert.True(deserialized.EnableAutoSave);
        Assert.Equal(3, deserialized.AutoSaveIntervalMinutes);
        Assert.Equal(15, deserialized.AutoSaveMaxBackups);
        Assert.True(deserialized.UseDefaultProjectDirectory);
        Assert.Equal(@"C:\Music\MyProjects", deserialized.DefaultProjectDirectory);
    }

    [Fact]
    public void ProjectBackup_FileNameGeneration_ShouldFormatNumberProperly()
    {
        var baseName = "MySong";
        var number = 3;
        var ext = ".chord";
        var fileName = $"{baseName}_backup_{number:D2}{ext}";
        Assert.Equal("MySong_backup_03.chord", fileName);
    }

    [Fact]
    public void ProjectStructure_PathsGeneration_ShouldFormProperStructure()
    {
        var parentDir = @"C:\Users\Test\Documents\ChordLaunchpad Projects";
        var projectName = "FutureBass_Drop";

        var projectDir = Path.Combine(parentDir, projectName);
        var projectFile = Path.Combine(projectDir, $"{projectName}.chord");
        var backupDir = Path.Combine(projectDir, "Project Backup");
        var midiDir = Path.Combine(projectDir, "Chord MIDI");

        Assert.Equal(@"C:\Users\Test\Documents\ChordLaunchpad Projects\FutureBass_Drop", projectDir);
        Assert.Equal(@"C:\Users\Test\Documents\ChordLaunchpad Projects\FutureBass_Drop\FutureBass_Drop.chord", projectFile);
        Assert.Equal(@"C:\Users\Test\Documents\ChordLaunchpad Projects\FutureBass_Drop\Project Backup", backupDir);
        Assert.Equal(@"C:\Users\Test\Documents\ChordLaunchpad Projects\FutureBass_Drop\Chord MIDI", midiDir);
    }
}
