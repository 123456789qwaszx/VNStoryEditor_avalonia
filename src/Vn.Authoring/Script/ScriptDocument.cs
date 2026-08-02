using Vn.Authoring.Model;

namespace Vn.Authoring.Script;

/// <summary>
/// 한 언어에서의 화자와 대사. 값 타입이므로 두 곳에서 같은 인스턴스를 들고 있어도
/// 한쪽만 바뀌는 일이 없다.
/// </summary>
public sealed record LocalizedLine(string Speaker, string Text)
{
    public static LocalizedLine Empty { get; } = new(string.Empty, string.Empty);
}

/// <summary>
/// 대본 한 줄의 <b>정체성</b>. 화자와 대사는 여기 없다.
///
/// 작가가 문구를 고치면 <see cref="Revision"/>만 오르고 <see cref="Id"/>는 그대로다.
/// 연출·녹음·번역·QA가 이 Id를 영구 참조 키로 쓰기 때문이다.
/// 줄이 대본에서 사라져도 지우지 않고 <see cref="IsRetired"/>로 남긴다.
/// 지워 버리면 그 줄을 가리키던 연출이 왜 고아가 되었는지 알 수 없게 된다.
/// </summary>
public sealed class ScriptLine
{
    public ScriptLine(string? id = null, int revision = 1, bool isRetired = false)
    {
        Id = id ?? Identifier.Line();
        Revision = revision;
        IsRetired = isRetired;
    }

    public string Id { get; }

    /// <summary>같은 LineId의 문구가 몇 번 바뀌었는지. 1부터 시작한다.</summary>
    public int Revision { get; set; }

    /// <summary>대본에서 사라진 줄. 순서에서 빠지지만 Id는 재사용하지 않는다.</summary>
    public bool IsRetired { get; set; }

    public ScriptLine Clone() => new(Id, Revision, IsRetired);
}

/// <summary>
/// 한 locale의 LineId별 화자·대사 표.
///
/// LineId를 키로 쓰는 덕분에 언어를 추가해도 Dialogue 논리와 Presentation binding이
/// 가리키는 Id는 전혀 바뀌지 않는다.
/// </summary>
public sealed class ScriptLocale
{
    public ScriptLocale(string locale)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);
        Locale = locale;
    }

    public string Locale { get; }

    public Dictionary<string, LocalizedLine> Entries { get; init; } =
        new(StringComparer.Ordinal);

    public LocalizedLine Find(string lineId) =>
        Entries.TryGetValue(lineId, out LocalizedLine? line) ? line : LocalizedLine.Empty;

    public ScriptLocale Clone()
    {
        return new ScriptLocale(Locale)
        {
            Entries = new Dictionary<string, LocalizedLine>(Entries, StringComparer.Ordinal)
        };
    }
}

/// <summary>
/// 작가의 평평한 대본 하나에서 나온 저작 산출물.
///
/// 두 가지를 함께 소유한다.
///   <b>줄 정체성과 순서</b> — <see cref="Lines"/>
///   <b>locale별 화자·대사</b> — <see cref="Locales"/>
///
/// <b>화자와 대사의 유일한 수정 가능한 원본이 여기다.</b> DialogueNode는 LineId만 참조하고
/// 본문을 복사하지 않는다. 같은 문장을 고칠 수 있는 자리가 두 곳이 되면 어느 쪽이 진실인지
/// 아무도 답할 수 없게 된다.
///
/// 작가의 원본 파일은 <b>읽기 전용 가져오기 입력</b>이다. VnTool은 그 파일에 쓰지 않는다.
/// 다시 읽어 들이는 것은 <see cref="ScriptSynchronizer"/>를 지나는 명시적인 동작이다.
/// </summary>
public sealed class ScriptDocument
{
    public const string DefaultLocale = "ko-KR";

    public ScriptDocument(string? id = null, string name = "새 대본")
    {
        Id = id ?? Identifier.Script();
        Name = name;
    }

    public string Id { get; }

    public string Name { get; set; }

    /// <summary>마지막으로 가져온 작가 파일의 경로. 기록용이며 저장 위치를 결정하지 않는다.</summary>
    public string? SourcePath { get; set; }

    /// <summary>동기화를 적용할 때마다 1씩 오른다. 아직 한 번도 읽지 않았으면 0이다.</summary>
    public int SourceRevision { get; set; }

    /// <summary>마지막으로 반영한 원본 텍스트의 해시. 같은 파일을 다시 읽었는지 알 수 있다.</summary>
    public string? SourceContentHash { get; set; }

    public string PrimaryLocale { get; set; } = DefaultLocale;

    /// <summary>현재 순서. 은퇴한 줄도 목록에 남지만 <see cref="ActiveLines"/>에서는 빠진다.</summary>
    public List<ScriptLine> Lines { get; init; } = new();

    public List<ScriptLocale> Locales { get; init; } = new();

    /// <summary>대본에 살아 있는 줄만, 현재 순서대로.</summary>
    public IEnumerable<ScriptLine> ActiveLines => Lines.Where(line => !line.IsRetired);

    public ScriptLine? FindLine(string? lineId)
    {
        return lineId is null
            ? null
            : Lines.FirstOrDefault(line => string.Equals(line.Id, lineId, StringComparison.Ordinal));
    }

    public ScriptLocale? FindLocale(string? locale)
    {
        return locale is null
            ? null
            : Locales.FirstOrDefault(item => string.Equals(item.Locale, locale, StringComparison.Ordinal));
    }

    /// <summary>없으면 만들어서 돌려준다. 편집 통로에서만 호출한다.</summary>
    public ScriptLocale RequireLocale(string locale)
    {
        if (FindLocale(locale) is { } existing)
        {
            return existing;
        }

        var created = new ScriptLocale(locale);
        Locales.Add(created);
        return created;
    }

    public LocalizedLine Text(string lineId, string? locale = null)
    {
        return FindLocale(locale ?? PrimaryLocale)?.Find(lineId) ?? LocalizedLine.Empty;
    }

    public ScriptDocument Clone()
    {
        return new ScriptDocument(Id, Name)
        {
            SourcePath = SourcePath,
            SourceRevision = SourceRevision,
            SourceContentHash = SourceContentHash,
            PrimaryLocale = PrimaryLocale,
            Lines = Lines.Select(line => line.Clone()).ToList(),
            Locales = Locales.Select(locale => locale.Clone()).ToList()
        };
    }
}
