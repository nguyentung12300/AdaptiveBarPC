using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using AdaptiveBarPC.Models;
using AdaptiveBarPC.Services;
using Microsoft.Win32;

namespace AdaptiveBarPC.Designer;

public partial class MainWindow
{
    private void NewProfile_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Create a profile for an application", Filter = "Application (*.exe)|*.exe", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        var decision = _viewModel.IsDirty ? PromptDirty() : DirtyDecision.Discard;
        if (!_viewModel.CreateProfileForApplication(dialog.FileName, decision) && decision != DirtyDecision.Cancel)
            MessageBox.Show(this, _viewModel.StatusMessage, "Create profile", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void OpenLibrary_OnClick(object sender, RoutedEventArgs e) => _viewModel.SelectedLeftTabIndex = 1;

    private void DiagnosticGo_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ProfileDiagnostic diagnostic })
            _viewModel.SelectDiagnostic(diagnostic);
    }

    private void DiagnosticsList_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DiagnosticsList.SelectedItem is not ProfileDiagnostic diagnostic) return;
        _viewModel.SelectDiagnostic(diagnostic);
        e.Handled = true;
    }

    private void WidthStep_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is RepeatButton { DataContext: WidgetEditorViewModel editor, Tag: string direction })
            editor.StepWidth(direction == "1" ? 1 : -1);
    }

    private void ItemMenu_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom };
        void Add(string label, RoutedEventHandler handler, bool enabled = true)
        {
            var item = new MenuItem { Header = label, IsEnabled = enabled };
            item.Click += handler;
            menu.Items.Add(item);
        }
        void Command(string label, ICommand command)
        {
            var item = new MenuItem { Header = label, Command = command };
            menu.Items.Add(item);
        }
        switch (button.DataContext)
        {
            case ProfileEntry profile:
                if (!_viewModel.TrySwitchProfile(profile, ReferenceEquals(profile, _viewModel.SelectedProfile) || !_viewModel.IsDirty ? DirtyDecision.Discard : PromptDirty())) return;
                ProfilesList.SelectedItem = profile;
                Add("Assign application…", ChooseAppButton_OnClick);
                Add("Delete profile…", DeleteProfileButton_OnClick);
                break;
            case DesignerPage page:
                _viewModel.SelectedPage = page;
                Add("Rename page", (_, _) =>
                {
                    _viewModel.SelectedWidget = null;
                    _viewModel.SelectedRightTabIndex = 0;
                    UpdateLayout();
                    PageNameEditor.BringIntoView();
                    PageNameEditor.Focus();
                    PageNameEditor.SelectAll();
                });
                Command("Move page left", _viewModel.MovePageLeftCommand);
                Command("Move page right", _viewModel.MovePageRightCommand);
                Add("Delete page…", DeletePageButton_OnClick);
                break;
            case ControlDefinition widget:
                _viewModel.SelectedWidget = widget;
                Command("Move left", _viewModel.MoveSelectedWidgetLeftCommand);
                Command("Move right", _viewModel.MoveSelectedWidgetRightCommand);
                Command("Delete widget", _viewModel.DeleteSelectedWidgetCommand);
                break;
            default: return;
        }
        menu.IsOpen = true;
        e.Handled = true;
    }
}
