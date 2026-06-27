# TorVps — C# WPF Dashboard

Нативное Windows-приложение: дашборд для цепочки **Tor → mihomo → VPS**.
Переписан с Rust + Tauri + React на C# + WPF (.NET 9), с сохранением визуального стиля
исходного приложения (`reference/tor-tauri/`).

Показывает статус Tor и mihomo, bridges, health-чипы, график пропускной способности/пинга,
метрики удалённого VPS (Glances API) и runtime-лог — и позволяет включать/выключать
туннель прямо из UI.

## Требования

- Windows 10/11
- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- Развёрнутая цепочка Tor/mihomo в **`C:\tor\`** (путь настраивается в `torrc_manager.cfg`):
  - `tor.exe`, `mihomo.exe`, `pluggable_transports\lyrebird.exe`
  - `torrc_manager.cfg`, `mihomo.yaml`, `bridges.txt`
  - `data\control_auth_cookie` появляется после первого запуска Tor

Без этого набора файлов приложение всё равно запустится (Conf-таб покажет, чего не хватает),
но цепочку поднять не получится.

## Структура

```
src/TorVps.Core/     ← Бизнес-логика. БЕЗ UI-зависимостей. Тестируемо.
  Models/            ← Data-классы (DashboardState, ServerMetrics, BridgeRecord, ...)
  Interfaces/        ← Контракты сервисов (ITorService, IMihomoService, ...)
  Services/          ← Реализации (TorService, MihomoService, Socks5Probe, ...)
  Config/            ← Парсинг torrc_manager.cfg / mihomo.yaml, генерация torrc

src/TorVps.App/      ← WPF UI. Только UI-код.
  ViewModels/        ← DashboardViewModel (MVVM, CommunityToolkit.Mvvm)
  Converters/        ← HealthState → Brush конвертеры
  Resources/          ← Theme.xaml (палитра, стили)
  MainWindow.xaml     ← Главное окно дашборда

tests/TorVps.Tests/  ← xUnit + Moq тесты (только для Core)
```

Список модулей `TorVps.Core`, полностью покрытых тестами — в [DONE_MODULES.md](DONE_MODULES.md).

## Команды

Через `dotnet` напрямую:

```sh
dotnet build                                              # компиляция = typecheck
dotnet test                                               # тесты
dotnet format --verify-no-changes                         # lint
dotnet publish src/TorVps.App -c Release -r win-x64 --self-contained
```

Или через `make` (обёртка над теми же командами):

```sh
make build
make test
make lint
make publish
make clean
```

## Правила домена

- Базовая директория: `C:\tor\` (настраивается в `torrc_manager.cfg`)
- **Порядок выключения**: mihomo → потом Tor (никогда не наоборот)
- Все дочерние процессы: `CreateNoWindow=true`, `UseShellExecute=false`
- Windows proxy: `HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings`
- Tor Control: cookie-auth из `C:\tor\data\control_auth_cookie`

Подробнее — в [CLAUDE.md](CLAUDE.md).
