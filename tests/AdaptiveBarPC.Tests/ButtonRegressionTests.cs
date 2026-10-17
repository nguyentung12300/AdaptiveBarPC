using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using AdaptiveBarPC.Controls;
using AdaptiveBarPC.Models;
using AdaptiveBarPC.Services;
using AdaptiveBarPC.ViewModels;
using Xunit;

namespace AdaptiveBarPC.Tests;

public class ButtonRegressionTests
{
    [Fact]
    public void LongPressButton_AutomationInvoke_ExecutesOnceWithoutFocus_AndRejectsDisabled()
    {
        RunInSta(() =>
        {
            var count = 0;
            var button = new LongPressButton { Command = new DelegateCommand(() => count++) };
            var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(button);
            var invoke = Assert.IsAssignableFrom<System.Windows.Automation.Provider.IInvokeProvider>(
                peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke));

            invoke.Invoke();
            Pump(30);
            Assert.Equal(1, count);
            Assert.False(button.IsKeyboardFocused);
            Assert.False(button.Focusable);

            button.IsEnabled = false;
            Assert.Throws<System.Windows.Automation.ElementNotEnabledException>(() => invoke.Invoke());
            Pump(30);
            Assert.Equal(1, count);
        });
    }

    [Fact]
    public void LongPressButton_AllowsParentSwipeManipulation()
    {
        RunInSta(() => Assert.True(new LongPressButton().IsManipulationEnabled));
    }

    [Fact]
    public void ButtonItemViewModel_Command_Invokes_Action()
    {
        var actionDef = new ActionDefinition { Type = "launch", Value = "explorer.exe" };
        var executed = false;
        ActionDefinition? receivedAction = null;

        var vm = new ButtonItemViewModel
        {
            Label = "Explorer",
            Width = 96,
            Command = new DelegateCommand(() =>
            {
                executed = true;
                receivedAction = actionDef;
            })
        };

        Assert.True(vm.Command.CanExecute(null));
        vm.Command.Execute(null);

        Assert.True(executed);
        Assert.NotNull(receivedAction);
        Assert.Equal("launch", receivedAction.Type);
        Assert.Equal("explorer.exe", receivedAction.Value);
    }

    [Fact]
    public void LongPressButton_PrimaryCommand_ExecutesOnce_OnTap()
    {
        RunInSta(() =>
        {
            int primaryCount = 0;
            int secondaryCount = 0;

            var btn = new LongPressButton
            {
                LongPressMilliseconds = 300,
                Command = new DelegateCommand(() => primaryCount++),
                SecondaryCommand = new DelegateCommand(() => secondaryCount++)
            };

            var win = new Window { Content = btn, Width = 100, Height = 100 };
            win.Show();
            Pump(30);

            var dev = new TestTouchDevice(1);

            // Simulate touch tap
            RaiseTouchDown(btn, dev);
            Pump(30);
            RaiseTouchUp(btn, dev);

            // Simulate WPF promoting touch to synthetic mouse events immediately
            RaisePreviewMouseDown(btn);
            RaisePreviewMouseUp(btn);
            InvokeOnClick(btn);
            Pump(30);

            Assert.Equal(1, primaryCount);
            Assert.Equal(0, secondaryCount);

            win.Close();
        });
    }

    [Fact]
    public void PageNavigationButton_TouchTap_ExecutesNavigationCommand_WithoutDoubleFire()
    {
        RunInSta(() =>
        {
            int pageIndex = 0;
            var nextPageCommand = new DelegateCommand(() => pageIndex++);
            var prevPageCommand = new DelegateCommand(() => pageIndex--);

            var nextBtn = new LongPressButton
            {
                Content = "\u203A",
                Width = 26,
                Height = 40,
                Focusable = false,
                IsTabStop = false,
                Command = nextPageCommand
            };

            var prevBtn = new LongPressButton
            {
                Content = "\u2039",
                Width = 26,
                Height = 40,
                Focusable = false,
                IsTabStop = false,
                Command = prevPageCommand
            };

            Assert.False(nextBtn.Focusable);
            Assert.False(nextBtn.IsTabStop);
            Assert.False(prevBtn.Focusable);
            Assert.False(prevBtn.IsTabStop);

            var panel = new System.Windows.Controls.StackPanel();
            panel.Children.Add(prevBtn);
            panel.Children.Add(nextBtn);

            var win = new Window { Content = panel, Width = 150, Height = 100 };
            win.Show();
            Pump(30);

            var dev = new TestTouchDevice(10);

            // 1. Touch tap Next button: 0 -> 1
            RaiseTouchDown(nextBtn, dev);
            Pump(20);
            RaiseTouchUp(nextBtn, dev);

            // Promoted mouse event is suppressed/ignored
            RaisePreviewMouseDown(nextBtn);
            RaisePreviewMouseUp(nextBtn);
            InvokeOnClick(nextBtn);
            Pump(20);

            Assert.Equal(1, pageIndex);

            // 2. Touch tap Prev button: 1 -> 0
            RaiseTouchDown(prevBtn, dev);
            Pump(20);
            RaiseTouchUp(prevBtn, dev);

            RaisePreviewMouseDown(prevBtn);
            RaisePreviewMouseUp(prevBtn);
            InvokeOnClick(prevBtn);
            Pump(20);

            Assert.Equal(0, pageIndex);

            win.Close();
        });
    }

    [Fact]
    public void LongPressButton_LongPress_ExecutesSecondary_And_SuppressesPrimary()
    {
        RunInSta(() =>
        {
            int primaryCount = 0;
            int secondaryCount = 0;

            var btn = new LongPressButton
            {
                LongPressMilliseconds = 250,
                Command = new DelegateCommand(() => primaryCount++),
                SecondaryCommand = new DelegateCommand(() => secondaryCount++)
            };

            var win = new Window { Content = btn, Width = 100, Height = 100 };
            win.Show();
            Pump(30);

            var dev = new TestTouchDevice(2);

            // Simulate touch hold >= threshold
            RaiseTouchDown(btn, dev);
            Pump(350); // wait for timer to tick

            // Release
            RaiseTouchUp(btn, dev);

            // Synthetic promoted mouse events
            RaisePreviewMouseDown(btn);
            RaisePreviewMouseUp(btn);
            InvokeOnClick(btn);
            Pump(30);

            Assert.Equal(0, primaryCount);
            Assert.Equal(1, secondaryCount);

            win.Close();
        });
    }

    [Fact]
    public void LongPressButton_FreshMouseClick_ExecutesPrimary()
    {
        RunInSta(() =>
        {
            int primaryCount = 0;
            var btn = new LongPressButton
            {
                Command = new DelegateCommand(() => primaryCount++)
            };

            var win = new Window { Content = btn, Width = 100, Height = 100 };
            win.Show();
            Pump(30);

            SetMouseOver(btn, true);
            RaisePreviewMouseDown(btn);
            RaisePreviewMouseUp(btn);
            Pump(20);

            Assert.Equal(1, primaryCount);

            win.Close();
        });
    }

    [Fact]
    public void LongPressButton_RepeatedClicks_And_Taps_Work()
    {
        RunInSta(() =>
        {
            int primaryCount = 0;
            var btn = new LongPressButton
            {
                LongPressMilliseconds = 250,
                Command = new DelegateCommand(() => primaryCount++)
            };

            var win = new Window { Content = btn, Width = 100, Height = 100 };
            win.Show();
            Pump(30);

            var dev = new TestTouchDevice(3);

            // 1. Touch tap
            RaiseTouchDown(btn, dev);
            Pump(20);
            RaiseTouchUp(btn, dev);
            Pump(20);
            Assert.Equal(1, primaryCount);

            // 2. Mouse click after touch tap
            Pump(300); // simulate delay between touch and mouse (>250ms)
            SetMouseOver(btn, true);
            RaisePreviewMouseDown(btn);
            RaisePreviewMouseUp(btn);
            InvokeOnClick(btn);
            Pump(20);
            Assert.Equal(2, primaryCount);
            SetMouseOver(btn, false);

            // 3. Another touch tap
            RaiseTouchDown(btn, dev);
            Pump(20);
            RaiseTouchUp(btn, dev);
            Pump(20);
            Assert.Equal(3, primaryCount);

            win.Close();
        });
    }

    [Fact]
    public void ProfileRebuild_PreservesCommandWiring_And_DisposesOldItems()
    {
        RunInSta(() =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "AdaptiveBarPC_Test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var profileJson = @"{
                  ""id"": ""test"",
                  ""displayName"": ""Test Profile"",
                  ""priority"": 0,
                  ""isDefault"": true,
                  ""pages"": [
                    {
                      ""id"": ""page1"",
                      ""displayName"": ""Page 1"",
                      ""items"": [
                        { ""type"": ""button"", ""label"": ""Btn1"", ""width"": 90, ""action"": { ""type"": ""launch"", ""value"": ""calc.exe"" } },
                        { ""type"": ""button"", ""label"": ""Btn2"", ""width"": 90, ""action"": { ""type"": ""launch"", ""value"": ""notepad.exe"" } }
                      ]
                    },
                    {
                      ""id"": ""page2"",
                      ""displayName"": ""Page 2"",
                      ""items"": [
                        { ""type"": ""button"", ""label"": ""Btn3"", ""width"": 90, ""action"": { ""type"": ""media"", ""value"": ""mute"" } }
                      ]
                    }
                  ]
                }";
                File.WriteAllText(Path.Combine(tempDir, "00-test.json"), profileJson);

                var logger = new AppLogger(tempDir);
                var config = new AppConfig();
                var foreground = new ForegroundAppService();
                var profiles = new ProfileService(tempDir, logger);
                var audio = new WindowsAudioProvider();
                var actions = new ActionExecutor(config, logger);
                var mediaService = new MediaSessionService(logger);

                var vm = new MainViewModel(config, foreground, profiles, audio, actions, mediaService, logger);
                vm.ApplyForegroundApp(new ForegroundAppInfo(IntPtr.Zero, "", "", null, "Desktop", null, "Progman"));

                // Initial page has 2 buttons
                Assert.Equal(2, vm.Items.Count);
                var btn1 = vm.Items[0] as ButtonItemViewModel;
                Assert.NotNull(btn1);
                Assert.Equal("Btn1", btn1.Label);
                Assert.NotNull(btn1.Command);

                // Switch to page 2
                vm.NextPage();
                Assert.Single(vm.Items);
                var btn3 = vm.Items[0] as ButtonItemViewModel;
                Assert.NotNull(btn3);
                Assert.Equal("Btn3", btn3.Label);
                Assert.NotNull(btn3.Command);

                // Switch back to page 1
                vm.PreviousPage();
                Assert.Equal(2, vm.Items.Count);
                var btn1Rebuilt = vm.Items[0] as ButtonItemViewModel;
                Assert.NotNull(btn1Rebuilt);
                Assert.Equal("Btn1", btn1Rebuilt.Label);
                Assert.NotNull(btn1Rebuilt.Command);

                vm.Dispose();
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        });
    }

    // ------------------------------------------------------------------ //
    //  Helpers
    // ------------------------------------------------------------------ //


    private static void RaiseTouchDown(UIElement elem, TouchDevice dev)
    {
        var e = new TouchEventArgs(dev, 0) { RoutedEvent = UIElement.TouchDownEvent, Source = elem };
        elem.RaiseEvent(e);
    }

    private static void RaiseTouchUp(UIElement elem, TouchDevice dev)
    {
        var e = new TouchEventArgs(dev, 0) { RoutedEvent = UIElement.TouchUpEvent, Source = elem };
        elem.RaiseEvent(e);
    }

    private static void RaisePreviewMouseDown(UIElement elem)
    {
        var e1 = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent, Source = elem };
        elem.RaiseEvent(e1);
        var e2 = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent, Source = elem };
        elem.RaiseEvent(e2);
    }

    private static void RaisePreviewMouseUp(UIElement elem)
    {
        var e1 = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseUpEvent, Source = elem };
        elem.RaiseEvent(e1);
        var e2 = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent, Source = elem };
        elem.RaiseEvent(e2);
    }

    private static void InvokeOnClick(UIElement elem)
    {
        var m = typeof(System.Windows.Controls.Primitives.ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic);
        m?.Invoke(elem, null);
    }

    private static void SetMouseOver(UIElement elem, bool value)
    {
        var changeMethod = typeof(MouseDevice).GetMethod("ChangeMouseOver", BindingFlags.Instance | BindingFlags.NonPublic);
        changeMethod?.Invoke(Mouse.PrimaryDevice, new object?[] { value ? elem : null, Environment.TickCount });
    }

    private static void Pump(int ms = 50)
    {
        var end = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < end)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(5);
        }
    }

    private class TestTouchDevice : TouchDevice
    {
        public TestTouchDevice(int id) : base(id) { }
        public override TouchPointCollection GetIntermediateTouchPoints(IInputElement relativeTo) => new();
        public override TouchPoint GetTouchPoint(IInputElement relativeTo) => new(this, new Point(0, 0), new Rect(0, 0, 0, 0), TouchAction.Down);
    }
}
