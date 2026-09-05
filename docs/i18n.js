(() => {
  const translations = {
    'ja-JP': {
      native: '日本語', beta: '公開ベータ', feedback: '不具合を報告', guide: '3分で始める', title: 'RELYR — キーボードを、拡張する。',
      description: 'Spaceを押している間、すべてのキーがショートカットに変わる。キー、マウス、DeckからWindowsを動かします。',
      skip: '本文へ移動', nav: ['レイヤー', 'Deck', 'ワークフロー', 'ダウンロード'], download: 'ダウンロード',
      hero: ['RELYR ／ Windows入力システム', 'キーボードを、', '拡張する。', 'Spaceを押している間、すべてのキーがショートカットに変わる。キー、マウス、Deckから、アプリ起動、ウィンドウ操作、マクロを実行。', 'Windows版をダウンロード', '自分に合うか確認', '無料　／　オープンソース　／　WINDOWS 10—11　／　アカウント不要'],
      expand: '拡大して見る', video: ['デモを一時停止', 'デモを再生'],
      layers: ['Spaceを押す。<br>キーボード全体が、ショートカットになる。', '普段のキー入力を残したまま、押している間だけ役割を切り替えます。レイヤーごとに色が見えるので、どこに何を置いたかを画面上で確認できます。', '選択、複数移動、色分け、TAP / HOLDをひとつの面で編集'],
      actions: ['ひとつのActionを、<br>キーにも、マウスにも、Deckにも。', '押す、長押し、修飾クリック', 'EXE、ショートカット、URLを開く', '整列、モニター移動、仮想デスクトップ', 'マクロとマウスジェスチャーを実行', 'ランチャーとPC状態を画面に常設'],
      deck: ['画面の端を、<br>自分専用の操作列に。', 'アプリやファイルを置くだけで起動ボタンに。Windows操作とPCの状態表示も、同じDeckへ並べられます。', 'スクロールに沿って、Deck全体を見る。', '空いている場所へ、<br>Actionをドラッグ。', 'グリッドと一覧表示は同じDeckを編集します。ボタンの並び、色、アイコン、透明度、表示動作まで一か所で調整できます。', '複数ボタンをまとめて移動・着色', 'EXEとショートカットを直接登録', '複数Deckを同時に表示'],
      workflows: ['マクロも、マウスジェスチャーも。<br>キーやDeckから、すぐ呼び出せる。', '記録した手順とマウスジェスチャーを、ほかのActionと同じように割り当てられます。', '記録後も、順番と待機時間を編集。', '移動方向と短押しを別々のActionへ。'],
      fit: ['WHO RELYR IS FOR', 'AutoHotkeyやマクロキーボードで足りるなら、無理に替えなくていい。', 'RELYRは、コードを書き直さずに入力環境を組み替えたい人のための選択肢です。キー、マウス、ジェスチャー、Deckを同じActionから画面上で設定できます。', 'KEEP YOUR CURRENT SETUP', '今の環境を使い続ける', '既存のスクリプトや機器で目的を達成でき、コードの管理も苦にならないなら、その環境を替える必要はありません。', 'CHOOSE RELYR', '画面で設定を変えたい', 'RELYRが合うのは、次の操作をひとつの画面とAction一覧で管理したい場合です。', '押している間だけ有効になるキーレイヤー', 'キー、マウス、ジェスチャー、Deckへの共通Action', 'アプリ別プロファイルとローカル保存', 'TWO STARTER SETUPS', '最初の価値を、2つの設定で確かめる。', '大きな移行は不要です。普段の入力を残したまま、まず安全な操作をひとつだけ追加できます。', '片手でエクスプローラーを開く', 'Spaceレイヤーを選ぶ', 'Eキーを選ぶ', '「エクスプローラーを開く」を割り当てる', '保存し、Spaceを押しながらEを押す', '作業開始の操作をDeckへまとめる', 'Deckエディターを開く', 'EXEやショートカットを空き枠へドロップする', 'Windows操作やPCモニターのActionを追加する', '保存し、必要なDeckを表示する', 'Windows版を試す', '3分ガイドを読む', '開発を任意支援（Ko-fi）'],
      local: ['入力環境は、<br>PCの中で完結する。', 'RELYRは入力内容を外部へ送信しません。設定とマクロはローカルに保存。アカウント作成も必要ありません。', '入力内容の外部送信なし', '設定はAppDataに保存', 'MIT Licenseで公開', 'SHA-256を同時配布'],
      final: ['Windows 10 / 11で今すぐ利用可能', 'まず、ひとつのキーから。', 'Setup版にはMicrosoft公式の.NET Desktop Runtimeを同梱しています。インストール後の更新は、アプリ内から軽量版を取得できます。', 'RELYRをダウンロード', 'SHA-256を確認', '現在の公開ベータ版はコード署名前のため、Windows SmartScreenが「不明な発行元」と表示する場合があります。必ず公式GitHub Releasesから取得し、SHA-256を確認してください。', '無料', '不要'], close: '閉じる', screens: ['Space + E 設定例', 'Work Deck 設定例', 'Spaceレイヤー画面', 'Deckオーバーレイ', 'Deckエディター', 'マクロエディター', 'ジェスチャーエディター']
    },
    'en-US': {
      native: 'English', beta: 'Public Beta', feedback: 'Report a problem', guide: 'Three-minute guide', title: 'RELYR — Extend your keyboard.',
      description: 'Hold Space and every key becomes a shortcut. Control Windows from keys, mouse, and Deck.',
      skip: 'Skip to content', nav: ['Layers', 'Deck', 'Workflows', 'Download'], download: 'Download',
      hero: ['RELYR ／ Windows input system', 'Extend your', 'keyboard.', 'While you hold Space, every key becomes a shortcut. Launch apps, control windows, and run macros from keys, mouse, or Deck.', 'Download for Windows', 'See if RELYR fits', 'FREE　／　OPEN SOURCE　／　WINDOWS 10—11　／　NO ACCOUNT'],
      expand: 'View full size', video: ['Pause demo', 'Play demo'],
      layers: ['Hold Space.<br>Every key becomes a shortcut.', 'Keep normal typing intact and switch roles only while a layer key is held. Color shows every assignment at a glance.', 'Edit selection, multi-move, colors, TAP and HOLD in one workspace'],
      actions: ['One Action.<br>On keys, mouse, or Deck.', 'Press, hold, or modified click', 'Open EXEs, shortcuts, and URLs', 'Snap windows, move monitors, switch desktops', 'Run macros and mouse gestures', 'Keep launchers and PC status on screen'],
      deck: ['Turn the edge of the screen<br>into your command line.', 'Drop in an app or file to create a launcher. Add Windows controls and live PC status to the same Deck.', 'Scroll to travel across the full Deck.', 'Drag an Action<br>into any open slot.', 'Grid and list views edit the same Deck. Arrange buttons, color, icons, opacity, and display behavior in one place.', 'Move and recolor multiple buttons together', 'Drop EXEs and shortcuts directly', 'Show multiple Decks at once'],
      workflows: ['Macros and mouse gestures.<br>Ready from any key or Deck.', 'Assign recorded sequences and mouse gestures just like any other Action.', 'Edit order and timing after recording.', 'Map directions and a tap to separate Actions.'],
      fit: ['WHO RELYR IS FOR', 'If AutoHotkey or your macro keyboard already does the job, keep it.', 'RELYR is for people who want to reshape their input setup without rewriting code. Configure keys, mouse, gestures, and Deck visually from one Action catalog.', 'KEEP YOUR CURRENT SETUP', 'Stay with what already works', 'If your scripts or device software solve the problem and you are comfortable maintaining code, there is no reason to switch.', 'CHOOSE RELYR', 'Change the setup visually', 'RELYR fits when you want to manage these controls from one workspace and Action catalog.', 'Key layers active only while held', 'Shared Actions across keys, mouse, gestures, and Deck', 'Per-app profiles with local storage', 'TWO STARTER SETUPS', 'Prove the value with two small setups.', 'No migration is required. Keep normal input intact and add one safe action first.', 'Open File Explorer with one hand', 'Select the Space layer', 'Select the E key', 'Assign “Open File Explorer”', 'Save, then hold Space and press E', 'Put your work starters on a Deck', 'Open the Deck editor', 'Drop EXEs or shortcuts into open slots', 'Add Windows controls or PC monitor Actions', 'Save and show the Deck you need', 'Try the Windows build', 'Read the three-minute guide', 'Support development (Ko-fi)'],
      local: ['Your input environment<br>stays on your PC.', 'RELYR does not send input content outside your computer. Settings and macros stay local. No account required.', 'No external transmission of input content', 'Settings stored in AppData', 'Open under the MIT License', 'SHA-256 provided with every release'],
      final: ['Available now for Windows 10 / 11', 'Start with one key.', 'The Setup package includes Microsoft’s official .NET Desktop Runtime. Later updates use the lightweight installer from inside the app.', 'Download RELYR', 'Verify SHA-256', 'This public beta is not code-signed yet, so Windows SmartScreen may identify it as an unknown publisher. Download only from the official GitHub Releases page and verify its SHA-256 file.', 'Free', 'Not required'], close: 'Close', screens: ['Space + E setup', 'Work Deck setup', 'Space layer workspace', 'Deck overlay', 'Deck editor', 'Macro editor', 'Gesture editor']
    },
    'zh-CN': {
      native: '简体中文', beta: '公开测试版', feedback: '报告问题', guide: '三分钟入门', title: 'RELYR — 扩展你的键盘。',
      description: '按住 Space，每个按键都会变成快捷键。通过按键、鼠标和 Deck 控制 Windows。',
      skip: '跳到正文', nav: ['按键层', 'Deck', '工作流', '下载'], download: '下载',
      hero: ['RELYR ／ Windows 输入系统', '扩展你的', '键盘。', '按住 Space，每个按键都会变成快捷键。通过按键、鼠标或 Deck 启动应用、控制窗口并运行宏。', '下载 Windows 版', '看看是否适合你', '免费　／　开源　／　WINDOWS 10—11　／　无需账户'],
      expand: '查看大图', video: ['暂停演示', '播放演示'],
      layers: ['按住 Space。<br>整块键盘，瞬间变成快捷键。', '保留正常输入，仅在按住层按键时切换功能。颜色让所有分配一目了然。', '在同一界面完成选择、多项移动、配色及 TAP / HOLD 编辑'],
      actions: ['同一个 Action，<br>可用于按键、鼠标或 Deck。', '按下、长按或组合点击', '打开 EXE、快捷方式和网址', '排列窗口、跨显示器移动、切换桌面', '运行宏和鼠标手势', '在屏幕上常驻启动器与电脑状态'],
      deck: ['把屏幕边缘变成<br>专属操作列。', '拖入应用或文件即可创建启动按钮。Windows 操作与电脑状态也能放进同一个 Deck。', '随滚动浏览完整 Deck。', '将 Action 拖到<br>任意空位。', '网格和列表编辑同一个 Deck。按钮顺序、颜色、图标、透明度与显示方式均可集中调整。', '批量移动并更改多个按钮颜色', '直接拖入 EXE 与快捷方式', '同时显示多个 Deck'],
      workflows: ['宏与鼠标手势。<br>通过按键或 Deck，随时调用。', '录制的步骤与鼠标手势，可以像其他 Action 一样分配。', '录制后仍可编辑顺序与等待时间。', '为各方向和短按分别设置 Action。'],
      fit: ['RELYR 适合谁', '如果 AutoHotkey 或宏键盘已经够用，就不必更换。', 'RELYR 适合希望无需重写代码就能调整输入环境的人。按键、鼠标、手势和 Deck 都可通过同一个 Action 列表进行可视化设置。', '保留当前设置', '继续使用有效的工具', '如果现有脚本或设备软件已解决问题，并且你愿意维护代码，就没有必要切换。', '选择 RELYR', '通过界面更改设置', '当你希望从同一工作区和 Action 列表管理以下操作时，RELYR 更合适。', '仅在按住时启用的按键层', '按键、鼠标、手势和 Deck 共用 Action', '按应用切换配置并保存在本地', '两个入门设置', '用两个小设置确认价值。', '无需整体迁移。保留正常输入，先添加一个安全操作。', '单手打开文件资源管理器', '选择 Space 按键层', '选择 E 键', '分配“打开文件资源管理器”', '保存后按住 Space 再按 E', '把工作启动项集中到 Deck', '打开 Deck 编辑器', '将 EXE 或快捷方式拖入空位', '添加 Windows 控制或电脑监控 Action', '保存并显示所需的 Deck', '试用 Windows 版', '阅读三分钟指南', '支持开发（Ko-fi）'],
      local: ['输入环境，<br>只留在你的电脑中。', 'RELYR 不会向外发送输入内容。设置和宏保存在本地，也无需创建账户。', '不向外部传输输入内容', '设置保存在 AppData', '采用 MIT 许可证开源', '随版本提供 SHA-256'],
      final: ['现已支持 Windows 10 / 11', '从一个按键开始。', '安装包包含 Microsoft 官方 .NET Desktop Runtime。安装后的更新可在应用内获取轻量版本。', '下载 RELYR', '验证 SHA-256', '当前公开测试版尚未进行代码签名，Windows SmartScreen 可能会显示“未知发布者”。请仅从官方 GitHub Releases 页面下载并验证 SHA-256。', '免费', '无需'], close: '关闭', screens: ['Space + E 设置示例', 'Work Deck 设置示例', 'Space 按键层界面', 'Deck 浮层', 'Deck 编辑器', '宏编辑器', '手势编辑器']
    },
    'zh-TW': {
      native: '繁體中文', beta: '公開測試版', feedback: '回報問題', guide: '三分鐘入門', title: 'RELYR — 擴充你的鍵盤。',
      description: '按住 Space，每個按鍵都會變成快速鍵。透過按鍵、滑鼠和 Deck 控制 Windows。',
      skip: '跳到正文', nav: ['按鍵層', 'Deck', '工作流程', '下載'], download: '下載',
      hero: ['RELYR ／ Windows 輸入系統', '擴充你的', '鍵盤。', '按住 Space，每個按鍵都會變成快速鍵。透過按鍵、滑鼠或 Deck 啟動應用程式、控制視窗並執行巨集。', '下載 Windows 版', '看看是否適合你', '免費　／　開放原始碼　／　WINDOWS 10—11　／　無需帳戶'],
      expand: '查看大圖', video: ['暫停示範', '播放示範'],
      layers: ['按住 Space。<br>整個鍵盤，瞬間變成快速鍵。', '保留正常輸入，只在按住按鍵層時切換功能。色彩讓所有配置一目了然。', '在同一畫面編輯選取、多項移動、色彩及 TAP / HOLD'],
      actions: ['同一個 Action，<br>可用於按鍵、滑鼠或 Deck。', '按下、長按或組合點擊', '開啟 EXE、捷徑和網址', '排列視窗、跨顯示器移動、切換桌面', '執行巨集和滑鼠手勢', '在螢幕上常駐啟動器與電腦狀態'],
      deck: ['把螢幕邊緣變成<br>專屬操作列。', '拖入應用程式或檔案即可建立啟動按鈕。Windows 操作與電腦狀態也能放進同一個 Deck。', '隨捲動瀏覽完整 Deck。', '將 Action 拖到<br>任意空位。', '網格和清單編輯同一個 Deck。按鈕順序、色彩、圖示、透明度與顯示方式均可集中調整。', '批次移動並變更多個按鈕色彩', '直接拖入 EXE 與捷徑', '同時顯示多個 Deck'],
      workflows: ['巨集與滑鼠手勢。<br>透過按鍵或 Deck，隨時呼叫。', '錄製的步驟與滑鼠手勢，可以像其他 Action 一樣指派。', '錄製後仍可編輯順序與等待時間。', '為各方向和短按分別設定 Action。'],
      fit: ['RELYR 適合誰', '如果 AutoHotkey 或巨集鍵盤已經夠用，就不必更換。', 'RELYR 適合希望不用重寫程式碼就能調整輸入環境的人。按鍵、滑鼠、手勢和 Deck 都可透過同一個 Action 清單進行視覺化設定。', '保留目前設定', '繼續使用有效的工具', '如果現有腳本或裝置軟體已解決問題，而且你願意維護程式碼，就沒有必要切換。', '選擇 RELYR', '透過畫面更改設定', '當你希望從同一工作區和 Action 清單管理以下操作時，RELYR 更合適。', '只在按住時啟用的按鍵層', '按鍵、滑鼠、手勢和 Deck 共用 Action', '依應用程式切換設定並保存在本機', '兩個入門設定', '用兩個小設定確認價值。', '不必整體移轉。保留正常輸入，先加入一個安全操作。', '單手開啟檔案總管', '選擇 Space 按鍵層', '選擇 E 鍵', '指派「開啟檔案總管」', '儲存後按住 Space 再按 E', '把工作啟動項集中到 Deck', '開啟 Deck 編輯器', '將 EXE 或捷徑拖入空位', '加入 Windows 控制或電腦監控 Action', '儲存並顯示所需的 Deck', '試用 Windows 版', '閱讀三分鐘指南', '支持開發（Ko-fi）'],
      local: ['輸入環境，<br>只留在你的電腦中。', 'RELYR 不會向外傳送輸入內容。設定和巨集保存在本機，也無需建立帳戶。', '不向外部傳輸輸入內容', '設定保存在 AppData', '採用 MIT 授權開源', '隨版本提供 SHA-256'],
      final: ['現已支援 Windows 10 / 11', '從一個按鍵開始。', '安裝程式包含 Microsoft 官方 .NET Desktop Runtime。安裝後的更新可在應用程式內取得輕量版本。', '下載 RELYR', '驗證 SHA-256', '目前公開測試版尚未進行程式碼簽署，Windows SmartScreen 可能顯示「未知的發行者」。請只從官方 GitHub Releases 頁面下載並驗證 SHA-256。', '免費', '不需要'], close: '關閉', screens: ['Space + E 設定範例', 'Work Deck 設定範例', 'Space 按鍵層畫面', 'Deck 浮層', 'Deck 編輯器', '巨集編輯器', '手勢編輯器']
    },
    'ko-KR': {
      native: '한국어', beta: '공개 베타', feedback: '문제 신고', guide: '3분 시작 가이드', title: 'RELYR — 키보드를 확장하세요.',
      description: 'Space를 누르는 동안 모든 키가 단축키가 됩니다. 키, 마우스와 Deck에서 Windows를 제어하세요.',
      skip: '본문으로 이동', nav: ['레이어', 'Deck', '워크플로', '다운로드'], download: '다운로드',
      hero: ['RELYR ／ Windows 입력 시스템', '키보드를', '확장하세요.', 'Space를 누르는 동안 모든 키가 단축키가 됩니다. 키, 마우스 또는 Deck에서 앱 실행, 창 제어와 매크로를 실행하세요.', 'Windows용 다운로드', '나에게 맞는지 확인', '무료　／　오픈 소스　／　WINDOWS 10—11　／　계정 불필요'],
      expand: '크게 보기', video: ['데모 일시 정지', '데모 재생'],
      layers: ['Space를 누르면<br>키보드 전체가 단축키가 됩니다.', '평소 입력은 그대로 유지하고 레이어 키를 누르는 동안에만 역할을 전환합니다. 색상으로 모든 할당을 한눈에 확인할 수 있습니다.', '선택, 다중 이동, 색상, TAP / HOLD를 한 화면에서 편집'],
      actions: ['하나의 Action을<br>키, 마우스, Deck 어디서나.', '누르기, 길게 누르기, 조합 클릭', 'EXE, 바로가기와 URL 열기', '창 정렬, 모니터 이동, 데스크톱 전환', '매크로와 마우스 제스처 실행', '런처와 PC 상태를 화면에 상시 표시'],
      deck: ['화면 가장자리를<br>나만의 명령줄로.', '앱이나 파일을 놓으면 실행 버튼이 됩니다. Windows 동작과 PC 상태도 같은 Deck에 배치할 수 있습니다.', '스크롤하며 전체 Deck을 확인하세요.', '빈 칸에 Action을<br>드래그하세요.', '그리드와 목록은 같은 Deck을 편집합니다. 버튼 순서, 색상, 아이콘, 투명도와 표시 동작을 한곳에서 조정합니다.', '여러 버튼을 함께 이동하고 색상 변경', 'EXE와 바로가기를 바로 등록', '여러 Deck을 동시에 표시'],
      workflows: ['매크로와 마우스 제스처.<br>키나 Deck에서 바로 실행하세요.', '기록한 순서와 마우스 제스처를 다른 Action과 같은 방식으로 할당합니다.', '기록 후에도 순서와 대기 시간을 편집.', '방향과 짧게 누르기를 각각 다른 Action으로.'],
      fit: ['RELYR가 맞는 사람', 'AutoHotkey나 매크로 키보드로 충분하다면 바꿀 필요가 없습니다.', 'RELYR는 코드를 다시 쓰지 않고 입력 환경을 바꾸고 싶은 사람을 위한 선택지입니다. 키, 마우스, 제스처와 Deck을 하나의 Action 목록에서 화면으로 설정합니다.', '현재 설정 유지', '이미 잘 되는 도구를 계속 사용', '기존 스크립트나 장치 소프트웨어가 문제를 해결하고 코드 관리도 괜찮다면 전환할 이유가 없습니다.', 'RELYR 선택', '화면에서 설정 변경', '다음 조작을 하나의 작업 공간과 Action 목록에서 관리하고 싶을 때 RELYR가 적합합니다.', '누르는 동안만 활성화되는 키 레이어', '키, 마우스, 제스처와 Deck에서 공유하는 Action', '앱별 프로필과 로컬 저장', '두 가지 시작 설정', '작은 설정 두 개로 가치를 확인하세요.', '전체 이전은 필요 없습니다. 일반 입력을 유지하고 안전한 동작 하나부터 추가하세요.', '한 손으로 파일 탐색기 열기', 'Space 레이어 선택', 'E 키 선택', '“파일 탐색기 열기” 할당', '저장 후 Space를 누른 채 E 누르기', '작업 시작 항목을 Deck에 모으기', 'Deck 편집기 열기', 'EXE나 바로가기를 빈 칸에 놓기', 'Windows 제어나 PC 모니터 Action 추가', '저장하고 필요한 Deck 표시', 'Windows 버전 사용해 보기', '3분 가이드 읽기', '개발 지원하기 (Ko-fi)'],
      local: ['입력 환경은<br>PC 안에서 완결됩니다.', 'RELYR는 입력 내용을 외부로 보내지 않습니다. 설정과 매크로는 로컬에 저장되며 계정도 필요 없습니다.', '입력 내용 외부 전송 없음', '설정은 AppData에 저장', 'MIT License로 공개', '릴리스마다 SHA-256 제공'],
      final: ['Windows 10 / 11에서 지금 사용 가능', '키 하나부터 시작하세요.', '설치 패키지에는 Microsoft 공식 .NET Desktop Runtime이 포함됩니다. 이후 업데이트는 앱 안에서 경량 버전을 받습니다.', 'RELYR 다운로드', 'SHA-256 확인', '현재 공개 베타는 코드 서명 전이므로 Windows SmartScreen에 알 수 없는 게시자로 표시될 수 있습니다. 공식 GitHub Releases에서만 다운로드하고 SHA-256을 확인하세요.', '무료', '불필요'], close: '닫기', screens: ['Space + E 설정 예시', 'Work Deck 설정 예시', 'Space 레이어 화면', 'Deck 오버레이', 'Deck 편집기', '매크로 편집기', '제스처 편집기']
    },
    'fr-FR': {
      native: 'Français', beta: 'Bêta publique', feedback: 'Signaler un problème', guide: 'Guide de démarrage', title: 'RELYR — Étendez votre clavier.',
      description: 'Maintenez Espace : chaque touche devient un raccourci. Contrôlez Windows depuis le clavier, la souris et le Deck.',
      skip: 'Aller au contenu', nav: ['Calques', 'Deck', 'Flux', 'Télécharger'], download: 'Télécharger',
      hero: ['RELYR ／ Système de saisie Windows', 'Étendez votre', 'clavier.', 'Maintenez Espace : chaque touche devient un raccourci. Lancez des apps, contrôlez les fenêtres et exécutez des macros depuis les touches, la souris ou le Deck.', 'Télécharger pour Windows', 'Vérifier si RELYR convient', 'GRATUIT　／　OPEN SOURCE　／　WINDOWS 10—11　／　SANS COMPTE'],
      expand: 'Agrandir', video: ['Mettre la démo en pause', 'Lire la démo'],
      layers: ['Maintenez Espace.<br>Chaque touche devient un raccourci.', 'La saisie normale reste intacte ; les rôles ne changent que pendant l’appui. Les couleurs rendent chaque affectation immédiatement visible.', 'Sélection, déplacement multiple, couleurs, TAP et HOLD sur un même écran'],
      actions: ['Une Action.<br>Sur les touches, la souris ou le Deck.', 'Appui, appui long ou clic modifié', 'Ouvrir EXE, raccourcis et URL', 'Aligner les fenêtres, changer d’écran ou de bureau', 'Exécuter macros et gestes de souris', 'Garder lanceurs et état du PC à l’écran'],
      deck: ['Transformez le bord de l’écran<br>en ligne de commande.', 'Déposez une app ou un fichier pour créer un lanceur. Ajoutez les commandes Windows et l’état du PC au même Deck.', 'Faites défiler l’intégralité du Deck.', 'Glissez une Action<br>dans un emplacement libre.', 'La grille et la liste modifient le même Deck. Ordre, couleurs, icônes, opacité et affichage se règlent au même endroit.', 'Déplacer et recolorer plusieurs boutons', 'Déposer directement EXE et raccourcis', 'Afficher plusieurs Decks à la fois'],
      workflows: ['Macros et gestes de souris.<br>Déclenchez-les depuis une touche ou le Deck.', 'Affectez séquences et gestes de souris comme n’importe quelle autre Action.', 'Modifier l’ordre et les délais après enregistrement.', 'Associer directions et appui court à des Actions distinctes.'],
      fit: ['À QUI S’ADRESSE RELYR', 'Si AutoHotkey ou votre clavier macro suffit déjà, gardez-le.', 'RELYR s’adresse à ceux qui veulent modifier leur environnement de saisie sans réécrire de code. Configurez visuellement touches, souris, gestes et Deck depuis un même catalogue d’Actions.', 'GARDER VOTRE CONFIGURATION', 'Conserver ce qui fonctionne', 'Si vos scripts ou le logiciel de votre périphérique répondent au besoin et que maintenir du code ne vous gêne pas, inutile de changer.', 'CHOISIR RELYR', 'Modifier la configuration visuellement', 'RELYR convient lorsque vous voulez gérer ces commandes depuis un espace et un catalogue d’Actions uniques.', 'Calques actifs uniquement pendant l’appui', 'Actions partagées entre touches, souris, gestes et Deck', 'Profils par application et stockage local', 'DEUX CONFIGURATIONS DE DÉPART', 'Vérifiez la valeur avec deux petits réglages.', 'Aucune migration complète. Gardez la saisie normale et ajoutez d’abord une action sûre.', 'Ouvrir l’Explorateur d’une seule main', 'Sélectionner le calque Espace', 'Sélectionner la touche E', 'Affecter « Ouvrir l’Explorateur de fichiers »', 'Enregistrer, puis maintenir Espace et appuyer sur E', 'Regrouper les outils de travail dans un Deck', 'Ouvrir l’éditeur de Deck', 'Déposer EXE ou raccourcis dans les cases libres', 'Ajouter des commandes Windows ou des Actions de suivi PC', 'Enregistrer et afficher le Deck voulu', 'Essayer la version Windows', 'Lire le guide de trois minutes', 'Soutenir le développement (Ko-fi)'],
      local: ['Votre environnement de saisie<br>reste sur votre PC.', 'RELYR n’envoie aucun contenu de saisie. Réglages et macros restent en local. Aucun compte requis.', 'Aucun envoi externe du contenu saisi', 'Réglages stockés dans AppData', 'Publié sous licence MIT', 'SHA-256 fourni avec chaque version'],
      final: ['Disponible pour Windows 10 / 11', 'Commencez par une touche.', 'Le programme d’installation inclut le .NET Desktop Runtime officiel de Microsoft. Les mises à jour suivantes utilisent la version légère intégrée.', 'Télécharger RELYR', 'Vérifier SHA-256', 'Cette bêta publique n’est pas encore signée ; Windows SmartScreen peut afficher un éditeur inconnu. Téléchargez-la uniquement depuis la page GitHub Releases officielle et vérifiez le SHA-256.', 'Gratuit', 'Non requis'], close: 'Fermer', screens: ['Configuration Space + E', 'Configuration Work Deck', 'Écran du calque Space', 'Overlay Deck', 'Éditeur de Deck', 'Éditeur de macros', 'Éditeur de gestes']
    },
    'de-DE': {
      native: 'Deutsch', beta: 'Öffentliche Beta', feedback: 'Problem melden', guide: 'Schnellstart', title: 'RELYR — Erweitere deine Tastatur.',
      description: 'Halte die Leertaste: Jede Taste wird zum Shortcut. Steuere Windows über Tasten, Maus und Deck.',
      skip: 'Zum Inhalt', nav: ['Ebenen', 'Deck', 'Abläufe', 'Download'], download: 'Download',
      hero: ['RELYR ／ Windows-Eingabesystem', 'Erweitere deine', 'Tastatur.', 'Halte die Leertaste: Jede Taste wird zum Shortcut. Starte Apps, steuere Fenster und führe Makros über Tasten, Maus oder Deck aus.', 'Für Windows herunterladen', 'Prüfen, ob RELYR passt', 'KOSTENLOS　／　OPEN SOURCE　／　WINDOWS 10—11　／　KEIN KONTO'],
      expand: 'Groß anzeigen', video: ['Demo pausieren', 'Demo abspielen'],
      layers: ['Leertaste halten.<br>Jede Taste wird zum Shortcut.', 'Normale Eingaben bleiben erhalten; Funktionen wechseln nur solange die Ebenentaste gedrückt ist. Farben zeigen jede Belegung sofort.', 'Auswahl, Mehrfachverschieben, Farben, TAP und HOLD auf einer Fläche'],
      actions: ['Eine Action.<br>Auf Taste, Maus oder Deck.', 'Drücken, halten oder modifiziert klicken', 'EXE, Verknüpfungen und URLs öffnen', 'Fenster anordnen, Monitor oder Desktop wechseln', 'Makros und Mausgesten ausführen', 'Starter und PC-Status auf dem Bildschirm halten'],
      deck: ['Mach den Bildschirmrand<br>zu deiner Befehlsleiste.', 'App oder Datei ablegen und einen Starter erstellen. Windows-Steuerung und PC-Status passen in dasselbe Deck.', 'Beim Scrollen das ganze Deck durchlaufen.', 'Eine Action in einen<br>freien Platz ziehen.', 'Raster und Liste bearbeiten dasselbe Deck. Reihenfolge, Farben, Symbole, Deckkraft und Anzeige an einem Ort einstellen.', 'Mehrere Tasten gemeinsam verschieben und färben', 'EXE-Dateien und Verknüpfungen direkt ablegen', 'Mehrere Decks gleichzeitig anzeigen'],
      workflows: ['Makros und Mausgesten.<br>Direkt per Taste oder Deck auslösen.', 'Aufgezeichnete Abläufe und Mausgesten wie jede andere Action belegen.', 'Reihenfolge und Wartezeiten nachträglich ändern.', 'Richtungen und kurzes Tippen getrennt belegen.'],
      fit: ['FÜR WEN RELYR PASST', 'Wenn AutoHotkey oder deine Makrotastatur genügt, bleib dabei.', 'RELYR ist für Menschen, die ihre Eingabeumgebung ohne neues Programmieren ändern möchten. Tasten, Maus, Gesten und Deck werden visuell aus einem Action-Katalog konfiguriert.', 'AKTUELLE EINRICHTUNG BEHALTEN', 'Behalten, was bereits funktioniert', 'Wenn Skripte oder Gerätesoftware das Problem lösen und Codepflege kein Problem ist, gibt es keinen Grund zu wechseln.', 'RELYR WÄHLEN', 'Einrichtung visuell ändern', 'RELYR passt, wenn du diese Steuerungen in einer Arbeitsfläche und einem Action-Katalog verwalten willst.', 'Tastenebenen, die nur beim Halten aktiv sind', 'Gemeinsame Actions für Tasten, Maus, Gesten und Deck', 'App-Profile mit lokaler Speicherung', 'ZWEI STARTKONFIGURATIONEN', 'Prüfe den Nutzen mit zwei kleinen Setups.', 'Keine vollständige Migration. Normale Eingabe bleibt erhalten; füge zuerst eine sichere Action hinzu.', 'Datei-Explorer mit einer Hand öffnen', 'Space-Ebene auswählen', 'Taste E auswählen', '„Datei-Explorer öffnen“ zuweisen', 'Speichern, dann Space halten und E drücken', 'Arbeitsstarter in einem Deck sammeln', 'Deck-Editor öffnen', 'EXE oder Verknüpfungen in freie Plätze ziehen', 'Windows-Steuerungen oder PC-Monitor-Actions hinzufügen', 'Speichern und gewünschtes Deck anzeigen', 'Windows-Version testen', 'Drei-Minuten-Anleitung lesen', 'Entwicklung unterstützen (Ko-fi)'],
      local: ['Deine Eingabeumgebung<br>bleibt auf deinem PC.', 'RELYR sendet keine Eingabeinhalte nach außen. Einstellungen und Makros bleiben lokal. Kein Konto erforderlich.', 'Keine externe Übertragung von Eingaben', 'Einstellungen in AppData gespeichert', 'Unter MIT-Lizenz veröffentlicht', 'SHA-256 zu jeder Version'],
      final: ['Jetzt für Windows 10 / 11', 'Beginne mit einer Taste.', 'Das Setup enthält Microsofts offizielle .NET Desktop Runtime. Spätere Updates nutzen den schlanken Installer in der App.', 'RELYR herunterladen', 'SHA-256 prüfen', 'Diese öffentliche Beta ist noch nicht codesigniert; Windows SmartScreen kann einen unbekannten Herausgeber anzeigen. Lade sie nur von der offiziellen GitHub-Releases-Seite herunter und prüfe SHA-256.', 'Kostenlos', 'Nicht erforderlich'], close: 'Schließen', screens: ['Space + E Setup', 'Work-Deck-Setup', 'Space-Ebene', 'Deck-Overlay', 'Deck-Editor', 'Makro-Editor', 'Gesten-Editor']
    },
    'es-ES': {
      native: 'Español', beta: 'Beta pública', feedback: 'Informar de un problema', guide: 'Guía de inicio', title: 'RELYR — Amplía tu teclado.',
      description: 'Mantén Espacio y cada tecla se convierte en un atajo. Controla Windows desde el teclado, el ratón y el Deck.',
      skip: 'Ir al contenido', nav: ['Capas', 'Deck', 'Flujos', 'Descargar'], download: 'Descargar',
      hero: ['RELYR ／ Sistema de entrada para Windows', 'Amplía tu', 'teclado.', 'Mantén Espacio y cada tecla se convierte en un atajo. Abre apps, controla ventanas y ejecuta macros desde teclas, ratón o Deck.', 'Descargar para Windows', 'Ver si RELYR encaja', 'GRATIS　／　CÓDIGO ABIERTO　／　WINDOWS 10—11　／　SIN CUENTA'],
      expand: 'Ver a tamaño completo', video: ['Pausar demo', 'Reproducir demo'],
      layers: ['Mantén Espacio.<br>Cada tecla se convierte en un atajo.', 'La escritura normal se mantiene; las funciones solo cambian mientras pulsas la tecla de capa. El color muestra cada asignación de un vistazo.', 'Selección, movimiento múltiple, color, TAP y HOLD en una sola vista'],
      actions: ['Una Action.<br>En teclas, ratón o Deck.', 'Pulsar, mantener o clic modificado', 'Abrir EXE, accesos directos y URL', 'Alinear ventanas, cambiar monitor o escritorio', 'Ejecutar macros y gestos del ratón', 'Mantener lanzadores y estado del PC en pantalla'],
      deck: ['Convierte el borde de la pantalla<br>en tu línea de comandos.', 'Suelta una app o archivo para crear un lanzador. Añade controles de Windows y el estado del PC al mismo Deck.', 'Desplázate por todo el Deck.', 'Arrastra una Action<br>a cualquier espacio libre.', 'La cuadrícula y la lista editan el mismo Deck. Orden, color, iconos, opacidad y visualización se ajustan en un solo lugar.', 'Mover y recolorear varios botones', 'Soltar EXE y accesos directos directamente', 'Mostrar varios Decks a la vez'],
      workflows: ['Macros y gestos del ratón.<br>Actívalos desde una tecla o el Deck.', 'Asigna secuencias y gestos del ratón igual que cualquier otra Action.', 'Edita el orden y los tiempos después de grabar.', 'Asigna direcciones y toque corto a Actions distintas.'],
      fit: ['PARA QUIÉN ES RELYR', 'Si AutoHotkey o tu teclado de macros ya basta, sigue usándolo.', 'RELYR es para quienes quieren cambiar su entorno de entrada sin reescribir código. Configura visualmente teclas, ratón, gestos y Deck desde un mismo catálogo de Actions.', 'MANTENER TU CONFIGURACIÓN', 'Conservar lo que ya funciona', 'Si tus scripts o el software del dispositivo resuelven el problema y no te molesta mantener código, no hay motivo para cambiar.', 'ELEGIR RELYR', 'Cambiar la configuración visualmente', 'RELYR encaja cuando quieres gestionar estos controles desde un solo espacio y catálogo de Actions.', 'Capas activas solo mientras mantienes pulsado', 'Actions compartidas entre teclas, ratón, gestos y Deck', 'Perfiles por aplicación y almacenamiento local', 'DOS CONFIGURACIONES INICIALES', 'Comprueba el valor con dos ajustes pequeños.', 'No hace falta migrar todo. Mantén la entrada normal y añade primero una acción segura.', 'Abrir el Explorador con una mano', 'Selecciona la capa Space', 'Selecciona la tecla E', 'Asigna “Abrir el Explorador de archivos”', 'Guarda, mantén Space y pulsa E', 'Reúne tus accesos de trabajo en un Deck', 'Abre el editor de Deck', 'Suelta EXE o accesos directos en espacios libres', 'Añade controles de Windows o Actions de monitorización', 'Guarda y muestra el Deck necesario', 'Probar la versión para Windows', 'Leer la guía de tres minutos', 'Apoyar el desarrollo (Ko-fi)'],
      local: ['Tu entorno de entrada<br>se queda en tu PC.', 'RELYR no envía contenido de entrada fuera del equipo. Los ajustes y macros son locales. No se necesita cuenta.', 'Sin transmisión externa de entradas', 'Ajustes guardados en AppData', 'Publicado con licencia MIT', 'SHA-256 incluido con cada versión'],
      final: ['Disponible para Windows 10 / 11', 'Empieza con una tecla.', 'El instalador incluye .NET Desktop Runtime oficial de Microsoft. Las siguientes actualizaciones usan la versión ligera desde la app.', 'Descargar RELYR', 'Verificar SHA-256', 'Esta beta pública aún no está firmada; Windows SmartScreen puede mostrar un editor desconocido. Descárgala solo desde la página oficial de GitHub Releases y verifica el SHA-256.', 'Gratis', 'No necesaria'], close: 'Cerrar', screens: ['Configuración Space + E', 'Configuración Work Deck', 'Pantalla de capa Space', 'Superposición Deck', 'Editor de Deck', 'Editor de macros', 'Editor de gestos']
    }
  };

  const legalTranslations = {
    'ja-JP': ['プライバシー', 'このWebサイトについて', 'このサイトはRELYRを紹介する静的なWebサイトです。アクセス解析、広告、Cookie、入力フォームは設置していません。', '外部サイトへのリンク', 'ダウンロード、ソースコード、支援ページでは、GitHubまたはKo-fiのプライバシーポリシーが適用されます。', 'RELYRアプリについて', 'RELYRは入力内容を外部サーバーへ送信しません。設定とマクロは利用者のPC内へ保存します。', 'アンインストール後のデータ', '通常のアンインストールでは、ユーザーデータを削除するか確認します。無人アンインストールでは、意図しない消失を防ぐため、%AppData%\\RELYRの設定と%LocalAppData%\\RELYRの診断情報を標準で残します。無人実行での完全削除はアンインストーラーの/VERYSILENT /PURGEUSERDATA=1オプションで指定できます。', '← RELYRのホームへ戻る'],
    'en-US': ['Privacy', 'About this website', 'This is a static website introducing RELYR. It uses no analytics, advertising, cookies, or input forms.', 'Links to external websites', 'GitHub or Ko-fi privacy policies apply to download, source code, and support pages.', 'About the RELYR app', 'RELYR does not send input content to external servers. Settings and macros are stored on your PC.', 'Data after uninstallation', 'Interactive uninstallation asks whether to remove user data. Silent uninstallation preserves settings in %AppData%\\RELYR and diagnostics in %LocalAppData%\\RELYR by default to prevent unintended data loss. Use /VERYSILENT /PURGEUSERDATA=1 for a silent complete removal.', '← Back to RELYR'],
    'zh-CN': ['隐私', '关于本网站', '这是介绍 RELYR 的静态网站，不使用分析、广告、Cookie 或输入表单。', '外部网站链接', '下载、源代码与支持页面适用 GitHub 或 Ko-fi 的隐私政策。', '关于 RELYR 应用', 'RELYR 不会将输入内容发送到外部服务器。设置和宏保存在您的电脑中。', '卸载后的数据', '交互式卸载会询问是否删除用户数据。静默卸载默认保留%AppData%\\RELYR中的设置和%LocalAppData%\\RELYR中的诊断信息，以防止意外丢失数据。要静默完全删除，请使用/VERYSILENT /PURGEUSERDATA=1。', '← 返回 RELYR'],
    'zh-TW': ['隱私權', '關於本網站', '這是介紹 RELYR 的靜態網站，不使用分析、廣告、Cookie 或輸入表單。', '外部網站連結', '下載、原始碼與支援頁面適用 GitHub 或Ko-fi的隱私權政策。', '關於 RELYR 應用程式', 'RELYR 不會將輸入內容傳送至外部伺服器。設定與巨集保存在您的電腦中。', '解除安裝後的資料', '互動式解除安裝會詢問是否刪除使用者資料。靜默解除安裝預設保留%AppData%\\RELYR中的設定和%LocalAppData%\\RELYR中的診斷資訊，以防止意外遺失資料。若要靜默完整刪除，請使用/VERYSILENT /PURGEUSERDATA=1。', '← 返回 RELYR'],
    'ko-KR': ['개인정보 보호', '이 웹사이트에 대하여', 'RELYR를 소개하는 정적 웹사이트입니다. 분석, 광고, 쿠키 또는 입력 양식을 사용하지 않습니다.', '외부 사이트 링크', '다운로드, 소스 코드와 후원 페이지에는 GitHub 또는 Ko-fi의 개인정보 처리방침이 적용됩니다.', 'RELYR 앱에 대하여', 'RELYR는 입력 내용을 외부 서버로 보내지 않습니다. 설정과 매크로는 사용자의 PC에 저장됩니다.', '제거 후 데이터', '대화형 제거에서는 사용자 데이터 삭제 여부를 묻습니다. 자동 제거는 의도하지 않은 데이터 손실을 막기 위해 기본적으로 %AppData%\\RELYR의 설정과 %LocalAppData%\\RELYR의 진단 정보를 유지합니다. 자동으로 완전히 삭제하려면 /VERYSILENT /PURGEUSERDATA=1을 사용하세요.', '← RELYR로 돌아가기'],
    'fr-FR': ['Confidentialité', 'À propos de ce site', 'Ce site statique présente RELYR. Il n’utilise ni analyse, ni publicité, ni cookie, ni formulaire.', 'Liens externes', 'Les politiques de confidentialité de GitHub ou Ko-fi s’appliquent aux pages de téléchargement, de code source et de soutien.', 'À propos de l’application RELYR', 'RELYR n’envoie aucun contenu de saisie vers un serveur externe. Les réglages et macros restent sur votre PC.', 'Données après la désinstallation', 'La désinstallation interactive demande si les données utilisateur doivent être supprimées. La désinstallation silencieuse conserve par défaut les réglages dans %AppData%\\RELYR et les diagnostics dans %LocalAppData%\\RELYR afin d’éviter toute perte involontaire. Utilisez /VERYSILENT /PURGEUSERDATA=1 pour une suppression complète silencieuse.', '← Retour à RELYR'],
    'de-DE': ['Datenschutz', 'Über diese Website', 'Diese statische Website stellt RELYR vor. Sie verwendet keine Analyse, Werbung, Cookies oder Eingabeformulare.', 'Externe Links', 'Für Download-, Quellcode- und Supportseiten gelten die Datenschutzrichtlinien von GitHub oder Ko-fi.', 'Über die RELYR-App', 'RELYR sendet keine Eingabeinhalte an externe Server. Einstellungen und Makros werden auf deinem PC gespeichert.', 'Daten nach der Deinstallation', 'Bei der interaktiven Deinstallation wird gefragt, ob Benutzerdaten gelöscht werden sollen. Die unbeaufsichtigte Deinstallation behält standardmäßig Einstellungen in %AppData%\\RELYR und Diagnosedaten in %LocalAppData%\\RELYR, um unbeabsichtigten Datenverlust zu verhindern. Für eine unbeaufsichtigte vollständige Entfernung dient /VERYSILENT /PURGEUSERDATA=1.', '← Zurück zu RELYR'],
    'es-ES': ['Privacidad', 'Acerca de este sitio', 'Este sitio estático presenta RELYR. No usa analítica, publicidad, cookies ni formularios.', 'Enlaces externos', 'Las políticas de privacidad de GitHub o Ko-fi se aplican a las páginas de descarga, código fuente y soporte.', 'Acerca de la aplicación RELYR', 'RELYR no envía el contenido introducido a servidores externos. Los ajustes y macros se guardan en tu PC.', 'Datos después de la desinstalación', 'La desinstalación interactiva pregunta si se deben borrar los datos del usuario. La desinstalación silenciosa conserva por defecto los ajustes en %AppData%\\RELYR y los diagnósticos en %LocalAppData%\\RELYR para evitar pérdidas involuntarias. Usa /VERYSILENT /PURGEUSERDATA=1 para una eliminación completa silenciosa.', '← Volver a RELYR']
  };
  const notFoundTranslations = {
    'ja-JP': ['ここには、<br>何もありません。', 'URLが変更されたか、ページが削除された可能性があります。', '← RELYRのホームへ戻る'],
    'en-US': ['Nothing lives<br>at this address.', 'The URL may have changed, or the page may have been removed.', '← Back to RELYR'],
    'zh-CN': ['这里<br>什么也没有。', '网址可能已更改，或页面已被删除。', '← 返回 RELYR'],
    'zh-TW': ['這裡<br>什麼也沒有。', '網址可能已變更，或頁面已被移除。', '← 返回 RELYR'],
    'ko-KR': ['이 주소에는<br>아무것도 없습니다.', '주소가 변경되었거나 페이지가 삭제되었을 수 있습니다.', '← RELYR로 돌아가기'],
    'fr-FR': ['Rien ne se trouve<br>à cette adresse.', 'L’adresse a peut-être changé ou la page a été supprimée.', '← Retour à RELYR'],
    'de-DE': ['Unter dieser Adresse<br>gibt es nichts.', 'Die URL wurde möglicherweise geändert oder die Seite entfernt.', '← Zurück zu RELYR'],
    'es-ES': ['No hay nada<br>en esta dirección.', 'La URL puede haber cambiado o la página puede haber sido eliminada.', '← Volver a RELYR']
  };
  const interfaceTranslations = {
    'ja-JP': { layers: ['デフォルト', 'Space', 'CapsLock', '右クリック', '進む / 戻る', 'タスクバー'], footer: ['リリース', 'ソース', 'サポート', 'プライバシー', '利用条件', 'ライセンス'] },
    'en-US': { layers: ['Default', 'Space', 'CapsLock', 'Right Click', 'Forward / Back', 'Taskbar'], footer: ['Releases', 'Source', 'Support', 'Privacy', 'Terms', 'License'] },
    'zh-CN': { layers: ['默认', 'Space', 'CapsLock', '右键', '前进 / 后退', '任务栏'], footer: ['版本', '源代码', '支持', '隐私', '使用条款', '许可证'] },
    'zh-TW': { layers: ['預設', 'Space', 'CapsLock', '右鍵', '前進 / 返回', '工作列'], footer: ['版本', '原始碼', '支援', '隱私權', '使用條款', '授權'] },
    'ko-KR': { layers: ['기본', 'Space', 'CapsLock', '오른쪽 클릭', '앞으로 / 뒤로', '작업 표시줄'], footer: ['릴리스', '소스', '지원', '개인정보', '이용 약관', '라이선스'] },
    'fr-FR': { layers: ['Par défaut', 'Espace', 'Verr. maj.', 'Clic droit', 'Suivant / Retour', 'Barre des tâches'], footer: ['Versions', 'Source', 'Assistance', 'Confidentialité', "Conditions d’utilisation", 'Licence'] },
    'de-DE': { layers: ['Standard', 'Leertaste', 'Feststelltaste', 'Rechtsklick', 'Vor / Zurück', 'Taskleiste'], footer: ['Versionen', 'Quellcode', 'Support', 'Datenschutz', 'Nutzungsbedingungen', 'Lizenz'] },
    'es-ES': { layers: ['Predeterminado', 'Espacio', 'Bloq Mayús', 'Clic derecho', 'Adelante / Atrás', 'Barra de tareas'], footer: ['Versiones', 'Código', 'Soporte', 'Privacidad', 'Términos de uso', 'Licencia'] }
  };

  const supported = Object.keys(translations);
  const findInitialLanguage = () => {
    const saved = localStorage.getItem('relyr-site-language');
    if (supported.includes(saved)) return saved;
    for (const candidate of navigator.languages || [navigator.language]) {
      const exact = supported.find((code) => code.toLowerCase() === String(candidate).toLowerCase());
      if (exact) return exact;
      const base = String(candidate).split('-')[0].toLowerCase();
      const partial = supported.find((code) => code.split('-')[0].toLowerCase() === base);
      if (partial) return partial;
    }
    return 'en-US';
  };

  const one = (selector) => document.querySelector(selector);
  const all = (selector) => [...document.querySelectorAll(selector)];
  const text = (selector, value) => { const node = one(selector); if (node) node.textContent = value; };
  const html = (selector, value) => { const node = one(selector); if (node) node.innerHTML = value; };
  const list = (selector, values, useHtml = false) => {
    all(selector).forEach((node, index) => {
      if (values[index] == null) return;
      if (useHtml) node.innerHTML = values[index]; else node.textContent = values[index];
    });
  };

  const applyLanguage = (code) => {
    const language = translations[code] || translations['en-US'];
    document.documentElement.lang = code;
    document.title = language.title;
    one('meta[name="description"]')?.setAttribute('content', language.description);
    one('meta[property="og:title"]')?.setAttribute('content', language.title);
    one('meta[property="og:description"]')?.setAttribute('content', language.description);
    one('meta[property="og:locale"]')?.setAttribute('content', ({ 'ja-JP': 'ja_JP', 'en-US': 'en_US', 'zh-CN': 'zh_CN', 'zh-TW': 'zh_TW', 'ko-KR': 'ko_KR', 'fr-FR': 'fr_FR', 'de-DE': 'de_DE', 'es-ES': 'es_ES' })[code] || 'en_US');
    text('.skip-link', language.skip);
    list('.site-header nav a', language.nav);
    text('.header-download span', language.download);
    one('.header-download')?.setAttribute('aria-label', `${language.download} RELYR`);
    text('[data-language-label]', language.native);
    one('.wordmark')?.setAttribute('aria-label', 'RELYR');
    one('.site-header nav')?.setAttribute('aria-label', 'RELYR');
    one('.language-switcher summary')?.setAttribute('aria-label', language.native);
    one('.language-menu')?.setAttribute('aria-label', language.native);
    one('.layer-index')?.setAttribute('aria-label', language.nav[0]);
    one('.download-spec')?.setAttribute('aria-label', language.download);
    one('footer nav')?.setAttribute('aria-label', 'RELYR');
    all('[data-language]').forEach((button) => button.classList.toggle('is-active', button.dataset.language === code));
    const interfaceCopy = interfaceTranslations[code];
    if (interfaceCopy) {
      one('.support-links')?.setAttribute('aria-label', interfaceCopy.footer[2]);
      all('.layer-index p').forEach((node, index) => {
        const number = node.querySelector('span');
        const value = interfaceCopy.layers[index];
        if (number && value) node.replaceChildren(number, document.createTextNode(value));
      });
      list('footer nav a', interfaceCopy.footer);
    }

    html('.hero .overline', language.hero[0]);
    text('.beta-status', language.beta);
    text('.hero h1 > .hero-line span', language.hero[1]);
    text('.hero h1 > em span', language.hero[2]);
    text('.hero-lead', language.hero[3]);
    text('.hero-actions .download-button > span', language.hero[4]);
    const heroExplore = one('.hero-actions .text-link');
    if (heroExplore?.childNodes[0]) heroExplore.childNodes[0].nodeValue = `${language.hero[5]} `;
    text('.hero-note', language.hero[6]);
    all('.shot-button > span').forEach((node) => { node.textContent = language.expand; });
    all('[data-shot]').forEach((button, index) => {
      const label = language.screens[index];
      if (!label) return;
      button.dataset.caption = label;
      button.querySelector('img')?.setAttribute('alt', label);
      button.setAttribute('aria-label', `${label} — ${language.expand}`);
    });

    const fit = language.fit;
    if (fit) {
      text('.fit-heading .overline', fit[0]);
      text('.fit-heading h2', fit[1]);
      text('.fit-heading > p:last-child', fit[2]);
      text('.fit-card-keep .fit-label', fit[3]);
      text('.fit-card-keep h3', fit[4]);
      text('.fit-card-keep > p:not(.fit-label)', fit[5]);
      text('.fit-card-choose .fit-label', fit[6]);
      text('.fit-card-choose h3', fit[7]);
      text('.fit-card-choose > p:not(.fit-label)', fit[8]);
      list('.fit-card-choose li', fit.slice(9, 12));
      text('.recipe-heading .overline', fit[12]);
      text('.recipe-heading h2', fit[13]);
      text('.recipe-heading > p:last-child', fit[14]);
      list('.recipe-copy h3', [fit[15], fit[20]]);
      list('.recipe-card:first-child .recipe-copy li', fit.slice(16, 20));
      list('.recipe-card:last-child .recipe-copy li', fit.slice(21, 25));
      text('.fit-download > span', fit[25]);
      const fitGuide = one('.fit-guide');
      if (fitGuide?.childNodes[0]) fitGuide.childNodes[0].nodeValue = `${fit[26]} `;
      text('[data-support-label]', fit[27]);
    }

    html('.layers-section .section-heading h2', language.layers[0]);
    text('.layers-section .section-heading > p:last-child', language.layers[1]);
    text('.layer-figure figcaption span', language.layers[2]);
    html('.ledger-intro h2', language.actions[0]);
    list('.action-index article b', language.actions.slice(1));

    html('.deck-title h2', language.deck[0]);
    text('.deck-title > p:last-child', language.deck[1]);
    text('.deck-strip figcaption b', language.deck[2]);
    html('.editor-copy h3', language.deck[3]);
    text('.editor-copy > p:not(.editor-mark)', language.deck[4]);
    list('.editor-copy li', language.deck.slice(5).map((value, index) => `<span>0${index + 1}</span>${value}`), true);

    html('.workflow-heading h2', language.workflows[0]);
    text('.workflow-heading > p:last-child', language.workflows[1]);
    list('.workflow-label b', language.workflows.slice(2));
    html('.principles-copy h2', language.local[0]);
    text('.principles-copy > p:last-child', language.local[1]);
    const localValues = language.local.slice(2);
    [0, 1, 3, 4].forEach((targetIndex, sourceIndex) => {
      const node = all('.principle-list dd')[targetIndex];
      if (node) node.textContent = localValues[sourceIndex];
    });

    text('.download-copy .overline', language.final[0]);
    text('.download-copy h2', language.final[1]);
    text('.download-copy > p:not(.overline):not(.signing-note)', language.final[2]);
    text('.download-actions .download-button > span', language.final[3]);
    text('.checksum-link', language.final[4]);
    text('.signing-note', language.final[5]);
    text('[data-guide-label]', language.guide);
    const guideUrl = code === 'ja-JP'
      ? 'https://github.com/zitan-source/RELYR/blob/main/docs/getting-started.ja.md'
      : 'https://github.com/zitan-source/RELYR/blob/main/docs/getting-started.md';
    one('.guide-link')?.setAttribute('href', guideUrl);
    one('.fit-guide')?.setAttribute('href', guideUrl);
    text('[data-feedback-label]', language.feedback);
    const specValues = all('.download-spec b');
    if (specValues[2]) specValues[2].textContent = language.final[6];
    if (specValues[4]) specValues[4].textContent = language.final[7];
    one('[data-shot-close]')?.setAttribute('aria-label', language.close);
    one('[data-shot-dialog]')?.setAttribute('aria-label', language.expand);

    const legal = legalTranslations[code];
    if (one('.legal-shell') && legal) {
      document.title = `${legal[0]} — RELYR`;
      one('meta[name="description"]')?.setAttribute('content', legal[2]);
      text('.legal-shell h1', legal[0]);
      list('.legal-shell h2', [legal[1], legal[3], legal[5], legal[7]]);
      list('.legal-shell h2 + p', [legal[2], legal[4], legal[6], legal[8]]);
      text('.legal-shell > p:last-child .text-link', legal[9]);
    }
    const notFound = notFoundTranslations[code];
    if (one('.not-found main') && notFound) {
      document.title = `404 — RELYR`;
      html('.not-found h1', notFound[0]);
      text('.not-found main > p', notFound[1]);
      text('.not-found .text-link', notFound[2]);
    }

    localStorage.setItem('relyr-site-language', code);
    document.dispatchEvent(new CustomEvent('relyr:languagechange', { detail: { code } }));
  };

  const switcher = one('.language-switcher');
  all('[data-language]').forEach((button) => {
    button.addEventListener('click', () => {
      applyLanguage(button.dataset.language);
      switcher?.removeAttribute('open');
    });
  });
  document.addEventListener('click', (event) => {
    if (switcher?.open && !switcher.contains(event.target)) switcher.removeAttribute('open');
  });
  applyLanguage(findInitialLanguage());
})();
