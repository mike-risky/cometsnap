; Cometsnap Setup (Inno Setup 6.3+).
; Per-user install, no admin rights, in the Windows display language (English, French, German,
; Italian, Spanish, Portuguese, Simplified Chinese or Hindi; English otherwise). Says plainly whether it installs, updates or reinstalls,
; closes any running Cometsnap first, and hands over to Cometsnap's own
; "Replace your old screen capture" step when it finishes.
;
; Build:  ISCC /DAppVersion=0.1.42 /DArch=x64 /DSourceExe=..\out\x64\Cometsnap.exe /O..\out\x64 Cometsnap.iss

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef Arch
  #define Arch "x64"
#endif
#ifndef SourceExe
  #define SourceExe "..\out\" + Arch + "\Cometsnap.exe"
#endif
#define AppId "F967EC12-96EC-4899-8998-A59765E250CA"
; AppId in [Setup] must stay the same GUID.
#define AppIdBraced "{" + AppId + "}"

[Setup]
AppId={{F967EC12-96EC-4899-8998-A59765E250CA}
AppName=Cometsnap
AppVersion={#AppVersion}
AppVerName=Cometsnap {#AppVersion}
AppPublisher=Michael Solioz
AppPublisherURL=https://github.com/mike-risky/cometsnap
AppSupportURL=https://github.com/mike-risky/cometsnap/issues
VersionInfoVersion={#AppVersion}
VersionInfoProductName=Cometsnap
VersionInfoDescription=Cometsnap Setup
VersionInfoProductVersion={#AppVersion}
VersionInfoProductTextVersion={#AppVersion}
VersionInfoCompany=Michael Solioz
VersionInfoCopyright=Copyright (c) 2026 Michael Solioz
DefaultDirName={autopf}\Cometsnap
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableWelcomePage=no
PrivilegesRequired=lowest
OutputDir=..\out
OutputBaseFilename=Cometsnap-Setup-{#Arch}
SetupIconFile=..\assets\cometsnap.ico
UninstallDisplayIcon={app}\Cometsnap.exe
UninstallDisplayName=Cometsnap
WizardStyle=modern
WizardImageFile=..\assets\installer\wizard-100.bmp,..\assets\installer\wizard-250.bmp
WizardSmallImageFile=..\assets\installer\small-100.bmp,..\assets\installer\small-250.bmp
Compression=lzma2/max
SolidCompression=yes
CloseApplications=force
#ifdef SignedUninstallerDir
; Code signing (CI "sign" job): the uninstaller is signed by SignPath between two compiles.
; The first compile writes uninst-*.e32 here and stops; the second embeds the signed copy.
SignedUninstaller=yes
SignedUninstallerDir={#SignedUninstallerDir}
#endif
RestartApplications=no
#if Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif
; Speak the Windows display language without asking.
ShowLanguageDialog=no
LanguageDetectionMethod=uilanguage

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"
Name: "de"; MessagesFile: "compiler:Languages\German.isl"
Name: "it"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "pt"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
; Inno Setup has no official Chinese or Hindi: the files live in installer\Languages.
Name: "zh"; MessagesFile: "Languages\ChineseSimplified.isl"
Name: "hi"; MessagesFile: "compiler:Default.isl,Languages\Hindi.isl"

[Messages]
SetupWindowTitle=Cometsnap Setup
fr.SetupWindowTitle=Installation de Cometsnap
de.SetupWindowTitle=Cometsnap Setup
it.SetupWindowTitle=Installazione di Cometsnap
es.SetupWindowTitle=Instalación de Cometsnap
pt.SetupWindowTitle=Instalação do Cometsnap
zh.SetupWindowTitle=Cometsnap 安装
hi.SetupWindowTitle=Cometsnap सेटअप
en.ConfirmUninstall=Remove Cometsnap from this PC?%n%nCometsnap will be closed and removed from the Start menu and from startup, and the Print Screen key goes back to Windows.%n%nYour captures (Pictures\Cometsnap) are kept.
fr.ConfirmUninstall=Retirer Cometsnap de ce PC ?%n%nCometsnap sera fermé et retiré du menu Démarrer et du démarrage, et la touche Impr. écran revient à Windows.%n%nVos captures (Images\Cometsnap) sont conservées.
de.ConfirmUninstall=Cometsnap von diesem PC entfernen?%n%nCometsnap wird geschlossen und aus dem Startmenü und dem Autostart entfernt, und die Druck-Taste geht an Windows zurück.%n%nIhre Aufnahmen (Bilder\Cometsnap) bleiben erhalten.
it.ConfirmUninstall=Rimuovere Cometsnap da questo PC?%n%nCometsnap verrà chiuso e rimosso dal menu Start e dall’avvio, e il tasto Stamp torna a Windows.%n%nLe tue catture (Immagini\Cometsnap) restano al loro posto.
es.ConfirmUninstall=¿Quitar Cometsnap de este PC?%n%nCometsnap se cerrará y se quitará del menú Inicio y del arranque, y la tecla Impr Pant vuelve a Windows.%n%nTus capturas (Imágenes\Cometsnap) se conservan.
pt.ConfirmUninstall=Remover o Cometsnap deste PC?%n%nO Cometsnap será fechado e removido do menu Iniciar e da inicialização, e a tecla Print Screen volta para o Windows.%n%nSuas capturas (Imagens\Cometsnap) são mantidas.
zh.ConfirmUninstall=要从这台电脑移除 Cometsnap 吗？%n%nCometsnap 会被关闭，并从开始菜单和开机启动中移除，Print Screen 键也会还给 Windows。%n%n你的截图（图片\Cometsnap）会保留。
hi.ConfirmUninstall=इस PC से Cometsnap हटाएँ?%n%nCometsnap बंद होकर स्टार्ट मेनू और स्टार्टअप से हट जाएगा, और Print Screen कुंजी Windows को वापस मिल जाएगी।%n%nआपके कैप्चर (Pictures\Cometsnap) बने रहेंगे।
en.UninstalledAll=Cometsnap was removed. Your captures are still in Pictures\Cometsnap.
fr.UninstalledAll=Cometsnap a été retiré. Vos captures sont toujours dans Images\Cometsnap.
de.UninstalledAll=Cometsnap wurde entfernt. Ihre Aufnahmen liegen weiterhin in Bilder\Cometsnap.
it.UninstalledAll=Cometsnap è stato rimosso. Le tue catture sono ancora in Immagini\Cometsnap.
es.UninstalledAll=Cometsnap se ha quitado. Tus capturas siguen en Imágenes\Cometsnap.
pt.UninstalledAll=O Cometsnap foi removido. Suas capturas continuam em Imagens\Cometsnap.
zh.UninstalledAll=Cometsnap 已移除。你的截图仍在 图片\Cometsnap 中。
hi.UninstalledAll=Cometsnap हटा दिया गया। आपके कैप्चर अब भी Pictures\Cometsnap में हैं।

[CustomMessages]
en.TaskAutostart=Start Cometsnap with Windows (recommended, so Print Screen always works)
fr.TaskAutostart=Démarrer Cometsnap avec Windows (recommandé, pour qu’Impr. écran marche toujours)
de.TaskAutostart=Cometsnap mit Windows starten (empfohlen, damit die Druck-Taste immer funktioniert)
it.TaskAutostart=Avvia Cometsnap con Windows (consigliato, così il tasto Stamp funziona sempre)
es.TaskAutostart=Iniciar Cometsnap con Windows (recomendado, para que Impr Pant funcione siempre)
pt.TaskAutostart=Iniciar o Cometsnap com o Windows (recomendado, para o Print Screen sempre funcionar)
zh.TaskAutostart=随 Windows 启动 Cometsnap（推荐，让 Print Screen 随时可用）
hi.TaskAutostart=Windows के साथ Cometsnap शुरू करें (सुझाया गया, ताकि Print Screen हमेशा काम करे)
en.TaskDesktop=Create a desktop shortcut
fr.TaskDesktop=Créer un raccourci sur le Bureau
de.TaskDesktop=Desktop-Verknüpfung erstellen
it.TaskDesktop=Crea un collegamento sul desktop
es.TaskDesktop=Crear un acceso directo en el escritorio
pt.TaskDesktop=Criar um atalho na área de trabalho
zh.TaskDesktop=创建桌面快捷方式
hi.TaskDesktop=डेस्कटॉप पर शॉर्टकट बनाएँ
en.RunAfter=Start Cometsnap and replace my old screen capture tool
fr.RunAfter=Lancer Cometsnap et remplacer mon vieil outil de capture
de.RunAfter=Cometsnap starten und mein altes Screenshot-Tool ersetzen
it.RunAfter=Avvia Cometsnap e sostituisci il mio vecchio strumento di cattura
es.RunAfter=Iniciar Cometsnap y sustituir mi vieja herramienta de captura
pt.RunAfter=Iniciar o Cometsnap e trocar minha velha ferramenta de captura
zh.RunAfter=启动 Cometsnap，并换掉我旧的截图工具
hi.RunAfter=Cometsnap शुरू करें और मेरा पुराना स्क्रीन कैप्चर टूल बदलें
en.FreshTitle=Install Cometsnap %1
fr.FreshTitle=Installer Cometsnap %1
de.FreshTitle=Cometsnap %1 installieren
it.FreshTitle=Installa Cometsnap %1
es.FreshTitle=Instalar Cometsnap %1
pt.FreshTitle=Instalar o Cometsnap %1
zh.FreshTitle=安装 Cometsnap %1
hi.FreshTitle=Cometsnap %1 इंस्टॉल करें
en.FreshText=Cometsnap is a fast screenshot tool that lives in your tray.%n%nSetup installs it just for you, no admin rights needed. When it finishes, Cometsnap takes over the Print Screen key and offers to replace your old screen capture tool (Snipping Tool, Greenshot, ShareX, Lightshot...).%n%nClick Next to continue.
fr.FreshText=Cometsnap est un outil de capture d’écran rapide comme une comète, qui vit dans votre barre des tâches.%n%nL’installation se fait juste pour vous, sans droits d’administrateur. À la fin, Cometsnap prend la touche Impr. écran et propose de remplacer votre vieil outil de capture (Outil Capture, Greenshot, ShareX, Lightshot...).%n%nCliquez sur Suivant pour continuer.
de.FreshText=Cometsnap ist ein blitzschnelles Screenshot-Tool, das in Ihrem Infobereich wohnt.%n%nSetup installiert es nur für Sie, ganz ohne Administratorrechte. Danach übernimmt Cometsnap die Druck-Taste und bietet an, Ihr altes Screenshot-Tool zu ersetzen (Snipping Tool, Greenshot, ShareX, Lightshot...).%n%nKlicken Sie auf Weiter, um fortzufahren.
it.FreshText=Cometsnap è uno strumento di cattura veloce come una cometa, che abita nell’area di notifica.%n%nL’installazione è solo per te, senza diritti di amministratore. Alla fine, Cometsnap prende il tasto Stamp e propone di sostituire il tuo vecchio strumento di cattura (Strumento di cattura, Greenshot, ShareX, Lightshot...).%n%nFai clic su Avanti per continuare.
es.FreshText=Cometsnap es una herramienta de capturas rápida como un cometa que vive en tu bandeja.%n%nSe instala solo para ti, sin derechos de administrador. Al terminar, Cometsnap se queda con la tecla Impr Pant y te ofrece sustituir tu vieja herramienta de captura (Recortes, Greenshot, ShareX, Lightshot...).%n%nHaz clic en Siguiente para continuar.
pt.FreshText=O Cometsnap é uma ferramenta de captura de tela rápida como um cometa, que mora na sua bandeja.%n%nA instalação é só para você, sem direitos de administrador. No fim, o Cometsnap assume a tecla Print Screen e oferece trocar sua velha ferramenta de captura (Ferramenta de Captura, Greenshot, ShareX, Lightshot...).%n%nClique em Avançar para continuar.
zh.FreshText=Cometsnap 是一款像彗星一样快的截图工具，就住在你的系统托盘里。%n%n只为你安装，无需管理员权限。安装完成后，Cometsnap 会接管 Print Screen 键，并帮你换掉旧的截图工具（截图工具、Greenshot、ShareX、Lightshot...）。%n%n点击“下一步”继续。
hi.FreshText=Cometsnap धूमकेतु जैसा तेज़ स्क्रीनशॉट टूल है, जो आपकी ट्रे में रहता है।%n%nसेटअप इसे सिर्फ़ आपके लिए इंस्टॉल करता है, एडमिन अधिकारों की ज़रूरत नहीं। पूरा होने पर Cometsnap Print Screen कुंजी संभाल लेता है और आपका पुराना स्क्रीन कैप्चर टूल (Snipping Tool, Greenshot, ShareX, Lightshot...) बदलने का सुझाव देता है।%n%nआगे बढ़ने के लिए आगे पर क्लिक करें।
en.UpdateTitle=Update Cometsnap
fr.UpdateTitle=Mettre à jour Cometsnap
de.UpdateTitle=Cometsnap aktualisieren
it.UpdateTitle=Aggiorna Cometsnap
es.UpdateTitle=Actualizar Cometsnap
pt.UpdateTitle=Atualizar o Cometsnap
zh.UpdateTitle=更新 Cometsnap
hi.UpdateTitle=Cometsnap अपडेट करें
en.UpdateText=Cometsnap %1 is installed on this PC.%n%nSetup will close it and replace it with version %2.%nYour settings and captures are kept.%n%nClick Next to continue.
fr.UpdateText=Cometsnap %1 est installé sur ce PC.%n%nL’installation va le fermer et le remplacer par la version %2.%nVos réglages et vos captures sont conservés.%n%nCliquez sur Suivant pour continuer.
de.UpdateText=Cometsnap %1 ist auf diesem PC installiert.%n%nSetup schließt es und ersetzt es durch Version %2.%nIhre Einstellungen und Aufnahmen bleiben erhalten.%n%nKlicken Sie auf Weiter, um fortzufahren.
it.UpdateText=Cometsnap %1 è installato su questo PC.%n%nL’installazione lo chiuderà e lo sostituirà con la versione %2.%nLe tue impostazioni e catture restano al loro posto.%n%nFai clic su Avanti per continuare.
es.UpdateText=Cometsnap %1 está instalado en este PC.%n%nEl instalador lo cerrará y lo sustituirá por la versión %2.%nTus ajustes y capturas se conservan.%n%nHaz clic en Siguiente para continuar.
pt.UpdateText=O Cometsnap %1 está instalado neste PC.%n%nA instalação vai fechá-lo e substituí-lo pela versão %2.%nSuas configurações e capturas são mantidas.%n%nClique em Avançar para continuar.
zh.UpdateText=这台电脑上已安装 Cometsnap %1。%n%n安装程序会关闭它，并替换为 %2 版本。%n你的设置和截图都会保留。%n%n点击“下一步”继续。
hi.UpdateText=इस PC पर Cometsnap %1 इंस्टॉल है।%n%nसेटअप इसे बंद करके वर्शन %2 से बदल देगा।%nआपकी सेटिंग्स और कैप्चर बने रहेंगे।%n%nआगे बढ़ने के लिए आगे पर क्लिक करें।
en.ReinstallTitle=Reinstall Cometsnap %1
fr.ReinstallTitle=Réinstaller Cometsnap %1
de.ReinstallTitle=Cometsnap %1 neu installieren
it.ReinstallTitle=Reinstalla Cometsnap %1
es.ReinstallTitle=Reinstalar Cometsnap %1
pt.ReinstallTitle=Reinstalar o Cometsnap %1
zh.ReinstallTitle=重新安装 Cometsnap %1
hi.ReinstallTitle=Cometsnap %1 फिर से इंस्टॉल करें
en.ReinstallText=This version of Cometsnap is already installed.%n%nSetup will close it and install it again, which repairs it.%nYour settings and captures are kept.%n%nClick Next to continue.
fr.ReinstallText=Cette version de Cometsnap est déjà installée.%n%nL’installation va le fermer et le réinstaller, ce qui le répare.%nVos réglages et vos captures sont conservés.%n%nCliquez sur Suivant pour continuer.
de.ReinstallText=Diese Version von Cometsnap ist bereits installiert.%n%nSetup schließt es und installiert es erneut, was es repariert.%nIhre Einstellungen und Aufnahmen bleiben erhalten.%n%nKlicken Sie auf Weiter, um fortzufahren.
it.ReinstallText=Questa versione di Cometsnap è già installata.%n%nL’installazione lo chiuderà e lo installerà di nuovo, riparandolo.%nLe tue impostazioni e catture restano al loro posto.%n%nFai clic su Avanti per continuare.
es.ReinstallText=Esta versión de Cometsnap ya está instalada.%n%nEl instalador la cerrará y la instalará de nuevo, lo que la repara.%nTus ajustes y capturas se conservan.%n%nHaz clic en Siguiente para continuar.
pt.ReinstallText=Esta versão do Cometsnap já está instalada.%n%nA instalação vai fechá-lo e instalá-lo de novo, o que o conserta.%nSuas configurações e capturas são mantidas.%n%nClique em Avançar para continuar.
zh.ReinstallText=此版本的 Cometsnap 已经安装。%n%n安装程序会关闭它并重新安装，顺便修复它。%n你的设置和截图都会保留。%n%n点击“下一步”继续。
hi.ReinstallText=Cometsnap का यह वर्शन पहले से इंस्टॉल है।%n%nसेटअप इसे बंद करके फिर से इंस्टॉल करेगा, जिससे यह ठीक हो जाता है।%nआपकी सेटिंग्स और कैप्चर बने रहेंगे।%n%nआगे बढ़ने के लिए आगे पर क्लिक करें।
en.MemoFresh=New install: Cometsnap %1
fr.MemoFresh=Nouvelle installation : Cometsnap %1
de.MemoFresh=Neuinstallation: Cometsnap %1
it.MemoFresh=Nuova installazione: Cometsnap %1
es.MemoFresh=Instalación nueva: Cometsnap %1
pt.MemoFresh=Nova instalação: Cometsnap %1
zh.MemoFresh=全新安装：Cometsnap %1
hi.MemoFresh=नया इंस्टॉल: Cometsnap %1
en.MemoUpdate=Update: Cometsnap %1 will be replaced by %2
fr.MemoUpdate=Mise à jour : Cometsnap %1 sera remplacé par %2
de.MemoUpdate=Update: Cometsnap %1 wird durch %2 ersetzt
it.MemoUpdate=Aggiornamento: Cometsnap %1 sarà sostituito da %2
es.MemoUpdate=Actualización: Cometsnap %1 se sustituirá por %2
pt.MemoUpdate=Atualização: o Cometsnap %1 será substituído pelo %2
zh.MemoUpdate=更新：Cometsnap %1 将替换为 %2
hi.MemoUpdate=अपडेट: Cometsnap %1 की जगह %2 आएगा
en.MemoReinstall=Reinstall: Cometsnap %1 will be installed again
fr.MemoReinstall=Réinstallation : Cometsnap %1 sera installé à nouveau
de.MemoReinstall=Neuinstallation: Cometsnap %1 wird erneut installiert
it.MemoReinstall=Reinstallazione: Cometsnap %1 sarà installato di nuovo
es.MemoReinstall=Reinstalación: Cometsnap %1 se instalará de nuevo
pt.MemoReinstall=Reinstalação: o Cometsnap %1 será instalado de novo
zh.MemoReinstall=重新安装：将再次安装 Cometsnap %1
hi.MemoReinstall=फिर से इंस्टॉल: Cometsnap %1 दोबारा इंस्टॉल होगा
en.MemoWhat=What Setup does:
fr.MemoWhat=Ce que fait l’installation :
de.MemoWhat=Was Setup macht:
it.MemoWhat=Cosa fa l’installazione:
es.MemoWhat=Lo que hace el instalador:
pt.MemoWhat=O que a instalação faz:
zh.MemoWhat=安装程序会：
hi.MemoWhat=सेटअप क्या करता है:
en.MemoClose=Closes any running Cometsnap (also a downloaded Cometsnap-x64.exe)
fr.MemoClose=Ferme tout Cometsnap en cours (aussi un Cometsnap-x64.exe téléchargé)
de.MemoClose=Schließt jedes laufende Cometsnap (auch ein heruntergeladenes Cometsnap-x64.exe)
it.MemoClose=Chiude ogni Cometsnap in esecuzione (anche un Cometsnap-x64.exe scaricato)
es.MemoClose=Cierra cualquier Cometsnap abierto (también un Cometsnap-x64.exe descargado)
pt.MemoClose=Fecha qualquer Cometsnap aberto (também um Cometsnap-x64.exe baixado)
zh.MemoClose=关闭正在运行的 Cometsnap（包括下载的 Cometsnap-x64.exe）
hi.MemoClose=चल रहा हर Cometsnap बंद करता है (डाउनलोड किया Cometsnap-x64.exe भी)
en.MemoInstallIn=Installs Cometsnap in %1
fr.MemoInstallIn=Installe Cometsnap dans %1
de.MemoInstallIn=Installiert Cometsnap in %1
it.MemoInstallIn=Installa Cometsnap in %1
es.MemoInstallIn=Instala Cometsnap en %1
pt.MemoInstallIn=Instala o Cometsnap em %1
zh.MemoInstallIn=将 Cometsnap 安装到 %1
hi.MemoInstallIn=Cometsnap को %1 में इंस्टॉल करता है
en.MemoStart=Adds it to the Start menu and to Apps (so you can uninstall it)
fr.MemoStart=L’ajoute au menu Démarrer et aux Applications (pour pouvoir le désinstaller)
de.MemoStart=Fügt es dem Startmenü und den Apps hinzu (damit Sie es deinstallieren können)
it.MemoStart=Lo aggiunge al menu Start e alle App (così puoi disinstallarlo)
es.MemoStart=Lo añade al menú Inicio y a Aplicaciones (para poder desinstalarlo)
pt.MemoStart=Adiciona ao menu Iniciar e a Aplicativos (para você poder desinstalar)
zh.MemoStart=添加到开始菜单和“应用”中（方便卸载）
hi.MemoStart=इसे स्टार्ट मेनू और ऐप्स में जोड़ता है (ताकि आप इसे अनइंस्टॉल कर सकें)
en.MemoKeep=Keeps your settings and captures
fr.MemoKeep=Conserve vos réglages et vos captures
de.MemoKeep=Behält Ihre Einstellungen und Aufnahmen
it.MemoKeep=Mantiene le tue impostazioni e catture
es.MemoKeep=Conserva tus ajustes y capturas
pt.MemoKeep=Mantém suas configurações e capturas
zh.MemoKeep=保留你的设置和截图
hi.MemoKeep=आपकी सेटिंग्स और कैप्चर बनाए रखता है
en.MemoThen=Then Cometsnap offers to replace your old screen capture tool
fr.MemoThen=Puis Cometsnap propose de remplacer votre vieil outil de capture
de.MemoThen=Danach bietet Cometsnap an, Ihr altes Screenshot-Tool zu ersetzen
it.MemoThen=Poi Cometsnap propone di sostituire il tuo vecchio strumento di cattura
es.MemoThen=Después, Cometsnap te ofrece sustituir tu vieja herramienta de captura
pt.MemoThen=Depois, o Cometsnap oferece trocar sua velha ferramenta de captura
zh.MemoThen=然后 Cometsnap 会帮你换掉旧的截图工具
hi.MemoThen=फिर Cometsnap आपका पुराना स्क्रीन कैप्चर टूल बदलने का सुझाव देता है
en.ButtonUpdate=Update
fr.ButtonUpdate=Mettre à jour
de.ButtonUpdate=Aktualisieren
it.ButtonUpdate=Aggiorna
es.ButtonUpdate=Actualizar
pt.ButtonUpdate=Atualizar
zh.ButtonUpdate=更新
hi.ButtonUpdate=अपडेट करें
en.ButtonReinstall=Reinstall
fr.ButtonReinstall=Réinstaller
de.ButtonReinstall=Neu installieren
it.ButtonReinstall=Reinstalla
es.ButtonReinstall=Reinstalar
pt.ButtonReinstall=Reinstalar
zh.ButtonReinstall=重新安装
hi.ButtonReinstall=फिर से इंस्टॉल करें
en.DoneFresh=Cometsnap is installed
fr.DoneFresh=Cometsnap est installé
de.DoneFresh=Cometsnap ist installiert
it.DoneFresh=Cometsnap è installato
es.DoneFresh=Cometsnap está instalado
pt.DoneFresh=O Cometsnap está instalado
zh.DoneFresh=Cometsnap 已安装
hi.DoneFresh=Cometsnap इंस्टॉल हो गया
en.DoneUpdate=Cometsnap is updated to %1
fr.DoneUpdate=Cometsnap est à jour en %1
de.DoneUpdate=Cometsnap ist jetzt auf %1
it.DoneUpdate=Cometsnap è aggiornato alla %1
es.DoneUpdate=Cometsnap está actualizado a la %1
pt.DoneUpdate=O Cometsnap foi atualizado para %1
zh.DoneUpdate=Cometsnap 已更新到 %1
hi.DoneUpdate=Cometsnap %1 पर अपडेट हो गया
en.DoneReinstall=Cometsnap is reinstalled
fr.DoneReinstall=Cometsnap est réinstallé
de.DoneReinstall=Cometsnap ist neu installiert
it.DoneReinstall=Cometsnap è reinstallato
es.DoneReinstall=Cometsnap está reinstalado
pt.DoneReinstall=O Cometsnap foi reinstalado
zh.DoneReinstall=Cometsnap 已重新安装
hi.DoneReinstall=Cometsnap फिर से इंस्टॉल हो गया
en.DoneText=Cometsnap lives in your tray, next to the clock, and in the Start menu.%n%nLeave the box below ticked to start it now and choose which screen capture tool it replaces.
fr.DoneText=Cometsnap vit dans votre barre des tâches, près de l’horloge, et dans le menu Démarrer.%n%nLaissez la case ci-dessous cochée pour le lancer maintenant et choisir quel outil de capture il remplace.
de.DoneText=Cometsnap wohnt im Infobereich neben der Uhr und im Startmenü.%n%nLassen Sie das Häkchen unten gesetzt, um es jetzt zu starten und zu wählen, welches Screenshot-Tool es ersetzt.
it.DoneText=Cometsnap abita nell’area di notifica, vicino all’orologio, e nel menu Start.%n%nLascia selezionata la casella qui sotto per avviarlo ora e scegliere quale strumento di cattura sostituisce.
es.DoneText=Cometsnap vive en tu bandeja, junto al reloj, y en el menú Inicio.%n%nDeja marcada la casilla de abajo para iniciarlo ahora y elegir qué herramienta de captura sustituye.
pt.DoneText=O Cometsnap mora na sua bandeja, perto do relógio, e no menu Iniciar.%n%nDeixe a caixa abaixo marcada para iniciá-lo agora e escolher qual ferramenta de captura ele substitui.
zh.DoneText=Cometsnap 住在时钟旁边的系统托盘里，也在开始菜单中。%n%n保持勾选下面的选项，即可立即启动它，并选择要替换哪个截图工具。
hi.DoneText=Cometsnap आपकी ट्रे में घड़ी के पास और स्टार्ट मेनू में रहता है।%n%nइसे अभी शुरू करने और यह चुनने के लिए कि यह कौन-सा स्क्रीन कैप्चर टूल बदले, नीचे वाला बॉक्स चुना रहने दें।

[Tasks]
Name: autostart; Description: "{cm:TaskAutostart}"
Name: desktopicon; Description: "{cm:TaskDesktop}"; Flags: unchecked

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; DestName: "Cometsnap.exe"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Cometsnap"; Filename: "{app}\Cometsnap.exe"
Name: "{autodesktop}\Cometsnap"; Filename: "{app}\Cometsnap.exe"; Tasks: desktopicon

[Registry]
; Cometsnap reads this once at its next start, so its own setting matches the choice made here.
Root: HKCU; Subkey: "Software\Cometsnap"; ValueType: dword; ValueName: "InstallerStartWithWindows"; ValueData: 1; Tasks: autostart; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Cometsnap"; ValueType: dword; ValueName: "InstallerStartWithWindows"; ValueData: 0; Tasks: not autostart; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Cometsnap"; ValueData: """{app}\Cometsnap.exe"""; Tasks: autostart; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Cometsnap"; Tasks: not autostart; Flags: deletevalue

[Run]
Filename: "{app}\Cometsnap.exe"; Parameters: "--after-install"; Description: "{cm:RunAfter}"; Flags: nowait postinstall skipifsilent

[Code]
const
  EVENT_MODIFY_STATE = $0002;
  SYNCHRONIZE = $00100000;
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  ModeFresh = 0;
  ModeUpdate = 1;
  ModeReinstall = 2;

var
  PreviousVersion: String;
  Mode: Integer;

function OpenEvent(dwDesiredAccess: DWORD; bInheritHandle: BOOL; lpName: String): THandle;
  external 'OpenEventW@kernel32.dll stdcall';
function SetEvent(hEvent: THandle): BOOL;
  external 'SetEvent@kernel32.dll stdcall';
function OpenMutex(dwDesiredAccess: DWORD; bInheritHandle: BOOL; lpName: String): THandle;
  external 'OpenMutexW@kernel32.dll stdcall';
function CloseHandle(hObject: THandle): BOOL;
  external 'CloseHandle@kernel32.dll stdcall';

function CometsnapRunning: Boolean;
var
  H: THandle;
begin
  H := OpenMutex(SYNCHRONIZE, False, 'Local\Cometsnap.SingleInstance');
  Result := H <> 0;
  if Result then CloseHandle(H);
end;

{ Asks a running Cometsnap (installed or a downloaded exe) to quit, then makes sure it is gone. }
procedure CloseCometsnap;
var
  H: THandle;
  I, Code: Integer;
begin
  if CometsnapRunning then
  begin
    H := OpenEvent(EVENT_MODIFY_STATE, False, 'Local\Cometsnap.Quit');
    if H <> 0 then
    begin
      SetEvent(H);
      CloseHandle(H);
    end;
    I := 0;
    while CometsnapRunning and (I < 50) do
    begin
      Sleep(100);
      I := I + 1;
    end;
  end;
  { Also catches copies that are not answering, under any of their download names. }
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM Cometsnap.exe /IM Cometsnap-x64.exe /IM Cometsnap-arm64.exe',
    '', SW_HIDE, ewWaitUntilTerminated, Code);
end;

function InitializeSetup: Boolean;
begin
  PreviousVersion := '';
  RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#AppIdBraced}_is1', 'DisplayVersion', PreviousVersion);
  if PreviousVersion = '' then
    Mode := ModeFresh
  else if PreviousVersion = '{#AppVersion}' then
    Mode := ModeReinstall
  else
    Mode := ModeUpdate;
  Result := True;
end;

procedure InitializeWizard;
begin
  case Mode of
    ModeFresh:
      begin
        WizardForm.WelcomeLabel1.Caption := FmtMessage(CustomMessage('FreshTitle'), ['{#AppVersion}']);
        WizardForm.WelcomeLabel2.Caption := CustomMessage('FreshText');
      end;
    ModeUpdate:
      begin
        WizardForm.WelcomeLabel1.Caption := CustomMessage('UpdateTitle');
        WizardForm.WelcomeLabel2.Caption := FmtMessage(CustomMessage('UpdateText'), [PreviousVersion, '{#AppVersion}']);
      end;
    ModeReinstall:
      begin
        WizardForm.WelcomeLabel1.Caption := FmtMessage(CustomMessage('ReinstallTitle'), ['{#AppVersion}']);
        WizardForm.WelcomeLabel2.Caption := CustomMessage('ReinstallText');
      end;
  end;
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo,
  MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  case Mode of
    ModeFresh: Result := FmtMessage(CustomMessage('MemoFresh'), ['{#AppVersion}']);
    ModeUpdate: Result := FmtMessage(CustomMessage('MemoUpdate'), [PreviousVersion, '{#AppVersion}']);
  else
    Result := FmtMessage(CustomMessage('MemoReinstall'), ['{#AppVersion}']);
  end;
  Result := Result + NewLine + NewLine + CustomMessage('MemoWhat') + NewLine +
    Space + CustomMessage('MemoClose') + NewLine +
    Space + FmtMessage(CustomMessage('MemoInstallIn'), [ExpandConstant('{app}')]) + NewLine +
    Space + CustomMessage('MemoStart') + NewLine;
  if Mode <> ModeFresh then
    Result := Result + Space + CustomMessage('MemoKeep') + NewLine;
  Result := Result + Space + CustomMessage('MemoThen') + NewLine;
  if MemoTasksInfo <> '' then
    Result := Result + NewLine + MemoTasksInfo;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpReady then
  begin
    if Mode = ModeUpdate then WizardForm.NextButton.Caption := CustomMessage('ButtonUpdate')
    else if Mode = ModeReinstall then WizardForm.NextButton.Caption := CustomMessage('ButtonReinstall');
  end
  else if CurPageID = wpFinished then
  begin
    case Mode of
      ModeFresh: WizardForm.FinishedHeadingLabel.Caption := CustomMessage('DoneFresh');
      ModeUpdate: WizardForm.FinishedHeadingLabel.Caption := FmtMessage(CustomMessage('DoneUpdate'), ['{#AppVersion}']);
    else
      WizardForm.FinishedHeadingLabel.Caption := CustomMessage('DoneReinstall');
    end;
    WizardForm.FinishedLabel.Caption := CustomMessage('DoneText');
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  CloseCometsnap;
  Result := '';
end;

function InitializeUninstall: Boolean;
begin
  CloseCometsnap;
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Value: Cardinal;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    { Cometsnap may have pointed startup at a downloaded copy: remove that too. }
    RegDeleteValue(HKCU, RunKey, 'Cometsnap');
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run', 'Cometsnap');
    { Give Print Screen back to Windows' Snipping Tool if Cometsnap had taken it. }
    if RegQueryDWordValue(HKCU, 'Control Panel\Keyboard', 'PrintScreenKeyForSnippingEnabled', Value) and (Value = 0) then
      RegWriteDWordValue(HKCU, 'Control Panel\Keyboard', 'PrintScreenKeyForSnippingEnabled', 1);
  end;
end;
