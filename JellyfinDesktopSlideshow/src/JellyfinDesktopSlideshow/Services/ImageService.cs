using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using JellyfinDesktopSlideshow.Infrastructure;
using JellyfinDesktopSlideshow.Localization;
using JellyfinDesktopSlideshow.Models;

namespace JellyfinDesktopSlideshow.Services;

public sealed class ImageService : IImageService
{
    private const int DefaultMaxWidth = 1920;
    private const int DefaultQuality = 92;
    private const int MaxCollectionNestingDepth = 3;

    private readonly IJellyfinClient _client;
    private readonly IAuthenticationService _authService;
    private readonly ICacheService _cacheService;
    private readonly ISettingsService _settingsService;
    private readonly ILoggingService _logger;
    private readonly ILocalizationService _localization;
    private readonly ConcurrentDictionary<string, IReadOnlyList<MediaItem>> _offlineCollectionMembersCache =
        new(StringComparer.OrdinalIgnoreCase);

    public ImageService(
        IJellyfinClient client,
        IAuthenticationService authService,
        ICacheService cacheService,
        ISettingsService settingsService,
        ILoggingService logger,
        ILocalizationService? localization = null)
    {
        _client = client;
        _authService = authService;
        _cacheService = cacheService;
        _settingsService = settingsService;
        _logger = logger;
        _localization = localization ?? AppLocalization.Current;
    }

    public async Task<BackdropResolveResult> ResolveBackdropAsync(
        MediaItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        var settings = _settingsService.Current;

        List<BackdropCandidate> candidates;
        List<string> knownMemberIds;
        var networkErrorOccurred = false;
        string? lastFailureReason = null;

        if (item.IsCollection)
        {
            var collectionResult = await CollectCollectionCandidatesInternalAsync(item, settings, cancellationToken);
            candidates = collectionResult.Candidates;
            knownMemberIds = collectionResult.KnownMemberIds;
            networkErrorOccurred = collectionResult.NetworkErrorOccurred;
            lastFailureReason = collectionResult.FailureReason;
        }
        else
        {
            knownMemberIds = [];
            if (item.BackdropImageTags.Count == 0)
            {
                return ResolveWithoutBackdrop(item, settings);
            }

            candidates = new List<BackdropCandidate>(item.BackdropImageTags.Count);
            for (var i = 0; i < item.BackdropImageTags.Count; i++)
            {
                candidates.Add(new BackdropCandidate(
                    item.Id,
                    item.Name,
                    i,
                    item.BackdropImageTags[i],
                    item.BackdropImageUrl,
                    IsFromCollectionMember: false));
            }
        }

        if (candidates.Count == 0)
        {
            if (networkErrorOccurred)
            {
                var offlineInfo = new JellyfinImageInfo
                {
                    ItemId = item.Id,
                    ImageIndex = 0,
                    MaxWidth = DefaultMaxWidth,
                    Quality = DefaultQuality,
                    RequestUrl = item.BackdropImageUrl
                };

                return TryOfflineCacheOrFallback(
                    item,
                    knownMemberIds,
                    offlineInfo,
                    settings,
                    lastFailureReason ?? _localization.GetString("Image.Error.CollectionFetchFailed"));
            }

            return ResolveWithoutBackdrop(item, settings);
        }

        var candidateIndices = BuildCandidateIndices(candidates.Count, settings);
        var rejectedByAspectCount = 0;
        var anyNonAspectFailure = false;
        string? lastAspectReason = null;
        JellyfinImageInfo? lastImageInfo = null;
        var unauthenticated = false;
        var rejectedContentHashes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var candidatePosition in candidateIndices)
        {
            var candidate = candidates[candidatePosition];
            var imageInfo = new JellyfinImageInfo
            {
                ItemId = candidate.SourceItemId,
                ImageIndex = candidate.ImageIndex,
                Tag = candidate.Tag,
                MaxWidth = DefaultMaxWidth,
                Quality = DefaultQuality,
                RequestUrl = candidate.RequestUrl ?? item.BackdropImageUrl
            };
            lastImageInfo = imageInfo;

            var cachedPath = _cacheService.TryGetCachedFile(candidate.SourceItemId, candidate.ImageIndex, candidate.Tag);
            if (cachedPath is not null)
            {
                if (item.IsCollection && IsDuplicateFileContent(cachedPath, rejectedContentHashes))
                {
                    continue;
                }

                if (ImageAspectFilter.IsAcceptableFile(cachedPath, settings, out var cachedAspectReason))
                {
                    return new BackdropResolveResult
                    {
                        Status = BackdropResolveStatus.Success,
                        LocalFilePath = cachedPath,
                        ImageInfo = imageInfo,
                        Message = candidate.IsFromCollectionMember
                            ? _localization.Format(
                                "Image.Cache.LoadedCollectionMemberFormat",
                                BidiTextFormatter.Isolate(candidate.SourceItemName))
                            : _localization.GetString("Image.Cache.LoadedLocal")
                    };
                }

                rejectedByAspectCount++;
                lastAspectReason = cachedAspectReason;
                RecordRejectedFileHash(cachedPath, rejectedContentHashes);
                _logger.Info($"Backdrop شماره {candidate.ImageIndex} عنوان «{candidate.SourceItemName}» ({candidate.SourceItemId}) در کش به‌دلیل نسبت نامناسب رد شد: {cachedAspectReason}");
                continue;
            }

            var session = _authService.CurrentSession;
            if (!session.IsAuthenticated ||
                session.ServerUri is null ||
                string.IsNullOrWhiteSpace(session.AccessToken))
            {
                unauthenticated = true;
                lastFailureReason = _localization.GetString("Image.Error.NoActiveConnection");
                break;
            }

            try
            {
                var (bytes, _) = await _client.DownloadBackdropImageAsync(
                    session.ServerUri,
                    candidate.SourceItemId,
                    candidate.ImageIndex,
                    candidate.Tag,
                    DefaultMaxWidth,
                    DefaultQuality,
                    session.AccessToken,
                    settings.DeviceId,
                    cancellationToken);

                if (!ImageFormatValidator.TryDetectImageExtension(bytes, out var extension))
                {
                    anyNonAspectFailure = true;
                    _logger.Warning($"تصویر دریافت‌شده برای {candidate.SourceItemName} ({candidate.SourceItemId}) در Backdrop شماره {candidate.ImageIndex} ناقص یا نامعتبر بود.");
                    lastFailureReason = _localization.GetString("Image.Error.CorruptDownload");
                    continue;
                }

                var contentHash = item.IsCollection ? ComputeContentHash(bytes) : null;
                if (contentHash is not null && rejectedContentHashes.Contains(contentHash))
                {
                    continue;
                }

                if (!ImageAspectFilter.IsAcceptableBytes(bytes, settings, out var aspectReason))
                {
                    rejectedByAspectCount++;
                    lastAspectReason = aspectReason;
                    if (contentHash is not null)
                    {
                        rejectedContentHashes.Add(contentHash);
                    }

                    _logger.Info($"Backdrop شماره {candidate.ImageIndex} عنوان «{candidate.SourceItemName}» ({candidate.SourceItemId}) به‌دلیل نسبت تصویر نامناسب رد شد: {aspectReason}");
                    continue;
                }

                var savedPath = await _cacheService.SaveToCacheAsync(
                    candidate.SourceItemId,
                    candidate.ImageIndex,
                    candidate.Tag,
                    bytes,
                    extension,
                    cancellationToken);

                if (savedPath is null)
                {
                    anyNonAspectFailure = true;
                    lastFailureReason = _localization.GetString("Image.Error.DiskFullOrPermission");
                    continue;
                }

                _cacheService.PruneCache(settings.MaxCacheSizeMb, settings.MaxCacheAgeDays);

                return new BackdropResolveResult
                {
                    Status = BackdropResolveStatus.Success,
                    LocalFilePath = savedPath,
                    ImageInfo = imageInfo,
                    Message = candidate.IsFromCollectionMember
                        ? _localization.Format(
                            "Image.Status.SavedCollectionMemberFormat",
                            BidiTextFormatter.Isolate(candidate.SourceItemName))
                        : _localization.GetString("Image.Status.Saved")
                };
            }
            catch (JellyfinApiException ex)
            {
                anyNonAspectFailure = true;
                _logger.Warning($"خطا در دریافت Backdrop برای {candidate.SourceItemName}: {ex.Message}");
                networkErrorOccurred = true;
                lastFailureReason = ex.Message;
                break;
            }
        }

        if (rejectedByAspectCount > 0 && !anyNonAspectFailure && !unauthenticated && !networkErrorOccurred)
        {
            var isolatedItemName = BidiTextFormatter.Isolate(item.Name);
            return new BackdropResolveResult
            {
                Status = BackdropResolveStatus.SkippedNoBackdrop,
                ImageInfo = lastImageInfo,
                Message = lastAspectReason is null
                    ? _localization.Format("Image.Aspect.AllRejectedFormat", isolatedItemName)
                    : _localization.Format("Image.Aspect.ItemRejectedWithReasonFormat", isolatedItemName, lastAspectReason)
            };
        }

        var firstCandidate = candidates[candidateIndices[0]];
        var fallbackImageInfo = lastImageInfo ?? new JellyfinImageInfo
        {
            ItemId = firstCandidate.SourceItemId,
            ImageIndex = firstCandidate.ImageIndex,
            Tag = firstCandidate.Tag,
            MaxWidth = DefaultMaxWidth,
            Quality = DefaultQuality,
            RequestUrl = firstCandidate.RequestUrl ?? item.BackdropImageUrl
        };

        if (unauthenticated || networkErrorOccurred)
        {
            return TryOfflineCacheOrFallback(
                item,
                knownMemberIds,
                fallbackImageInfo,
                settings,
                lastFailureReason ?? _localization.GetString("Image.Error.NoActiveConnection"));
        }

        return TryFallbackOrFailure(
            fallbackImageInfo,
            settings,
            lastFailureReason ?? _localization.GetString("Image.Error.ResolveFailed"));
    }

    internal async Task<IReadOnlyList<BackdropCandidate>> CollectBackdropCandidatesAsync(
        MediaItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (!item.IsCollection)
        {
            var direct = new List<BackdropCandidate>(item.BackdropImageTags.Count);
            for (var i = 0; i < item.BackdropImageTags.Count; i++)
            {
                direct.Add(new BackdropCandidate(
                    item.Id,
                    item.Name,
                    i,
                    item.BackdropImageTags[i],
                    item.BackdropImageUrl,
                    IsFromCollectionMember: false));
            }

            return direct;
        }

        var result = await CollectCollectionCandidatesInternalAsync(item, _settingsService.Current, cancellationToken);
        return result.Candidates;
    }

    private async Task<CollectionCandidateCollectionResult> CollectCollectionCandidatesInternalAsync(
        MediaItem rootCollection,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        var candidates = new List<BackdropCandidate>();
        var knownMemberIds = new List<string>();
        var visitedItemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { rootCollection.Id };
        var seenTags = new HashSet<string>(StringComparer.Ordinal);
        var networkErrorOccurred = false;
        string? failureReason = null;

        AddItemCandidates(rootCollection, isFromMember: false, candidates, seenTags);

        var queue = new Queue<(MediaItem Collection, int Depth)>();
        queue.Enqueue((rootCollection, 0));

        while (queue.Count > 0)
        {
            var (currentCollection, depth) = queue.Dequeue();
            if (depth >= MaxCollectionNestingDepth)
            {
                continue;
            }

            var (members, fetchError, errorReason) = await GetCollectionMembersAsync(currentCollection, settings, cancellationToken);
            if (fetchError)
            {
                networkErrorOccurred = true;
                failureReason ??= errorReason;
            }

            foreach (var member in members)
            {
                if (string.IsNullOrWhiteSpace(member.Id) || !visitedItemIds.Add(member.Id))
                {
                    continue;
                }

                knownMemberIds.Add(member.Id);
                AddItemCandidates(member, isFromMember: true, candidates, seenTags);

                if (member.IsCollection)
                {
                    queue.Enqueue((member, depth + 1));
                }
            }
        }

        return new CollectionCandidateCollectionResult(candidates, knownMemberIds, networkErrorOccurred, failureReason);
    }

    private async Task<(IReadOnlyList<MediaItem> Members, bool NetworkError, string? ErrorReason)> GetCollectionMembersAsync(
        MediaItem collection,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        if (collection.CollectionMembers.Count > 0)
        {
            _offlineCollectionMembersCache[collection.Id] = collection.CollectionMembers;
            return (collection.CollectionMembers, false, null);
        }

        var session = _authService.CurrentSession;
        if (!session.IsAuthenticated ||
            session.ServerUri is null ||
            string.IsNullOrWhiteSpace(session.AccessToken))
        {
            var cached = _offlineCollectionMembersCache.TryGetValue(collection.Id, out var offlineMembers)
                ? offlineMembers
                : [];
            return (cached, false, null);
        }

        try
        {
            var members = await _client.GetCollectionItemsAsync(
                session.ServerUri,
                session.UserId,
                session.AccessToken,
                settings.DeviceId,
                collection.Id,
                cancellationToken);

            _offlineCollectionMembersCache[collection.Id] = members;
            return (members, false, null);
        }
        catch (JellyfinApiException ex)
        {
            _logger.Warning($"خطا در دریافت اعضای کالکشن «{collection.Name}» ({collection.Id}): {ex.Message}");
            var cached = _offlineCollectionMembersCache.TryGetValue(collection.Id, out var offlineMembers)
                ? offlineMembers
                : [];
            return (cached, true, ex.Message);
        }
    }

    private static void AddItemCandidates(
        MediaItem sourceItem,
        bool isFromMember,
        List<BackdropCandidate> candidates,
        HashSet<string> seenTags)
    {
        for (var i = 0; i < sourceItem.BackdropImageTags.Count; i++)
        {
            var tag = sourceItem.BackdropImageTags[i];
            if (!string.IsNullOrWhiteSpace(tag) && !seenTags.Add(tag))
            {
                continue;
            }

            candidates.Add(new BackdropCandidate(
                sourceItem.Id,
                sourceItem.Name,
                i,
                tag,
                sourceItem.BackdropImageUrl,
                isFromMember));
        }
    }

    private static List<int> BuildCandidateIndices(int availableCount, AppSettings settings)
    {
        var primaryIndex = SelectBackdropIndex(availableCount, settings);
        var indices = new List<int>(availableCount) { primaryIndex };
        for (var i = 0; i < availableCount; i++)
        {
            if (i != primaryIndex)
            {
                indices.Add(i);
            }
        }

        return indices;
    }

    private static int SelectBackdropIndex(int availableCount, AppSettings settings)
    {
        if (availableCount <= 1)
        {
            return 0;
        }

        if (settings.RandomBackdropPerItem)
        {
            return Random.Shared.Next(availableCount);
        }

        return Math.Clamp(settings.BackdropIndexPreference, 0, availableCount - 1);
    }

    private BackdropResolveResult ResolveWithoutBackdrop(MediaItem item, AppSettings settings)
    {
        var isolatedName = BidiTextFormatter.Isolate(item.Name);
        if (ImageFormatValidator.IsValidImageFile(settings.FallbackImagePath))
        {
            return new BackdropResolveResult
            {
                Status = BackdropResolveStatus.UsedFallback,
                LocalFilePath = settings.FallbackImagePath,
                Message = _localization.Format("Image.NoBackdrop.UsedFallbackFormat", isolatedName)
            };
        }

        return new BackdropResolveResult
        {
            Status = BackdropResolveStatus.SkippedNoBackdrop,
            Message = _localization.Format("Image.NoBackdrop.SkippedFormat", isolatedName)
        };
    }

    private BackdropResolveResult TryOfflineCacheOrFallback(
        MediaItem item,
        IReadOnlyList<string> knownMemberIds,
        JellyfinImageInfo imageInfo,
        AppSettings settings,
        string reason)
    {
        var offlineCached = FindValidOfflineCachedFile(item.Id, settings);
        if (offlineCached is null && item.IsCollection)
        {
            foreach (var memberId in EnumerateKnownMemberIds(item, knownMemberIds))
            {
                offlineCached = FindValidOfflineCachedFile(memberId, settings);
                if (offlineCached is not null)
                {
                    break;
                }
            }
        }

        if (offlineCached is not null)
        {
            return new BackdropResolveResult
            {
                Status = BackdropResolveStatus.UsedCacheOffline,
                LocalFilePath = offlineCached,
                ImageInfo = imageInfo,
                Message = _localization.GetString("Image.Offline.UsedCache")
            };
        }

        return TryFallbackOrFailure(imageInfo, settings, reason);
    }

    private string? FindValidOfflineCachedFile(string itemId, AppSettings settings)
    {
        var cached = _cacheService.TryGetAnyCachedFileForItem(itemId);
        return cached is not null && ImageAspectFilter.IsAcceptableFile(cached, settings, out _)
            ? cached
            : null;
    }

    private IEnumerable<string> EnumerateKnownMemberIds(MediaItem rootItem, IReadOnlyList<string> discoveredMemberIds)
    {
        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { rootItem.Id };

        foreach (var memberId in discoveredMemberIds)
        {
            if (yielded.Add(memberId))
            {
                yield return memberId;
            }
        }

        foreach (var member in rootItem.CollectionMembers)
        {
            if (!string.IsNullOrWhiteSpace(member.Id) && yielded.Add(member.Id))
            {
                yield return member.Id;
            }
        }

        if (_offlineCollectionMembersCache.TryGetValue(rootItem.Id, out var cachedMembers))
        {
            foreach (var member in cachedMembers)
            {
                if (!string.IsNullOrWhiteSpace(member.Id) && yielded.Add(member.Id))
                {
                    yield return member.Id;
                }
            }
        }
    }

    private BackdropResolveResult TryFallbackOrFailure(
        JellyfinImageInfo imageInfo,
        AppSettings settings,
        string reason)
    {
        if (ImageFormatValidator.IsValidImageFile(settings.FallbackImagePath))
        {
            return new BackdropResolveResult
            {
                Status = BackdropResolveStatus.UsedFallback,
                LocalFilePath = settings.FallbackImagePath,
                ImageInfo = imageInfo,
                Message = _localization.Format("Image.Fallback.ReasonWithFallbackFormat", reason)
            };
        }

        return new BackdropResolveResult
        {
            Status = BackdropResolveStatus.FailedCorruptOrIo,
            ImageInfo = imageInfo,
            Message = reason
        };
    }

    private static bool IsDuplicateFileContent(string filePath, HashSet<string> rejectedHashes)
    {
        if (rejectedHashes.Count == 0)
        {
            return false;
        }

        try
        {
            var bytes = File.ReadAllBytes(filePath);
            return rejectedHashes.Contains(ComputeContentHash(bytes));
        }
        catch
        {
            return false;
        }
    }

    private static void RecordRejectedFileHash(string filePath, HashSet<string> rejectedHashes)
    {
        try
        {
            var bytes = File.ReadAllBytes(filePath);
            rejectedHashes.Add(ComputeContentHash(bytes));
        }
        catch
        {
            // Best-effort deduplication
        }
    }

    private static string ComputeContentHash(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes));

    internal readonly record struct BackdropCandidate(
        string SourceItemId,
        string SourceItemName,
        int ImageIndex,
        string Tag,
        string? RequestUrl,
        bool IsFromCollectionMember);

    private readonly record struct CollectionCandidateCollectionResult(
        List<BackdropCandidate> Candidates,
        List<string> KnownMemberIds,
        bool NetworkErrorOccurred,
        string? FailureReason);
}
