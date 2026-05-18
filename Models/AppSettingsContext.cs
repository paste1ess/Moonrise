using System.Text.Json.Serialization;
using Moonrise.Services;

[JsonSerializable(typeof(AppSettings))]
internal partial class AppSettingsContext : JsonSerializerContext { }