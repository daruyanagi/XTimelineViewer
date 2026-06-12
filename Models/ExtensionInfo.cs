namespace XTimelineViewer.Models
{
    internal record ExtensionInfo(
        string Name,
        string DirectoryPath,
        string? IconPath,
        string? OptionsPage,
        string? HomepageUrl,
        string? ExtensionId
    );
}
