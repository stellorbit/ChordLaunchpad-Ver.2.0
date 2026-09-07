# Microsoft Store 公開ガイド手順書 (Store Submission Guide)

本ドキュメントでは、ChordLaunchpad を Microsoft Store に提出・公開し、SmartScreen（警告画面）を完全に回避して世界中の Windows ユーザーへ届けるための全手順を解説します。

---

## 全体の流れ

1. **パートナーセンター個人開発者アカウントの登録**（初回 約$19 のみ・年額費用なし）
2. **アプリ名の予約と「製品 ID」の取得**
3. **Package.appxmanifest への ID 転記**
4. **Store 提出用 MSIX パッケージの生成**（package.ps1）
5. **パートナーセンターでの提出・審査申請**
6. **winget への登録申請**

---

## Step 1: パートナーセンター個人アカウントの登録

1. [Microsoft パートナーセンター](https://partner.microsoft.com/dashboard) にアクセスし、個人の Microsoft アカウントでサインインします。
2. 「Windows および Xbox」開発者プログラムの登録画面に進みます。
3. **アカウントの種類**: **「個人（Individual）」** を選択します。
4. **登録料の支払い**: クレジットカードにて約 $19（約 2,500〜3,000 円、初回 1 回のみ）を決済します。
5. 登録完了後、開発者ダッシュボードが開きます。

---

## Step 2: アプリ名の予約と「製品 ID」の取得

1. ダッシュボードの左メニュー「**アプリとゲーム (Apps & games)**」を開き、**「新しいアプリの作成 (Create a new app)」** をクリックします。
2. アプリ名として **ChordLaunchpad** を入力し、**「アプリ名の可用性を確認」→「製品名を予約」** をクリックします。
3. 予約完了後、左メニューの「**製品管理 (Product management)**」>「**製品 ID (Product Identity)**」を開きます。
4. 以下の **3 つの値** を控えます：
   - **パッケージ/ID/名前（Package/Identity/Name）**  
     （例: 12345stellorbit.ChordLaunchpad のような文字列）
   - **パッケージ/ID/発行者（Package/Identity/Publisher）**  
     （例: CN=XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX）
   - **プロパティ/発行者の表示名（Package/Properties/PublisherDisplayName）**  
     （例: stellorbit やご本名）

---

## Step 3: Package.appxmanifest への ID 転記

プロジェクト内の `ChordLaunchpad\Package.appxmanifest` には、既にパートナーセンターから取得された正式な値が設定されています：

```xml
  <Identity
    Name="Stellorbit.ChordLaunchpad"
    Publisher="CN=5035B553-447F-4A5D-A9CF-2DC176508228"
    Version="2.0.0.0" />

  <Properties>
    <DisplayName>ChordLaunchpad</DisplayName>
    <PublisherDisplayName>Stellorbit</PublisherDisplayName>
    <Logo>Assets\StoreLogo.png</Logo>
  </Properties>
```

---

## Step 4: Store 提出用 MSIX パッケージの生成

PowerShell を開き、リポジトリのルートで以下のコマンドを実行します：

`powershell
pwsh -File package.ps1
`

処理が完了すると、dist\ フォルダーに以下の提出用パッケージが生成されます：
- **dist\ChordLaunchpad_v2.0.0_x64.msix**

※ Store 提出用パッケージには手元の署名は不要です（アップロード時に Microsoft のインフラによって自動署名されます）。

---

## Step 5: パートナーセンターでの提出・審査申請

1. パートナーセンターのアプリ管理画面で **「申請の開始 (Start your submission)」** をクリックします。
2. 各項目を入力します：
   - **パッケージ (Packages)**:  
     dist\ChordLaunchpad_v2.0.0_x64.msix をドラッグ＆ドロップしてアップロードします。
   - **価格と使用可能性 (Pricing and availability)**:  
     - 価格: 「無料 (Free)」
     - 提供地域: すべての国/地域
   - **プロパティ (Properties)**:  
     - カテゴリ: 「マルチメディア (Music)」「開発者ツール」など
   - **年齢区分 (Age ratings)**:  
     簡単な質問票に回答（音楽制作ツールのため「すべての年齢」適合）。
   - **ストアの掲載情報 (Store listings)**:  
     - **説明**: README.md の主要機能説明をコピー＆ペースト。
     - **スクリーンショット**: アプリケーションの操作画面キャプチャを 1 枚以上アップロード。
     - **アプリアイコン**: ChordLaunchpad\Assets\StoreLogo.png（または Square150x150Logo.scale-200.png）。
3. すべてのセクションに緑色のチェックが付いたら、**「審査のために提出 (Submit to the Store)」** をクリックします。

審査はおおむね 24〜48 時間程度で完了し、自動的に Microsoft Store 上に公開されます。

---

## Step 6: winget への登録（2 つの方法）

### 方法 A: Microsoft Store 公開後に Store ソースとして登録（最も簡単）
Store 公開完了後、数日以内に winget は Store ソース（msstore）から自動認識可能になります。  
ユーザーは以下のようにインストールできます：
`powershell
winget install ChordLaunchpad --source msstore
`

### 方法 B: GitHub の winget-pkgs 公式リポジトリへ PR 提出
リポジトリの winget\manifests\s\stellorbit\ChordLaunchpad\2.0.0\ 配下に生成されているマニフェスト YAML ファイル群を、[microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs) リポジトリへフォーク＆プルリクエストすることで、winget install stellorbit.ChordLaunchpad として公式登録されます。
