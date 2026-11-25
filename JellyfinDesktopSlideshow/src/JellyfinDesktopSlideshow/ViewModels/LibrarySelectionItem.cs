namespace JellyfinDesktopSlideshow.ViewModels;

public sealed class LibrarySelectionItem : ViewModelBase
{
    private bool _isSelected;

    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? CollectionType { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
