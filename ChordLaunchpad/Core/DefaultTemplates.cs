using System.Collections.Generic;
using ChordLaunchpad.Core.Models;

namespace ChordLaunchpad.Core;

public static class DefaultTemplates
{
    public static readonly IReadOnlyList<ProgressionTemplate> Templates = new List<ProgressionTemplate>
    {
        // ==========================================
        // 🇯🇵 ポップス・アニソン
        // ==========================================
        new(
            "oudo",
            "🔥 人気・定番",
            "王道進行 (4536)",
            "IVmaj7 V7 iiim7 vim7",
            "J-POP・アニソンで最も愛される大ヒット王道進行",
            "🇯🇵 ポップス・アニソン"
        ),
        new(
            "marusa",
            "🔥 人気・定番",
            "丸サ進行 (Just The Two of Us)",
            "IVmaj7 III7 vim7 I7",
            "椎名林檎やNeo Soulで絶大な人気を誇るお洒落ループ",
            "🇯🇵 ポップス・アニソン"
        ),
        new(
            "komuro",
            "🔥 人気・定番",
            "小室進行",
            "vi IV V I",
            "ドラマチックで力強い90年代〜現代ヒット進行",
            "🇯🇵 ポップス・アニソン"
        ),
        new(
            "canon",
            "🔥 人気・定番",
            "カノン進行",
            "I V vi iii IV I IV V",
            "誰もが耳にしたことのあるクラシック由来の感動進行",
            "🇯🇵 ポップス・アニソン"
        ),
        new(
            "pop-punk-four",
            "🔥 人気・定番",
            "4コード進行 (Let It Be)",
            "I V vi IV",
            "世界中で最も多くのヒット曲を生んだ4コード",
            "🇯🇵 ポップス・アニソン"
        ),
        new(
            "cycle",
            "🔥 人気・定番",
            "循環進行",
            "I VI ii V",
            "ジャズやスタンダードで回り続ける基本ループ",
            "🇯🇵 ポップス・アニソン"
        ),
        new(
            "confirmation-anime",
            "🇯🇵 J-POP・アニソン",
            "コンファメ進行 (Confirmation / アニソン進行)",
            "Imaj7 viim7b5 III7 vim7 Vm7 I7 IVmaj7",
            "『ハレ晴レユカイ』『M@STERPIECE』『Pretender』等で名高い、ジャズ由来の究極エモーショナル進行",
            "🇯🇵 ポップス・アニソン"
        ),
        new(
            "jpop-emotional",
            "🇯🇵 J-POP・アニソン",
            "青春エモ進行",
            "IVmaj7 V7 Imaj7 vi7",
            "爽やかさと切なさが両立するサビ進行",
            "🇯🇵 ポップス・アニソン"
        ),
        new(
            "subdominant-minor",
            "🇯🇵 J-POP・アニソン",
            "サブドミナントマイナー終止",
            "I V IV iv I",
            "iv (IVm) の借用和音で胸を締めつける切ない終止",
            "🇯🇵 ポップス・アニソン"
        ),
        new(
            "jrock-drive",
            "🇯🇵 J-POP・アニソン",
            "疾走J-Rockループ",
            "vi V IV V",
            "疾走感のあるAメロやイントロに最適なマイナードライブ",
            "🇯🇵 ポップス・アニソン"
        ),
        new(
            "anison-climax",
            "🇯🇵 J-POP・アニソン",
            "アニソン大サビ進行",
            "IV V iiim vi ii V I",
            "サビの後半で一気に盛り上げて完結するドラマチック進行",
            "🇯🇵 ポップス・アニソン"
        ),

        // ==========================================
        // ⚡ EDM / クラブミュージック
        // ==========================================
        new(
            "future-bass-kawaii",
            "⚡ Future Bass",
            "Future Bass 7thループ",
            "IVmaj7 V7 vim7 Imaj7",
            "豊かなボイシングでサイドチェインをかけると映えるモダンFuture Bass進行",
            "⚡ EDM / クラブミュージック"
        ),
        new(
            "future-bass-emotional",
            "⚡ Future Bass",
            "Future Bass エモーショナル展開",
            "IVmaj7 V7 iiim7 vim7 IVmaj7 V7 Imaj7",
            "広がりのあるシンセスタブで感情を高揚させる展開進行",
            "⚡ EDM / クラブミュージック"
        ),
        new(
            "progressive-house-anthem",
            "⚡ Progressive House",
            "フェス系アンセム4コード (Avicii Style)",
            "vi IV I V",
            "巨大フェスで大合唱を生み出すエバーグリーンなアンセム進行",
            "⚡ EDM / クラブミュージック"
        ),
        new(
            "progressive-house-buildup",
            "⚡ Progressive House",
            "プログレッシブ・ビルドアップ",
            "IV V vi I",
            "ドロップ（サビ）へ向けてエネルギーを極限まで溜め込む進行",
            "⚡ EDM / クラブミュージック"
        ),
        new(
            "melodic-dubstep-illenium",
            "⚡ Melodic Dubstep",
            "メロディック・ダブステップ (Illenium Style)",
            "vi IV I V",
            "激しいドラムと美しいピアノ・スーパソーが絡み合うエモ系ベースミュージック",
            "⚡ EDM / クラブミュージック"
        ),
        new(
            "deep-house-minimal",
            "⚡ Deep House",
            "ディープハウス・マイナー",
            "i v VI VII",
            "淡々と繰り返されるクラブトラック定番ミニマルループ",
            "⚡ EDM / クラブミュージック"
        ),
        new(
            "deep-house-classic",
            "⚡ Deep House",
            "90s クラブ・グルーヴ",
            "im7 ivm7 vm7 im7",
            "ソウルフルなコードスタブとオルガンベースに最適な90sハウス進行",
            "⚡ EDM / クラブミュージック"
        ),
        new(
            "synthwave-retro",
            "⚡ Synthwave / Retro",
            "80s シンセウェーブ・リフ",
            "i bVII bVI bVII",
            "アナログシンセのアルペジオと相性抜群のレトロフューチャー進行",
            "⚡ EDM / クラブミュージック"
        ),
        new(
            "synthwave-outrun",
            "⚡ Synthwave / Retro",
            "アウトラン・ドライブ",
            "i bVI bIII bVII",
            "夜のハイウェイを疾走するサイバーパンク・レトロウェーブ進行",
            "⚡ EDM / クラブミュージック"
        ),

        // ==========================================
        // 🏎️ ユーロビート (Eurobeat)
        // ==========================================
        new(
            "eurobeat-initial-d",
            "🏎️ ユーロビート",
            "哀愁ユーロ進行 (Initial D Classic)",
            "vi IV V I",
            "頭文字D・スーパーユーロビートの代名詞。疾走感と切なさが爆発する王道マイナーループ",
            "🏎️ ユーロビート"
        ),
        new(
            "eurobeat-hyper",
            "🏎️ ユーロビート",
            "ハイパー・ユーロビート",
            "IV V vi vi",
            "パラパラやハイパーユーロのサビで畳み掛ける強烈な高揚感",
            "🏎️ ユーロビート"
        ),
        new(
            "eurobeat-dejavu",
            "🏎️ ユーロビート",
            "哀愁メロディック・ユーロ (Deja Vu Cadence)",
            "vi IV V III7",
            "サビの終わりにIII7で緊張感を高め、次のループへと引き戻すドラマチック進行",
            "🏎️ ユーロビート"
        ),
        new(
            "eurobeat-parapara",
            "🏎️ ユーロビート",
            "90s Avex パラパラ進行 (Night of Fire Loop)",
            "vi V IV V",
            "90年代ダンスフロアを熱狂させたノンストップ・ユーロビートの基本形",
            "🏎️ ユーロビート"
        ),

        // ==========================================
        // 🎷 フュージョン (Fusion)
        // ==========================================
        new(
            "fusion-t-square",
            "🎷 フュージョン",
            "T-SQUARE風 爽快フュージョン (Breeze Fusion)",
            "IVmaj7 V iiim7 VI7 iim7 V7 Imaj7",
            "80〜90年代の日本のインスト・フュージョン黄金期を象徴する爽快でドライブ感ある進行",
            "🎷 フュージョン"
        ),
        new(
            "fusion-casiopea",
            "🎷 フュージョン",
            "CASIOPEA風 スラップ・ファンク・フュージョン",
            "iim7 V7 iiim7 VI7 iim7 V7 Imaj7",
            "タイトな16ビートスラップベースと相性抜群のキレ味鋭いファンキー進行",
            "🎷 フュージョン"
        ),
        new(
            "fusion-modern",
            "🎷 フュージョン",
            "Modern Fusion テンション進行",
            "IVmaj7 V7 iiim7 vim7 iim7 V7 Imaj7",
            "洗練されたボイシングとメロディの浮遊感を引き出すモダンフュージョン進行",
            "🎷 フュージョン"
        ),
        new(
            "fusion-mellow-cruising",
            "🎷 フュージョン",
            "メロウ・フュージョン・バラード (Mellow Cruising)",
            "IVmaj7 III7 vim7 Vm7 I7 IVmaj7 V7 Imaj7",
            "アーバンな夜のドライブを想起させる大人のフュージョンバラード",
            "🎷 フュージョン"
        ),

        // ==========================================
        // 🌃 City Pop・Neo Soul
        // ==========================================
        new(
            "citypop-breeze",
            "🌃 City Pop・Neo Soul",
            "80s シティポップ・ループ",
            "IVmaj7 iii7 vim7 ii7 V7",
            "都会的で浮遊感のあるテンションコード進行",
            "🌃 City Pop・Neo Soul"
        ),
        new(
            "neosoul-groove",
            "🌃 City Pop・Neo Soul",
            "ネオソウル・スムーズ",
            "iim7 V7 Imaj7 VI7",
            "グルーヴ感あふれるR&B/ソウルの定番2-5-1-6",
            "🌃 City Pop・Neo Soul"
        ),
        new(
            "backdoor-prog",
            "🌃 City Pop・Neo Soul",
            "バックドア終止進行",
            "Imaj7 IVmaj7 bVII7 Imaj7",
            "bVII7からIへ滑り込むお洒落な解決",
            "🌃 City Pop・Neo Soul"
        ),

        // ==========================================
        // ☕ Jazz・Lo-Fi
        // ==========================================
        new(
            "jazz-two-five-one",
            "☕ Jazz・Lo-Fi",
            "メジャー II-V-I",
            "iim7 V7 Imaj7",
            "すべてのポピュラー音楽の基盤となる重要終止",
            "☕ Jazz・Lo-Fi"
        ),
        new(
            "minor-two-five-one",
            "☕ Jazz・Lo-Fi",
            "マイナー II-V-I",
            "iim7b5 V7 im7",
            "ビターで大人びたLo-Fi HipHopに最適なマイナー進行",
            "☕ Jazz・Lo-Fi"
        ),
        new(
            "autumn-cadence",
            "☕ Jazz・Lo-Fi",
            "枯葉 (Autumn Leaves)",
            "iim7 V7 Imaj7 IVmaj7 viim7b5 III7 vim",
            "ジャズスタンダードの代名詞的な美しい進行",
            "☕ Jazz・Lo-Fi"
        ),

        // ==========================================
        // 🎸 Rock・Punk
        // ==========================================
        new(
            "greenday-punk",
            "🎸 Rock・Punk",
            "パンクロック定番",
            "I V vi IV",
            "パワーコードでストレートに突っ走る直球進行",
            "🎸 Rock・Punk"
        ),
        new(
            "grunge-loop",
            "🎸 Rock・Punk",
            "グランジ・オルタナティブ",
            "I bVII IV I",
            "bVIIのラフな歪み感が最高に心地よいロックループ",
            "🎸 Rock・Punk"
        ),
        new(
            "hardrock-riff",
            "🎸 Rock・Punk",
            "ハードロック・リフ進行",
            "i bVII bVI bVII",
            "ヘビーなリフと相性抜群のマイナーリフ進行",
            "🎸 Rock・Punk"
        )
    };
}
