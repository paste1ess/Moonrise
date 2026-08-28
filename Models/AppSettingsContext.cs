using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;
using Moonrise.Services;

[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(ElementTheme))]
internal partial class AppSettingsContext : JsonSerializerContext { }