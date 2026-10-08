using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dlt645.Core.Protocol;

namespace Dlt645.Core.DataItems;

/// <summary>DI 在配置表中的匹配结果。</summary>
public sealed record CatalogMatch(DataItemDefinition Item, int Tariff, int History)
{
    public string Describe()
    {
        var extra = new List<string>();
        if (Tariff >= 0) extra.Add(Tariffs.Name(Tariff));
        var h = Item.FormatHistory(History);
        if (h.Length > 0) extra.Add(h);
        return extra.Count == 0 ? Item.Name : $"{Item.Name}·{string.Join("·", extra)}";
    }
}

/// <summary>
/// 数据项配置表。默认使用内置的 DataItems.json；
/// 若程序目录下存在 DataItems.json，则优先加载该文件，现场可直接增改数据项而无需重新编译。
/// </summary>
public sealed class DataItemCatalog
{
    public const string FileName = "DataItems.json";

    private DataItemCatalog(IReadOnlyList<DataItemDefinition> items, IReadOnlyList<string> categories, string source)
    {
        Items = items;
        Categories = categories;
        Source = source;
    }

    public IReadOnlyList<DataItemDefinition> Items { get; }
    public IReadOnlyList<string> Categories { get; }
    /// <summary>配置来源（“内置配置”或文件路径）。</summary>
    public string Source { get; }

    public IEnumerable<DataItemDefinition> ByCategory(string category) =>
        Items.Where(i => i.Category == category);

    public IEnumerable<DataItemDefinition> CommonItems => Items.Where(i => i.Common);

    public CatalogMatch? Find(uint di)
    {
        // 优先精确匹配（无费率、无历史的项），再按模板匹配
        foreach (var item in Items)
        {
            if (!item.Tariffs && item.History == HistoryKind.None && item.Di == di)
                return new CatalogMatch(item, -1, -1);
        }
        foreach (var item in Items)
        {
            if ((item.Tariffs || item.History != HistoryKind.None) && item.TryMatch(di, out var t, out var h))
                return new CatalogMatch(item, t, h);
        }
        return null;
    }

    public string? DescribeDi(uint di) => Find(di)?.Describe();

    /// <summary>加载配置：程序目录下的文件优先，失败时回退到内置配置并通过 <paramref name="warning"/> 告知。</summary>
    public static DataItemCatalog Load(string? overrideDirectory, out string? warning)
    {
        warning = null;
        if (!string.IsNullOrEmpty(overrideDirectory))
        {
            var path = Path.Combine(overrideDirectory, FileName);
            if (File.Exists(path))
            {
                try
                {
                    return LoadFromJson(File.ReadAllText(path), path);
                }
                catch (Exception ex)
                {
                    warning = $"外部配置文件 {path} 加载失败，已使用内置配置：{ex.Message}";
                }
            }
        }
        return LoadBuiltIn();
    }

    public static DataItemCatalog LoadBuiltIn() => LoadFromJson(ReadBuiltInJson(), "内置配置");

    public static string ReadBuiltInJson()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Dlt645.Core.DataItems.json")
                           ?? throw new InvalidOperationException("缺少内置数据项配置");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static DataItemCatalog LoadFromJson(string json, string source)
    {
        var doc = JsonSerializer.Deserialize<CatalogJson>(json, JsonOptions)
                  ?? throw new FormatException("配置表为空");
        var items = new List<DataItemDefinition>();
        var categories = new List<string>();
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var cat in doc.Categories)
        {
            if (string.IsNullOrWhiteSpace(cat.Name)) throw new FormatException("存在未命名的分类");
            categories.Add(cat.Name);
            foreach (var it in cat.Items)
            {
                string where = $"“{cat.Name} / {it.Name}”";
                if (string.IsNullOrWhiteSpace(it.Name)) throw new FormatException($"分类 {cat.Name} 中存在未命名的数据项");
                if (!DataId.TryParse(it.Di, out var di, out var diError)) throw new FormatException($"{where} 的 DI 无效：{diError}");

                var fields = new List<FieldDefinition>();
                try
                {
                    if (it.Fields is { Count: > 0 })
                    {
                        foreach (var f in it.Fields)
                            fields.Add(new FieldDefinition(f.Name ?? string.Empty, f.Format ?? string.Empty, f.Unit, f.Signed, f.Sample));
                    }
                    else
                    {
                        fields.Add(new FieldDefinition(string.Empty, it.Format ?? string.Empty, it.Unit, it.Signed, it.Sample));
                    }
                }
                catch (FormatException ex)
                {
                    throw new FormatException($"{where} 的格式无效：{ex.Message}");
                }

                var id = string.IsNullOrWhiteSpace(it.Id) ? $"{cat.Name}.{DataId.Format(di)}" : it.Id!;
                if (!ids.Add(id)) throw new FormatException($"数据项 ID 重复：{id}");

                int max = it.HistoryMax ?? (it.History == HistoryKind.Month ? 12 : it.History == HistoryKind.Times ? 10 : 0);
                string label = it.HistoryLabel ?? (it.History == HistoryKind.Month ? "上{0}月" : "上{0}次");

                items.Add(new DataItemDefinition
                {
                    Id = id,
                    Name = it.Name!,
                    Category = cat.Name,
                    Group = it.Group ?? string.Empty,
                    Di = di,
                    Fields = fields,
                    Tariffs = it.Tariffs,
                    History = it.History,
                    HistoryMax = max,
                    HistoryLabel = label,
                    Common = it.Common,
                    CommonTariffs = it.CommonTariffs is { Count: > 0 } ? it.CommonTariffs : new[] { 0 },
                    Note = it.Note ?? string.Empty,
                });
            }
        }
        return new DataItemCatalog(items, categories, source);
    }

    // ---- JSON 结构 ----
    private sealed class CatalogJson
    {
        public int Version { get; set; }
        public List<CategoryJson> Categories { get; set; } = new();
    }

    private sealed class CategoryJson
    {
        public string Name { get; set; } = string.Empty;
        public List<ItemJson> Items { get; set; } = new();
    }

    private sealed class ItemJson
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Group { get; set; }
        public string? Di { get; set; }
        public string? Format { get; set; }
        public string? Unit { get; set; }
        public bool Signed { get; set; }
        public string? Sample { get; set; }
        public List<FieldJson>? Fields { get; set; }
        public bool Tariffs { get; set; }
        public HistoryKind History { get; set; }
        public int? HistoryMax { get; set; }
        public string? HistoryLabel { get; set; }
        public bool Common { get; set; }
        public List<int>? CommonTariffs { get; set; }
        public string? Note { get; set; }
    }

    private sealed class FieldJson
    {
        public string? Name { get; set; }
        public string? Format { get; set; }
        public string? Unit { get; set; }
        public bool Signed { get; set; }
        public string? Sample { get; set; }
    }
}
