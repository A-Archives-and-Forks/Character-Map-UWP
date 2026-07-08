using CommunityToolkit.Mvvm.Input;
using Microsoft.Collections.Extensions;
using System.Collections.Specialized;
using System.ComponentModel;
using Windows.UI.Xaml.Media;

namespace CharacterMap.ViewModels;

public class SubsetterArgs
{
    public bool MDL2FluentOnly { get; set; } = true;
}

public record class CollectionChangedMessage(object Sender, NotifyCollectionChangedEventArgs Args);

public class FamilySelectionModel(CMFontFamily family, IMessenger messenger)
{
    public List<FaceSelectionModel> Faces => field
        ??= [..Family.NonSimulatedVariants.OfType<CMFontFace>().Select(
            v => new FaceSelectionModel(this, v, messenger))];

    public FaceSelectionModel Default => field ??= Faces.Where(f => f.Face == Family.DefaultVariant).FirstOrDefault();

    public CMFontFamily Family { get; } = family;

    public bool CanSelectFace => Faces?.Count > 1;

    private FamilySelectionModel This => this;
}

public partial class FaceSelectionModel : ObservableObject
{
    private readonly IMessenger _messenger;

    [ObservableProperty]
    ObservableCollection<Character> _selectedCharacters = new();

    public FamilySelectionModel Family { get; }

    public CMFontFace Face { get; }

    public FaceSelectionModel(FamilySelectionModel family, CMFontFace face, IMessenger messenger)
    {
        Family = family;
        Face = face;
        _messenger = messenger;

        // Notify when our selection changes
        SelectedCharacters.CollectionChanged += SelectedCharacters_CollectionChanged;
    }

    private void SelectedCharacters_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        _messenger.Send(new CollectionChangedMessage(this, e));
    }

    public void SelectAll()
    {
        SelectedCharacters.CollectionChanged -= SelectedCharacters_CollectionChanged;
        SelectedCharacters = [.. Face.Characters];
        SelectedCharacters.CollectionChanged += SelectedCharacters_CollectionChanged;
        _messenger.Send(new CollectionChangedMessage(this, null));
    }
}


public partial class SubsetterViewModel : ViewModelBase
{
    public const string EDIT_STATE = "EditingState";
    public const string PREVIEW_STATE = "PreviewingState";
    public const string EXPORT_STATE = "ExportState";

    public StrongReferenceMessenger StrongMessenger { get; } = new();

    public ObservableCollection<FamilySelectionModel> Families { get; }

    public ObservableCollection<FaceSelectionModel> SelectedFaces { get; } = new();

    public bool IsPreviewable => SelectedFaces.Count > 0 && !string.IsNullOrWhiteSpace(FamilyName);
    public bool IsExportable => IsPreviewable && !HasClashing;

    [ObservableProperty] FamilySelectionModel _selectedFamily;
    [ObservableProperty] FaceSelectionModel _selectedFace;
    [ObservableProperty] FontFamily _selectedXAMLFontFamily;
    [ObservableProperty] string _familyName = "Segoe Icons Subset";
    [ObservableProperty] string _version = "Version 1.00";
    [ObservableProperty] bool _hasClashing = false;

    [ObservableProperty] ObservableCollection<FontGlyph> _previewList;

    HashSet<int> _selectedIndexes { get; } = new();

    /// <summary>
    /// Unicode indexes that appear more than once in <see cref="PreviewList"/>,
    /// meaning two glyphs from different source fonts share the same codepoint
    /// and will clash in the output font.
    /// </summary>
    [ObservableProperty] HashSet<uint> _clashingIndexes = [];

    public SubsetterViewModel(SubsetterArgs args)
    {
        ViewState = EDIT_STATE;

        Families = [..(args.MDL2FluentOnly
            ? FontFinder.Fonts.Where(f => f.Name.Contains("MDL2", StringComparison.InvariantCultureIgnoreCase) ||
                f.Name.Contains("Fluent", StringComparison.InvariantCultureIgnoreCase)).ToList()
            : FontFinder.Fonts).Select(f => new FamilySelectionModel(f, StrongMessenger))];

        SelectedFamily = Families.FirstOrDefault();

        StrongMessenger.Register<CollectionChangedMessage>(this, (o, msg) =>
        {
            if (msg.Sender is not FaceSelectionModel face)
                return;

            if (face.SelectedCharacters.Count > 0)
            {
                if (SelectedFaces.Contains(face) is false)
                    SelectedFaces.Add(face);
            }
            else
                SelectedFaces.Remove(face);

            OnPropertyChanged(nameof(IsPreviewable));
            OnPropertyChanged(nameof(IsExportable));
            _debouncer.Debounce(100, UpdateClashing);
        });
    }

    Debouncer _debouncer = new();

    private void UpdateClashing()
    {
        // Build a map of UnicodeIndex → how many different FaceSelectionModels selected it.
        // Any index appearing in more than one face's selection is a clash.
        MultiValueDictionary<uint, Character> dic = new();
        foreach (FaceSelectionModel face in SelectedFaces)
            foreach (Character c in face.SelectedCharacters)
                dic.Add(c.UnicodeIndex, c);

        ClashingIndexes = [..dic
                .Where(kvp => kvp.Value.Count > 1)
                .Select(kvp => kvp.Key)];

        HasClashing = ClashingIndexes.Count > 0;
        OnPropertyChanged(nameof(IsExportable));
    }

    bool _blockFace = false;

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName == nameof(FamilyName))
            OnPropertyChanged(nameof(IsPreviewable));

        if (e.PropertyName == nameof(SelectedFamily) && !_blockFace)
            SelectedFace = SelectedFamily?.Default;

        if (e.PropertyName == nameof(SelectedFace))
            SelectedXAMLFontFamily = SelectedFace == null ? null : new FontFamily(SelectedFace.Face.Source);
    }

    [RelayCommand]
    public void GoBack()
    {
        if (ViewState == PREVIEW_STATE)
            ViewState = EDIT_STATE;
    }

    [RelayCommand]
    void ShowPreview()
    {
        UpdateClashing();

        ObservableCollection<FontGlyph> list = [..SelectedFaces
            .SelectMany(sf => sf.SelectedCharacters.Select(c => new FontGlyph(sf.Face, c)))
            .OrderBy(fg => fg.Character.UnicodeIndex)];

        //foreach (var item in list)
        //{
        //    if (ClashingIndexes.Contains(item.Character.UnicodeIndex))
        //        item.IsClashing = true;
        //}

        PreviewList = list;
        ViewState = PREVIEW_STATE;
    }

    [RelayCommand]
    void ClearSelection() => SelectedFace?.SelectedCharacters.Clear();

    [RelayCommand]
    void SelectAll() => SelectedFace?.SelectAll();

    [RelayCommand]
    async Task OpenAsync()
    {
        if (await StorageHelper.PickOpenFileAsync(
                   FontImporter.ImportFormats.Where(f => !f.Equals(".zip", StringComparison.InvariantCultureIgnoreCase)),
                   Localization.Get("OpenFontPickerConfirm")) is StorageFile file)
        {
            if (await FontImporter.LoadFromFileAsync(file) is CMFontFamily font)
            {
                FamilySelectionModel family = new(font, StrongMessenger);
                Families.Add(family);
                SelectedFamily = family;
            }
            else
            {
                // TODO: Show error
            }
        }
    }

    [RelayCommand]
    void RemoveGlyph(FontGlyph glyph)
    {
        PreviewList.Remove(glyph);
        SelectedFaces.First(f => glyph.FontFace == f.Face).SelectedCharacters.Remove(glyph.Character);

        if (HasClashing)
            UpdateClashing();
    }

    [RelayCommand]
    void SetListItem(object e)
    {
        if (e is FaceSelectionModel face)
        {
            _blockFace = true;
            SelectedFamily = face.Family;
            SelectedFace = face;
            _blockFace = false;
        }
    }

    [RelayCommand]
    private async Task SubsetAsync()
    {
        var sourceState = ViewState;
        ViewState = $"Export{sourceState}";

        try
        {
            string fontName = FamilyName;
            string version = Version;

            // 1. Choose a file
            if (await StorageHelper.PickSaveFileAsync(fontName, Localization.Get("ExportFontFile/Text"), new[] { ".ttf" }, PickerLocationId.DocumentsLibrary)
                is not StorageFile target)
                return;

            var chars = SelectedFaces.SelectMany(sf => sf.SelectedCharacters.Select(c => new FontGlyph(sf.Face, c))).ToList();

            // Note: version string currently isn't supported by the subsetter table-rewritter
            var file = await FontSubsetter.CreateSubsetAsync(new(fontName, chars, target, version));
            if (file is not null && await FontImporter.LoadFromFileAsync(file) is CMFontFamily font)
            {
                // TODO: we should actually show an in-app notification with buttons "Show in folder" and "Open".
                //       "Open"" should call CreateNewViewForFontAsync like below
                //await FontMapView.CreateNewViewForFontAsync(font, file);
                Notify(new SubsetResultMessage(font, file));
            }
            else
            {
                // Send null, meaning there was some error
                Notify(new SubsetResultMessage(null, file));
            }

        }
        finally
        {
            ViewState = sourceState;
        }
    }
}
