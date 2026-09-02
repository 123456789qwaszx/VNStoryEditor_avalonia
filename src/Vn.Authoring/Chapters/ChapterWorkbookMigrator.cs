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

        // 내용이 그대로면 판정도 그대로다 (2026-08-24 성능) — 아래 프로브는 워크북을
        // 통째로 파싱하는데, 앱이 다시 읽을 때마다 모든 챕터에 그 질문을 다시 했다.
        // 실측으로 워크북 작업의 절반이 이 "필요 없음"이었다(`WorkbookMigrationGate`).
        if (WorkbookMigrationGate.IsKnownCurrent(path))
        {
            return MigrationResult.NotNeeded;
        }

        // 필요 여부는 읽기로만 판정한다 — 최신 파일을 다시 저장하는 낭비를 만들지 않는다.
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var probe = new XLWorkbook(stream);

            if (!NeedsMigration(probe))
            {
                WorkbookMigrationGate.MarkCurrent(path);
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
            FoldEdgeColumnsV12(workbook);               // v12 — 간선의 `종류`·`연출` 폐지
            DropHideWhenLocked(workbook);               // 2026-08-24 — `잠금시 숨김` 폐지
            DropEpisodeKind(workbook);                  // v13 — `종류` 폐지
            // ⚠ 둘 다 맨 뒤다 — 앞 단계들이 열을 밀고 당기므로, 자리가 다 정해진 뒤에
            //   이름으로 찾아 옮긴다.
            MoveEndingKeyToEventKey(workbook);          // v14 — 엔딩키 → 에피소드 `이벤트키`
            // 반드시 엔딩키를 옮긴 뒤다. v11~v13의 H/I열을 자동 열로 먼저 덮으면 키를 잃는다.
            EnsureAutoColumn(workbook);                 // R2 — 명시적 자동 진행, 구판은 FALSE
            MigrateConditions(workbook);
            MigrateStats(workbook);
            // ⚠ 순서 이행은 <b>그 시트를 만지는 모든 단계 뒤</b>다 — 앞이 열을 밀고 당기므로
            //   자리가 다 정해진 뒤에 머리글을 읽어 옮긴다. 규칙은 한 벌이다(v14).
            ReorderColumns(workbook, ChapterSheetNames.Episodes, EpisodeColumnOrder);
            ReorderColumns(workbook, ChapterSheetNames.Stats, StatColumnOrder);
            RemoveEmptyFixtures(workbook);
            ReapplyDropdowns(workbook);

            // 겉모습도 규격이다 (2026-08-18) — 고정·필터·열 너비. 만들 때와 <b>같은
            // 함수</b>를 부른다: 두 곳에 적으면 한쪽만 고쳐지는 날이 온다.
            ChapterWorkbookWriter.ApplyChapterChrome(workbook);

            workbook.SaveAs(path);

            // 방금 쓴 그 내용으로 판정을 기록한다 — 이행 직후 한 번 더 파고들 이유가 없다.
            WorkbookMigrationGate.MarkCurrent(path);
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
               (Find(workbook, ChapterSheetNames.Stats) is { } stats &&
                HeaderColumn(stats, "타입") == 0) ||
               ColumnsNeedReorder(workbook, ChapterSheetNames.Stats, StatColumnOrder) ||
               (fixtures is not null && fixtures.RowsUsed().Count() <= 1) ||
               // v14 (2026-08-26) — 간선은 일곱 칸(끝이 `잠금 안내문`)이고 `엔딩키`가 없어야
               // 하며, 에피소드의 일곱째 칸은 `이벤트키`여야 한다(엔딩키의 후신 — 유니티
               // 전용 패스스루. v12에서 종류·연출이, 2026-08-24에 `잠금시 숨김`이 빠졌다).
               //
               // ⚠ <b>이 조건이 곧 "이행이 끝났다"의 정의다.</b> 열을 걷으면서 여기를 안
               // 고치면 최신 파일도 늘 "이행 필요"가 되어 <b>열 때마다 다시 쓰고 `.bak`이
               // 매번 갈린다</b> — 사람이 되돌릴 자리를 조용히 잃는다(테스트가 그것을 잡는다).
               (Find(workbook, ChapterSheetNames.Edges) is not null &&
                Header(workbook, ChapterSheetNames.Edges, 7) != "잠금 안내문") ||
               (Find(workbook, ChapterSheetNames.Edges) is { } edgeSheet &&
                HeaderColumn(edgeSheet, "자동") == 0) ||
               Header(workbook, ChapterSheetNames.Edges, 8) == "엔딩키" ||
               (Find(workbook, ChapterSheetNames.Episodes) is { } episodes &&
                (HeaderColumn(episodes, "이벤트키") == 0 ||
                 HeaderColumn(episodes, "장면ID") == 0)) ||
               ColumnsNeedReorder(workbook, ChapterSheetNames.Episodes, EpisodeColumnOrder) ||
               // v13 (2026-08-25) — 에피소드의 `종류`가 남아 있으면 아직 이행 전이다.
               Header(workbook, ChapterSheetNames.Episodes, 3) == "종류" ||
               // 겉모습이 안 입혀진 파일 (2026-08-18). 자동 필터 하나로 대표해 본다 —
               // 셋(고정·필터·너비)이 언제나 함께 들어가므로 하나가 없으면 셋 다 없다.
               NeedsChrome(workbook);
    }

    /// <summary>
    /// R2 — 구판의 빈 선택지 문구를 자동 진행으로 추측하지 않는다. 열만 끝에 세우고 모든
    /// 기존 행을 FALSE로 둔다. 의도는 사람이 명시해야 하며 원본은 이행 시작 때 만든 .bak에 남는다.
    /// </summary>
    private static void EnsureAutoColumn(XLWorkbook workbook)
    {
        if (Find(workbook, ChapterSheetNames.Edges) is not { } sheet ||
            HeaderColumn(sheet, "자동") > 0)
        {
            return;
        }

        const int column = 8;
        sheet.Cell(1, column).SetValue("자동");

        foreach (IXLRow row in sheet.RowsUsed().Skip(1))
        {
            row.Cell(column).SetValue(false);
        }
    }

    /// <summary>
    /// 겉모습(고정·필터·너비)이 아직 없는 파일인가.
    ///
    /// <b>이행을 부르는 조건이 곧 이행이 멱등이라는 약속</b>이라, 여기서 보는 것과
    /// <see cref="ChapterWorkbookWriter.ApplyChapterChrome"/>이 거는 것이 같아야 한다 —
    /// 다르면 열 때마다 파일을 다시 쓰고 `.bak`이 매번 갈린다.
    /// </summary>
    private static bool NeedsChrome(XLWorkbook workbook)
    {
        if (Find(workbook, ChapterSheetNames.Episodes) is not { } episodes)
        {
            return false;
        }

        // 머리글의 <b>흰 글자</b>로 대표해 본다. 자동 필터 하나만 보면, 필터는 걸렸는데
        // 글꼴·색은 아직인 중간 상태(서식이 두 단계로 들어온 2026-08-18의 실제 모습)를
        // "다 됐다"로 잘못 읽는다.
        //
        // ⛔ 규격 <b>바깥에</b> 남은 칠도 이행을 부른다 (2026-08-25). 열을 걷는 이행은 이미
        //    끝난 파일 — 머리글 글자는 지웠지만 그 자리의 배경은 그대로인 파일 — 이 실제로
        //    소유자 손에 있었다("에피소드 시트에서 g열이 색이 칠해져 있는거"). 열 이름으로만
        //    이행을 부르면 그 파일은 <b>영영 안 고쳐진다</b>: 걷을 열은 이미 없기 때문이다.
        //
        //    에피소드 규격은 여덟 칸(v15 — `장면ID` 포함)이므로 아홉째가 곧 "바깥"이다.
        //    ⚠ 선택 열 `도달불가 허용`이 아홉째에 살 수 있지만, 그 열은 칠 없이 태어나므로
        //    (UpdateEpisode가 머리글 글자만 적는다) 이 검사와 부딪히지 않는다.
        return !episodes.AutoFilter.IsEnabled ||
               episodes.Cell(1, 1).Style.Font.FontColor != XLColor.White ||
               episodes.Cell(1, 9).Style.Fill.PatternType != XLFillPatternValues.None;
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
    /// v13 (2026-08-25 소유자) — 에피소드의 <c>종류</c> 열을 걷는다.
    ///
    /// <c>Main</c>/<c>Attachment</c>는 코어가 <c>EpisodeKind</c>를 지우면서 뜻을 잃었다.
    /// 내보내기가 싣지 않고, 도달성 증명의 부착 특례도 함께 사라졌으므로 <b>읽어도 아무도
    /// 쓰지 않는 칸</b>이 됐다. 의도한 섬은 <c>도달불가 허용</c>이 적는다.
    ///
    /// ⚠ 값을 옮기지 않는다 — 옮길 곳이 없다. <c>Attachment</c>였던 행은 이제 들어오는
    /// 간선이 없으면 도달 불가로 잡히고, 그것이 맞는 판정이다. 원본은 <c>.bak</c>에 남는다.
    /// </summary>
    private static void DropEpisodeKind(XLWorkbook workbook)
    {
        if (Find(workbook, ChapterSheetNames.Episodes) is not { } sheet ||
            Header(workbook, ChapterSheetNames.Episodes, 3) != "종류")
        {
            return;
        }

        sheet.Column(3).Delete();   // 대사엔트리·X·Y·메모가 한 칸씩 당겨진다
    }

    /// <summary>
    /// 간선 시트의 v11 꼬리 열을 접는다 (v12, 2026-08-24).
    ///
    /// v11은 뒤에 셋(`종류`·`엔딩키`·`연출`)을 달았는데, 소유자 결정으로 `종류`와 `연출`이
    /// 폐지됐다 — 모든 길이 선택지이므로 종류를 물을 것이 없고, 간선에 매다는 연출은
    /// 개념째 접었다. (간선의 `엔딩키`도 v14에서 에피소드 `이벤트키`로 돌아갔다 —
    /// <see cref="MoveEndingKeyToEventKey"/>가 옮긴다.)
    ///
    /// <b>뒤에서부터 지운다</b> — 앞을 먼저 지우면 뒤 열이 밀려 엉뚱한 칸을 삭제한다.
    /// </summary>
    private static void FoldEdgeColumnsV12(XLWorkbook workbook)
    {
        if (Find(workbook, ChapterSheetNames.Edges) is not { } edges)
        {
            return;
        }

        if (string.Equals(edges.Cell(1, 11).GetString().Trim(), "연출", StringComparison.Ordinal))
        {
            edges.Column(11).Delete();
        }

        if (string.Equals(edges.Cell(1, 9).GetString().Trim(), "종류", StringComparison.Ordinal))
        {
            edges.Column(9).Delete();
        }

        // v12 — 문구 없는 길("보이지 않는 기본")이 폐지됐다. 빈 칸을 그대로 두면 열자마자
        // 오류 더미가 되므로 넘어가기 버튼의 이름을 넣어 준다. 기획자가 고치면 된다.
        foreach (IXLRow row in edges.RowsUsed().Skip(1))
        {
            if (row.Cell(1).GetString().Trim().Length > 0 &&
                row.Cell(4).GetString().Trim().Length == 0)
            {
                row.Cell(4).SetValue("계속");
            }
        }
    }

    /// <summary>
    /// v14 (2026-08-26 소유자) — 엔딩키가 <b>간선에서 에피소드의 `이벤트키`(G)</b>로 돌아간다.
    ///
    /// 간선의 `엔딩키`(v11~v13)는 시나리오 층으로 나가는 통로(`EndingRules`)가 이 툴에 없어
    /// 아무 데도 실리지 않는 칸이었다("어차피 EndingKey는 내보낼 방법이 없었어"). 같은 날
    /// 소유자가 개념을 다시 정의했다: <b>유니티 전용 패스스루 인덱스</b> — "특정 에피소드를
    /// 다 시청했을 때 유니티에서 자체적으로 이벤트라던지 보상을 발생시키기 위해서 이어주는
    /// 인덱스", 이름은 "엔딩키보다도 이벤트키가 맞겠다". 에피소드에 살면 "같은 도착의 키
    /// 충돌"이라는 오류 부류가 구조적으로 없다(v11의 충돌 거부·이행 손실이 전부 소멸).
    ///
    /// 옮기는 방향은 v11의 역이다: 간선의 엔딩키 → <b>도착</b> 에피소드의 이벤트키.
    /// 자리가 아니라 <b>머리글 이름으로</b> 찾는다 — v11 이전 파일은 에피소드 시트에,
    /// v11~v13 파일은 간선 시트에 이 열이 있고, 앞 단계들이 열을 밀고 당긴 뒤라 번호를
    /// 믿을 수 없다. ⚠ 같은 도착으로 서로 다른 키가 들어오던 파일은 <b>먼저 적힌 것이
    /// 남는다</b>(막지 않는다 — 원본은 `.bak`에 있고, 이제 값의 뜻이 전이가 아니라 장식이다).
    /// </summary>
    private static void MoveEndingKeyToEventKey(XLWorkbook workbook)
    {
        if (Find(workbook, ChapterSheetNames.Episodes) is not { } episodes)
        {
            return;
        }

        var keyByEpisode = new Dictionary<string, string>(StringComparer.Ordinal);

        // ① 에피소드 시트의 옛 `엔딩키`(v11 이전) — 거두고 열을 걷는다. 뒤에서부터.
        for (int column = 15; column >= 1; column--)
        {
            if (!string.Equals(episodes.Cell(1, column).GetString().Trim(), "엔딩키", StringComparison.Ordinal))
            {
                continue;
            }

            Collect(episodes, idColumn: 1, keyColumn: column, keyByEpisode);
            episodes.Column(column).Delete();
        }

        // ② 간선 시트의 `엔딩키`(v11~v13) — 도착 에피소드의 것으로 거두고 열을 걷는다.
        if (Find(workbook, ChapterSheetNames.Edges) is { } edges)
        {
            for (int column = 15; column >= 1; column--)
            {
                if (!string.Equals(edges.Cell(1, column).GetString().Trim(), "엔딩키", StringComparison.Ordinal))
                {
                    continue;
                }

                Collect(edges, idColumn: 2, keyColumn: column, keyByEpisode);
                edges.Column(column).Delete();
            }
        }

        // ③ `이벤트키` 열이 있기만 하면 된다 — <b>자리는 뒤의 순서 이행이 정한다</b>
        //    (<see cref="ReorderEpisodeColumns"/>). 여기서 자리까지 박으면 같은 규칙이
        //    두 곳에 살고, 그 둘이 갈리는 날이 온다.
        int keyColumn = HeaderColumn(episodes, "이벤트키");

        if (keyColumn == 0)
        {
            keyColumn = 1;

            while (episodes.Cell(1, keyColumn).GetString().Trim().Length > 0)
            {
                keyColumn++;
            }

            episodes.Cell(1, keyColumn).SetValue("이벤트키");
        }

        // ④ 거둔 키를 되쓴다 — 이미 적힌 칸은 사람의 것이라 덮지 않는다.
        foreach (IXLRow row in episodes.RowsUsed().Skip(1))
        {
            string id = row.Cell(1).GetString().Trim();

            if (id.Length > 0 &&
                keyByEpisode.TryGetValue(id, out string? key) &&
                row.Cell(keyColumn).GetString().Trim().Length == 0)
            {
                row.Cell(keyColumn).SetValue(key);
            }
        }

        static void Collect(
            IXLWorksheet sheet, int idColumn, int keyColumn, Dictionary<string, string> into)
        {
            foreach (IXLRow row in sheet.RowsUsed().Skip(1))
            {
                string id = row.Cell(idColumn).GetString().Trim();
                string key = row.Cell(keyColumn).GetString().Trim();

                if (id.Length > 0 && key.Length > 0 && !into.ContainsKey(id))
                {
                    into[id] = key;
                }
            }
        }
    }

    /// <summary>
    /// `에피소드` 시트의 v15 열 순서.
    ///
    /// 신원·내용이 앞(<c>EpisodeId · 대사엔트리 · 제목</c>), 남에게 건네는 열쇠가 가운데
    /// (<c>장면ID · 이벤트키</c>), 판 좌표와 곁말이 뒤(<c>X · Y · 메모</c>). 종류가 같은 값끼리
    /// 붙어 있어야 눈이 한 번에 짚는다.
    ///
    /// ⚠ <b>이 배열 하나가 정본이다</b> — 리더의 <c>EpisodeHeaders</c>·라이터의 새 워크북
    /// 머리글과 같은 순서여야 하고, 갈리면 이행이 매번 다시 돈다.
    /// </summary>
    private static readonly string[] EpisodeColumnOrder =
        ["EpisodeId", "대사엔트리", "제목", "장면ID", "이벤트키", "X", "Y", "메모"];

    /// <summary>
    /// `스탯` 시트의 v14 열 순서 (2026-08-26 소유자: "스탯시트에서도 타입이 가장 앞쪽으로").
    ///
    /// <b>타입이 맨 앞이다</b> — 그 값이 <b>나머지를 어떻게 읽을지</b>를 정하기 때문이다
    /// (bool이면 최소·최대를 읽지 않고 0·1로 굳힌다). 대본 시트의 <c>유형</c>이 첫 칸인 것과
    /// 같은 이유다: 첫 칸만 훑으면 이 표가 무슨 표인지 보인다.
    ///
    /// ⚠ <see cref="EpisodeColumnOrder"/>와 같은 규율 — 이 배열이 리더의 <c>StatHeaders</c>·
    /// 라이터의 새 워크북 머리글과 같은 순서여야 한다.
    /// </summary>
    private static readonly string[] StatColumnOrder =
        ["타입", "스탯키", "표시명", "초기값", "최소", "최대"];

    /// <summary>
    /// v14 (2026-08-26) — 시트 하나를 <paramref name="order"/> 자리로 옮긴다.
    ///
    /// <b>낱말은 그대로이고 자리만 바뀐 순열</b>이라, 짝마다 맞바꾸기를 두면 규격이 움직일
    /// 때마다 경우가 배로 는다. 대본 v14와 같은 방식으로 푼다 — <b>머리글을 읽어 어느 칸이
    /// 무엇인지 알아낸 뒤</b> 새 자리로 옮긴다. 시트마다 사본을 두지 않는 이유도 같다:
    /// 에피소드와 스탯이 같은 날 같은 종류로 움직였고, 규칙이 한 벌이어야 안 갈린다.
    ///
    /// ⚠ <b>통째로 읽고 나서 쓴다</b> — 순열이라 제자리가 아닌 열은 <b>반드시 남의 자리를
    /// 뺏는다</b>. 값은 <c>XLCellValue</c>로 옮기므로 X·Y·초기값의 숫자 셀이 글자로 굳지
    /// 않는다(서식은 뒤따르는 <c>ApplyChapterChrome</c>이 자리 기준으로 다시 입힌다).
    ///
    /// ⚠ 규격 <b>바깥</b>의 열(선택 열 `도달불가 허용`, 사람이 적어 둔 곁말)은 <b>규격 칸
    /// 뒤로, 원래 순서 그대로</b> 따라온다 — 리더가 그 열을 이름으로 찾으므로 자리는 자유다.
    ///
    /// ⚠ <b>빠진 낱말은 빈 열로 세운다</b>(구판에는 `메모`도 `타입`도 없는 시트가 실재한다).
    /// 자리를 안 채우고 두면 <b>남은 칸이 제자리로 못 가고</b>, 리더는 규격 자리대로 읽으므로
    /// 제목을 대사엔트리로 읽는 반쪽 상태가 된다 — 이행이 고치라고 있는 바로 그 모양이다.
    /// </summary>
    private static void ReorderColumns(XLWorkbook workbook, string sheetName, string[] order)
    {
        if (!ColumnsNeedReorder(workbook, sheetName, order) ||
            Find(workbook, sheetName) is not { } sheet)
        {
            return;
        }

        string[] headers = HeaderRowOf(sheet);

        // 새 자리 → 옛 자리. 규격 칸이 먼저, 나머지는 원래 순서대로 뒤에.
        var sources = new List<int>();

        foreach (string name in order)
        {
            sources.Add(Array.IndexOf(headers, name));
        }

        for (int column = 1; column < headers.Length; column++)
        {
            if (!order.Contains(headers[column], StringComparer.Ordinal))
            {
                sources.Add(column);
            }
        }

        int lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
        var buffer = new XLCellValue[lastRow + 1, sources.Count];

        for (int row = 1; row <= lastRow; row++)
        {
            for (int slot = 0; slot < sources.Count; slot++)
            {
                // 없던 낱말(source 0)은 빈 칸으로 태어난다 — 머리글만 아래에서 얹는다.
                buffer[row, slot] = sources[slot] > 0
                    ? sheet.Cell(row, sources[slot]).Value
                    : Blank.Value;
            }
        }

        for (int row = 1; row <= lastRow; row++)
        {
            for (int slot = 0; slot < sources.Count; slot++)
            {
                sheet.Cell(row, slot + 1).Value = buffer[row, slot];
            }
        }

        for (int slot = 0; slot < order.Length; slot++)
        {
            sheet.Cell(1, slot + 1).SetValue(order[slot]);
        }
    }

    /// <summary>
    /// 그 시트의 규격 열이 <paramref name="order"/> 자리에 하나씩 있는가.
    ///
    /// <b>이 판정이 곧 "순서 이행이 끝났다"의 정의다</b> — 이행이 하는 일과 어긋나면
    /// 최신 파일도 늘 "이행 필요"가 되어 열 때마다 다시 쓰고 `.bak`이 매번 갈린다.
    /// 그래서 <see cref="NeedsMigration"/>과 <see cref="ReorderColumns"/>가
    /// <b>같은 함수 하나</b>를 부른다.
    /// </summary>
    private static bool ColumnsNeedReorder(XLWorkbook workbook, string sheetName, string[] order)
    {
        if (Find(workbook, sheetName) is not { } sheet)
        {
            return false;
        }

        string[] headers = HeaderRowOf(sheet);

        // 낱말이 제 자리에 하나씩 있어야 끝난 것이다. 빠진 낱말(−1)도 "옮길 것 있음"에
        // 든다 — 이행이 빈 열로 세워 준다. ⚠ 판정과 이행이 <b>같은 범위·같은 규칙</b>을
        // 봐야 한다: 한쪽만 찾은 낱말이 생기면 그 자리에서 이행이 터진다(실제로 그랬다).
        for (int slot = 0; slot < order.Length; slot++)
        {
            if (Array.IndexOf(headers, order[slot]) != slot + 1)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 머리글 행 — <b>자리 그대로</b>(0번은 비운다). <b>빈 칸 하나가 표의 끝</b>이라
    /// 그 뒤의 곁말은 담지 않는다(리더의 규칙과 같다).
    ///
    /// 판정(<see cref="ColumnsNeedReorder"/>)과 이행(<see cref="ReorderColumns"/>)이
    /// <b>같은 범위</b>를 보게 하는 것이 이 함수의 일이다 — 둘이 다른 범위를 보면 한쪽만
    /// 찾은 낱말이 생기고, 그 자리에서 이행이 터진다.
    /// </summary>
    private static string[] HeaderRowOf(IXLWorksheet sheet)
    {
        var headers = new List<string> { string.Empty };

        for (int column = 1; column <= 15; column++)
        {
            string header = sheet.Cell(1, column).GetString().Trim();

            if (header.Length == 0)
            {
                break;
            }

            headers.Add(header);
        }

        return [.. headers];
    }

    /// <summary>머리글이 그 이름인 열. 없으면 0.</summary>
    private static int HeaderColumn(IXLWorksheet sheet, string header)
    {
        for (int column = 1; column <= 15; column++)
        {
            if (string.Equals(sheet.Cell(1, column).GetString().Trim(), header, StringComparison.Ordinal))
            {
                return column;
            }
        }

        return 0;
    }

    /// <summary>
    /// <c>잠금시 숨김</c>(G) 열을 걷는다 (2026-08-24 소유자: "이미 표시조건과 해금조건이
    /// 있다보니 기능적으로 제거하더라도 아무런 차이가 없어").
    ///
    /// 같은 말을 두 번 하는 칸이었다 — <b>해금조건 + 잠기면 숨김</b>은 그 식을
    /// <b>표시조건</b>에 적은 것과 결과가 같다. 개념이 둘이면 작가가 어느 쪽으로 적었는지에
    /// 따라 같은 이야기가 다르게 보인다.
    ///
    /// ⚠ <b>켜져 있던 값은 옮기지 않는다.</b> 옮기려면 그 해금조건을 표시조건 칸에 복사해야
    /// 하는데, 표시조건에 이미 <b>다른</b> 식이 적혀 있으면 둘 중 하나를 버려야 한다 —
    /// 사람이 쓴 관문을 툴이 임의로 고르는 자리다. 원본은 <c>.bak</c>에 그대로 있고,
    /// 켜져 있던 행은 <b>잠긴 채 보이는</b> 상태가 된다(숨지 않을 뿐 이야기는 그대로다).
    /// </summary>
    private static void DropHideWhenLocked(XLWorkbook workbook)
    {
        if (Find(workbook, ChapterSheetNames.Edges) is not { } sheet ||
            Header(workbook, ChapterSheetNames.Edges, 7) != "잠금시 숨김")
        {
            return;
        }

        sheet.Column(7).Delete();   // 잠금 안내문(H→G) · 엔딩키(I→H)가 한 칸씩 당겨진다
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

    /// <summary>
    /// `타입` 열이 없는 구판 `스탯` 시트에 그 칸을 세운다 (2026-08-16).
    ///
    /// ⚠ <b>있는지 없는지는 이름으로 본다.</b> 예전에는 "여섯째가 타입인가"로 물었는데,
    /// v14에서 타입이 <b>맨 앞</b>으로 가면서 그 물음이 이행 끝난 파일에서도 참이 아니게 됐다
    /// — 다른 이유(겉모습 등)로 이행이 한 번 더 돌면 <b>여섯째인 `최대`의 머리글을
    /// "타입"으로 덮어쓴다</b>. 자리는 뒤의 순서 이행이 정하고, 여기서는 있기만 하게 한다.
    /// </summary>
    private static void MigrateStats(XLWorkbook workbook)
    {
        if (Find(workbook, ChapterSheetNames.Stats) is not { } sheet ||
            HeaderColumn(sheet, "타입") != 0)
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
