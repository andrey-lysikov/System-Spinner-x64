using System.Globalization;

namespace SystemSpinnerX64.Localization;

/// <summary>
/// Interface strings: the tray menu, notifications, the status window and the panel notice line.
/// The log and config.conf are always English — they are read when something goes wrong.
///
/// Every string is written out in all eight languages next to its own name rather than kept in
/// separate resource files: there are some sixty of them, and a wording that drifts away from
/// where it is used is the usual way translations rot. A language left empty falls back to
/// English, so filling in one more is a matter of text rather than code.
/// </summary>
internal static class Text
{
    private static Language _current = Resolve(Language.Auto);

    /// <summary>Applies the language from the config. <see cref="Language.Auto"/> follows the system.</summary>
    public static void Use(Language language) => _current = Resolve(language);

    public static Language Current => _current;

    /// <summary>Arabic is written right to left; the windows and the menu mirror themselves for it.</summary>
    public static bool IsRightToLeft => _current == Language.Ar;

    // Auto is resolved once, when it is applied: the system language does not change while the
    // app runs, and re-reading the culture for every string would be work for nothing.
    private static Language Resolve(Language language)
    {
        if (language != Language.Auto) return language;

        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant() switch
        {
            "ru" => Language.Ru,
            "ar" => Language.Ar,
            "zh" => Language.Zh,
            "fr" => Language.Fr,
            "de" => Language.De,
            "it" => Language.It,
            "ja" => Language.Ja,
            _ => Language.En
        };
    }

    /// <summary>
    /// Picks the string for the current language. English is the unnamed argument because it is
    /// also the fallback: an empty translation means "not written yet", and English beats nothing.
    /// </summary>
    private static string S(string en, string ru = "", string ar = "", string zh = "",
                            string fr = "", string de = "", string it = "", string ja = "")
    {
        string chosen = _current switch
        {
            Language.Ru => ru,
            Language.Ar => ar,
            Language.Zh => zh,
            Language.Fr => fr,
            Language.De => de,
            Language.It => it,
            Language.Ja => ja,
            _ => en
        };

        return chosen.Length > 0 ? chosen : en;
    }

    // --- The tray menu ---

    public static string MenuAutoStart => S(
        "Enable autostart", "Включить автозапуск", "تشغيل تلقائي مع النظام", "开机自动启动",
        "Lancer au démarrage", "Automatisch starten", "Avvio automatico", "自動起動を有効にする");

    public static string MenuDisplays => S(
        "HDMI/DVI DDC enabled", "HDMI/DVI DDC включён", "تمكين HDMI/DVI DDC", "已启用 HDMI/DVI DDC",
        "HDMI/DVI DDC activé", "HDMI/DVI DDC aktiv", "HDMI/DVI DDC attivo", "HDMI/DVI DDC 有効");

    public static string MenuRefreshDisplays => S(
        "Rescan displays", "Пересканировать экраны", "إعادة فحص الشاشات", "重新扫描显示器",
        "Réanalyser les écrans", "Bildschirme neu suchen", "Rileva di nuovo gli schermi",
        "ディスプレイを再検出");

    public static string MenuAdjustmentSteps => S(
        "Adjustment steps", "Шагов регулировки", "خطوات الضبط", "调节步数",
        "Pas de réglage", "Einstellschritte", "Passi di regolazione", "調整ステップ数");

    public static string MenuAlwaysCustomOsd => S(
        "Always use custom OSD", "Всегда своё экранное меню", "استخدام العرض المخصص دائمًا",
        "始终使用自定义提示", "Toujours l'affichage personnalisé", "Immer eigene Anzeige",
        "Usa sempre l'OSD personalizzato", "常にカスタム OSD を使う");

    public static string MenuSystemLanguage => S(
        "Use system language", "Язык системы", "استخدام لغة النظام", "使用系统语言",
        "Langue du système", "Systemsprache verwenden", "Usa la lingua di sistema",
        "システムの言語を使う");

    public static string MenuExternalAddress => S(
        "Show external IP address", "Показывать внешний IP-адрес", "إظهار عنوان IP الخارجي",
        "显示外部 IP 地址", "Afficher l'adresse IP externe", "Externe IP-Adresse anzeigen",
        "Mostra l'indirizzo IP esterno", "外部 IP アドレスを表示");

    public static string MenuSpinners => S(
        "Spinners", "Анимации", "الرسوم المتحركة", "动画",
        "Animations", "Animationen", "Animazioni", "アニメーション");

    public static string MenuUpdateInterval => S(
        "Data update every", "Обновлять данные каждые", "تحديث البيانات كل", "数据更新间隔",
        "Mise à jour toutes les", "Daten aktualisieren alle", "Aggiorna i dati ogni",
        "データ更新間隔");

    public static string MenuEffects => S(
        "Spinners effects", "Эффекты анимаций", "تأثيرات الرسوم المتحركة", "动画效果",
        "Effets des animations", "Animationseffekte", "Effetti delle animazioni",
        "アニメーションの効果");

    public static string MenuInvertRotation => S(
        "Invert rotation", "Обратное вращение", "عكس اتجاه الدوران", "反向旋转",
        "Inverser la rotation", "Drehung umkehren", "Inverti la rotazione", "回転を逆にする");

    public static string MenuOverlay => S(
        "Overlay over full-screen apps", "Оверлей поверх полноэкранных приложений",
        "طبقة فوق التطبيقات ملء الشاشة", "全屏应用上的叠加层",
        "Superposition en plein écran", "Overlay über Vollbild-Apps",
        "Overlay sulle app a schermo intero", "全画面アプリ上のオーバーレイ");

    public static string MenuOpenConfig => S(
        "Open config.conf", "Открыть config.conf", "فتح config.conf", "打开 config.conf",
        "Ouvrir config.conf", "config.conf öffnen", "Apri config.conf", "config.conf を開く");

    public static string MenuOpenLog => S(
        "Open the log", "Открыть журнал", "فتح السجل", "打开日志",
        "Ouvrir le journal", "Protokoll öffnen", "Apri il registro", "ログを開く");

    public static string MenuAbout => S(
        "About", "О программе", "حول التطبيق", "关于",
        "À propos", "Über", "Informazioni", "このアプリについて");

    public static string MenuExit => S(
        "Quit", "Выход", "خروج", "退出",
        "Quitter", "Beenden", "Esci", "終了");

    public static string FileMissing(string path) => S(
        $"The file does not exist yet: {path}", $"Файла ещё нет: {path}",
        $"الملف غير موجود بعد: {path}", $"文件尚不存在：{path}",
        $"Le fichier n'existe pas encore : {path}", $"Die Datei gibt es noch nicht: {path}",
        $"Il file non esiste ancora: {path}", $"ファイルはまだありません: {path}");

    // No line breaks inside a paragraph: the window wraps by its own width, and hard breaks
    // would leave a ragged edge.
    public static string AboutText(string version) => S(
        "Shows the state of the system in the Windows tray, and over a full-screen application " +
        $"it becomes a CPU, GPU and frame-rate panel.\n\nAuthor: @Andrey.Lysikov\nVersion: {version}",

        "Показывает состояние системы в трее Windows, а поверх полноэкранного приложения — " +
        $"панель с CPU, GPU и счётчиком кадров.\n\nАвтор: @Andrey.Lysikov\nВерсия: {version}",

        "يعرض حالة النظام في شريط مهام Windows، وفوق التطبيقات ملء الشاشة يتحول إلى لوحة " +
        $"للمعالج وبطاقة الرسوم ومعدل الإطارات.\n\nالمؤلف: @Andrey.Lysikov\nالإصدار: {version}",

        "在 Windows 通知区域显示系统状态；在全屏应用之上则变为 CPU、GPU 和帧率面板。\n\n" +
        $"作者：@Andrey.Lysikov\n版本：{version}",

        "Affiche l'état du système dans la zone de notification de Windows et, par-dessus une " +
        "application en plein écran, devient un panneau CPU, GPU et images par seconde.\n\n" +
        $"Auteur : @Andrey.Lysikov\nVersion : {version}",

        "Zeigt den Systemzustand im Windows-Infobereich und wird über einer Vollbildanwendung " +
        $"zu einer Anzeige für CPU, GPU und Bildrate.\n\nAutor: @Andrey.Lysikov\nVersion: {version}",

        "Mostra lo stato del sistema nell'area di notifica di Windows e, sopra un'applicazione " +
        "a schermo intero, diventa un pannello con CPU, GPU e frame rate.\n\n" +
        $"Autore: @Andrey.Lysikov\nVersione: {version}",

        "Windows の通知領域にシステムの状態を表示し、全画面アプリの上では CPU・GPU・フレームレートの" +
        $"パネルになります。\n\n作者: @Andrey.Lysikov\nバージョン: {version}");

    /// <summary>Poll periods in the menu. The unit is short: it stands right after a number.</summary>
    public static string Seconds(double value) =>
        value.ToString("0.#", CultureInfo.InvariantCulture) + " " +
        S("s", "с", "ث", "秒", "s", "s", "s", "秒");

    public static string EffectOriginal => S(
        "Original", "Как нарисовано", "الأصلي", "原样",
        "Original", "Original", "Originale", "そのまま");

    public static string EffectWhite => S(
        "White shaded", "Белый силуэт", "ظل أبيض", "白色剪影",
        "Silhouette blanche", "Weiße Silhouette", "Sagoma bianca", "白のシルエット");

    public static string EffectBlack => S(
        "Black shaded", "Чёрный силуэт", "ظل أسود", "黑色剪影",
        "Silhouette noire", "Schwarze Silhouette", "Sagoma nera", "黒のシルエット");

    public static string EffectAuto => S(
        "Match the taskbar theme", "По теме панели задач", "حسب سمة شريط المهام", "跟随任务栏主题",
        "Selon le thème de la barre", "Wie die Taskleiste", "Come la barra delle applicazioni",
        "タスクバーのテーマに合わせる");

    // --- Tray notifications ---

    public static string AutoStartOn => S(
        "The app will start with Windows.", "Приложение будет запускаться вместе с Windows.",
        "سيبدأ التطبيق مع Windows.", "应用将随 Windows 一起启动。",
        "L'application démarrera avec Windows.", "Die App startet mit Windows.",
        "L'applicazione si avvierà con Windows.", "Windows と一緒に起動します。");

    public static string AutoStartOff => S(
        "Autostart disabled.", "Автозапуск выключен.", "تم إيقاف التشغيل التلقائي.", "已关闭开机自启。",
        "Démarrage automatique désactivé.", "Autostart ausgeschaltet.",
        "Avvio automatico disattivato.", "自動起動を無効にしました。");

    public static string AutoStartFailed(string problem) => S(
        $"Autostart unchanged: {problem}", $"Автозапуск не изменён: {problem}",
        $"لم يتغير التشغيل التلقائي: {problem}", $"开机自启未更改：{problem}",
        $"Démarrage automatique inchangé : {problem}", $"Autostart unverändert: {problem}",
        $"Avvio automatico invariato: {problem}", $"自動起動は変更されませんでした: {problem}");

    public static string DisplaysFound(int count) => S(
        $"Displays found: {count}.", $"Найдено экранов: {count}.",
        $"الشاشات الموجودة: {count}.", $"找到显示器：{count}。",
        $"Écrans détectés : {count}.", $"Gefundene Bildschirme: {count}.",
        $"Schermi trovati: {count}.", $"検出したディスプレイ: {count}");

    public static string SchedulerRefused(int code) => S(
        $"Task Scheduler refused (code {code})", $"Планировщик заданий отказал (код {code})",
        $"رفض برنامج جدولة المهام (الرمز {code})", $"任务计划程序拒绝（代码 {code}）",
        $"Le planificateur de tâches a refusé (code {code})",
        $"Die Aufgabenplanung hat abgelehnt (Code {code})",
        $"Utilità di pianificazione ha rifiutato (codice {code})",
        $"タスク スケジューラが拒否しました (コード {code})");

    public static string NoOwnExePath => S(
        "could not determine the path to the app's own exe",
        "не удалось определить путь к собственному exe",
        "تعذر تحديد مسار الملف التنفيذي للتطبيق", "无法确定应用自身 exe 的路径",
        "impossible de déterminer le chemin de l'exe de l'application",
        "Der Pfad zur eigenen exe konnte nicht ermittelt werden",
        "impossibile determinare il percorso dell'exe dell'applicazione",
        "アプリ自身の exe のパスを特定できませんでした");

    public static string HotKeyRefused(string problem) => S(
        $"The brightness keys were not taken: {problem}",
        $"Клавиши яркости не заняты приложением: {problem}",
        $"لم يتم أخذ مفاتيح السطوع: {problem}", $"未能占用亮度快捷键：{problem}",
        $"Les touches de luminosité n'ont pas été prises : {problem}",
        $"Die Helligkeitstasten wurden nicht belegt: {problem}",
        $"I tasti della luminosità non sono stati presi: {problem}",
        $"明るさキーを取得できませんでした: {problem}");

    // --- The panel notice line ---

    public static string ReadError(string message) => S(
        $"Sensor read error: {message}", $"Ошибка опроса: {message}",
        $"خطأ في قراءة المستشعر: {message}", $"传感器读取错误：{message}",
        $"Erreur de lecture des capteurs : {message}", $"Sensorfehler: {message}",
        $"Errore di lettura dei sensori: {message}", $"センサーの読み取りエラー: {message}");

    public static string FpsDisabled => S(
        "The frame counter is off", "Счётчик кадров отключён",
        "عداد الإطارات معطل", "帧计数器已关闭",
        "Le compteur d'images est désactivé", "Der Bildzähler ist aus",
        "Il contatore dei fotogrammi è disattivato", "フレームカウンターは無効です");

    public static string FpsNeedsAdmin => S(
        "The FPS counter requires administrator rights",
        "Для счётчика FPS нужны права администратора",
        "يتطلب عداد الإطارات صلاحيات المسؤول", "FPS 计数器需要管理员权限",
        "Le compteur FPS exige les droits administrateur",
        "Der FPS-Zähler benötigt Administratorrechte",
        "Il contatore FPS richiede i diritti di amministratore",
        "FPS カウンターには管理者権限が必要です");

    public static string FpsSessionBroken(string message) => S(
        $"The ETW session ended: {message}", $"ETW-сессия оборвалась: {message}",
        $"انتهت جلسة ETW: {message}", $"ETW 会话已结束：{message}",
        $"La session ETW s'est arrêtée : {message}", $"Die ETW-Sitzung endete: {message}",
        $"La sessione ETW è terminata: {message}", $"ETW セッションが終了しました: {message}");

    public static string FpsNotStarted(string message) => S(
        $"The FPS counter did not start: {message}", $"Счётчик FPS не запустился: {message}",
        $"لم يبدأ عداد الإطارات: {message}", $"FPS 计数器未能启动：{message}",
        $"Le compteur FPS n'a pas démarré : {message}", $"Der FPS-Zähler startete nicht: {message}",
        $"Il contatore FPS non è partito: {message}", $"FPS カウンターを開始できませんでした: {message}");

    // --- The status window ---

    /// <summary>Row headlines. A value is appended to each: "CPU Usage 9 %".</summary>
    public static string StatsCpu => S(
        "CPU Usage", "Загрузка ЦП", "استخدام المعالج", "CPU 占用",
        "Charge CPU", "CPU-Auslastung", "Utilizzo CPU", "CPU 使用率");

    public static string StatsGpu => S(
        "GPU Usage", "Загрузка ГП", "استخدام بطاقة الرسوم", "GPU 占用",
        "Charge GPU", "GPU-Auslastung", "Utilizzo GPU", "GPU 使用率");

    public static string StatsCpuTemp => S(
        "CPU Temp", "Температура ЦП", "حرارة المعالج", "CPU 温度",
        "Température CPU", "CPU-Temperatur", "Temperatura CPU", "CPU 温度");

    public static string StatsGpuTemp => S(
        "GPU Temp", "Температура ГП", "حرارة بطاقة الرسوم", "GPU 温度",
        "Température GPU", "GPU-Temperatur", "Temperatura GPU", "GPU 温度");

    public static string StatsMemory => S(
        "MEM Usage", "Занято памяти", "استخدام الذاكرة", "内存占用",
        "Mémoire utilisée", "Speicherauslastung", "Memoria usata", "メモリ使用率");

    public static string StatsGpuMemory => S(
        "GPU MEM", "Видеопамять", "ذاكرة الرسوم", "显存",
        "Mémoire GPU", "Grafikspeicher", "Memoria GPU", "VRAM 使用率");

    public static string StatsSwap => S(
        "Swap", "Подкачка", "ملف الترحيل", "交换文件",
        "Fichier d'échange", "Auslagerungsdatei", "File di scambio", "スワップ");

    public static string StatsNoAddress => S(
        "no ip found", "адрес не определён", "لا يوجد عنوان", "未找到 IP",
        "adresse introuvable", "keine Adresse", "indirizzo non trovato", "IP を取得できません");

    public static string StatsWaiting => S(
        "no data", "нет данных", "لا توجد بيانات", "无数据",
        "aucune donnée", "keine Daten", "nessun dato", "データなし");

    /// <summary>The unit for fan speed. One for both places it appears: the CPU and the card.</summary>
    public static string Rpm => S(
        "rpm", "об/мин", "دورة/د", "转/分",
        "tr/min", "U/min", "giri/min", "rpm");

    /// <summary>Fan speeds with tags: "CPU 903 · AIO 2210 rpm".</summary>
    public static string StatsFans(string speeds) => $"{speeds} {Rpm}";

    public static string StatsFansStopped => S(
        "fans stopped", "вентиляторы стоят", "المراوح متوقفة", "风扇已停转",
        "ventilateurs arrêtés", "Lüfter stehen", "ventole ferme", "ファン停止中");

    // --- The chart window ---

    public static string DetailCpu => S(
        "CPU usage details:", "Загрузка процессора подробно:", "تفاصيل استخدام المعالج:",
        "CPU 占用详情：", "Détail de la charge CPU :", "CPU-Auslastung im Detail:",
        "Dettaglio utilizzo CPU:", "CPU 使用率の詳細:");

    public static string DetailMemory => S(
        "Memory usage details:", "Занятая память подробно:", "تفاصيل استخدام الذاكرة:",
        "内存占用详情：", "Détail de la mémoire :", "Speicherauslastung im Detail:",
        "Dettaglio memoria usata:", "メモリ使用率の詳細:");

    public static string DetailColumnPid => S(
        "PID", "PID", "المعرف", "PID",
        "PID", "PID", "PID", "PID");

    public static string DetailColumnName => S(
        "Name", "Процесс", "الاسم", "名称",
        "Nom", "Name", "Nome", "名前");

    public static string DetailColumnUsage => S(
        "Usage", "Нагрузка", "الاستخدام", "占用",
        "Charge", "Auslastung", "Utilizzo", "使用率");
}
