using CharacterMapCX.Controls;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Toolkit.Uwp.UI.Controls;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using System.Xml.Linq;
using Windows.UI.Core;
using Windows.UI.Input.Inking;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Hosting;
using Windows.UI.Xaml.Markup;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;

namespace CharacterMap.Views;

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
        SelectedCharacters = [..Face.Characters];
        SelectedCharacters.CollectionChanged += SelectedCharacters_CollectionChanged;
        _messenger.Send(new CollectionChangedMessage(this, null));
    }
}

public partial class SubsetterViewModel : ViewModelBase
{
    StrongReferenceMessenger _messenger { get; } = new();

    public ObservableCollection<FamilySelectionModel> Families { get; }

    public ObservableCollection<FaceSelectionModel> SelectedFaces { get; } = new();

    [ObservableProperty] FamilySelectionModel _selectedFamily;
    [ObservableProperty] FaceSelectionModel _selectedFace;
    [ObservableProperty] FontFamily _selectedXAMLFontFamily;
    [ObservableProperty] string _familyName = "Segoe Icons Subset";
    [ObservableProperty] string _version = "Version 1.00";

    public SubsetterViewModel(SubsetterArgs args)
    {
        Families = [..(args.MDL2FluentOnly
            ? FontFinder.Fonts.Where(f => f.Name.Contains("MDL2", StringComparison.InvariantCultureIgnoreCase) ||
                f.Name.Contains("Fluent", StringComparison.InvariantCultureIgnoreCase)).ToList()
            : FontFinder.Fonts).Select(f => new FamilySelectionModel(f, _messenger))];

        SelectedFamily = Families.FirstOrDefault();

        _messenger.Register<CollectionChangedMessage>(this, (o, msg) =>
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
        });
    }

    bool _blockFace = false;

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName == nameof(SelectedFamily) && !_blockFace)
            SelectedFace = SelectedFamily?.Default;

        if (e.PropertyName == nameof(SelectedFace))
            SelectedXAMLFontFamily = SelectedFace == null ? null : new FontFamily(SelectedFace.Face.Source);
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
                FamilySelectionModel family = new(font, _messenger);
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
}

public sealed partial class SubsetterView : ViewBase, IInAppNotificationPresenter
{
    public SubsetterViewModel ViewModel { get; }

    public SubsetterView() : this(new()) { }

    private NavigationHelper _navHelper { get; } = new();

    public SubsetterView(SubsetterArgs args)
    {
        this.InitializeComponent();

        ViewModel = new SubsetterViewModel(args);
        this.DataContext = this;
    }

    protected override void OnLoaded(object sender, RoutedEventArgs e)
    {
        VisualStateManager.GoToState(this, "NormalState", false);
        VisualStateManager.GoToState(this, "OverlayState", false);

        TitleBarHelper.SetTitle(Presenter.Title);

        Register<AppNotificationMessage>(OnNotificationMessage);

        // Pre-create element visuals to ensure animations run
        // properly when requested later
        PresentationRoot.GetElementVisual();

        AnimateIn();
    }




    /* Notification Helpers */

    public InAppNotification GetNotifier()
    {
        if (NotificationRoot == null)
            this.FindName(nameof(NotificationRoot));

        return DefaultNotification;
    }

    void OnNotificationMessage(AppNotificationMessage msg)
    {
        if (!Dispatcher.HasThreadAccess)
            return;

        InAppNotificationHelper.OnMessage(this, msg);
    }




    /* ANIMATION HELPERS */

    #region Animation

    /// <summary>
    /// Animates the page in on first load
    /// </summary>
    private void AnimateIn()
    {
        ContentRoot.Opacity = 1;
        if (ResourceHelper.AllowAnimation is false)
            return;

        int s = 100;
        int o = 110;

        // Title
        CompositionFactory.PlayEntrance(Presenter.GetTitleContainerElement(), s + 30, o);

        // First Row
        CompositionFactory.PlayEntrance(ButtonsPanel, s + 113, o);
        //CompositionFactory.PlayEntrance(InputContainer, s + 113, o);
        //CompositionFactory.PlayEntrance(SliderContainer, s + 113, o);

        // Second Row
        CompositionFactory.PlayEntrance(ContentGrid, s + 200, o);

        // Third Row
        CompositionFactory.PlayEntrance(PresentationRoot, s + 300, o);
    }

    //ConnectedAnimation _addHistoryAnim;


    #endregion




    /* VISUAL STATE HELPERS */

    #region State Helpers



    #endregion

}





public partial class SubsetterView
{
    public static void CreateDefault()
    {
        _ = CreateWindowAsync(new());
    }

    public static async Task<WindowInformation> CreateWindowAsync(SubsetterArgs args)
    {
        static void CreateView(SubsetterArgs a)
        {
            SubsetterView view = new(a);
            Window.Current.Content = view;
            Window.Current.Activate();
        }

        var view = await WindowService.CreateViewAsync(() => CreateView(args), false);
        await WindowService.TrySwitchToWindowAsync(view, false);
        return view;
    }
}
