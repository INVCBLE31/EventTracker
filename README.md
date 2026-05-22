# EventTracker

Современный system monitoring / forensic timeline tool для Windows.

EventTracker в реальном времени отслеживает активность компьютера, сохраняет историю изменений системы и позволяет анализировать происходящее через AI.

<img width="256" height="256" alt="app_preview" src="https://github.com/user-attachments/assets/2666947a-cbb4-4b81-bbcc-adf276827b12" />

---

# Скриншоты

<img width="1288" height="789" alt="Снимок экрана 2026-05-23 020001" src="https://github.com/user-attachments/assets/ac755260-9ec5-45a9-8bfd-97e1f39bc9b8" />

---

# Возможности

## Глобальный мониторинг системы

EventTracker отслеживает:

* создание файлов,
* удаление файлов,
* изменение файлов,
* переименование файлов,
* создание папок,
* удаление папок,
* запуск приложений,
* завершение процессов,
* активность системы.

Мониторинг работает:

* на всех дисках,
* во всех директориях,
* включая вложенные папки.

Для каждого события сохраняется:

* время,
* тип события,
* полный путь,
* имя файла,
* имя папки,
* process name,
* executable path,
* дополнительная информация.

---

## Timeline активности

Главный интерфейс отображает события системы в реальном времени:

```text
[14:03] chrome.exe started
[14:04] setup.exe created file in Downloads
[14:05] discord.exe modified cache
[14:07] Steam updated game files
```

Timeline автоматически обновляется при новых событиях.

---

## Глобальный поиск

EventTracker поддерживает быстрый поиск по всей истории системы.

Можно искать:

* приложения,
* файлы,
* папки,
* расширения,
* действия,
* типы событий.

Примеры:

```text
chrome.exe
Downloads
deleted
setup.exe
png
Steam
```

Поиск отображает:

* запуск процессов,
* изменение файлов,
* удаление,
* создание,
* переименование,
* активность приложений.

---

## Навигация по папкам

В приложении доступен sidebar/tree view файловой системы.

Пользователь может:

* открывать диски,
* переходить по папкам,
* просматривать историю изменений конкретной директории.

Пример:

```text
D:/Games/Minecraft/

[14:03] options.txt modified
[14:05] mods.zip added
[14:07] shaderpack deleted
```

---

# AI Features

EventTracker поддерживает AI-анализ истории системы на базе OpenAI.

---

## “Объясни что происходило”

AI анализирует выбранный промежуток времени и объясняет события человеческим языком.

Пример:

> “В 14:03 пользователь установил программу setup.exe, которая создала 47 файлов в AppData и добавила себя в автозапуск.”

---

## Автоматическая классификация событий

AI автоматически определяет тип активности:

* установка,
* обновление,
* игровая сессия,
* подозрительная активность,
* системные изменения.

---

## Чат с историей ПК

Теперь можно задавать вопросы истории системы.

Примеры:

```text
Что делал Steam вчера вечером?
Какие программы изменяли Downloads?
Что происходило перед вылетом игры?
```

AI отвечает на основе реальных логов EventTracker.

---

# Tray-режим

EventTracker поддерживает tray-иконку.

Приложение может:

* работать в фоне,
* сворачиваться в системный трей,
* быстро открываться через tray menu.

---

# Установка

После сборки проекта необходимо:

1. Открыть папку:

```text
AppData/Roaming/
```

2. Создать папку:

```text
EventTracker
```

3. Переместить все файлы приложения в:

```text
AppData/Roaming/EventTracker/
```

---

После этого:

4. Запустить:

```text
build.bat
```

После завершения сборки `.exe` файл появится в папке:

```text
publish/
```

---

5. Нажать ПКМ по `.exe` файлу и выбрать:

```text
Создать ярлык
```

6. Переместить ярлык:

* Desktop
* Taskbar
* Start Menu

После этого приложение можно запускать как обычную Windows utility.

---

# Настройка AI

Для работы AI-функций требуется API Key OpenAI.

## Как получить API Key

1. Перейти на:
   https://platform.openai.com

2. Войти или создать аккаунт.

3. Открыть:
   https://platform.openai.com/api-keys

4. Нажать:

```text
Create new secret key
```

5. Скопировать API Key.

---

## Как подключить API Key

В приложении:

```text
Settings → AI Settings
```

Вставьте OpenAI API Key и сохраните настройки.

После этого AI-функции будут доступны внутри EventTracker.

---

# Технологии

## Backend

* C#
* .NET
* SQLite

## UI

* WPF
* MVVM Architecture

## Monitoring

* FileSystemWatcher
* WMI
* Process API

## AI

* OpenAI API

---

# База данных

Используется SQLite.

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

---

# Производительность

EventTracker разработан для:

* длительной стабильной работы,
* минимальной нагрузки на CPU,
* асинхронной записи событий,
* обработки большого количества изменений,
* background monitoring,
* live updates.

---

# Интерфейс

Особенности UI:

* dark mode,
* современный timeline,
* минималистичный дизайн,
* быстрый поиск,
* live updates,
* tree navigation,
* tray integration.

---

# Цель проекта

EventTracker создаётся как удобный и современный аналог:

* Process Monitor,
* GlassWire,
* forensic timeline tools.

Главная цель проекта — дать пользователю полный контроль и понятную историю изменений системы.

---

# Лицензия

MIT License
