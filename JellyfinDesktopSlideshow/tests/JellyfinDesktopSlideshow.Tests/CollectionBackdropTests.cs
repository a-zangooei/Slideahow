using System.IO;
using System.Net;
using System.Net.Http;
using JellyfinDesktopSlideshow.Infrastructure;
using JellyfinDesktopSlideshow.Models;
using JellyfinDesktopSlideshow.Services;
using Xunit;

namespace JellyfinDesktopSlideshow.Tests;

public sealed class CollectionBackdropTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"jf-collection-test-{Guid.NewGuid():N}");

    [Fact]
    public async Task Scenario1_CollectionWithOwnAndMemberBackdrops_UsesBothAndRespectsBackdropPreferenceAndAspectFilter()
    {
        Directory.CreateDirectory(_tempDir);
        var bannerBytes = ImageAspectFilterTests.CreatePngWithDimensions(1000, 185);
        var collectionOwnBytes = ImageAspectFilterTests.CreatePngWithDimensions(1920, 1080);
        var memberOneBytes = ImageAspectFilterTests.CreatePngWithDimensions(2560, 1440);
        var memberTwoBytes = ImageAspectFilterTests.CreatePngWithDimensions(1920, 1080);
        var returnBannerForCollection = false;

        var handler = CreateHandler((path, _) =>
        {
            if (path.Contains("/Items", StringComparison.Ordinal) && path.Contains("ParentId=col-both", StringComparison.Ordinal))
            {
                return StubHttpMessageHandler.JsonResponse(
                    HttpStatusCode.OK,
                    """
                    {
                      "Items": [
                        {"Id":"m-1","Name":"Member Movie 1","Type":"Movie","BackdropImageTags":["tag-m1"]},
                        {"Id":"m-2","Name":"Member Series 2","Type":"Series","BackdropImageTags":["tag-m2"]}
                      ],
                      "TotalRecordCount": 2
                    }
                    """);
            }

            if (path.Contains("/Images/Backdrop/", StringComparison.Ordinal))
            {
                if (path.Contains("/Items/col-both/", StringComparison.Ordinal))
                {
                    return ImageResponse(returnBannerForCollection ? bannerBytes : collectionOwnBytes);
                }

                if (path.Contains("/Items/m-1/", StringComparison.Ordinal))
                {
                    return ImageResponse(memberOneBytes);
                }

                if (path.Contains("/Items/m-2/", StringComparison.Ordinal))
                {
                    return ImageResponse(memberTwoBytes);
                }
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var (imageService, settings, _) = await CreateImageServiceAsync(handler, useApiKey: false);

        var collection = new MediaItem
        {
            Id = "col-both",
            Name = "Sci-Fi Anthology",
            Type = "BoxSet",
            BackdropImageTags = ["tag-col"]
        };

        var candidates = await imageService.CollectBackdropCandidatesAsync(collection);
        Assert.Equal(3, candidates.Count);
        Assert.Equal(["col-both", "m-1", "m-2"], candidates.Select(c => c.SourceItemId).ToArray());

        // Preference index 0 selects the collection's own valid backdrop
        settings.Current.BackdropIndexPreference = 0;
        var ownResult = await imageService.ResolveBackdropAsync(collection);
        Assert.Equal(BackdropResolveStatus.Success, ownResult.Status);
        Assert.Equal("col-both", ownResult.ImageInfo!.ItemId);

        // Preference index 1 selects member 1's backdrop directly from the combined pool
        settings.Current.BackdropIndexPreference = 1;
        var memberResult = await imageService.ResolveBackdropAsync(collection);
        Assert.Equal(BackdropResolveStatus.Success, memberResult.Status);
        Assert.Equal("m-1", memberResult.ImageInfo!.ItemId);

        // When preference index 0 is a banner, falls back seamlessly to a valid member backdrop
        returnBannerForCollection = true;
        var uncachedCollection = new MediaItem
        {
            Id = "col-both",
            Name = "Sci-Fi Anthology",
            Type = "BoxSet",
            BackdropImageTags = ["tag-col-banner"]
        };
        settings.Current.BackdropIndexPreference = 0;
        var fallbackToMemberResult = await imageService.ResolveBackdropAsync(uncachedCollection);
        Assert.Equal(BackdropResolveStatus.Success, fallbackToMemberResult.Status);
        Assert.Equal("m-1", fallbackToMemberResult.ImageInfo!.ItemId);
    }

    [Fact]
    public async Task Scenario2_CollectionWithoutOwnBackdrop_UsesMemberBackdropsAndPreservesCollectionForOpenInJellyfin()
    {
        Directory.CreateDirectory(_tempDir);
        var memberBytes = ImageAspectFilterTests.CreatePngWithDimensions(1920, 1080);

        var handler = CreateHandler((path, _) =>
        {
            if (path.Contains("/Items", StringComparison.Ordinal) && path.Contains("ParentId=col-no-own", StringComparison.Ordinal))
            {
                Assert.Contains("CollapseBoxSetItems=false", path, StringComparison.Ordinal);
                return StubHttpMessageHandler.JsonResponse(
                    HttpStatusCode.OK,
                    """
                    {
                      "Items": [
                        {"Id":"m-no-art","Name":"Hidden Movie 1","Type":"Movie","BackdropImageTags":[]},
                        {"Id":"m-with-art","Name":"Hidden Movie 2","Type":"Movie","BackdropImageTags":["tag-hidden-m2"]}
                      ],
                      "TotalRecordCount": 2
                    }
                    """);
            }

            if (path.Contains("/Items/m-with-art/Images/Backdrop/0", StringComparison.Ordinal))
            {
                return ImageResponse(memberBytes);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var (imageService, settings, _) = await CreateImageServiceAsync(handler, useApiKey: true);
        settings.Current.OrderMode = SlideshowOrderMode.Sequential;

        var collection = new MediaItem
        {
            Id = "col-no-own",
            Name = "Hidden Gem Collection",
            Type = "BoxSet",
            BackdropImageTags = []
        };

        var store = new CurrentWallpaperItemStore(new NullLoggingService(), Path.Combine(_tempDir, "current-item.json"));
        var slideshow = new SlideshowService(imageService, settings, new FakeTimer(), new NullLoggingService(), store);
        slideshow.SetItems([collection]);

        await slideshow.StartAsync();

        Assert.Equal("col-no-own", slideshow.CurrentState.CurrentItemId);
        Assert.Equal("BoxSet", slideshow.CurrentState.CurrentItem?.Type);
        Assert.True(File.Exists(slideshow.CurrentState.CurrentImagePath));

        // Stored item remains the BoxSet collection so opening in Jellyfin targets the collection itself
        var persistedItem = store.Load();
        Assert.NotNull(persistedItem);
        Assert.Equal("col-no-own", persistedItem!.Id);
        Assert.Equal("BoxSet", persistedItem.Type);
    }

    [Fact]
    public async Task Scenario3_CollectionWithoutOwnBackdropAndNoMemberBackdrops_ContinuesSafelyWithFallbackOrSkips()
    {
        Directory.CreateDirectory(_tempDir);
        var validMovieBytes = ImageAspectFilterTests.CreatePngWithDimensions(1920, 1080);

        var handler = CreateHandler((path, _) =>
        {
            if (path.Contains("/Items", StringComparison.Ordinal) && path.Contains("ParentId=col-empty", StringComparison.Ordinal))
            {
                return StubHttpMessageHandler.JsonResponse(
                    HttpStatusCode.OK,
                    """
                    {
                      "Items": [
                        {"Id":"m-empty-a","Name":"Silent A","Type":"Movie","BackdropImageTags":[]},
                        {"Id":"m-empty-b","Name":"Silent B","Type":"Series","BackdropImageTags":[]}
                      ],
                      "TotalRecordCount": 2
                    }
                    """);
            }

            if (path.Contains("/Items/m-valid/Images/Backdrop/0", StringComparison.Ordinal))
            {
                return ImageResponse(validMovieBytes);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var (imageService, settings, _) = await CreateImageServiceAsync(handler, useApiKey: true);
        settings.Current.OrderMode = SlideshowOrderMode.Sequential;

        var emptyCollection = new MediaItem
        {
            Id = "col-empty",
            Name = "No Art Collection",
            Type = "BoxSet",
            BackdropImageTags = []
        };

        var skipped = await imageService.ResolveBackdropAsync(emptyCollection);
        Assert.Equal(BackdropResolveStatus.SkippedNoBackdrop, skipped.Status);
        Assert.False(skipped.HasImage);

        // In slideshow, empty collection is skipped safely and next valid item is displayed
        var validMovie = new MediaItem
        {
            Id = "m-valid",
            Name = "Regular Movie",
            Type = "Movie",
            BackdropImageTags = ["tag-valid"]
        };
        var slideshow = new SlideshowService(imageService, settings, new FakeTimer(), new NullLoggingService());
        slideshow.SetItems([emptyCollection, validMovie]);
        await slideshow.StartAsync();

        Assert.Equal("m-valid", slideshow.CurrentState.CurrentItemId);
        Assert.True(File.Exists(slideshow.CurrentState.CurrentImagePath));

        // If fallback image is configured, ResolveBackdropAsync uses fallback safely
        var fallbackFile = Path.Combine(_tempDir, "fallback.png");
        await File.WriteAllBytesAsync(fallbackFile, validMovieBytes);
        settings.Current.FallbackImagePath = fallbackFile;

        var fallbackResult = await imageService.ResolveBackdropAsync(emptyCollection);
        Assert.Equal(BackdropResolveStatus.UsedFallback, fallbackResult.Status);
        Assert.Equal(fallbackFile, fallbackResult.LocalFilePath);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Scenario4_WorksIndependentlyOfLibrarySource_CollectionsLibraryAndCollapsedMoviesLibrary(bool useApiKey)
    {
        Directory.CreateDirectory(_tempDir);
        var memberBytes = ImageAspectFilterTests.CreatePngWithDimensions(1920, 1080);
        var collectionQueries = new List<string>();

        var handler = CreateHandler((path, absolutePath) =>
        {
            if (absolutePath.EndsWith("/Library/MediaFolders", StringComparison.Ordinal) ||
                absolutePath.EndsWith("/Users/u-1/Views", StringComparison.Ordinal))
            {
                return StubHttpMessageHandler.JsonResponse(
                    HttpStatusCode.OK,
                    """
                    {
                      "Items": [
                        {"Id":"lib-movies","Name":"Movies","CollectionType":"movies"},
                        {"Id":"lib-boxsets","Name":"Collections","CollectionType":"boxsets"}
                      ],
                      "TotalRecordCount": 2
                    }
                    """);
            }

            if (absolutePath.EndsWith("/Items", StringComparison.Ordinal) ||
                absolutePath.EndsWith("/Users/u-1/Items", StringComparison.Ordinal))
            {
                if (path.Contains("ParentId=col-collapsed", StringComparison.Ordinal) ||
                    path.Contains("ParentId=col-dedicated", StringComparison.Ordinal))
                {
                    collectionQueries.Add(path);
                    Assert.Contains("CollapseBoxSetItems=false", path, StringComparison.Ordinal);

                    var memberJson = path.Contains("ParentId=col-collapsed", StringComparison.Ordinal)
                        ? """{"Items":[{"Id":"m-hidden","Name":"Collapsed Movie","Type":"Movie","BackdropImageTags":["tag-hidden"]}],"TotalRecordCount":1}"""
                        : """{"Items":[{"Id":"m-boxset-child","Name":"Dedicated Child","Type":"Movie","BackdropImageTags":["tag-dedicated"]}],"TotalRecordCount":1}""";
                    return StubHttpMessageHandler.JsonResponse(HttpStatusCode.OK, memberJson);
                }

                Assert.Contains("BoxSet", path, StringComparison.Ordinal);

                if (path.Contains("ParentId=lib-movies", StringComparison.Ordinal))
                {
                    return StubHttpMessageHandler.JsonResponse(
                        HttpStatusCode.OK,
                        """{"Items":[{"Id":"col-collapsed","Name":"Collapsed Saga","Type":"BoxSet","BackdropImageTags":[]}],"TotalRecordCount":1}""");
                }

                if (path.Contains("ParentId=lib-boxsets", StringComparison.Ordinal))
                {
                    return StubHttpMessageHandler.JsonResponse(
                        HttpStatusCode.OK,
                        """{"Items":[{"Id":"col-dedicated","Name":"Dedicated Saga","Type":"BoxSet","BackdropImageTags":[]}],"TotalRecordCount":1}""");
                }
            }

            if (path.Contains("/Images/Backdrop/", StringComparison.Ordinal))
            {
                return ImageResponse(memberBytes);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var (client, auth, settings, imageService) = await CreateFullPipelineAsync(handler, useApiKey);
        var libraryService = new LibraryService(client, auth, settings);

        var moviesItems = await libraryService.GetMediaItemsAsync(["lib-movies"], ["Movie"]);
        var boxsetsItems = await libraryService.GetMediaItemsAsync(["lib-boxsets"], ["Movie", "Series"]);

        var collapsedCol = Assert.Single(moviesItems);
        Assert.True(collapsedCol.IsCollection);
        Assert.True(collapsedCol.HasBackdrop);

        var dedicatedCol = Assert.Single(boxsetsItems);
        Assert.True(dedicatedCol.IsCollection);
        Assert.True(dedicatedCol.HasBackdrop);

        var collapsedResolved = await imageService.ResolveBackdropAsync(collapsedCol);
        var dedicatedResolved = await imageService.ResolveBackdropAsync(dedicatedCol);

        Assert.Equal(BackdropResolveStatus.Success, collapsedResolved.Status);
        Assert.Equal("m-hidden", collapsedResolved.ImageInfo!.ItemId);
        Assert.Equal(BackdropResolveStatus.Success, dedicatedResolved.Status);
        Assert.Equal("m-boxset-child", dedicatedResolved.ImageInfo!.ItemId);
        Assert.Equal(2, collectionQueries.Count);
    }

    [Fact]
    public async Task Scenario5_DeduplicatesRepeatedMembersTagsAndIdenticalImageBytes()
    {
        Directory.CreateDirectory(_tempDir);
        var duplicateBannerBytes = ImageAspectFilterTests.CreatePngWithDimensions(900, 180);
        var uniqueValidBytes = ImageAspectFilterTests.CreatePngWithDimensions(1920, 1080);
        var downloadedUrls = new List<string>();

        var handler = CreateHandler((path, _) =>
        {
            if (path.Contains("/Items", StringComparison.Ordinal) && path.Contains("ParentId=col-dup", StringComparison.Ordinal))
            {
                return StubHttpMessageHandler.JsonResponse(
                    HttpStatusCode.OK,
                    """
                    {
                      "Items": [
                        {"Id":"col-dup","Name":"Self Ref","Type":"BoxSet","BackdropImageTags":["shared-tag"]},
                        {"Id":"m-1","Name":"Member 1","Type":"Movie","BackdropImageTags":["shared-tag","same-bytes-tag-2"]},
                        {"Id":"m-1","Name":"Member 1 Duplicate","Type":"Movie","BackdropImageTags":["shared-tag","same-bytes-tag-2"]},
                        {"Id":"m-2","Name":"Member 2","Type":"Movie","BackdropImageTags":["unique-good-tag"]}
                      ],
                      "TotalRecordCount": 4
                    }
                    """);
            }

            if (path.Contains("/Images/Backdrop/", StringComparison.Ordinal))
            {
                downloadedUrls.Add(path);
                if (path.Contains("unique-good-tag", StringComparison.Ordinal))
                {
                    return ImageResponse(uniqueValidBytes);
                }

                // Collection ("shared-tag") and Member 1's second tag ("same-bytes-tag-2") return identical banner bytes
                return ImageResponse(duplicateBannerBytes);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var (imageService, settings, _) = await CreateImageServiceAsync(handler, useApiKey: true);
        settings.Current.BackdropIndexPreference = 0;
        settings.Current.RandomBackdropPerItem = false;

        var collection = new MediaItem
        {
            Id = "col-dup",
            Name = "Duplicate Heavy Collection",
            Type = "BoxSet",
            BackdropImageTags = ["shared-tag", "shared-tag"]
        };

        // Candidate list excludes self-ref, repeated member ID, and duplicate "shared-tag"
        var candidates = await imageService.CollectBackdropCandidatesAsync(collection);
        Assert.Equal(3, candidates.Count);
        Assert.Equal(["shared-tag", "same-bytes-tag-2", "unique-good-tag"], candidates.Select(c => c.Tag).ToArray());

        // Resolution downloads collection banner ("shared-tag"), skips identical bytes on "same-bytes-tag-2", and picks unique valid image
        var result = await imageService.ResolveBackdropAsync(collection);
        Assert.Equal(BackdropResolveStatus.Success, result.Status);
        Assert.Equal("m-2", result.ImageInfo!.ItemId);
        Assert.Equal("unique-good-tag", result.ImageInfo.Tag);
    }

    [Fact]
    public async Task OfflineMode_FindsCachedMemberBackdropForCollection()
    {
        Directory.CreateDirectory(_tempDir);
        var validBytes = ImageAspectFilterTests.CreatePngWithDimensions(1920, 1080);
        var failNetwork = false;

        var handler = CreateHandler((path, _) =>
        {
            if (failNetwork)
            {
                throw new HttpRequestException("Offline");
            }

            if (path.Contains("/Items", StringComparison.Ordinal) && path.Contains("ParentId=col-offline", StringComparison.Ordinal))
            {
                return StubHttpMessageHandler.JsonResponse(
                    HttpStatusCode.OK,
                    """{"Items":[{"Id":"m-offline-child","Name":"Offline Member","Type":"Movie","BackdropImageTags":["tag-old"]}],"TotalRecordCount":1}""");
            }

            return ImageResponse(validBytes);
        });

        var (imageService, _, cache) = await CreateImageServiceAsync(handler, useApiKey: true);

        // Pre-seed cache for member item, then go offline with changed tag
        await cache.SaveToCacheAsync("m-offline-child", 0, "tag-old", validBytes, ".png");
        failNetwork = true;

        var collection = new MediaItem
        {
            Id = "col-offline",
            Name = "Offline Collection",
            Type = "BoxSet",
            BackdropImageTags = [],
            CollectionMembers =
            [
                new MediaItem
                {
                    Id = "m-offline-child",
                    Name = "Offline Member",
                    Type = "Movie",
                    BackdropImageTags = ["tag-new"]
                }
            ]
        };

        var result = await imageService.ResolveBackdropAsync(collection);
        Assert.Equal(BackdropResolveStatus.UsedCacheOffline, result.Status);
        Assert.True(result.HasImage);
    }

    private async Task<(ImageService ImageService, SettingsService Settings, CacheService Cache)> CreateImageServiceAsync(
        StubHttpMessageHandler handler,
        bool useApiKey)
    {
        var (_, _, settings, imageService, cache) = await CreateFullPipelineInternalAsync(handler, useApiKey);
        return (imageService, settings, cache);
    }

    private async Task<(JellyfinClient Client, AuthenticationService Auth, SettingsService Settings, ImageService ImageService)> CreateFullPipelineAsync(
        StubHttpMessageHandler handler,
        bool useApiKey)
    {
        var (client, auth, settings, imageService, _) = await CreateFullPipelineInternalAsync(handler, useApiKey);
        return (client, auth, settings, imageService);
    }

    private async Task<(JellyfinClient Client, AuthenticationService Auth, SettingsService Settings, ImageService ImageService, CacheService Cache)> CreateFullPipelineInternalAsync(
        StubHttpMessageHandler handler,
        bool useApiKey)
    {
        var logger = new NullLoggingService();
        var settings = new SettingsService(logger, Path.Combine(_tempDir, $"settings-{Guid.NewGuid():N}.json"));
        settings.Load();

        var client = new JellyfinClient(new HttpClient(handler), logger);
        var auth = new AuthenticationService(client, settings, new InMemorySecureStorageService(), logger);
        if (useApiKey)
        {
            await auth.LoginWithApiKeyAsync("http://192.168.1.10:8096", "api-key-test");
        }
        else
        {
            await auth.LoginAsync("http://192.168.1.10:8096", "ali", "pass");
        }

        var cache = new CacheService(logger, Path.Combine(_tempDir, $"cache-{Guid.NewGuid():N}"));
        var imageService = new ImageService(client, auth, cache, settings, logger);
        return (client, auth, settings, imageService, cache);
    }

    private static StubHttpMessageHandler CreateHandler(Func<string, string, HttpResponseMessage> responder)
    {
        return new StubHttpMessageHandler(req =>
        {
            var pathAndQuery = req.RequestUri!.PathAndQuery;
            var absolutePath = req.RequestUri.AbsolutePath;

            if (absolutePath.EndsWith("/System/Info/Public", StringComparison.Ordinal))
            {
                return StubHttpMessageHandler.JsonResponse(HttpStatusCode.OK, """{"Id":"srv-1","ServerName":"NAS","Version":"10.10.3"}""");
            }

            if (absolutePath.EndsWith("/System/Info", StringComparison.Ordinal))
            {
                return StubHttpMessageHandler.JsonResponse(HttpStatusCode.OK, """{"Id":"srv-1","Version":"10.10.3"}""");
            }

            if (absolutePath.EndsWith("/Users/AuthenticateByName", StringComparison.Ordinal))
            {
                return StubHttpMessageHandler.JsonResponse(HttpStatusCode.OK, """{"User":{"Id":"u-1","Name":"ali"},"AccessToken":"tok","ServerId":"srv-1"}""");
            }

            return responder(pathAndQuery, absolutePath);
        });
    }

    private static HttpResponseMessage ImageResponse(byte[] bytes)
        => new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
        };

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private sealed class FakeTimer : ISlideshowTimer
    {
        public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(60);
        public bool IsEnabled { get; private set; }
        public event EventHandler? Tick;
        public void Start() => IsEnabled = true;
        public void Stop() => IsEnabled = false;
        public void RaiseTick() => Tick?.Invoke(this, EventArgs.Empty);
    }
}
