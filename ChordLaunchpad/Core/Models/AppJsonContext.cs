using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ChordLaunchpad.Core.Models;

/// <summary>
/// System.Text.Json コンパイル時シリアライザーコンテキスト。
/// リフレクションを排除し、AOT・トリミング環境での安全性と高速なシリアライズを提供します。
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ProjectData))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(List<ProgressionTemplate>))]
public partial class AppJsonContext : JsonSerializerContext
{
}
