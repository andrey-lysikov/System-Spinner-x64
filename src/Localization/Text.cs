using System.Globalization;

namespace SystemSpinnerX64.Localization;

// Interface strings: the tray menu, notifications, the status window and the panel notice line.
internal static class Text
{
    private static Language _current = Resolve(Language.Auto);

    // Applies the language from the config.
    public static void Use(Language language) => _current = Resolve(language);

    public static Language Current => _current;

    // Arabic is written right to left; the windows and the menu mirror themselves for it.
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

    // Picks the string for the current language.
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
        "Spinners", "Спиннеры", "Spinners", "Spinners",
        "Spinners", "Spinners", "Spinners", "Spinners");

    public static string MenuUpdateInterval => S(
        "Data update every", "Обновлять данные каждые", "تحديث البيانات كل", "数据更新间隔",
        "Mise à jour toutes les", "Daten aktualisieren alle", "Aggiorna i dati ogni",
        "データ更新間隔");

    public static string MenuEffects => S(
        "Spinner effects", "Эффекты спиннеров", "تأثيرات Spinners", "Spinners 效果",
        "Effets des spinners", "Spinner-Effekte", "Effetti degli spinner",
        "Spinner の効果");

    public static string MenuInvertRotation => S(
        "Invert rotation", "Обратное вращение", "عكس اتجاه الدوران", "反向旋转",
        "Inverser la rotation", "Drehung umkehren", "Inverti la rotazione", "回転を逆にする");

    public static string MenuOverlay => S(
        "Full-screen overlay", "Полноэкранный оверлей", "طبقة ملء الشاشة", "全屏叠加层",
        "Superposition plein écran", "Vollbild-Overlay", "Overlay a schermo intero",
        "全画面オーバーレイ");

    public static string MenuOpenConfig => S(
        "Open config.conf", "Открыть config.conf", "فتح config.conf", "打开 config.conf",
        "Ouvrir config.conf", "config.conf öffnen", "Apri config.conf", "config.conf を開く");

    public static string MenuOpenLog => S(
        "Open the log", "Открыть журнал", "فتح السجل", "打开日志",
        "Ouvrir le journal", "Protokoll öffnen", "Apri il registro", "ログを開く");

    public static string MenuCheckUpdate => S(
        "Check for updates", "Проверить обновление", "التحقق من التحديثات", "检查更新",
        "Rechercher des mises à jour", "Nach Updates suchen", "Controlla aggiornamenti",
        "更新を確認");

    public static string MenuProject => S(
        "Project page", "Страница проекта", "صفحة المشروع", "项目页面",
        "Page du projet", "Projektseite", "Pagina del progetto", "プロジェクトのページ");

    public static string MenuAbout => S(
        "About", "О программе", "حول التطبيق", "关于",
        "À propos", "Über", "Informazioni", "このアプリについて");

    // The notification about a newer release: a click on it opens the download page.
    public static string UpdateAvailable(string version) => S(
        $"Version {version} is out. Click to download it.",
        $"Вышла версия {version}. Нажмите, чтобы скачать.",
        $"صدر الإصدار {version}. انقر للتنزيل.",
        $"新版本 {version} 已发布。点击下载。",
        $"La version {version} est disponible. Cliquez pour la télécharger.",
        $"Version {version} ist da. Zum Herunterladen klicken.",
        $"È uscita la versione {version}. Fai clic per scaricarla.",
        $"バージョン {version} が公開されました。クリックしてダウンロード。");

    public static string UpdateUpToDate(string version) => S(
        $"No updates right now — {version} is the newest one.",
        $"Обновлений сейчас нет: {version} — самая свежая версия.",
        $"لا توجد تحديثات الآن — {version} هو الأحدث.",
        $"当前没有更新，{version} 已是最新。",
        $"Aucune mise à jour pour le moment — {version} est la plus récente.",
        $"Zurzeit keine Updates — {version} ist die neueste Version.",
        $"Nessun aggiornamento al momento: {version} è la più recente.",
        $"現在、更新はありません。{version} が最新です。");

    public static string UpdateCheckFailed => S(
        "Could not reach GitHub — the check for updates did not go through.",
        "GitHub недоступен — проверить обновление не удалось.",
        "تعذر الوصول إلى GitHub — لم يتم التحقق من التحديثات.",
        "无法连接 GitHub，检查更新失败。",
        "GitHub est injoignable — la recherche de mises à jour a échoué.",
        "GitHub nicht erreichbar — die Updatesuche ist fehlgeschlagen.",
        "GitHub non raggiungibile: il controllo aggiornamenti non è riuscito.",
        "GitHub に接続できず、更新を確認できませんでした。");

    // Why the app refused to start, in the words the notification uses.
    public static string ReasonNoRights => S(
        "administrator rights are required", "нужны права администратора",
        "مطلوب صلاحيات المسؤول", "需要管理员权限",
        "des droits d'administrateur sont requis", "Administratorrechte sind erforderlich",
        "servono i diritti di amministratore", "管理者権限が必要です");

    public static string ReasonAlreadyRunning => S(
        "it is already running", "программа уже запущена",
        "التطبيق قيد التشغيل بالفعل", "程序已在运行",
        "l'application est déjà lancée", "die Anwendung läuft bereits",
        "l'applicazione è già in esecuzione", "すでに実行中です");

    public static string ReasonChecksFailed => S(
        "the startup checks did not pass", "проверки при запуске не прошли",
        "لم تنجح فحوصات بدء التشغيل", "启动检查未通过",
        "les vérifications de démarrage ont échoué", "die Startprüfungen sind fehlgeschlagen",
        "i controlli di avvio non sono riusciti", "起動時のチェックに失敗しました");

    public static string ReasonCrashed => S(
        "an unexpected error", "непредвиденная ошибка",
        "خطأ غير متوقع", "发生意外错误",
        "une erreur inattendue", "ein unerwarteter Fehler",
        "un errore imprevisto", "予期しないエラー");

    // Shown when the app refuses to start: there is no window to say it in, and the log is the
    // only place the reason exists.
    public static string StartupFailed(string reason) => S(
        $"System-Spinner did not start: {reason}. Click to open the log.",
        $"System-Spinner не запустился: {reason}. Нажмите, чтобы открыть журнал.",
        $"لم يبدأ System-Spinner: {reason}. انقر لفتح السجل.",
        $"System-Spinner 未能启动：{reason}。点击打开日志。",
        $"System-Spinner n'a pas démarré : {reason}. Cliquez pour ouvrir le journal.",
        $"System-Spinner ist nicht gestartet: {reason}. Zum Öffnen des Protokolls klicken.",
        $"System-Spinner non si è avviato: {reason}. Fai clic per aprire il registro.",
        $"System-Spinner を起動できませんでした: {reason}。クリックしてログを開きます。");

    public static string MenuExit => S(
        "Quit", "Выход", "خروج", "退出",
        "Quitter", "Beenden", "Esci", "終了");

    public static string FileMissing(string path) => S(
        $"The file does not exist yet: {path}", $"Файла ещё нет: {path}",
        $"الملف غير موجود بعد: {path}", $"文件尚不存在：{path}",
        $"Le fichier n'existe pas encore : {path}", $"Die Datei gibt es noch nicht: {path}",
        $"Il file non esiste ancora: {path}", $"ファイルはまだありません: {path}");

    // The About window: the name with the version stands on its own line above this text.
    public static string AboutText => S(
        "A program that shows how the resources of your computer are being used, in full screen " +
        "as well. Small, light, practical.\n\nAuthor: @Andrey.Lysikov",

        "Программа которая показывает утилизацию ресурсов вашего компьютера и в полноэкранном " +
        "режиме тоже. Небольшая, легкая практичная.\n\nАвтор: @Andrey.Lysikov",

        "برنامج يعرض استخدام موارد جهازك، وفي وضع ملء الشاشة أيضًا. صغير وخفيف وعملي.\n\n" +
        "المؤلف: @Andrey.Lysikov",

        "显示计算机资源占用情况的程序，全屏模式下同样可用。小巧、轻量、实用。\n\n" +
        "作者：@Andrey.Lysikov",

        "Un programme qui montre l'utilisation des ressources de votre ordinateur, en plein écran " +
        "aussi. Petit, léger, pratique.\n\nAuteur : @Andrey.Lysikov",

        "Ein Programm, das die Auslastung der Ressourcen Ihres Rechners zeigt, im Vollbild " +
        "ebenfalls. Klein, leicht, praktisch.\n\nAutor: @Andrey.Lysikov",

        "Un programma che mostra l'utilizzo delle risorse del computer, anche a schermo intero. " +
        "Piccolo, leggero, pratico.\n\nAutore: @Andrey.Lysikov",

        "パソコンのリソース使用状況を表示するプログラムです。全画面でも同じように使えます。" +
        "小さく、軽く、実用的。\n\n作者: @Andrey.Lysikov");

    // Poll periods in the menu. The unit is short: it stands right after a number.
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

    // Row headlines. A value is appended to each: "CPU Usage 9 %".
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

    // The unit for fan speed. One for both places it appears: the CPU and the card.
    public static string Rpm => S(
        "rpm", "об/мин", "دورة/د", "转/分",
        "tr/min", "U/min", "giri/min", "rpm");

    // Fan speeds with tags: "CPU 903 · AIO 2210 rpm".
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
