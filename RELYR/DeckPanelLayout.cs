using System.Windows;
using System.Windows.Controls;

namespace RELYR;

internal static class DeckPanelLayout
{
    internal const string Layer="Deck";
    internal const int Rows=5;
    internal const int Columns=9;
    internal const int SlotCount=Rows*Columns;
    internal const double KeyWidth=54;
    internal const double KeyHeight=52;
    internal const double Gap=4;

    internal static string InputName(int slot)=>$"{Layer}+{slot:00}";

    internal static bool IsInputName(string? input)
    {
        if(string.IsNullOrWhiteSpace(input)||!input.StartsWith(Layer+"+",StringComparison.OrdinalIgnoreCase))return false;
        return int.TryParse(input[(Layer.Length+1)..],out int slot)&&slot is >=1 and <=SlotCount;
    }

    internal static int SlotNumber(string input)=>
        IsInputName(input)&&int.TryParse(input[(Layer.Length+1)..],out int slot)?slot:0;

    internal static Mapping? FindMapping(AppConfig config,int slot)
    {
        string input=InputName(slot);
        if(config.UseSharedDeckPanel)
            return config.SharedDeckMappings.LastOrDefault(x=>x.Input.Equals(input,StringComparison.OrdinalIgnoreCase));
        if(config.Profiles.Count==0)return null;
        var profile=config.Profiles.FirstOrDefault(x=>x.Name==config.ActiveProfile)??config.Profiles[0];
        return profile.Mappings.LastOrDefault(x=>x.Input.Equals(input,StringComparison.OrdinalIgnoreCase));
    }

    internal static IReadOnlyList<Profile> ProfilesWithDeckMappings(IEnumerable<Profile> profiles)=>profiles
        .Where(profile=>profile.Mappings.Any(map=>IsInputName(map.Input)))
        .ToList();

    internal static int DistinctDeckCount(IEnumerable<Profile> profiles)=>profiles
        .Select(profile=>DeckSignature(profile.Mappings))
        .Distinct(StringComparer.Ordinal)
        .Count();

    static string DeckSignature(IEnumerable<Mapping> mappings)=>string.Join("\n",mappings
        .Where(map=>IsInputName(map.Input))
        .OrderBy(map=>SlotNumber(map.Input))
        .Select(map=>$"{map.Input}\u001f{map.Kind}\u001f{map.Value}\u001f{map.LongPressKind}\u001f{map.LongPressValue}\u001f{map.LongPressMs}\u001f{map.Application}\u001f{map.Description}"));

    internal static string ActionLabel(string input,Mapping? mapping)
    {
        int slot=SlotNumber(input);
        string action=MainWindow.MappingInterceptsInput(mapping)
            ?MainWindow.FriendlyActionValue(mapping!.Kind,mapping.Value)
            :slot.ToString("00");
        if(action.Length>10)action=action[..9]+"…";
        return action;
    }

    internal static TextBlock CreateNameLabel(Mapping? mapping)
    {
        var label=new TextBlock
        {
            Text=mapping?.Description??"",
            Height=14,
            FontSize=9,
            Margin=new Thickness(1,1,1,0),
            TextAlignment=TextAlignment.Center,
            TextTrimming=TextTrimming.CharacterEllipsis,
            VerticalAlignment=VerticalAlignment.Top,
            IsHitTestVisible=false
        };
        label.SetResourceReference(TextBlock.ForegroundProperty,"SecondaryText");
        return label;
    }

    internal static FrameworkElement CreateButtonContent(string input,Mapping? mapping)=>new TextBlock
        {
            Text=ActionLabel(input,mapping),
            FontSize=11,
            FontWeight=FontWeights.SemiBold,
            TextAlignment=TextAlignment.Center,
            TextTrimming=TextTrimming.CharacterEllipsis
        };
}
