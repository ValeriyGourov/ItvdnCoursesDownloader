using System;
using System.Collections.Generic;

namespace Downloader.Models;

/// <summary>
/// Определение списка видео, извлекаемое из конфигурации на странице курса.
/// </summary>
internal sealed record VideoListDefinition(VideoRequest Request);

internal sealed record VideoRequest(VideoFile Files);

internal sealed record VideoFile(IEnumerable<ProgressiveItem> Progressive);

internal sealed record ProgressiveItem(int Profile, Uri Url, string Quality);
