using BaseLogApp.Core.Diagnostics;
using BaseLogApp.Core.Models;
using BaseLogApp.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.IO;

namespace BaseLogApp.Views;

public partial class JumpsPage : ContentPage
{
    private readonly JumpsViewModel _vm;
    private readonly IServiceProvider _services;
    private readonly ToolbarItem _dbSwitchItem;
    private bool _coherenceChecked;

    private string GetLogPath() => _vm.GetCurrentDbPath() + ".log";

    public JumpsPage(JumpsViewModel vm, IServiceProvider services)
    {
        InitializeComponent();

        _vm = vm;
        _services = services;
        BindingContext = _vm;

        _dbSwitchItem = new ToolbarItem
        {
            Text = _vm.CurrentProfileLabel,
            Priority = 0,
            Order = ToolbarItemOrder.Primary,
            Command = new Command(async () => await OnSwitchDbClicked())
        };

        ToolbarItems.Add(_dbSwitchItem);
        ToolbarItems.Add(new ToolbarItem
        {
            Text = "+",
            Priority = 1,
            Order = ToolbarItemOrder.Primary,
            Command = new Command(async () => await OnAddMenuClicked())
        });
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
        await EnsureCoherenceCheckedAsync();

        _dbSwitchItem.Text = _vm.CurrentProfileLabel;
    }

    private async Task OnSwitchDbClicked()
    {
        var selectedPath = await PickAndCopyDatabaseAsync();
        if (string.IsNullOrWhiteSpace(selectedPath))
            return;

        var switched = await _vm.SetCustomDbPathAsync(selectedPath);
        if (!switched)
        {
            await DisplayAlert("DB", "Impossibile impostare il database selezionato.", "OK");
            return;
        }

        _coherenceChecked = false;
        await EnsureCoherenceCheckedAsync();
        _dbSwitchItem.Text = _vm.CurrentProfileLabel;
        await DisplayAlert("DB attivo", _vm.GetCurrentDbPath(), "OK");
    }

    private async Task<string?> PickAndCopyDatabaseAsync()
    {
        try
        {
            var picked = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Seleziona file database sqlite"
            });

            if (picked is null)
                return null;

            var fileName = string.IsNullOrWhiteSpace(picked.FileName)
                ? "BASELogbook.selected.sqlite"
                : picked.FileName;

            var targetDir = Path.Combine(FileSystem.AppDataDirectory, "ExternalDb");
            Directory.CreateDirectory(targetDir);
            var targetPath = Path.Combine(targetDir, Path.GetFileName(fileName));

            await using var source = await picked.OpenReadAsync();
            await using var destination = File.Create(targetPath);
            await source.CopyToAsync(destination);

            return targetPath;
        }
        catch (Exception ex)
        {
            AppLog.Error(GetLogPath(), LogCategories.RuntimeError, nameof(JumpsPage), nameof(PickAndCopyDatabaseAsync), "Select custom DB failed.", ex: ex);
            await DisplayAlert("DB", $"Errore selezione DB: {ex.Message}", "OK");
            return null;
        }
    }

    private async Task EnsureCoherenceCheckedAsync()
    {
        if (_coherenceChecked)
            return;

        _coherenceChecked = true;
        var normalized = await _vm.NormalizeJumpNumbersAsync();
        if (normalized <= 0)
            return;

        AppLog.Warn(
            GetLogPath(),
            LogCategories.NumberShift,
            nameof(JumpsPage),
            nameof(OnAppearing),
            "Jump-number normalization applied on startup.",
            details: $"normalized={normalized};dbPath={_vm.GetCurrentDbPath()}");

        await DisplayAlert("Coerenza logbook", $"Trovate incongruenze nella numerazione salti. Normalizzati {normalized} record in base al numero salto. Vedi Diagnostic per il dettaglio.", "OK");
        await _vm.LoadAsync();
    }

    private async Task OnAddMenuClicked()
    {
        var action = await DisplayActionSheet(
            "Aggiungi salto",
            "Annulla",
            null,
            "Add jump",
            "Copy last");

        if (string.IsNullOrWhiteSpace(action) || action == "Annulla")
            return;

        if (action == "Copy last")
        {
            var latest = _vm.Items
                .OrderByDescending(x => x.NumeroSalto)
                .ThenByDescending(x => x.Id)
                .FirstOrDefault();

            await OpenNewJumpPage(template: latest);
            return;
        }

        await OpenNewJumpPage();
    }

    private async Task OpenNewJumpPage(JumpListItem? edit = null, JumpListItem? template = null)
    {
        var knownObjects = await _vm.GetObjectNamesAsync();
        var knownJumpTypes = await _vm.GetJumpTypeNamesAsync();
        var suggested = edit?.NumeroSalto ?? _vm.NextJumpNumber;
        var page = new NewJumpPage(_vm, suggested, knownObjects, knownJumpTypes, edit, template);
        page.SaveRequested = SaveJumpFromEditorAsync;
        await Navigation.PushModalAsync(new NavigationPage(page));
    }

    private async Task<bool> SaveJumpFromEditorAsync(JumpListItem e)
    {
        var result = await _vm.SaveJumpWithValidationAsync(e, allowShiftOnConflict: false);
        if (result.RequiresConfirmation)
        {
            var confirmShift = await DisplayAlert("Numero esistente", result.UserMessage, "Si", "No");
            if (!confirmShift)
                return false;

            result = await _vm.SaveJumpWithValidationAsync(e, allowShiftOnConflict: true);
        }

        if (!result.Success)
        {
            await DisplayAlert("DB", result.UserMessage, "OK");
            return false;
        }

        if (!string.IsNullOrWhiteSpace(result.Notice))
            await DisplayAlert("Numero salto", result.Notice, "OK");

        return true;
    }

    private async void OnEditJumpInvoked(object? sender, EventArgs e)
    {
        if (sender is SwipeItem { CommandParameter: JumpListItem item })
            await OpenNewJumpPage(item);
    }

    private async void OnDeleteJumpInvoked(object? sender, EventArgs e)
    {
        if (sender is not SwipeItem { CommandParameter: JumpListItem item })
            return;

        var confirm = await DisplayAlert("Conferma", $"Eliminare il salto #{item.NumeroSalto}?", "Si", "No");
        if (!confirm)
            return;

        var deleted = await _vm.DeleteJumpAsync(item);
        if (!deleted)
            await DisplayAlert("DB", "Impossibile eliminare il salto.", "OK");
    }

    private async void OnLockEditTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is JumpListItem item)
            await OpenNewJumpPage(item);
    }

    private void OnCardHeaderTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not JumpListItem item)
            return;

        _vm.SetExpandedState(item, !item.IsExpanded);
    }

    private async void OnPhotoTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not JumpListItem item)
            return;

        var page = ActivatorUtilities.CreateInstance<PhotoViewerPage>(_services, item);
        await Navigation.PushModalAsync(new NavigationPage(page));
    }
}

