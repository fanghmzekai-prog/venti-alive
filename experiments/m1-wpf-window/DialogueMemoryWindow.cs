using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.Win32;
using Venti.LocalDialogue;

namespace Aeris.WindowProbe;

internal sealed class DialogueMemoryWindow : Window
{
    private readonly MemoryStore memory;
    private readonly ListBox entries = new() { DisplayMemberPath = nameof(MemoryEntry.Text), MinHeight = 110 };
    private readonly TextBox edit = new() { MaxLength = 200, MinHeight = 58, Padding = new Thickness(7),
        TextWrapping = TextWrapping.Wrap, AcceptsReturn = true };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, MinHeight = 20 };
    private readonly Button save;
    private readonly Button delete;

    internal DialogueMemoryWindow(MemoryStore memory)
    {
        this.memory = memory;
        Title = "温迪 · 记忆";
        Width = 460;
        Height = 373;
        MinWidth = 435;
        MinHeight = 340;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AutomationProperties.SetName(edit, "记忆内容");
        AutomationProperties.SetAutomationId(edit, "MemoryText");
        AutomationProperties.SetAutomationId(entries, "MemoryEntries");

        var body = new DockPanel { Margin = new Thickness(15) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 9, 0, 6) };
        actions.Children.Add(MakeButton("添加", "MemoryAdd", () => Change(() => memory.Add(edit.Text))));
        save = MakeButton("保存修改", "MemorySave", () =>
        {
            if (entries.SelectedItem is MemoryEntry entry) Change(() => memory.Update(entry.Id, edit.Text));
        });
        delete = MakeButton("删除", "MemoryDelete", () =>
        {
            if (entries.SelectedItem is MemoryEntry entry) Change(() => memory.Delete(entry.Id));
        });
        actions.Children.Add(save);
        actions.Children.Add(delete);
        actions.Children.Add(MakeButton("导出 JSON", "MemoryExport", Export));
        var bottom = new StackPanel();
        bottom.Children.Add(edit);
        bottom.Children.Add(actions);
        bottom.Children.Add(status);
        DockPanel.SetDock(bottom, Dock.Bottom);
        body.Children.Add(bottom);
        body.Children.Add(entries);
        Content = body;
        entries.SelectionChanged += (_, _) =>
        {
            var selected = entries.SelectedItem as MemoryEntry;
            edit.Text = selected?.Text ?? string.Empty;
            save.IsEnabled = delete.IsEnabled = selected is not null;
        };
        save.IsEnabled = delete.IsEnabled = false;
        TryOperation(RefreshEntries);
    }

    internal bool Changed { get; private set; }

    private static Button MakeButton(string label, string id, Action action)
    {
        var button = new Button { Content = label, Padding = new Thickness(9, 5, 9, 5), Margin = new Thickness(0, 0, 6, 0) };
        AutomationProperties.SetAutomationId(button, id);
        button.Click += (_, _) => action();
        return button;
    }

    private void RefreshEntries()
    {
        entries.ItemsSource = memory.Read();
        status.Text = $"已保存 {entries.Items.Count}/{MemoryStore.MaxEntries} 条。";
    }

    private void Change(Action action)
    {
        TryOperation(() =>
        {
            action();
            Changed = true;
            entries.SelectedItem = null;
            edit.Clear();
            RefreshEntries();
        });
    }

    private void Export()
    {
        var picker = new SaveFileDialog
        {
            Title = "导出温迪的记忆",
            FileName = "温迪记忆",
            Filter = "JSON 文件 (*.json)|*.json",
            DefaultExt = ".json",
            OverwritePrompt = false
        };
        if (picker.ShowDialog(this) == true)
        {
            TryOperation(() =>
            {
                if (File.Exists(picker.FileName)) throw new IOException("该文件已存在，请选择新名称。");
                memory.Export(picker.FileName);
                status.Text = "记忆已导出。";
            });
        }
    }

    private void TryOperation(Action action)
    {
        try
        {
            action();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or
            ArgumentException or InvalidOperationException or JsonException)
        {
            status.Text = error.Message;
        }
    }
}
