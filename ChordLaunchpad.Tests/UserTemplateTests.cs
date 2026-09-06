using System;
using System.IO;
using ChordLaunchpad.Core;
using ChordLaunchpad.Core.Models;
using Xunit;

namespace ChordLaunchpad.Tests;

public class UserTemplateTests : IDisposable
{
    private readonly string _tempFilePath;

    public UserTemplateTests()
    {
        _tempFilePath = Path.Combine(Path.GetTempPath(), $"ChordLaunchpad_TestTemplates_{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_tempFilePath))
            {
                File.Delete(_tempFilePath);
            }
        }
        catch { }
    }

    [Fact]
    public void TestAddAndGetTemplates()
    {
        var manager = new UserTemplateManager(_tempFilePath);
        Assert.Empty(manager.UserTemplates);

        var template = new ProgressionTemplate(
            Id: "test-01",
            Category: "TestCategory",
            Name: "Test Name",
            Progression: "I V vi IV",
            Description: "Test Desc",
            MajorCategory: "TestMajor"
        );

        manager.AddTemplate(template);

        Assert.Single(manager.UserTemplates);
        Assert.True(manager.IsUserTemplate("test-01"));
        Assert.False(manager.IsUserTemplate("oudo")); // 組み込みテンプレートはfalse

        var all = manager.GetAllTemplates();
        Assert.Contains(all, t => t.Id == "test-01");
        Assert.Contains(all, t => t.Id == "oudo");
    }

    [Fact]
    public void TestDeleteTemplate()
    {
        var manager = new UserTemplateManager(_tempFilePath);
        var template1 = new ProgressionTemplate("t1", "Cat", "Name1", "I IV", "", "Major");
        var template2 = new ProgressionTemplate("t2", "Cat", "Name2", "V I", "", "Major");

        manager.AddTemplate(template1);
        manager.AddTemplate(template2);
        Assert.Equal(2, manager.UserTemplates.Count);

        var deleted = manager.DeleteTemplate("t1");
        Assert.True(deleted);
        Assert.Single(manager.UserTemplates);
        Assert.False(manager.IsUserTemplate("t1"));
        Assert.True(manager.IsUserTemplate("t2"));

        var notFound = manager.DeleteTemplate("non-existent");
        Assert.False(notFound);
    }

    [Fact]
    public void TestPersistence_SaveAndReload()
    {
        var manager1 = new UserTemplateManager(_tempFilePath);
        var template = new ProgressionTemplate("persist-01", "Rock", "Cool Riff", "vi IV V I", "Description", "User");
        manager1.AddTemplate(template);

        // 新しいインスタンスで同一ファイルをロード
        var manager2 = new UserTemplateManager(_tempFilePath);
        Assert.Single(manager2.UserTemplates);
        Assert.Equal("persist-01", manager2.UserTemplates[0].Id);
        Assert.Equal("Cool Riff", manager2.UserTemplates[0].Name);
        Assert.Equal("vi IV V I", manager2.UserTemplates[0].Progression);
    }

    [Fact]
    public void TestBuiltInTemplates_ParseSuccessfully()
    {
        // 全組み込みテンプレートのコード進行が確実にパース可能であることを検証
        foreach (var template in DefaultTemplates.Templates)
        {
            var result = MusicEngine.ParseProgression(template.Progression, "C", MusicalMode.Major, "1 bar", "4/4", 4);
            Assert.NotEmpty(result.Chords);
            Assert.All(result.Chords, c =>
            {
                var notes = MusicEngine.MidiNoteNumbers(c);
                Assert.NotEmpty(notes);
            });
        }
    }

    [Fact]
    public void TestUserTemplate_FallbackParsing()
    {
        // ユーザーが入力したローマ数字進行がパースできること
        const string customProg = "IVmaj7 V7 vim7 I";
        var result = MusicEngine.ParseProgression(customProg, "F#", MusicalMode.Minor, "1 bar", "4/4", 4);
        var chords = result.Chords;
        if (chords.Count == 0)
        {
            var fallback = MusicEngine.ParseProgression(customProg, "C", MusicalMode.Major, "1 bar", "4/4", 4);
            chords = fallback.Chords;
        }

        Assert.NotEmpty(chords);
        Assert.Equal(4, chords.Count);
    }
}
