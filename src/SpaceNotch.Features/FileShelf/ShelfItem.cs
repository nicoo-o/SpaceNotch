using System;

namespace SpaceNotch.Features.FileShelf;

public record ShelfItem(
    string Id,
    string FilePath,
    string FileName,
    long FileSizeBytes,
    DateTime AddedAt
)
{
    public string FormattedSize
    {
        get
        {
            if (FileSizeBytes < 1024) return $"{FileSizeBytes} B";
            if (FileSizeBytes < 1024 * 1024) return $"{FileSizeBytes / 1024.0:F1} KB";
            return $"{FileSizeBytes / (1024.0 * 1024.0):F1} MB";
        }
    }
}
