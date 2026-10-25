# LiveDLSS 0.1.0

Камера iPhone → Windows-ПК с NVIDIA RTX → DLSS / Neural Rendering → изображение на iPhone.

[Русский](#запуск) · [English](#quick-start)

## Запуск

1. Скачайте `LiveDLSS-0.1.0-Windows-x64.zip` из [Releases](../../releases) и распакуйте архив целиком.
2. Добавьте свою **`nvngx_dlssnr.dll`** в папку **`LiveDLSSServer`**, рядом с exe:

   ```text
   LiveDLSS-Windows-x64/
   ├── Run-LiveDLSS.bat
   └── LiveDLSSServer/
       ├── LiveDLSSServer.exe
       ├── nvngx.dll_LiveDLSS.NeuralRendering.dll  ← уже в сборке
       └── nvngx_dlssnr.dll                      ← добавьте сами
   ```

   Модели NVIDIA в релизе нет. Мост не переименовывайте. `nvngx_dlss.dll` для апскейла не заменяет NR-модель.
3. Подключите ПК и iPhone к одной локальной сети. Для сети Windows выберите профиль **«Частная»**.
4. В папке сборки один раз выполните в PowerShell **от администратора**:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\Allow-Firewall.ps1
   ```

5. Запустите **`Run-LiveDLSS.bat`** и оставьте сервер открытым. .NET устанавливать не нужно.
6. На iPhone откройте приложение, разрешите камеру и локальную сеть, выберите ПК. Если он не найден, введите его IPv4 и порт: `192.168.1.10:47800`.
7. Выберите вид **«Результат»**. В инспекторе ПК дождитесь статуса **`nvngx_dlssnr.dll активна`** и превью **«Выход (Neural Rendering → iPhone)»**.

**DLSS на iPhone переключает только апскейл.** Нейрорендеринг включается отдельно в инспекторе ПК. После добавления DLL перезапустите сервер. NR требует совместимых GPU и драйвера; текущая интеграция рассчитана на RTX 50.

### Установка на iPhone из исходников

Нужны Mac, Xcode и XcodeGen. Из корня репозитория:

```bash
brew install xcodegen
cp apps/ios/Configs/Local.xcconfig.example apps/ios/Configs/Local.xcconfig
```

Укажите в `Local.xcconfig` свой `DEVELOPMENT_TEAM` и уникальный `LIVEDLSS_BUNDLE_ID_PREFIX`. Добавьте Apple Account в Xcode, подключите разблокированный iPhone и подтвердите доверие и режим разработчика, если потребуется.

```bash
xcrun devicectl list devices
make ios-run IOS_DEVICE=<UDID_физического_iPhone>
```

Команда соберёт, установит и запустит приложение. Готовый архив из Releases предназначен только для Windows.

## Quick start

1. Download `LiveDLSS-0.1.0-Windows-x64.zip` from [Releases](../../releases) and extract it.
2. Put your **`nvngx_dlssnr.dll`** beside **`LiveDLSSServer.exe`** inside the `LiveDLSSServer` folder. The NVIDIA model is not included; the `nvngx.dll_LiveDLSS.NeuralRendering.dll` bridge is included. Keep its name unchanged.
3. Connect the PC and iPhone to the same local network. Set the Windows network profile to **Private**.
4. Run this once in an **administrator PowerShell** from the extracted package root:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\Allow-Firewall.ps1
   ```

5. Start **`Run-LiveDLSS.bat`**. No .NET installation is needed.
6. Open the iPhone app, allow camera and local network access, and select the PC. For a manual connection, use its IPv4 and port, e.g. `192.168.1.10:47800`.
7. Select **Result** and wait for **`nvngx_dlssnr.dll active`** in the PC inspector. The NR output preview is the image sent to the phone.

The iPhone **DLSS** switch controls upscaling only; toggle **NR** separately on the PC. Restart the server after adding the DLL. NR requires a compatible GPU/driver; this integration targets RTX 50.

To install the iOS app from source, use a Mac with Xcode and XcodeGen. Copy `apps/ios/Configs/Local.xcconfig.example` to `Local.xcconfig`, set your development team and unique bundle prefix, add your Apple Account in Xcode, then connect and unlock the phone:

```bash
xcrun devicectl list devices
make ios-run IOS_DEVICE=<PHYSICAL_IPHONE_UDID>
```

## Разработка / Development

- `apps/ios` — SwiftUI app + `LiveDLSSKit`.
- `apps/windows` — C# server + native bridges.
- `tools/windows-package` — packaging scripts.

```bash
make help
make win-build       # .NET SDK 10
make win-package     # requires local FFmpeg runtime files
make ios-build
```

`make win-package` включает NR-мост и исключает модель NVIDIA из архива. / Includes the NR bridge and excludes the NVIDIA model.
