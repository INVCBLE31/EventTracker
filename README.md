# EventTracker

> EventTracker автоматически собирает готовое `.exe` приложение для удобного запуска на Windows.

<img width="256" height="256" alt="app_preview" src="https://github.com/user-attachments/assets/2666947a-cbb4-4b81-bbcc-adf276827b12" />

## Скриншоты

<img width="1288" height="789" alt="Снимок экрана 2026-05-23 020001" src="https://github.com/user-attachments/assets/ac755260-9ec5-45a9-8bfd-97e1f39bc9b8" />

## Установка

После сборки проекта необходимо:

1. Открыть папку:

```text
AppData/Roaming/
```

2. Создать папку:

```text
EventTracker
```

3. Переместить все установленные файлы приложения в:

```text
AppData/Roaming/EventTracker/
```

После этого:

4. Запустить файл:

```text
build.bat
```

После завершения сборки `.exe` файл появится в папке:

```text
publish/
```

5. Нажать ПКМ по `.exe` файлу и выбрать:

```text
Создать ярлык
```

6. Переместить ярлык в любое удобное место:
- Desktop
- Taskbar
- Start Menu

После этого приложение можно запускать как обычную Windows utility.


Современный system monitoring / forensic timeline tool для Windows.

EventTracker в реальном времени отслеживает изменения на компьютере и сохраняет историю активности системы в локальную базу данных.

Приложение позволяет:
- видеть изменения файлов и папок,
- отслеживать запуск приложений,
- искать события по приложениям, файлам и папкам,
- анализировать историю активности ПК,
- просматривать timeline изменений системы.

---

# Основные возможности

## Глобальный мониторинг файловой системы

EventTracker отслеживает:
- создание файлов,
- удаление файлов,
- изменение файлов,
- переименование файлов,
- создание папок,
- удаление папок.

Мониторинг работает для:
- всех дисков,
- всех директорий,
- вложенных папок.

Для каждого события сохраняется:
- время,
- тип события,
- полный путь,
- имя файла,
- имя папки,
- процесс-источник (если доступен).

---

## Мониторинг процессов

EventTracker отслеживает:
- запуск приложений,
- завершение приложений,
- активность процессов.

Логируется:
- process name,
- PID,
- executable path,
- время запуска.

---

## Timeline активности

Главный интерфейс отображает события в реальном времени:

```text
[14:03] chrome.exe started
[14:04] setup.exe created file in Downloads
[14:05] discord.exe modified cache
[14:07] Steam updated game files
```

Timeline автоматически обновляется при новых событиях.

---

## Поиск

EventTracker поддерживает глобальный поиск по истории событий.

Можно искать:
- приложения,
- файлы,
- папки,
- расширения,
- действия,
- типы событий.

Примеры запросов:

```text
chrome.exe
Downloads
deleted
setup.exe
png
Steam
```

Поиск отображает все связанные события:
- запуск процессов,
- изменение файлов,
- удаление,
- создание,
- переименование.

---

## Навигация по папкам

В приложении доступен sidebar/tree view файловой системы.

Пользователь может:
- открывать диски,
- переходить по папкам,
- просматривать историю изменений конкретной директории.

Пример:

```text
D:/Games/Minecraft/

[14:03] options.txt modified
[14:05] mods.zip added
[14:07] shaderpack deleted
```

---

## Фильтрация

Поддерживаются фильтры:
- Files
- Folders
- Processes
- USB
- Network
- Errors
- Warnings

---

# Технологии

## Backend
- C#
- .NET
- SQLite

## UI
- WPF
- MVVM Architecture

## Monitoring
- FileSystemWatcher
- WMI
- Process API

---

# База данных

Используется SQLite.

Таблица событий:

```sql
CREATE TABLE events (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    timestamp TEXT,
    category TEXT,
    process_name TEXT,
    action TEXT,
    path TEXT,
    file_name TEXT,
    directory_name TEXT,
    details TEXT
);
```

Индексы:

```sql
CREATE INDEX idx_process_name ON events(process_name);
CREATE INDEX idx_path ON events(path);
CREATE INDEX idx_timestamp ON events(timestamp);
CREATE INDEX idx_category ON events(category);
```

---

# Производительность

EventTracker разработан для:
- длительной стабильной работы,
- минимальной нагрузки на CPU,
- асинхронной записи событий,
- обработки большого количества изменений,
- работы в background режиме.

---

# Дополнительные возможности

Планируемые функции:
- экспорт логов,
- dashboard активности,
- статистика приложений,
- suspicious activity detection,
- heatmap активности,
- “что изменилось сегодня”,
- аналитика активности приложений.

---

# Интерфейс

Особенности UI:
- dark mode,
- минималистичный дизайн,
- современный timeline,
- быстрый поиск,
- live updates,
- удобная навигация.

---

# Цель проекта

EventTracker создаётся как упрощённый и удобный аналог:
- Process Monitor,
- GlassWire,
- forensic timeline tools.

Основная цель — дать обычному пользователю удобный инструмент для просмотра всей истории изменений системы.

---

# Лицензия

MIT License

