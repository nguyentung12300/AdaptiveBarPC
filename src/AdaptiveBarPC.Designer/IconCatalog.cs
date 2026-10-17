using System;
using System.Collections.Generic;
using System.Linq;
using AdaptiveBarPC.ViewModels;

namespace AdaptiveBarPC.Designer;

public sealed record IconCatalogItem(
    string Name,
    string GlyphValue,
    string Category,
    string Keywords = "")
{
    public string DisplayGlyph => GlyphHelper.Resolve(GlyphValue);
}

public sealed record IconPickerTile(
    IconCatalogItem Item,
    bool IsSelected)
{
    public string Name => Item.Name;
    public string GlyphValue => Item.GlyphValue;
    public string Category => Item.Category;
    public string DisplayGlyph => Item.DisplayGlyph;
}

public sealed class IconCategoryGroup
{
    public string Category { get; }
    public IReadOnlyList<IconPickerTile> Items { get; }

    public IconCategoryGroup(string category, IEnumerable<IconPickerTile> items)
    {
        Category = category;
        Items = items.ToList();
    }
}

public static class IconCatalog
{
    private static readonly string[] CategoryDisplayOrder =
    [
        "Common",
        "Navigation",
        "Media",
        "Audio",
        "System",
        "Hardware",
        "Communication",
        "Actions"
    ];

    public static IReadOnlyList<IconCatalogItem> All { get; } = new List<IconCatalogItem>
    {
        // --- Common ---
        new("Home", "E80F", "Common", "home, house, main, start"),
        new("Settings", "E713", "Common", "settings, gear, preferences, options, config"),
        new("Search", "E721", "Common", "search, find, magnifying, lookup, query"),
        new("Refresh", "E72C", "Common", "refresh, reload, sync, update"),
        new("Add", "E710", "Common", "add, new, plus, create"),
        new("Edit", "E70F", "Common", "edit, pencil, modify, rename"),
        new("Save", "E74E", "Common", "save, disk, store"),
        new("Delete", "E74D", "Common", "delete, trash, remove, bin"),
        new("Cancel", "E711", "Common", "cancel, close, dismiss, x"),
        new("Checkmark", "E73E", "Common", "check, checkmark, done, ok, select"),
        new("Folder", "E8B7", "Common", "folder, directory, browse, files"),
        new("Document", "E8A5", "Common", "document, file, text, page"),
        new("Star", "E734", "Common", "star, favorite, bookmark"),
        new("Info", "E946", "Common", "info, information, about, help"),
        new("More", "E712", "Common", "more, overflow, dots, menu"),
        new("Pin", "E718", "Common", "pin, keep, stick"),
        new("Link", "E71B", "Common", "link, url, web, hyperlink"),

        // --- Navigation ---
        new("Back", "E72B", "Navigation", "back, left, previous, arrow"),
        new("Forward", "E72A", "Navigation", "forward, right, next, arrow"),
        new("Up", "E74A", "Navigation", "up, top, arrow, ascend"),
        new("Down", "E74B", "Navigation", "down, bottom, arrow, descend"),
        new("Previous", "E892", "Navigation", "previous, track, rewind, skip"),
        new("Next", "E893", "Navigation", "next, track, forward, skip"),
        new("Open External", "E8A7", "Navigation", "open, external, window, popup, launch"),

        // --- Media ---
        new("Play", "E768", "Media", "play, start, resume"),
        new("Pause", "E769", "Media", "pause, hold, wait"),
        new("Stop", "E71A", "Media", "stop, end"),
        new("Previous Track", "E892", "Media", "previous, track, rewind, skip"),
        new("Next Track", "E893", "Media", "next, track, fast forward, skip"),
        new("Fast Forward", "EB9D", "Media", "fast forward, seek, speed"),
        new("Rewind", "EB9E", "Media", "rewind, seek, speed"),
        new("Music", "E8D6", "Media", "music, song, audio, track, note"),
        new("Video", "E714", "Media", "video, movie, film, player"),
        new("Fullscreen", "E740", "Media", "fullscreen, expand, max"),
        new("Repeat", "E8EE", "Media", "repeat, loop, cycle"),
        new("Shuffle", "E8B1", "Media", "shuffle, random, mix"),

        // --- Audio ---
        new("Volume High", "E767", "Audio", "volume, sound, speaker, audio, loud, high"),
        new("Volume Medium", "E994", "Audio", "volume, sound, speaker, audio, medium"),
        new("Volume Low", "E993", "Audio", "volume, sound, speaker, audio, quiet, low"),
        new("Volume Mute", "E74F", "Audio", "volume, mute, silent, quiet, sound, off"),
        new("Microphone", "E720", "Audio", "microphone, mic, voice, recording, input"),
        new("Headphone", "E7F6", "Audio", "headphone, headset, earphones, listen"),
        new("Speaker", "E7F5", "Audio", "speaker, sound, output, audio"),

        // --- System ---
        new("Power", "E7E8", "System", "power, shutdown, turn off"),
        new("Restart", "E895", "System", "restart, reboot, sync"),
        new("Lock", "E72E", "System", "lock, secure, password"),
        new("Unlock", "E785", "System", "unlock, open, accessible"),
        new("Calculator", "E1D0", "System", "calculator, calc, math, numbers"),
        new("Terminal", "E756", "System", "terminal, console, command, cmd, powershell, bash"),
        new("Task Manager", "E9F9", "System", "task manager, performance, processes, system"),
        new("Clock", "E823", "System", "clock, time, alarm, timer"),
        new("Calendar", "E787", "System", "calendar, date, day, month"),

        // --- Hardware ---
        new("Display", "E7F4", "Hardware", "display, monitor, screen"),
        new("Battery", "E83F", "Hardware", "battery, power, charge, level"),
        new("Battery Charging", "EA93", "Hardware", "battery, charging, plug, power, ac"),
        new("Brightness", "E706", "Hardware", "brightness, screen, light, display, sun"),
        new("Keyboard", "E765", "Hardware", "keyboard, type, input, keys"),
        new("Mouse", "E962", "Hardware", "mouse, pointer, cursor, click"),
        new("CPU", "E950", "Hardware", "cpu, processor, chip, core"),
        new("RAM", "E964", "Hardware", "ram, memory, memory usage"),
        new("Hard Drive", "EDA2", "Hardware", "hard drive, disk, storage, ssd, hdd"),
        new("Fan", "E9D9", "Hardware", "fan, cooling, rpm, temp, temperature"),
        new("Game Controller", "E7FC", "Hardware", "game, controller, gamepad, joystick, gaming"),

        // --- Communication ---
        new("Mail", "E715", "Communication", "mail, email, envelope, inbox"),
        new("Chat", "E8BD", "Communication", "chat, message, conversation, talk"),
        new("Phone", "E717", "Communication", "phone, call, telephone"),
        new("Web Browser", "E774", "Communication", "web, browser, internet, globe, world"),
        new("WiFi", "E701", "Communication", "wifi, wireless, network, internet, connection"),
        new("Bluetooth", "E702", "Communication", "bluetooth, bt, wireless, connect"),
        new("Ethernet", "E839", "Communication", "ethernet, network, lan, cable, connection"),
        new("Share", "E72D", "Communication", "share, send, export"),

        // --- Actions ---
        new("Copy", "E8C8", "Actions", "copy, duplicate, clipboard"),
        new("Cut", "E8C6", "Actions", "cut, clipboard, scissors"),
        new("Paste", "E77F", "Actions", "paste, clipboard"),
        new("Undo", "E7A7", "Actions", "undo, revert, back"),
        new("Redo", "E7A6", "Actions", "redo, repeat, forward"),
        new("Zoom In", "E8A3", "Actions", "zoom in, enlarge, magnify"),
        new("Zoom Out", "E8A4", "Actions", "zoom out, shrink, reduce"),
        new("Camera", "E722", "Actions", "camera, photo, picture, screenshot"),
        new("Print", "E749", "Actions", "print, printer, paper")
    };

    public static IReadOnlyList<IconCatalogItem> Search(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return All;

        var terms = query.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return All.Where(item =>
        {
            foreach (var term in terms)
            {
                var matches = item.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                              item.Category.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                              item.Keywords.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                              item.GlyphValue.Contains(term, StringComparison.OrdinalIgnoreCase);
                if (!matches)
                    return false;
            }
            return true;
        }).ToList();
    }

    public static IconCatalogItem? FindByGlyph(string? glyphOrHex)
    {
        if (string.IsNullOrWhiteSpace(glyphOrHex))
            return null;

        var clean = CleanHex(glyphOrHex);
        if (!string.IsNullOrEmpty(clean))
        {
            foreach (var item in All)
            {
                if (string.Equals(item.GlyphValue, clean, StringComparison.OrdinalIgnoreCase))
                    return item;
            }
        }

        var resolved = GlyphHelper.Resolve(glyphOrHex);
        if (!string.IsNullOrWhiteSpace(resolved))
        {
            foreach (var item in All)
            {
                if (string.Equals(item.DisplayGlyph, resolved, StringComparison.Ordinal))
                    return item;
            }
        }

        return null;
    }

    public static IReadOnlyList<IconCategoryGroup> GroupByCategory(
        IEnumerable<IconCatalogItem> items,
        string? selectedGlyphOrHex = null)
    {
        var selectedItem = FindByGlyph(selectedGlyphOrHex);
        var cleanSelected = selectedItem?.GlyphValue ?? CleanHex(selectedGlyphOrHex ?? "");

        return items
            .GroupBy(x => x.Category)
            .OrderBy(g =>
            {
                var idx = Array.IndexOf(CategoryDisplayOrder, g.Key);
                return idx >= 0 ? idx : int.MaxValue;
            })
            .Select(g => new IconCategoryGroup(
                g.Key,
                g.Select(item => new IconPickerTile(
                    item,
                    !string.IsNullOrEmpty(cleanSelected) &&
                    string.Equals(item.GlyphValue, cleanSelected, StringComparison.OrdinalIgnoreCase)))))
            .ToList();
    }

    private static string CleanHex(string input)
    {
        var trimmed = input.Trim();
        if (trimmed.StartsWith("&#x", StringComparison.OrdinalIgnoreCase) && trimmed.EndsWith(";"))
            return trimmed.Substring(3, trimmed.Length - 4).Trim();
        if (trimmed.StartsWith(@"\u", StringComparison.OrdinalIgnoreCase) && trimmed.Length == 6)
            return trimmed.Substring(2).Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return trimmed.Substring(2).Trim();
        if ((trimmed.Length == 4 || trimmed.Length == 5) &&
            int.TryParse(trimmed, System.Globalization.NumberStyles.HexNumber, null, out _))
            return trimmed;

        return "";
    }
}
