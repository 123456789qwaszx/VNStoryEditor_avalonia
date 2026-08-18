using ClosedXML.Excel;

namespace Vn.Authoring.Chapters;

/// <summary>
/// 구판 챕터 워크북을 2026-08-16 규격으로 한 번에 이행한다.
///
/// 바뀐 것: ① 에피소드의 `인덱스` 열 삭제 ② 간선의 `스탯변화`가 C열로, `선택지 라벨`은
/// `선택지`로 ③ 조건의 `조건식` 한 칸이 `스탯`·`연산자`·`값` 세 칸으로(분해 불가한 식은
/// 스탯 칸에 원문 그대로 — 리더의 탈출구와 짝) ④ 스탯에 `타입` 열(선택) ⑤ 데이터 없는
/// 픽스처 시트 제거(임시 제거 — 데이터가 있으면 지우지 않는다).
///
/// <b>이행이 필요 없으면 파일에 손대지 않는다</b> — 챕터를 열 때마다 불려도 쓰기는 구판을
/// 처음 만난 그 한 번뿐이라 폴더 감시가 맴돌지 않는다. 쓰기 전 원본은 <c>.bak</c>으로 남는다.
/// </summary>
public static class ChapterWorkbookMigrator
{
    public sealed record MigrationResult(bool Migrated, string? Failure)
    {
        public static MigrationResult NotNeeded { get; } = new(false, null);
    }

    public static MigrationResult Migrate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // 필요 여부는 읽기로만 판정한다 — 최신 파일을 다시 저장하는 낭비를 만들지 않는다.
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var probe = new XLWorkbook(stream);

            if (!NeedsMigration(probe))
            {
                return MigrationResult.NotNeeded;
            }
        }
        catch (Exception exception)
        {
            return new MigrationResult(false,
                $"'{Path.GetFileName(path)}'를 읽지 못해 규격 이행을 건너뜁니다: {exception.Message}");
        }

        try
        {
            using var memory = new MemoryStream();

            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                stream.CopyTo(memory);
            }

            File.WriteAllBytes(path + ".bak", memory.ToArray());
            memory.Position = 0;

            using var workbook = new XLWorkbook(memory);

            MigrateEpisodes(workbook);
            MigrateEdges(workbook);
            MigrateEdgeLabelsToChoiceSheet(workbook);   // v6·v7 — 선택지 문구 → 선택지 시트
            MigrateGatesToEdges(workbook);              // v8 — 표시·해금조건 → 간선
            MigrateChoiceSheetToDictionary(workbook);   // v9 — 칸 → 전역 문구 사전
            MigrateEndingKeysToEdges(workbook);         // v11 — 엔딩키가 에피소드 → 간선
            MigrateConditions(workbook);
            MigrateStats(workbook);
            RemoveEmptyFixtures(workbook);
            ReapplyDropdowns(workbook);

            workbook.SaveAs(path);
            return new MigrationResult(true, null);
        }
        catch (Exception exception)
        {
            return new MigrationResult(false,
                $"'{Path.GetFileName(path)}' 규격 이행에 실패했습니다" +
                $"(파일이 잠겨 있을 수 있습니다): {exception.Message}");
        }
    }

    private static bool NeedsMigration(XLWorkbook workbook)
    {
        IXLWorksheet? fixtures = Find(workbook, ChapterSheetNames.Fixtures);

        return Header(workbook, ChapterSheetNames.Episodes, 3) == "인덱스" ||
               Header(workbook, ChapterSheetNames.Edges, 3) == "선택지 라벨" ||
               Header(workbook, ChapterSheetNames.Edges, 4) == "선택지수" ||  // v6
               Header(workbook, ChapterSheetNames.Choices, 2) == "도착" ||    // v6 선택지 시트
               Header(workbook, ChapterSheetNames.Choices, 1) == "출발" ||    // v7·v8 칸 → v9 사전
               // v9 — 에피소드가 칸 수를 선언하던 열. v8 이행 전이면 K, 뒤면 I에 있다.
               Header(workbook, ChapterSheetNames.Episodes, 9) == "선택지수" ||
               Header(workbook, ChapterSheetNames.Episodes, 11) == "선택지수" ||
               (Find(workbook, ChapterSheetNames.Edges) is not null &&
                Find(workbook, ChapterSheetNames.Choices) is null) ||
               Header(workbook, ChapterSheetNames.Episodes, 7) == "표시조건" ||  // v8 이전
               Header(workbook, ChapterSheetNames.Edges, 5) == "조건" ||        // v8 이전
               Header(workbook, ChapterSheetNames.Conditions, 2) == "조건식" ||
               (Find(workbook, ChapterSheetNames.Stats) is not null &&
                Header(workbook, ChapterSheetNames.Stats, 6) != "타입") ||
               (fixtures is not null && fixtures.RowsUsed().Count() <= 1) ||
               // v11 — 간선의 종류·엔딩키·연출. 에피소드에 엔딩키가 남아 있어도 이행 대상이다.
               (Find(workbook, ChapterSheetNames.Edges) is not null &&
                Header(workbook, ChapterSheetNames.Edges, 9) != "종류") ||
               Header(workbook, ChapterSheetNames.Episodes, 7) == "엔딩키";
    }

    /// <summary>
    /// v7 (2026-08-16 소유자) — 선택지의 정본은 챕터 `선택지` 시트(출발·인덱스·대본·메모)이고
    /// <b>간선 하나에 칸 하나가 1:1</b>이다: 간선 D열이 짝 칸의 인덱스를 가리킨다.
    ///
    /// 받는 모양 셋: ① v5 — 간선 D열에 문구가 그대로 있다(선택지 시트 없음) ② v6 — 선택지
    /// 시트에 도착 열이 있고 간선 D열은 선택지수다 ③ 에피소드 시트에 `선택지수`(K)가 없다.
    /// 전부 v7로 모은다. 사람이 적어 둔 대본 text는 보존한다.
    /// </summary>
    private static void MigrateEdgeLabelsToChoiceSheet(XLWorkbook workbook)
    {
        // v9 이후로는 칸 자체가 없다 — 이 단계는 <b>칸을 가진 시트(출발 열)를 v7로 모으는</b>
        // 중간 다리로만 남는다. 시트가 아예 없는 v5(간선 D = 문구)는 여기서 손대지 않고
        // <see cref="MigrateChoiceSheetToDictionary"/>가 문구를 바로 사전으로 거둔다 —
        // 칸으로 만들었다 되돌리는 왕복이 없어야 문구가 온전하다.
        if (Find(workbook, ChapterSheetNames.Edges) is not { } edges ||
            Find(workbook, ChapterSheetNames.Episodes) is not { } episodes ||
            Find(workbook, ChapterSheetNames.Choices) is not { } choices ||
            Header(workbook, ChapterSheetNames.Choices, 1) != "출발")
        {
            return;
        }

        // ── v6 선택지 시트(도착 열 있음) → v7: 도착 열을 지운다. 짝은 아래에서 간선 D에
        //    인덱스로 새겨 넣으므로, 지우기 전에 (출발, 도착) → 인덱스들 맵을 뜬다.
        var byPair = new Dictionary<(string, string), Queue<string>>();

        if (Header(workbook, ChapterSheetNames.Choices, 2) == "도착")
        {
            foreach (IXLRow row in choices.RowsUsed().Skip(1))
            {
                string from = row.Cell(1).GetString().Trim();
                string to = row.Cell(2).GetString().Trim();
                string index = row.Cell(3).GetString().Trim();

                if (from.Length == 0 || index.Length == 0)
                {
                    continue;
                }

                if (!byPair.TryGetValue((from, to), out Queue<string>? queue))
                {
                    byPair[(from, to)] = queue = new Queue<string>();
                }

                queue.Enqueue(index);
            }

            choices.Column(2).Delete(); // 도착 폐지 — 도착은 간선이 소유한다
            choices.Cell(1, 2).SetValue("인덱스");
        }

        // ── 간선 → 칸 1:1 배선. D열이 v6 선택지수면 인덱스 참조로 바꾼다.
        const bool edgeLabelColumn = false; // v5 문구는 이 단계로 오지 않는다(위 빗장)
        var nextIndex = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (IXLRow row in choices.RowsUsed().Skip(1))
        {
            string from = row.Cell(1).GetString().Trim();

            if (from.Length > 0 && int.TryParse(row.Cell(2).GetString(), out int existing))
            {
                nextIndex[from] = Math.Max(nextIndex.GetValueOrDefault(from), existing);
            }
        }

        foreach (IXLRow row in edges.RowsUsed().Skip(1))
        {
            string from = row.Cell(1).GetString().Trim();
            string to = row.Cell(2).GetString().Trim();

            if (from.Length == 0 || to.Length == 0)
            {
                continue;
            }

            string cellD = row.Cell(4).GetString().Trim();

            // v7 재이행 방지 — 이미 인덱스를 가리키는 간선(그 칸이 실제로 있다)은 그대로.
            bool alreadyWired = cellD.Length > 0 && choices.RowsUsed().Skip(1).Any(slot =>
                string.Equals(slot.Cell(1).GetString().Trim(), from, StringComparison.Ordinal) &&
                string.Equals(slot.Cell(2).GetString().Trim(), cellD, StringComparison.Ordinal));

            if (alreadyWired && !edgeLabelColumn)
            {
                continue;
            }

            string? index = null;

            if (byPair.TryGetValue((from, to), out Queue<string>? queue) && queue.Count > 0)
            {
                index = queue.Dequeue(); // v6 칸을 물려받는다 — 사람이 적은 대본 그대로
            }
            else
            {
                // 새 칸 — v5 문구가 있으면 대본으로 옮겨 심는다.
                string text = edgeLabelColumn ? cellD : string.Empty;
                int assigned = nextIndex.GetValueOrDefault(from) + 10;
                nextIndex[from] = assigned;

                int slotRow = (choices.LastRowUsed()?.RowNumber() ?? 1) + 1;
                choices.Cell(slotRow, 1).SetValue(from);
                choices.Cell(slotRow, 2).SetValue(assigned);

                if (text.Length > 0)
                {
                    choices.Cell(slotRow, 3).SetValue(text);
                }

                index = assigned.ToString();
            }

            row.Cell(4).SetValue(int.TryParse(index, out int numeric) ? numeric : 0);
        }

        edges.Cell(1, 4).SetValue("선택지");

        // ── 에피소드의 선택지수 — 칸 수가 곧 선언값이다(최소 1). 관문 열이 아직 있으면
        //    (v8 이행 전) K, 이미 빠졌으면 I가 그 자리다.
        int countColumn = Header(workbook, ChapterSheetNames.Episodes, 7) == "표시조건" ? 11 : 9;

        if (Header(workbook, ChapterSheetNames.Episodes, countColumn) != "선택지수")
        {
            if (Header(workbook, ChapterSheetNames.Episodes, countColumn) == "도달불가 허용")
            {
                episodes.Column(countColumn).InsertColumnsBefore(1);
            }

            IXLCell header = episodes.Cell(1, countColumn);
            header.SetValue("선택지수");
            header.Style.Font.SetBold(true);
            header.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#E8EAED"));
        }

        foreach (IXLRow row in episodes.RowsUsed().Skip(1))
        {
            string episodeId = row.Cell(1).GetString().Trim();

            if (episodeId.Length == 0 || row.Cell(countColumn).GetString().Trim().Length > 0)
            {
                continue;
            }

            int slots = choices.RowsUsed().Skip(1).Count(slot =>
                string.Equals(slot.Cell(1).GetString().Trim(), episodeId, StringComparison.Ordinal));
            row.Cell(countColumn).SetValue(Math.Max(1, slots));
        }
    }

    private static void MigrateEpisodes(XLWorkbook workbook)
    {
        if (Find(workbook, ChapterSheetNames.Episodes) is not { } sheet ||
            Header(workbook, ChapterSheetNames.Episodes, 3) != "인덱스")
        {
            return;
        }

        // 인덱스는 안 쓰인다(소유자) — 열째로 지운다. `도달불가 허용`(옛 L)이 K로 따라온다.
        sheet.Column(3).Delete();
    }

    /// <summary>
    /// v11 (2026-08-18) — 엔딩키의 주인이 <b>에피소드에서 간선으로</b> 옮겨 간다.
    ///
    /// 연출이 간선에 붙게 되면서, 엔딩키가 노드에 남으면 <b>엔딩이라는 한 개념이
    /// (노드의 키) + (간선의 연출)로 갈린다.</b> 간선에 모으면 기획자가 한 행에서 다 본다.
    ///
    /// 옮기는 방향: 에피소드의 엔딩키 → 그 에피소드로 <b>들어오는</b> 모든 간선.
    /// "이 길을 타면 저 엔딩"이 되므로 들어오는 쪽이 맞다.
    ///
    /// ⚠ <b>들어오는 간선이 없는 엔딩 에피소드는 키를 잃는다.</b> 그런 에피소드는 이미
    /// 도달 불가라 검증기가 따로 짚고 있어 여기서 막지 않는다(v8에서 부착 에피소드의
    /// 관문이 갈 곳을 잃었을 때와 같은 처리다). `.bak`에 원본이 남는다.
    /// </summary>
    private static void MigrateEndingKeysToEdges(XLWorkbook workbook)
    {
        if (Find(workbook, ChapterSheetNames.Episodes) is not { } episodes ||
            Find(workbook, ChapterSheetNames.Edges) is not { } edges)
        {
            return;
        }

        EnsureEdgeColumnsV11(edges);

        // 에피소드 시트에 엔딩키 열이 남아 있을 때만 옮긴다 — 두 번 돌아도 안전하다.
        if (!string.Equals(episodes.Cell(1, 7).GetString().Trim(), "엔딩키", StringComparison.Ordinal))
        {
            return;
        }

        var keyByEpisode = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (IXLRow row in episodes.RowsUsed().Skip(1))
        {
            string id = row.Cell(1).GetString().Trim();
            string key = row.Cell(7).GetString().Trim();

            if (id.Length > 0 && key.Length > 0)
            {
                keyByEpisode[id] = key;
            }
        }

        foreach (IXLRow row in edges.RowsUsed().Skip(1))
        {
            string to = row.Cell(2).GetString().Trim();

            if (to.Length > 0 &&
                keyByEpisode.TryGetValue(to, out string? key) &&
                row.Cell(10).GetString().Trim().Length == 0)
            {
                row.Cell(10).SetValue(key);
            }
        }

        episodes.Column(7).Delete();
    }

    /// <summary>
    /// v11의 세 열(`종류`·`엔딩키`·`연출`)을 뒤에 붙인다. 이미 있으면 손대지 않는다.
    /// `종류`는 <b>문구를 보고</b> 채운다 — 구판에서는 문구가 곧 그 뜻이었다.
    /// </summary>
    private static void EnsureEdgeColumnsV11(IXLWorksheet edges)
    {
        if (string.Equals(edges.Cell(1, 9).GetString().Trim(), "종류", StringComparison.Ordinal))
        {
            return;
        }

        edges.Cell(1, 9).SetValue("종류");
        edges.Cell(1, 10).SetValue("엔딩키");
        edges.Cell(1, 11).SetValue("연출");

        foreach (IXLRow row in edges.RowsUsed().Skip(1))
        {
            if (row.Cell(1).GetString().Trim().Length == 0)
            {
                continue;
            }

            row.Cell(9).SetValue(
                row.Cell(4).GetString().Trim().Length == 0 ? "자동" : "선택지");
        }
    }

    private static void MigrateEdges(XLWorkbook workbook)
    {
        if (Find(workbook, ChapterSheetNames.Edges) is not { } sheet ||
            Header(workbook, ChapterSheetNames.Edges, 3) != "선택지 라벨")
        {
            return;
        }

        // 옛 모양: 출발|도착|선택지 라벨|조건|잠금시 숨김|잠금 안내문|(스탯변화)
        bool hadStatColumn = string.Equals(
            sheet.Cell(1, 7).GetString().Trim(), "스탯변화", StringComparison.Ordinal);

        sheet.Column(3).InsertColumnsBefore(1); // 새 C — 스탯변화 자리. 나머지가 한 칸 밀린다.
        sheet.Cell(1, 3).SetValue("스탯변화");
        sheet.Cell(1, 4).SetValue("선택지");

        if (hadStatColumn)
        {
            // 옛 스탯변화(밀려서 지금 H=8)를 C로 옮기고 그 열은 지운다.
            foreach (IXLRow row in sheet.RowsUsed().Skip(1))
            {
                string value = row.Cell(8).GetString();

                if (value.Trim().Length > 0)
                {
                    row.Cell(3).SetValue(value);
                }
            }

            sheet.Column(8).Delete();
        }
    }

    /// <summary>
    /// v8 (2026-08-16 소유자) — "보일지 말지는 이제 간선이 정한다". 에피소드의 표시조건·
    /// 해금조건을 <b>그 에피소드로 들어오는 간선들</b>에 옮기고, 에피소드에서는 두 열을 지운다.
    /// 간선 쪽은 `조건`(해금)이 F로 밀리고 E가 표시조건이 된다.
    ///
    /// 간선에 이미 값이 있으면 그것을 남긴다(에피소드 값은 버려진다 — 원본은 .bak에 있다).
    /// 여러 간선이 한 에피소드로 들어오면 <b>모두</b>가 같은 관문을 받는다 — 옛 의미
    /// ("이 에피소드에 들어가려면")를 길 단위로 푼 것이다.
    /// </summary>
    private static void MigrateGatesToEdges(XLWorkbook workbook)
    {
        if (Find(workbook, ChapterSheetNames.Edges) is not { } edges ||
            Find(workbook, ChapterSheetNames.Episodes) is not { } episodes)
        {
            return;
        }

        // ── 간선: `조건`(E) → 해금조건(F), E는 표시조건.
        if (Header(workbook, ChapterSheetNames.Edges, 5) == "조건")
        {
            edges.Column(5).InsertColumnsBefore(1);
            edges.Cell(1, 5).SetValue("표시조건");
            edges.Cell(1, 6).SetValue("해금조건");
        }

        // ── 에피소드의 관문을 걷어 들어오는 간선에 나눠 준다.
        bool episodeGates = Header(workbook, ChapterSheetNames.Episodes, 7) == "표시조건";

        if (!episodeGates)
        {
            return;
        }

        var gates = new Dictionary<string, (string Visible, string Unlock)>(StringComparer.Ordinal);

        foreach (IXLRow row in episodes.RowsUsed().Skip(1))
        {
            string episodeId = row.Cell(1).GetString().Trim();
            string visible = row.Cell(7).GetString().Trim();
            string unlock = row.Cell(8).GetString().Trim();

            if (episodeId.Length > 0 && (visible.Length > 0 || unlock.Length > 0))
            {
                gates[episodeId] = (visible, unlock);
            }
        }

        foreach (IXLRow row in edges.RowsUsed().Skip(1))
        {
            string to = row.Cell(2).GetString().Trim();

            if (to.Length == 0 || !gates.TryGetValue(to, out (string Visible, string Unlock) gate))
            {
                continue;
            }

            if (gate.Visible.Length > 0 && row.Cell(5).GetString().Trim().Length == 0)
            {
                row.Cell(5).SetValue(gate.Visible);
            }

            if (gate.Unlock.Length > 0 && row.Cell(6).GetString().Trim().Length == 0)
            {
                row.Cell(6).SetValue(gate.Unlock);
            }
        }

        // 에피소드에서 두 열을 지운다 — 관문의 주인이 하나여야 한다.
        episodes.Column(7).Delete();
        episodes.Column(7).Delete();
    }

    /// <summary>
    /// v9 (2026-08-17 소유자) — "선택지는 인덱스를 가져오는 게 아니라 그냥 깡으로 대사만
    /// 가져오면 된다. 선택지 시트에서 출발노드는 제거." 칸(에피소드 소유)이 사라지고
    /// <b>챕터 전체가 함께 쓰는 문구 사전</b>이 남는다.
    ///
    /// ① 간선 D열: 짝 칸의 인덱스 → 그 칸의 대본 <b>문구 그 자체</b> (칸이 없거나 빈 대본이면
    /// 빈 칸 = 보이지 않는 기본) ② 선택지 시트: 출발 열 삭제, 문구를 중복 없이 모아 10·20·30
    /// 으로 다시 번호 매김(빈 대본 행은 사전에 오를 낱말이 없으니 걷어낸다) ③ 에피소드의
    /// `선택지수` 열 삭제 — 이제 나가는 간선 수가 곧 선택지 수다.
    /// </summary>
    private static void MigrateChoiceSheetToDictionary(XLWorkbook workbook)
    {
        IXLWorksheet? choices = Find(workbook, ChapterSheetNames.Choices);
        IXLWorksheet? edges = Find(workbook, ChapterSheetNames.Edges);

        if (choices is null)
        {
            // 시트가 아예 없는 워크북(v5 이하) — 간선 D열이 이미 문구다. 사전만 세우고
            // 거기 쓰인 낱말을 거둔다.
            choices = ChapterWorkbookWriter.CreateChoiceSheet(workbook);

            if (edges is not null && Header(workbook, ChapterSheetNames.Edges, 4) == "선택지")
            {
                var harvested = new List<string>();

                foreach (IXLRow row in edges.RowsUsed().Skip(1))
                {
                    string text = row.Cell(4).GetString().Trim();

                    if (text.Length > 0 && !harvested.Contains(text, StringComparer.Ordinal))
                    {
                        harvested.Add(text);
                    }
                }

                for (int slot = 0; slot < harvested.Count; slot++)
                {
                    choices.Cell(slot + 2, 1).SetValue((slot + 1) * 10);
                    choices.Cell(slot + 2, 2).SetValue(harvested[slot]);
                }
            }
        }
        else if (Header(workbook, ChapterSheetNames.Choices, 1) == "출발")
        {
            // ① 간선이 가리키던 (출발, 인덱스)를 문구로 바꿔 심는다.
            var bySlot = new Dictionary<(string, string), string>();

            foreach (IXLRow row in choices.RowsUsed().Skip(1))
            {
                string from = row.Cell(1).GetString().Trim();
                string index = row.Cell(2).GetString().Trim();

                if (from.Length > 0 && index.Length > 0)
                {
                    bySlot[(from, index)] = row.Cell(3).GetString().Trim();
                }
            }

            if (edges is not null)
            {
                foreach (IXLRow row in edges.RowsUsed().Skip(1))
                {
                    string from = row.Cell(1).GetString().Trim();
                    string index = row.Cell(4).GetString().Trim();

                    row.Cell(4).SetValue(
                        index.Length > 0 && bySlot.TryGetValue((from, index), out string? text)
                            ? text
                            : string.Empty);
                }
            }

            // ② 사전 다시 짓기 — 출발을 지우면 인덱스|대본|메모가 남는다.
            choices.Column(1).Delete();
            choices.Cell(1, 1).SetValue("인덱스");
            choices.Cell(1, 2).SetValue("대본");
            choices.Cell(1, 3).SetValue("메모");

            var vocabulary = new List<(string Text, string Memo)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (IXLRow row in choices.RowsUsed().Skip(1))
            {
                string text = row.Cell(2).GetString().Trim();

                if (text.Length > 0 && seen.Add(text))
                {
                    vocabulary.Add((text, row.Cell(3).GetString().Trim()));
                }
            }

            // 지우면서 훑으면 행 번호가 밀린다 — 모아 두었다가 한 번에 비우고 다시 쓴다.
            if ((choices.LastRowUsed()?.RowNumber() ?? 1) > 1)
            {
                choices.Rows(2, choices.LastRowUsed()!.RowNumber()).Clear(XLClearOptions.Contents);
            }

            for (int slot = 0; slot < vocabulary.Count; slot++)
            {
                choices.Cell(slot + 2, 1).SetValue((slot + 1) * 10);
                choices.Cell(slot + 2, 2).SetValue(vocabulary[slot].Text);

                if (vocabulary[slot].Memo.Length > 0)
                {
                    choices.Cell(slot + 2, 3).SetValue(vocabulary[slot].Memo);
                }
            }
        }

        // ③ 에피소드의 선택지수 — 자리는 v8 이행 전후로 갈리니 머리글로 찾아 지운다.
        if (Find(workbook, ChapterSheetNames.Episodes) is { } episodes)
        {
            for (int column = 7; column <= 12; column++)
            {
                if (string.Equals(episodes.Cell(1, column).GetString().Trim(), "선택지수",
                        StringComparison.Ordinal))
                {
                    episodes.Column(column).Delete();
                    break;
                }
            }
        }
    }

    private static void MigrateConditions(XLWorkbook workbook)
    {
        if (Find(workbook, ChapterSheetNames.Conditions) is not { } sheet ||
            Header(workbook, ChapterSheetNames.Conditions, 2) != "조건식")
        {
            return;
        }

        // 옛 모양: 라벨|조건식|설명 → 새 모양: 라벨|스탯|연산자|값|설명
        sheet.Column(3).InsertColumnsBefore(2); // 설명이 C→E로 밀린다.
        sheet.Cell(1, 2).SetValue("스탯");
        sheet.Cell(1, 3).SetValue("연산자");
        sheet.Cell(1, 4).SetValue("값");

        foreach (IXLRow row in sheet.RowsUsed().Skip(1))
        {
            string expression = row.Cell(2).GetString().Trim();

            if (expression.Length == 0)
            {
                continue;
            }

            if (ConditionExpressionParser.TryDecomposeSingle(
                    expression, out string statKey, out string operatorText, out string valueText))
            {
                row.Cell(2).SetValue(statKey);
                row.Cell(3).SetValue(operatorText);

                if (valueText.Length > 0)
                {
                    row.Cell(4).SetValue(valueText);
                }
            }
            // 분해 불가(cleared:·복합식) — 스탯 칸에 원문 그대로 남는다. 리더의 탈출구가 읽는다.
        }
    }

    private static void MigrateStats(XLWorkbook workbook)
    {
        if (Find(workbook, ChapterSheetNames.Stats) is not { } sheet ||
            Header(workbook, ChapterSheetNames.Stats, 6) == "타입")
        {
            return;
        }

        // F가 표에 들어오면서, F 옆(G)에 적어 둔 안내문이 "표 안의 모르는 열"이 된다 —
        // 표 밖의 글은 빈 칸 하나 뒤여야 하므로(리더 규칙) H로 한 칸 민다.
        string note = sheet.Cell(1, 7).GetString();

        if (note.Trim().Length > 0 && sheet.Cell(1, 8).GetString().Trim().Length == 0)
        {
            sheet.Cell(1, 8).SetValue(note);
            sheet.Cell(1, 7).Clear(XLClearOptions.Contents);
        }

        IXLCell cell = sheet.Cell(1, 6);
        cell.SetValue("타입");
        cell.Style.Font.SetBold(true);
        cell.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#E8EAED"));
    }

    /// <summary>데이터가 없는(머리글뿐인) 픽스처 시트만 지운다 — 임시 제거이지 데이터 파기가 아니다.</summary>
    private static void RemoveEmptyFixtures(XLWorkbook workbook)
    {
        if (Find(workbook, ChapterSheetNames.Fixtures) is { } sheet &&
            sheet.RowsUsed().Count() <= 1)
        {
            workbook.Worksheets.Delete(sheet.Name);
        }
    }

    /// <summary>
    /// 검증(드롭다운)은 열 이동을 따라오지 못한다 — 전부 걷어내고 최신 규격으로 다시 깐다.
    /// 정의는 <see cref="ChapterWorkbookWriter.ApplyChapterDropdowns"/> 하나뿐이다(사본 금지).
    /// </summary>
    private static void ReapplyDropdowns(XLWorkbook workbook)
    {
        IXLWorksheet? episodes = Find(workbook, ChapterSheetNames.Episodes);
        IXLWorksheet? edges = Find(workbook, ChapterSheetNames.Edges);
        IXLWorksheet? conditions = Find(workbook, ChapterSheetNames.Conditions);
        IXLWorksheet? stats = Find(workbook, ChapterSheetNames.Stats);
        IXLWorksheet? choices = Find(workbook, ChapterSheetNames.Choices);

        if (episodes is null || edges is null || conditions is null || stats is null)
        {
            return; // 시트가 빠진 깨진 워크북 — 드롭다운보다 리더 진단이 먼저다.
        }

        foreach (IXLWorksheet? sheet in new[] { episodes, edges, conditions, stats, choices })
        {
            sheet?.DataValidations.Delete(_ => true);
        }

        ChapterWorkbookWriter.ApplyChapterDropdowns(episodes, edges, conditions, stats, choices);
    }

    private static IXLWorksheet? Find(XLWorkbook workbook, string name) =>
        workbook.Worksheets.FirstOrDefault(sheet =>
            string.Equals(sheet.Name, name, StringComparison.Ordinal));

    private static string Header(XLWorkbook workbook, string sheetName, int column) =>
        Find(workbook, sheetName)?.Cell(1, column).GetString().Trim() ?? string.Empty;
}
