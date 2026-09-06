using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;
using ChordLaunchpad.Audio;
using ChordLaunchpad.Core;
using ChordLaunchpad.Core.Models;

namespace ChordLaunchpad;

public class TemplateItemViewModel : INotifyPropertyChanged
{
    public ProgressionTemplate Template { get; }

    public string Id => Template.Id;
    public string Name => Template.Name;
    public string Category => Template.Category;
    public string MajorCategory => Template.MajorCategory;
    public string Progression => Template.Progression;
    public string Description => Template.Description;

    private bool _isPlaying;
    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (_isPlaying != value)
            {
                _isPlaying = value;
                OnPropertyChanged(nameof(IsPlaying));
                OnPropertyChanged(nameof(PreviewButtonText));
                OnPropertyChanged(nameof(PreviewIconGlyph));
            }
        }
    }

    public string PreviewButtonText => IsPlaying
        ? (LocalizationService.IsEnglish ? "Stop" : "停止")
        : (LocalizationService.IsEnglish ? "Audition" : "試聴");

    public string PreviewIconGlyph => IsPlaying ? "\uE71A" : "\uE768";

    public string ApplyButtonText => LocalizationService.IsEnglish ? "Apply" : "適用";
    public string AppendButtonText => LocalizationService.IsEnglish ? "+ Append" : "+ 追加";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public TemplateItemViewModel(ProgressionTemplate template)
    {
        Template = template;
    }
}

public sealed partial class TemplateDialog : ContentDialog
{
    public event Action<string, bool>? ProgressionApplied;

    private readonly string _key;
    private readonly MusicalMode _mode;
    private readonly PlaybackTone _tone;
    private readonly string? _currentProgression;
    private CancellationTokenSource? _previewCts;
    private bool _isPlayingPreview;
    private string? _previewingTemplateId;
    private TemplateItemViewModel? _currentPlayingVm;
    private bool _isUpdatingList;

    private string AllMajorCategoryLabel => LocalizationService.IsEnglish ? "All (All Genres)" : "すべて (全ジャンル)";
    private string AllSubCategoryLabel => LocalizationService.IsEnglish ? "All" : "すべて";

    public ProgressionTemplate? SelectedTemplate => (TemplateListView.SelectedItem as TemplateItemViewModel)?.Template;

    public TemplateDialog(string key, MusicalMode mode, PlaybackTone tone, string? currentProgression = null)
    {
        InitializeComponent();
        _key = key;
        _mode = mode;
        _tone = tone;
        _currentProgression = currentProgression;

        bool isEn = LocalizationService.IsEnglish;
        Title = isEn
            ? $"Popular Chord Progression Templates (Key: {_key} {_mode})"
            : $"定番・ジャンル別コード進行テンプレート (Key: {_key} {_mode})";
        CloseButtonText = isEn ? "Close" : "閉じる";

        MajorCategoryLabel.Text = isEn ? "Main Category" : "大分類";
        SubCategoryLabel.Text = isEn ? "Subgenre" : "サブジャンル";

        // 新規追加・削除 UI のローカライズ
        AddTemplateButtonText.Text = isEn ? "+ New" : "+ 新規追加";
        DeleteButtonText.Text = isEn ? "Delete" : "削除";
        FlyoutTitleText.Text = isEn ? "Register New Template" : "新規テンプレートの登録";
        NewTemplateNameBox.Header = isEn ? "Template Name" : "テンプレート名";
        NewTemplateNameBox.PlaceholderText = isEn ? "e.g.: My Chorus Progression" : "例: マイ進行 Aメロ";
        NewTemplateMajorBox.Header = isEn ? "Main Category" : "大分類";
        NewTemplateMajorBox.Text = isEn ? "👤 User Custom" : "👤 ユーザー定義";
        NewTemplateCategoryBox.Header = isEn ? "Subgenre" : "サブジャンル";
        NewTemplateCategoryBox.Text = isEn ? "Custom" : "カスタム";
        NewTemplateProgressionBox.Header = isEn ? "Chord Progression (Space-separated)" : "コード進行 (半角スペース区切り)";
        NewTemplateProgressionBox.PlaceholderText = isEn ? "e.g.: IVmaj7 V7 vim7 I" : "例: IVmaj7 V7 vim7 I";
        InsertCurrentProgressionButton.Content = isEn ? "Import Current Timeline" : "現在のタイムラインを取り込む";
        NewTemplateDescBox.Header = isEn ? "Description (Optional)" : "説明 (任意)";
        NewTemplateDescBox.PlaceholderText = isEn ? "Progression notes or characteristics" : "進行の特徴やメモ";
        ConfirmAddButton.Content = isEn ? "Save & Register" : "登録して保存";

        if (!string.IsNullOrWhiteSpace(_currentProgression))
        {
            NewTemplateProgressionBox.Text = _currentProgression;
        }

        ReloadCategoriesAndTemplates(selectTemplateId: null);

        Closed += (_, _) => StopPreview();
        Loaded += (_, _) => TemplateListView.Focus(FocusState.Programmatic);
    }

    private void ReloadCategoriesAndTemplates(string? selectTemplateId)
    {
        _isUpdatingList = true;
        try
        {
            var allTemplates = UserTemplateManager.Instance.GetAllTemplates();

            // 大分類リストの構築
            var majorCategories = new List<string> { AllMajorCategoryLabel };
            majorCategories.AddRange(allTemplates
                .Select(t => t.MajorCategory)
                .Where(mc => !string.IsNullOrEmpty(mc))
                .Distinct());

            MajorCategoryComboBox.ItemsSource = majorCategories;

            // 選択すべき大分類の特定
            string selectedMajor = AllMajorCategoryLabel;
            string? targetSubCategory = null;

            if (!string.IsNullOrEmpty(selectTemplateId))
            {
                var target = allTemplates.FirstOrDefault(t => t.Id == selectTemplateId);
                if (target != null)
                {
                    if (majorCategories.Contains(target.MajorCategory))
                    {
                        selectedMajor = target.MajorCategory;
                    }
                    targetSubCategory = target.Category;
                }
            }

            MajorCategoryComboBox.SelectedItem = selectedMajor;

            // サブジャンルの構築
            UpdateSubCategoriesAndTemplates(selectedMajor, targetSubCategory, selectTemplateId);
        }
        finally
        {
            _isUpdatingList = false;
        }
    }

    private void UpdateSubCategoriesAndTemplates(string selectedMajor, string? preferredSubCategory, string? targetTemplateId)
    {
        var allTemplates = UserTemplateManager.Instance.GetAllTemplates();
        var subCategories = new List<string> { AllSubCategoryLabel };

        if (selectedMajor == AllMajorCategoryLabel)
        {
            subCategories.AddRange(allTemplates.Select(t => t.Category).Distinct());
        }
        else
        {
            subCategories.AddRange(allTemplates
                .Where(t => t.MajorCategory == selectedMajor)
                .Select(t => t.Category)
                .Distinct());
        }

        CategoryListView.ItemsSource = subCategories;

        string chosenSub = AllSubCategoryLabel;
        if (!string.IsNullOrEmpty(preferredSubCategory) && subCategories.Contains(preferredSubCategory))
        {
            chosenSub = preferredSubCategory;
        }
        CategoryListView.SelectedItem = chosenSub;

        // テンプレート一覧の絞り込みと反映
        UpdateTemplateList(selectedMajor, chosenSub, targetTemplateId);
    }

    private void UpdateTemplateList(string selectedMajor, string selectedSub, string? targetTemplateId)
    {
        IEnumerable<ProgressionTemplate> query = UserTemplateManager.Instance.GetAllTemplates();

        if (selectedMajor != AllMajorCategoryLabel)
        {
            query = query.Where(t => t.MajorCategory == selectedMajor);
        }

        if (selectedSub != AllSubCategoryLabel)
        {
            query = query.Where(t => t.Category == selectedSub);
        }

        var items = query.Select(t =>
        {
            var vm = new TemplateItemViewModel(t);
            if (t.Id == _previewingTemplateId)
            {
                vm.IsPlaying = true;
                _currentPlayingVm = vm;
            }
            return vm;
        }).ToList();

        TemplateListView.ItemsSource = items;

        TemplateItemViewModel? toSelect = null;
        if (!string.IsNullOrEmpty(targetTemplateId))
        {
            toSelect = items.FirstOrDefault(t => t.Id == targetTemplateId);
        }
        toSelect ??= items.FirstOrDefault();

        TemplateListView.SelectedItem = toSelect;
        if (toSelect != null)
        {
            TemplateListView.ScrollIntoView(toSelect);
        }

        UpdateSelectedLabel(toSelect?.Template);
    }

    private void MajorCategoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingList) return;
        if (MajorCategoryComboBox.SelectedItem is not string selectedMajor) return;

        StopPreview();

        _isUpdatingList = true;
        try
        {
            UpdateSubCategoriesAndTemplates(selectedMajor, preferredSubCategory: null, targetTemplateId: null);
        }
        finally
        {
            _isUpdatingList = false;
        }
    }

    private void CategoryListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingList) return;

        StopPreview();

        var selectedMajor = MajorCategoryComboBox.SelectedItem as string ?? AllMajorCategoryLabel;
        var selectedSub = CategoryListView.SelectedItem as string ?? AllSubCategoryLabel;

        _isUpdatingList = true;
        try
        {
            UpdateTemplateList(selectedMajor, selectedSub, targetTemplateId: null);
        }
        finally
        {
            _isUpdatingList = false;
        }
    }

    private void TemplateListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingList) return;

        var selectedVm = TemplateListView.SelectedItem as TemplateItemViewModel;
        UpdateSelectedLabel(selectedVm?.Template);
    }

    private void TemplateListView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (TemplateListView.SelectedItem is TemplateItemViewModel vm)
        {
            ApplyAndClose(vm.Progression, isAppend: false);
        }
    }

    private void ItemPreviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is TemplateItemViewModel vm)
        {
            TogglePreview(vm);
        }
    }

    private void ItemApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is TemplateItemViewModel vm)
        {
            ApplyAndClose(vm.Progression, isAppend: false);
        }
    }

    private void ItemAppendButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is TemplateItemViewModel vm)
        {
            ApplyAndClose(vm.Progression, isAppend: true);
        }
    }

    private void UpdateSelectedLabel(ProgressionTemplate? template)
    {
        bool isEn = LocalizationService.IsEnglish;
        if (template != null)
        {
            SelectedTemplateLabel.Text = isEn
                ? $"Selected: {template.Name} ({template.Progression})"
                : $"選択中: {template.Name} ({template.Progression})";

            // ユーザー定義テンプレートのみ削除ボタンを有効化
            bool isUser = UserTemplateManager.Instance.IsUserTemplate(template.Id);
            if (DeleteTemplateButton != null)
            {
                DeleteTemplateButton.IsEnabled = isUser;
                ToolTipService.SetToolTip(DeleteTemplateButton, isUser
                    ? (isEn ? "Delete this custom template" : "このカスタムテンプレートを削除")
                    : (isEn ? "Built-in templates cannot be deleted" : "組み込みテンプレートは削除できません"));
            }
        }
        else
        {
            SelectedTemplateLabel.Text = isEn ? "Selected: None" : "選択中: なし";
            if (DeleteTemplateButton != null) DeleteTemplateButton.IsEnabled = false;
        }
    }

    private void TogglePreview(TemplateItemViewModel vm)
    {
        if (_isPlayingPreview && _previewingTemplateId == vm.Id)
        {
            StopPreview();
            return;
        }

        StartPreview(vm);
    }

    private async void StartPreview(TemplateItemViewModel vm)
    {
        StopPreview();
        _previewCts = new CancellationTokenSource();
        var ct = _previewCts.Token;

        _isPlayingPreview = true;
        _previewingTemplateId = vm.Id;
        _currentPlayingVm = vm;
        vm.IsPlaying = true;

        var result = MusicEngine.ParseProgression(vm.Progression, _key, _mode, "1 bar", "4/4", 4);
        var chords = result.Chords;
        if (chords.Count == 0)
        {
            // フォールバック: C Major で再試行
            var fallback = MusicEngine.ParseProgression(vm.Progression, "C", MusicalMode.Major, "1 bar", "4/4", 4);
            chords = fallback.Chords;
        }

        try
        {
            foreach (var chord in chords)
            {
                if (ct.IsCancellationRequested) break;
                var notes = MusicEngine.MidiNoteNumbers(chord);
                AudioEngine.Instance.PlayNotes(notes, 550, _tone);
                await Task.Delay(550, ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                StopPreview();
            }
        }
    }

    private void ApplyAndClose(string progression, bool isAppend)
    {
        StopPreview();
        ProgressionApplied?.Invoke(progression, isAppend);
        Hide();
    }

    private void StopPreview()
    {
        _isPlayingPreview = false;
        _previewingTemplateId = null;
        if (_currentPlayingVm != null)
        {
            _currentPlayingVm.IsPlaying = false;
            _currentPlayingVm = null;
        }

        if (_previewCts != null)
        {
            try { _previewCts.Cancel(); } catch { }
            _previewCts = null;
        }
        AudioEngine.Instance.StopAll();
    }

    // ==========================================
    // テンプレート追加・削除処理
    // ==========================================

    private void InsertCurrentProgression_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_currentProgression))
        {
            NewTemplateProgressionBox.Text = _currentProgression;
        }
    }

    private void ConfirmAddTemplate_Click(object sender, RoutedEventArgs e)
    {
        bool isEn = LocalizationService.IsEnglish;
        var name = NewTemplateNameBox.Text.Trim();
        var prog = NewTemplateProgressionBox.Text.Trim();
        var major = NewTemplateMajorBox.Text.Trim();
        var cat = NewTemplateCategoryBox.Text.Trim();
        var desc = NewTemplateDescBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            AddErrorText.Text = isEn ? "Template name is required." : "テンプレート名を入力してください。";
            return;
        }

        if (string.IsNullOrWhiteSpace(prog))
        {
            AddErrorText.Text = isEn ? "Chord progression is required." : "コード進行を入力してください。";
            return;
        }

        var parseCheck = MusicEngine.ParseProgression(prog, _key, _mode, "1 bar", "4/4", 4);
        if (parseCheck.Chords.Count == 0)
        {
            parseCheck = MusicEngine.ParseProgression(prog, "C", MusicalMode.Major, "1 bar", "4/4", 4);
            if (parseCheck.Chords.Count == 0)
            {
                AddErrorText.Text = isEn ? "Invalid chord progression." : "有効なコード進行を解釈できませんでした。";
                return;
            }
        }

        AddErrorText.Text = "";

        if (string.IsNullOrWhiteSpace(major))
        {
            major = isEn ? "👤 User Custom" : "👤 ユーザー定義";
        }
        if (string.IsNullOrWhiteSpace(cat))
        {
            cat = isEn ? "Custom" : "カスタム";
        }

        var newTemplate = new ProgressionTemplate(
            Id: $"user-{Guid.NewGuid().ToString("N")[..8]}",
            Category: cat,
            Name: name,
            Progression: prog,
            Description: desc,
            MajorCategory: major
        );

        UserTemplateManager.Instance.AddTemplate(newTemplate);
        AddTemplateFlyout.Hide();

        // フォームをリセット
        NewTemplateNameBox.Text = "";
        NewTemplateDescBox.Text = "";

        // 一覧を再読み込みして追加した項目を選択
        ReloadCategoriesAndTemplates(newTemplate.Id);
    }

    private void DeleteTemplate_Click(object sender, RoutedEventArgs e)
    {
        var target = SelectedTemplate ?? TemplateListView.SelectedItem as ProgressionTemplate;
        if (target == null) return;
        if (!UserTemplateManager.Instance.IsUserTemplate(target.Id)) return;

        StopPreview();
        var idToDelete = target.Id;
        UserTemplateManager.Instance.DeleteTemplate(idToDelete);

        ReloadCategoriesAndTemplates(selectTemplateId: null);
    }
}
