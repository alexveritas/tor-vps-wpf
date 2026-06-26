# TorVps — C# WPF Dashboard

Нативное Windows-приложение: дашборд для цепочки Tor → mihomo → VPS.
Переписан с Rust+Tauri+React на C# + WPF (.NET 9).

## Структура

```
src/TorVps.Core/     ← Бизнес-логика. БЕЗ UI-зависимостей. Тестируемо.
  Models/            ← Data-классы (DashboardState, ServerMetrics, BridgeRecord)
  Services/          ← Сервисы (TorService, MihomoService, Socks5Probe, ...)
  Config/            ← Парсинг torrc_manager.cfg и mihomo.yaml

src/TorVps.App/      ← WPF UI. Только UI-код.
  ViewModels/        ← INotifyPropertyChanged ViewModels
  Views/             ← XAML-окна и контролы

tests/TorVps.Tests/  ← xUnit тесты (только для Core)
```

## Команды

```sh
dotnet build                           # компиляция = typecheck
dotnet test                            # тесты
dotnet format --verify-no-changes      # lint
dotnet publish src/TorVps.App -c Release -r win-x64 --self-contained
```

## Правила домена

- Базовая директория: C:\tor\ (настраивается в torrc_manager.cfg)
- ПОРЯДОК ВЫКЛЮЧЕНИЯ: mihomo → потом Tor (никогда не наоборот)
- Все дочерние процессы: CreateNoWindow=true, UseShellExecute=false
- Windows proxy: HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings
- Tor Control: cookie-auth из C:\tor\data\control_auth_cookie

## Запрещено

- UI-зависимости в TorVps.Core
- .exe/.dll/.zip в git
- Thread.Sleep в UI-потоке (только async/await)
- IDisposable без using / Dispose
