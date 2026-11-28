// Translations of the update texts ("Cometsnap 1.4 is here", Settings > Updates). Kept apart from
// Strings.cs so the update feature stays in one place; L.Table merges both. The English text is the key.
// Keep the {0} placeholders. Tone: friendly, a little spacey, always clear.

namespace Cometsnap.Core.Localization;

internal static class UpdateStrings
{
    public static IReadOnlyDictionary<string, string>? Table(string code) => code switch
    {
        "fr" => French,
        "de" => German,
        "it" => Italian,
        "es" => Spanish,
        "pt" => Portuguese,
        "zh" => Chinese,
        "hi" => Hindi,
        _ => null,
    };

    public static readonly IReadOnlyDictionary<string, string> French = new Dictionary<string, string>
    {
        ["Cometsnap {0} is here"] = "Cometsnap {0} est arrivé",
        ["Update now"] = "Mettre à jour",
        ["Later"] = "Plus tard",
        ["Skip this version"] = "Ignorer cette version",
        ["See everything new"] = "Voir toutes les nouveautés",
        ["Downloading… {0}%"] = "Téléchargement… {0} %",
        ["Cometsnap restarts in a moment."] = "Cometsnap redémarre dans un instant.",
        ["The update could not be downloaded. Try again later."] = "La mise à jour n’a pas pu être téléchargée. Réessayez plus tard.",
        ["Download"] = "Télécharger",
        ["Portable Cometsnap: download the new file and use it instead of this one."] = "Cometsnap portable : téléchargez le nouveau fichier et utilisez-le à la place de celui-ci.",
        ["Cometsnap opens the new version: drag it into Applications and choose Replace."] = "Cometsnap ouvre la nouvelle version : glissez-la dans Applications et choisissez Remplacer.",
        ["Drag Cometsnap into Applications and choose Replace."] = "Glissez Cometsnap dans Applications et choisissez Remplacer.",
        ["If macOS won't open the new Cometsnap, use the Terminal line from the download page."] = "Si macOS refuse d’ouvrir le nouveau Cometsnap, utilisez la ligne Terminal de la page de téléchargement.",
        ["Installed as a package: download the new .deb and install it like the first one."] = "Installé en paquet : téléchargez le nouveau .deb et installez-le comme le premier.",
        ["Fixes and improvements."] = "Corrections et améliorations.",
        ["Updates"] = "Mises à jour",
        ["Check for updates once a day"] = "Chercher les mises à jour une fois par jour",
        ["Cometsnap asks GitHub if a new version is out. Nothing installs without your click."] = "Cometsnap demande à GitHub si une nouvelle version est sortie. Rien ne s’installe sans votre clic.",
        ["Check now"] = "Vérifier maintenant",
        ["Checking…"] = "Vérification…",
        ["You have the latest version ({0})."] = "Vous avez la dernière version ({0}).",
        ["Could not reach GitHub. Try again later."] = "Impossible de joindre GitHub. Réessayez plus tard.",
    };

    public static readonly IReadOnlyDictionary<string, string> German = new Dictionary<string, string>
    {
        ["Cometsnap {0} is here"] = "Cometsnap {0} ist da",
        ["Update now"] = "Jetzt aktualisieren",
        ["Later"] = "Später",
        ["Skip this version"] = "Diese Version überspringen",
        ["See everything new"] = "Alle Neuerungen ansehen",
        ["Downloading… {0}%"] = "Wird geladen… {0} %",
        ["Cometsnap restarts in a moment."] = "Cometsnap startet gleich neu.",
        ["The update could not be downloaded. Try again later."] = "Das Update konnte nicht geladen werden. Versuchen Sie es später noch einmal.",
        ["Download"] = "Herunterladen",
        ["Portable Cometsnap: download the new file and use it instead of this one."] = "Portables Cometsnap: Laden Sie die neue Datei herunter und verwenden Sie sie statt dieser.",
        ["Cometsnap opens the new version: drag it into Applications and choose Replace."] = "Cometsnap öffnet die neue Version: Ziehen Sie sie in „Programme“ und wählen Sie „Ersetzen“.",
        ["Drag Cometsnap into Applications and choose Replace."] = "Ziehen Sie Cometsnap in „Programme“ und wählen Sie „Ersetzen“.",
        ["If macOS won't open the new Cometsnap, use the Terminal line from the download page."] = "Wenn macOS das neue Cometsnap nicht öffnet, hilft die Terminal-Zeile auf der Download-Seite.",
        ["Installed as a package: download the new .deb and install it like the first one."] = "Als Paket installiert: Laden Sie das neue .deb herunter und installieren Sie es wie das erste.",
        ["Fixes and improvements."] = "Korrekturen und Verbesserungen.",
        ["Updates"] = "Updates",
        ["Check for updates once a day"] = "Einmal am Tag nach Updates suchen",
        ["Cometsnap asks GitHub if a new version is out. Nothing installs without your click."] = "Cometsnap fragt bei GitHub, ob eine neue Version da ist. Ohne Ihren Klick wird nichts installiert.",
        ["Check now"] = "Jetzt prüfen",
        ["Checking…"] = "Wird geprüft…",
        ["You have the latest version ({0})."] = "Sie haben die neueste Version ({0}).",
        ["Could not reach GitHub. Try again later."] = "GitHub ist nicht erreichbar. Versuchen Sie es später noch einmal.",
    };

    public static readonly IReadOnlyDictionary<string, string> Italian = new Dictionary<string, string>
    {
        ["Cometsnap {0} is here"] = "È arrivato Cometsnap {0}",
        ["Update now"] = "Aggiorna ora",
        ["Later"] = "Più tardi",
        ["Skip this version"] = "Salta questa versione",
        ["See everything new"] = "Vedi tutte le novità",
        ["Downloading… {0}%"] = "Download in corso… {0}%",
        ["Cometsnap restarts in a moment."] = "Cometsnap si riavvia tra un attimo.",
        ["The update could not be downloaded. Try again later."] = "Non è stato possibile scaricare l’aggiornamento. Riprova più tardi.",
        ["Download"] = "Scarica",
        ["Portable Cometsnap: download the new file and use it instead of this one."] = "Cometsnap portatile: scarica il nuovo file e usalo al posto di questo.",
        ["Cometsnap opens the new version: drag it into Applications and choose Replace."] = "Cometsnap apre la nuova versione: trascinala in Applicazioni e scegli Sostituisci.",
        ["Drag Cometsnap into Applications and choose Replace."] = "Trascina Cometsnap in Applicazioni e scegli Sostituisci.",
        ["If macOS won't open the new Cometsnap, use the Terminal line from the download page."] = "Se macOS non apre il nuovo Cometsnap, usa la riga per il Terminale della pagina di download.",
        ["Installed as a package: download the new .deb and install it like the first one."] = "Installato come pacchetto: scarica il nuovo .deb e installalo come il primo.",
        ["Fixes and improvements."] = "Correzioni e miglioramenti.",
        ["Updates"] = "Aggiornamenti",
        ["Check for updates once a day"] = "Cerca aggiornamenti una volta al giorno",
        ["Cometsnap asks GitHub if a new version is out. Nothing installs without your click."] = "Cometsnap chiede a GitHub se è uscita una nuova versione. Niente si installa senza il tuo clic.",
        ["Check now"] = "Controlla ora",
        ["Checking…"] = "Controllo in corso…",
        ["You have the latest version ({0})."] = "Hai l’ultima versione ({0}).",
        ["Could not reach GitHub. Try again later."] = "Impossibile raggiungere GitHub. Riprova più tardi.",
    };

    public static readonly IReadOnlyDictionary<string, string> Spanish = new Dictionary<string, string>
    {
        ["Cometsnap {0} is here"] = "Ya llegó Cometsnap {0}",
        ["Update now"] = "Actualizar ahora",
        ["Later"] = "Más tarde",
        ["Skip this version"] = "Saltar esta versión",
        ["See everything new"] = "Ver todas las novedades",
        ["Downloading… {0}%"] = "Descargando… {0} %",
        ["Cometsnap restarts in a moment."] = "Cometsnap se reinicia en un momento.",
        ["The update could not be downloaded. Try again later."] = "No se pudo descargar la actualización. Inténtalo más tarde.",
        ["Download"] = "Descargar",
        ["Portable Cometsnap: download the new file and use it instead of this one."] = "Cometsnap portátil: descarga el archivo nuevo y úsalo en lugar de este.",
        ["Cometsnap opens the new version: drag it into Applications and choose Replace."] = "Cometsnap abre la nueva versión: arrástrala a Aplicaciones y elige Reemplazar.",
        ["Drag Cometsnap into Applications and choose Replace."] = "Arrastra Cometsnap a Aplicaciones y elige Reemplazar.",
        ["If macOS won't open the new Cometsnap, use the Terminal line from the download page."] = "Si macOS no abre el nuevo Cometsnap, usa la línea de Terminal de la página de descarga.",
        ["Installed as a package: download the new .deb and install it like the first one."] = "Instalado como paquete: descarga el nuevo .deb e instálalo como el primero.",
        ["Fixes and improvements."] = "Correcciones y mejoras.",
        ["Updates"] = "Actualizaciones",
        ["Check for updates once a day"] = "Buscar actualizaciones una vez al día",
        ["Cometsnap asks GitHub if a new version is out. Nothing installs without your click."] = "Cometsnap pregunta a GitHub si hay una versión nueva. Nada se instala sin tu clic.",
        ["Check now"] = "Comprobar ahora",
        ["Checking…"] = "Comprobando…",
        ["You have the latest version ({0})."] = "Tienes la última versión ({0}).",
        ["Could not reach GitHub. Try again later."] = "No se pudo conectar con GitHub. Inténtalo más tarde.",
    };

    public static readonly IReadOnlyDictionary<string, string> Portuguese = new Dictionary<string, string>
    {
        ["Cometsnap {0} is here"] = "Chegou o Cometsnap {0}",
        ["Update now"] = "Atualizar agora",
        ["Later"] = "Mais tarde",
        ["Skip this version"] = "Pular esta versão",
        ["See everything new"] = "Ver todas as novidades",
        ["Downloading… {0}%"] = "Baixando… {0}%",
        ["Cometsnap restarts in a moment."] = "O Cometsnap reinicia em um instante.",
        ["The update could not be downloaded. Try again later."] = "Não foi possível baixar a atualização. Tente de novo mais tarde.",
        ["Download"] = "Baixar",
        ["Portable Cometsnap: download the new file and use it instead of this one."] = "Cometsnap portátil: baixe o arquivo novo e use-o no lugar deste.",
        ["Cometsnap opens the new version: drag it into Applications and choose Replace."] = "O Cometsnap abre a nova versão: arraste-a para Aplicativos e escolha Substituir.",
        ["Drag Cometsnap into Applications and choose Replace."] = "Arraste o Cometsnap para Aplicativos e escolha Substituir.",
        ["If macOS won't open the new Cometsnap, use the Terminal line from the download page."] = "Se o macOS não abrir o novo Cometsnap, use a linha do Terminal da página de download.",
        ["Installed as a package: download the new .deb and install it like the first one."] = "Instalado como pacote: baixe o novo .deb e instale como o primeiro.",
        ["Fixes and improvements."] = "Correções e melhorias.",
        ["Updates"] = "Atualizações",
        ["Check for updates once a day"] = "Procurar atualizações uma vez por dia",
        ["Cometsnap asks GitHub if a new version is out. Nothing installs without your click."] = "O Cometsnap pergunta ao GitHub se saiu uma versão nova. Nada é instalado sem o seu clique.",
        ["Check now"] = "Verificar agora",
        ["Checking…"] = "Verificando…",
        ["You have the latest version ({0})."] = "Você tem a versão mais recente ({0}).",
        ["Could not reach GitHub. Try again later."] = "Não foi possível acessar o GitHub. Tente de novo mais tarde.",
    };

    public static readonly IReadOnlyDictionary<string, string> Chinese = new Dictionary<string, string>
    {
        ["Cometsnap {0} is here"] = "Cometsnap {0} 来了",
        ["Update now"] = "立即更新",
        ["Later"] = "稍后",
        ["Skip this version"] = "跳过此版本",
        ["See everything new"] = "查看全部新内容",
        ["Downloading… {0}%"] = "正在下载… {0}%",
        ["Cometsnap restarts in a moment."] = "Cometsnap 马上就会重启。",
        ["The update could not be downloaded. Try again later."] = "无法下载更新，请稍后再试。",
        ["Download"] = "下载",
        ["Portable Cometsnap: download the new file and use it instead of this one."] = "便携版 Cometsnap：下载新文件，用它替换当前这个。",
        ["Cometsnap opens the new version: drag it into Applications and choose Replace."] = "Cometsnap 会打开新版本：把它拖进“应用程序”，然后选择“替换”。",
        ["Drag Cometsnap into Applications and choose Replace."] = "把 Cometsnap 拖进“应用程序”，然后选择“替换”。",
        ["If macOS won't open the new Cometsnap, use the Terminal line from the download page."] = "如果 macOS 打不开新的 Cometsnap，请使用下载页面上的那行终端命令。",
        ["Installed as a package: download the new .deb and install it like the first one."] = "以软件包安装：下载新的 .deb，像第一次那样安装即可。",
        ["Fixes and improvements."] = "问题修复和改进。",
        ["Updates"] = "更新",
        ["Check for updates once a day"] = "每天检查一次更新",
        ["Cometsnap asks GitHub if a new version is out. Nothing installs without your click."] = "Cometsnap 会向 GitHub 询问是否有新版本。不经你点击，什么都不会安装。",
        ["Check now"] = "立即检查",
        ["Checking…"] = "正在检查…",
        ["You have the latest version ({0})."] = "你用的已是最新版本（{0}）。",
        ["Could not reach GitHub. Try again later."] = "无法连接 GitHub，请稍后再试。",
    };

    public static readonly IReadOnlyDictionary<string, string> Hindi = new Dictionary<string, string>
    {
        ["Cometsnap {0} is here"] = "Cometsnap {0} आ गया है",
        ["Update now"] = "अभी अपडेट करें",
        ["Later"] = "बाद में",
        ["Skip this version"] = "यह वर्ज़न छोड़ें",
        ["See everything new"] = "सारी नई चीज़ें देखें",
        ["Downloading… {0}%"] = "डाउनलोड हो रहा है… {0}%",
        ["Cometsnap restarts in a moment."] = "Cometsnap बस एक पल में फिर से शुरू होगा।",
        ["The update could not be downloaded. Try again later."] = "अपडेट डाउनलोड नहीं हो सका। बाद में फिर कोशिश करें।",
        ["Download"] = "डाउनलोड करें",
        ["Portable Cometsnap: download the new file and use it instead of this one."] = "पोर्टेबल Cometsnap: नई फ़ाइल डाउनलोड करें और इसकी जगह उसे इस्तेमाल करें।",
        ["Cometsnap opens the new version: drag it into Applications and choose Replace."] = "Cometsnap नया वर्ज़न खोलेगा: उसे Applications में खींचें और Replace चुनें।",
        ["Drag Cometsnap into Applications and choose Replace."] = "Cometsnap को Applications में खींचें और Replace चुनें।",
        ["If macOS won't open the new Cometsnap, use the Terminal line from the download page."] = "अगर macOS नया Cometsnap न खोले, तो डाउनलोड पेज वाली Terminal लाइन इस्तेमाल करें।",
        ["Installed as a package: download the new .deb and install it like the first one."] = "पैकेज के रूप में इंस्टॉल है: नया .deb डाउनलोड करें और पहले वाले की तरह इंस्टॉल करें।",
        ["Fixes and improvements."] = "सुधार और बेहतरियाँ।",
        ["Updates"] = "अपडेट",
        ["Check for updates once a day"] = "दिन में एक बार अपडेट देखें",
        ["Cometsnap asks GitHub if a new version is out. Nothing installs without your click."] = "Cometsnap GitHub से पूछता है कि कोई नया वर्ज़न आया है या नहीं। आपके क्लिक के बिना कुछ भी इंस्टॉल नहीं होता।",
        ["Check now"] = "अभी देखें",
        ["Checking…"] = "देख रहे हैं…",
        ["You have the latest version ({0})."] = "आपके पास सबसे नया वर्ज़न है ({0})।",
        ["Could not reach GitHub. Try again later."] = "GitHub तक नहीं पहुँच सके। बाद में फिर कोशिश करें।",
    };
}
