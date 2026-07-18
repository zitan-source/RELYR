using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RELYR;

public partial class ActionPickerWindow:Window
{
    List<CatalogAction> filtered=[];
    public CatalogAction? SelectedAction=>ActionList.SelectedItem as CatalogAction;
    internal bool TitleBarUsesDarkMode{get;private set;}

    public ActionPickerWindow()
    {
        InitializeComponent();
        MainWindow.FollowWindowsTitleBarTheme(this,value=>TitleBarUsesDarkMode=value);
        RefreshSearch();
    }
    void SearchChanged(object sender,TextChangedEventArgs e)=>RefreshSearch();
    void RefreshSearch()
    {
        string? selectedMajor=MajorCategoryList.SelectedItem as string;
        filtered=ActionCatalog.Search(SearchBox?.Text).ToList();
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
    void ActionChanged(object sender,SelectionChangedEventArgs e){SelectButton.IsEnabled=SelectedAction!=null;SelectedDescription.Text=SelectedAction?.Description??"";}
    void ActionDoubleClick(object sender,MouseButtonEventArgs e){if(SelectedAction!=null){DialogResult=true;Close();}}
    void Select_Click(object sender,RoutedEventArgs e){if(SelectedAction!=null){DialogResult=true;Close();}}
    void Cancel_Click(object sender,RoutedEventArgs e){DialogResult=false;Close();}
}
