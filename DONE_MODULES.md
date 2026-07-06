# Protected Modules

Файлы в этом списке **нельзя изменять** в PR.
CI автоматически блокирует любой PR который их трогает.

Байпас-команд не существует — это сделано специально.

## Как снять защиту с файла

1. Создать **отдельный** PR который трогает **только этот файл**
2. Удалить файл из списка ниже
3. Смержить этот PR
4. После этого файл можно менять

## Как добавить файл в защиту

Когда модуль готов и отлажен — добавьте строку:
```
- src/TorVps.Core/Services/ИмяКласса.cs
```

---

## Готовые модули (защищены)

<!-- Добавляйте сюда файлы по мере готовности, например: -->
<!-- - src/TorVps.Core/Services/TorService.cs -->
<!-- - src/TorVps.Core/Models/TorStatus.cs -->

- src/TorVps.Core/Config/ConfigParser.cs
- src/TorVps.Core/Config/TorrcGenerator.cs
- src/TorVps.Core/Services/Socks5Probe.cs
- src/TorVps.Core/Services/WindowsProxyService.cs
- src/TorVps.Core/Services/ProcessManager.cs
- src/TorVps.Core/Services/ConfigCheckService.cs

