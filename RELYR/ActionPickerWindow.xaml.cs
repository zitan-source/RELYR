using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RELYR;

public partial class ActionPickerWindow:Window
{
    readonly List<CatalogAction> allActions;
    readonly string keyboardLayout;
    List<CatalogAction> filtered=[];
    CatalogAction? result;
    MacroInputPickerWindow? keypad;

    public CatalogAction? SelectedAction=>result??ActionList.SelectedItem as CatalogAction;
    internal bool TitleBarUsesDarkMode{get;private set;}
    internal IReadOnlyList<CatalogAction> ActionsForTest=>allActions;

    public ActionPickerWindow(IEnumerable<Profile>? profiles=null,string keyboardLayout="JIS")
    {
        InitializeComponent();
        this.keyboardLayout=keyboardLayout;
        allActions=[
            new CatalogAction("割り当て","無効化","元の入力も実行せず、この割り当てを無効にします",ActionKind.Disabled,""),
            ..ActionCatalog.Items
        ];
        MainWindow.FollowWindowsTitleBarTheme(this,value=>TitleBarUsesDarkMode=value);
        RefreshSearch();
    }

    void SearchChanged(object sender,TextChangedEventArgs e)=>RefreshSearch();
    void RefreshSearch()
    {
        string? selectedMajor=MajorCategoryList.SelectedItem as string;
        string text=SearchBox?.Text.Trim()??"";
        filtered=(text.Length==0?allActions:allActions.Where(x=>new[]{x.MajorCategory,x.Category,x.Name,x.Description,x.Value}.Any(value=>value.Contains(text,StringComparison.OrdinalIgnoreCase)))).ToList();
        var majors=filtered.Select(x=>x.MajorCategory).Distinct().ToList();
        MajorCategoryList.ItemsSource=majors;
        MajorCategoryList.SelectedItem=selectedMajor!=null&&majors.Contains(selectedMajor)?selectedMajor:majors.FirstOrDefault();
        if(MajorCategoryList.SelectedItem==null){CategoryList.ItemsSource=null;ActionList.ItemsSource=null;ResultCount.Text="0件";}
    }

    void MajorCategoryChanged(object sender,SelectionChangedEventArgs e)
    {
        string? selectedCategory=CategoryList.SelectedItem as string;
        if(MajorCategoryList.SelectedItem is not string major){CategoryList.ItemsSource=null;ActionList.ItemsSource=null;return;}
        var categories=filtered.Where(x=>x.MajorCategory==major).Select(x=>x.Category).Distinct().ToList();
        CategoryList.ItemsSource=categories;
        CategoryList.SelectedItem=selectedCategory!=null&&categories.Contains(selectedCategory)?selectedCategory:categories.FirstOrDefault();
    }

    void CategoryChanged(object sender,SelectionChangedEventArgs e)
    {
        if(MajorCategoryList.SelectedItem is not string major||CategoryList.SelectedItem is not string category){ActionList.ItemsSource=null;ResultCount.Text="0件";return;}
        var actions=filtered.Where(x=>x.MajorCategory==major&&x.Category==category).ToList();
        ActionList.ItemsSource=actions;ActionList.SelectedIndex=-1;ResultCount.Text=$"{actions.Count}件";
    }

    void ActionChanged(object sender,SelectionChangedEventArgs e)
    {
        SelectButton.IsEnabled=ActionList.SelectedItem is CatalogAction;
        SelectedDescription.Text=(ActionList.SelectedItem as CatalogAction)?.Description??"";
    }

    void ActionDoubleClick(object sender,MouseButtonEventArgs e){if(ActionList.SelectedItem is CatalogAction action)Accept(action);}
    void Select_Click(object sender,RoutedEventArgs e){if(ActionList.SelectedItem is CatalogAction action)Accept(action);}
    void Accept(CatalogAction action){result=action;DialogResult=true;Close();}

    void OpenKeypad_Click(object sender,RoutedEventArgs e)
    {
        if(keypad is {IsVisible:true}){keypad.Activate();return;}
        keypad=new MacroInputPickerWindow(keyboardLayout){Owner=this};
        keypad.ConfigureShortcutEditing(CustomShortcutBox.Text);
        keypad.ShortcutChanged+=value=>
        {
            if(CustomShortcutBox.Text==value)return;
            CustomShortcutBox.Text=value;
            CustomShortcutBox.CaretIndex=CustomShortcutBox.Text.Length;
        };
        keypad.Closed+=(_,_)=>keypad=null;
        keypad.Show();
    }

    void AppendShortcutPart(string input)
    {
        string token=NormalizeShortcutToken(input);
        CustomShortcutBox.Text=AddShortcutPart(CustomShortcutBox.Text,token);
        CustomShortcutBox.CaretIndex=CustomShortcutBox.Text.Length;
        keypad?.SetShortcutPreview(token,CustomShortcutBox.Text);
    }

    internal static string NormalizeShortcutToken(string input)=>input switch
        {
            "LeftCtrl" or "RightCtrl"=>"Ctrl",
            "LeftShift" or "RightShift"=>"Shift",
            "LeftAlt" or "RightAlt"=>"Alt",
            "LWin" or "RWin"=>"Win",
            _=>input
        };

    internal static string AddShortcutPart(string current,string input)
    {
        string token=NormalizeShortcutToken(input);
        var parts=current.Split('+',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).ToList();
        if(!parts.Contains(token,StringComparer.OrdinalIgnoreCase))parts.Add(token);
        return string.Join("+",parts);
    }

    void CustomShortcutChanged(object sender,TextChangedEventArgs e)
    {
        UseShortcutButton.IsEnabled=!string.IsNullOrWhiteSpace(CustomShortcutBox.Text);
        keypad?.SetShortcutValue(CustomShortcutBox.Text);
    }
    void ClearShortcut_Click(object sender,RoutedEventArgs e)=>CustomShortcutBox.Clear();
    void UseShortcut_Click(object sender,RoutedEventArgs e)
    {
        string value=CustomShortcutBox.Text.Trim();
        if(value.Length>0)Accept(new CatalogAction("任意のショートカット","任意のショートカット","キーパッドまたは手入力で指定したキー操作です",ActionKind.Shortcut,value));
    }
    void Cancel_Click(object sender,RoutedEventArgs e){DialogResult=false;Close();}
    protected override void OnClosed(EventArgs e){keypad?.Close();base.OnClosed(e);}
}
