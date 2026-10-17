using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows.Threading;
using AdaptiveBarPC.Models;
using AdaptiveBarPC.Services;
using AdaptiveBarPC.ViewModels;
using Xunit;

namespace AdaptiveBarPC.Tests;

public class DynamicWidgetTests
{
    [Fact]
    public void BackwardCompatibleProfileLoading_ExistingAndNewProfiles_LoadCorrectly()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        // 1. Existing profiles deserialize cleanly
        var baseDir = AppContext.BaseDirectory;
        var profilesDir = Path.Combine(baseDir, "Profiles");
        if (Directory.Exists(profilesDir))
        {
            var files = Directory.GetFiles(profilesDir, "*.json");
            Assert.NotEmpty(files);
            foreach (var file in files)
            {
                var json = File.ReadAllText(file);
                var profile = JsonSerializer.Deserialize<ProfileDefinition>(json, options);
                Assert.NotNull(profile);
                Assert.False(string.IsNullOrWhiteSpace(profile.Id));
            }
        }

        // 2. Additive new widget types deserialize with full fidelity
        var dynamicJson = """
        {
          "id": "dynamic_test",
          "displayName": "Dynamic Test",
          "pages": [
            {
              "id": "page1",
              "items": [
                {
                  "type": "dynamiclist",
                  "id": "dyn_host",
                  "provider": "mockSessions",
                  "emptyBehavior": "collapse",
                  "maxItems": 5,
                  "width": 0
                },
                {
                  "type": "toggle",
                  "id": "mic_mute",
                  "label": "Mute",
                  "onLabel": "Muted",
                  "offLabel": "Live",
                  "isChecked": true,
                  "width": 80
                },
                {
                  "type": "multistate",
                  "id": "fan_profile",
                  "state": "quiet",
                  "states": [
                    { "id": "quiet", "label": "Quiet" },
                    { "id": "balanced", "label": "Balanced" },
                    { "id": "turbo", "label": "Turbo" }
                  ],
                  "width": 90
                },
                {
                  "type": "sensor",
                  "id": "cpu_sensor",
                  "label": "CPU",
                  "metric": "cpu_load",
                  "unit": "%",
                  "format": "{0:F0}%",
                  "warningThreshold": 80.0,
                  "criticalThreshold": 95.0,
                  "width": 84
                }
              ]
            }
          ]
        }
        """;

        var loaded = JsonSerializer.Deserialize<ProfileDefinition>(dynamicJson, options);
        Assert.NotNull(loaded);
        Assert.Single(loaded.Pages);

        var items = loaded.Pages[0].Items;
        Assert.Equal(4, items.Count);

        // DynamicList definition
        Assert.Equal("dynamiclist", items[0].Type);
        Assert.Equal("mockSessions", items[0].Provider);
        Assert.Equal("collapse", items[0].EmptyBehavior);
        Assert.Equal(5, items[0].MaxItems);

        // Toggle definition
        Assert.Equal("toggle", items[1].Type);
        Assert.Equal("Muted", items[1].OnLabel);
        Assert.True(items[1].IsChecked);

        // MultiState definition
        Assert.Equal("multistate", items[2].Type);
        Assert.Equal("quiet", items[2].State);
        Assert.Equal(3, items[2].States.Count);

        // Sensor definition
        Assert.Equal("sensor", items[3].Type);
        Assert.Equal("cpu_load", items[3].Metric);
        Assert.Equal(80.0, items[3].WarningThreshold);
    }

    [Fact]
    public void StableDynamicItemIdentity_ItemsRetainStableIdentityAndInstance()
    {
        var provider = new MockDynamicWidgetProvider();
        provider.SetItems(new[]
        {
            new DynamicItemSnapshot { Id = "audio:pid:1234", Label = "Spotify", Width = 84 }
        });

        using var vm = new DynamicListItemViewModel(provider);

        Assert.Single(vm.Items);
        var initialItem = vm.Items[0] as ButtonItemViewModel;
        Assert.NotNull(initialItem);
        Assert.Equal("audio:pid:1234", initialItem.Id);
        Assert.Equal("Spotify", initialItem.Label);

        // Emit update for same Id
        provider.SetItems(new[]
        {
            new DynamicItemSnapshot { Id = "audio:pid:1234", Label = "Spotify (Paused)", Width = 96 }
        });

        Assert.Single(vm.Items);
        var updatedItem = vm.Items[0] as ButtonItemViewModel;
        Assert.NotNull(updatedItem);

        // MUST be the exact same object instance
        Assert.Same(initialItem, updatedItem);
        Assert.Equal("audio:pid:1234", updatedItem.Id);
        Assert.Equal("Spotify (Paused)", updatedItem.Label);
        Assert.Equal(96, updatedItem.Width);
    }

    [Fact]
    public void DynamicList_Add_InsertsItemWithoutRecreatingExisting()
    {
        var provider = new MockDynamicWidgetProvider();
        provider.SetItems(new[]
        {
            new DynamicItemSnapshot { Id = "A", Label = "A" },
            new DynamicItemSnapshot { Id = "B", Label = "B" }
        });

        using var vm = new DynamicListItemViewModel(provider);
        Assert.Equal(2, vm.Items.Count);
        var refA = vm.Items[0];
        var refB = vm.Items[1];

        // Add C: A, B, C
        provider.SetItems(new[]
        {
            new DynamicItemSnapshot { Id = "A", Label = "A" },
            new DynamicItemSnapshot { Id = "B", Label = "B" },
            new DynamicItemSnapshot { Id = "C", Label = "C" }
        });

        Assert.Equal(3, vm.Items.Count);
        Assert.Same(refA, vm.Items[0]);
        Assert.Same(refB, vm.Items[1]);
        Assert.Equal("C", vm.Items[2].Id);
    }

    [Fact]
    public void DynamicList_Remove_DisposesAndRemovesItem()
    {
        var provider = new MockDynamicWidgetProvider();
        provider.SetItems(new[]
        {
            new DynamicItemSnapshot { Id = "A", Label = "A" },
            new DynamicItemSnapshot { Id = "B", Label = "B" },
            new DynamicItemSnapshot { Id = "C", Label = "C" }
        });

        using var vm = new DynamicListItemViewModel(provider);
        Assert.Equal(3, vm.Items.Count);
        var refA = vm.Items[0];
        var refC = vm.Items[2];

        // Remove B: A, C
        provider.SetItems(new[]
        {
            new DynamicItemSnapshot { Id = "A", Label = "A" },
            new DynamicItemSnapshot { Id = "C", Label = "C" }
        });

        Assert.Equal(2, vm.Items.Count);
        Assert.Same(refA, vm.Items[0]);
        Assert.Same(refC, vm.Items[1]);
    }

    [Fact]
    public void DynamicList_Update_UpdatesPropertiesInPlaceWithoutRecreating()
    {
        var provider = new MockDynamicWidgetProvider();
        provider.SetItems(new[]
        {
            new DynamicItemSnapshot { Id = "A", Label = "Original" }
        });

        using var vm = new DynamicListItemViewModel(provider);
        var refA = (ButtonItemViewModel)vm.Items[0];
        Assert.Equal("Original", refA.Label);

        // In-place update
        provider.SetItems(new[]
        {
            new DynamicItemSnapshot { Id = "A", Label = "Updated" }
        });

        Assert.Same(refA, vm.Items[0]);
        Assert.Equal("Updated", refA.Label);
    }

    [Fact]
    public void DynamicList_Reorder_MovesItemWithoutRecreating()
    {
        var provider = new MockDynamicWidgetProvider();
        provider.SetItems(new[]
        {
            new DynamicItemSnapshot { Id = "A", Label = "A" },
            new DynamicItemSnapshot { Id = "B", Label = "B" },
            new DynamicItemSnapshot { Id = "C", Label = "C" }
        });

        using var vm = new DynamicListItemViewModel(provider);
        var refA = vm.Items[0];
        var refB = vm.Items[1];
        var refC = vm.Items[2];

        // Reorder to C, A, B
        provider.SetItems(new[]
        {
            new DynamicItemSnapshot { Id = "C", Label = "C" },
            new DynamicItemSnapshot { Id = "A", Label = "A" },
            new DynamicItemSnapshot { Id = "B", Label = "B" }
        });

        Assert.Equal(3, vm.Items.Count);
        Assert.Same(refC, vm.Items[0]);
        Assert.Same(refA, vm.Items[1]);
        Assert.Same(refB, vm.Items[2]);
    }

    [Fact]
    public void MockDynamicWidgetProvider_DemonstratesRequiredSequence_ABC_to_ACD_to_AupdatedCD()
    {
        var provider = new MockDynamicWidgetProvider();

        // 1. Initial: A, B, C
        provider.SetSequenceInitial();
        using var vm = new DynamicListItemViewModel(provider);

        Assert.Equal(3, vm.Items.Count);
        var initialA = vm.Items[0] as ButtonItemViewModel;
        var initialB = vm.Items[1] as ButtonItemViewModel;
        var initialC = vm.Items[2] as ButtonItemViewModel;

        Assert.NotNull(initialA);
        Assert.NotNull(initialB);
        Assert.NotNull(initialC);
        Assert.Equal("A", initialA.Label);
        Assert.Equal("B", initialB.Label);
        Assert.Equal("C", initialC.Label);

        // 2. Step 1: A, C, D (B removed, D added, A and C retained)
        provider.SetSequenceStep1();

        Assert.Equal(3, vm.Items.Count);
        Assert.Same(initialA, vm.Items[0]);
        Assert.Same(initialC, vm.Items[1]);
        Assert.Equal("D", vm.Items[2].Id);
        var itemD = vm.Items[2];

        // 3. Step 2: A(updated), C, D (A updated in-place, C and D retained)
        provider.SetSequenceStep2();

        Assert.Equal(3, vm.Items.Count);
        Assert.Same(initialA, vm.Items[0]);
        Assert.Equal("A(updated)", initialA.Label);
        Assert.Same(initialC, vm.Items[1]);
        Assert.Same(itemD, vm.Items[2]);
    }

    [Fact]
    public void Provider_ChangedEvent_TriggersDynamicListReconciliation()
    {
        var provider = new MockDynamicWidgetProvider();
        using var vm = new DynamicListItemViewModel(provider);

        int eventFired = 0;
        provider.ItemsChanged += (_, _) => eventFired++;

        provider.SetItems(new[]
        {
            new DynamicItemSnapshot { Id = "X", Label = "X" }
        });

        Assert.Equal(1, eventFired);
        Assert.Single(vm.Items);
        Assert.Equal("X", vm.Items[0].Id);
    }

    [Fact]
    public void Provider_Disposal_SharedVsTransientOwnership()
    {
        var registry = new WidgetProviderRegistry();

        var sharedProvider = new MockDynamicWidgetProvider("shared");
        registry.RegisterShared(sharedProvider);

        var transientProvider = new MockDynamicWidgetProvider("transient");
        registry.RegisterTransient("transient", () => transientProvider);

        // 1. Shared provider resolved: ownsProvider = false
        var (p1, owns1) = registry.Resolve("shared");
        Assert.Same(sharedProvider, p1);
        Assert.False(owns1);

        var vm1 = new DynamicListItemViewModel(p1!, owns1);
        vm1.Dispose();
        Assert.False(sharedProvider.IsDisposed); // Shared must NOT be disposed by widget

        // 2. Transient provider resolved: ownsProvider = true
        var (p2, owns2) = registry.Resolve("transient");
        Assert.Same(transientProvider, p2);
        Assert.True(owns2);

        var vm2 = new DynamicListItemViewModel(p2!, owns2);
        vm2.Dispose();
        Assert.True(transientProvider.IsDisposed); // Transient MUST be disposed by widget

        registry.Dispose();
        Assert.True(sharedProvider.IsDisposed); // Registry disposal disposes shared singletons
    }

    [Fact]
    public void NoDuplicateSubscription_AfterPageSwitching()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "AdaptiveBarPC_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var logger = new AppLogger(tempDir);
            var config = new AppConfig();
            var foreground = new ForegroundAppService(logger);
            var currentApp = foreground.GetCurrent();
            var proc = string.IsNullOrEmpty(currentApp.ProcessName) ? "explorer" : currentApp.ProcessName;

            var provider = new MockDynamicWidgetProvider("pagedProvider");
            var registry = new WidgetProviderRegistry();
            registry.RegisterShared(provider);

            var profileJson = $$"""
            {
              "id": "paged-test",
              "displayName": "Paged Profile",
              "priority": 100,
              "isDefault": false,
              "match": {
                "processNames": [ "{{proc}}" ]
              },
              "pages": [
                {
                  "id": "page1",
                  "items": [
                    { "type": "dynamiclist", "provider": "pagedProvider", "width": 84 }
                  ]
                },
                {
                  "id": "page2",
                  "items": [
                    { "type": "button", "label": "StaticBtn", "width": 84 }
                  ]
                }
              ]
            }
            """;
            File.WriteAllText(Path.Combine(tempDir, "00-test.json"), profileJson);

            var profiles = new ProfileService(tempDir, logger);
            var audio = new WindowsAudioProvider();
            var actions = new ActionExecutor(config, logger);
            var media = new MediaSessionService(logger);

            var vm = new MainViewModel(config, foreground, profiles, audio, actions, media, logger, registry);
            vm.ApplyForegroundApp(currentApp);

            // Page 1 is active with dynamic list: exactly 1 subscription
            Assert.Equal(1, provider.SubscriptionCount);

            // Switch to Page 2 (no dynamic list): old dynamic list disposed, exactly 0 subscriptions
            vm.NextPage();
            Assert.Equal(0, provider.SubscriptionCount);

            // Switch back to Page 1: subscribed once, NOT duplicated
            vm.PreviousPage();
            Assert.Equal(1, provider.SubscriptionCount);

            vm.Dispose();
            Assert.Equal(0, provider.SubscriptionCount);
            registry.Dispose();
        }
        finally
        {
            if (Directory.Exists(tempDir))
                try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void ProfileDisposal_Cleanup_UnsubscribesAndDisposes()
    {
        var provider = new MockDynamicWidgetProvider("cleanupProvider");
        var registry = new WidgetProviderRegistry();
        registry.RegisterShared(provider);

        var tempDir = Path.Combine(Path.GetTempPath(), "AdaptiveBarPC_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var logger = new AppLogger(tempDir);
            var config = new AppConfig();
            var foreground = new ForegroundAppService(logger);
            var currentApp = foreground.GetCurrent();
            var proc = string.IsNullOrEmpty(currentApp.ProcessName) ? "explorer" : currentApp.ProcessName;

            var profileJson = $$"""
            {
              "id": "cleanup-test",
              "displayName": "Cleanup Profile",
              "priority": 100,
              "isDefault": false,
              "match": {
                "processNames": [ "{{proc}}" ]
              },
              "pages": [
                {
                  "id": "main",
                  "items": [
                    { "type": "dynamiclist", "provider": "cleanupProvider", "width": 84 }
                  ]
                }
              ]
            }
            """;
            File.WriteAllText(Path.Combine(tempDir, "00-cleanup.json"), profileJson);

            var profiles = new ProfileService(tempDir, logger);
            var audio = new WindowsAudioProvider();
            var actions = new ActionExecutor(config, logger);
            var media = new MediaSessionService(logger);

            var vm = new MainViewModel(config, foreground, profiles, audio, actions, media, logger, registry);
            vm.ApplyForegroundApp(currentApp);

            Assert.Equal(1, provider.SubscriptionCount);

            vm.Dispose();
            Assert.Equal(0, provider.SubscriptionCount);
            registry.Dispose();
        }
        finally
        {
            if (Directory.Exists(tempDir))
                try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void UnrelatedWidgets_AreNotRebuilt_WhenDynamicListUpdates()
    {
        var provider = new MockDynamicWidgetProvider("isolatedProvider");
        var registry = new WidgetProviderRegistry();
        registry.RegisterShared(provider);

        var tempDir = Path.Combine(Path.GetTempPath(), "AdaptiveBarPC_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var logger = new AppLogger(tempDir);
            var config = new AppConfig();
            var foreground = new ForegroundAppService(logger);
            var currentApp = foreground.GetCurrent();
            var proc = string.IsNullOrEmpty(currentApp.ProcessName) ? "explorer" : currentApp.ProcessName;

            var profileJson = $$"""
            {
              "id": "isolated-test",
              "displayName": "Isolated Profile",
              "priority": 100,
              "isDefault": false,
              "match": {
                "processNames": [ "{{proc}}" ]
              },
              "pages": [
                {
                  "id": "main",
                  "items": [
                    { "type": "button", "label": "StaticBefore", "width": 84 },
                    { "type": "dynamiclist", "provider": "isolatedProvider", "width": 84 },
                    { "type": "button", "label": "StaticAfter", "width": 84 }
                  ]
                }
              ]
            }
            """;
            File.WriteAllText(Path.Combine(tempDir, "00-isolated.json"), profileJson);

            var profiles = new ProfileService(tempDir, logger);
            var audio = new WindowsAudioProvider();
            var actions = new ActionExecutor(config, logger);
            var media = new MediaSessionService(logger);

            var vm = new MainViewModel(config, foreground, profiles, audio, actions, media, logger, registry);
            vm.ApplyForegroundApp(currentApp);

            Assert.Equal(3, vm.Items.Count);
            var staticBefore = vm.Items[0];
            var dynamicList = vm.Items[1] as DynamicListItemViewModel;
            var staticAfter = vm.Items[2];

            Assert.NotNull(dynamicList);
            Assert.Equal(3, dynamicList.Items.Count); // initial A, B, C

            // Provider updates items
            provider.SetSequenceStep1(); // A, C, D

            // Unrelated widgets on the page MUST be the exact same instances
            Assert.Equal(3, vm.Items.Count);
            Assert.Same(staticBefore, vm.Items[0]);
            Assert.Same(dynamicList, vm.Items[1]);
            Assert.Same(staticAfter, vm.Items[2]);

            // Page itself was NOT rebuilt
            Assert.Equal("StaticBefore", ((ButtonItemViewModel)vm.Items[0]).Label);
            Assert.Equal("StaticAfter", ((ButtonItemViewModel)vm.Items[2]).Label);

            vm.Dispose();
            registry.Dispose();
        }
        finally
        {
            if (Directory.Exists(tempDir))
                try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void DynamicList_Layout_EmptyBehavior_CollapseAndCompact()
    {
        var provider = new MockDynamicWidgetProvider();

        // 1. Collapse behavior
        using var vmCollapse = new DynamicListItemViewModel(provider, emptyBehavior: "collapse");
        Assert.Equal(System.Windows.Visibility.Visible, vmCollapse.Visibility);

        provider.SetItems(Array.Empty<DynamicItemSnapshot>());
        Assert.Equal(System.Windows.Visibility.Collapsed, vmCollapse.Visibility);

        // 2. Hidden behavior
        using var vmHidden = new DynamicListItemViewModel(provider, emptyBehavior: "hidden");
        Assert.Equal(System.Windows.Visibility.Hidden, vmHidden.Visibility);

        // 3. Compact/visible behavior
        using var vmCompact = new DynamicListItemViewModel(provider, emptyBehavior: "compact");
        Assert.Equal(System.Windows.Visibility.Visible, vmCompact.Visibility);

        // 4. Flexible vs fixed width
        using var vmFlex = new DynamicListItemViewModel(provider, width: 0);
        Assert.True(double.IsNaN(vmFlex.Width));

        using var vmFixed = new DynamicListItemViewModel(provider, width: 150);
        Assert.Equal(150, vmFixed.Width);
    }

    [Fact]
    public void DynamicList_DuplicateItemIds_DeterministicallyRejected_AndLogged()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "AdaptiveBarPC_Test_Dups_" + Guid.NewGuid().ToString("N"));
        try
        {
            var logger = new AppLogger(tempDir);
            var provider = new MockDynamicWidgetProvider();
            using var vm = new DynamicListItemViewModel(provider, logger: logger);

            // Emit snapshot with duplicate IDs: "A", "B", "A", "C", "B"
            provider.SetItems(new[]
            {
                new DynamicItemSnapshot { Id = "A", Label = "First A" },
                new DynamicItemSnapshot { Id = "B", Label = "First B" },
                new DynamicItemSnapshot { Id = "A", Label = "Duplicate A" },
                new DynamicItemSnapshot { Id = "C", Label = "First C" },
                new DynamicItemSnapshot { Id = "B", Label = "Duplicate B" }
            });

            // Must NOT crash. Duplicate IDs must be deterministically rejected (first occurrence wins)
            Assert.Equal(3, vm.Items.Count);
            Assert.Equal("A", vm.Items[0].Id);
            Assert.Equal("First A", ((ButtonItemViewModel)vm.Items[0]).Label);

            Assert.Equal("B", vm.Items[1].Id);
            Assert.Equal("First B", ((ButtonItemViewModel)vm.Items[1]).Label);

            Assert.Equal("C", vm.Items[2].Id);
            Assert.Equal("First C", ((ButtonItemViewModel)vm.Items[2]).Label);

            // Log file must contain warning about duplicate IDs
            var logPath = Path.Combine(tempDir, "adaptivebar.log");
            Assert.True(File.Exists(logPath));
            var logContent = File.ReadAllText(logPath);
            Assert.Contains("[WARN]", logContent);
            Assert.Contains("duplicate item ID 'A'", logContent);
            Assert.Contains("duplicate item ID 'B'", logContent);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void DynamicList_SameId_SameType_UpdatesInPlace_DifferentType_DisposesAndReplaces()
    {
        var provider = new MockDynamicWidgetProvider();

        // 1. Initial item: Button
        provider.SetItems(new[]
        {
            new DynamicItemSnapshot { Id = "item1", Type = WidgetTypes.Button, Label = "Button1", Width = 84 }
        });

        using var vm = new DynamicListItemViewModel(provider);
        Assert.Single(vm.Items);
        var initialBtn = vm.Items[0] as ButtonItemViewModel;
        Assert.NotNull(initialBtn);
        Assert.Equal("Button1", initialBtn.Label);

        // 2. Same ID, Same Type -> update existing in place
        provider.SetItems(new[]
        {
            new DynamicItemSnapshot { Id = "item1", Type = WidgetTypes.Button, Label = "Button1_Updated", Width = 96 }
        });

        Assert.Single(vm.Items);
        var updatedBtn = vm.Items[0] as ButtonItemViewModel;
        Assert.NotNull(updatedBtn);
        Assert.Same(initialBtn, updatedBtn);
        Assert.Equal("Button1_Updated", updatedBtn.Label);
        Assert.Equal(96, updatedBtn.Width);

        // 3. Same ID, Different Type (Button -> Slider) -> dispose old VM, create replacement VM
        provider.SetItems(new[]
        {
            new DynamicItemSnapshot
            {
                Id = "item1",
                Type = WidgetTypes.Slider,
                Label = "VolumeSlider",
                Min = 0,
                Max = 100,
                Value = 42,
                Width = 120
            }
        });

        Assert.Single(vm.Items);
        var replacementSlider = vm.Items[0] as SliderItemViewModel;
        Assert.NotNull(replacementSlider);
        Assert.NotSame(initialBtn, replacementSlider);
        Assert.Equal("VolumeSlider", replacementSlider.Label);
        Assert.Equal(42, replacementSlider.Value);

        // 4. Same ID, Same Type (Slider -> Slider) -> in place update
        provider.SetItems(new[]
        {
            new DynamicItemSnapshot
            {
                Id = "item1",
                Type = WidgetTypes.Slider,
                Label = "VolumeSlider",
                Min = 0,
                Max = 100,
                Value = 75,
                Width = 120
            }
        });

        Assert.Single(vm.Items);
        var updatedSlider = vm.Items[0] as SliderItemViewModel;
        Assert.NotNull(updatedSlider);
        Assert.Same(replacementSlider, updatedSlider);
        Assert.Equal(75, updatedSlider.Value);
    }

    [Fact]
    public void DynamicList_StaleAndOutOfOrderRevisions_AreIgnored()
    {
        var provider = new MockDynamicWidgetProvider();
        using var vm = new DynamicListItemViewModel(provider);

        // 1. Emit Revision 10
        provider.SetItems(new[]
        {
            new DynamicItemSnapshot { Id = "ItemA", Label = "Revision 10" }
        }, revision: 10);

        Assert.Single(vm.Items);
        Assert.Equal("ItemA", vm.Items[0].Id);
        Assert.Equal("Revision 10", ((ButtonItemViewModel)vm.Items[0]).Label);

        // 2. Emit Stale Revision 5 (older than 10) -> MUST be ignored
        provider.SetItems(new[]
        {
            new DynamicItemSnapshot { Id = "ItemA", Label = "Stale Rev 5" },
            new DynamicItemSnapshot { Id = "StaleItem", Label = "Stale Item" }
        }, revision: 5);

        // State must remain Revision 10
        Assert.Single(vm.Items);
        Assert.Equal("ItemA", vm.Items[0].Id);
        Assert.Equal("Revision 10", ((ButtonItemViewModel)vm.Items[0]).Label);

        // 3. Emit Duplicate Revision 10 -> MUST be ignored
        provider.SetItems(new[]
        {
            new DynamicItemSnapshot { Id = "ItemA", Label = "Dup Rev 10" }
        }, revision: 10);

        Assert.Single(vm.Items);
        Assert.Equal("Revision 10", ((ButtonItemViewModel)vm.Items[0]).Label);

        // 4. Emit Newer Revision 12 -> Applied
        provider.SetItems(new[]
        {
            new DynamicItemSnapshot { Id = "ItemB", Label = "Revision 12" }
        }, revision: 12);

        Assert.Single(vm.Items);
        Assert.Equal("ItemB", vm.Items[0].Id);
        Assert.Equal("Revision 12", ((ButtonItemViewModel)vm.Items[0]).Label);
    }

    [Fact]
    public void DynamicList_SharedProvider_TwoConsumers_DisposingFirst_SecondStillReceivesUpdates()
    {
        var registry = new WidgetProviderRegistry();
        var sharedProvider = new MockDynamicWidgetProvider("sharedAudio");
        registry.RegisterShared(sharedProvider);

        var (p1, owns1) = registry.Resolve("sharedAudio");
        var (p2, owns2) = registry.Resolve("sharedAudio");

        Assert.False(owns1);
        Assert.False(owns2);
        Assert.Same(sharedProvider, p1);
        Assert.Same(sharedProvider, p2);

        var consumer1 = new DynamicListItemViewModel(p1!, owns1);
        var consumer2 = new DynamicListItemViewModel(p2!, owns2);

        // Both consumers have initial items
        Assert.Equal(3, consumer1.Items.Count);
        Assert.Equal(3, consumer2.Items.Count);

        // Dispose consumer 1
        consumer1.Dispose();

        // Shared provider must NOT be disposed
        Assert.False(sharedProvider.IsDisposed);
        Assert.Empty(consumer1.Items);

        // Shared provider emits new items
        sharedProvider.SetItems(new[]
        {
            new DynamicItemSnapshot { Id = "Spotify", Label = "Spotify Playing" }
        });

        // Consumer 2 must still receive updates
        Assert.Single(consumer2.Items);
        Assert.Equal("Spotify", consumer2.Items[0].Id);
        Assert.Equal("Spotify Playing", ((ButtonItemViewModel)consumer2.Items[0]).Label);

        // Consumer 1 must remain empty
        Assert.Empty(consumer1.Items);

        consumer2.Dispose();
        Assert.False(sharedProvider.IsDisposed);

        registry.Dispose();
        Assert.True(sharedProvider.IsDisposed);
    }

    [Fact]
    public void DynamicList_DisposedViewModel_NeverMutatesCollections_OnSubsequentEvents()
    {
        var provider = new MockDynamicWidgetProvider();
        var vm = new DynamicListItemViewModel(provider);
        Assert.Equal(3, vm.Items.Count);

        vm.Dispose();
        Assert.Empty(vm.Items);

        // Provider emits event after VM disposal
        provider.SetItems(new[]
        {
            new DynamicItemSnapshot { Id = "NewAfterDispose", Label = "Late Item" }
        });

        // Items must remain empty, no crash
        Assert.Empty(vm.Items);
    }

    [Fact]
    public void DynamicList_CallbackLifetime_UpdatedOnSnapshot_AndClearedOnDisposal()
    {
        var provider = new MockDynamicWidgetProvider();

        int clickCountA = 0;
        int clickCountB = 0;
        double sliderVal = 0;

        // 1. Initial snapshot with callbacks
        provider.SetItems(new[]
        {
            new DynamicItemSnapshot
            {
                Id = "btn",
                Type = WidgetTypes.Button,
                Label = "Button",
                OnClick = () => clickCountA++
            },
            new DynamicItemSnapshot
            {
                Id = "slider",
                Type = WidgetTypes.Slider,
                Value = 10,
                ValueSetter = v => sliderVal = v
            }
        });

        var vm = new DynamicListItemViewModel(provider);
        var btn = (ButtonItemViewModel)vm.Items[0];
        var slider = (SliderItemViewModel)vm.Items[1];

        btn.Command.Execute(null);
        Assert.Equal(1, clickCountA);

        slider.Value = 50;
        Assert.Equal(50, sliderVal);

        // 2. Update snapshot: btn gets new OnClick B, slider gets null ValueSetter
        provider.SetItems(new[]
        {
            new DynamicItemSnapshot
            {
                Id = "btn",
                Type = WidgetTypes.Button,
                Label = "Button Updated",
                OnClick = () => clickCountB++
            },
            new DynamicItemSnapshot
            {
                Id = "slider",
                Type = WidgetTypes.Slider,
                Value = 60,
                ValueSetter = null
            }
        });

        btn.Command.Execute(null);
        Assert.Equal(1, clickCountA); // old callback was NOT invoked
        Assert.Equal(1, clickCountB); // new callback was invoked

        slider.Value = 80;
        Assert.Equal(50, sliderVal); // ValueSetter was cleared, sliderVal remained 50

        // 3. Dispose VM: callbacks are cleaned up
        vm.Dispose();
        btn.Command.Execute(null);
        Assert.Equal(1, clickCountA);
        Assert.Equal(1, clickCountB);
    }

    [Fact]
    public void DynamicList_BackgroundThread_MarshalsToUIThread()
    {
        RunInSta(() =>
        {
            var uiDispatcher = Dispatcher.CurrentDispatcher;
            var provider = new MockDynamicWidgetProvider();
            using var vm = new DynamicListItemViewModel(provider, dispatcher: uiDispatcher);

            var bgThreadCompleted = new ManualResetEventSlim(false);

            // Fire ItemsChanged from a background thread pool thread
            Task.Run(() =>
            {
                provider.SetItems(new[]
                {
                    new DynamicItemSnapshot { Id = "fromBackground", Label = "BG Item", Width = 100 }
                });
                bgThreadCompleted.Set();
            });

            Assert.True(bgThreadCompleted.Wait(TimeSpan.FromSeconds(5)));

            // Pump dispatcher on the STA thread
            Pump(uiDispatcher, 100);

            Assert.Single(vm.Items);
            Assert.Equal("fromBackground", vm.Items[0].Id);
            Assert.Equal("BG Item", ((ButtonItemViewModel)vm.Items[0]).Label);
        });
    }

    [Fact]
    public void MainViewModel_RegistryOwnership_InjectedRegistryNotDisposed_InternalRegistryDisposed()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "AdaptiveBarPC_Test_RegOwnership_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var logger = new AppLogger(tempDir);
            var config = new AppConfig();
            var foreground = new ForegroundAppService(logger);
            var profiles = new ProfileService(tempDir, logger);
            var audio = new WindowsAudioProvider();
            var actions = new ActionExecutor(config, logger);
            var media = new MediaSessionService(logger);

            // 1. Injected registry: MainViewModel does NOT own it and must NOT dispose it
            var injectedRegistry = new WidgetProviderRegistry();
            var vmWithInjected = new MainViewModel(config, foreground, profiles, audio, actions, media, logger, injectedRegistry);
            vmWithInjected.Dispose();

            Assert.False(injectedRegistry.IsDisposed);
            injectedRegistry.Dispose();
            Assert.True(injectedRegistry.IsDisposed);

            // 2. Internal registry: MainViewModel creates it internally and MUST dispose it on Dispose()
            var foreground2 = new ForegroundAppService(logger);
            var profiles2 = new ProfileService(tempDir, logger);
            var vmWithInternal = new MainViewModel(config, foreground2, profiles2, audio, actions, media, logger, providerRegistry: null);
            var internalRegistry = vmWithInternal.ProviderRegistry;
            Assert.NotNull(internalRegistry);
            Assert.False(internalRegistry.IsDisposed);

            vmWithInternal.Dispose();
            Assert.True(internalRegistry.IsDisposed);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void DynamicList_RevisionZero_ProcessedUnconditionally()
    {
        var provider = new MockDynamicWidgetProvider();
        using var vm = new DynamicListItemViewModel(provider);

        // 1. Emit Revision 10
        provider.SetItems(new[]
        {
            new DynamicItemSnapshot { Id = "Item1", Label = "Rev 10" }
        }, revision: 10);

        Assert.Single(vm.Items);
        Assert.Equal("Rev 10", ((ButtonItemViewModel)vm.Items[0]).Label);

        // 2. Emit Revision 0 (legacy/unversioned): applied unconditionally even though 0 <= 10
        provider.SetItems(new[]
        {
            new DynamicItemSnapshot { Id = "Item1", Label = "Rev 0 First" }
        }, revision: 0);

        Assert.Single(vm.Items);
        Assert.Equal("Rev 0 First", ((ButtonItemViewModel)vm.Items[0]).Label);

        // 3. Emit Revision 0 again: applied unconditionally again
        provider.SetItems(new[]
        {
            new DynamicItemSnapshot { Id = "Item1", Label = "Rev 0 Second" }
        }, revision: 0);

        Assert.Single(vm.Items);
        Assert.Equal("Rev 0 Second", ((ButtonItemViewModel)vm.Items[0]).Label);

        // 4. Stale versioned update (Rev 9 < Rev 10) must still be ignored
        provider.SetItems(new[]
        {
            new DynamicItemSnapshot { Id = "Item1", Label = "Stale Rev 9" }
        }, revision: 9);

        Assert.Single(vm.Items);
        Assert.Equal("Rev 0 Second", ((ButtonItemViewModel)vm.Items[0]).Label);

        // 5. Newer versioned update (Rev 15 > Rev 10) is applied
        provider.SetItems(new[]
        {
            new DynamicItemSnapshot { Id = "Item1", Label = "Rev 15" }
        }, revision: 15);

        Assert.Single(vm.Items);
        Assert.Equal("Rev 15", ((ButtonItemViewModel)vm.Items[0]).Label);
    }


    private static void Pump(Dispatcher dispatcher, int ms = 50)
    {
        var end = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < end)
        {
            var frame = new DispatcherFrame();
            dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(5);
        }
    }
}
