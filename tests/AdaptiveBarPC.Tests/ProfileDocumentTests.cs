using System.IO;
using AdaptiveBarPC.Models;
using AdaptiveBarPC.Services;

namespace AdaptiveBarPC.Tests;

public class ProfileDocumentTests
{
    private readonly ProfileDocumentSerializer _serializer = new();

    [Fact]
    public void BundledProfiles_LoadAndValidate()
    {
        var profilesDir = Path.Combine(AppContext.BaseDirectory, "Profiles");
        var files = Directory.EnumerateFiles(profilesDir, "*.json").OrderBy(x => x).ToList();
        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            var result = _serializer.LoadFile(file);
            Assert.NotNull(result.Document);
            Assert.True(result.IsValid,
                $"{Path.GetFileName(file)}: {string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}"))}");
        }
    }

    [Fact]
    public void SchemaVersion_LegacyDefaultsToOne_AndFutureVersionIsRejected()
    {
        var legacy = _serializer.Load("""{"id":"legacy","isDefault":true,"pages":[]}""");
        Assert.True(legacy.IsValid);
        Assert.Equal(ProfileDocumentSerializer.CurrentSchemaVersion, legacy.Document!.SchemaVersion);

        var future = _serializer.Load("""{"schemaVersion":99,"id":"future","isDefault":true,"pages":[]}""");
        Assert.False(future.IsValid);
        Assert.Contains(future.Diagnostics, d => d.Code == ProfileDiagnosticCodes.UnsupportedSchemaVersion);
    }

    [Fact]
    public void Appearance_IsOptional_AndRoundTripsWithoutMigratingLegacyProfiles()
    {
        var legacy = _serializer.Load("""{"id":"legacy","isDefault":true,"pages":[]}""");
        Assert.True(legacy.IsValid);
        Assert.Null(legacy.Document!.Appearance);

        const string json = """
        {
          "id": "appearance",
          "isDefault": true,
          "pages": [],
          "appearance": {
            "preset": "cyber",
            "accent": "#FF12ABEF",
            "secondaryAccent": "#FFEE44AA",
            "intensity": 0.75,
            "motionEnabled": false,
            "futureAppearanceField": "keep"
          }
        }
        """;

        var loaded = _serializer.Load(json);
        Assert.True(loaded.IsValid);
        var saved = _serializer.Save(loaded.Document!);
        var roundTrip = _serializer.Load(saved);

        Assert.True(roundTrip.IsValid);
        var appearance = Assert.IsType<AppearanceDefinition>(roundTrip.Document!.Appearance);
        Assert.Equal("cyber", appearance.Preset);
        Assert.Equal("#FF12ABEF", appearance.Accent);
        Assert.Equal("#FFEE44AA", appearance.SecondaryAccent);
        Assert.Equal(0.75, appearance.Intensity);
        Assert.False(appearance.MotionEnabled);
        Assert.Contains("\"futureAppearanceField\": \"keep\"", saved);
    }

    [Fact]
    public void RoundTrip_PreservesIdsOrderProviderMatchIconAndExtensionData()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "id": "browser",
          "displayName": "Browser",
          "match": {
            "processNames": ["chrome"],
            "executablePaths": ["C:\\Apps\\Chrome\\chrome.exe"],
            "category": "browser"
          },
          "fixedLeft": [
            { "id": "fixed-volume", "type": "button", "label": "Volume", "width": 84 }
          ],
          "fixedRight": [
            { "id": "fixed-mute", "type": "button", "label": "Mute", "width": 84 }
          ],
          "pages": [
            {
              "id": "nav",
              "items": [
                { "id": "back", "type": "button", "label": "Back", "width": 72, "futureFlag": true },
                {
                  "id": "sessions",
                  "type": "dynamiclist",
                  "provider": "audioSessions",
                  "width": 0,
                  "icon": { "kind": "asset", "value": "assets/audio.svg" }
                }
              ]
            },
            {
              "id": "media",
              "items": [
                { "id": "player", "type": "media", "label": "Media", "width": 0 }
              ]
            }
          ]
        }
        """;

        var loaded = _serializer.Load(json);
        Assert.True(loaded.IsValid);

        var saved = _serializer.Save(loaded.Document!);
        var roundTrip = _serializer.Load(saved);
        Assert.True(roundTrip.IsValid);

        var profile = roundTrip.Document!;
        Assert.Equal("browser", profile.Id);
        Assert.Equal(["nav", "media"], profile.Pages.Select(p => p.Id));
        Assert.Equal(["back", "sessions"], profile.Pages[0].Items.Select(i => i.Id));
        Assert.Equal("audioSessions", profile.Pages[0].Items[1].Provider);
        Assert.Equal("browser", profile.Match.Category);
        Assert.Equal(["chrome"], profile.Match.ProcessNames);
        Assert.Equal([@"C:\Apps\Chrome\chrome.exe"], profile.Match.ExecutablePaths);
        Assert.Equal(["fixed-volume"], profile.FixedLeft.Select(i => i.Id));
        Assert.Equal(["fixed-mute"], profile.FixedRight.Select(i => i.Id));
        Assert.Equal("asset", profile.Pages[0].Items[1].Icon!.Kind);
        Assert.Equal("assets/audio.svg", profile.Pages[0].Items[1].Icon!.Value);
        Assert.Contains("\"futureFlag\": true", saved);
    }

    [Fact]
    public void ReorderingPagesAndWidgets_PreservesExplicitIdsAndRuntimeOrder()
    {
        var document = new ProfileDefinition
        {
            Id = "reorder",
            IsDefault = true,
            Pages =
            [
                new PageDefinition
                {
                    Id = "one",
                    Items =
                    [
                        new ControlDefinition { Id = "a", Type = WidgetTypes.Button, Width = 84 },
                        new ControlDefinition { Id = "b", Type = WidgetTypes.Button, Width = 84 }
                    ]
                },
                new PageDefinition
                {
                    Id = "two",
                    Items = [new ControlDefinition { Id = "c", Type = WidgetTypes.Button, Width = 84 }]
                }
            ]
        };

        document.Pages.Reverse();
        document.Pages[1].Items.Reverse();

        var runtime = _serializer.ToRuntime(document);
        Assert.True(runtime.IsValid);
        Assert.Equal(["two", "one"], runtime.Document!.Pages.Select(p => p.Id));
        Assert.Equal(["b", "a"], runtime.Document.Pages[1].Items.Select(i => i.Id));
    }

    [Fact]
    public void LegacyMissingWidgetIds_AreDeterministicAcrossReorder()
    {
        const string first = """
        {
          "id":"legacy",
          "isDefault":true,
          "pages":[{"id":"main","items":[
            {"type":"button","label":"First","width":84},
            {"type":"button","label":"Second","width":84}
          ]}]
        }
        """;
        const string reordered = """
        {
          "id":"legacy",
          "isDefault":true,
          "pages":[{"id":"main","items":[
            {"type":"button","label":"Second","width":84},
            {"type":"button","label":"First","width":84}
          ]}]
        }
        """;

        var a = _serializer.Load(first).Document!.Pages[0].Items.ToDictionary(i => i.Label, i => i.Id);
        var b = _serializer.Load(reordered).Document!.Pages[0].Items.ToDictionary(i => i.Label, i => i.Id);

        Assert.Equal(a["First"], b["First"]);
        Assert.Equal(a["Second"], b["Second"]);
    }

    [Fact]
    public void Validation_DetectsDuplicateIdsUnknownTypeAndInvalidWidth()
    {
        var document = new ProfileDefinition
        {
            Id = "invalid",
            IsDefault = true,
            Pages =
            [
                new PageDefinition
                {
                    Id = "same",
                    Items =
                    [
                        new ControlDefinition { Id = "dup", Type = WidgetTypes.Button, Width = 84 },
                        new ControlDefinition { Id = "dup", Type = WidgetTypes.Button, Width = 84 },
                        new ControlDefinition { Id = "unknown", Type = "future_widget", Width = 84 },
                        new ControlDefinition { Id = "bad-width", Type = WidgetTypes.Button, Width = -1 }
                    ]
                },
                new PageDefinition { Id = "same" }
            ]
        };

        var diagnostics = _serializer.Validate(document);
        Assert.Contains(diagnostics, d => d.Code == ProfileDiagnosticCodes.DuplicatePageId);
        Assert.Contains(diagnostics, d => d.Code == ProfileDiagnosticCodes.DuplicateWidgetId);
        Assert.Contains(diagnostics, d => d.Code == ProfileDiagnosticCodes.UnknownWidgetType);
        Assert.Contains(diagnostics, d => d.Code == ProfileDiagnosticCodes.InvalidWidth);
    }

    [Fact]
    public void RuntimeGeneratedWidgetType_IsNotPlaceable()
    {
        var document = new ProfileDefinition
        {
            Id = "runtime-child",
            IsDefault = true,
            Pages =
            [
                new PageDefinition
                {
                    Id = "main",
                    Items = [new ControlDefinition { Id = "child", Type = WidgetTypes.AudioSession, Width = 180 }]
                }
            ]
        };

        var diagnostics = _serializer.Validate(document);
        Assert.Contains(diagnostics, d => d.Code == ProfileDiagnosticCodes.RuntimeGeneratedWidgetType);
        Assert.False(WidgetDefinitionRegistry.All.Single(x => x.TypeId == WidgetTypes.AudioSession).IsPlaceable);
    }

    [Fact]
    public void DecorativeMedia_RoundTripsAndNormalizesDefaults()
    {
        const string json = """
        {
          "id": "decorative",
          "isDefault": true,
          "pages": [{
            "id": "main",
            "items": [{
              "id": "art",
              "type": "decorative_media",
              "width": 144,
              "source": "Assets/media/art.png",
              "fit": "invalid",
              "opacity": 2
            }]
          }]
        }
        """;

        var loaded = _serializer.Load(json);
        Assert.True(loaded.IsValid);
        var media = loaded.Document!.Pages[0].Items[0];
        Assert.Equal(WidgetTypes.DecorativeMedia, media.Type);
        Assert.Equal("Assets/media/art.png", media.Source);
        Assert.Equal("contain", media.Fit);
        Assert.True(media.Loop);
        Assert.True(media.Enabled);
        Assert.Equal(1d, media.Opacity);

        var saved = _serializer.Save(loaded.Document);
        var roundTrip = _serializer.Load(saved);
        Assert.True(roundTrip.IsValid);
        Assert.Equal("Assets/media/art.png", roundTrip.Document!.Pages[0].Items[0].Source);
        Assert.True(WidgetDefinitionRegistry.All.Single(x => x.TypeId == WidgetTypes.DecorativeMedia).IsPlaceable);
    }

    [Fact]
    public void DecorativeMedia_ClampsOpacityAndPreservesExplicitOptions()
    {
        const string json = """
        {
          "id": "decorative",
          "isDefault": true,
          "items": [{
            "id": "clip",
            "type": "decorative_media",
            "width": 120,
            "fit": "cover",
            "loop": false,
            "opacity": -0.5,
            "enabled": false,
            "futureMediaField": 42
          }]
        }
        """;

        var loaded = _serializer.Load(json);
        Assert.True(loaded.IsValid);
        var media = loaded.Document!.Items[0];
        Assert.Equal("cover", media.Fit);
        Assert.False(media.Loop);
        Assert.False(media.Enabled);
        Assert.Equal(0d, media.Opacity);
        Assert.Contains("\"futureMediaField\": 42", _serializer.Save(loaded.Document));
    }

    [Fact]
    public void RuntimeSnapshotState_IsNotSerializedIntoProfileDocument()
    {
        var document = new ProfileDefinition
        {
            Id = "dynamic",
            IsDefault = true,
            Pages =
            [
                new PageDefinition
                {
                    Id = "main",
                    Items =
                    [
                        new ControlDefinition
                        {
                            Id = "sessions",
                            Type = WidgetTypes.DynamicList,
                            Provider = "audioSessions",
                            Width = 0
                        }
                    ]
                }
            ]
        };

        _ = new DynamicItemSnapshot
        {
            Id = "audio:pid:123",
            Label = "Transient Player",
            Value = 42,
            CustomData = new object()
        };

        var json = _serializer.Save(document);
        Assert.DoesNotContain("Transient Player", json);
        Assert.DoesNotContain("customData", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MatchAndProviderBinding_SurviveRuntimeConversion()
    {
        var document = new ProfileDefinition
        {
            Id = "binding",
            Match = new MatchDefinition
            {
                ProcessNames = ["app"],
                WindowTitleContains = "Project",
                Categories = ["development"]
            },
            Pages =
            [
                new PageDefinition
                {
                    Id = "main",
                    Items =
                    [
                        new ControlDefinition
                        {
                            Id = "hardware",
                            Type = WidgetTypes.DynamicList,
                            Provider = "hardware",
                            Width = 0
                        }
                    ]
                }
            ]
        };

        var runtime = _serializer.ToRuntime(document);
        Assert.True(runtime.IsValid);
        Assert.Equal(["app"], runtime.Document!.Match.ProcessNames);
        Assert.Equal("Project", runtime.Document.Match.WindowTitleContains);
        Assert.Equal(["development"], runtime.Document.Match.Categories);
        Assert.Equal("hardware", runtime.Document.Pages[0].Items[0].Provider);
    }

    [Fact]
    public void IconAssetReference_AllowsAbsolutePath_ButRejectsRelativeTraversal()
    {
        var document = new ProfileDefinition
        {
            Id = "icons",
            IsDefault = true,
            Pages =
            [
                new PageDefinition
                {
                    Id = "main",
                    Items =
                    [
                        new ControlDefinition
                        {
                            Id = "icon",
                            Type = WidgetTypes.Button,
                            Width = 84,
                            Icon = new IconDefinition { Kind = "asset", Value = "C:\\temp\\icon.png" }
                        }
                    ]
                }
            ]
        };

        var diagnostics = _serializer.Validate(document);
        Assert.DoesNotContain(diagnostics, d => d.Code == ProfileDiagnosticCodes.InvalidIconAssetReference);

        document.Pages[0].Items[0].Icon!.Value = "../outside/icon.png";
        diagnostics = _serializer.Validate(document);
        Assert.Contains(diagnostics, d => d.Code == ProfileDiagnosticCodes.InvalidIconAssetReference);
    }
}
