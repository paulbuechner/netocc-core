// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Threading.Tasks;

// Avalonia
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

//
using OCC.Core;

namespace NetOcc.Viewer.Avalonia;

public partial class MainWindow : Window
{
    private string? _startup;

    // Avalonia's XAML loader and previewer need a parameterless constructor
    public MainWindow() : this(null)
    {
    }

    /// <param name="startup">A STEP file to open once there's a view, or --sample.</param>
    public MainWindow(string? startup)
    {
        _startup = startup;
        InitializeComponent();
        Host.ViewerCreated += Host_ViewerCreated;
        if (!OperatingSystem.IsWindows())
        {
            Status.Text = "This demo hosts the OCCT view as a Win32 window: it runs on Windows only.";
        }
    }

    // the command line's file, once
    private async void Host_ViewerCreated(object? sender, EventArgs e)
    {
        var startup = _startup;
        _startup = null;
        switch (startup)
        {
            case "--sample":
                await OpenSampleAsync();
                break;
            case not null:
                await OpenAsync(startup);
                break;
        }
    }

    private async void Open_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open STEP",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("STEP files") { Patterns = ["*.step", "*.stp"] }, FilePickerFileTypes.All],
        });
        if (files is [var file] && file.TryGetLocalPath() is { } path)
        {
            await OpenAsync(path);
        }
    }

    private async void OpenSample_Click(object? sender, RoutedEventArgs e) => await OpenSampleAsync();

    private void Exit_Click(object? sender, RoutedEventArgs e) => Close();

    private void FitAll_Click(object? sender, RoutedEventArgs e) => Host.Viewer?.FitAll();

    // writes the sample assembly as STEP, then reads it like any other file
    private async Task OpenSampleAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), "netocc-sample.step");
        try
        {
            await Task.Run(() => SampleModel.Write(path));
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or OcctException)
        {
            Status.Text = exception.Message;
            return;
        }

        await OpenAsync(path);
    }

    // reads off the UI thread, shows on it
    private async Task OpenAsync(string path)
    {
        var name = Path.GetFileName(path);
        if (Host.Viewer is not { } viewer)
        {
            Status.Text = $"{name}: there's no view to show it in.";
            return;
        }

        Status.Text = $"Reading {name}...";
        try
        {
            var document = await Task.Run(() => StepDocument.Read(path));
            viewer.Show(document);
            Status.Text = $"{name}: {document.Roots.Count} shape(s). Left button: rotate, middle: pan, right or wheel: zoom.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OcctException)
        {
            Status.Text = $"{name}: {exception.Message}";
        }
    }
}
