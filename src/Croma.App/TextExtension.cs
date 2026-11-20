using System.Windows.Markup;
using FolderHue.App.Services;

namespace FolderHue.App;

/// <summary>XAML helper: <c>Text="{loc:Text PickColor}"</c> resolves the active language.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TextExtension : MarkupExtension
{
    public string Key { get; set; } = "";

    public TextExtension() { }
    public TextExtension(string key) => Key = key;

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Key);
}
