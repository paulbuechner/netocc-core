// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

//
using OCC.Core;

namespace NetOcc.Viewer.Wpf;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    // a STEP file given on the command line, or --sample, opens right away
    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        switch (Environment.GetCommandLineArgs())
        {
            case [_, "--sample", ..]:
                await OpenSampleAsync();
                break;
            case [_, var path, ..]:
                await OpenAsync(path);
                break;
        }
    }

    private void Window_Closed(object sender, EventArgs e) => Host.Dispose();

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Open STEP", Filter = "STEP files (*.step;*.stp)|*.step;*.stp|All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) == true)
        {
            await OpenAsync(dialog.FileName);
        }
    }

    private async void OpenSample_Click(object sender, RoutedEventArgs e) => await OpenSampleAsync();

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void FitAll_Click(object sender, RoutedEventArgs e) => Host.Viewer.FitAll();

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
        Status.Text = $"Reading {name}...";
        Cursor = Cursors.Wait;
        try
        {
            var document = await Task.Run(() => StepDocument.Read(path));
            Host.Viewer.Show(document);
            Status.Text = $"{name}: {document.Roots.Count} shape(s). Left button: rotate, middle: pan, right or wheel: zoom.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OcctException)
        {
            Status.Text = $"{name}: {exception.Message}";
        }
        finally
        {
            Cursor = null;
        }
    }
}
