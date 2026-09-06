using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Microsoft.UI.Xaml.Media;
using Line = Microsoft.UI.Xaml.Shapes.Line;
using ChordLaunchpad.Audio;
using ChordLaunchpad.Core;
using ChordLaunchpad.Core.Models;
using ChordLaunchpad.Export;

namespace ChordLaunchpad;

public sealed partial class MainPage : Page
{
    private PlaybackTone _currentTone = PlaybackTone.Piano;
    private List<ChordBlock> _currentChords = new();
    private readonly ObservableCollection<ChordCardItem> _chordCardItems = new();
    private readonly ObservableCollection<SectionMarker> _sectionMarkers = new();
    private string? _selectedChordId;
    private ChordBlock? _clipboardChord;
    private CancellationTokenSource? _playbackCts;
    private bool _isUpdatingInternally;
    private string _activeKey = "C";
    private double _pixelsPerBeat = 40.0;
    private bool _isRightPanelVisible = true;

    // 初期化完了フラグおよびリサイズデバウンスタイマー
    private bool _isInitialized;
    private readonly DispatcherTimer _resizeDebounceTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    // ガイドパネル用キャッシュ
    private string? _cachedGuideKey;
    private MusicalMode? _cachedGuideMode;
    private bool _pendingGuideUpdate;

    // アンドゥ・リドゥ履歴スタック (最大50世代でクリッピング)
    private const int MaxUndoHistory = 50;
    private readonly Stack<TimelineHistoryState> _undoStack = new();
    private readonly Stack<TimelineHistoryState> _redoStack = new();

    private string? _currentProjectPath;
    private readonly DispatcherTimer _autoSaveTimer = new();

    // グリッドスナップ単位（既定: 1拍）およびリサイズドラッグ状態
    private double _currentGridBeats = 1.0;
    private bool _isResizingCard;
    private bool _isLeftResize;
    private string? _resizingChordId;
    private double _resizeStartPointerX;
    private double _resizeInitialBeats = 1.0;
    private double _resizePrevInitialBeats = 1.0;

    // タイムライン背景グリッド描画キャッシュ (不要な再描画とブラシ再生成の徹底防止)
    private double _lastGridMaxBeats;
    private double _lastGridStepBeats;
    private double _lastGridPixelsPerBeat;
    private bool? _lastGridIsDark;
    private SolidColorBrush? _cachedGridBgBrush;
    private SolidColorBrush? _cachedRulerBgBrush;
    private SolidColorBrush? _cachedRulerBottomLineBrush;
    private SolidColorBrush? _cachedTextBrush;
    private SolidColorBrush? _cachedRulerMeasureTickBrush;
    private SolidColorBrush? _cachedRulerBeatTickBrush;
    private SolidColorBrush? _cachedGridMeasureLineBrush;
    private SolidColorBrush? _cachedGridBeatLineBrush;
    private SolidColorBrush? _cachedGridSubLineBrush;

    public MainPage()
    {
        InitializeComponent();

        TimelineItemsControl.ItemsSource = _chordCardItems;
        MarkersItemsControl.ItemsSource = _sectionMarkers;

        // キー一覧の初期化
        foreach (var key in MusicEngine.KeyCandidates)
        {
            KeyComboBox.Items.Add(key);
        }
        KeyComboBox.SelectedItem = "C";

        // 初期テキスト入力
        InputTextBox.Text = "4mas 3svn 6mis 1svn";

        // リサイズデバウンスタイマーの設定 (連続リサイズ中の過剰なCanvas再描画を防止)
        _resizeDebounceTimer.Tick += (_, _) =>
        {
            _resizeDebounceTimer.Stop();
            UpdateTimelineBackgroundGrid();
        };

        // タイムライン描画の初期化 (Loaded イベントで1度だけまとめて初回構築)
        Loaded += (_, _) =>
        {
            if (!_isInitialized)
            {
                _isInitialized = true;
                ApplyInput(saveUndo: false);
            }
            else
            {
                UpdateTimelineBackgroundGrid();
            }
        };

        SizeChanged += (_, _) =>
        {
            if (!_isInitialized) return;
            _resizeDebounceTimer.Stop();
            _resizeDebounceTimer.Start();
        };

        ActualThemeChanged += (_, _) =>
        {
            _lastGridIsDark = null; // テーマ変更時はブラシ再生成
            UpdateTimelineBackgroundGrid();
        };

        Unloaded += (_, _) =>
        {
            ChordLaunchpad.Core.LocalizationService.LanguageChanged -= ApplyLanguage;
            _autoSaveTimer.Stop();
            _resizeDebounceTimer.Stop();
        };

        // 自動保存タイマーの初期化
        InitAutoSaveTimer();

        // タイムライン領域のホイール操作 (Shift+ホイール横スクロール & Ctrl+ホイールズーム) を確実に捕捉
        TimelineOuterBorder.AddHandler(
            UIElement.PointerWheelChangedEvent,
            new PointerEventHandler(Timeline_PointerWheelChanged),
            handledEventsToo: true);
        TimelineScrollViewer.AddHandler(
            UIElement.PointerWheelChangedEvent,
            new PointerEventHandler(Timeline_PointerWheelChanged),
            handledEventsToo: true);

        // UI言語設定の適用および言語変更イベント購読
        ChordLaunchpad.Core.LocalizationService.LanguageChanged += ApplyLanguage;
        ApplyLanguage();
    }

    private string CurrentKey => (KeyComboBox.SelectedItem as string) ?? "C";

    private MusicalMode CurrentMode =>
        (ModeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "Minor"
            ? MusicalMode.Minor
            : MusicalMode.Major;

    private int CurrentBpm => (int)Math.Clamp(BpmBox.Value, 40, 300);

    private BassAdditionMode CurrentBassAddition
    {
        get
        {
            var tag = (BassComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            return tag switch
            {
                "One" => BassAdditionMode.One,
                "Two" => BassAdditionMode.Two,
                "Both" => BassAdditionMode.Both,
                _ => BassAdditionMode.None
            };
        }
    }

    private OpenVoicingMode CurrentOpenVoicing => OpenVoicingMode.Closed;

    private ChordBlock? SelectedChord => _currentChords.FirstOrDefault(c => c.Id == _selectedChordId);

    // ==========================================
    // アンドゥ・リドゥ履歴管理
    // ==========================================

    private void SaveState()
    {
        var snapshot = new TimelineHistoryState(
            _currentChords.Select(c => c with { }).ToList(),
            _sectionMarkers.Select(m => m with { }).ToList(),
            _selectedChordId,
            _activeKey,
            CurrentMode
        );
        _undoStack.Push(snapshot);
        _redoStack.Clear();

        // 履歴上限（MaxUndoHistory = 50）を超えた場合は最古の世代を切り詰めてメモリ肥大化を防止
        if (_undoStack.Count > MaxUndoHistory)
        {
            var items = _undoStack.ToArray();
            _undoStack.Clear();
            for (int i = MaxUndoHistory - 1; i >= 0; i--)
            {
                _undoStack.Push(items[i]);
            }
        }
    }

    private void Undo()
    {
        if (_undoStack.Count == 0) return;

        var currentState = new TimelineHistoryState(
            _currentChords.Select(c => c with { }).ToList(),
            _sectionMarkers.Select(m => m with { }).ToList(),
            _selectedChordId,
            _activeKey,
            CurrentMode
        );
        _redoStack.Push(currentState);

        var prev = _undoStack.Pop();
        RestoreState(prev);
        StatusTextBlock.Text = "アンドゥ (元に戻しました)";
    }

    private void Redo()
    {
        if (_redoStack.Count == 0) return;

        var currentState = new TimelineHistoryState(
            _currentChords.Select(c => c with { }).ToList(),
            _sectionMarkers.Select(m => m with { }).ToList(),
            _selectedChordId,
            _activeKey,
            CurrentMode
        );
        _undoStack.Push(currentState);

        var next = _redoStack.Pop();
        RestoreState(next);
        StatusTextBlock.Text = "リドゥ (やり直しました)";
    }

    private void SetSectionMarkers(IEnumerable<SectionMarker>? markers)
    {
        _sectionMarkers.Clear();
        if (markers != null)
        {
            foreach (var m in markers.OrderBy(m => m.InsertIndex))
            {
                _sectionMarkers.Add(m);
            }
        }
    }

    private void RestoreState(TimelineHistoryState state)
    {
        _currentChords = state.Chords.Select(c => c with { }).ToList();
        SetSectionMarkers(state.Markers);
        _selectedChordId = state.SelectedChordId;
        _activeKey = state.Key;

        _isUpdatingInternally = true;
        try
        {
            KeyComboBox.SelectedItem = _activeKey;
        }
        finally
        {
            _isUpdatingInternally = false;
        }

        RefreshTimeline();
        SyncInputTextBoxFromChords();
        UpdateGuideAndSuggestions();
    }

    private void UndoButton_Click(object sender, RoutedEventArgs e)
    {
        Undo();
        RemoveFocusFromTopBar();
    }

    private void RedoButton_Click(object sender, RoutedEventArgs e)
    {
        Redo();
        RemoveFocusFromTopBar();
    }

    // ==========================================
    // タイムライン反映・描画
    // ==========================================

    private double CalculateCardWidth(string duration, bool dotted)
    {
        var beats = MusicEngine.ChordDurationBeats(duration, "4/4", dotted);
        return Math.Max(20.0, beats * _pixelsPerBeat);
    }

    private void ApplyInput(bool saveUndo = true)
    {
        if (_isUpdatingInternally) return;

        var raw = InputTextBox.Text;
        if (string.IsNullOrWhiteSpace(raw))
        {
            if (saveUndo) SaveState();
            _currentChords.Clear();
            _selectedChordId = null;
            RefreshTimeline();
            UpdateGuideAndSuggestions();
            return;
        }

        var result = MusicEngine.ParseProgression(
            raw,
            CurrentKey,
            CurrentMode,
            "1 bar",
            "4/4",
            4
        );

        if (saveUndo) SaveState();
        _currentChords = result.Chords.Select(c => c with
        {
            CardWidth = CalculateCardWidth(c.Duration, c.Dotted)
        }).ToList();

        if (_currentChords.Count > 0)
        {
            if (string.IsNullOrEmpty(_selectedChordId) || !_currentChords.Any(c => c.Id == _selectedChordId))
            {
                _selectedChordId = _currentChords[0].Id;
            }
            RefreshTimeline();
            SelectChord(_selectedChordId, preview: false);
        }
        else
        {
            _selectedChordId = null;
            RefreshTimeline();
        }

        bool isEn = ChordLaunchpad.Core.LocalizationService.IsEnglish;
        if (result.Errors.Count > 0)
        {
            StatusTextBlock.Text = isEn
                ? $"Parse Warning: {result.Errors[0].Token} - {result.Errors[0].Message}"
                : $"解析警告: {result.Errors[0].Token} - {result.Errors[0].Message}";
        }
        else
        {
            StatusTextBlock.Text = isEn
                ? $"Applied {_currentChords.Count} chords (Key: {result.ResolvedKey} {result.ResolvedMode})"
                : $"{_currentChords.Count} 個のコードを反映しました (Key: {result.ResolvedKey} {result.ResolvedMode})";
        }

        UpdateGuideAndSuggestions();
    }

    private void RefreshTimeline()
    {
        // 1. 各コードの幅を更新
        for (var i = 0; i < _currentChords.Count; i++)
        {
            _currentChords[i] = _currentChords[i] with
            {
                CardWidth = CalculateCardWidth(_currentChords[i].Duration, _currentChords[i].Dotted)
            };
        }

        // 2. _chordCardItems のインプレース差分同期 (全件破棄・全件再生成を防止し、60fpsを維持)
        int targetCount = _currentChords.Count;
        int currentCount = _chordCardItems.Count;
        int commonCount = Math.Min(targetCount, currentCount);

        for (int i = 0; i < commonCount; i++)
        {
            var chord = _currentChords[i];
            var item = _chordCardItems[i];
            bool isSel = (chord.Id == _selectedChordId);

            if (item.Model != chord)
            {
                item.Model = chord;
            }
            if (Math.Abs(item.CardWidth - chord.CardWidth) > 0.001)
            {
                item.CardWidth = chord.CardWidth;
            }
            if (item.IsSelected != isSel)
            {
                item.IsSelected = isSel;
            }
        }

        while (_chordCardItems.Count > targetCount)
        {
            _chordCardItems.RemoveAt(_chordCardItems.Count - 1);
        }

        for (int i = currentCount; i < targetCount; i++)
        {
            var chord = _currentChords[i];
            bool isSel = (chord.Id == _selectedChordId);
            _chordCardItems.Add(new ChordCardItem(chord, isSel, chord.CardWidth));
        }

        bool isEn = ChordLaunchpad.Core.LocalizationService.IsEnglish;
        ChordCountBadge.Text = isEn
            ? (_currentChords.Count == 1 ? "1 chord" : $"{_currentChords.Count} chords")
            : $"{_currentChords.Count} 個";

        var selected = SelectedChord;
        if (selected != null)
        {
            UpdateInspector(selected);
        }
        else
        {
            InspectorSymbolText.Text = "-";
            InspectorDurationText.Text = "-";
            InspectorInversionText.Text = isEn ? "Root" : "基本";
            InspectorNotesText.Text = "-";
        }

        UpdateTimelineBackgroundGrid();
    }

    private void UpdateTimelineBackgroundGrid()
    {
        if (TimelineGridCanvas == null || TimelineRulerCanvas == null) return;

        double totalBeats = 0.0;
        foreach (var chord in _currentChords)
        {
            totalBeats += MusicEngine.ChordDurationBeats(chord.Duration, "4/4", chord.Dotted);
        }

        // 最低でも16小節（64拍）または現在の小節数 + 6小節分描画
        var displayBars = Math.Max(16, (int)Math.Ceiling(totalBeats / 4.0) + 6);
        var maxBeats = displayBars * 4.0;
        var stepBeats = _currentGridBeats <= 0 ? 1.0 : _currentGridBeats;
        var isDark = ActualTheme == ElementTheme.Dark;

        var canvasWidth = maxBeats * _pixelsPerBeat;
        TimelineGridCanvas.Width = canvasWidth;
        TimelineRulerCanvas.Width = canvasWidth;
        if (TimelineContentGrid != null)
        {
            TimelineContentGrid.Width = Math.Max(canvasWidth, TimelineScrollViewer?.ActualWidth ?? canvasWidth);
        }

        // パラメータが前回と全く同一の場合は Canvas 再描画をスキップ（大量の UIElement 再生成を完全回避）
        bool needFullRedraw = (_lastGridMaxBeats != maxBeats) ||
                              (_lastGridStepBeats != stepBeats) ||
                              (Math.Abs(_lastGridPixelsPerBeat - _pixelsPerBeat) > 0.001) ||
                              (_lastGridIsDark != isDark) ||
                              (TimelineGridCanvas.Children.Count == 0);

        if (!needFullRedraw)
        {
            return;
        }

        _lastGridMaxBeats = maxBeats;
        _lastGridStepBeats = stepBeats;
        _lastGridPixelsPerBeat = _pixelsPerBeat;
        _lastGridIsDark = isDark;

        // ブラシのキャッシュ初期化・更新（テーマ切り替え時のみ再生成）
        if (_cachedGridBgBrush == null || _lastGridIsDark != isDark)
        {
            _cachedGridBgBrush = new SolidColorBrush(isDark
                ? Windows.UI.Color.FromArgb(255, 24, 28, 36)
                : Windows.UI.Color.FromArgb(255, 248, 250, 252));
            _cachedRulerBgBrush = new SolidColorBrush(isDark
                ? Windows.UI.Color.FromArgb(255, 32, 38, 48)
                : Windows.UI.Color.FromArgb(255, 228, 233, 242));
            _cachedRulerBottomLineBrush = new SolidColorBrush(isDark
                ? Windows.UI.Color.FromArgb(255, 55, 62, 72)
                : Windows.UI.Color.FromArgb(255, 194, 203, 216));
            _cachedTextBrush = new SolidColorBrush(isDark
                ? Windows.UI.Color.FromArgb(220, 200, 210, 225)
                : Windows.UI.Color.FromArgb(255, 30, 41, 59));
            _cachedRulerMeasureTickBrush = new SolidColorBrush(isDark
                ? Windows.UI.Color.FromArgb(255, 75, 85, 100)
                : Windows.UI.Color.FromArgb(255, 148, 163, 184));
            _cachedRulerBeatTickBrush = new SolidColorBrush(isDark
                ? Windows.UI.Color.FromArgb(140, 65, 75, 90)
                : Windows.UI.Color.FromArgb(255, 203, 213, 225));
            _cachedGridMeasureLineBrush = new SolidColorBrush(isDark
                ? Windows.UI.Color.FromArgb(180, 80, 90, 105)
                : Windows.UI.Color.FromArgb(255, 160, 174, 192));
            _cachedGridBeatLineBrush = new SolidColorBrush(isDark
                ? Windows.UI.Color.FromArgb(85, 65, 75, 90)
                : Windows.UI.Color.FromArgb(255, 203, 213, 225));
            _cachedGridSubLineBrush = new SolidColorBrush(isDark
                ? Windows.UI.Color.FromArgb(40, 65, 75, 90)
                : Windows.UI.Color.FromArgb(255, 226, 232, 240));
        }

        // ==========================================
        // TimelineGridCanvas のプーリング更新（Clearせず既存要素のプロパティを再利用）
        // ==========================================
        // 0: トラック全体の背景矩形
        if (TimelineGridCanvas.Children.Count > 0 && TimelineGridCanvas.Children[0] is Microsoft.UI.Xaml.Shapes.Rectangle existingGridBg)
        {
            existingGridBg.Width = canvasWidth;
            existingGridBg.Height = 110;
            existingGridBg.Fill = _cachedGridBgBrush;
        }
        else
        {
            if (TimelineGridCanvas.Children.Count > 0) TimelineGridCanvas.Children.Clear();
            TimelineGridCanvas.Children.Add(new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                Width = canvasWidth,
                Height = 110,
                Fill = _cachedGridBgBrush
            });
        }

        int gridLineIdx = 1;

        // ==========================================
        // TimelineRulerCanvas のプーリング更新
        // ==========================================
        // 0: ルーラー背景
        if (TimelineRulerCanvas.Children.Count > 0 && TimelineRulerCanvas.Children[0] is Microsoft.UI.Xaml.Shapes.Rectangle existingRulerBg)
        {
            existingRulerBg.Width = canvasWidth;
            existingRulerBg.Height = 22;
            existingRulerBg.Fill = _cachedRulerBgBrush;
        }
        else
        {
            if (TimelineRulerCanvas.Children.Count > 0) TimelineRulerCanvas.Children.Clear();
            TimelineRulerCanvas.Children.Add(new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                Width = canvasWidth,
                Height = 22,
                Fill = _cachedRulerBgBrush
            });
        }

        // 1: ルーラー下境界線
        if (TimelineRulerCanvas.Children.Count > 1 && TimelineRulerCanvas.Children[1] is Line existingBottomLine)
        {
            existingBottomLine.X1 = 0;
            existingBottomLine.Y1 = 22;
            existingBottomLine.X2 = canvasWidth;
            existingBottomLine.Y2 = 22;
            existingBottomLine.Stroke = _cachedRulerBottomLineBrush;
            existingBottomLine.StrokeThickness = 1.0;
        }
        else
        {
            while (TimelineRulerCanvas.Children.Count > 1)
            {
                TimelineRulerCanvas.Children.RemoveAt(TimelineRulerCanvas.Children.Count - 1);
            }
            TimelineRulerCanvas.Children.Add(new Line
            {
                X1 = 0,
                Y1 = 22,
                X2 = canvasWidth,
                Y2 = 22,
                Stroke = _cachedRulerBottomLineBrush,
                StrokeThickness = 1.0
            });
        }

        int rulerItemIdx = 2;

        // 2. グリッド線 ＆ ルーラー目盛り描画（既存の UIElement を再利用）
        for (double b = 0.0; b <= maxBeats; b += stepBeats)
        {
            var x = b * _pixelsPerBeat;
            var isMeasure = Math.Abs(b % 4.0) < 0.001;
            var isBeat = Math.Abs(b % 1.0) < 0.001;

            if (isMeasure)
            {
                var barNumber = (int)Math.Round(b / 4.0) + 1;
                var barStr = barNumber.ToString();

                // 小節番号テキスト (ルーラー)
                if (rulerItemIdx < TimelineRulerCanvas.Children.Count && TimelineRulerCanvas.Children[rulerItemIdx] is TextBlock tb)
                {
                    tb.Text = barStr;
                    tb.Foreground = _cachedTextBrush;
                    Canvas.SetLeft(tb, x + 4);
                    Canvas.SetTop(tb, 2);
                }
                else
                {
                    var text = new TextBlock
                    {
                        Text = barStr,
                        FontSize = 10,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = _cachedTextBrush
                    };
                    Canvas.SetLeft(text, x + 4);
                    Canvas.SetTop(text, 2);
                    if (rulerItemIdx < TimelineRulerCanvas.Children.Count)
                        TimelineRulerCanvas.Children[rulerItemIdx] = text;
                    else
                        TimelineRulerCanvas.Children.Add(text);
                }
                rulerItemIdx++;

                // ルーラー小節境界線
                SetOrCreateRulerLine(ref rulerItemIdx, x, 0, x, 22, _cachedRulerMeasureTickBrush, 1.5);

                // タイムライン小節線 (背景グリッド: ルーラーからクリップ全域へ貫通)
                SetOrCreateGridLine(ref gridLineIdx, x, 0, x, 110, _cachedGridMeasureLineBrush, 1.5);
            }
            else if (isBeat)
            {
                // 拍目盛り (ルーラー下部に小さなティック)
                SetOrCreateRulerLine(ref rulerItemIdx, x, 14, x, 22, _cachedRulerBeatTickBrush, 1.0);

                // タイムライン拍線 (背景グリッド)
                SetOrCreateGridLine(ref gridLineIdx, x, 22, x, 110, _cachedGridBeatLineBrush, 1.0);
            }
            else
            {
                // サブグリッド線 (0.5拍, 0.25拍等)
                SetOrCreateGridLine(ref gridLineIdx, x, 22, x, 110, _cachedGridSubLineBrush, 0.8);
            }
        }

        // 余剰要素の切り詰め
        while (TimelineGridCanvas.Children.Count > gridLineIdx)
        {
            TimelineGridCanvas.Children.RemoveAt(TimelineGridCanvas.Children.Count - 1);
        }
        while (TimelineRulerCanvas.Children.Count > rulerItemIdx)
        {
            TimelineRulerCanvas.Children.RemoveAt(TimelineRulerCanvas.Children.Count - 1);
        }
    }

    private void SetOrCreateGridLine(ref int index, double x1, double y1, double x2, double y2, SolidColorBrush? brush, double thickness)
    {
        if (index < TimelineGridCanvas.Children.Count && TimelineGridCanvas.Children[index] is Line line)
        {
            line.X1 = x1;
            line.Y1 = y1;
            line.X2 = x2;
            line.Y2 = y2;
            line.Stroke = brush;
            line.StrokeThickness = thickness;
        }
        else
        {
            var newLine = new Line
            {
                X1 = x1,
                Y1 = y1,
                X2 = x2,
                Y2 = y2,
                Stroke = brush,
                StrokeThickness = thickness
            };
            if (index < TimelineGridCanvas.Children.Count)
                TimelineGridCanvas.Children[index] = newLine;
            else
                TimelineGridCanvas.Children.Add(newLine);
        }
        index++;
    }

    private void SetOrCreateRulerLine(ref int index, double x1, double y1, double x2, double y2, SolidColorBrush? brush, double thickness)
    {
        if (index < TimelineRulerCanvas.Children.Count && TimelineRulerCanvas.Children[index] is Line line)
        {
            line.X1 = x1;
            line.Y1 = y1;
            line.X2 = x2;
            line.Y2 = y2;
            line.Stroke = brush;
            line.StrokeThickness = thickness;
        }
        else
        {
            var newLine = new Line
            {
                X1 = x1,
                Y1 = y1,
                X2 = x2,
                Y2 = y2,
                Stroke = brush,
                StrokeThickness = thickness
            };
            if (index < TimelineRulerCanvas.Children.Count)
                TimelineRulerCanvas.Children[index] = newLine;
            else
                TimelineRulerCanvas.Children.Add(newLine);
        }
        index++;
    }

    private void SyncInputTextBoxFromChords()
    {
        _isUpdatingInternally = true;
        try
        {
            InputTextBox.Text = MusicEngine.ProgressionToInput(_currentChords, NotationPreference.Symbol, "4/4", 4);
        }
        finally
        {
            _isUpdatingInternally = false;
        }
    }

    private void SelectChord(string chordId, bool preview = true)
    {
        _selectedChordId = chordId;

        // UIツリーを破棄せず、各アイテムの IsSelected プロパティのみ更新
        foreach (var item in _chordCardItems)
        {
            item.IsSelected = (item.Id == chordId);
        }

        var selected = SelectedChord;
        if (selected != null)
        {
            UpdateInspector(selected);

            // 該当コードのセクションキーを解決してガイドを連動
            var sectionKey = ResolveKeyAtChord(chordId);
            if (!string.IsNullOrEmpty(sectionKey) && sectionKey != _activeKey)
            {
                _activeKey = sectionKey;
                UpdateGuideAndSuggestions();
            }

            if (preview)
            {
                var notes = MusicEngine.MidiNoteNumbers(selected, CurrentBassAddition, CurrentOpenVoicing);
                var durationMs = MusicEngine.ChordDurationBeats(selected.Duration, "4/4", selected.Dotted) * (60_000.0 / CurrentBpm);
                AudioEngine.Instance.PlayNotes(notes, Math.Max(400, durationMs), _currentTone);
                bool isEn = ChordLaunchpad.Core.LocalizationService.IsEnglish;
                StatusTextBlock.Text = isEn
                    ? $"Selected: {selected.Symbol} ({selected.RomanNumeral}) [{string.Join(", ", notes)}]"
                    : $"選択中: {selected.Symbol} ({selected.RomanNumeral}) [{string.Join(", ", notes)}]";
            }

            ScrollChordIntoView(chordId);
        }
    }

    private void ScrollTimelineBy(double deltaX)
    {
        if (TimelineScrollViewer == null) return;
        var maxScroll = Math.Max(0.0, TimelineScrollViewer.ExtentWidth - TimelineScrollViewer.ViewportWidth);
        if (maxScroll <= 0 && TimelineScrollViewer.ScrollableWidth > 0)
        {
            maxScroll = TimelineScrollViewer.ScrollableWidth;
        }
        var current = TimelineScrollViewer.HorizontalOffset;
        var targetOffset = maxScroll > 0 ? Math.Clamp(current + deltaX, 0.0, maxScroll) : Math.Max(0.0, current + deltaX);
        TimelineScrollViewer.ChangeView(targetOffset, null, null, disableAnimation: true);
    }

    private void ScrollChordIntoView(string chordId)
    {
        if (TimelineScrollViewer == null || _currentChords.Count == 0) return;

        double xOffset = 0.0;
        double chordWidth = 0.0;
        bool found = false;

        foreach (var chord in _currentChords)
        {
            var beats = MusicEngine.ChordDurationBeats(chord.Duration, "4/4", chord.Dotted);
            var width = beats * _pixelsPerBeat;
            if (chord.Id == chordId)
            {
                chordWidth = width;
                found = true;
                break;
            }
            xOffset += width;
        }

        if (!found) return;

        var currentScroll = TimelineScrollViewer.HorizontalOffset;
        var viewport = TimelineScrollViewer.ViewportWidth;
        if (viewport <= 0) return;

        var chordLeft = xOffset;
        var chordRight = xOffset + chordWidth;

        // 左側にはみ出している場合
        if (chordLeft < currentScroll)
        {
            var target = Math.Max(0, chordLeft - 24.0);
            TimelineScrollViewer.ChangeView(target, null, null, disableAnimation: false);
        }
        // 右側にはみ出している場合
        else if (chordRight > currentScroll + viewport)
        {
            var target = Math.Max(0, chordRight - viewport + 24.0);
            TimelineScrollViewer.ChangeView(target, null, null, disableAnimation: false);
        }
    }

    private void SelectPreviousChord()
    {
        if (_currentChords.Count == 0) return;
        var idx = _currentChords.FindIndex(c => c.Id == _selectedChordId);
        if (idx < 0)
        {
            idx = 0;
        }
        else if (idx > 0)
        {
            idx--;
        }
        var target = _currentChords[idx];
        SelectChord(target.Id, preview: true);
        ScrollChordIntoView(target.Id);
    }

    private void SelectNextChord()
    {
        if (_currentChords.Count == 0) return;
        var idx = _currentChords.FindIndex(c => c.Id == _selectedChordId);
        if (idx < 0)
        {
            idx = 0;
        }
        else if (idx < _currentChords.Count - 1)
        {
            idx++;
        }
        var target = _currentChords[idx];
        SelectChord(target.Id, preview: true);
        ScrollChordIntoView(target.Id);
    }

    private string ResolveKeyAtChord(string chordId)
    {
        var chordIndex = _currentChords.FindIndex(c => c.Id == chordId);
        if (chordIndex < 0) return CurrentKey;

        var latestMarker = _sectionMarkers
            .Where(m => m.InsertIndex <= chordIndex)
            .OrderByDescending(m => m.InsertIndex)
            .FirstOrDefault();

        return latestMarker?.Key ?? CurrentKey;
    }

    private void UpdateInspector(ChordBlock chord)
    {
        InspectorSymbolText.Text = $"{chord.Symbol} / {chord.RomanNumeral}";
        InspectorDurationText.Text = MusicEngine.DurationLabel(chord.Duration, chord.Dotted);
        InspectorInversionText.Text = MusicEngine.StyleLabel(chord.Inversion);
        InspectorNotesText.Text = string.Join(" ", chord.Notes);
    }

    private void UpdateGuideAndSuggestions()
    {
        if (!_isRightPanelVisible)
        {
            _pendingGuideUpdate = true;
            return;
        }

        var key = _activeKey;
        var mode = CurrentMode;

        // Key または Mode が変化した場合のみダイアトニック・借用・セカンダリードミナントを再バインド（UIスパイク防止）
        if (_cachedGuideKey != key || _cachedGuideMode != mode)
        {
            _cachedGuideKey = key;
            _cachedGuideMode = mode;
            GuideGridView.ItemsSource = MusicEngine.DiatonicChords(key, mode);
            BorrowedGridView.ItemsSource = MusicEngine.ModalInterchangeChords(key, mode);
            SecondaryDominantsGridView.ItemsSource = MusicEngine.SecondaryDominantChords(key, mode);
        }

        // 次候補（Suggestions）は進行（コードリスト）に依存するため更新
        SuggestionsGridView.ItemsSource = MusicEngine.SuggestionSet(_currentChords, key, mode);
    }

    // ==========================================
    // ズームスライダー操作
    // ==========================================

    private void ZoomSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        _pixelsPerBeat = e.NewValue;
        if (IsLoaded)
        {
            foreach (var item in _chordCardItems)
            {
                item.CardWidth = CalculateCardWidth(item.Duration, item.Dotted);
            }
            for (var i = 0; i < _currentChords.Count; i++)
            {
                _currentChords[i] = _currentChords[i] with
                {
                    CardWidth = CalculateCardWidth(_currentChords[i].Duration, _currentChords[i].Dotted)
                };
            }
            UpdateTimelineBackgroundGrid();
        }
    }

    // ==========================================
    // グリッドスナップ ＆ コード長ドラッグリサイズ（両端対応）
    // ==========================================

    private void GridSnapComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GridSnapComboBox?.SelectedItem is ComboBoxItem item &&
            item.Tag is string tagStr &&
            double.TryParse(tagStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var gridVal))
        {
            _currentGridBeats = gridVal;
            if (StatusTextBlock != null)
            {
                StatusTextBlock.Text = $"グリッドを {item.Content} に設定しました";
            }
            if (IsLoaded)
            {
                UpdateTimelineBackgroundGrid();
            }
            RemoveFocusFromTopBar();
        }
    }

    // --- 左端リサイズ (開始点調整・シンコペーション / 前倒し) ---

    private void LeftResizeGrip_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not string chordId) return;

        var chordIdx = _currentChords.FindIndex(c => c.Id == chordId);
        if (chordIdx < 0) return;

        e.Handled = true;
        _isResizingCard = true;
        _isLeftResize = true;
        _resizingChordId = chordId;
        _resizeInitialBeats = MusicEngine.ChordDurationBeats(_currentChords[chordIdx].Duration, "4/4", _currentChords[chordIdx].Dotted);
        _resizePrevInitialBeats = chordIdx > 0
            ? MusicEngine.ChordDurationBeats(_currentChords[chordIdx - 1].Duration, "4/4", _currentChords[chordIdx - 1].Dotted)
            : 0;
        _resizeStartPointerX = e.GetCurrentPoint(this).Position.X;

        element.CapturePointer(e.Pointer);
        SelectChord(chordId, preview: false);
    }

    private void LeftResizeGrip_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isResizingCard || !_isLeftResize || _resizingChordId == null) return;

        e.Handled = true;
        var currentX = e.GetCurrentPoint(this).Position.X;
        var deltaX = currentX - _resizeStartPointerX;
        var deltaBeats = deltaX / _pixelsPerBeat;

        var chordIdx = _currentChords.FindIndex(c => c.Id == _resizingChordId);
        if (chordIdx < 0) return;

        // グリッド単位にスナップ
        var snappedDeltaBeats = Math.Round(deltaBeats / _currentGridBeats) * _currentGridBeats;

        if (chordIdx > 0)
        {
            // 前のコードが存在する場合: 前のコード長と現在のコード長を連動（シンコペーション食い込み）
            // 左にドラッグ (delta < 0) -> 前のコードが短縮、現在のコードが拡大
            var newPrevBeats = _resizePrevInitialBeats + snappedDeltaBeats;
            var newCurrBeats = _resizeInitialBeats - snappedDeltaBeats;

            if (newPrevBeats >= _currentGridBeats && newCurrBeats >= _currentGridBeats)
            {
                var prevDuration = MusicEngine.BeatsToDuration(newPrevBeats, "4/4");
                var currDuration = MusicEngine.BeatsToDuration(newCurrBeats, "4/4");

                var prevWidth = CalculateCardWidth(prevDuration, false);
                var currWidth = CalculateCardWidth(currDuration, false);

                var prevChord = _currentChords[chordIdx - 1] with { Duration = prevDuration, Dotted = false, CardWidth = prevWidth };
                var currChord = _currentChords[chordIdx] with { Duration = currDuration, Dotted = false, CardWidth = currWidth };

                _currentChords[chordIdx - 1] = prevChord;
                _currentChords[chordIdx] = currChord;

                var prevCard = _chordCardItems.FirstOrDefault(c => c.Id == prevChord.Id);
                var currCard = _chordCardItems.FirstOrDefault(c => c.Id == currChord.Id);

                if (prevCard != null) { prevCard.Model = prevChord; prevCard.CardWidth = prevWidth; }
                if (currCard != null) { currCard.Model = currChord; currCard.CardWidth = currWidth; }

                UpdateInspector(currChord);
                StatusTextBlock.Text = $"シンコペーション調整: {_currentChords[chordIdx - 1].Symbol} ({MusicEngine.DurationLabel(prevDuration)}) ◀▶ {currChord.Symbol} ({MusicEngine.DurationLabel(currDuration)})";
            }
        }
        else
        {
            // 先頭コードの場合: 左端の移動により自身の長さを調整
            var newCurrBeats = Math.Max(_currentGridBeats, _resizeInitialBeats - snappedDeltaBeats);
            var currDuration = MusicEngine.BeatsToDuration(newCurrBeats, "4/4");
            var currWidth = CalculateCardWidth(currDuration, false);

            var currChord = _currentChords[0] with { Duration = currDuration, Dotted = false, CardWidth = currWidth };
            _currentChords[0] = currChord;

            var currCard = _chordCardItems.FirstOrDefault(c => c.Id == currChord.Id);
            if (currCard != null) { currCard.Model = currChord; currCard.CardWidth = currWidth; }

            UpdateInspector(currChord);
            StatusTextBlock.Text = $"先頭コード長調整: {currChord.Symbol} ({MusicEngine.DurationLabel(currDuration)})";
        }
    }

    private void LeftResizeGrip_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        EndResize(sender, e, commit: true);
    }

    private void LeftResizeGrip_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        EndResize(sender, e, commit: false);
    }

    private void LeftResizeGrip_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        EndResize(sender, e, commit: false);
    }

    // --- 右端リサイズ (末尾長さ調整・グリッド伸縮) ---

    private void RightResizeGrip_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not string chordId) return;

        var chord = _currentChords.FirstOrDefault(c => c.Id == chordId);
        if (chord == null) return;

        e.Handled = true;
        _isResizingCard = true;
        _isLeftResize = false;
        _resizingChordId = chordId;
        _resizeInitialBeats = MusicEngine.ChordDurationBeats(chord.Duration, "4/4", chord.Dotted);
        _resizeStartPointerX = e.GetCurrentPoint(this).Position.X;

        element.CapturePointer(e.Pointer);
        SelectChord(chordId, preview: false);
    }

    private void RightResizeGrip_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isResizingCard || _isLeftResize || _resizingChordId == null) return;

        e.Handled = true;
        var currentX = e.GetCurrentPoint(this).Position.X;
        var deltaX = currentX - _resizeStartPointerX;
        var deltaBeats = deltaX / _pixelsPerBeat;

        var targetBeats = _resizeInitialBeats + deltaBeats;
        var snappedBeats = Math.Round(targetBeats / _currentGridBeats) * _currentGridBeats;
        snappedBeats = Math.Max(_currentGridBeats, snappedBeats);

        var chordIdx = _currentChords.FindIndex(c => c.Id == _resizingChordId);
        var cardItem = _chordCardItems.FirstOrDefault(c => c.Id == _resizingChordId);

        if (chordIdx >= 0 && cardItem != null)
        {
            var newDuration = MusicEngine.BeatsToDuration(snappedBeats, "4/4");
            var newWidth = CalculateCardWidth(newDuration, false);

            if (_currentChords[chordIdx].Duration != newDuration || Math.Abs(cardItem.CardWidth - newWidth) > 0.5)
            {
                var updated = _currentChords[chordIdx] with
                {
                    Duration = newDuration,
                    Dotted = false,
                    CardWidth = newWidth
                };
                _currentChords[chordIdx] = updated;
                cardItem.Model = updated;
                cardItem.CardWidth = newWidth;
                UpdateInspector(updated);
                StatusTextBlock.Text = $"コード長調整: {updated.Symbol} ({MusicEngine.DurationLabel(newDuration)})";
            }
        }
    }

    private void RightResizeGrip_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        EndResize(sender, e, commit: true);
    }

    private void RightResizeGrip_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        EndResize(sender, e, commit: false);
    }

    private void RightResizeGrip_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        EndResize(sender, e, commit: false);
    }

    private void ResizeGrip_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border)
        {
            bool isEn = ChordLaunchpad.Core.LocalizationService.IsEnglish;
            if (border.Name == "LeftResizeGrip")
            {
                ToolTipService.SetToolTip(border, isEn ? "Drag left edge to adjust start position / syncopation" : "左端をドラッグして開始点・シンコペーション調整 (前倒し/後ろ倒し)");
            }
            else
            {
                ToolTipService.SetToolTip(border, isEn ? "Drag right edge to resize chord length by grid" : "右端をドラッグしてグリッド単位で長さを伸縮");
            }

            border.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(90, 0, 120, 212));
            try
            {
                ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.SizeWestEast);
            }
            catch
            {
            }
        }
    }

    private void MarkerFlyout_Opening(object? sender, object e)
    {
        if (sender is Flyout flyout && flyout.Content is StackPanel panel)
        {
            bool isEn = ChordLaunchpad.Core.LocalizationService.IsEnglish;
            foreach (var child in panel.Children)
            {
                if (child is TextBlock tb)
                {
                    tb.Text = isEn ? "Section Settings" : "セクション設定";
                }
                else if (child is TextBox txt)
                {
                    txt.Header = isEn ? "Section Name" : "セクション名";
                }
                else if (child is ComboBox cb)
                {
                    cb.Header = isEn ? "Key (Modulation)" : "キー (転調)";
                }
                else if (child is Button btn)
                {
                    btn.Content = isEn ? "Delete Marker" : "マーカー削除";
                }
            }
        }
    }

    private void ResizeGrip_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border && !_isResizingCard)
        {
            border.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(21, 0, 120, 212));
            try
            {
                ProtectedCursor = null;
            }
            catch
            {
            }
        }
    }

    private void EndResize(object sender, PointerRoutedEventArgs e, bool commit)
    {
        if (_isResizingCard)
        {
            e.Handled = true;
            if (commit)
            {
                SaveState();
                StatusTextBlock.Text = "コードの長さを確定しました";
            }

            if (sender is FrameworkElement element)
            {
                element.ReleasePointerCapture(e.Pointer);
                if (element is Border border)
                {
                    border.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(21, 0, 120, 212));
                }
            }

            try
            {
                ProtectedCursor = null;
            }
            catch
            {
            }

            _isResizingCard = false;
            _isLeftResize = false;
            _resizingChordId = null;

            RefreshTimeline();
            SyncInputTextBoxFromChords();
        }
    }

    // ==========================================
    // インスペクターによるグリッド単位の伸縮
    // ==========================================

    private void DecreaseDuration_Click(object sender, RoutedEventArgs e)
    {
        ChangeSelectedChordDuration(-_currentGridBeats);
    }

    private void IncreaseDuration_Click(object sender, RoutedEventArgs e)
    {
        ChangeSelectedChordDuration(_currentGridBeats);
    }

    private void ChangeSelectedChordDuration(double deltaBeats)
    {
        if (string.IsNullOrEmpty(_selectedChordId)) return;

        var index = _currentChords.FindIndex(c => c.Id == _selectedChordId);
        if (index < 0) return;

        SaveState();
        var currentChord = _currentChords[index];
        var currentBeats = MusicEngine.ChordDurationBeats(currentChord.Duration, "4/4", currentChord.Dotted);
        var targetBeats = Math.Max(_currentGridBeats, currentBeats + deltaBeats);

        var newDuration = MusicEngine.BeatsToDuration(targetBeats, "4/4");
        var newWidth = CalculateCardWidth(newDuration, false);

        _currentChords[index] = currentChord with
        {
            Duration = newDuration,
            Dotted = false,
            CardWidth = newWidth
        };

        RefreshTimeline();
        SyncInputTextBoxFromChords();
        StatusTextBlock.Text = $"{_currentChords[index].Symbol} の長さを '{MusicEngine.DurationLabel(newDuration)}' に変更しました";
    }

    private void DeleteCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string chordId) return;
        DeleteChord(chordId);
    }

    private void DeleteChord(string chordId)
    {
        var index = _currentChords.FindIndex(c => c.Id == chordId);
        if (index < 0) return;

        SaveState();
        var removedSymbol = _currentChords[index].Symbol;
        _currentChords.RemoveAt(index);

        if (_selectedChordId == chordId)
        {
            _selectedChordId = _currentChords.Count > 0 ? _currentChords[Math.Min(index, _currentChords.Count - 1)].Id : null;
        }

        RefreshTimeline();

        if (!string.IsNullOrEmpty(_selectedChordId))
        {
            SelectChord(_selectedChordId, preview: false);
        }

        SyncInputTextBoxFromChords();
        UpdateGuideAndSuggestions();
        StatusTextBlock.Text = $"'{removedSymbol}' を削除しました";
    }

    private void ChordCard_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not ChordCardItem item) return;
        SelectChord(item.Id, preview: true);
    }

    // ==========================================
    // タイムライン内カードのドラッグ＆ドロップ（並び替え・挿入）
    // ==========================================

    private void ChordCard_DragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not ChordCardItem item) return;
        args.Data.SetText($"timeline-chord:{item.Id}");
        args.Data.RequestedOperation = DataPackageOperation.Move;
        StatusTextBlock.Text = $"'{item.Symbol}' を移動中 (移動先カードへドロップしてください)";
    }

    private void ChordCard_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.Text))
        {
            e.AcceptedOperation = DataPackageOperation.Move;
            e.DragUIOverride.Caption = "ここに配置";
            e.DragUIOverride.IsCaptionVisible = true;
        }
    }

    private async void ChordCard_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.Text)) return;
        var text = await e.DataView.GetTextAsync();
        e.Handled = true;

        if (sender is not FrameworkElement fe || fe.DataContext is not ChordCardItem targetItem) return;
        var targetIndex = _currentChords.FindIndex(c => c.Id == targetItem.Id);
        if (targetIndex < 0) return;

        SaveState();
        if (text.StartsWith("timeline-chord:"))
        {
            var sourceId = text["timeline-chord:".Length..];
            var sourceIndex = _currentChords.FindIndex(c => c.Id == sourceId);
            if (sourceIndex >= 0 && sourceIndex != targetIndex)
            {
                var item = _currentChords[sourceIndex];
                _currentChords.RemoveAt(sourceIndex);
                _currentChords.Insert(targetIndex, item);
                RefreshTimeline();
                SyncInputTextBoxFromChords();
                SelectChord(item.Id, preview: false);
                StatusTextBlock.Text = $"'{item.Symbol}' を {targetIndex + 1} 番目に移動しました";
            }
        }
        else
        {
            InsertChordAt(text, targetIndex);
        }
    }

    private void InsertChordAt(string symbol, int index)
    {
        var parsed = MusicEngine.ParseSymbolToken(symbol, CurrentKey, CurrentMode);
        if (parsed == null) return;

        var newChord = parsed with
        {
            Id = $"chord-{Guid.NewGuid().ToString("N")[..8]}",
            Duration = "1 bar",
            Inversion = StylePreset.Root,
            CardWidth = CalculateCardWidth("1 bar", false)
        };

        _currentChords.Insert(Math.Clamp(index, 0, _currentChords.Count), newChord);
        RefreshTimeline();
        SelectChord(newChord.Id, preview: true);
        SyncInputTextBoxFromChords();
        UpdateGuideAndSuggestions();
        StatusTextBlock.Text = $"'{symbol}' を {index + 1} 番目に挿入しました";
    }

    private void Timeline_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.Text))
        {
            e.AcceptedOperation = DataPackageOperation.Copy | DataPackageOperation.Move;
            e.DragUIOverride.Caption = "タイムラインに追加";
            e.DragUIOverride.IsCaptionVisible = true;
        }
    }

    private async void Timeline_Drop(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.Text))
        {
            var text = await e.DataView.GetTextAsync();
            SaveState();
            if (text.StartsWith("timeline-chord:"))
            {
                var sourceId = text["timeline-chord:".Length..];
                var sourceIndex = _currentChords.FindIndex(c => c.Id == sourceId);
                if (sourceIndex >= 0 && sourceIndex != _currentChords.Count - 1)
                {
                    var item = _currentChords[sourceIndex];
                    _currentChords.RemoveAt(sourceIndex);
                    _currentChords.Add(item);
                    RefreshTimeline();
                    SyncInputTextBoxFromChords();
                    SelectChord(item.Id, preview: false);
                    StatusTextBlock.Text = $"'{item.Symbol}' を末尾へ移動しました";
                }
            }
            else
            {
                AddChordToTimeline(text);
            }
        }
    }

    // ==========================================
    // セクション・転調マーカー操作
    // ==========================================

    private void AddMarkerButton_Click(object sender, RoutedEventArgs e)
    {
        SaveState();
        var insertIdx = 0;
        if (!string.IsNullOrEmpty(_selectedChordId))
        {
            insertIdx = Math.Max(0, _currentChords.FindIndex(c => c.Id == _selectedChordId));
        }

        var marker = new SectionMarker
        {
            Name = _sectionMarkers.Count == 0 ? "Intro" : $"Section {_sectionMarkers.Count + 1}",
            Key = CurrentKey,
            Mode = CurrentMode,
            InsertIndex = insertIdx
        };

        int insertAt = 0;
        while (insertAt < _sectionMarkers.Count && _sectionMarkers[insertAt].InsertIndex <= marker.InsertIndex)
        {
            insertAt++;
        }
        _sectionMarkers.Insert(insertAt, marker);

        RefreshTimeline();
        StatusTextBlock.Text = $"マーカー '{marker.Name}' を追加しました";
        RemoveFocusFromTopBar();
    }

    private void MarkerName_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox tb || tb.Tag is not string id) return;
        var marker = _sectionMarkers.FirstOrDefault(m => m.Id == id);
        if (marker != null)
        {
            var idx = _sectionMarkers.IndexOf(marker);
            _sectionMarkers[idx] = marker with { Name = tb.Text };
        }
    }

    private void MarkerKey_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox cb || cb.Tag is not string id) return;
        if (cb.SelectedItem is not ComboBoxItem item) return;
        var newKey = item.Content?.ToString() ?? "C";

        var marker = _sectionMarkers.FirstOrDefault(m => m.Id == id);
        if (marker != null && marker.Key != newKey)
        {
            SaveState();
            var idx = _sectionMarkers.IndexOf(marker);
            _sectionMarkers[idx] = marker with { Key = newKey };
            _activeKey = newKey;
            UpdateGuideAndSuggestions();
            RefreshTimeline();
            StatusTextBlock.Text = $"セクション '{marker.Name}' のキーを '{newKey}' に設定しました";
        }
    }

    private void DeleteMarker_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string id) return;
        SaveState();
        var target = _sectionMarkers.FirstOrDefault(m => m.Id == id);
        if (target != null)
        {
            _sectionMarkers.Remove(target);
        }
        RefreshTimeline();
        StatusTextBlock.Text = "マーカーを削除しました";
    }

    // ==========================================
    // コピペ・切り取り・貼り付け・キー操作
    // ==========================================

    private void CopySelected()
    {
        var selected = SelectedChord;
        if (selected == null) return;
        _clipboardChord = selected;
        StatusTextBlock.Text = $"'{selected.Symbol}' をコピーしました (Ctrl+C)";
    }

    private void CutSelected()
    {
        var selected = SelectedChord;
        if (selected == null) return;
        CopySelected();
        DeleteChord(selected.Id);
        StatusTextBlock.Text = $"'{selected.Symbol}' を切り取りました (Ctrl+X)";
    }

    private void PasteChord()
    {
        if (_clipboardChord == null) return;

        SaveState();
        var newChord = _clipboardChord with
        {
            Id = $"chord-{Guid.NewGuid().ToString("N")[..8]}",
            IsSelected = true,
            CardWidth = CalculateCardWidth(_clipboardChord.Duration, _clipboardChord.Dotted)
        };

        var insertIndex = _currentChords.Count;
        if (!string.IsNullOrEmpty(_selectedChordId))
        {
            var selIdx = _currentChords.FindIndex(c => c.Id == _selectedChordId);
            if (selIdx >= 0) insertIndex = selIdx + 1;
        }

        _currentChords.Insert(insertIndex, newChord);
        RefreshTimeline();
        SelectChord(newChord.Id, preview: true);
        SyncInputTextBoxFromChords();
        UpdateGuideAndSuggestions();
        StatusTextBlock.Text = $"'{newChord.Symbol}' を貼り付けました (Ctrl+V)";
    }

    private void DuplicateSelected_Click(object sender, RoutedEventArgs e)
    {
        CopySelected();
        PasteChord();
        RemoveFocusFromTopBar();
    }

    private void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_selectedChordId))
        {
            DeleteChord(_selectedChordId);
        }
        RemoveFocusFromTopBar();
    }

    private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var focused = FocusManager.GetFocusedElement(this.XamlRoot);
        if (focused is TextBox)
        {
            return;
        }

        var isCtrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var isAlt = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var isShift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        // F4: 右パネル開閉
        if (e.Key == VirtualKey.F4)
        {
            e.Handled = true;
            ToggleRightPanel();
            return;
        }

        // Space: 再生 / 停止
        if (e.Key == VirtualKey.Space)
        {
            e.Handled = true;
            TogglePlayBack();
            return;
        }

        // Ctrl+S: 保存
        if (isCtrl && e.Key == VirtualKey.S)
        {
            e.Handled = true;
            SaveProject();
            return;
        }

        // Ctrl+O: 開く
        if (isCtrl && e.Key == VirtualKey.O)
        {
            e.Handled = true;
            OpenProject();
            return;
        }

        // Ctrl+Z: アンドゥ
        if (isCtrl && e.Key == VirtualKey.Z)
        {
            e.Handled = true;
            Undo();
            return;
        }

        // Ctrl+Y: リドゥ
        if (isCtrl && e.Key == VirtualKey.Y)
        {
            e.Handled = true;
            Redo();
            return;
        }

        // Ctrl+C: コピー
        if (isCtrl && e.Key == VirtualKey.C)
        {
            e.Handled = true;
            CopySelected();
            return;
        }

        // Ctrl+X: 切り取り
        if (isCtrl && e.Key == VirtualKey.X)
        {
            e.Handled = true;
            CutSelected();
            return;
        }

        // Ctrl+V: 貼り付け
        if (isCtrl && e.Key == VirtualKey.V)
        {
            e.Handled = true;
            PasteChord();
            return;
        }

        // Backspace / Delete: 削除
        if (e.Key is VirtualKey.Back or VirtualKey.Delete)
        {
            e.Handled = true;
            DeleteSelected_Click(sender, e);
            return;
        }

        // Alt+Left / Alt+Right: 選択中コードのグリッド単位伸縮 (シンコペーション調整)
        if (isAlt && !isCtrl && !isShift && !string.IsNullOrEmpty(_selectedChordId))
        {
            if (e.Key == VirtualKey.Left)
            {
                e.Handled = true;
                DecreaseDuration_Click(sender, e);
                return;
            }
            if (e.Key == VirtualKey.Right)
            {
                e.Handled = true;
                IncreaseDuration_Click(sender, e);
                return;
            }
        }

        // Shift+Left / Shift+Right / Shift+Up / Shift+Down: タイムラインの横スクロール
        if (isShift && !isCtrl && !isAlt)
        {
            if (e.Key == VirtualKey.Left)
            {
                e.Handled = true;
                ScrollTimelineBy(-Math.Max(50.0, _pixelsPerBeat * 2));
                return;
            }
            if (e.Key == VirtualKey.Right)
            {
                e.Handled = true;
                ScrollTimelineBy(Math.Max(50.0, _pixelsPerBeat * 2));
                return;
            }
            if (e.Key == VirtualKey.Up)
            {
                e.Handled = true;
                ScrollTimelineBy(-Math.Max(80.0, _pixelsPerBeat * 4));
                return;
            }
            if (e.Key == VirtualKey.Down)
            {
                e.Handled = true;
                ScrollTimelineBy(Math.Max(80.0, _pixelsPerBeat * 4));
                return;
            }
        }

        // Left / Right: タイムライン上のコードを前後に移動＆スクロールしながらプレビュー鳴らす
        if (!isCtrl && !isAlt && !isShift && _currentChords.Count > 0)
        {
            if (e.Key == VirtualKey.Left)
            {
                e.Handled = true;
                SelectPreviousChord();
                return;
            }
            if (e.Key == VirtualKey.Right)
            {
                e.Handled = true;
                SelectNextChord();
                return;
            }
        }

        // ズームショートカットの動的判定 (Pro Tools, Cubase, Studio One, 動画編集ソフト, カスタム)
        if (ProcessZoomShortcut(e))
        {
            return;
        }
    }

    private bool ProcessZoomShortcut(KeyRoutedEventArgs e)
    {
        var settings = SettingsManager.Current;
        var isCtrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var isAlt = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var isShift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        // タップテンポ待機枠にフォーカスがある時は T キーをズームに奪われないようにする
        var focused = FocusManager.GetFocusedElement(this.XamlRoot);
        if (ReferenceEquals(focused, BpmLabelBorder) && e.Key == VirtualKey.T)
        {
            return false;
        }

        // 1. テンキーの [+]/[-] によるズーム判定 (独立ON/OFF機能)
        if (settings.EnableNumpadZoom)
        {
            if (e.Key == VirtualKey.Subtract)
            {
                e.Handled = true;
                ZoomOut_Click(this, new RoutedEventArgs());
                return true;
            }
            if (e.Key == VirtualKey.Add)
            {
                e.Handled = true;
                ZoomIn_Click(this, new RoutedEventArgs());
                return true;
            }
        }

        // 2. ズームスタイルの判定
        switch (settings.ZoomStyle)
        {
            case ZoomShortcutStyle.ProTools:
                // Pro Tools: R = 縮小, T = 拡大 (修飾キーなし)
                if (!isCtrl && !isAlt && !isShift)
                {
                    if (e.Key == VirtualKey.R)
                    {
                        e.Handled = true;
                        ZoomOut_Click(this, new RoutedEventArgs());
                        return true;
                    }
                    if (e.Key == VirtualKey.T)
                    {
                        e.Handled = true;
                        ZoomIn_Click(this, new RoutedEventArgs());
                        return true;
                    }
                }
                break;

            case ZoomShortcutStyle.Cubase:
                // Cubase: G = 縮小, H = 拡大 (修飾キーなし)
                if (!isCtrl && !isAlt && !isShift)
                {
                    if (e.Key == VirtualKey.G)
                    {
                        e.Handled = true;
                        ZoomOut_Click(this, new RoutedEventArgs());
                        return true;
                    }
                    if (e.Key == VirtualKey.H)
                    {
                        e.Handled = true;
                        ZoomIn_Click(this, new RoutedEventArgs());
                        return true;
                    }
                }
                break;

            case ZoomShortcutStyle.StudioOne:
                // Studio One / Fender Studio: W = 縮小, E = 拡大 (修飾キーなし)
                if (!isCtrl && !isAlt && !isShift)
                {
                    if (e.Key == VirtualKey.W)
                    {
                        e.Handled = true;
                        ZoomOut_Click(this, new RoutedEventArgs());
                        return true;
                    }
                    if (e.Key == VirtualKey.E)
                    {
                        e.Handled = true;
                        ZoomIn_Click(this, new RoutedEventArgs());
                        return true;
                    }
                }
                break;

            case ZoomShortcutStyle.PremiereResolve:
                // Premiere / DaVinci Resolve / Final Cut:
                // 誤操作防止のため【Ctrl】修飾キーが必須。
                // 縮小: Ctrl + [-] (JIS: 189 / US: 189)
                // 拡大: Ctrl + [=] または [; / +] (JIS: 187 / US: 187)
                if (isCtrl && !isAlt)
                {
                    if ((int)e.Key == 189)
                    {
                        e.Handled = true;
                        ZoomOut_Click(this, new RoutedEventArgs());
                        return true;
                    }
                    if ((int)e.Key == 187)
                    {
                        e.Handled = true;
                        ZoomIn_Click(this, new RoutedEventArgs());
                        return true;
                    }
                }
                break;

            case ZoomShortcutStyle.AvidMediaComposer:
                // Avid Media Composer:
                // 縮小: Ctrl + K
                // 拡大: Ctrl + L
                if (isCtrl && !isAlt && !isShift)
                {
                    if (e.Key == VirtualKey.K)
                    {
                        e.Handled = true;
                        ZoomOut_Click(this, new RoutedEventArgs());
                        return true;
                    }
                    if (e.Key == VirtualKey.L)
                    {
                        e.Handled = true;
                        ZoomIn_Click(this, new RoutedEventArgs());
                        return true;
                    }
                }
                break;

            case ZoomShortcutStyle.Custom:
                // ユーザー定義キー
                if (e.Key == settings.CustomZoomInKey &&
                    isCtrl == settings.CustomZoomInCtrl &&
                    isAlt == settings.CustomZoomInAlt &&
                    isShift == settings.CustomZoomInShift)
                {
                    e.Handled = true;
                    ZoomIn_Click(this, new RoutedEventArgs());
                    return true;
                }

                if (e.Key == settings.CustomZoomOutKey &&
                    isCtrl == settings.CustomZoomOutCtrl &&
                    isAlt == settings.CustomZoomOutAlt &&
                    isShift == settings.CustomZoomOutShift)
                {
                    e.Handled = true;
                    ZoomOut_Click(this, new RoutedEventArgs());
                    return true;
                }
                break;
        }

        return false;
    }

    private void Timeline_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var pointerPoint = e.GetCurrentPoint(TimelineScrollViewer);
        var delta = pointerPoint.Properties.MouseWheelDelta;
        if (delta == 0) return;

        var isHorizontal = pointerPoint.Properties.IsHorizontalMouseWheel;
        var isCtrl = e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control) ||
                     Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var isShift = e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift) ||
                      Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down) ||
                      Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.LeftShift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down) ||
                      Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.RightShift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        // 1. Shift 押下中、またはチルト/水平マウスホイールによる横スクロール
        if (isShift || isHorizontal)
        {
            double scrollDelta;
            if (isHorizontal && !isShift)
            {
                // チルトホイール: 右チルト(>0)で右へ、左チルト(<0)で左へ
                scrollDelta = (delta > 0 ? 1.0 : -1.0) * Math.Max(50.0, _pixelsPerBeat * 2);
            }
            else
            {
                // Shift + 垂直ホイール: 奥回転/上(>0)で左へ、手前回転/下(<0)で右へ
                scrollDelta = (delta > 0 ? -1.0 : 1.0) * Math.Max(50.0, _pixelsPerBeat * 2);
            }
            ScrollTimelineBy(scrollDelta);
            e.Handled = true;
            return;
        }

        // 2. Ctrl 押下中のホイールでズーム
        var settings = SettingsManager.Current;
        if (isCtrl && settings.EnableWheelZoom)
        {
            if (delta > 0)
            {
                ZoomSlider.Value = Math.Min(ZoomSlider.Maximum, ZoomSlider.Value + 3);
                e.Handled = true;
            }
            else if (delta < 0)
            {
                ZoomSlider.Value = Math.Max(ZoomSlider.Minimum, ZoomSlider.Value - 3);
                e.Handled = true;
            }
        }
    }

    private async void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog { XamlRoot = this.XamlRoot };
        await dialog.ShowAsync();
        UpdateAutoSaveTimer();
        ApplyLanguage();
        RemoveFocusFromTopBar();
    }

    private void ApplyLanguage()
    {
        bool isEn = ChordLaunchpad.Core.LocalizationService.IsEnglish;

        // MenuBar: File
        MenuFile.Title = isEn ? "File" : "ファイル";
        MenuFileNew.Text = isEn ? "New Project" : "新規作成";
        MenuFileOpen.Text = isEn ? "Open Project..." : "プロジェクトを開く...";
        MenuFileSave.Text = isEn ? "Save Project..." : "プロジェクトを保存...";
        MenuFileSaveAs.Text = isEn ? "Save Project As..." : "名前を付けて保存...";
        MenuFileTemplates.Text = isEn ? "Chord Templates..." : "テンプレート集...";
        MenuFileExportMidi.Text = isEn ? "Export MIDI..." : "MIDIファイルをエクスポート...";
        MenuFileExit.Text = isEn ? "Exit" : "終了";

        // MenuBar: Edit
        MenuEdit.Title = isEn ? "Edit" : "編集";
        MenuEditUndo.Text = isEn ? "Undo" : "元に戻す";
        MenuEditRedo.Text = isEn ? "Redo" : "やり直す";
        MenuEditCut.Text = isEn ? "Cut" : "切り取り";
        MenuEditCopy.Text = isEn ? "Copy" : "コピー";
        MenuEditPaste.Text = isEn ? "Paste" : "貼り付け";
        MenuEditDelete.Text = isEn ? "Delete" : "削除";
        MenuEditClear.Text = isEn ? "Clear Timeline" : "全タイムラインクリア";
        MenuEditSettings.Text = isEn ? "Settings..." : "環境設定...";

        // MenuBar: View
        MenuView.Title = isEn ? "View" : "表示";
        MenuViewGuide.Text = isEn ? "Toggle Guide Panel" : "ガイドパネル開閉";
        MenuViewZoomIn.Text = isEn ? "Zoom In" : "ズーム拡大";
        MenuViewZoomOut.Text = isEn ? "Zoom Out" : "ズーム縮小";
        MenuViewZoomReset.Text = isEn ? "Reset Zoom" : "ズームリセット";
        MenuViewZoomSettings.Text = isEn ? "Zoom Shortcut Settings..." : "ズーム操作スタイル設定...";

        // MenuBar: Play
        MenuPlay.Title = isEn ? "Playback" : "再生";
        MenuPlayPlayStop.Text = isEn ? "Play / Stop (Space)" : "再生 / 停止 (Space)";

        // MenuBar: Help
        MenuHelp.Title = isEn ? "Help" : "ヘルプ";
        MenuHelpReference.Text = isEn ? "Syntax Reference & Guide..." : "コマンド・記法リファレンス...";
        MenuHelpAbout.Text = isEn ? "About ChordLaunchpad..." : "バージョン情報";

        // Header Buttons & Tooltips
        ToolTipService.SetToolTip(SaveProjectButton, isEn ? "Save Project (Ctrl+S)" : "プロジェクトを保存 (Ctrl+S)");
        ToolTipService.SetToolTip(OpenProjectButton, isEn ? "Open Project (Ctrl+O)" : "プロジェクトを開く (Ctrl+O)");
        ToolTipService.SetToolTip(OpenTemplatesButton, isEn ? "Genre Templates (Ctrl+T)" : "ジャンル別テンプレート集 (Ctrl+T)");
        OpenTemplatesButtonText.Text = isEn ? "Templates" : "テンプレート集";
        ToolTipService.SetToolTip(UndoButton, isEn ? "Undo (Ctrl+Z)" : "元に戻す (Ctrl+Z)");
        ToolTipService.SetToolTip(RedoButton, isEn ? "Redo (Ctrl+Y)" : "やり直す (Ctrl+Y)");

        ToolTipService.SetToolTip(PlayAllButton, isEn ? "Play / Stop (Space)" : "再生 / 停止 (Space)");
        ToolTipService.SetToolTip(StopButton, isEn ? "Stop (Space)" : "停止 (Space)");
        ToolTipService.SetToolTip(ExportMidiButton, isEn ? "Export MIDI file (Ctrl+E)" : "MIDIファイルを書き出し (Ctrl+E)");
        ExportMidiButtonText.Text = isEn ? "Export MIDI" : "MIDI保存";
        ToolTipService.SetToolTip(MidiDragHandle, isEn ? "Drag & drop directly to your DAW track" : "このボタンをDAWのトラックへ直接ドラッグ＆ドロップしてください");
        MidiDragHandleText.Text = isEn ? "Drag MIDI to DAW" : "DAWへMIDIドラッグ";
        ToolTipService.SetToolTip(ToggleRightPanelButton, isEn ? "Toggle Guide Panel (F4)" : "右パネル開閉 (F4)");
        ToolTipService.SetToolTip(SettingsButton, isEn ? "Settings (Ctrl+0)" : "環境設定 (Ctrl+0)");

        // Settings Bar
        KeyLabelText.Text = isEn ? "Key:" : "キー:";
        ScaleLabelText.Text = isEn ? "Scale:" : "スケール:";
        if (ModeComboBox.Items.Count >= 2)
        {
            (ModeComboBox.Items[0] as ComboBoxItem)!.Content = isEn ? "Major" : "メジャー";
            (ModeComboBox.Items[1] as ComboBoxItem)!.Content = isEn ? "Minor" : "マイナー";
        }
        ToneLabelText.Text = isEn ? "Sound:" : "音色:";
        BassLabelText.Text = isEn ? "Bass:" : "ベース:";
        if (BassComboBox.Items.Count >= 4)
        {
            (BassComboBox.Items[0] as ComboBoxItem)!.Content = isEn ? "None" : "なし";
            (BassComboBox.Items[1] as ComboBoxItem)!.Content = isEn ? "Root" : "1";
            (BassComboBox.Items[2] as ComboBoxItem)!.Content = isEn ? "Octave" : "2";
            (BassComboBox.Items[3] as ComboBoxItem)!.Content = isEn ? "Both" : "両方";
        }
        GridLabelText.Text = isEn ? "Grid:" : "グリッド:";
        if (GridSnapComboBox.Items.Count >= 5)
        {
            (GridSnapComboBox.Items[0] as ComboBoxItem)!.Content = isEn ? "1/16 Beat" : "1/16拍";
            (GridSnapComboBox.Items[1] as ComboBoxItem)!.Content = isEn ? "1/8 Beat" : "1/8拍";
            (GridSnapComboBox.Items[2] as ComboBoxItem)!.Content = isEn ? "1 Beat" : "1拍";
            (GridSnapComboBox.Items[3] as ComboBoxItem)!.Content = isEn ? "2 Beats" : "2拍";
            (GridSnapComboBox.Items[4] as ComboBoxItem)!.Content = isEn ? "1 Bar" : "1小節";
        }
        OpsLabelText.Text = isEn ? "Actions:" : "操作:";
        ToolTipService.SetToolTip(DuplicateButton, isEn ? "Duplicate selected chord (Ctrl+C / Ctrl+V)" : "選択中のコードを複製 (Ctrl+C / Ctrl+V)");
        ToolTipService.SetToolTip(DeleteButton, isEn ? "Delete selected chord (Backspace / Del)" : "選択中のコードを削除 (Backspace / Del)");
        ToolTipService.SetToolTip(AddMarkerButton, isEn ? "Add modulation section marker at selection" : "選択位置に転調セクションマーカーを追加");
        AddMarkerButtonText.Text = isEn ? "+ Marker" : "+ マーカー";

        // Input Column
        InputHeaderTitle.Text = isEn ? "Chord / Degree Input" : "コード / ディグリー入力";
        ApplyInputButton.Content = isEn ? "Apply (Ctrl+Enter)" : "反映 (Ctrl+Enter)";

        // Timeline Column
        TimelineHeaderTitle.Text = isEn ? "TIMELINE (Progression)" : "TIMELINE (進行)";
        ClearTimelineButton.Content = isEn ? "Clear All" : "全クリア";
        TimelineHintText.Text = isEn ? "Drag to reorder / Double-click to audition" : "※ ドラッグで並び替え / ダブルクリックで試聴";

        // Inspector
        InspectorChordLabel.Text = isEn ? "Chord:" : "コード:";
        InspectorDurationLabel.Text = isEn ? "Length:" : "長さ:";
        InspectorInversionLabel.Text = isEn ? "Inversion:" : "転回形:";
        InspectorNotesLabel.Text = isEn ? "Notes:" : "構成音:";
        ZoomLabelText.Text = isEn ? "Zoom:" : "ズーム:";
        ToolTipService.SetToolTip(ZoomSlider, isEn ? "Timeline zoom ratio" : "タイムライン表示倍率（ズーム）");

        // Guide Panel
        DiatonicExpanderHeader.Text = isEn ? "Diatonic Chords" : "ダイアトニック";
        BorrowedExpanderHeader.Text = isEn ? "Modal Interchange" : "借用和音 (同主調)";
        SecondaryDominantsExpanderHeader.Text = isEn ? "Secondary Dominants" : "セカンダリードミナント";
        SuggestionsExpanderHeader.Text = isEn ? "Next Suggestions" : "SUGGESTIONS (次候補)";

        InputTextBox.PlaceholderText = isEn
            ? "e.g.: 4mas 1 2 5, FM7 Em7 Dm7 G7 (both degrees and chord names supported)"
            : "例: 4mas 1 2 5、FM7 Em7 Dm7 G7 (ディグリー・コード名どちらもOK)";
        ToolTipService.SetToolTip(BpmLabelBorder, isEn
            ? "Double-click to focus, then tap [T] key in tempo to set BPM (Pro Tools style)"
            : "ダブルクリックしてフォーカス後、[T]キーをテンポよく叩いてタップテンポ設定 (Pro Tools仕様)");

        // タイムライン描画を言語切り替えに合わせて再描画（起動完了後の動的言語切替時のみ実行し、初期起動時の重複を完全排除）
        if (_isInitialized)
        {
            RefreshTimeline();
            UpdateGuideAndSuggestions();
        }

        // Status
        if (StatusTextBlock.Text == "準備完了" || StatusTextBlock.Text == "Ready")
        {
            StatusTextBlock.Text = isEn ? "Ready" : "準備完了";
        }
    }

    // ==========================================
    // 右カラム収納 (折りたたみ)
    // ==========================================

    private void ToggleRightPanelButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleRightPanel();
        RemoveFocusFromTopBar();
    }

    private void ToggleRightPanel()
    {
        _isRightPanelVisible = !_isRightPanelVisible;
        RightColumnDefinition.Width = _isRightPanelVisible ? new GridLength(185) : new GridLength(0);
        RightPanelBorder.Visibility = _isRightPanelVisible ? Visibility.Visible : Visibility.Collapsed;

        // 非表示中に保留されていたガイド更新があれば、展開時に1度だけ遅延実行
        if (_isRightPanelVisible && _pendingGuideUpdate)
        {
            _pendingGuideUpdate = false;
            UpdateGuideAndSuggestions();
        }

        bool isEn = ChordLaunchpad.Core.LocalizationService.IsEnglish;
        StatusTextBlock.Text = _isRightPanelVisible
            ? (isEn ? "Guide panel displayed" : "ガイドパネルを表示しました")
            : (isEn ? "Guide panel collapsed" : "ガイドパネルを収納しました");
    }

    // ==========================================
    // メニューバー項目ハンドラ
    // ==========================================

    private async void NewProject_Click(object sender, RoutedEventArgs e)
    {
        bool isEn = ChordLaunchpad.Core.LocalizationService.IsEnglish;

        // 既存の作業内容がある場合、未保存確認
        if (_currentChords.Count > 0)
        {
            var confirmDialog = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = isEn ? "New Project" : "新規プロジェクトの作成",
                Content = isEn
                    ? "Create a new project?\n(Current work will be cleared)"
                    : "新規プロジェクトを作成しますか？\n（現在の作業内容はクリアされます）",
                PrimaryButtonText = isEn ? "Continue" : "新規作成を続行",
                CloseButtonText = isEn ? "Cancel" : "キャンセル",
                DefaultButton = ContentDialogButton.Primary
            };

            var confirmResult = await confirmDialog.ShowAsync();
            if (confirmResult != ContentDialogResult.Primary)
            {
                return;
            }
        }

        // プロジェクト作成・保存ダイアログの表示
        var dialog = new ProjectFolderDialog(defaultProjectName: null, isNewProject: true)
        {
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            SaveState();
            _currentChords.Clear();
            _sectionMarkers.Clear();
            _selectedChordId = null;
            InputTextBox.Text = string.Empty;
            RefreshTimeline();
            UpdateGuideAndSuggestions();

            _currentProjectPath = dialog.TargetProjectFilePath;
            // 初期プロジェクトファイルを書き出し
            await SaveToFileAsync(_currentProjectPath);
            StatusTextBlock.Text = isEn
                ? $"Created new project: {dialog.ProjectName}"
                : $"新規プロジェクトを作成しました: {dialog.ProjectName}";
        }
    }

    private void ExitApp_Click(object sender, RoutedEventArgs e)
    {
        App.MainWindowInstance?.Close();
    }

    private void Cut_Click(object sender, RoutedEventArgs e) => CutSelected();
    private void Copy_Click(object sender, RoutedEventArgs e) => CopySelected();
    private void Paste_Click(object sender, RoutedEventArgs e) => PasteChord();

    private void ZoomIn_Click(object sender, RoutedEventArgs e)
    {
        ZoomSlider.Value = Math.Min(ZoomSlider.Maximum, ZoomSlider.Value + 5);
    }

    private void ZoomOut_Click(object sender, RoutedEventArgs e)
    {
        ZoomSlider.Value = Math.Max(ZoomSlider.Minimum, ZoomSlider.Value - 5);
    }

    private void ZoomReset_Click(object sender, RoutedEventArgs e)
    {
        ZoomSlider.Value = 40;
    }

    private async void OpenCommandReference_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new HelpDialog { XamlRoot = this.XamlRoot };
        await dialog.ShowAsync();
    }

    private async void AboutDialog_Click(object sender, RoutedEventArgs e)
    {
        bool isEn = ChordLaunchpad.Core.LocalizationService.IsEnglish;
        var dialog = new ContentDialog
        {
            XamlRoot = this.XamlRoot,
            Title = "ChordLaunchpad Ver 2.0",
            Content = isEn
                ? "Version 2.0.0 (WinUI 3 / Windows App SDK)\n\nDAW Integration, Modal Interchange & Modulation Support\nChord Progression Assistant & Creation App"
                : "バージョン 2.0.0 (WinUI 3 / Windows App SDK)\n\nDAW連携・借用和音・転調セクション対応\nコード進行生成支援アプリケーション",
            CloseButtonText = "OK"
        };
        await dialog.ShowAsync();
    }

    // ==========================================
    // プロジェクト保存 / 読込
    // ==========================================

    private ProjectData BuildCurrentProjectData()
    {
        return new ProjectData
        {
            Title = string.IsNullOrEmpty(_currentProjectPath) ? "Chord Progression" : Path.GetFileNameWithoutExtension(_currentProjectPath),
            Key = CurrentKey,
            Mode = CurrentMode,
            Bpm = CurrentBpm,
            PlaybackTone = _currentTone,
            BassAddition = CurrentBassAddition,
            OpenVoicing = CurrentOpenVoicing,
            RawInput = InputTextBox.Text,
            Chords = _currentChords,
            Markers = _sectionMarkers
        };
    }

    private void SaveProjectButton_Click(object sender, RoutedEventArgs e)
    {
        SaveProject();
        RemoveFocusFromTopBar();
    }

    private void SaveProjectAs_Click(object sender, RoutedEventArgs e) => SaveProjectAs();

    private async void SaveProject()
    {
        // 既存の保存先が存在する場合: 上書きするかを確認
        if (!string.IsNullOrEmpty(_currentProjectPath) && File.Exists(_currentProjectPath))
        {
            var fileName = Path.GetFileName(_currentProjectPath);
            bool isEn = ChordLaunchpad.Core.LocalizationService.IsEnglish;
            var confirmDialog = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = isEn ? "Confirm Project Overwrite" : "プロジェクトの上書き確認",
                Content = isEn
                    ? $"Overwrite existing project file '{fileName}'?\n\nChoosing 'No' will prompt you to save as a new file."
                    : $"既存のプロジェクトファイル '{fileName}' に上書き保存しますか？\n\n「いいえ」を選択すると、別の名前で保存します。",
                PrimaryButtonText = isEn ? "Overwrite (Yes)" : "上書き保存 (はい)",
                SecondaryButtonText = isEn ? "Save As (No)" : "別名で保存 (いいえ)",
                CloseButtonText = isEn ? "Cancel" : "キャンセル",
                DefaultButton = ContentDialogButton.Primary
            };

            var result = await confirmDialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await SaveToFileAsync(_currentProjectPath);
                return;
            }
            else if (result == ContentDialogResult.Secondary)
            {
                SaveProjectAs();
                return;
            }
            else
            {
                // キャンセル
                return;
            }
        }

        // 新規作成時（既存ファイルなし）は別名保存フロー
        SaveProjectAs();
    }

    private async void SaveProjectAs()
    {
        var currentName = !string.IsNullOrEmpty(_currentProjectPath)
            ? Path.GetFileNameWithoutExtension(_currentProjectPath)
            : null;

        var dialog = new ProjectFolderDialog(defaultProjectName: currentName, isNewProject: false)
        {
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var targetPath = dialog.TargetProjectFilePath;
            await SaveToFileAsync(targetPath);
            _currentProjectPath = targetPath;
            bool isEn = ChordLaunchpad.Core.LocalizationService.IsEnglish;
            StatusTextBlock.Text = isEn
                ? $"Project saved: {dialog.ProjectName}"
                : $"プロジェクトを保存しました: {dialog.ProjectName}";
        }
    }

    private async Task SaveToFileAsync(string filePath)
    {
        bool isEn = ChordLaunchpad.Core.LocalizationService.IsEnglish;
        try
        {
            var project = BuildCurrentProjectData();
            var json = JsonSerializer.Serialize(project, AppJsonContext.Default.ProjectData);
            await File.WriteAllTextAsync(filePath, json);
            _currentProjectPath = filePath;
            StatusTextBlock.Text = isEn
                ? $"Project saved: {Path.GetFileName(filePath)}"
                : $"プロジェクトを上書き保存しました: {Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = isEn
                ? $"Save error: {ex.Message}"
                : $"上書き保存エラー: {ex.Message}";
        }
    }

    private void OpenProjectButton_Click(object sender, RoutedEventArgs e)
    {
        OpenProject();
        RemoveFocusFromTopBar();
    }

    private async void OpenProject()
    {
        try
        {
            var openPicker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary
            };
            openPicker.FileTypeFilter.Add(".chord");
            openPicker.FileTypeFilter.Add(".json");

            if (App.MainWindowInstance != null)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
                WinRT.Interop.InitializeWithWindow.Initialize(openPicker, hwnd);
            }

            var file = await openPicker.PickSingleFileAsync();
            if (file != null)
            {
                var json = await FileIO.ReadTextAsync(file);
                var project = JsonSerializer.Deserialize(json, AppJsonContext.Default.ProjectData);
                if (project != null)
                {
                    SaveState();
                    _currentProjectPath = file.Path;
                    LoadProject(project);
                    bool isEn = ChordLaunchpad.Core.LocalizationService.IsEnglish;
                    StatusTextBlock.Text = isEn
                        ? $"Project loaded: {file.Name}"
                        : $"プロジェクトを読み込みました: {file.Name}";
                }
            }
        }
        catch (Exception ex)
        {
            bool isEn = ChordLaunchpad.Core.LocalizationService.IsEnglish;
            StatusTextBlock.Text = isEn
                ? $"Load error: {ex.Message}"
                : $"読込エラー: {ex.Message}";
        }
    }

    private void LoadProject(ProjectData project)
    {
        _isUpdatingInternally = true;
        try
        {
            KeyComboBox.SelectedItem = project.Key;
            BpmBox.Value = project.Bpm;
            InputTextBox.Text = project.RawInput;
            ModeComboBox.SelectedIndex = project.Mode == MusicalMode.Minor ? 1 : 0;
            var targetToneTag = project.PlaybackTone.ToString();
            var matchedItem = ToneComboBox.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), targetToneTag, StringComparison.OrdinalIgnoreCase));
            if (matchedItem != null)
            {
                ToneComboBox.SelectedItem = matchedItem;
                _currentTone = project.PlaybackTone;
            }
            else
            {
                ToneComboBox.SelectedIndex = 0;
                _currentTone = PlaybackTone.Piano;
            }
            BassComboBox.SelectedIndex = (int)project.BassAddition;
        }
        finally
        {
            _isUpdatingInternally = false;
        }

        _activeKey = project.Key;
        _currentChords = project.Chords.ToList();
        SetSectionMarkers(project.Markers);
        _selectedChordId = _currentChords.FirstOrDefault()?.Id;

        RefreshTimeline();
        if (!string.IsNullOrEmpty(_selectedChordId))
        {
            SelectChord(_selectedChordId, preview: false);
        }
        UpdateGuideAndSuggestions();
    }

    // ==========================================
    // MIDIエクスポート ＆ DAWドラッグ
    // ==========================================

    private async void ExportMidiButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentChords.Count == 0)
        {
            StatusTextBlock.Text = "書き出すコード進行がありません";
            return;
        }

        try
        {
            var midiBytes = MidiBuilder.BuildMidiBytes(
                _currentChords,
                CurrentBpm,
                "4/4",
                CurrentBassAddition,
                CurrentOpenVoicing
            );

            // プロジェクトフォルダが存在する場合、既定の Chord MIDI フォルダを優先提示
            if (!string.IsNullOrEmpty(_currentProjectPath))
            {
                var projectDir = Path.GetDirectoryName(_currentProjectPath);
                if (!string.IsNullOrEmpty(projectDir) && Directory.Exists(projectDir))
                {
                    var chordMidiDir = Path.Combine(projectDir, "Chord MIDI");
                    if (!Directory.Exists(chordMidiDir))
                    {
                        Directory.CreateDirectory(chordMidiDir);
                    }

                    var projectName = Path.GetFileNameWithoutExtension(_currentProjectPath);
                    var defaultMidiName = $"{projectName}.mid";

                    // エクスポート確認・ファイル名指定ダイアログ
                    var fileNameTextBox = new TextBox
                    {
                        Text = defaultMidiName,
                        Margin = new Thickness(0, 6, 0, 0),
                        FontSize = 12
                    };

                    bool isEn = ChordLaunchpad.Core.LocalizationService.IsEnglish;
                    var contentStack = new StackPanel { Spacing = 6 };
                    contentStack.Children.Add(new TextBlock
                    {
                        Text = isEn
                            ? "Save to the project's 'Chord MIDI' folder."
                            : "プロジェクト内の「Chord MIDI」フォルダに保存します。",
                        FontSize = 12
                    });
                    contentStack.Children.Add(new TextBlock
                    {
                        Text = isEn ? $"Target Folder: {chordMidiDir}" : $"保存先: {chordMidiDir}",
                        FontSize = 11,
                        Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                    });
                    contentStack.Children.Add(new TextBlock
                    {
                        Text = isEn ? "MIDI File Name:" : "MIDIファイル名:",
                        FontSize = 12,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Margin = new Thickness(0, 4, 0, 0)
                    });
                    contentStack.Children.Add(fileNameTextBox);

                    var exportDialog = new ContentDialog
                    {
                        XamlRoot = this.XamlRoot,
                        Title = isEn ? "Export MIDI File" : "MIDIファイルのエクスポート",
                        Content = contentStack,
                        PrimaryButtonText = isEn ? "Save to Chord MIDI" : "Chord MIDI に保存",
                        SecondaryButtonText = isEn ? "Choose Other Location..." : "別の場所を選択...",
                        CloseButtonText = isEn ? "Cancel" : "キャンセル",
                        DefaultButton = ContentDialogButton.Primary
                    };

                    var dialogResult = await exportDialog.ShowAsync();
                    if (dialogResult == ContentDialogResult.Primary)
                    {
                        var fileName = fileNameTextBox.Text.Trim();
                        if (string.IsNullOrEmpty(fileName)) fileName = defaultMidiName;
                        if (!fileName.EndsWith(".mid", StringComparison.OrdinalIgnoreCase)) fileName += ".mid";

                        var targetMidiPath = Path.Combine(chordMidiDir, fileName);
                        await File.WriteAllBytesAsync(targetMidiPath, midiBytes);
                        StatusTextBlock.Text = isEn
                            ? $"Saved MIDI file: {fileName} (Chord MIDI)"
                            : $"MIDIファイルを保存しました: {fileName} (Chord MIDI)";
                        return;
                    }
                    else if (dialogResult != ContentDialogResult.Secondary)
                    {
                        // キャンセル
                        return;
                    }
                    // Secondary の場合は以下の FileSavePicker へフォールスルー
                }
            }

            var savePicker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.MusicLibrary,
                SuggestedFileName = !string.IsNullOrEmpty(_currentProjectPath)
                    ? $"{Path.GetFileNameWithoutExtension(_currentProjectPath)}.mid"
                    : "chord-progression.mid"
            };
            savePicker.FileTypeChoices.Add("Standard MIDI File", new List<string> { ".mid" });

            if (App.MainWindowInstance != null)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
                WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hwnd);
            }

            var file = await savePicker.PickSaveFileAsync();
            if (file != null)
            {
                await FileIO.WriteBytesAsync(file, midiBytes);
                StatusTextBlock.Text = $"MIDIファイルを保存しました: {file.Name}";
            }
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"MIDI保存エラー: {ex.Message}";
        }
        finally
        {
            RemoveFocusFromTopBar();
        }
    }

    private async void MidiDragHandle_DragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if (_currentChords.Count == 0)
        {
            args.Cancel = true;
            StatusTextBlock.Text = "ドラッグするコード進行がありません";
            return;
        }

        var deferral = args.GetDeferral();
        try
        {
            StatusTextBlock.Text = "MIDIファイルを生成中...";
            var tempMidi = MidiBuilder.SaveTempMidiFile(
                _currentChords,
                CurrentBpm,
                "4/4",
                "chord-progression.mid",
                CurrentBassAddition,
                CurrentOpenVoicing
            );

            var storageFile = await StorageFile.GetFileFromPathAsync(tempMidi);
            args.Data.SetStorageItems(new[] { storageFile });
            args.Data.RequestedOperation = DataPackageOperation.Copy;
            StatusTextBlock.Text = "DAWのトラックへドロップしてください";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"ドラッグ準備エラー: {ex.Message}";
        }
        finally
        {
            deferral.Complete();
        }
    }

    // ==========================================
    // テンプレート別ダイアログ
    // ==========================================

    private async void OpenTemplatesButton_Click(object sender, RoutedEventArgs e)
    {
        var currentProgression = _currentChords.Count > 0
            ? MusicEngine.ProgressionToInput(_currentChords, NotationPreference.Symbol, "4/4", 4)
            : InputTextBox.Text;

        var dialog = new TemplateDialog(CurrentKey, CurrentMode, _currentTone, currentProgression)
        {
            XamlRoot = this.XamlRoot
        };

        dialog.ProgressionApplied += (progression, isAppend) =>
        {
            SaveState();

            var parseResult = MusicEngine.ParseProgression(
                progression,
                CurrentKey,
                CurrentMode,
                "1 bar",
                "4/4",
                4
            );

            var chords = parseResult.Chords;
            if (chords.Count == 0)
            {
                // フォールバック: C Major で再試行
                parseResult = MusicEngine.ParseProgression(progression, "C", MusicalMode.Major, "1 bar", "4/4", 4);
                chords = parseResult.Chords;
            }

            var newBlocks = chords.Select(c => c with
            {
                CardWidth = CalculateCardWidth(c.Duration, c.Dotted)
            }).ToList();

            if (isAppend)
            {
                _currentChords.AddRange(newBlocks);
            }
            else
            {
                _currentChords.Clear();
                _currentChords.AddRange(newBlocks);
            }

            if (_currentChords.Count > 0)
            {
                _selectedChordId = newBlocks.FirstOrDefault()?.Id ?? _currentChords[0].Id;
            }
            else
            {
                _selectedChordId = null;
            }

            RefreshTimeline();
            if (!string.IsNullOrEmpty(_selectedChordId))
            {
                SelectChord(_selectedChordId, preview: false);
            }
            SyncInputTextBoxFromChords();
            UpdateGuideAndSuggestions();

            bool isEn = LocalizationService.IsEnglish;
            StatusTextBlock.Text = isAppend
                ? (isEn ? $"Appended template '{progression}' ({newBlocks.Count} chords)" : $"テンプレート '{progression}' を末尾に追加しました ({newBlocks.Count}コード)")
                : (isEn ? $"Applied template '{progression}' ({newBlocks.Count} chords)" : $"テンプレート '{progression}' をタイムラインに適用しました ({newBlocks.Count}コード)");

            // 適用後にボタンからフォーカスを外し、タイムラインにフォーカスを移す
            TimelineScrollViewer?.Focus(FocusState.Programmatic);
        };

        await dialog.ShowAsync();

        // ダイアログを閉じた後、テンプレート集ボタンからフォーカスを外し、タイムラインにフォーカスを移す
        TimelineScrollViewer?.Focus(FocusState.Programmatic);
    }

    private void OpenTemplatesButton_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Space)
        {
            e.Handled = true;
            TimelineScrollViewer?.Focus(FocusState.Programmatic);
            TogglePlayBack();
        }
    }

    // ==========================================
    // キー変更時自動移調
    // ==========================================

    private void KeyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        var newKey = (KeyComboBox.SelectedItem as string) ?? "C";
        if (newKey == _activeKey) return;

        var oldKey = _activeKey;
        _activeKey = newKey;

        if (_currentChords.Count > 0)
        {
            SaveState();
            var oldIndex = MusicEngine.NoteIndex(oldKey);
            var newIndex = MusicEngine.NoteIndex(newKey);
            var delta = (newIndex - oldIndex + 12) % 12;
            if (delta > 6) delta -= 12;

            _currentChords = MusicEngine.TransposeProgression(_currentChords, delta, newKey, CurrentMode);
            RefreshTimeline();
            SyncInputTextBoxFromChords();
            StatusTextBlock.Text = $"キーを {oldKey} から {newKey} へ変更し、進行をトランスポーズしました ({delta:+#;-#;0}半音)";
        }

        UpdateGuideAndSuggestions();
        RemoveFocusFromTopBar();
    }

    // ==========================================
    // 右側 GUIDE / 借用 / セカンダリー / 次候補
    // ==========================================

    private void ChordBadge_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SuggestionItem item)
        {
            PreviewChordSymbol(item.Symbol, item.RomanNumeral);
        }
    }

    private void ChordBadge_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (e.Items.FirstOrDefault() is SuggestionItem item)
        {
            e.Data.SetText(item.Symbol);
            e.Data.RequestedOperation = DataPackageOperation.Copy;
            StatusTextBlock.Text = $"'{item.Symbol}' をドラッグ中 (タイムラインへドロップしてください)";
        }
    }

    private void AddChordToTimeline(string symbol)
    {
        var parsed = MusicEngine.ParseSymbolToken(symbol, CurrentKey, CurrentMode);
        if (parsed == null) return;

        var newChord = parsed with
        {
            Id = $"chord-{Guid.NewGuid().ToString("N")[..8]}",
            Duration = "1 bar",
            Inversion = StylePreset.Root,
            CardWidth = CalculateCardWidth("1 bar", false)
        };

        _currentChords.Add(newChord);
        RefreshTimeline();
        SelectChord(newChord.Id, preview: true);

        SyncInputTextBoxFromChords();
        UpdateGuideAndSuggestions();
        StatusTextBlock.Text = $"'{symbol}' をタイムラインに追加しました";
    }

    private void PreviewChordSymbol(string symbol, string roman)
    {
        var parsed = MusicEngine.ParseSymbolToken(symbol, _activeKey, CurrentMode);
        if (parsed == null) return;

        var notes = MusicEngine.MidiNoteNumbers(parsed, CurrentBassAddition, CurrentOpenVoicing);
        AudioEngine.Instance.PlayNotes(notes, 800, _currentTone);
        StatusTextBlock.Text = $"試聴: {symbol} ({roman}) [{string.Join(", ", notes)}]";
    }

    private void ClearTimelineButton_Click(object sender, RoutedEventArgs e)
    {
        SaveState();
        _currentChords.Clear();
        _sectionMarkers.Clear();
        _selectedChordId = null;
        InputTextBox.Text = string.Empty;
        RefreshTimeline();
        UpdateGuideAndSuggestions();
        StatusTextBlock.Text = "タイムラインをクリアしました";
    }

    // ==========================================
    // 再生・自動スクロール・DAWドラッグ
    // ==========================================

    private async Task StartPlayback()
    {
        StopPlayback();

        if (_currentChords.Count == 0) return;

        _playbackCts = new CancellationTokenSource();
        var ct = _playbackCts.Token;

        try
        {
            StatusTextBlock.Text = "シーケンス再生中...";

            var playlist = _currentChords.ToList();
            double accumulatedOffset = 0.0;

            for (var i = 0; i < playlist.Count; i++)
            {
                if (ct.IsCancellationRequested) break;

                var chord = playlist[i];
                SelectChord(chord.Id, preview: false);

                // 再生ヘッド自動追従スクロール
                TimelineScrollViewer.ChangeView(Math.Max(0, accumulatedOffset - 40), null, null, false);
                accumulatedOffset += chord.CardWidth + 6;

                var notes = MusicEngine.MidiNoteNumbers(chord, CurrentBassAddition, CurrentOpenVoicing);
                var durationMs = MusicEngine.ChordDurationBeats(chord.Duration, "4/4", chord.Dotted) * (60_000.0 / CurrentBpm);

                AudioEngine.Instance.PlayNotes(notes, durationMs, _currentTone);

                await Task.Delay((int)durationMs, ct);
            }

            if (!ct.IsCancellationRequested)
            {
                StatusTextBlock.Text = "シーケンス再生終了";
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"再生エラー: {ex.Message}";
        }
    }

    private async void PlayAllButton_Click(object sender, RoutedEventArgs e)
    {
        await StartPlayback();
        RemoveFocusFromTopBar();
    }

    private async void TogglePlayBack()
    {
        if (_playbackCts != null)
        {
            StopPlayback();
            StatusTextBlock.Text = "停止しました";
        }
        else
        {
            await StartPlayback();
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        StopPlayback();
        StatusTextBlock.Text = "停止しました";
        RemoveFocusFromTopBar();
    }

    private void StopPlayback()
    {
        if (_playbackCts != null)
        {
            try { _playbackCts.Cancel(); } catch { }
            _playbackCts = null;
        }
        AudioEngine.Instance.StopAll();
    }

    private void ApplyInputButton_Click(object sender, RoutedEventArgs e) => ApplyInput();

    private void InputTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter &&
            Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
        {
            e.Handled = true;
            ApplyInput();
        }
    }

    private void ModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitialized)
        {
            ApplyInput();
        }
        RemoveFocusFromTopBar();
    }

    private void BpmBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        // BPM値の変更は再生テンポにのみ影響するため、コード進行の再パースやタイムライン再構築は一切行わない（完全ゼロ負荷）
        RemoveFocusFromTopBar();
    }

    private void BassComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // ベース音追加の変更は選択中コードの構成音インスペクター表示にのみ反映
        if (_isInitialized && SelectedChord != null)
        {
            UpdateInspector(SelectedChord);
        }
        RemoveFocusFromTopBar();
    }

    private void ToneComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ToneComboBox.SelectedItem is ComboBoxItem item &&
            Enum.TryParse<PlaybackTone>(item.Tag?.ToString(), out var tone))
        {
            _currentTone = tone;
            if (StatusTextBlock != null)
            {
                StatusTextBlock.Text = $"音色を '{_currentTone}' に設定しました";
            }
            RemoveFocusFromTopBar();
        }
    }

    // ==========================================
    // タップテンポ (Pro Tools スタイル)
    // ==========================================

    private readonly List<long> _tapTimestamps = new();
    private const double MaxTapIntervalSec = 2.0;

    private void BpmLabel_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        e.Handled = true;
        BpmLabelBorder.Focus(FocusState.Programmatic);
    }

    private void BpmLabel_GotFocus(object sender, RoutedEventArgs e)
    {
        BpmLabelBorder.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        BpmLabelBorder.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"];
        StatusTextBlock.Text = "タップテンポ待機中: [T]キーをテンポよく叩いてください (Enterで確定)";
    }

    private void BpmLabel_LostFocus(object sender, RoutedEventArgs e)
    {
        BpmLabelBorder.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        BpmLabelBorder.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        _tapTimestamps.Clear();
    }

    private void BpmLabel_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.T)
        {
            e.Handled = true;
            ProcessTapTempo();
        }
        else if (e.Key == VirtualKey.Enter || e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            TimelineItemsControl.Focus(FocusState.Programmatic);
        }
    }

    private void BpmBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.T)
        {
            e.Handled = true;
            ProcessTapTempo();
        }
        else if (e.Key == VirtualKey.Enter || e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            RemoveFocusFromTopBar();
        }
        else if (e.Key == VirtualKey.Space)
        {
            e.Handled = true;
            RemoveFocusFromTopBar();
            TogglePlayBack();
        }
    }

    private void ProcessTapTempo()
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var freq = (double)System.Diagnostics.Stopwatch.Frequency;

        if (_tapTimestamps.Count > 0)
        {
            var lastElapsed = (now - _tapTimestamps[^1]) / freq;
            if (lastElapsed > MaxTapIntervalSec)
            {
                _tapTimestamps.Clear();
            }
        }

        _tapTimestamps.Add(now);
        if (_tapTimestamps.Count > 8)
        {
            _tapTimestamps.RemoveAt(0);
        }

        // タップフィードバック音（短いクリック）
        AudioEngine.Instance.PlayNotes(new[] { 76 }, 40, _currentTone);

        if (_tapTimestamps.Count >= 2)
        {
            var intervals = new List<double>();
            for (var i = 1; i < _tapTimestamps.Count; i++)
            {
                intervals.Add((_tapTimestamps[i] - _tapTimestamps[i - 1]) / freq);
            }

            var avgSec = intervals.Average();
            if (avgSec > 0.05)
            {
                var calculatedBpm = Math.Clamp(Math.Round(60.0 / avgSec), 40, 300);
                BpmBox.Value = calculatedBpm;
                StatusTextBlock.Text = $"タップテンポ: {calculatedBpm} BPM ({_tapTimestamps.Count} taps)";
            }
        }
        else
        {
            StatusTextBlock.Text = "タップテンポ: 1 回目を記録... 続けて[T]キーを叩いてください";
        }
    }

    // ==========================================
    // プロジェクト一時ファイルの自動保存 (Project Backup)
    // ==========================================

    private void InitAutoSaveTimer()
    {
        _autoSaveTimer.Tick += AutoSaveTimer_Tick;
        UpdateAutoSaveTimer();
    }

    private void UpdateAutoSaveTimer()
    {
        var settings = SettingsManager.Current;
        if (settings.EnableAutoSave)
        {
            var interval = Math.Max(1, settings.AutoSaveIntervalMinutes);
            _autoSaveTimer.Interval = TimeSpan.FromMinutes(interval);
            if (!_autoSaveTimer.IsEnabled)
            {
                _autoSaveTimer.Start();
            }
        }
        else
        {
            if (_autoSaveTimer.IsEnabled)
            {
                _autoSaveTimer.Stop();
            }
        }
    }

    private async void AutoSaveTimer_Tick(object? sender, object e)
    {
        await ExecuteAutoSaveAsync();
    }

    private async Task ExecuteAutoSaveAsync()
    {
        var settings = SettingsManager.Current;
        if (!settings.EnableAutoSave) return;

        // タイムラインにコードがない場合はスキップ
        if (_currentChords.Count == 0) return;

        try
        {
            var currentPath = _currentProjectPath;
            var maxBackups = Math.Max(1, settings.AutoSaveMaxBackups);
            var project = BuildCurrentProjectData();

            string newBackupFileName = await Task.Run(async () =>
            {
                // 保存先ディレクトリ:
                // 既存ファイルが存在する場合はその親フォルダ配下の「Project Backup」
                // 未保存の新規プロジェクトの場合はローカルアプリデータ配下の「Project Backup」
                string baseFolder;
                string baseName;
                string extension;

                if (!string.IsNullOrEmpty(currentPath) && File.Exists(currentPath))
                {
                    baseFolder = Path.GetDirectoryName(currentPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    baseName = Path.GetFileNameWithoutExtension(currentPath);
                    extension = Path.GetExtension(currentPath);
                    if (string.IsNullOrEmpty(extension)) extension = ".chord";
                }
                else
                {
                    baseFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChordLaunchpad");
                    baseName = "Untitled";
                    extension = ".chord";
                }

                var backupFolder = Path.Combine(baseFolder, "Project Backup");
                if (!Directory.Exists(backupFolder))
                {
                    Directory.CreateDirectory(backupFolder);
                }

                // 既存の同名バックアップファイルを取得（連番ソート）
                var searchPattern = $"{baseName}_backup_*{extension}";
                var existingBackups = Directory.GetFiles(backupFolder, searchPattern)
                    .OrderBy(f => File.GetCreationTimeUtc(f))
                    .ToList();

                // 新しい連番の決定
                int nextNumber = 1;
                if (existingBackups.Count > 0)
                {
                    foreach (var file in existingBackups)
                    {
                        var stem = Path.GetFileNameWithoutExtension(file);
                        var parts = stem.Split("_backup_");
                        if (parts.Length == 2 && int.TryParse(parts[1], out var num))
                        {
                            if (num >= nextNumber)
                            {
                                nextNumber = num + 1;
                            }
                        }
                    }
                }

                var backupName = $"{baseName}_backup_{nextNumber:D2}{extension}";
                var backupPath = Path.Combine(backupFolder, backupName);

                var json = JsonSerializer.Serialize(project, AppJsonContext.Default.ProjectData);
                await File.WriteAllTextAsync(backupPath, json);

                // 最大保存数を超えている古い一時バックアップを削除
                var allBackups = Directory.GetFiles(backupFolder, searchPattern)
                    .OrderBy(f => File.GetCreationTimeUtc(f))
                    .ToList();

                while (allBackups.Count > maxBackups)
                {
                    var oldestFile = allBackups[0];
                    try
                    {
                        File.Delete(oldestFile);
                    }
                    catch
                    {
                        // 削除エラーは無視
                    }
                    allBackups.RemoveAt(0);
                }

                return backupName;
            });

            bool isEn = ChordLaunchpad.Core.LocalizationService.IsEnglish;
            StatusTextBlock.Text = isEn
                ? $"Auto-backup saved: {newBackupFileName}"
                : $"自動バックアップ保存完了: {newBackupFileName}";
        }
        catch (Exception ex)
        {
            // バックグラウンド自動保存のエラーでユーザー作業を阻害しないようログステータスにのみ通知
            bool isEn = ChordLaunchpad.Core.LocalizationService.IsEnglish;
            StatusTextBlock.Text = isEn
                ? $"Auto-save error: {ex.Message}"
                : $"自動保存エラー: {ex.Message}";
        }
    }

    // ==========================================
    // 上部セクション操作後のフォーカス外し・Space再生支援
    // ==========================================

    /// <summary>
    /// 上部セクション（ヘッダー・設定バー）の操作後にフォーカスをタイムラインへ戻し、Spaceキーでの再生等を有効化する
    /// </summary>
    private void RemoveFocusFromTopBar()
    {
        TimelineScrollViewer?.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// 上部セクション内の要素にフォーカスがある状態でSpaceキーが押された際、
    /// コントロールによるキー消費を防いで即座に再生/停止を行う
    /// </summary>
    private void TopSection_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Space)
        {
            e.Handled = true;
            RemoveFocusFromTopBar();
            TogglePlayBack();
        }
    }

    /// <summary>
    /// 上部バーの各 ComboBox のドロップダウンが閉じた際にフォーカスを外し、タイムラインをアクティブにする
    /// </summary>
    private void TopBarComboBox_DropDownClosed(object sender, object e)
    {
        RemoveFocusFromTopBar();
    }
}
